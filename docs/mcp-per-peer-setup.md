# MCP Per-Peer Setup (안 A 착지 가이드)

이 문서는 관리자(operator) 용 가이드입니다. DAD-v2 릴레이는 MCP(Model
Context Protocol) 에 대해 **아무것도 알지 못하며**, 각 피어(Codex CLI /
Claude Code CLI) 가 **자체 설정으로** MCP 서버에 붙습니다.

설계 근거: [.autopilot/B17-MCP-DESIGN-OPTIONS.md](../.autopilot/B17-MCP-DESIGN-OPTIONS.md)
안 A (Per-peer pass-through). 운영자 결정: `b17 = a` (2026-04-19).

## 한 줄 요약

> 릴레이는 턴 패킷(`turn-{N}.yaml`)만 왕복시킨다. MCP 서버 설정은 각
> CLI 가 자기 홈 설정 파일에서 읽는다. 두 피어가 **같은 도구를 보려면**
> 관리자가 **양쪽 설정을 같게 유지**해야 한다.

## 관리자가 해야 할 일

### 1. Codex CLI 쪽 MCP 설정

Codex CLI 의 MCP 서버 목록은 CLI 자체 설정(일반적으로
`~/.codex/config.toml` 또는 프로젝트 로컬 설정)에서 관리합니다. 현재
릴레이는 이 파일을 읽거나 쓰지 않습니다.

설정 방법은 Codex CLI 공식 문서를 따르세요. 릴레이 쪽에서 추가로 할
일은 없습니다.

### 2. Claude Code CLI 쪽 MCP 설정

Claude Code CLI 는 `~/.claude/mcp.json` (또는 프로젝트
`.claude/mcp.json`) 에서 MCP 서버 목록을 읽습니다. 예시:

```json
{
  "mcpServers": {
    "fs-read": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "D:/work"]
    }
  }
}
```

릴레이는 이 파일을 변경하지 않습니다.

### 3. Peer-symmetry 유지 (권장)

DAD-v2 는 peer-symmetric 프로토콜입니다. 두 피어가 **다른 도구 집합** 을
가지면 형식적 대칭은 유지되지만 **관찰 가능한 대칭성은 약해집니다**.
가능하면 양쪽에 **동일한 MCP 서버 목록** 을 등록하세요.

관리자 체크리스트:

- [ ] Codex CLI 쪽 MCP 서버 목록 정리(N개)
- [ ] Claude Code CLI 쪽에 **같은 N개** 등록
- [ ] 두 피어 모두에서 `status` 또는 동등 명령으로 서버 연결 확인

## 릴레이는 무엇을 보지 못하는가

안 A 선택에 따른 **알려진 한계**:

1. **감사 로그 공백**: JSONL 감사 로그(G8)에 "이번 턴에서 어떤 MCP
   도구가 호출됐는지" 는 기록되지 않습니다. 릴레이가 그 호출을 보지
   못하기 때문입니다.
2. **도구 장애 감지 불가**: MCP 서버가 죽어도 릴레이는 감지할 수
   없습니다. 각 CLI 가 알아서 리포트합니다.
3. **도구 집합 실질 대칭 비보장**: 위 3단계 체크리스트를 관리자가
   수동으로 지켜야 합니다.

이 한계가 운영상 문제가 되면 **안 B (shared registry)** 로 업그레이드하는
것이 다음 단계입니다. 설계는
[.autopilot/B17-MCP-DESIGN-OPTIONS.md](../.autopilot/B17-MCP-DESIGN-OPTIONS.md)
안 B 섹션 참조.

## 릴레이 코드 변경 없음

안 A 는 **C# 코드 0 변경** 입니다. `IRelayAdapter` 인터페이스, 패킷
스키마, 어댑터 구현체 모두 건드리지 않습니다. 본 문서 1건 착륙이 안 A
의 전부입니다.
