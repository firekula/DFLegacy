"""生成 map/index.md 与确定性 manifest.json（无时间戳、键有序、LF 结尾）。"""
from __future__ import annotations

import csv
import hashlib
import json
from collections import Counter
from pathlib import Path

TRACKED = ("map/opcode-map.tsv", "map/s2s-map.tsv", "ledger/opcodes.tsv",
           "ledger/scenarios.tsv", "ledger/runs.tsv", "map/index.md")

TIERS = ("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
         "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write")
MARKS = ("byte_verified", "field_read", "name_only")
STATUSES = ("mapped", "no-dispatcher", "unused")

BOUNDARIES = """\
## 边界（不得误读）

1. `T3/T4` 只表示"能在 handlers 目录里看到字段读/写调用"，**不等于**已读出有序字段布局。
2. `write_count` 含 `put_header`（129 行）；排除后换档数为 0，但家族级计数会偏大。
3. handlers 目录只覆盖 239/425 家族 ⇒ 186 个家族无字段调用数据（落 T1），是"没有数据"不是"没有字段"。
4. `[存在]` 只证明帧在场；S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"，`pass` 不得读作"该 opcode 已实现"。
5. `runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻。
6. `party-invite` / `trade-open` / `town-position-sync` 是占位骨架，有 netdump 时必然 red。
7. `ext_mark` 是人工轴，与六档正交；当前 byte_verified/field_read 极少，不得引用为"字段级进度"。
"""


def _rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    with path.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in r.items()} for r in csv.DictReader(fh, delimiter="\t")]


def render_index(map_rows: list[dict], runs: list[dict]) -> str:
    """人读报告；末尾恰好一个换行（生成物契约）。"""
    tiers = Counter(r.get("tier", "") for r in map_rows)
    kinds = Counter(r.get("kind", "") for r in map_rows)
    marks = Counter(r.get("ext_mark", "") for r in map_rows)
    status = Counter(r.get("status", "") for r in map_rows)
    lines = ["# 协议地图与进度账", "",
             f"总 opcode：**{len(map_rows)}**（CMD {kinds.get('CMD', 0)} / "
             f"NOTI {kinds.get('NOTI', 0)}）", "",
             "## kind 分布", "", "| kind | 条数 |", "|---|---|"]
    for kind in ("CMD", "NOTI"):
        lines.append(f"| {kind} | {kinds.get(kind, 0)} |")
    lines += [f"| **合计** | **{len(map_rows)}** |", "",
              "## 分档（六档之和必须等于总数）", "",
              "| 档位 | 条数 |", "|---|---|"]
    for tier in TIERS:
        lines.append(f"| {tier} | {tiers.get(tier, 0)} |")
    lines += [f"| **合计** | **{sum(tiers.values())}** |", "",
              "## status 分布", "", "| status | 条数 |", "|---|---|"]
    for state in STATUSES:
        lines.append(f"| {state} | {status.get(state, 0)} |")
    lines += [f"| **合计** | **{len(map_rows)}** |", "",
              "## 外部证据轴（人工标注，与六档正交）", "",
              "| ext_mark | 条数 |", "|---|---|"]
    for mark in MARKS:
        lines.append(f"| {mark} | {marks.get(mark, 0)} |")
    lines += ["", "## 真机 run", ""]
    if runs:
        lines += ["| run_id | status | pass | needs-human | missing | fail | 证据类 |",
                  "|---|---|---|---|---|---|---|"]
        for r in runs:
            lines.append(f"| {r.get('run_id', '')} | {r.get('status', '')} | {r.get('pass', '')} | "
                         f"{r.get('needs_human', '')} | {r.get('missing_evidence', '')} | "
                         f"{r.get('fail', '')} | {r.get('evidence_class', '')} |")
    else:
        lines.append("（暂无）")
    lines += ["", BOUNDARIES]
    return "\n".join(lines).rstrip("\n") + "\n"


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build_manifest(base: Path, index_bytes: bytes | None = None) -> dict:
    """构建清单；``index_bytes`` 给出时用它代替磁盘上的 ``map/index.md``（用于"先算后写"）。

    受跟踪产物缺失即 ``FileNotFoundError``：绝不生成"少了受跟踪产物"的清单 —— 那会让
    ``verify`` 的 §6「5 文件契约」无从判断，也让"删掉整本台账 + 从清单移除"成为可 PASS 的收窄路径。
    """
    base = Path(base)
    files: dict[str, dict] = {}
    for rel in TRACKED:
        if rel == "map/index.md" and index_bytes is not None:
            data = index_bytes                 # 尚未落盘的新 index（内存里算好的）
        else:
            p = base / rel
            if not p.is_file():
                raise FileNotFoundError(f"受跟踪产物缺失：{rel}（先补齐再 render）")
            data = p.read_bytes()
        rows = 0
        if rel.endswith(".tsv"):
            rows = max(0, data.decode("utf-8").count("\n") - 1)
        files[rel] = {"sha256": hashlib.sha256(data).hexdigest(), "rows": rows}
    map_rows = _rows(base / "map" / "opcode-map.tsv")
    s2s_rows = _rows(base / "map" / "s2s-map.tsv")
    tiers = Counter(r.get("tier", "") for r in map_rows)
    marks = Counter(r.get("ext_mark", "") for r in map_rows)
    kinds = Counter(r.get("kind", "") for r in map_rows)
    status = Counter(r.get("status", "") for r in map_rows)
    return {
        "schema": 1,
        "files": files,
        "baselines": {
            "opcodes_total": len(map_rows),
            "cmd": kinds.get("CMD", 0),
            "noti": kinds.get("NOTI", 0),
            "s2s": len(s2s_rows),
            "status": {k: status.get(k, 0) for k in STATUSES},
            "tiers": {k: tiers.get(k, 0) for k in TIERS},
            "ext_marks": {k: marks.get(k, 0) for k in MARKS},
        },
    }


def dump_manifest(manifest: dict) -> str:
    return json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


def write_manifest(base: Path, manifest: dict) -> Path:
    path = Path(base) / "manifest.json"
    path.write_bytes(dump_manifest(manifest).encode("utf-8"))
    return path


def render_all(base: Path) -> dict:
    """先把 index 与 manifest 都在**内存**里算好，任一步失败都不落盘；然后先写 index、再写 manifest。

    顺序与理由：
    1. manifest 里要记 `map/index.md` 的 sha256 ⇒ 先算出 index 的字节（内存里），把它交给
       ``build_manifest(index_bytes=…)``，**不需要**先把 index 写到磁盘上。
    2. 两者都算成功后，先写 `map/index.md`、再写 `manifest.json`：manifest 是"提交标记"，
       万一第二次写入失败，只会留下"index 比 manifest 新"的可检出不一致（``verify`` 的 sha256
       会报红），而不会留下"manifest 指向尚未写出的 index"这种更难排查的状态。
    """
    base = Path(base)
    map_rows = _rows(base / "map" / "opcode-map.tsv")
    runs = _rows(base / "ledger" / "runs.tsv")
    index_bytes = render_index(map_rows, runs).encode("utf-8")
    manifest = build_manifest(base, index_bytes=index_bytes)
    (base / "map" / "index.md").write_bytes(index_bytes)
    path = write_manifest(base, manifest)
    return {"manifest": str(path), "manifest_sha256": _sha256(path)}
