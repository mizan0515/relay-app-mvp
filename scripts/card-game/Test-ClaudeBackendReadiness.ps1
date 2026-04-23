param(
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-claude-backend.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-claude-backend.md'
}

function Get-CommandPath {
  param([string]$Name)

  $cmd = Get-Command $Name -ErrorAction SilentlyContinue
  if ($cmd) {
    return $cmd.Source
  }

  return ''
}

function Get-EnvValue {
  param([string]$Name)
  return [Environment]::GetEnvironmentVariable($Name)
}

function Get-StatusValue {
  param(
    [string]$Json,
    [string]$Property
  )

  if ([string]::IsNullOrWhiteSpace($Json)) {
    return ''
  }

  try {
    $parsed = $Json | ConvertFrom-Json
    if ($parsed.PSObject.Properties.Name -contains $Property) {
      return [string]$parsed.$Property
    }
  } catch {
  }

  return ''
}

$claudePath = Get-CommandPath -Name 'claude'
$claudeVersion = ''
$authStatusJson = ''
$authMethod = ''
$loggedIn = ''

if ($claudePath) {
  try {
    $claudeVersion = (& $claudePath --version 2>$null | Select-Object -First 1)
  } catch {
  }

  try {
    $authStatusJson = (& $claudePath auth status --output-format json 2>$null | Out-String).Trim()
    $authMethod = Get-StatusValue -Json $authStatusJson -Property 'authMethod'
    $loggedIn = Get-StatusValue -Json $authStatusJson -Property 'loggedIn'
  } catch {
  }
}

$anthropicApiKey = Get-EnvValue -Name 'ANTHROPIC_API_KEY'
$anthropicAuthToken = Get-EnvValue -Name 'ANTHROPIC_AUTH_TOKEN'
$anthropicBaseUrl = Get-EnvValue -Name 'ANTHROPIC_BASE_URL'
$claudeOauthToken = Get-EnvValue -Name 'CLAUDE_CODE_OAUTH_TOKEN'
$disableAdaptiveThinking = Get-EnvValue -Name 'CLAUDE_CODE_DISABLE_ADAPTIVE_THINKING'
$effortLevel = Get-EnvValue -Name 'CLAUDE_CODE_EFFORT_LEVEL'
$maxThinkingTokens = Get-EnvValue -Name 'MAX_THINKING_TOKENS'
$disable1MContext = Get-EnvValue -Name 'CLAUDE_CODE_DISABLE_1M_CONTEXT'
$disableFastMode = Get-EnvValue -Name 'CLAUDE_CODE_DISABLE_FAST_MODE'
$disableExperimentalBetas = Get-EnvValue -Name 'CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS'

$authPrecedence = if ($anthropicAuthToken) {
  'anthropic_auth_token'
} elseif ($anthropicApiKey) {
  'anthropic_api_key'
} elseif ($claudeOauthToken) {
  'claude_code_oauth_token'
} elseif ($authMethod) {
  $authMethod
} else {
  'unknown'
}

$usingGateway = -not [string]::IsNullOrWhiteSpace($anthropicBaseUrl)
$adaptiveThinkingMode = if ($disableAdaptiveThinking -eq '1') {
  'fixed-budget'
} elseif ($maxThinkingTokens -eq '0') {
  'disabled'
} else {
  'adaptive-default-on-supported-4.6-models'
}

$fastModeExpectation = if ($disableFastMode -eq '1') {
  'disabled'
} elseif ($usingGateway) {
  'uncertain-through-gateway'
} else {
  'available-if-plan-and-admin-policy-allow'
}

$oneMillionContextExpectation = if ($disable1MContext -eq '1') {
  'disabled'
} elseif ($usingGateway) {
  'requires-gateway-header-compatibility-and-account-support'
} else {
  'requires-[1m]-selection-and-account-support'
}

$betaHeaderRisk = if ($usingGateway) {
  'gateway-must-forward-anthropic-beta-and-anthropic-version'
} else {
  'direct-api-path'
}

$report = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  claude_cli = [ordered]@{
    found = [bool]$claudePath
    path = $claudePath
    version = $claudeVersion
  }
  auth = [ordered]@{
    precedence = $authPrecedence
    logged_in = $loggedIn
    auth_method = $authMethod
    has_anthropic_api_key = [bool]$anthropicApiKey
    has_anthropic_auth_token = [bool]$anthropicAuthToken
    has_claude_code_oauth_token = [bool]$claudeOauthToken
    note = 'In Claude CLI non-interactive mode (-p), ANTHROPIC_API_KEY takes precedence when present.'
  }
  routing = [ordered]@{
    anthropic_base_url = $anthropicBaseUrl
    using_gateway = $usingGateway
    beta_header_risk = $betaHeaderRisk
  }
  thinking = [ordered]@{
    mode = $adaptiveThinkingMode
    effort_level = $effortLevel
    max_thinking_tokens = $maxThinkingTokens
    disable_adaptive_thinking = $disableAdaptiveThinking
  }
  fast_mode = [ordered]@{
    expectation = $fastModeExpectation
    disable_fast_mode = $disableFastMode
    note = 'Fast mode is documented for direct Claude Code paths; third-party cloud providers are excluded, and gateways can interfere with tiering.'
  }
  context_1m = [ordered]@{
    expectation = $oneMillionContextExpectation
    disable_1m_context = $disable1MContext
    note = '1M context requires explicit [1m] selection or picker support plus account availability.'
  }
  prompt_caching = [ordered]@{
    note = 'Anthropic prompt caching is an API capability; exact-prefix reuse and cache headers matter most on direct native API paths.'
    disable_experimental_betas = $disableExperimentalBetas
  }
  operator_actions = @(
    'Prefer direct API/auth paths for cache, beta-header, and 1M-context reliability.',
    'If ANTHROPIC_API_KEY is set, expect claude -p to use it instead of subscription OAuth.',
    'When using gateways, validate anthropic-beta and anthropic-version header forwarding before relying on fast mode or long-context features.',
    'For Claude 4.6 cost control, set effort deliberately; do not assume MAX_THINKING_TOKENS alone constrains adaptive reasoning.',
    'Keep the relay prompt prefix stable so any backend-side caching has a chance to help.'
  )
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Claude Backend Readiness')
$lines.Add('')
$lines.Add('Generated at: ' + $report.generated_at)
$lines.Add('Claude CLI found: ' + $report.claude_cli.found)
$lines.Add('Claude CLI version: ' + $report.claude_cli.version)
$lines.Add('Auth precedence: ' + $report.auth.precedence)
$lines.Add('Gateway enabled: ' + $report.routing.using_gateway)
$lines.Add('Adaptive thinking mode: ' + $report.thinking.mode)
$lines.Add('Fast mode expectation: ' + $report.fast_mode.expectation)
$lines.Add('1M context expectation: ' + $report.context_1m.expectation)
$lines.Add('')
$lines.Add('## Actions')
$lines.Add('')
foreach ($action in $report.operator_actions) {
  $lines.Add('- ' + $action)
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote Claude backend JSON: $OutputJsonPath"
Write-Host "Wrote Claude backend Markdown: $OutputMarkdownPath"
