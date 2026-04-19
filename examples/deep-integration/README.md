# 딥 인티그레이션 — 릴레이가 **실제로** DAD 세션을 완주하는가

> **대상 독자**: 이 릴레이가 **진짜 프로젝트** 에서 Claude↔Codex
> 상호작용을 DAD 프로토콜로 자동화해 일을 완성할 수 있는지 확인하려는
> 관리자.
> **소요 시간**: 20초.

## 🎯 이 예시가 증명하는 것

이전 세 예시(`demo-session-dad-v2`, `template-integration`,
`live-roundtrip`)는 **릴레이의 개별 부품**(패킷 포맷, PacketIO 왕복,
스펙 필드 대칭) 을 각각 확인했다. 이 예시는 그 부품들이 모여 **실제
브로커**가 다음을 일관되게 수행하는지를 한 번에 보인다:

1. **턴 라우팅**: Codex → Claude Code → Codex → Claude Code 4턴이
   `active_agent` 상태를 자동 토글하며 진행
2. **패킷 지속화**: 매 턴 `turn-{N}.yaml` + `turn-{N}-handoff.md`
   세트를 `Document/dialogue/sessions/<id>/` 에 기록
3. **체크포인트 검증**: `cp-test-scaffold`, `cp-impl-pass` 가
   `peer_review.checkpoint_results` 에 반영되며 JSONL 감사 로그에
   `checkpoint.verified` 이벤트로 남음
4. **세션 봉인**: 4턴째 `final_no_handoff` 로 `suggest_done: true` +
   `done_reason` 을 방출하고 브로커가 `session.paused` 이벤트로 닫음
5. **감사 로그**: 29개 이벤트(`turn.started` / `turn.completed` /
   `handoff.accepted` / `packet_written` / `state_written` / ...) 가
   SHA-256 `event_hash` 로 체인되어 `<session-id>.jsonl` 에 append

## 🧪 시나리오: "greeting 기능 추가" (4턴)

스크립트 속 미니 프로젝트:

| 턴 | 에이전트 | 산출 |
|---|---|---|
| 1 | Codex | 계획 + 파일 구조(`greeting.py` / `test_greeting.py`) |
| 2 | Claude Code | 테스트 스캐폴드 작성 (cp-test-scaffold PASS) |
| 3 | Codex | `greet()` 구현 (cp-impl-pass PASS, pytest 2 passed) |
| 4 | Claude Code | 최종 검증 + `final_no_handoff` 봉인 |

> ⚠️ **주의**: 에이전트 응답은 **스크립트** 다. 실제 Claude/Codex CLI 를
> 불러오는 게 아니라 `ScriptedAdapter` 가 미리 정해진 `HandoffEnvelope`
> 를 방출한다. **릴레이(브로커·지속화·감사)는 진짜로 실행** 되며,
> 이것이 이 실험의 정직한 범위다. 실제 CLI 를 통한 라이브 상호작용은
> `CodexClaudeRelay.Desktop` 의 `ClaudeCliAdapter` / `CodexCliAdapter`
> 를 API 키와 함께 구동할 때 가능.

## 📁 구성

```
examples/deep-integration/
├── README.md                           ← 이 파일
├── run-deep.ps1                        ← 3단계 보고 드라이버
├── .gitignore                          ← session-workspace / bin / obj 제외
└── DeepIntegration/
    ├── DeepIntegration.csproj          ← Core 참조, 솔루션 밖
    └── Program.cs                      ← ~170줄, 브로커 4턴 구동 + 리포트
```

실행 시 `session-workspace/Document/dialogue/sessions/deep-<ts>/` 에:

- `turn-1.yaml` · `turn-1-handoff.md`
- `turn-2.yaml` · `turn-2-handoff.md`
- `turn-3.yaml` · `turn-3-handoff.md`
- `state.json` (`current_turn: 4`)

그리고 `…/logs/deep-<ts>.jsonl` 에 29개 이벤트가 쌓인다. gitignored.

> 턴 4(`final_no_handoff`)는 패킷 파일을 추가로 쓰지 않는다 — 브로커가
> `session.paused` 이벤트로 봉인하는 것이 설계. 이는 예시 실행 시
> **관찰되는 실제 동작** 이며 에러가 아니다.

## ▶️ 실행

```powershell
powershell -ExecutionPolicy Bypass -File examples/deep-integration/run-deep.ps1
```

3단계 보고:

1. **빌드 + 4턴 세션 구동** — 브로커·지속화·감사 이벤트 실행
2. **릴레이 자체 검증기** (`tools/Validate-Dad-Packet.ps1`) — 3개
   `turn-*.yaml` 전수 통과
3. **템플릿 스펙 필드 대칭** (`D:\dad-v2-system-template` 읽기
   전용) — 6개 최상위 필드 `[OK]`

옵션:
- `-WorkDir <경로>` · `-TemplateRoot <경로>` · `-TemplateVariant ko`

## 🔒 템플릿 저장소 쓰기 0건

이 드라이버도 `$TemplateRoot` 에서 **읽기만** 한다. `PROJECT-RULES.md`
의 read-only 규약 준수.

## 🧭 네 예시의 위치

| 예시 | 증명하는 층위 |
|---|---|
| `demo-session-dad-v2` | 수작업 픽스처가 스키마에 맞는가 |
| `template-integration` | 외부 스펙과의 필드 대칭 |
| `live-roundtrip` | PacketIO 바이트 동등 왕복 |
| **`deep-integration` (이 예시)** | **브로커 전체 기계가 4턴 세션을 완주** |

실 CLI 연계 시나리오는 `CodexClaudeRelay.Desktop` 의 어댑터를 직접
구동하는 운영자 워크플로로 남겨 둠(이 예시 범위 밖).
