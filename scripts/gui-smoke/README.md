# GUI Smoke (UIA driver)

WPF Desktop UI를 사람 클릭/MCP 없이 자동으로 구동하는 스모크 러너.
Windows UI Automation (UIA) PowerShell 스크립트로 Codex / Claude Code 등
에이전트가 직접 호출할 수 있다.

## 언제 쓰나

- Desktop GUI 변경 후 회귀 확인
- 어댑터(Codex CLI / Claude CLI) 라이브 헬스체크
- 2턴짜리 결정적 핸드오프 스모크 (`Smoke Test 2`) 자동 실행
- PR 머지 전 "실제로 GUI가 도는가" 증거 수집 (스크린샷 + 상태 텍스트)

헤드리스 어댑터 헬스만 필요하면 `CodexClaudeRelay.HeadlessSmoke` 콘솔
러너가 더 가볍다 (PR #76). UIA 러너는 *GUI 자체가 동작*하는지까지
확인할 때 쓴다.

## 사전 준비

1. Desktop 빌드:
   ```bash
   dotnet build CodexClaudeRelay.Desktop/CodexClaudeRelay.Desktop.csproj
   ```
   기본 출력: `CodexClaudeRelay.Desktop/bin/Debug/net10.0-windows/CodexClaudeRelay.Desktop.exe`
2. Codex / Claude CLI 가 PATH 에 설치되어 있어야 한다.
3. 작업 디렉토리는 어떤 git 프로젝트든 가능. 별도 더미 프로젝트가 필요하면
   `greet.py`, `PROJECT-RULES.md`, `AGENTS.md`, `README.md` 정도만 두고
   `git init` 하면 충분하다 (어댑터는 `--skip-git-repo-check` 사용).

## 실행

스모크(2턴 전송 검증):

```pwsh
pwsh scripts/gui-smoke/run-gui-smoke.ps1 `
    -WorkingDir 'D:\some-test-project' `
    -TimeoutSeconds 180
```

실작업(커스텀 프롬프트 + Auto Run 4):

```pwsh
pwsh scripts/gui-smoke/run-gui-worksession.ps1 `
    -WorkingDir 'D:\some-test-project' `
    -SessionId 'my-task' `
    -InitialPrompt 'Read PROJECT-RULES.md. Task: ...' `
    -TimeoutSeconds 600
```

기본 `ScreenshotDir` 은 `scripts/gui-smoke/out-*/` (이 디렉토리는 `.gitignore` 됨).

종료 코드:
- `0` — `SmokeTestReportTextBlock` 에 `PASS|success|completed` 토큰 검출
- `1` — 타임아웃 또는 실패 토큰

## 무엇을 자동으로 누르나

스크립트는 7단계로 진행된다:

1. 기존 `CodexClaudeRelay.Desktop` 프로세스 정리 후 재기동
2. `Relay App MVP` 창을 UIA RootElement 에서 검출
3. `WorkingDirectoryTextBox` 에 경로 세팅 + `AutoApproveAllRequestsCheckBox` 토글 ON
4. `CheckAdaptersButton` 클릭 → `AdapterStatusTextBlock` 출력
5. `SmokeTestButton` ("Smoke Test 2") 클릭
6. `SmokeTestReportTextBlock` 폴링 (4초 간격) → 종결 토큰 검출 시 break
7. `StateSummaryTextBlock` + 최종 리포트 콘솔 출력

각 단계에서 `Save-Screen` 으로 스크린샷을 남긴다.

## 의존하는 AutomationId

`MainWindow.xaml` 에 정의된 다음 ID들에 의존한다.

- `WorkingDirectoryTextBox`
- `AutoApproveAllRequestsCheckBox`
- `CheckAdaptersButton`
- `AdapterStatusTextBlock`
- `SmokeTestButton`
- `SmokeTestReportTextBlock`
- `StateSummaryTextBlock`

XAML 측 ID 변경 시 스크립트도 동기화 필요.

## 산출물 위치

- 스크린샷: `-ScreenshotDir` 아래 `yyyyMMdd-HHmmss-<tag>.png` (기본 `scripts/gui-smoke/out-*/`, gitignore 됨)
- 세션 이벤트 JSONL / 핸드오프 / 요약: `%LocalAppData%\CodexClaudeRelayMvp\`
  (`logs/`, `auto-logs/`, `summaries/`, `state-noninteractive.json`)
  — broker 의 `_appDataDirectory` 이며 `WorkingDirectory` 와 무관

## 검증된 결과 예시 (2026-04-22)

```
adapter status: claude-code Healthy / codex Healthy
SmokeTestReport: Result: PASS  Two relay turns completed successfully
StateSummary:    Status: Paused, Current turn: 3, Total cost: $0.0521
                 Codex handle:  019db416-0caa-7f20-85aa-fef5c7eefa80
                 Claude handle: f31f2f04-12fc-41cc-bb60-44c92b80eb0d
                 Last handoff:  claude-code -> codex turn 2
=== GUI SMOKE: SUCCESS ===
```

## 에이전트(Codex / Claude Code) 사용 가이드

이 러너는 보호 경로(`tools/`) 가 아닌 `examples/` 에 있으므로 두 에이전트가
직접 호출/수정 가능하다.

이 러너는 보호 경로(`tools/`) 가 아닌 `scripts/` 에 있으므로 두 에이전트 모두 직접 호출/수정 가능하다.

권장 호출 패턴:

```bash
pwsh scripts/gui-smoke/run-gui-smoke.ps1 -WorkingDir <abs-path>          # 전송 검증
pwsh scripts/gui-smoke/run-gui-worksession.ps1 -WorkingDir <abs-path> `  # 실작업
    -SessionId <id> -InitialPrompt <prompt>
```

PR 작업 흐름에서:
- Desktop XAML / 어댑터 / RelayBroker 를 건드린 PR 은 머지 전에 한 번
  돌려서 PASS 를 첨부한다 (스크린샷 또는 콘솔 로그 인용).
- AutomationId 를 새로 추가하면 이 README 의 "의존하는 AutomationId"
  목록도 갱신한다.
- 실패 시 `scripts/gui-smoke/out-*/*-after-smoke.png` 와
  `%LocalAppData%\CodexClaudeRelayMvp\logs\` 의 최신 JSONL 을 첨부한다.
