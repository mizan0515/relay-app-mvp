# B17 — MCP 지원 설계 3안 (관리자 결정 대기)

> **목적**: 미션 명확화 블록(PROMPT.md `mission-clarification`) 이 요구한
> "MCP 가능 경로 유지" 를 구체적 설계로 착륙시킨다. 관리자는 세 안 중
> **한 글자(a / b / c)** 로 답하시면 된다. 본 문서는 결정 전 설계 문서이며
> 실제 구현은 별도 PR.

---

## 🎯 3안 요약 (관리자 1분 컷)

| 안 | 한 줄 | 복잡도 | 이 릴레이가 MCP 에 **하는 일** |
|----|------|--------|------------------------------|
| **a** | Per-peer pass-through | **낮음** | 아무것도 안 함. 각 CLI 가 스스로 MCP 서버와 연결. |
| **b** | Broker-registered shared registry | 중간 | 두 피어가 **같은 MCP 서버 목록** 을 쓰도록 릴레이가 전달. |
| **c** | Relay-proxied centralized | **높음** | 릴레이가 MCP 서버를 **대신 호출** 하고 결과를 두 피어에 중계. |

관리자가 한 안만 답해 주시면 해당 방향으로 구현 PR 을 엽니다.

---

## 📚 배경 — MCP 가 이 릴레이에서 왜 중요한가

**MCP (Model Context Protocol)** 는 LLM 에이전트에 **도구(툴)** 와 **외부 리소스** 를 제공하는 표준 프로토콜입니다. 예를 들어:

- "파일시스템 읽기" MCP 서버 → LLM 이 로컬 파일 접근 가능
- "Slack 발송" MCP 서버 → LLM 이 메시지 발송 가능
- "사내 DB 조회" MCP 서버 → LLM 이 DB 쿼리 가능

이 릴레이의 두 피어(Codex, Claude Code) 는 이미 각자의 CLI 안에서 툴 사용이 가능합니다. 문제는 **"같은 세션 안에서 두 피어가 동일한 도구를 동일한 방식으로 쓰게 할 것인가, 아니면 각자 알아서 쓰게 할 것인가?"** 이고, 이 질문에 세 가지 답이 있습니다.

---

## 안 A — Per-peer pass-through (각자 알아서)

### 한 줄 정의
릴레이는 MCP 에 대해 **아무것도 모른다**. 각 CLI(Codex / Claude Code) 가 자체 설정 파일로 MCP 서버를 붙인다.

### 그림
```
     ┌─────────────┐                      ┌─────────────┐
     │  Codex CLI  │                      │ Claude Code │
     │             │                      │     CLI     │
     │  ┌──────┐   │                      │   ┌──────┐  │
     │  │ MCP  │←  │  ← CLI 자체 설정 →   │ → │ MCP  │  │
     │  │ cfg  │   │                      │   │ cfg  │  │
     │  └──────┘   │                      │   └──────┘  │
     └──────┬──────┘                      └──────┬──────┘
            │                                    │
            ▼                                    ▼
      RelayBroker  ←── turn-{N}.yaml 만 왕복 ──→
         (MCP 에 대해 아무것도 모름)
```

### 장점
- **구현 PR 규모: 0~1 파일**. 문서에 "각 CLI 의 MCP 설정은 operator 책임" 라고 적으면 끝.
- 기존 `IRelayAdapter` 인터페이스 무변경.
- 각 피어가 자기 환경에 맞는 MCP 서버만 골라 쓸 수 있음 (flexibility).

### 단점
- 두 피어가 **서로 다른 도구를 쓸 수 있어** peer-symmetry 언어는 지키지만 실질 대칭은 깨질 수 있음.
- 릴레이는 "어느 턴에서 어떤 MCP 도구가 호출되었는지" 감사 로그에 남길 수 없음 (G8 감사 로그 커버리지 공백).
- MCP 서버 장애를 릴레이가 감지·우회 불가.

### peer 대칭성 검증
✅ 형식적으로 대칭 — 두 피어가 "각자 MCP 설정한다" 는 동일 규칙. 하지만 실질 도구 집합이 다를 수 있어 관찰 가능한 대칭성은 약함.

### MVP 이후 단계
- 문서 `docs/mcp-per-peer-setup.md` 추가 (관리자용 설정 가이드).
- `AdapterStatus` 에 "MCP 서버 N개 연결됨" 정보만 리포트하도록 소확장 (선택).

### 예상 PR 규모
**🟢 최대 1 파일 · 0 C# 변경 · 1 세션 (30분)**

---

## 안 B — Broker-registered shared registry (공용 목록 동기화)

### 한 줄 정의
릴레이가 MCP 서버 **목록 하나** 를 소유하고, 턴 시작 시 **두 피어에게 동일하게 전달** 한다. 실제 MCP 호출은 여전히 각 피어가 직접 수행.

### 그림
```
     ┌─────────────┐                      ┌─────────────┐
     │  Codex CLI  │                      │ Claude Code │
     └──────┬──────┘                      └──────┬──────┘
            │     ▲                        ▲     │
            ▼     │ (턴 시작 시 목록 주입)  │     ▼
       ┌──────────┴────────────────────────┴──────────┐
       │              RelayBroker                      │
       │   ┌────────────────────────────────────┐      │
       │   │  mcp_registry.yaml (단일 진실)     │      │
       │   │  - fs-read @ stdio                 │      │
       │   │  - github @ https                  │      │
       │   │  - slack  @ stdio                  │      │
       │   └────────────────────────────────────┘      │
       └───────────────────────────────────────────────┘
                                │
                                ▼
                      실제 MCP 호출은 각 CLI 가 수행
                      (하지만 같은 서버 리스트를 본다)
```

### 장점
- 두 피어가 **관찰 가능하게 동일한 도구 집합** 사용 → 진짜 peer-symmetry.
- `turn-{N}.yaml` 에 "이번 턴에 가용했던 MCP 서버 목록" 을 필드로 추가해 감사 로그 확장 가능.
- `mcp_registry.yaml` 한 곳만 바꾸면 양쪽 피어 도구 세트 변경.

### 단점
- `IRelayAdapter.RunTurnAsync` 에 MCP 서버 목록 파라미터 추가 필요 → **양쪽 어댑터 수정 필수**.
- 각 CLI 가 "외부에서 주입받은 MCP 설정" 을 받아들이는 방법이 CLI 마다 다를 수 있음 (Codex CLI 와 Claude Code CLI 의 MCP 설정 메커니즘이 완전 동일하지 않음).
- 릴레이는 여전히 **개별 도구 호출을 보지 못함** (서버 목록까지만 앎).

### peer 대칭성 검증
✅ 형식·실질 모두 대칭. `mcp_registry.yaml` 이 단일 진실, 두 어댑터는 동일 인자를 받음.

### MVP 이후 단계
1. `CodexClaudeRelay.Core/Models/McpServerSpec.cs` 추가 (`Name`, `Transport`, `Command`, `Env` 등).
2. `RelayTurnContext` 에 `IReadOnlyList<McpServerSpec> McpRegistry` 추가.
3. `ClaudeCliAdapter` / `CodexCliAdapter` 가 해당 목록을 CLI 인자 또는 환경변수로 주입.
4. `turn-{N}.yaml` 에 `mcp_registry` 필드 추가 (PACKET-SCHEMA.md 업데이트).
5. 라운드트립 테스트에 MCP 필드 추가 (PacketIOTests).

### 예상 PR 규모
**🟡 6~10 파일 · Core + Desktop + Tests · 2~3 세션 (3~5시간)**

---

## 안 C — Relay-proxied centralized (릴레이가 전부 대행)

### 한 줄 정의
MCP 서버는 **릴레이에만** 붙는다. 두 피어가 도구를 쓰고 싶으면 릴레이에게 "tool call" 을 요청하고, 릴레이가 MCP 를 호출해 결과를 반환.

### 그림
```
            ┌─────────────┐                      ┌─────────────┐
            │  Codex CLI  │                      │ Claude Code │
            └──────┬──────┘                      └──────┬──────┘
                   │  ① "fs-read /foo" 요청           │
                   ▼                                    ▼
       ┌──────────────────────────────────────────────────────┐
       │  RelayBroker (MCP client 역할)                        │
       │   ┌────────────────────────────────────┐              │
       │   │  ② MCP 서버 호출 (릴레이만 연결)   │              │
       │   │  - fs-read                         │──→ MCP server │
       │   │  - github                          │──→ MCP server │
       │   │  - slack                           │──→ MCP server │
       │   └────────────────────────────────────┘              │
       └──────────────────────────────────────────────────────┘
                   │  ③ 도구 결과 중계                 │
                   ▼                                    ▼
            Codex CLI                            Claude Code CLI
       (MCP 에 직접 연결 안 함)               (MCP 에 직접 연결 안 함)
```

### 장점
- 릴레이가 **모든 도구 호출을 본다** → 완전한 감사 로그 (G8 확장).
- 도구 호출 **캐싱·중복 제거** 가능 (피어 A 가 부른 `fs-read /foo` 결과를 피어 B 에 재사용).
- 도구 호출에 **승인 정책** 적용 가능 (destructive tool → operator approval UI).
- MCP 서버 관리가 **한 곳** 에 집중 (운영 단순).

### 단점
- **완전히 새로운 어댑터 ↔ 릴레이 프로토콜 필요**: 기존 `IRelayAdapter.RunTurnAsync` 는 "턴 실행" 만 다루는데, 턴 중간에 피어가 릴레이에 도구 호출을 요청하는 **역방향 채널** 이 필요.
- 각 CLI 가 자체 MCP 설정을 쓰는 걸 **금지** 하고 릴레이 호출로 대체하게 해야 함 → CLI 마다 배선 방법 다름 (기술적 난이도 높음).
- 실질적으로 릴레이가 MCP 호스트로 진화하는 것 → 릴레이의 **스코프 확장**. "브리지" 에서 "도구 게이트웨이" 로 성격 변함.
- PacketIO 스키마에 tool-call 이벤트 필드 확장 필요.

### peer 대칭성 검증
✅ 완벽한 대칭 — 두 피어가 동일한 gateway 를 경유.

### MVP 이후 단계
1. 새 프로젝트 `CodexClaudeRelay.Mcp/` 추가 (MCP client 구현 또는 `ModelContextProtocol.NET` 의존성).
2. `IRelayToolBroker` 인터페이스 + 기본 구현.
3. `IRelayAdapter` 확장 또는 신규 `IRelayToolSession` 인터페이스 — 어댑터가 턴 중간에 broker 에 tool call 요청.
4. 각 CLI 연결 방법 조사 및 배선 (Claude Code CLI, Codex CLI 가 외부 tool-broker 를 받아들이게 하는 방법).
5. `turn-{N}.yaml` 에 `tool_calls: []` 필드 추가.
6. 승인 UI (선택적으로 destructive tool 게이팅).

### 예상 PR 규모
**🔴 15~25 파일 · 신규 프로젝트 1~2개 · 새 인터페이스 + 어댑터 대개편 · 여러 세션 (10~20시간)**

---

## 📊 비교표 (한눈 요약)

| 기준 | 안 A | 안 B | 안 C |
|------|------|------|------|
| 구현 시간 | 30분 | 3~5시간 | 10~20시간 |
| 새 C# 파일 | 0 | 1~2 | 8~15 |
| 기존 인터페이스 변경 | 없음 | `RelayTurnContext` 필드 +1 | `IRelayAdapter` 재설계 |
| peer 관찰 대칭성 | 낮음 | 높음 | 완벽 |
| 감사 로그 커버리지 | 없음 | 서버 목록 | 모든 도구 호출 |
| 도구 호출 캐싱 | 불가 | 불가 | 가능 |
| 승인 정책 게이팅 | 불가 | 불가 | 가능 |
| 릴레이 스코프 | 불변 | 소확장 | **대확장** (브리지 → 게이트웨이) |
| MCP 서버 관리 부담 | 각 CLI 마다 | 릴레이 한 곳 | 릴레이 한 곳 |
| 미래 MCP 표준 변화 대응 | 각 CLI 의존 | 어댑터 수정 | 릴레이 중앙 수정 |

---

## 🤖 로봇 추천

**추천: 안 A 로 즉시 착지 → 안 B 로 점진 확장 → 안 C 는 운영 신호 나오면 검토**

### 근거

1. **현재 미션의 핵심은 "브리지" 이지 "도구 게이트웨이" 가 아니다.** `mission-clarification` 블록은 "MCP 가 가능한 경로" 를 요구했지 "릴레이가 MCP 를 주도" 하라고 요구하지 않음. 안 C 는 릴레이의 성격을 근본적으로 바꾸는 범위 확장이며 MVP 후속으로는 **과하다**.

2. **안 A 는 MVP 정신과 정합.** 96/96 테스트·8/8 게이트를 달성한 현재, 릴레이는 이미 "Codex↔Claude Code 턴 브리지" 로 완결돼 있음. MCP 지원을 "각 CLI 에 맡긴다" 고 공식화하는 것만으로도 미션 명확화 블록의 `MCP 가능 경로` 요구는 **문서적으로 만족**.

3. **안 B 는 진짜 대칭성 증거가 필요해질 때 착수.** "관리자가 실제로 두 피어 모두에 같은 도구를 주고 싶다" 는 운영 신호가 나오면 안 B 로 전진. 그 전까지는 안 A 로 충분.

4. **안 C 는 강력한 운영 유인 (감사·승인·캐싱 중 하나가 진짜로 요구됨) 이 생기기 전까지 보류.** 필요가 없는 상태에서 착수하면 릴레이 스코프가 불필요하게 부풀고 G8 감사 로그 / 기존 어댑터 / 패킷 스키마를 전부 재작업해야 함.

### 즉, 순차 로드맵
- **지금**: 안 A 채택 → 관리자용 MCP 설정 가이드 문서(`docs/mcp-per-peer-setup.md`) 1건만 착륙.
- **운영 중 "두 피어 도구 동기화 필요" 신호가 3회 이상 나올 때**: 안 B 로 전환.
- **운영 중 "도구 호출 감사·승인·캐싱 중 하나라도 긴급 필요" 신호가 나올 때**: 안 C 검토.

---

## 👉 관리자 결정 양식

다음 한 줄로 답해 주세요:

> `OPERATOR: b17 = a`  *(또는 b / c)*

추가 조건이나 변형이 필요하면:

> `OPERATOR: b17 = a + <추가 요청 한 줄>`

결정 접수 즉시 다음 iter 가 해당 안의 "예상 PR 규모" 섹션에 따라 실제 구현 PR 을 엽니다.

---

## 🔗 참조 파일

- `.autopilot/PROMPT.md` IMMUTABLE:mission-clarification (MCP 요구 원문)
- `CodexClaudeRelay.Core/Adapters/IRelayAdapter.cs` (어댑터 인터페이스)
- `CodexClaudeRelay.Desktop/Adapters/ClaudeCliAdapter.cs`
- `CodexClaudeRelay.Desktop/Adapters/CodexCliAdapter.cs`
- `Document/DAD/PACKET-SCHEMA.md` (패킷 스키마 — 안 B/C 에서 확장 필요)
