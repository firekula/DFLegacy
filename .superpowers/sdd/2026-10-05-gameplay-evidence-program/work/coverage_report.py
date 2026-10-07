#!/usr/bin/env python3
r"""coverage_report.py —— 覆盖度盘点的 **CLI 入口**：读真产物 → 分档 → 落盘 → 当场对账。

输入（默认在 `--out-dir` 里，只读）：opcode-map.tsv / catalog_dispatchers_full.txt /
catalog_handlers_full.txt。输出（写进 `--out-dir`）：coverage-by-opcode.tsv、
coverage-summary.md。对账五条基线，**任一不符即非零退出** —— 不静默通过。

传 `--external <tsv>` 时再叠加**外部证据档**（独立轴 `ext_mark` / `ext_evidence`，由人工
标注、本脚本只校验与合并）；不传则**不给任何行加该轴的两列**（`coverage-by-opcode.tsv` 仍与
Task 2 逐字节相同），但 `ext_mark` 的**定义段**（分级门槛 + 当前清单）照样写进报告 ——
那一列在逐条产物里出现与否，读者都需要能就地查到它的取值含义。
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from coverage_tier import (EXTERNAL_MARKS, TIER_ORDER, apply_external_marks, classify,
                           count_sources, load_external_marks, load_handler_rows,
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

# Step 6 的对照句（**逐字**取自计划，不许改写措辞）。
VERBATIM_EXTERNAL = (
    "[静态] 本表 ext_mark=byte_verified 的条目，其 evidence 全部指向本项目已落盘产物；\n"
    '       未出现在 tiers-external.tsv 的 opcode 一律 ext_mark=name_only（即"只有名字"）。'
)

# 不跑 `--external` 时的报告尾部。ext 轴的**定义**（分级门槛）两种模式都要有 ——
# 默认产物的读者也会看到 `ext_mark` 这一列，必须能就地查到它的取值是怎么定义的；
# 但默认模式**不**给任何行加 `ext_mark` 列 ⇒ `coverage-by-opcode.tsv` 与 Task 2 逐字节相同（真正的零回归断言）。
TAIL_NO_EXTERNAL_LEGACY_NOTE = """[作废] 本节旧文案"外部证据档（byte_verified / field_read）首版为空（Task 3 才建
       `tiers-external.tsv`）"写于外部证据档为空时，已被上面的定义与清单取代（原文见 `8334e23`，**不删**）。"""


def _fmt_keys(keys: list) -> str:
    """`[("CMD", 1), ("NOTI", 561)]` → `CMD 1、NOTI 561`（空列表 → 明确写"（无）"，不留空）。"""
    return "、".join(f"{k} {i}" for k, i in keys) if keys else "（无）"


def ext_definition_block(byte_keys, field_keys, missing_note: str = "") -> str:
    """`ext_mark` 的分级门槛（**定义**，两种运行模式都渲染）。

    当前清单**从外部证据档算出来**（`byte_keys is None` 表示读不到档，此时把原因写出来，
    不留空白、也不假装知道）。判定依据一律指回 TSV 的 `evidence` 列 —— 本报告不生成依据。
    """
    if byte_keys is None:
        current = f"[未验证] {missing_note}"
    else:
        current = (f"[静态] 当前 `byte_verified` 只有 {_fmt_keys(byte_keys)}（{len(byte_keys)} 条）；"
                   f"`field_read` 只有 {_fmt_keys(field_keys)}（{len(field_keys)} 条）。")
    return f"""### `ext_mark` 的分级门槛（定义）

- `byte_verified` —— **该 opcode 的线上字节被逐字段钉住、落盘且可复跑**；只有达到这条才配 `byte_verified`。
- `field_read` —— 只有**静态字段序**（反汇编里的读/写次序）或**未经逐字段核对的抓包观测**。
- `name_only` —— 没有任何字段级证据，只有名字。

每个非 `name_only` 的判定依据写在**该 opcode 的 `evidence` 列**（`out/tiers-external.tsv`）；
本报告只做校验与合并，**不自动生成依据**。

{current}"""


# `NOTI 1` 降级的依据（只在它**真的**处于 field_read 时才渲染 ⇒ 标注再变这句话不会变成假话）。
NOTI1_DOWNGRADE_NOTE = """[作废] `NOTI 1` 曾标 `byte_verified`（原文见 `8334e23` 的 `out/tiers-external.tsv`，**不删**），
       Task 3 的 Fix 1 按证据分级降为 `field_read`：它的证据只有 `cipher-callchain-audit.txt` L3342-3517 的
       **静态字段序**（`CUser::InitOnAccept` 里 `put_binary(334)`/`get_channel_name`/seed/random 的写入次序）
       与"抓包两样本前 334 B 相同"这条观测，**未**把 334 B 载荷逐字段对上。项目内两处反向记录指向推翻它：
       `cipher-audit-corrections.md:70` 的 `[作废]`（"334B key+103B 字段已逐字段闭合" —— "没有解开 NOTI1，
       也没有逐字段比对"）、`dynamic-evidence-progress-2026-10-07.md:368`（"[真机] 未取的：334 B key 本体"）。
       该行 `evidence` 原文保留 —— 它就是降级的依据。
       上面那个提交指针指向**本分支**的提交；若历史被改写，以 `out/tiers-external.tsv` 的当前内容为准。"""


def _noti1_note(field_keys) -> str:
    """`NOTI 1` 的降级说明是"对某条数据的裁决"，不是定义 ⇒ 只在它真的是 field_read 时才渲染。"""
    if field_keys and ("NOTI", 1) in field_keys:
        return "\n\n" + NOTI1_DOWNGRADE_NOTE
    return ""


# `NOTI 561` 降级的依据（同样只在它**真的**处于 field_read 时才渲染）。本轮 fix wave 的 F-1。
NOTI561_DOWNGRADE_NOTE = """[作废] `NOTI 561` 曾标 `byte_verified`（原文见 `09e2b75` 的 `out/tiers-external.tsv`，**不删**），
       本轮 fix wave（F-1）按证据分级降为 `field_read`：`out/noti561-closure.md` 自述
       "类型17密码/封装 **已闭合**；类型19密码/封装结构 **已闭合**，BinCount=5 的业务作用 **已降级**；
       54 个内层业务字段语义 **已降级**" ⇒ 密码/封装闭合，但**字段语义没有被逐字段钉住**，
       不满足本表的 `byte_verified` 门槛。该行 `evidence` 保留 `57/54/3` 三个数字，并补记该文件的自述限定；
       上面那个提交指针指向**本分支**的提交；若历史被改写，以 `out/tiers-external.tsv` 的当前内容为准。"""


def _noti561_note(field_keys) -> str:
    """`NOTI 561` 的降级说明同上：只在对得上数据时才渲染。"""
    if field_keys and ("NOTI", 561) in field_keys:
        return "\n\n" + NOTI561_DOWNGRADE_NOTE
    return ""


def _downgrade_notes(field_keys) -> str:
    return _noti1_note(field_keys) + _noti561_note(field_keys)


def repro_block(number: int) -> str:
    """「可复跑命令」节（计划 File Structure 承诺 `coverage-summary.md` 含可复跑命令）。

    命令里的路径一律**相对 work 目录**（`out` / `tests` / `out/tiers-external.tsv`）；
    只有 `cd` 那行是相对 **worktree 根**，用来指明 work 目录在哪。
    `number` 由调用方给：`--external` 时本节是 §6（前面已有 §5 对照），默认时是 §5。
    """
    return f"""## {number}. 可复跑命令（路径相对 `work/` 目录）

```powershell
$env:PYTHONIOENCODING='utf-8'
cd .superpowers/sdd/2026-10-05-gameplay-evidence-program/work   # 相对 worktree 根
python coverage_report.py --out-dir out --external out/tiers-external.tsv   # 生成 out/coverage-by-opcode.tsv（1171 条 + 表头）与本文件
python coverage_report.py --out-dir out                                     # 同一份分档，但不加 ext_mark 轴（逐条产物与 Task 2 逐字节相同）
#   ⚠️ 上面这条会用"默认态"重写 out/coverage-by-opcode.tsv（即去掉 ext_mark / ext_evidence 两列）。
#      要保留带 ext 轴的产物：把它改成 --out-dir <临时目录> 跑，或在它之后再跑一次最上面那条命令。
python -m unittest discover -s tests                                        # 20 个单测（含真产物对账，缺失即失败、不跳过）
```

对账语义：上面第一条命令打印的五条基线（源行数 / status / 目录家族数 / mapped dispatchers /
各档之和）任一不符 ⇒ 脚本**非零退出**，不静默通过；`--external` 档非法（未知 opcode / 非法 mark /
列数不对）⇒ `exit 2` 且**不落任何产物**。`$env:PYTHONIOENCODING='utf-8'` 是必须的（产物含中文）。
"""


def default_tail(defn: dict) -> str:
    """**不传** `--external` 时的尾部：给出 `ext_mark` 的定义与当前标注清单。

    读数只为**定义说明**服务（本模式不给行加 `ext_mark` 列）⇒ `coverage-by-opcode.tsv` 与 Task 2 逐字节相同。
    """
    return f"""## 4. 外部证据档状态（**独立轴**，本次运行未启用该轴）

外部证据档由**人工**标注在 `out/tiers-external.tsv`。本次未传 `--external` ⇒
`out/coverage-by-opcode.tsv` **不**输出 `ext_mark` / `ext_evidence` 两列，上表的 T3/T4 仍只表示
"handler 目录里能看到字段调用"，不表示"字段级已完成"。

{ext_definition_block(defn["byte_keys"], defn["field_keys"], defn["note"])}

{VERBATIM_EXTERNAL}

（本次未给逐条产物加 `ext_mark` 列 ⇒ 上面句中的"本表"指传 `--external` 时生成的逐条产物。）{_downgrade_notes(defn["field_keys"])}

{TAIL_NO_EXTERNAL_LEGACY_NOTE}

{repro_block(5)}"""


def _orthogonality_note(t0_byte: list, field_keys) -> str:
    """「两条轴正交」的例句，**按数据渲染**。

    降级会让某些组合变空（例如 `NOTI 561` 也降为 `field_read` 后，"T0 却 byte_verified"就没有了），
    写死的例句会退化成"（无）在 ext_mark 轴却是 byte_verified"这种假话 ⇒ 这里按实际组合组装。
    """
    bits = []
    if t0_byte:
        bits.append(f"{_fmt_keys(t0_byte)} 在 tier 轴是 `T0_no-evidence`（`status=no-dispatcher`），"
                    f"在 ext_mark 轴却是 `byte_verified`")
    t0_field = [k for k in (field_keys or []) if k[0] == "NOTI"]
    if t0_field:
        bits.append(f"{_fmt_keys(t0_field)} 在 tier 轴同样没有可达处理器，按分级门槛只到 `field_read`")
    head = "两条轴**正交**：ext_mark 不参与 tier 判定"
    body = (" —— " + "；".join(bits) + "。") if bits else "。"
    return head + body + '"没有 dispatcher"不等于"没有外部证据"。'


def external_tail(ext_counts: dict, marked: int, total: int, source: str,
                  keys: dict, t0_byte: list, defn: dict) -> str:
    """`--external` 跑过时的报告尾部：计数 + 定义（同一份）+ 两条轴对照 + 两条 `[作废]`。

    `byte_verified` / `field_read` 覆盖哪几条 opcode **从本次数据算出来**（不写死），
    这样标注一改，报告不会出现自相矛盾的旧结论。
    """
    rows = "\n".join(f"| {m} | {ext_counts[m]} |" for m in EXTERNAL_MARKS)
    return f"""## 4. 外部证据档状态（**独立轴**，不混进上表六档）

人工标注源：`{source}`（本次运行传入 `--external`）。

| ext_mark | 条数 |
|---|---|
{rows}
| **合计** | **{total}** |

被标注（出现在 `tiers-external.tsv`）的 opcode = {marked} 条；其余 {total - marked} 条
`ext_mark=name_only`（即"只有名字"）。

本次运行的 stdout 对账行：`ext_byte_verified={ext_counts["byte_verified"]}`、`ext_field_read={ext_counts["field_read"]}`、`ext_name_only={ext_counts["name_only"]}`。

{ext_definition_block(defn["byte_keys"], defn["field_keys"], defn["note"])}{_downgrade_notes(defn["field_keys"])}

{VERBATIM_EXTERNAL}

## 5. 锚点回填 vs 纯推导（对照）

| | 纯推导（tier 轴，§2） | 锚点回填（ext_mark 轴，§4） |
|---|---|---|
| 判据来源 | handlers 目录里的 `name=...::get_/put_` 调用 | **人工**标注的已落盘产物（字节级自检 / 抓包 / 静态反汇编） |
| 覆盖 | 1171 条全部 | 只有 {marked} 条有标注 |
| 能证明什么 | 只到"字段读/写调用的候选" | `byte_verified` 的那几条才有字节级产物支撑 |

{_orthogonality_note(t0_byte, keys["field_read"])}

[作废] §3 第 4 句"[未验证] 外部证据档…首版为空"写于外部证据档为空时；本任务的
       `out/tiers-external.tsv` 已回填 {marked} 行，该句的"首版为空"已被推翻。
       原文按证据纪律**保留不删**，此处指向推翻它的产物。

{repro_block(6)}"""


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
    """写 `coverage-summary.md`。四项限定语句**逐字**取自计划，不许改写措辞。

    `ext["ext_mark_counts"]` 存在（即跑过 `--external`）时，尾部是外部证据档计数 +
    两条轴的对照；否则尾部是 `ext_mark` 的**定义段**（分级门槛 + 当前清单）—— 旧文案
    "外部证据档首版为空"已被本任务推翻，故不再逐字沿用，改由 `default_tail` 生成。
    """
    tier_rows = "\n".join(f"| {t} | {summary[t]} |" for t in TIER_ORDER)
    limits = "\n".join(VERBATIM_LIMITS)
    counts_by_mark = ext.get("ext_mark_counts")
    defn = ext["ext_def"]
    if counts_by_mark is None:
        tail = default_tail(defn)
    else:
        tail = external_tail(counts_by_mark, ext["ext_marked"], summary["total"],
                             ext["ext_source"], ext["ext_keys"], ext["ext_t0_byte"], defn)
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
⚠️ 但是 `name=InterfacePacketBuf::put_header(...)` 这类**带 `name=`** 的行**会**匹配 `WRITE`；
本表 `write_count` 的口径是"写调用条数（**含**帧头构造 `put_header`）"。

## 3. 这数字不能证明什么（限定语句）

{limits}

{tail}"""
    Path(path).write_text(body, encoding="utf-8")


def double_read_problems(handler_rows: dict, skipped: int, counts, opcodes: list[dict]) -> list[str]:
    """双读一致性：`count_sources` 内部又读了一遍，两次结果必须一致（防静默丢弃）。

    **显式检查，不用 `assert`** —— `python -O` 会把 `assert` 整条剥掉，最想防的
    "双读不一致"反而会在优化模式下静默通过。本函数返回问题清单，由 `main` 打印诊断并
    非零退出（`-O` 下同样生效）。
    """
    problems: list[str] = []
    if len(handler_rows) != counts.handler_families:
        problems.append(f"家族数不一致: {len(handler_rows)} != {counts.handler_families}")
    if skipped != counts.handler_skipped:
        problems.append(f"skipped 不一致: {skipped} != {counts.handler_skipped}")
    if len(opcodes) != counts.opcode_rows:
        problems.append(f"opcode 行数不一致: {len(opcodes)} != {counts.opcode_rows}")
    return problems


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="协议字段级覆盖度盘点（离线静态）")
    ap.add_argument("--out-dir", default=str(WORK / "out"),
                    help="真产物目录（读输入 + 写输出）")
    ap.add_argument("--external", default=None,
                    help="外部证据档 TSV（列序 kind/id/mark/evidence；不传则不加 ext_mark 轴）")
    args = ap.parse_args(argv)

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

    ext_path = None
    if args.external:
        ext_path = Path(args.external)
        if not ext_path.is_absolute():
            ext_path = (WORK / ext_path).resolve()
        if not ext_path.exists():
            print(f"缺外部证据档: {ext_path}", file=sys.stderr)
            return 2

    opcodes = load_opcodes(opcode_path)
    handler_rows, skipped = load_handler_rows(handler_path)
    counts = count_sources(opcode_path, dispatcher_path, handler_path)

    problems = double_read_problems(handler_rows, skipped, counts, opcodes)
    if problems:
        for p in problems:
            print(f"✗ 双读不一致: {p}", file=sys.stderr)
        sys.exit(3)

    classified = classify(opcodes, handler_rows)
    summary = summarize(classified)
    extra = compute_extra(opcodes, set(handler_rows))
    tier_sum = sum(summary[t] for t in TIER_ORDER)

    if ext_path is not None:
        try:
            marks = load_external_marks(ext_path)
            classified = apply_external_marks(classified, marks)
        except (ValueError, KeyError) as exc:
            # 未知 opcode / 非法 mark / 列数不对 —— "带一半标注"的产物是最危险的失败形状，
            # 所以这里不落任何产物、直接非零退出（库函数本身照旧抛错，见单测）。
            print(f"外部证据档非法: {type(exc).__name__}: {exc}", file=sys.stderr)
            return 2
        ext_counts = {m: 0 for m in EXTERNAL_MARKS}
        by_mark: dict[str, list] = {m: [] for m in EXTERNAL_MARKS}
        for r in classified:
            ext_counts[r["ext_mark"]] += 1
            by_mark[r["ext_mark"]].append((r["kind"], r["id"]))
        for m in EXTERNAL_MARKS:
            by_mark[m].sort()
        extra["ext_mark_counts"] = ext_counts
        extra["ext_marked"] = len(classified) - ext_counts["name_only"]
        extra["ext_keys"] = by_mark
        # "T0 却 byte_verified" 的那几条：报告里用来证明两条轴正交（从数据算，不写死）。
        extra["ext_t0_byte"] = sorted(
            (r["kind"], r["id"]) for r in classified
            if r["ext_mark"] == "byte_verified" and r["tier"] == "T0_no-evidence")
        # 记录**调用时给的**路径（报告里不嵌入本机绝对路径）。
        extra["ext_source"] = args.external

    # `ext_mark` 的定义段两种模式都要有 ⇒ 清单也得两种模式都能拿到。不传 `--external` 时**只读一次
    # `out_dir/tiers-external.tsv` 用于**定义说明**（不给任何行加 ext 列 ⇒ by-opcode 仍逐字节等于 Task 2）；
    # 读不到就把原因写进报告，不留空白、也不假装知道。
    defn_note = ""
    if ext_path is not None:
        defn_keys = (extra["ext_keys"]["byte_verified"], extra["ext_keys"]["field_read"])
    else:
        defn_candidate = out_dir / "tiers-external.tsv"
        defn_keys = (None, None)
        if defn_candidate.exists():
            try:
                list_marks = load_external_marks(defn_candidate)
            except ValueError as exc:
                defn_note = f"外部证据档 `{defn_candidate.name}` 无法解析（{exc}），当前清单缺失"
            else:
                defn_keys = (sorted(k for k, v in list_marks.items() if v["mark"] == "byte_verified"),
                             sorted(k for k, v in list_marks.items() if v["mark"] == "field_read"))
        else:
            defn_note = f"外部证据档 `{defn_candidate}` 不存在，当前清单缺失"
    extra["ext_def"] = {"byte_keys": defn_keys[0], "field_keys": defn_keys[1], "note": defn_note}

    write_by_opcode(out_dir / "coverage-by-opcode.tsv", classified)
    write_summary(out_dir / "coverage-summary.md", counts, summary, extra)

    ok = True
    for text, good in reconcile_lines(counts, extra, tier_sum):
        print(text if good else "✗ " + text)
        ok = ok and good
    for t in TIER_ORDER:
        print(f"    {t}={summary[t]}")
    if ext_path is not None:
        for m in EXTERNAL_MARKS:
            print(f"    ext_{m}={extra['ext_mark_counts'][m]}")

    if not ok:
        print("对账失败：带 ✗ 的行与基线不符", file=sys.stderr)
        return 1
    print(f"输出: {out_dir / 'coverage-by-opcode.tsv'}")
    print(f"输出: {out_dir / 'coverage-summary.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
