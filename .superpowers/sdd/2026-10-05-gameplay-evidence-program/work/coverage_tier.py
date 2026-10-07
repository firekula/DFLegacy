#!/usr/bin/env python3
r"""coverage_tier.py —— 协议字段级覆盖度的**纯逻辑**（加载 / 对账 / 分档）。

## 档位词表（互斥且穷尽；改这里必须同步改计划文档）

判定优先级：从上往下**第一个命中即停**。

  T0  no-evidence                status != "mapped" 或 dispatcher 为空
  T1  dispatcher-no-handlerdata  dispatcher 非空，但该家族不在 handlers 目录
  T4  fields-read+write          家族同时有 读 与 写 调用
  T3  fields-read-only           家族有 读、无 写
  T3b fields-write-only          家族有 写、无 读
  T2  handlerdata-no-fields      家族在目录里，但既无读也无写

判定只依据 handlers 目录的 `name=` 字段：

  READ  = r"name=PacketBuf::get_"
  WRITE = r"name=(?:InterfacePacketBuf|PacketBuf)::put_"

⚠️ 已知边界（必须写进报告，不许藏）：
  * `catalog_handlers_full.txt` 是**处理器体内的调用目标表**，不是字段布局。
    因此 T3/T4 只表示"能看到字段读/写的**候选**"，**不等于**已读出有序字段规格。
  * `catalog_handlers_full.txt` 只覆盖 239 个家族，而 dispatchers 目录有 425 个
    ⇒ 有 186 个 dispatcher **没有**字段调用数据（落 T1），这不是"没有字段"，是"没有数据"。
"""
from __future__ import annotations

import csv
import re
from dataclasses import dataclass
from pathlib import Path

READ = re.compile(r"name=PacketBuf::get_")
WRITE = re.compile(r"name=(?:InterfacePacketBuf|PacketBuf)::put_")

TIER_ORDER = (
    "T0_no-evidence",
    "T1_dispatcher-no-handlerdata",
    "T2_handlerdata-no-fields",
    "T3b_fields-write-only",
    "T3_fields-read-only",
    "T4_fields-read+write",
)

OPCODE_KEYS = ("kind", "id", "name", "rodata_va", "dispatcher", "status")


def load_opcodes(path: Path) -> list[dict]:
    """读 opcode-map.tsv。`id` 转 int；空 dispatcher 统一成 ''（**不是 None**，避免调用方两种判空）。"""
    out: list[dict] = []
    with Path(path).open(encoding="utf-8", newline="") as fh:
        for row in csv.DictReader(fh, delimiter="\t"):
            assert set(row) == set(OPCODE_KEYS), f"列名不符: {sorted(row)}"
            out.append({
                "kind": row["kind"],
                "id": int(row["id"]),
                "name": row["name"],
                "rodata_va": row["rodata_va"],
                "dispatcher": (row["dispatcher"] or "").strip(),
                "status": row["status"],
            })
    return out


def load_dispatcher_families(path: Path) -> set[str]:
    fams: set[str] = set()
    for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        if parts and parts[0].strip():
            fams.add(parts[0].strip())
    return fams


def load_handler_rows(path: Path) -> tuple[dict[str, list[str]], int]:
    """家族 -> 原始行列表（**保序**），并返回 `(fams, skipped)`。

    注释行与空行跳过；字段数 <3 的行**计入 skipped 并返回**，调用方必须断言它 ——
    "静默丢弃"正是本项目最警惕的失败形状（`run_ida.sh` 的 `${@:3}` 同类）。
    """
    fams: dict[str, list[str]] = {}
    skipped = 0
    for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) < 3:
            skipped += 1
            continue
        fams.setdefault(parts[0].strip(), []).append(line)
    return fams, skipped


@dataclass(frozen=True)
class SourceCounts:
    opcode_rows: int
    cmd: int
    noti: int
    mapped: int
    no_dispatcher: int
    unused: int
    dispatcher_families: int
    handler_families: int
    handler_rows: int
    handler_skipped: int

    def as_dict(self) -> dict:
        return self.__dict__.copy()


def count_sources(opcode_path: Path, dispatcher_path: Path, handler_path: Path) -> SourceCounts:
    rows = load_opcodes(opcode_path)
    fams, skipped = load_handler_rows(handler_path)
    return SourceCounts(
        opcode_rows=len(rows),
        cmd=sum(1 for r in rows if r["kind"] == "CMD"),
        noti=sum(1 for r in rows if r["kind"] == "NOTI"),
        mapped=sum(1 for r in rows if r["status"] == "mapped"),
        no_dispatcher=sum(1 for r in rows if r["status"] == "no-dispatcher"),
        unused=sum(1 for r in rows if r["status"] == "unused"),
        dispatcher_families=len(load_dispatcher_families(dispatcher_path)),
        handler_families=len(fams),
        handler_rows=sum(len(v) for v in fams.values()),
        handler_skipped=skipped,
    )


def classify(opcodes: list[dict], handler_rows: dict[str, list[str]]) -> list[dict]:
    """给每条 opcode 追加 tier / read_count / write_count。判定优先级第一个命中即停。"""
    out: list[dict] = []
    for r in opcodes:
        fam = r["dispatcher"]
        rows = handler_rows.get(fam, []) if fam else []
        read_n = sum(1 for line in rows if READ.search(line))
        write_n = sum(1 for line in rows if WRITE.search(line))

        if r["status"] != "mapped" or not fam:
            tier = "T0_no-evidence"
        elif not rows:
            tier = "T1_dispatcher-no-handlerdata"
        elif read_n and write_n:
            tier = "T4_fields-read+write"
        elif read_n:
            tier = "T3_fields-read-only"
        elif write_n:
            tier = "T3b_fields-write-only"
        else:
            tier = "T2_handlerdata-no-fields"

        item = dict(r)
        item["tier"] = tier
        item["read_count"] = read_n
        item["write_count"] = write_n
        out.append(item)
    return out


def summarize(classified: list[dict]) -> dict:
    s: dict[str, int] = {t: 0 for t in TIER_ORDER}
    s["total"] = 0
    for r in classified:
        s[r["tier"]] += 1
        s["total"] += 1
    return s


def write_by_opcode(path: Path, classified: list[dict]) -> None:
    cols = ("kind", "id", "name", "status", "dispatcher", "tier",
            "read_count", "write_count", "rodata_va")
    lines = ["\t".join(cols)]
    for r in sorted(classified, key=lambda x: (x["kind"], x["id"])):
        lines.append("\t".join(str(r[c]) for c in cols))
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")
