#!/usr/bin/env python3
r"""coverage_report.py —— 覆盖度盘点的 **CLI 入口**：读真产物 → 分档 → 落盘 → 当场对账。

输入（默认在 `--out-dir` 里，只读）：opcode-map.tsv / catalog_dispatchers_full.txt /
catalog_handlers_full.txt。输出（写进 `--out-dir`）：coverage-by-opcode.tsv、
coverage-summary.md。对账五条基线，**任一不符即非零退出** —— 不静默通过。
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from coverage_tier import (TIER_ORDER, classify, count_sources, load_handler_rows,
                           load_opcodes, summarize, write_by_opcode)

WORK = Path(__file__).resolve().parent

# 对账基线（真产物，只读）。逐条与计划 Step 6 的 Expected 相同。
BASELINE = {
    "cmd": 608, "noti": 563, "total": 1171,
    "mapped": 428, "no_dispatcher": 741, "unused": 2,
    "dispatcher_families": 425, "handler_families": 239,
    "handler_rows": 7273, "handler_skipped": 0,
    "mapped_dispatchers": 423, "mapped_dispatchers_without_handlerdata": 186,
}

VERBATIM_LIMITS = (
    "[静态] 本覆盖率的分母 = opcode-map.tsv 的 1171 条 (CMD 608 / NOTI 563)。",
    '[静态] T3/T4 只表示"能在 handlers 目录里看到字段读/写调用"，不等于"已读出有序字段规格"；\n'
    "       字段布局仍须逐家族反编译读取（不在本计划范围）。",
    '[静态] catalog_handlers_full.txt 只覆盖 239/425 个 dispatcher 家族，而"mapped opcode 用到的\n'
    '       dispatcher"有 423 个 ⇒ 其中 186 个家族没有字段调用数据，落到 T1 的 opcode 是 187 条\n'
    '       （差 1 因为 StartGame 家族有 2 条 mapped opcode）；这是"没有字段调用数据"，不是"没有字段"。',
    "[未验证] 外部证据档（byte_verified / field_read）首版为空；在它有内容之前，\n"
    '       本覆盖率**不得**被引用为"字段级已完成多少"。',
)


def compute_extra(opcodes: list[dict], handler_families: set[str]) -> dict:
    """算两个 `SourceCounts` 装不下的数。

    * `mapped_dispatchers` —— `status=='mapped'` 且 dispatcher 非空的行里 **distinct
      dispatcher** 的个数（423）。
    * `mapped_dispatchers_without_handlerdata` —— 上者中家族名**不**在 handlers 目录家族
      集合里的个数（186）。

    分母是 **423**（mapped dispatchers），**不是** 425（dispatchers 目录全部家族）；
    两者差在"是否已被 opcode 引用"。handlers 家族集合含合成家族 `(sender)`。
    """
    fams = {r["dispatcher"] for r in opcodes
            if r["status"] == "mapped" and r["dispatcher"]}
    without = sum(1 for f in fams if f not in handler_families)
    return {"mapped_dispatchers": len(fams),
            "mapped_dispatchers_without_handlerdata": without}


def reconcile_lines(counts, extra: dict, tier_sum: int) -> list[tuple[str, bool]]:
    """五条对账，返回 `(打印文本, 是否相符)`。纯函数 ⇒ 可被独立复跑核对。"""
    b = BASELINE
    return [
        (f"CMD={counts.cmd} NOTI={counts.noti} total={counts.opcode_rows}",
         (counts.cmd, counts.noti, counts.opcode_rows) == (b["cmd"], b["noti"], b["total"])),
        (f"mapped={counts.mapped} no-dispatcher={counts.no_dispatcher} unused={counts.unused}",
         (counts.mapped, counts.no_dispatcher, counts.unused)
         == (b["mapped"], b["no_dispatcher"], b["unused"])),
        (f"dispatcher_families={counts.dispatcher_families} "
         f"handler_families={counts.handler_families} handler_rows={counts.handler_rows} "
         f"handler_skipped={counts.handler_skipped}",
         (counts.dispatcher_families, counts.handler_families, counts.handler_rows,
          counts.handler_skipped) == (b["dispatcher_families"], b["handler_families"],
                                      b["handler_rows"], b["handler_skipped"])),
        (f"mapped_dispatchers={extra['mapped_dispatchers']} "
         f"mapped_dispatchers_without_handlerdata="
         f"{extra['mapped_dispatchers_without_handlerdata']}",
         (extra["mapped_dispatchers"], extra["mapped_dispatchers_without_handlerdata"])
         == (b["mapped_dispatchers"], b["mapped_dispatchers_without_handlerdata"])),
        (f"tier_total={tier_sum}  (各档之和必须等于 {counts.opcode_rows})",
         tier_sum == counts.opcode_rows == b["total"]),
    ]


def write_summary(path: Path, counts, summary: dict, ext: dict) -> None:
    """写 `coverage-summary.md`。四项限定语句**逐字**取自计划，不许改写措辞。"""
    tier_rows = "\n".join(f"| {t} | {summary[t]} |" for t in TIER_ORDER)
    limits = "\n".join(VERBATIM_LIMITS)
    body = f"""# 协议字段级覆盖度盘点（1171 条 opcode）

## 1. 源对账（只读真产物）

| 项 | 值 |
|---|---|
| opcode 行数 | {counts.opcode_rows} |
| CMD / NOTI | {counts.cmd} / {counts.noti} |
| status=mapped | {counts.mapped} |
| status=no-dispatcher | {counts.no_dispatcher} |
| status=unused | {counts.unused} |
| dispatchers 目录家族数 | {counts.dispatcher_families} |
| handlers 目录家族数 | {counts.handler_families} |
| handlers 目录行数 | {counts.handler_rows} |
| handlers 被跳过的行（字段数<3） | {counts.handler_skipped} |
| mapped dispatchers（distinct） | {ext.get("mapped_dispatchers")} |
| 其中无 handler 数据 | {ext.get("mapped_dispatchers_without_handlerdata")} |

## 2. 分档结果（各档之和必须等于 {counts.opcode_rows}）

| 档位 | 条数 |
|---|---|
{tier_rows}
| **合计** | **{summary["total"]}** |

判定优先级（第一个命中即停）：

```
T0  no-evidence                status != "mapped" 或 dispatcher 为空
T1  dispatcher-no-handlerdata  dispatcher 非空，但该家族不在 handlers 目录
T4  fields-read+write          家族同时有 读 与 写 调用
T3  fields-read-only           家族有 读、无 写
T3b fields-write-only          家族有 写、无 读
T2  handlerdata-no-fields      家族在目录里，但既无读也无写
```

字段读/写判定只依据 handlers 目录的 `name=` 字段：

```
READ  = r"name=PacketBuf::get_"
WRITE = r"name=(?:InterfacePacketBuf|PacketBuf)::put_"
```

⚠️ `source=put_header`（构造帧头）**不**匹配 `WRITE` ⇒ 不计作字段写。

## 3. 这数字不能证明什么（限定语句）

{limits}

## 4. 外部证据档状态

外部证据档（`byte_verified` / `field_read`）首版为空（Task 3 才建 `tiers-external.tsv`）；
在它有内容之前，上表的 T3/T4 只表示"handler 目录里能看到字段调用"，不表示"字段级已完成"。
"""
    Path(path).write_text(body, encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="协议字段级覆盖度盘点（离线静态）")
    ap.add_argument("--out-dir", default=str(WORK / "out"),
                    help="真产物目录（读输入 + 写输出）")
    ap.add_argument("--external", default=None,
                    help="外部证据档 TSV —— Task 3 才实现；本版本传了会非零退出")
    args = ap.parse_args(argv)

    if args.external:
        print("--external 由 Task 3 实现，本版本不支持", file=sys.stderr)
        return 2

    out_dir = Path(args.out_dir)
    if not out_dir.is_absolute():
        # 相对路径按脚本所在目录（work/）解析，避免 cwd 漂移造成"读不到却像成功"。
        out_dir = (WORK / out_dir).resolve()

    opcode_path = out_dir / "opcode-map.tsv"
    dispatcher_path = out_dir / "catalog_dispatchers_full.txt"
    handler_path = out_dir / "catalog_handlers_full.txt"
    missing = [str(p) for p in (opcode_path, dispatcher_path, handler_path)
               if not p.exists()]
    if missing:
        print("缺输入: " + ", ".join(missing), file=sys.stderr)
        return 2

    opcodes = load_opcodes(opcode_path)
    handler_rows, skipped = load_handler_rows(handler_path)
    counts = count_sources(opcode_path, dispatcher_path, handler_path)

    # 双重核对：count_sources 内部又读了一遍，两次结果必须一致（防静默丢弃）。
    assert len(handler_rows) == counts.handler_families, (
        f"家族数不一致: {len(handler_rows)} != {counts.handler_families}")
    assert skipped == counts.handler_skipped, (
        f"skipped 不一致: {skipped} != {counts.handler_skipped}")
    assert len(opcodes) == counts.opcode_rows, "opcode 行数不一致"

    classified = classify(opcodes, handler_rows)
    summary = summarize(classified)
    extra = compute_extra(opcodes, set(handler_rows))
    tier_sum = sum(summary[t] for t in TIER_ORDER)

    write_by_opcode(out_dir / "coverage-by-opcode.tsv", classified)
    write_summary(out_dir / "coverage-summary.md", counts, summary, extra)

    ok = True
    for text, good in reconcile_lines(counts, extra, tier_sum):
        print(text if good else "✗ " + text)
        ok = ok and good
    for t in TIER_ORDER:
        print(f"    {t}={summary[t]}")

    if not ok:
        print("对账失败：带 ✗ 的行与基线不符", file=sys.stderr)
        return 1
    print(f"输出: {out_dir / 'coverage-by-opcode.tsv'}")
    print(f"输出: {out_dir / 'coverage-summary.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
