# 데모 세션 — DAD-v2 브리지 스모크

> **대상 독자**: 이 릴레이를 처음 시험해 보려는 **고등학생 비개발자 관리자**.
> **소요 시간**: 5분 (자동), 30분 (CLI 직접 배선 확장판).

## 🎯 이 예시가 증명하는 것

`D:\codex-claude-relay` (지금 이 릴레이) 는 두 개의 LLM 에이전트(Codex, Claude Code) 를 **동등한 피어** 로 이어주는 다리입니다. 이 예시는 그 다리가 실제로 **동일한 세션 패킷 포맷** 으로 두 에이전트의 턴을 주고받을 수 있다는 것을 파일 몇 개로 증명합니다.

참조 스펙(읽기 전용): `D:\dad-v2-system-template\ko\Document\DAD\PACKET-SCHEMA.md`.

## 📁 포함 파일

```
Document/dialogue/sessions/demo-20260419/
├── turn-1.yaml          ← Codex 턴 (세션 계약 제안)
├── turn-1-handoff.md    ← Codex → Claude Code 전달용 프롬프트
├── turn-2.yaml          ← Claude Code 턴 (체크포인트 검증 + 봉인)
├── state.json           ← 세션 수명 상태 (closed)
└── summary.md           ← 롤링 요약 (carry-forward 포맷)

examples/demo-session-dad-v2/
├── README.md            ← 이 파일
└── run-demo.ps1         ← 검증 스크립트 (관리자가 한 번 실행)
```

## ▶️ 빠른 실행 (관리자용 · 5분)

PowerShell 을 **이 저장소 루트** (`D:\codex-claude-relay`) 에서 열고:

```powershell
powershell -ExecutionPolicy Bypass -File examples/demo-session-dad-v2/run-demo.ps1
```

이 스크립트는 다음을 자동 수행합니다:

1. `dotnet build CodexClaudeRelay.sln` — 릴레이가 빌드되는지
2. `dotnet test --filter PacketIOTests` — 두 피어가 **동일한 YAML 스키마** 로 라운드트립되는지 (cp-1 증거)
3. 데모 세션 5개 아티팩트가 모두 존재하는지 파일 체크 (cp-2 증거)
4. `tools/Validate-Dad-Packet.ps1` 로 `turn-1.yaml`·`turn-2.yaml` 스키마 검증
5. 한국어로 결과 요약 출력 ✅/❌

**성공 기준**: 모든 단계가 ✅ 로 끝나면 릴레이가 현재 main 브랜치에서 **피어 대칭 브리지로 동작 준비 완료** 라는 뜻입니다.

## 🧪 확장판: 실제 CLI 직접 배선 (30분, 선택)

지금 데모는 **사람이 두 CLI 를 직접 띄워서 주고받는 상황의 기계적 아날로그** 입니다. 실제 Codex CLI ↔ Claude Code CLI 를 자식 프로세스로 연결하는 harness 는 백로그 **B8** 에 있고, 아직 구현되지 않았습니다.

관리자가 직접 시험해 보려면 현재 가용한 어댑터 클래스:

- `CodexClaudeRelay.Desktop/Adapters/ClaudeCliAdapter.cs`
- `CodexClaudeRelay.Desktop/Adapters/CodexCliAdapter.cs`
- `CodexClaudeRelay.Desktop/Adapters/NativeProcessLauncher.cs`

이들이 구현하는 `IRelayAdapter` 인터페이스(`CodexClaudeRelay.Core/Adapters/IRelayAdapter.cs`) 는 다음을 요구합니다:

```csharp
public interface IRelayAdapter
{
    string Role { get; }
    Task<AdapterStatus> GetStatusAsync(...);
    Task<RelayAdapterResult> RunTurnAsync(...);
    Task<RelayAdapterResult> RunRepairAsync(...);
}
```

**핵심 설계 원칙**: 이 인터페이스만 만족하면 **어떤 채널이든** peer 역할 수행 가능. 즉 관리자께서 명시하신 대체 가능성이 그대로 반영됨:

- Codex CLI ↔ Claude Code CLI (기본 가정)
- Codex Desktop / Claude Code Desktop 원격 제어 (computer-use 같은 도구)
- Anthropic SDK / OpenAI SDK 직접 호출
- MCP 서버를 통한 원격 도구 제공

## 🔁 DAD-v2 스펙과의 관계

| 항목 | 이 릴레이의 위치 | 템플릿 스펙의 위치 |
|------|----------------|------------------|
| 패킷 스키마 정의 | `Document/DAD/PACKET-SCHEMA.md` | `D:\dad-v2-system-template\ko\Document\DAD\PACKET-SCHEMA.md` |
| 패킷 I/O 구현 | `CodexClaudeRelay.Core/Protocol/PacketIO.cs` | (N/A — 템플릿은 스펙만) |
| 브로커 오케스트레이션 | `CodexClaudeRelay.Core/Broker/RelayBroker.cs` | (N/A) |
| validator | `tools/Validate-Dad-Packet.ps1` | `ko/tools/Validate-DadBacklog.ps1` 등 |
| 세션 scaffolding 툴 | (예정 — B8) | `ko/tools/New-DadSession.ps1` 등 |

템플릿 저장소는 "DAD-v2 가 어떻게 동작해야 하는가" 의 **언어 명세** 이고, 이 릴레이는 그 명세의 **C# 런타임 구현** 입니다. 두 저장소는 런타임에 서로 읽거나 쓰지 않습니다.

## ❓ 자주 묻는 질문

**Q. 이 데모 실행하면 실제로 Codex/Claude Code 가 호출되나요?**
A. **아니오**. 이 데모는 스키마·저장소·테스트만 검증합니다. 실제 CLI 호출은 B8 harness 완성 후 가능.

**Q. 실패 시 어디를 봐야 하나요?**
A. `run-demo.ps1` 가 어떤 단계에서 ❌ 를 냈는지 확인 후:
- `dotnet build` 실패 → .NET 10 SDK 설치 확인
- `PacketIOTests` 실패 → 릴레이 코드에 회귀 발생 (main 최신 머지 후 다시)
- validator 실패 → YAML 파일이 손상되었거나 스키마 변경

**Q. 이 데모 파일을 삭제해도 되나요?**
A. 됩니다. `.autopilot/MVP-GATES.md` 에서 G1·G4 증거로 인용되므로 지우기 전에 MVP 게이트 flip PR 이 열리게 됩니다.
