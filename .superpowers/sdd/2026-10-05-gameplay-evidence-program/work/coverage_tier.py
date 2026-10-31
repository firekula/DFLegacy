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
  * `write_count` 计的是"`name=...::put_*` 写调用条数"，**含**帧头构造
    `InterfacePacketBuf::put_header`（129 行；实测把它排除后 1171 条换档数 = 0，
    即档位判据不受影响，但含 `put_header` 的家族 `write_count` 会偏大）。

## 外部证据档（**独立轴**，不混进上面六档）

`ext_mark ∈ EXTERNAL_MARKS`（`byte_verified > field_read > name_only`）由**人工**标注，
来源是 `tiers-external.tsv`；`load_external_marks` 只做校验与合并，`apply_external_marks`
追加 `ext_mark` / `ext_evidence`。这一轴与 tier **正交**：`NOTI 1` 是 T0（没有可达处理器）
却可以是 `byte_verified`（"没有 dispatcher"不等于"没有外部证据"）。
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

# 外部证据档词表，**越靠前越强**（同一 (kind,id) 多行标注时取最靠前的那个）。
EXTERNAL_MARKS = ("byte_verified", "field_read", "name_only")


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


def load_external_marks(path: Path) -> dict[tuple[str, int], dict]:
    """读人工标注的外部证据档 TSV（列序 `kind, id, mark, evidence`；`#` 行与空行跳过）。

    同一个 `(kind, id)` **允许多行**（不同证据各自一行）：取**最强**的 `mark`
    （强度序见 `EXTERNAL_MARKS`，越靠前越强），`evidence` 按文件序用 `" | "` 合并。

    失败形状**不静默**：`mark not in EXTERNAL_MARKS` ⇒ `raise ValueError(mark)`；
    列数 != 4 的行（含漏列/多列）⇒ `raise ValueError` 并带 `路径:行号`；
    `id` 非整数 ⇒ `int()` 抛 `ValueError`。
    """
    merged: dict[tuple[str, int], dict] = {}
    src = Path(path)
    for lineno, line in enumerate(src.read_text(encoding="utf-8").splitlines(), start=1):
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 4:
            raise ValueError(
                f"{src}:{lineno} 需要 4 列 kind/id/mark/evidence，实得 {len(parts)} 列")
        kind, raw_id, mark, evidence = parts
        mark = mark.strip()
        if mark not in EXTERNAL_MARKS:
            raise ValueError(mark)
        key = (kind.strip(), int(raw_id))
        cur = merged.get(key)
        if cur is None:
            merged[key] = {"mark": mark, "evidence": evidence}
            continue
        if evidence:
            cur["evidence"] = (
                f"{cur['evidence']} | {evidence}" if cur["evidence"] else evidence)
        if EXTERNAL_MARKS.index(mark) < EXTERNAL_MARKS.index(cur["mark"]):
            cur["mark"] = mark
    return merged


def apply_external_marks(classified: list[dict], marks: dict) -> list[dict]:
    """给每条 opcode 追加外部证据档 `ext_mark` / `ext_evidence`（**独立轴**，不动 tier）。

    未出现在 `marks` 里的行 ⇒ `ext_mark="name_only"`、`ext_evidence=""`。
    `marks` 的键**必须**能在 `classified` 里找到，否则 `raise KeyError(键)` ——
    标注一个不存在的 opcode 是"证据指向空气"，绝不能静默吞掉。
    `mark not in EXTERNAL_MARKS` ⇒ `raise ValueError(mark)`。

    先把全部键与值校验完再改行：半途抛错不留"改了一半"的结果。
    """
    index = {(r["kind"], int(r["id"])): r for r in classified}
    norm: dict[tuple[str, int], dict] = {}
    for key, m in marks.items():
        k = (key[0], int(key[1]))
        if k not in index:
            raise KeyError(key)
        if m["mark"] not in EXTERNAL_MARKS:
            raise ValueError(m["mark"])
        norm[k] = m

    out: list[dict] = []
    for r in classified:
        item = dict(r)
        m = norm.get((r["kind"], int(r["id"])))
        if m is None:
            item["ext_mark"] = "name_only"
            item["ext_evidence"] = ""
        else:
            item["ext_mark"] = m["mark"]
            item["ext_evidence"] = m["evidence"]
        out.append(item)
    return out


def write_by_opcode(path: Path, classified: list[dict]) -> None:
    cols = ["kind", "id", "name", "status", "dispatcher", "tier",
            "read_count", "write_count", "rodata_va"]
    # 外部证据档只在**真跑过** apply_external_marks 时才追加两列：
    # 不跑 ⇒ 列集与 Task 2 逐字节相同（不引入回归）。新列一律**追加在末尾**，
    # 前 9 列的序号不变（下游按列号取 tier 的命令不受影响）。
    if any("ext_mark" in r for r in classified):
        cols += ["ext_mark", "ext_evidence"]
    lines = ["\t".join(cols)]
    for r in sorted(classified, key=lambda x: (x["kind"], x["id"])):
        lines.append("\t".join(str(r.get(c, "")) for c in cols))
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")
