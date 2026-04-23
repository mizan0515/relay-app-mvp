#!/usr/bin/env bash
# 오토파일럿 관리자 도구 (한국어, Unix/macOS/Linux)
# 비개발자도 사용할 수 있도록 만든 단일 메뉴 스크립트.
#
# 사용법:
#   bash .autopilot/관리자.sh           → 메뉴 표시
#   bash .autopilot/관리자.sh 상태      → 상태만 1회 출력
#   bash .autopilot/관리자.sh 정지      → HALT 파일 생성
#   bash .autopilot/관리자.sh 재개      → HALT 파일 삭제

set -u
ap=".autopilot"
verb="${1:-메뉴}"

줄() { printf '─%.0s' {1..60}; echo; }

# ─────────────────────────────────────────────────────────────
# 원격 머지 감지 + 결정 해소 자동 반영
# 로컬(특히 dev 브랜치)의 OPERATOR-DECISIONS.md 가 stale 일 때에도
# origin/<base> 를 권위자로 읽어서, resolved 된 결정이면:
#  (a) 로컬 파일 동기화  (b) HALT 자동 삭제  (c) STATE 의 decision_* 정리
# 오프라인/원격없음은 조용히 스킵.
# ─────────────────────────────────────────────────────────────

베이스브랜치() {
  if [ -f "$ap/STATE.md" ]; then
    b=$(grep -E '^base:' "$ap/STATE.md" | head -1 | awk '{print $2}')
    [ -n "$b" ] && { echo "$b"; return; }
  fi
  echo "main"
}

결정해소반영() {
  local base; base=$(베이스브랜치)
  local repoRoot; repoRoot=$(git rev-parse --show-toplevel 2>/dev/null) || return 0
  git fetch origin --quiet 2>/dev/null || true
  # .autopilot 상대 경로 계산
  local apAbs; apAbs=$(cd "$ap" && pwd) || return 0
  local rel="${apAbs#$repoRoot/}"
  local remoteText
  remoteText=$(git -C "$repoRoot" show "origin/${base}:${rel}/OPERATOR-DECISIONS.md" 2>/dev/null) || return 0
  [ -z "$remoteText" ] && return 0

  # STATE 에서 decision_slug 추출
  local decSlug=""
  if [ -f "$ap/STATE.md" ]; then
    decSlug=$(grep -E '^decision_slug:' "$ap/STATE.md" | head -1 | awk '{print $2}')
    [ "$decSlug" = "null" ] && decSlug=""
  fi

  # remote 에서 resolved 여부 확인
  local resolved=""
  if [ -n "$decSlug" ]; then
    if echo "$remoteText" | grep -qE "^##\s+${decSlug}.*status:\s*resolved"; then
      resolved="$decSlug"
    fi
  fi

  # 로컬 파일이 origin 과 다르면 동기화
  if [ -f "$ap/OPERATOR-DECISIONS.md" ]; then
    local localText; localText=$(cat "$ap/OPERATOR-DECISIONS.md")
    if [ "$localText" != "$remoteText" ]; then
      printf '%s' "$remoteText" > "$ap/OPERATOR-DECISIONS.md"
      echo "🔄 OPERATOR-DECISIONS.md 를 origin/${base} 기준으로 동기화했어요."
    fi
  fi

  if [ -n "$resolved" ]; then
    if [ -f "$ap/HALT" ]; then
      rm -f "$ap/HALT"
      echo "✅ 결정 '$resolved' 이 이미 머지되어 HALT 를 자동 해제했어요."
    fi
    # STATE decision_* 정리
    if [ -f "$ap/STATE.md" ]; then
      tmp=$(mktemp)
      sed -E -e '/^decision_slug:/d' -e '/^decision_pr:/d' -e '/^decision_branch:/d' -e '/^decision_note:/d' \
             -e 's/^status:\s*awaiting-decision.*/status: ready-after-decision/' \
             "$ap/STATE.md" > "$tmp" && mv "$tmp" "$ap/STATE.md"
    fi
    echo "$resolved"
    return 0
  fi
  return 0
}

상태읽기() {
  줄
  echo "🤖 오토파일럿 상태 점검"
  줄

  # 원격 머지 감지 → 해소 자동 반영 (오프라인은 조용히 스킵)
  결정해소반영 >/dev/null 2>&1 || true

  if [ -f "$ap/HALT" ]; then
    halt_body=$(tr -d '\r' < "$ap/HALT" 2>/dev/null | head -c 400)
    case "$halt_body" in
      *pending-decision*|*awaiting*|*decision*|*operator-direction*|*post-mvp*)
        echo "🙋 결정 PR 머지만 하시면 됩니다 (루프가 판단 대기 중)."
        echo "   → GitHub 에서 '🙋 결정 필요' 제목의 PR 을 찾아 머지하세요."
        echo "   → 파일 직접 수정·HALT 삭제 불필요. PR 머지로 자동 재개됩니다." ;;
      *)
        echo "🩺 건강 신호: ⛔ 비상정지 상태입니다"
        echo "   재개하려면 이 스크립트 한 줄만 실행하세요:"
        echo "   bash .autopilot/관리자.sh 재개" ;;
    esac
    줄; return
  fi

  # awaiting-decision (HALT 아님) — STATE.md 의 decision_pr 표시
  if [ -f "$ap/STATE.md" ]; then
    dec_pr=$(grep -E '^decision_pr:' "$ap/STATE.md" | head -1 | sed -E 's/^decision_pr:\s*//')
    if [ -n "$dec_pr" ] && [ "$dec_pr" != "null" ]; then
      echo "🙋 결정 PR 머지 대기 중: $dec_pr"
      echo "   → 이 PR 을 열어 옵션 하나만 [x] 체크(안 해도 A 기본) 후 머지 버튼."
      echo "   → 그게 전부입니다. 파일 편집 불필요."
      줄
    fi
  fi

  if [ -f "$ap/STATE.md" ]; then
    grep -E '^status:|^iteration:' "$ap/STATE.md" | head -5 | sed 's/^/📋 /'
  else
    echo "⚠️  STATE.md 없음 — 아직 한 번도 안 돌았거나 설치가 잘못됨"
  fi

  if [ ! -f "$ap/NEXT_DELAY" ]; then
    echo "ℹ️  아직 첫 반복이 끝나지 않았어요. 조금 기다려 주세요."
    줄; return
  fi
  if [ ! -f "$ap/LAST_RESCHEDULE" ]; then
    echo "🚨 멈춤 의심: 다음 깨어남 기록(LAST_RESCHEDULE)이 없어요."
    echo "   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md"
    줄; return
  fi

  line1=$(sed -n '1p' "$ap/LAST_RESCHEDULE")
  line2=$(sed -n '2p' "$ap/LAST_RESCHEDULE")

  case "$line1" in
    halted*|external-runner:*)
      echo "✅ 정상 (자동 깨어남 면제 상태: $line1)"; 줄; return ;;
  esac

  if [ -z "$line2" ] || [ "$line2" = "$line1" ]; then
    echo "🚨 멈춤 의심: 깨어남 증거가 부실해요 (1줄 기록 = 가짜 가능성)."
    echo "   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md"
    줄; return
  fi

  delay=$(tr -cd '0-9' < "$ap/NEXT_DELAY")
  ts_epoch=$(date -d "$line1" +%s 2>/dev/null \
    || python3 -c "import sys,datetime;print(int(datetime.datetime.fromisoformat(sys.argv[1].strip().replace('Z','+00:00')).timestamp()))" "$line1" 2>/dev/null \
    || echo 0)
  now_epoch=$(date +%s)
  age=$(( now_epoch - ts_epoch ))
  slack=600
  분지난=$(( age / 60 ))
  예정분=$(( delay / 60 ))

  if [ "$ts_epoch" -eq 0 ]; then
    echo "⚠️  타임스탬프 해석 실패: $line1"; 줄; return
  fi
  if [ "$age" -gt $(( delay + slack )) ]; then
    echo "🚨 멈춤 확인: ${분지난}분 전에 깨어났어야 했는데 안 깨어났어요."
    echo "   (예정 간격 ${예정분}분 + 여유 10분 초과)"
    echo "   → 클로드 코드 채팅에 다시 입력하세요:  /loop .autopilot/PROMPT.md"
  else
    남은=$(( (delay + slack - age) / 60 ))
    echo "✅ 정상 동작 중. 마지막 깨어남: ${분지난}분 전, 다음 점검까지 약 ${남은}분 여유"
  fi

  if [ -f "$ap/HISTORY.md" ]; then
    줄
    echo "📜 최근 작업 (HISTORY.md 끝 부분):"
    tail -12 "$ap/HISTORY.md" | sed 's/^/   /'
  fi
  줄
}

정지하기() {
  touch "$ap/HALT"
  echo "⛔ HALT 파일을 만들었어요. 다음 반복에서 루프가 정지합니다."
}

재개하기() {
  resolved=$(결정해소반영 2>&1 | tail -1)
  # 결정해소반영이 resolved 슬러그를 마지막 줄에 echo 하므로 감지.
  case "$resolved" in
    ''|🔄*|✅*HALT*) : ;;
    *) if echo "$resolved" | grep -Eq '^[A-Za-z0-9_-]+$'; then
         echo "✅ 머지된 결정 '$resolved' 을(를) 자동 반영했어요."
         echo "   /loop .autopilot/PROMPT.md 를 다시 입력하면 이어서 돕니다."
         return
       fi ;;
  esac

  if [ -f "$ap/HALT" ]; then
    body=$(tr -d '\r' < "$ap/HALT" 2>/dev/null | head -c 400)
    case "$body" in
      *pending-decision*|*awaiting*|*decision*|*operator-direction*|*post-mvp*)
        echo "🙋 결정 PR 이 아직 머지되지 않았어요."
        echo "   GitHub 에서 '🙋 결정 필요' PR 을 머지하면 자동 재개됩니다."
        echo "   (머지 후 이 명령을 다시 실행하면 HALT 를 자동 해제합니다.)"
        return ;;
    esac
    rm -f "$ap/HALT"
    echo "✅ 비상정지 HALT 를 해제했어요. 이제 /loop .autopilot/PROMPT.md 를 다시 입력하세요."
  else
    echo "ℹ️  HALT 파일이 원래 없었어요. 멈춰있다면 /loop .autopilot/PROMPT.md 를 다시 입력하세요."
  fi
}

시작안내() {
  줄
  echo "🚀 오토파일럿 시작"
  줄

  # start 한 번으로 원격 머지 감지 + HALT 자동 해제까지 처리.
  결정해소반영 >/dev/null 2>&1 || true

  if [ -f "$ap/HALT" ]; then
    body=$(tr -d '\r' < "$ap/HALT" 2>/dev/null | head -c 400)
    case "$body" in
      *pending-decision*|*awaiting*|*decision*|*operator-direction*|*post-mvp*)
        echo "🙋 결정 PR 이 아직 머지되지 않았어요. GitHub 에서 '🙋 결정 필요' PR 을 머지해 주세요."
        echo "   (머지 후 이 명령을 다시 실행하면 자동 재개됩니다.)"
        줄; return ;;
    esac
  fi

  echo "클로드 코드(터미널 앱)를 열고 다음을 입력하세요:"
  echo ""
  echo "   /loop .autopilot/PROMPT.md"
  echo ""
  echo "그게 끝이에요. 이후로는 알아서 작업하고, 작업이 끝나면 자동으로"
  echo "다시 시작합니다. PR 만들기·머지·브랜치 정리도 모두 자동입니다."
  echo ""
  echo "정지: 이 스크립트로 [2] 정지  또는  bash .autopilot/관리자.sh 정지"
  줄
}

메뉴() {
  while true; do
    상태읽기
    echo ""
    echo "무엇을 할까요?"
    echo "  [1] 시작 방법 보기"
    echo "  [2] 정지 (HALT 만들기)"
    echo "  [3] 재개 (HALT 지우기)"
    echo "  [4] 상태 새로고침"
    echo "  [0] 종료"
    read -r -p "번호 입력: " sel
    case "$sel" in
      1) 시작안내; read -r -p "엔터로 메뉴로" _ ;;
      2) 정지하기; read -r -p "엔터" _ ;;
      3) 재개하기; read -r -p "엔터" _ ;;
      4) continue ;;
      0) return ;;
      *) echo "0~4 중에서 골라주세요."; sleep 1 ;;
    esac
  done
}

case "$verb" in
  메뉴|menu)       메뉴 ;;
  상태|status)     상태읽기 ;;
  정지|stop)       정지하기 ;;
  재개|resume)     재개하기 ;;
  시작|start)      시작안내 ;;
  *) echo "모르는 명령: $verb"; echo "사용 가능: 메뉴 / 상태 / 정지 / 재개 / 시작" ;;
esac
