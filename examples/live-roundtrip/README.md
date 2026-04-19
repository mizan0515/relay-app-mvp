# 라이브 라운드트립 — 릴레이가 진짜로 패킷을 쏟아낸다

> **대상 독자**: 이전 예시(`template-integration`, `demo-session-dad-v2`)
> 가 **정적 픽스처** 로 스모크를 돌렸다면, 이 예시는 릴레이의 실제
> `PacketIO` 를 **런타임에 구동** 해서 `turn-{N}.yaml` 을 그 자리에서
> 생성합니다.
> **소요 시간**: 15초.

## 🎯 이 예시가 추가로 증명하는 것

| 예시 | 패킷 생성 방식 | 외부 템플릿 연계 |
|---|---|---|
| `demo-session-dad-v2` | 수작업 픽스처 체크인 | 읽기 전용 스펙 참조 |
| `template-integration` | 기존 픽스처 재검증 | 격차 관찰 |
| **`live-roundtrip` (이 예시)** | **런타임 C# 드라이버** | 필드 대칭 재확인 |

이 예시의 독점 증거: 릴레이의 `PacketIO.WriteAsync` ↔ `PacketIO.ReadAsync`
왕복이 **바이트 동등** (write → read → write 두 번째 쓰기가 첫 쓰기와
byte-equal). 캐노니컬 YAML 이 결정적임을 런타임에 확인.

## 📁 구성

```
examples/live-roundtrip/
├── README.md                       ← 이 파일
├── run-live.ps1                    ← PS1 드라이버 (3단계 보고)
├── .gitignore                      ← session-out / bin / obj 제외
└── LiveRoundtrip/
    ├── LiveRoundtrip.csproj        ← Core 참조, .sln 밖 (빌드 포커스 유지)
    └── Program.cs                  ← ~55줄, TurnPacket 2개 생성 + 왕복
```

실행하면 `examples/live-roundtrip/session-out/` 에 **새로운 세션**
(`live-YYYYMMDD-HHMMSS`) 두 턴이 뚝 떨어진다. gitignored 이므로
저장소에 커밋되지 않음.

## ▶️ 실행

```powershell
powershell -ExecutionPolicy Bypass -File examples/live-roundtrip/run-live.ps1
```

3단계 보고:

1. 빌드 + 드라이버 실행 → 2개 `turn-*.yaml` 생성, 바이트 동등 왕복 확인
2. `D:\dad-v2-system-template\en\Document\DAD\PACKET-SCHEMA.md` 와 생성된
   `turn-1.yaml` 의 **최상위 6개 필드** (`type`·`from`·`turn`·
   `session_id`·`handoff`·`peer_review`) 대칭 검사
3. 릴레이 자체 검증기(`tools/Validate-Dad-Packet.ps1`) 로 생성본 검증

옵션:
- `-OutDir <경로>` : 출력 위치 지정 (기본 `./session-out`)
- `-TemplateRoot <경로>` : 템플릿 저장소 위치 (기본 `D:\dad-v2-system-template`)
- `-TemplateVariant ko` : 한국어 변형 스펙 사용

## 🔒 템플릿 저장소 쓰기 0건

이 드라이버는 `$TemplateRoot` 에서 **읽기만** 한다 (`PACKET-SCHEMA.md`
한 파일). 세션 아티팩트는 전부 이 예시 폴더 안에 떨어진다.
`PROJECT-RULES.md` 의 read-only 규약 준수.

## 🧪 왜 솔루션 밖에 있나

`LiveRoundtrip.csproj` 는 의도적으로 `CodexClaudeRelay.sln` 에 **포함되지
않는다**. 루트 빌드(`dotnet build CodexClaudeRelay.sln`)는 릴레이 본체에
집중하고, 예시는 `dotnet run --project ...` 로만 구동된다. PR #63 의
`tools/run-smoke.ps1` 가 솔루션 안에서만 실행되는 것과 동일한 격리
원칙.
