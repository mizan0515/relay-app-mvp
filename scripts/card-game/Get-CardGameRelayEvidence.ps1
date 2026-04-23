param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$SessionId = '',
  [string]$EventLogPath = '',
  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'
$autoLogsDir = Join-Path $appDataDir 'auto-logs'
$liveSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'
$currentStatusPath = Join-Path $autoLogsDir 'current-status.txt'

function Read-JsonFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  try {
    return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Get-EventLogPathFromStatus {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return ''
  }

  foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
    if ($line -match '^[A-Z]:\\.+\.jsonl$') {
      return $line.Trim()
    }
  }

  return ''
}

$liveSignal = Read-JsonFile -Path $liveSignalPath
if (-not $SessionId -and $liveSignal -and $liveSignal.session_id) {
  $SessionId = [string]$liveSignal.session_id
}

if (-not $EventLogPath -and $SessionId) {
  $candidate = Join-Path $appDataDir "logs\$SessionId.jsonl"
  if (Test-Path -LiteralPath $candidate) {
    $EventLogPath = $candidate
  }
}

if (-not $EventLogPath -and $liveSignal -and $liveSignal.event_log_path) {
  $EventLogPath = [string]$liveSignal.event_log_path
}

if (-not $EventLogPath) {
  $EventLogPath = Get-EventLogPathFromStatus -Path $currentStatusPath
}

$resolvedSessionId = $SessionId
$toolEventsObserved = 0
$mcpCallsObserved = 0
$unityMcpCallsObserved = 0
$unityMcpObserved = $false
$unityMcpMentionedInPromptOrLog = $false
$status = 'missing'

if ($EventLogPath -and (Test-Path -LiteralPath $EventLogPath)) {
  $status = 'ok'
  foreach ($line in Get-Content -LiteralPath $EventLogPath -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    $entry = $null
    try {
      $entry = $line | ConvertFrom-Json
    } catch {
      continue
    }

    if (-not $resolvedSessionId -and $entry.PSObject.Properties.Name -contains 'Payload' -and $entry.Payload) {
      if ([string]$entry.Payload -match 'session_id[:=]"?([^"\s,}]+)') {
        $resolvedSessionId = $Matches[1]
      }
    }

    $rawLine = [string]$line
    if ($rawLine -match 'Unity MCP|mcp__unityMCP__|MCP-FOR-UNITY') {
      $unityMcpMentionedInPromptOrLog = $true
    }

    $eventType = [string]$entry.EventType
    if ($eventType -notin @('tool.invoked', 'tool.completed')) {
      continue
    }

    $payloadText = [string]$entry.Payload
    if ($payloadText -match '"type":"command_execution"' -or $rawLine -match 'aggregated_output') {
      continue
    }

    $toolEventsObserved++
    if ($payloadText -match 'mcp__') {
      $mcpCallsObserved++
    }

    if ($payloadText -match 'mcp__unityMCP__\.[A-Za-z_]+' -or [string]$entry.Message -match 'mcp__unityMCP__\.[A-Za-z_]+') {
      $unityMcpCallsObserved++
      $unityMcpObserved = $true
    }
  }
} elseif ($EventLogPath) {
  $status = 'event_log_missing'
}

$summary = [ordered]@{
  status = $status
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  session_id = $resolvedSessionId
  relay_signal_status = if ($liveSignal) { [string]$liveSignal.status } else { '' }
  event_log_path = $EventLogPath
  tool_events_observed = $toolEventsObserved
  mcp_calls_observed = $mcpCallsObserved
  unity_mcp_calls_observed = $unityMcpCallsObserved
  unity_mcp_observed = $unityMcpObserved
  unity_mcp_mentioned_in_prompt_or_log = $unityMcpMentionedInPromptOrLog
  compact_signal_only = $true
  summary_marker = if ($unityMcpObserved) {
    "[RELAY_EVIDENCE] unity_mcp=observed count=$unityMcpCallsObserved"
  } elseif ($mcpCallsObserved -gt 0) {
    "[RELAY_EVIDENCE] unity_mcp=not-observed mcp_calls=$mcpCallsObserved"
  } else {
    "[RELAY_EVIDENCE] unity_mcp=not-observed count=0"
  }
}

$json = $summary | ConvertTo-Json -Depth 5
if ($OutputPath) {
  $outputDir = Split-Path -Parent $OutputPath
  if ($outputDir) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
  }
  Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

Write-Output $json
