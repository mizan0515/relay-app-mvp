#!/usr/bin/env python3
"""
Regenerate the "📈 진행 히스토그램" block in .autopilot/대시보드.md from
.autopilot/METRICS.jsonl.

Usage: python3 .autopilot/regen-histogram.py
  (prints the block to stdout — paste into 대시보드.md under the table.)

Part of B13.1 follow-up (iter57). Kept deliberately simple — single-file
stdlib-only, no external deps.
"""
import json
import collections
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
METRICS = ROOT / "METRICS.jsonl"


def load_events():
    for line in METRICS.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        try:
            yield json.loads(line)
        except json.JSONDecodeError:
            continue


def bucket_events():
    buckets = collections.defaultdict(lambda: {"iters": 0, "merges": 0, "flips": []})
    flip_table = []
    for ev in load_events():
        ts = datetime.fromisoformat(ev["ts"].replace("Z", "+00:00"))
        hour = (ts.hour // 6) * 6
        key = f"{ts.strftime('%m-%d')} {hour:02d}h"
        buckets[key]["iters"] += 1
        if ev.get("merged"):
            buckets[key]["merges"] += 1
        gf = ev.get("gate_flip")
        if gf and gf != "null":
            buckets[key]["flips"].append(gf)
            flip_table.append((ev["iter"], ev["ts"][:16].replace("T", " "), gf))
    return buckets, flip_table


def render():
    buckets, flip_table = bucket_events()
    lines = ["## 📈 진행 히스토그램 (METRICS.jsonl 파생)\n"]
    lines.append("**6시간 구간별 머지 + 게이트 flip** — `#` 하나 = PR 머지 1건.\n")
    lines.append("```")
    for key in sorted(buckets):
        b = buckets[key]
        bar = "#" * b["merges"]
        flips = ", ".join(b["flips"])
        flip_tail = f"  <- {flips}" if flips else ""
        lines.append(f"{key}  iters={b['iters']:2d}  merges={b['merges']:2d}  {bar:<8}{flip_tail}")
    lines.append("```\n")
    lines.append("**게이트 flip 연표**:\n")
    lines.append("| iter | 시각 (UTC) | flip |")
    lines.append("|------|-----------|------|")
    for it, ts, gf in flip_table:
        lines.append(f"| {it} | {ts} | {gf} |")
    return "\n".join(lines)


if __name__ == "__main__":
    print(render())
