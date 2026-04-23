# 오토파일럿 관리자 도구 (한국어)
# 비개발자도 사용할 수 있도록 만든 단일 메뉴 스크립트.
#
# 사용법: 파워셸에서 .\.autopilot\관리자.ps1 실행 (인자 없으면 메뉴, 인자 주면 바로 실행)
#   .\.autopilot\관리자.ps1            → 메뉴 표시
#   .\.autopilot\관리자.ps1 상태       → 상태만 1회 출력
#   .\.autopilot\관리자.ps1 정지       → HALT 파일 생성 (정지)
#   .\.autopilot\관리자.ps1 재개       → HALT 파일 삭제 (재개)

param([string]$Verb = '메뉴')

$ErrorActionPreference = 'Stop'
# $ap = 이 스크립트가 들어있는 폴더 (.autopilot 혹은 autopilot-template 루트)
# 상대경로 '.autopilot' 가정 대신 자기 위치를 기준으로 삼아 어디서 불러도 동작.
$ap = $PSScriptRoot
if (-not $ap) { $ap = (Get-Location).Path }
chcp 65001 > $null  # UTF-8 출력 (한글 깨짐 방지)
$OutputEncoding = [System.Text.Encoding]::UTF8

function 줄 { Write-Host ('─' * 60) }

# ────────────────────────────────────────────────────────────────────
# 원격 동기화 + 결정 PR 해소 감지
# "선택 PR은 머지됐는데 로컬(특히 dev 브랜치)은 아직 pending으로 보임" 문제를
# 막기 위해, 스크립트는 매 실행 시 origin/<base> 의 OPERATOR-DECISIONS.md 를
# 권위자로 취급한다. 그 파일에서 해당 슬러그가 resolved 상태면:
#   (a) 로컬 작업 디렉터리의 .autopilot/OPERATOR-DECISIONS.md 를 origin 버전으로 동기화
#   (b) HALT 가 남아있으면 자동 삭제 (머지 = 재개 계약)
#   (c) STATE.md 의 decision_* 필드도 정리 (해소된 결정 흔적 지우기)
# 오프라인/원격없음은 오류 없이 조용히 스킵.
# ────────────────────────────────────────────────────────────────────

function 베이스브랜치 {
  # STATE.md 의 base: 필드를 우선, 없으면 main.
  $sp = Join-Path $ap 'STATE.md'
  if (Test-Path $sp) {
    $m = Select-String -Path $sp -Pattern '^base:\s*(\S+)' | Select-Object -First 1
    if ($m -and $m.Matches.Count -gt 0) { return $m.Matches[0].Groups[1].Value.Trim() }
  }
  return 'main'
}

function 원격동기화 {
  # 작업 트리가 더럽혀지지 않도록 fetch 만 수행. pull/checkout 은 하지 않는다.
  try {
    git -C (Split-Path $ap -Parent) fetch origin --quiet 2>$null | Out-Null
  } catch { }
}

function 원격결정파일읽기 {
  param([string]$base)
  try {
    $repoRoot = (git -C (Split-Path $ap -Parent) rev-parse --show-toplevel 2>$null).Trim()
    if (-not $repoRoot) { return $null }
    # .autopilot 가 리포 루트 하위 어디에 있는지 계산.
    $apAbs  = (Resolve-Path $ap).Path
    $rel    = $apAbs.Substring($repoRoot.Length).TrimStart('\','/') -replace '\\','/'
    $gitPath = "$rel/OPERATOR-DECISIONS.md"
    $text = git -C $repoRoot show "origin/${base}:$gitPath" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $text
  } catch { return $null }
}

function 결정상태맵 {
  param([string]$text)
  # 반환: @{ slug = 'pending'|'resolved' } 해시.
  $map = @{}
  if (-not $text) { return $map }
  foreach ($line in ($text -split "`n")) {
    if ($line -match '^##\s*(\S+).*status:\s*(pending|resolved)') {
      $map[$Matches[1]] = $Matches[2]
    }
  }
  return $map
}

function 결정해소반영 {
  # origin 에서 resolved 된 슬러그가 있고, STATE.md 의 decision_slug 가 그 중 하나면:
  # (1) 로컬 .autopilot/OPERATOR-DECISIONS.md 를 origin 버전으로 덮어쓰기
  # (2) HALT 삭제
  # (3) STATE 의 decision_* 필드 정리
  $base = 베이스브랜치
  원격동기화
  $remoteText = 원격결정파일읽기 -base $base
  if (-not $remoteText) { return $null }
  $remoteMap  = 결정상태맵 -text $remoteText

  # 로컬 STATE 에서 decision_slug 추출
  $sp = Join-Path $ap 'STATE.md'
  $decSlug = $null
  if (Test-Path $sp) {
    $m = Select-String -Path $sp -Pattern '^decision_slug:\s*(\S+)' | Select-Object -First 1
    if ($m) {
      $v = $m.Matches[0].Groups[1].Value.Trim()
      if ($v -and $v -ne 'null') { $decSlug = $v }
    }
  }

  $resolvedNow = $null
  if ($decSlug -and $remoteMap.ContainsKey($decSlug) -and $remoteMap[$decSlug] -eq 'resolved') {
    $resolvedNow = $decSlug
  }

  # 로컬 파일이 origin 과 다르면(즉, 머지 내용을 아직 못 받은 상태면) 동기화
  $decPath = Join-Path $ap 'OPERATOR-DECISIONS.md'
  if (Test-Path $decPath) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $localText = [System.IO.File]::ReadAllText($decPath, $utf8NoBom)
    if ($localText -ne $remoteText) {
      [System.IO.File]::WriteAllText($decPath, $remoteText, $utf8NoBom)
      Write-Host "🔄 OPERATOR-DECISIONS.md 를 origin/$base 기준으로 동기화했어요." -ForegroundColor Cyan
    }
  }

  if ($resolvedNow) {
    $haltPath = Join-Path $ap 'HALT'
    if (Test-Path $haltPath) {
      Remove-Item $haltPath -Force
      Write-Host "✅ 결정 '$resolvedNow' 이 이미 머지되어 HALT 를 자동 해제했어요." -ForegroundColor Green
    }
    # STATE 의 decision_* 흔적 정리 (루프가 다음 부팅에서 한번 더 정리하더라도 여기서 먼저 단정)
    if (Test-Path $sp) {
      $lines = Get-Content -Encoding UTF8 $sp
      $newLines = $lines | Where-Object {
        $_ -notmatch '^decision_slug:' -and
        $_ -notmatch '^decision_pr:' -and
        $_ -notmatch '^decision_branch:' -and
        $_ -notmatch '^decision_note:'
      }
      # status: awaiting-decision 도 떼기
      $newLines = $newLines | ForEach-Object {
        if ($_ -match '^status:\s*awaiting-decision') { 'status: ready-after-decision' } else { $_ }
      }
      $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
      [System.IO.File]::WriteAllText($sp, ($newLines -join "`n") + "`n", $utf8NoBom)
    }
    return $resolvedNow
  }
  return $null
}

function 상태읽기 {
  줄
  Write-Host '🤖 오토파일럿 상태 점검' -ForegroundColor Cyan
  줄

  # 0. 원격 머지 감지 — 이미 해소된 결정이면 HALT 도 여기서 자동 해제.
  try { [void](결정해소반영) } catch { }

  # 1. HALT 파일 (정지 상태인지)
  if (Test-Path (Join-Path $ap 'HALT')) {
    Write-Host '⛔ 상태: 정지됨 (HALT 파일이 있음)' -ForegroundColor Yellow
    Write-Host '   재개하려면: .\.autopilot\관리자.ps1 재개'
    줄
    return
  }

  # 2. STATE.md status 줄 읽기
  $statePath = Join-Path $ap 'STATE.md'
  if (Test-Path $statePath) {
    $statusLine = Select-String -Path $statePath -Pattern '^status:' | Select-Object -First 1
    $iterLine   = Select-String -Path $statePath -Pattern '^iteration:' | Select-Object -First 1
    if ($statusLine) { Write-Host ('📋 ' + $statusLine.Line) }
    if ($iterLine)   { Write-Host ('🔢 ' + $iterLine.Line) }
  } else {
    Write-Host '⚠️  STATE.md 없음 — 아직 한 번도 안 돌았거나 설치가 잘못됨' -ForegroundColor Yellow
  }

  # 3. 재예약(다음 깨어남) 점검
  $nd = Join-Path $ap 'NEXT_DELAY'
  $lr = Join-Path $ap 'LAST_RESCHEDULE'
  if (-not (Test-Path $nd)) {
    Write-Host 'ℹ️  아직 첫 반복이 끝나지 않았어요. 조금 기다려 주세요.'
    줄; return
  }
  if (-not (Test-Path $lr)) {
    Write-Host '🚨 멈춤 의심: 다음 깨어남 기록(LAST_RESCHEDULE)이 없어요.' -ForegroundColor Red
    Write-Host '   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md'
    줄; return
  }
  $lines = @(Get-Content -Encoding UTF8 $lr)
  $line1 = if ($lines.Count -ge 1) { $lines[0].Trim() } else { '' }
  $line2 = if ($lines.Count -ge 2) { $lines[1].Trim() } else { '' }

  if ($line1 -like 'halted*' -or $line1 -like 'external-runner:*') {
    Write-Host "✅ 정상 (자동 깨어남 면제 상태: $line1)" -ForegroundColor Green
    줄; return
  }
  if ([string]::IsNullOrWhiteSpace($line2) -or $line2 -eq $line1) {
    Write-Host '🚨 멈춤 의심: 깨어남 증거가 부실해요 (1줄 기록 = 가짜 가능성).' -ForegroundColor Red
    Write-Host '   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md'
    줄; return
  }

  $delay = [int]((Get-Content -Encoding UTF8 $nd -Raw).Trim())
  try { $ts = [DateTimeOffset]::Parse($line1) }
  catch {
    Write-Host "⚠️  타임스탬프 해석 실패: $line1" -ForegroundColor Yellow
    줄; return
  }
  $age = [int]((Get-Date) - $ts.UtcDateTime).TotalSeconds
  $slack = 600
  $분지난 = [math]::Round($age / 60, 1)
  $예정분 = [math]::Round($delay / 60, 1)

  if ($age -gt ($delay + $slack)) {
    Write-Host "🚨 멈춤 확인: $분지난 분 전에 깨어났어야 했는데 안 깨어났어요." -ForegroundColor Red
    Write-Host "   (예정 간격 $예정분 분 + 여유 10분 초과)"
    Write-Host '   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md'
  } else {
    $남은분 = [math]::Round(($delay + $slack - $age) / 60, 1)
    Write-Host "✅ 정상 동작 중. 마지막 깨어남: $분지난 분 전, 다음 점검까지 약 $남은분 분 여유" -ForegroundColor Green
  }

  # 4. 최근 히스토리 3줄
  $hist = Join-Path $ap 'HISTORY.md'
  if (Test-Path $hist) {
    줄
    Write-Host '📜 최근 작업 (HISTORY.md 끝 부분):' -ForegroundColor Cyan
    Get-Content -Encoding UTF8 $hist -Tail 12 | ForEach-Object { Write-Host "   $_" }
  }
  줄
}

function 정지하기 {
  New-Item -ItemType File -Path (Join-Path $ap 'HALT') -Force | Out-Null
  Write-Host '⛔ HALT 파일을 만들었어요. 다음 반복에서 루프가 정지합니다.' -ForegroundColor Yellow
}

function 재개하기 {
  # 원격 머지 감지: 결정이 이미 머지됐으면 HALT 도 자동 해제됨.
  $resolved = $null
  try { $resolved = 결정해소반영 } catch { }
  if ($resolved) {
    Write-Host "✅ 머지된 결정 '$resolved' 을(를) 자동 반영했어요." -ForegroundColor Green
    Write-Host '   이제 클로드 코드 채팅에 /loop 를 다시 입력하면 이어서 돕니다:'
    Write-Host '   /loop .autopilot/PROMPT.md' -ForegroundColor Cyan
    return
  }

  $h = Join-Path $ap 'HALT'
  if (Test-Path $h) {
    $body = try { (Get-Content -Encoding UTF8 -Raw $h).Trim() } catch { '' }
    if ($body -match 'pending-decision|awaiting|decision|operator-direction|post-mvp') {
      Write-Host '🙋 결정 PR 이 아직 머지되지 않았어요.' -ForegroundColor Yellow
      Write-Host '   GitHub 에서 "🙋 결정 필요" PR 을 열어 머지하면 자동 재개됩니다.'
      Write-Host '   (이 스크립트를 다시 실행하면 머지를 감지해서 HALT 를 자동 해제합니다.)'
      return
    }
    Remove-Item $h
    Write-Host '✅ 비상정지 HALT 를 해제했어요. 이제 클로드 코드에서 /loop 를 다시 시작하세요:'
    Write-Host '   /loop .autopilot/PROMPT.md' -ForegroundColor Cyan
  } else {
    Write-Host 'ℹ️  HALT 파일이 원래 없었어요. 멈춰있다면 클로드 코드에서 /loop 를 다시 입력하세요.'
  }
}

function 시작안내 {
  줄
  Write-Host '🚀 오토파일럿 시작' -ForegroundColor Cyan
  줄

  # start 를 한 번 누르면 원격 머지 감지 → HALT 자동 해제 → 안내 한 흐름으로.
  $resolved = $null
  try { $resolved = 결정해소반영 } catch { }
  if ($resolved) {
    Write-Host "✅ 머지된 결정 '$resolved' 을(를) 자동 반영했어요. HALT 도 해제됐습니다." -ForegroundColor Green
  } else {
    $h = Join-Path $ap 'HALT'
    if (Test-Path $h) {
      $body = try { (Get-Content -Encoding UTF8 -Raw $h).Trim() } catch { '' }
      if ($body -match 'pending-decision|awaiting|decision|operator-direction|post-mvp') {
        Write-Host '🙋 결정 PR 이 아직 머지되지 않았어요. GitHub 에서 "🙋 결정 필요" PR 을 머지해 주세요.' -ForegroundColor Yellow
        Write-Host '   (머지 후 이 명령을 다시 실행하면 자동 재개됩니다.)'
        줄; return
      }
    }
  }

  Write-Host '클로드 코드(터미널 앱)를 열고 다음을 입력하세요:'
  Write-Host ''
  Write-Host '   /loop .autopilot/PROMPT.md' -ForegroundColor Green
  Write-Host ''
  Write-Host '그게 끝이에요. 이후로는 알아서 작업하고, 작업이 끝나면 자동으로'
  Write-Host '다시 시작합니다. PR 만들기·머지·브랜치 정리도 모두 자동입니다.'
  Write-Host ''
  Write-Host '정지: 이 스크립트로 [2] 정지  또는  .\.autopilot\관리자.ps1 정지'
  줄
}

function 메뉴 {
  while ($true) {
    상태읽기
    Write-Host ''
    Write-Host '무엇을 할까요?'
    Write-Host '  [1] 시작 방법 보기'
    Write-Host '  [2] 정지 (HALT 만들기)'
    Write-Host '  [3] 재개 (HALT 지우기)'
    Write-Host '  [4] 상태 새로고침'
    Write-Host '  [5] 웹 대시보드 열기 (HTML)'
    Write-Host '  [0] 종료'
    $sel = Read-Host '번호 입력'
    switch ($sel) {
      '1' { 시작안내; Read-Host '엔터 누르면 메뉴로 돌아갑니다' | Out-Null }
      '2' { 정지하기; Read-Host '엔터' | Out-Null }
      '3' { 재개하기; Read-Host '엔터' | Out-Null }
      '4' { continue }
      '5' { 대시보드 }
      '0' { return }
      default { Write-Host '0~4 중에서 골라주세요.'; Start-Sleep -Seconds 1 }
    }
  }
}

# ────────────────────────────────────────────────────────────────────
# 대시보드: JSON을 생성 → HTML 템플릿에 주입 → 브라우저 열기
# 비개발자 관리자가 한 장의 HTML만 보면 되도록 설계.
# ────────────────────────────────────────────────────────────────────

function 대시보드데이터수집 {
  # 매 대시보드 렌더마다 원격 머지 감지 → 해소 자동 반영.
  # (오프라인/원격없음은 조용히 스킵하므로 안전.)
  try { [void](결정해소반영) } catch { }
  $now = Get-Date
  $data = [ordered]@{
    updated_at       = $now.ToUniversalTime().ToString('o')
    updated_at_local = $now.ToString('yyyy-MM-dd HH:mm:ss')
    status           = '(STATE.md 없음)'
    mode             = $null
    iteration        = $null
    iteration_hint   = ''
    hero_class       = 'ok'
    action_title     = '✅ 없습니다. 지금은 기다리면 됩니다.'
    action_body_html = '오토파일럿이 알아서 다음 작업을 시작합니다. 아무것도 안 하셔도 됩니다.'
    health_class     = 'ok'
    health_title     = '✅ 정상 작동 중입니다'
    health_detail    = '지금 이 순간 아무 문제도 감지되지 않았어요.'
    wake_summary     = '—'
    wake_hint        = ''
    progress_pct     = 0
    big_goals        = @()
    needs_approval   = @()
    do_not_worry     = @(
      'iteration / iter 숫자 — 그냥 "몇 번 일했나"의 표시예요.',
      'LOCK / NEXT_DELAY / LAST_RESCHEDULE 같은 내부 파일 — 오토파일럿이 알아서 관리합니다.',
      'PR / 브랜치 / 머지 충돌 없는 상황 — 모두 자동 처리됩니다.',
      '영어 커밋 메시지 — 코드 관리용이라 읽지 않으셔도 됩니다.',
      'METRICS.jsonl / EVOLUTION.md 같은 로그 — 개발자용 기록입니다.'
    )
    history_lines    = @()
  }

  $halted = Test-Path (Join-Path $ap 'HALT')

  # ── decision_pr/decision_slug 미리 읽기 (Hero 카드에서 사용) ──
  $decisionPR   = $null
  $decisionSlug = $null
  $spEarly = Join-Path $ap 'STATE.md'
  if (Test-Path $spEarly) {
    foreach ($line in (Get-Content -Encoding UTF8 $spEarly)) {
      if ($line -match '^decision_pr:\s*(\S+)')   { $decisionPR   = $Matches[1].Trim() }
      if ($line -match '^decision_slug:\s*(\S+)') { $decisionSlug = $Matches[1].Trim() }
    }
    if ($decisionPR -eq 'null')   { $decisionPR   = $null }
    if ($decisionSlug -eq 'null') { $decisionSlug = $null }
  }

  # ── HALT 우선 판정 ────────────────────────────────────
  if ($halted) {
    # HALT 내용을 확인해 "결정 PR 대기" 상태인지(레거시), 아니면 진짜 비상정지인지 구분.
    $haltBody = try { (Get-Content -Encoding UTF8 -Raw (Join-Path $ap 'HALT')).Trim() } catch { '' }
    $data.status       = '정지됨 (HALT)'
    $data.hero_class   = 'halted'
    $data.health_class = 'halted'
    $data.health_title = '⛔ 지금은 멈춰 있습니다'
    if ($haltBody -match 'pending-decision|awaiting|decision' -or $haltBody -match 'operator-direction|post-mvp') {
      # 레거시 흐름: 구 버전 루프가 HALT 를 판단용으로 썼던 경우 — 결정 PR 로 안내.
      $data.action_title     = '🙋 결정 PR 머지만 해주시면 재개됩니다'
      $data.action_body_html = '루프가 판단을 기다리고 있어요. GitHub 에서 <b>"🙋 결정 필요"</b> 제목의 PR 을 찾아 원하는 옵션을 고른 뒤 <b>머지</b> 만 누르세요. 파일을 직접 수정할 필요는 없습니다.'
      $data.health_detail    = '결정 PR 머지로 자동 재개됩니다.'
    } else {
      # 진짜 비상정지 — 대시보드 버튼으로 재개 (파일 삭제를 스크립트가 대신함).
      $data.action_title     = '⛔ 비상정지 상태입니다'
      $data.action_body_html = '재개하려면 PowerShell에서 <code>.\.autopilot\관리자.ps1 재개</code> 를 한 번 실행해 주세요. 그 외에는 어떤 파일도 직접 만질 필요가 없습니다.'
      $data.health_detail    = '정지 버튼이 눌려있어요. 재개 명령 한 줄이면 됩니다.'
    }
  }

  # ── awaiting-decision 상태 (HALT 아님) ───────────────
  if (-not $halted -and $decisionPR) {
    $data.hero_class       = 'review'
    $data.action_title     = '🙋 결정 PR 머지만 해주시면 됩니다'
    $data.action_body_html = "루프가 판단을 기다리고 있어요. <a href='$decisionPR' target='_blank' style='color:#fbbf24'>$decisionPR</a> 을(를) 열어 옵션을 고른 뒤 <b>머지</b> 만 누르세요."
  }

  # ── STATE.md 파싱 ────────────────────────────────────
  $sp = Join-Path $ap 'STATE.md'
  $operatorReviewRequired = $false
  $operatorLines = @()
  $openQuestions = @()
  if (Test-Path $sp) {
    $stateContent = Get-Content -Encoding UTF8 $sp
    foreach ($line in $stateContent) {
      if ($line -match '^status:\s*(.+)$' -and -not $halted) {
        $data.status = $Matches[1].Trim()
      }
      if ($line -match '^iteration:\s*(\d+)') {
        $data.iteration = [int]$Matches[1]
        $data.iteration_hint = "현재까지 $($Matches[1])번 일했어요"
        $data.progress_pct = [math]::Round(([int]$Matches[1] % 20) / 20 * 100)
      }
      if ($line -match '^OPERATOR:\s*(.+)$') {
        $op = $Matches[1].Trim()
        $operatorLines += $op
        if ($op -match 'require human review') { $operatorReviewRequired = $true }
      }
      if ($line -match '^\s*-\s*(.+)$' -and $line -notmatch '^OPERATOR:') {
        # very loose — matching list items that might be open questions; filter later
      }
    }
    # open_questions 섹션 (YAML 리스트 형태면 거칠게 뽑기)
    $inQ = $false
    foreach ($line in $stateContent) {
      if ($line -match '^open_questions:') { $inQ = $true; continue }
      if ($inQ) {
        if ($line -match '^\s*-\s*(.+)$') { $openQuestions += $Matches[1].Trim() }
        elseif ($line -match '^\S') { $inQ = $false }
      }
    }
  }

  # ── 재예약(wake) 판정 ────────────────────────────────
  $wakeBad = $false
  if (-not $halted) {
    $nd = Join-Path $ap 'NEXT_DELAY'
    $lr = Join-Path $ap 'LAST_RESCHEDULE'
    if (-not (Test-Path $nd)) {
      $data.wake_summary = '⏳ 아직 첫 반복 진행 중'
      $data.wake_hint    = '조금만 기다려 주세요.'
    } elseif (-not (Test-Path $lr)) {
      $wakeBad = $true
      $data.hero_class       = 'stuck'
      $data.action_title     = '🚨 멈췄습니다. 다시 켜주세요.'
      $data.action_body_html = '클로드 코드 채팅에 <code>/loop .autopilot/PROMPT.md</code> 를 다시 입력해 주세요.'
      $data.wake_summary     = '증거 파일 없음'
      $data.wake_hint        = 'LAST_RESCHEDULE 파일이 없습니다.'
    } else {
      $lines = @(Get-Content -Encoding UTF8 $lr)
      $line1 = if ($lines.Count -ge 1) { $lines[0].Trim() } else { '' }
      $line2 = if ($lines.Count -ge 2) { $lines[1].Trim() } else { '' }
      if ($line1 -like 'halted*' -or $line1 -like 'external-runner:*') {
        $data.wake_summary = '면제 상태'; $data.wake_hint = $line1
      } elseif ([string]::IsNullOrWhiteSpace($line2) -or $line2 -eq $line1) {
        $wakeBad = $true
        $data.hero_class       = 'stuck'
        $data.action_title     = '🚨 증거가 부실합니다 (위조 의심).'
        $data.action_body_html = '깨어남 증거 파일의 두 번째 줄이 비어있어요. 클로드 코드 채팅에 <code>/loop .autopilot/PROMPT.md</code> 를 다시 입력해 주세요.'
        $data.wake_summary     = '1줄 기록 (위조 가능)'
        $data.wake_hint        = '정상 기록은 2줄이어야 합니다.'
      } else {
        try {
          $ts = [DateTimeOffset]::Parse($line1)
          $delayRaw = (Get-Content -Encoding UTF8 $nd -Raw).Trim()
          $delay = if ($delayRaw -match '^\d+$') { [int]$delayRaw } else { 900 }
          $age = [int]((Get-Date) - $ts.UtcDateTime).TotalSeconds
          $slack = 600
          $minAgo = [math]::Round($age/60, 1)
          $expectedMin = [math]::Round($delay/60, 1)
          if ($age -gt ($delay + $slack)) {
            $wakeBad = $true
            $data.hero_class       = 'stuck'
            $data.action_title     = "🚨 $minAgo 분 전에 깨어났어야 하는데 멈춰있어요."
            $data.action_body_html = '클로드 코드 채팅에 <code>/loop .autopilot/PROMPT.md</code> 를 다시 입력해 주세요.'
            $data.wake_summary     = "$minAgo 분 전 (지연 중)"
            $data.wake_hint        = "예정 간격 $expectedMin 분 + 여유 10분을 초과했어요."
          } else {
            $remaining = [math]::Round(($delay + $slack - $age) / 60, 1)
            $data.hero_class       = 'ok'
            $data.action_title     = '✅ 없습니다. 지금은 기다리면 됩니다.'
            $data.action_body_html = "오토파일럿이 약 <b>$remaining 분</b> 안에 다음 작업을 시작합니다. 커밋·PR·머지까지 알아서 합니다."
            $data.wake_summary     = "$minAgo 분 전 (정상)"
            $data.wake_hint        = "다음 점검까지 약 $remaining 분 여유"
          }
        } catch {
          $data.wake_summary = '(타임스탬프 해석 실패)'
          $data.wake_hint    = $line1
        }
      }
    }
  }

  # ── BACKLOG P1 → 큰 목표 목록 ────────────────────────
  $bp = Join-Path $ap 'BACKLOG.md'
  $mvp = Join-Path $ap 'MVP-GATES.md'
  $goals = @()
  if (Test-Path $mvp) {
    # MVP 게이트가 있으면 우선 — 체크 안 된 것만
    foreach ($line in Get-Content -Encoding UTF8 $mvp) {
      if ($line -match '^\s*-\s*\[\s\]\s*(.+)$') {
        $goals += ($Matches[1].Trim() -replace '\s*\(.+?\)\s*$','').Substring(0, [math]::Min(80, ($Matches[1].Trim()).Length))
        if ($goals.Count -ge 5) { break }
      }
    }
  }
  if ($goals.Count -eq 0 -and (Test-Path $bp)) {
    foreach ($line in Get-Content -Encoding UTF8 $bp) {
      if ($line -match '^\s*[-*]\s*\[P1\]\s*(.+)$') {
        $t = $Matches[1].Trim()
        if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '…' }
        $goals += $t
        if ($goals.Count -ge 5) { break }
      }
    }
  }
  $data.big_goals = [string[]]$goals

  # ── 관리자 확인 필요 목록 ────────────────────────────
  # 새 규칙: 관리자 승인은 전부 "🙋 결정 필요" 한국어 PR로만 수렴됩니다.
  # 여기서는 (1) OPERATOR-DECISIONS.md 의 pending 블록, (2) STATE 의 decision_pr 필드,
  # (3) gh 가 있다면 operator-decision 라벨이 붙은 열린 PR을 모읍니다.
  $approvals = @()
  $decFile = Join-Path $ap 'OPERATOR-DECISIONS.md'
  $pendingDecisions = @()
  if (Test-Path $decFile) {
    $curSlug = $null; $curStatus = $null; $curQ = $null
    foreach ($line in (Get-Content -Encoding UTF8 $decFile)) {
      if ($line -match '^##\s*(\S+).*status:\s*(pending|resolved)') {
        if ($curSlug -and $curStatus -eq 'pending') {
          $pendingDecisions += [ordered]@{ slug = $curSlug; q = $curQ }
        }
        $curSlug = $Matches[1]; $curStatus = $Matches[2]; $curQ = $null
      } elseif ($line -match '^\*\*질문:\*\*\s*(.+)$' -and -not $curQ) {
        $curQ = $Matches[1].Trim()
      }
    }
    if ($curSlug -and $curStatus -eq 'pending') {
      $pendingDecisions += [ordered]@{ slug = $curSlug; q = $curQ }
    }
  }

  foreach ($d in $pendingDecisions) {
    $q = if ($d.q) { $d.q } else { '(질문 미기재)' }
    $label = "🙋 결정 PR 머지 대기: $q"
    if ($decisionPR -and $d.slug -eq $decisionSlug) {
      $label += "  →  $decisionPR"
    }
    $approvals += $label
  }

  if ($operatorReviewRequired) {
    # 레거시 — 새로운 프로젝트에서는 결정 PR 로 대체되었지만 읽기는 유지.
    $approvals += '(레거시) STATE.md 의 "require human review" 플래그가 감지됨. 다음 결정 PR에 인계됩니다.'
  }
  foreach ($q in $openQuestions) {
    if ($q) { $approvals += "열린 질문: $q (자동으로 다음 결정 PR에 반영됩니다)" }
  }
  $data.needs_approval = [string[]]$approvals

  # ── 건강 신호 5종 산출 (HALT 우선 이미 세팅됨) ──────
  if (-not $halted) {
    if ($wakeBad) {
      $data.health_class  = 'restart'
      $data.health_title  = '🔁 재시작만 하면 됩니다'
      $data.health_detail = '자동 깨어남이 끊겼어요. 클로드 코드에서 /loop를 한 번만 다시 입력해 주세요.'
    } elseif ($approvals.Count -gt 0) {
      $data.health_class  = 'review'
      $data.health_title  = '🙋 관리자 확인이 한 번 필요합니다'
      $data.health_detail = "총 $($approvals.Count)건. 아래 '관리자 확인이 필요한 일' 섹션을 봐주세요."
    } elseif ($data.status -match 'env-broken|blocked|error|fail') {
      $data.health_class  = 'review'
      $data.health_title  = '🙋 관리자 확인이 한 번 필요합니다'
      $data.health_detail = "상태가 '$($data.status)' 입니다. 개발자에게 알려주세요."
    } elseif ($data.status -match 'idle|upkeep|waiting' -or ($data.iteration -ne $null -and $data.progress_pct -eq 0 -and $data.iteration -gt 0)) {
      $data.health_class  = 'waiting'
      $data.health_title  = '💤 정상 대기 중입니다'
      $data.health_detail = '할 일이 잠시 비어서 다음 반복까지 대기하고 있어요. 문제 없습니다.'
    } else {
      $data.health_class  = 'ok'
      $data.health_title  = '✅ 정상 작동 중입니다'
      $data.health_detail = '지금 이 순간 아무 문제도 감지되지 않았어요.'
    }
  }

  # ── HISTORY 마지막 12줄 ──────────────────────────────
  $hist = Join-Path $ap 'HISTORY.md'
  if (Test-Path $hist) {
    $data.history_lines = [string[]](@(Get-Content -Encoding UTF8 $hist -Tail 12) | ForEach-Object { [string]$_ })
  }

  return $data
}

function 대시보드 {
  $tpl = Join-Path $ap 'OPERATOR-TEMPLATE.ko.html'
  if (-not (Test-Path $tpl)) {
    Write-Host "⚠️  대시보드 템플릿이 없어요: $tpl" -ForegroundColor Yellow
    Write-Host '   템플릿 저장소(autopilot-template)에서 OPERATOR-TEMPLATE.ko.html 을 복사해 주세요.'
    return
  }
  $data = 대시보드데이터수집
  $json = $data | ConvertTo-Json -Depth 5 -Compress
  # </script> 방지 (JSON 안에 포함될 경우 브라우저 파싱 깨짐)
  $json = $json -replace '</', '<\/'

  # 단순 문자열 치환 (Regex 아님 — JSON 안의 특수문자가 regex로 해석되지 않게)
  # 주의: Windows PowerShell 5의 Get-Content 기본 인코딩은 시스템 ANSI(한국어 Windows = CP949).
  # UTF-8 HTML을 CP949로 읽으면 한글이 모지바케되어 브라우저가 </title> 경계를 못 찾습니다.
  # [IO.File]::ReadAllText를 UTF-8로 명시해서 PS5/PS7 모두 안전하게 동작시킵니다.
  $template = [System.IO.File]::ReadAllText($tpl, [System.Text.UTF8Encoding]::new($false))
  $html = $template.Replace('{{JSON_DATA}}', $json)

  $jsonOut = Join-Path $ap 'OPERATOR-LIVE.ko.json'
  $htmlOut = Join-Path $ap 'OPERATOR-LIVE.ko.html'
  # Out-File -Encoding UTF8은 PS5에서 BOM을 붙입니다. WriteAllText로 UTF-8 no-BOM으로 저장.
  $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
  $jsonFull = $data | ConvertTo-Json -Depth 5
  [System.IO.File]::WriteAllText($jsonOut, $jsonFull, $utf8NoBom)
  [System.IO.File]::WriteAllText($htmlOut, $html, $utf8NoBom)

  Write-Host "✅ 대시보드 갱신 완료: $htmlOut" -ForegroundColor Green
  Start-Process $htmlOut
}

switch ($Verb) {
  '메뉴'      { 메뉴 }
  '상태'      { 상태읽기 }
  'status'    { 상태읽기 }
  '정지'      { 정지하기 }
  'stop'      { 정지하기 }
  '재개'      { 재개하기 }
  'resume'    { 재개하기 }
  '시작'      { 시작안내 }
  'start'     { 시작안내 }
  '대시보드'  { 대시보드 }
  'dashboard' { 대시보드 }
  default     { Write-Host "모르는 명령: $Verb"; Write-Host '사용 가능: 메뉴 / 상태 / 정지 / 재개 / 시작 / 대시보드' }
}
