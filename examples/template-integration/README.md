# 템플릿 저장소와의 실험적 통합

> **대상 독자**: 이 릴레이(`D:\codex-claude-relay`)와 외부 참조 저장소
> (`D:\dad-v2-system-template`)의 **실제 호환성**을 한 번에 확인하고
> 싶은 관리자.
> **소요 시간**: 1분.

## 🎯 이 예시가 증명하는 것

1. **릴레이 단독 건강도**: 9개 E2E 테스트 통과 (Broker 라운드트립 ·
   recovery_resume · rotation · convergence · replay-dedup)
2. **릴레이 패킷 자체 검증 통과**: `tools/Validate-Dad-Packet.ps1`
3. **템플릿 스펙과의 필드 대칭**: 릴레이의 `turn-1.yaml` 이 템플릿
   `PACKET-SCHEMA.md` 의 8개 최상위 필드(`type` · `from` · `turn` ·
   `session_id` · `contract` · `peer_review` · `my_work` · `handoff`)를
   모두 채운다.
4. **알려진 형식 격차**: 템플릿의 `Validate-DadPacket.ps1` 는
   `turn-01.yaml` (두 자리) 를 기대하지만 릴레이는 `turn-1.yaml` (한
   자리) 를 방출함. 이는 정직하게 "정보용" 으로 표시되며 실험을
   실패시키지 않는다.

## ▶️ 실행

```powershell
powershell -ExecutionPolicy Bypass -File examples/template-integration/run-experiment.ps1
```

기본 템플릿 경로는 `D:\dad-v2-system-template` 이며
`-TemplateRoot <경로>` 로 덮어쓸 수 있다. 변형은 `-TemplateVariant ko`
로 한국어 버전 선택도 가능.

## 🔒 읽기 전용 규약

이 스크립트는 템플릿 저장소에 **어떤 파일도 쓰지 않는다**. 템플릿은
`PROJECT-RULES.md` 가 명시한 read-only protocol reference 이며, 릴레이
쪽에서만 아티팩트를 생성한다.

## 📊 현재 관찰 가능한 격차

| 항목 | 릴레이 | 템플릿 | 상태 |
|---|---|---|---|
| 최상위 패킷 필드 8개 | ✅ 전부 방출 | ✅ 전부 요구 | **호환** |
| 턴 파일 이름 | `turn-1.yaml` | `turn-01.yaml` | **격차** (포맷 차이) |
| 검증기 이름 | `Validate-Dad-Packet.ps1` | `Validate-DadPacket.ps1` | 별개 (각자 자기 규약) |
| 세션 디렉터리 구조 | `Document/dialogue/sessions/` | 동일 | **호환** |

## 🔁 다음 단계 (선택)

격차를 메우고 싶다면:

- `turn-1.yaml` → `turn-01.yaml` 포맷으로 릴레이를 바꿀지는 운영자
  결정. 바꾸면 `PacketIO.cs` · 기존 테스트 픽스처 · 데모 세션 세 곳을
  동시에 갱신해야 한다.
- 바꾸지 않는다면 릴레이는 **고유한 단순화된 변형** 으로 유지되며
  템플릿은 읽기 전용 스펙으로만 남는다. 이 실험은 후자를 기본으로
  전제한다.
