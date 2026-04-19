# examples/ — 릴레이 실험 예시 인덱스

네 개의 예시가 **점진적으로 깊어지는** 순서로 나열되어 있다. 처음이면
위에서부터 내려오면 된다.

| # | 폴더 | 증명 층위 | 실행 방법 | 소요 |
|---|---|---|---|---|
| 1 | [`demo-session-dad-v2/`](demo-session-dad-v2/) | 수작업 픽스처가 DAD-v2 스키마에 맞는가 | `powershell -File examples/demo-session-dad-v2/run-demo.ps1` | 5분 |
| 2 | [`template-integration/`](template-integration/) | 외부 템플릿 저장소(`D:\dad-v2-system-template`) 스펙과 필드 대칭 | `powershell -File examples/template-integration/run-experiment.ps1` | 1분 |
| 3 | [`live-roundtrip/`](live-roundtrip/) | 릴레이의 `PacketIO` 런타임 바이트 동등 왕복 | `powershell -File examples/live-roundtrip/run-live.ps1` | 15초 |
| 4 | [`deep-integration/`](deep-integration/) | 실제 `RelayBroker` 가 4턴 세션 완주 + 감사 이벤트 29건 | `powershell -File examples/deep-integration/run-deep.ps1` | 20초 |

## 공통 규약

- **템플릿 저장소 쓰기 0건** — `D:\dad-v2-system-template` 는 `PROJECT-RULES.md` 의 read-only 참조 규약을 따름
- **.NET 예시(#3, #4)는 솔루션 밖** 격리 — 루트 `dotnet build CodexClaudeRelay.sln` 은 릴레이 본체에 집중
- **런타임 산출물 미커밋** — `session-out/`, `session-workspace/`, `bin/`, `obj/` 는 각 예시 `.gitignore`

## 어디까지 "진짜" 인가

| 예시 | 릴레이 코드 실행 | 에이전트 |
|---|---|---|
| #1 | 검증기만 | 정적 YAML |
| #2 | 검증기만 | 정적 YAML |
| #3 | `PacketIO` 런타임 | 직렬화 데이터 |
| #4 | `RelayBroker` + 지속화 + 감사 | **스크립트 어댑터** (실 CLI 아님) |

실제 Claude/Codex CLI 연계는 `CodexClaudeRelay.Desktop` 의
`ClaudeCliAdapter` / `CodexCliAdapter` 를 API 키와 함께 구동하는 운영자
워크플로로 남아 있으며, 이 예시 모음의 범위 밖이다.
