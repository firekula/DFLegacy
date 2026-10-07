# 协议字段级覆盖度盘点 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给官方 1171 条 opcode 逐条标注"字段级证据档位"，产出一个**可复算**的覆盖率数字，把"能不能完全复刻官服逻辑"从定性判断变成可报数的结论。

**Architecture:** 纯离线、确定性流水线。三个既有产物（`opcode-map.tsv`、`catalog_dispatchers_full.txt`、`catalog_handlers_full.txt`）经"加载+对账 → 分档 → 落盘"，产出一份逐条 TSV 与一份汇总报告。分档**只依据产物里的结构化字段**（`status` / `dispatcher` / `source=` / `name=`），不做任何名称猜测；无法从产物判定的档位（外部证据档）走**人工标注文件**，且标注必须通过 opcode 集合校验。

**Tech Stack:** Python 3.12（本机 `D:\Program Files\Python312\python.exe`，`python` 已解析到它）；`unittest`（标准库）；`csv` / `re` / `hashlib`（标准库）。**不引入新依赖**。

**Spec:** 无独立 spec —— 本计划直接实现本轮总结里承诺的"第一步只做 ①"。其依据是：
- `docs/superpowers/handoff/2026-10-07-dynamic-evidence-handoff.md`（范围与禁令）
- `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/dynamic-evidence-progress-2026-10-07.md`（本轮 A/B/C 结论）
- `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/client-business-backlog.md`（范围冻结：9 个 inner 组合、1171 条归后续单元）
- `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/evidence-closure-signoff.md`（CLOSED_WITH_LIMITS 的边界）

## Global Constraints

以下为项目硬约束，**每个任务的要求都隐含包含本节**（值逐字抄自上列文档）：

- 证据六类标记固定为 `[静态]`/`[抓包]`/`[真机]`/`[回放]`/`[推断]`/`[未验证]`，外加 `[作废]`；**被推翻的结论不删原文**，标 `[作废]` 并指向推翻它的证据。
- **不许 TBD / TODO / 模糊表述**写进结论。
- **一个结论要有两条独立来源**才算 `[静态]`/`[抓包]` 级。
- **不得扩大范围到全部 1171 条 opcode 的行为**（handoff §6 明令）；本计划**只做覆盖度盘点，不读任何 handler 函数体**。
- 不修改已签收的 `2026-10-06-gameplay-evidence-closure` 计划。
- 工作区 `G:\Game\DNF_ACT1\DFLegacy`，分支 `main`。**提交必须带 pathspec**（`git commit -m "..." -- <paths>`）；`.superpowers/` 与 `docs/` 被 gitignore，新增文件要 `git add -f`。**绝不 `git add -A`**。
- Python 读文本**显式 `encoding="utf-8"`**；跑 python 前设 `$env:PYTHONIOENCODING='utf-8'`。
- 落盘一律 `>` 重定向；**不要**用 `python x.py | Tee-Object out.txt | Select-Object -First N`（`-First` 会截断落盘文件）。
- 只用 Git Bash（`D:\Program Files\Git\bin\bash.exe`）跑 `.sh`；**不运行客户端、不挂调试器、不写产品代码**。
- 本计划的产物是**机器产物**，进 `work/out/`，不进 git（与既有惯例一致；如需入库单独决定）。

---

## 关键源事实（写代码时按这些数字对账，不要凭印象）

| 源文件（相对 `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/`） | 行数 | 结构 |
|---|---|---|
| `opcode-map.tsv` | **1172**（1 表头 + **1171** 数据） | `kind \t id \t name \t rodata_va \t dispatcher \t status`；`kind∈{CMD,NOTI}` |
| `catalog_dispatchers_full.txt` | **440**（表头 15 行注释 + **425** 家族） | `family \t process=0xADDR( SIZE) \t dispatch_sig=0xADDR( SIZE) \t spell=...` |
| `catalog_handlers_full.txt` | **7293**（表头 20 行注释 + **7273** 行） | `family \t handler=0xADDR(SIZE) \t source=<dispatch_sig\|put_header> \t name=<符号名>` |

已实测的对账基线（本计划的任务 1 会复算并断言它们）：

- `opcode-map.tsv`：`CMD` **608** + `NOTI` **563** = 1171；`status` = `mapped` **428** / `no-dispatcher` **741** / `unused` **2**。
- `CMD` 里：`mapped` **428**，`no-dispatcher` **178**，`unused` **2**；**`NOTI` 全部 563 条是 `no-dispatcher`**。
- `mapped` 且有 `dispatcher` 值的行为 **428**，指向 **423** 个不同 dispatcher 名；其中 **186** 个 dispatcher **不在** handlers 目录里。
- `catalog_handlers_full.txt`：非注释行 **7273**，`split("\t")` 后字段数 <3 的行 = **0**（无静默丢弃）；家族 **239** 个；`source` 分布 = `dispatch_sig` **6025** / `put_header` **1248**；含 `name=PacketBuf::get_` 的行 **634**，含 `name=InterfacePacketBuf::put_` 或 `name=PacketBuf::put_` 的行 **565**。
- 两张目录的家族名交集 **238**；handlers 目录独有的家族名只有 1 个，是伪条目 `(sender)`。
- `catalog_dispatchers_full.txt` 自述：`家族数 425；dispatch_sig 242；process 424；两者皆无 1；仅由 Arad 拼写贡献 1`；家族 `BuyItemCheck` 两者皆无。

## 档位词表（**先定这个，再写代码**；一次只落一档，互斥且穷尽）

对**每一条 opcode**（`(kind,id)` 唯一）恰好落一档：

| 档 | 名字 | 判定（只用产物字段） | 含义 |
|---|---|---|---|
| **T0** | `no-evidence` | `status != "mapped"` 或 `dispatcher` 为空 | 只有名字，没有可达处理器 |
| **T1** | `dispatcher-no-handlerdata` | `dispatcher` 非空，且该家族**不在** handlers 目录 | 有家族名，但**没有** handlers 目录的字段调用数据 |
| **T2** | `handlerdata-no-fields` | 家族在 handlers 目录，但该家族**既无**读调用**也无**写调用 | 有处理器数据，但看不到字段读/写 |
| **T3** | `fields-read-only` | 家族有读调用、无写调用 | 能看到字段**读**序列（字段级证据的**候选**） |
| **T4** | `fields-read+write` | 家族**同时**有读调用与写调用 | 收发两侧都有字段调用 |

判定用的正则（**只用 `name=` 字段**，不匹配整行，避免撞上路径/注释）：

```
READ  = r"name=PacketBuf::get_"
WRITE = r"name=(?:InterfacePacketBuf|PacketBuf)::put_"
```

排序与 `read_count` / `write_count` 只用于报告展示，**不参与分档**。

**外部证据档（T5）另走人工标注**，不混进上面五档：`tiers-external.tsv` 由人（或 `ida-dec`/`get_str` 链）写入 `opcode有字段级证据` 的行，格式 `kind \t id \t mark \t evidence`，`mark∈{byte_verified, field_read, name_only}`。程序只做**校验与合并**，不猜。

## File Structure

| 文件 | 责任 |
|---|---|
| `work/coverage_tier.py` | **纯逻辑库**：加载三源 + 对账 + 分档。无 I/O 副作用，可单测 |
| `work/tests/test_coverage_tier.py` | 上面那个库的单元测试（含黄金小夹具，不依赖 54 MB 产物） |
| `work/coverage_report.py` | **CLI**：调用库 → 写 `coverage-by-opcode.tsv` + `coverage-summary.md` |
| `work/out/coverage-by-opcode.tsv` | 逐条产物（1171 行 + 表头） |
| `work/out/coverage-summary.md` | 汇总报告（含可复跑命令与对账断言结果） |
| `work/out/tiers-external.tsv` | **人工**外部证据标注（首版留空，只有表头） |
| `work/tests/fixtures/` | 黄金小夹具（3 个极小文件） |

**为什么拆成"库 + CLI"**：分档规则是这份计划里唯一会被反复改的东西，必须能在毫秒级单测里验证；CLI 只是把结果落盘。54 MB 的官方产物**不进单测**。

---

### Task 1: 源加载与对账（库 + 黄金夹具单测）

**Files:**
- Create: `work/coverage_tier.py`
- Create: `work/tests/test_coverage_tier.py`
- Create: `work/tests/fixtures/opcode-map.tsv`
- Create: `work/tests/fixtures/catalog_dispatchers_full.txt`
- Create: `work/tests/fixtures/catalog_handlers_full.txt`

**Interfaces:**
- Consumes: 无（第一个任务）
- Produces:
  - `load_opcodes(path: Path) -> list[dict]`，每项键固定为 `kind:str, id:int, name:str, rodata_va:str, dispatcher:str, status:str`
  - `load_dispatcher_families(path: Path) -> set[str]`
  - `load_handler_rows(path: Path) -> tuple[dict[str, list[str]], int]`（家族名 → 原始行列表，**保序**；第二个值是"字段数<3 被跳过的行数"，调用方必须断言）
  - `SourceCounts` dataclass（**字段清单以此为准**）：`opcode_rows:int, cmd:int, noti:int, mapped:int, no_dispatcher:int, unused:int, dispatcher_families:int, handler_families:int, handler_rows:int, handler_skipped:int`
    —— `handler_skipped` 是"`split("\t")` 后字段数 <3 的行数"，**必须有**，因为调用方要断言它（静默丢弃是本项目最警惕的失败形状）

- [ ] **Step 1: 写夹具（3 个极小文件，覆盖每一档各一例）**

`work/tests/fixtures/opcode-map.tsv`（制表符分隔，**表头必须与真文件一致**）：

```
kind	id	name	rodata_va	dispatcher	status
CMD	1	LOGIN	0x08aeb9f8	Login	mapped
CMD	2	SET_UDP_IP_PORT	0x08aeba10	SetIP	mapped
CMD	3	EXIT	0x08aeba2f	Exit	mapped
CMD	4	SELECT_CHARACTER	0x08aeba44	SelectCharac	mapped
CMD	5	LONELY_NO_HANDLER	0x08aeba60	NoHandlerData	mapped
CMD	6	LONELY_NO_DISPATCHER	0x08aeba80		no-dispatcher
NOTI	7	DONT_CARE	0x08aebaa0		no-dispatcher
CMD	8	UNUSED_ONE	0x08aebac0		mapped
```

`work/tests/fixtures/catalog_dispatchers_full.txt`：

```
# 夹具：家族数 5
Login	process=0x081e8c78(4619)	dispatch_sig=0x08232a74(  10)	spell=DisPatcher_Login
SetIP	process=0x081b0000( 100)	dispatch_sig=0x081b0100( 200)	spell=DisPatcher_SetIP
Exit	process=0x081b0200( 100)	dispatch_sig=0x081b0300( 200)	spell=DisPatcher_Exit
SelectCharac	process=0x081b0400( 100)	dispatch_sig=0x081b0500( 200)	spell=DisPatcher_SelectCharac
NoHandlerData	process=0x081b0600( 100)	dispatch_sig=0x081b0700( 200)	spell=DisPatcher_NoHandlerData
```

`work/tests/fixtures/catalog_handlers_full.txt`（**每个家族至少 1 行**，共 **7 行** / 4 家族）：

```
# 夹具
Login	handler=0x08232a74(  10)	source=dispatch_sig	name=PacketBuf::get_int(unsigned long&)
Login	handler=0x085908d7(  83)	source=dispatch_sig	name=LineFunc(int, char const*, int, unsigned int)
Login	handler=0x0858d2bc( 100)	source=dispatch_sig	name=InterfacePacketBuf::put_str(char const*, int)
SetIP	handler=0x081b0100( 200)	source=dispatch_sig	name=PacketBuf::get_byte(unsigned char&)
Exit	handler=0x081b0300( 200)	source=dispatch_sig	name=InterfacePacketBuf::put_byte(unsigned char)
SelectCharac	handler=0x081b0500( 200)	source=dispatch_sig	name=PacketBuf::get_str(char*, int, int)
SelectCharac	handler=0x081b0600( 200)	source=put_header	class=1	cmd=4
```

**夹具的设计意图（每一档各一例，便于 Task 2 断言）**：

| 夹具家族 | 读调用 | 写调用 | 期望档 |
|---|---|---|---|
| `Login` | 1（`get_int`） | 1（`put_str`） | **T4** |
| `SetIP` | 1（`get_byte`） | 0 | **T3** |
| `Exit` | 0 | 1（`put_byte`） | **T3b** |
| `SelectCharac` | 1（`get_str`） | 0（`put_header` **不算**字段写） | **T3** |
| （没有 `NoHandlerData` 的家族行） | — | — | **T1** |

⚠️ **注意 `SelectCharac` 那行 `source=put_header`**：它是"构造帧头"，**不是**字段写入。
写判定只认 `name=...::put_` ⇒ `SelectCharac` 落 **T3**（这也正是 Task 2 Step 1→2 要纠正的那条测试）。

- [ ] **Step 2: 把这一步的结论落进模块 docstring**

Task 1 的 `coverage_tier.py` docstring 里**已经**写死了含 `T3b` 的完整档序
（见 Step 5 的代码）—— 因为词表缺口是在本计划起草时发现的，**不是**执行时才发现。
本步骤只做确认：`python -c "import coverage_tier as c; print(c.TIER_ORDER)` 打印出的 6 档
与计划开头那张表逐字一致；不一致就改代码使之一致，**不许留下两处真相源**。

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work
python -c "import coverage_tier as c; print(c.TIER_ORDER)"
```
Expected: `('T0_no-evidence', 'T1_dispatcher-no-handlerdata', 'T2_handlerdata-no-fields', 'T3b_fields-write-only', 'T3_fields-read-only', 'T4_fields-read+write')`

- [ ] **Step 3: 写失败测试（对账数字 + 每一档各一例）**

`work/tests/test_coverage_tier.py`：

```python
import unittest
from pathlib import Path

from coverage_tier import (SourceCounts, count_sources, load_dispatcher_families,
                           load_handler_rows, load_opcodes)

FIX = Path(__file__).resolve().parent / "fixtures"


class TestLoadAndReconcile(unittest.TestCase):
    def test_load_opcodes_has_exact_keys_and_types(self):
        rows = load_opcodes(FIX / "opcode-map.tsv")
        self.assertEqual(len(rows), 8)
        self.assertEqual(
            set(rows[0]),
            {"kind", "id", "name", "rodata_va", "dispatcher", "status"},
        )
        self.assertEqual(rows[0]["kind"], "CMD")
        self.assertEqual(rows[0]["id"], 1)          # int，不是 str
        self.assertEqual(rows[0]["name"], "LOGIN")
        self.assertEqual(rows[0]["dispatcher"], "Login")

    def test_blank_dispatcher_becomes_empty_string_not_none(self):
        rows = load_opcodes(FIX / "opcode-map.tsv")
        lonely = [r for r in rows if r["name"] == "LONELY_NO_DISPATCHER"]
        self.assertEqual(len(lonely), 1)
        self.assertEqual(lonely[0]["dispatcher"], "")

    def test_handler_rows_preserve_order_and_drop_comments(self):
        fams, skipped = load_handler_rows(FIX / "catalog_handlers_full.txt")
        self.assertEqual(skipped, 0)
        self.assertNotIn("#", fams)
        self.assertEqual(list(fams)[:2], ["Login", "SetIP"])
        self.assertEqual(len(fams["Login"]), 3)
        self.assertIn("PacketBuf::get_int", fams["Login"][0])   # 保序：第一行仍是第一行

    def test_counts_match_fixture(self):
        c = count_sources(
            FIX / "opcode-map.tsv",
            FIX / "catalog_dispatchers_full.txt",
            FIX / "catalog_handlers_full.txt",
        )
        self.assertIsInstance(c, SourceCounts)
        self.assertEqual(c.opcode_rows, 8)
        self.assertEqual(c.cmd, 7)
        self.assertEqual(c.noti, 1)
        self.assertEqual(c.mapped, 6)
        self.assertEqual(c.no_dispatcher, 2)
        self.assertEqual(c.unused, 0)
        self.assertEqual(c.dispatcher_families, 5)
        self.assertEqual(c.handler_families, 4)
        self.assertEqual(c.handler_rows, 7)      # Login 3 + SetIP 1 + Exit 1 + SelectCharac 2
        self.assertEqual(c.handler_skipped, 0)

    def test_counts_on_real_sources_match_documented_baseline(self):
        """真产物对账（**不**在夹具上做）：静默丢弃只有在这条断言上才抓得到。"""
        real = Path(
            r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work\out"
        )
        if not (real / "opcode-map.tsv").exists():
            self.skipTest("真产物不在本机（可选断言）")
        c = count_sources(real / "opcode-map.tsv",
                          real / "catalog_dispatchers_full.txt",
                          real / "catalog_handlers_full.txt")
        self.assertEqual(c.opcode_rows, 1171)
        self.assertEqual(c.cmd, 608)
        self.assertEqual(c.noti, 563)
        self.assertEqual(c.mapped, 428)
        self.assertEqual(c.no_dispatcher, 741)
        self.assertEqual(c.unused, 2)
        self.assertEqual(c.dispatcher_families, 425)
        self.assertEqual(c.handler_families, 239)
        self.assertEqual(c.handler_rows, 7273)
        self.assertEqual(c.handler_skipped, 0)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 4: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work
python -m unittest discover -s tests -v 2>&1 | Select-Object -First 20
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'coverage_tier'`

- [ ] **Step 5: 写最小实现 `work/coverage_tier.py`**

```python
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
  * **两个分母不要混**（实测，见下）：handlers 目录只覆盖 **239** 个家族，而
    `catalog_dispatchers_full.txt` 有 **425** 个家族、"mapped opcode 里出现过的 dispatcher" 只有 **423** 个。
    ⇒ 论"**mapped opcode 的**覆盖"用 **423** 这个分母（423 − 237 = **186** 个没有字段调用数据）；
      论"**全部 dispatcher 家族**的覆盖"用 **425** 这个分母（425 − 238 = **187**）。
      两个数字都对，**但必须各自说清分母**，否则读者会以为是同一个数变了。
  * 另外 `catalog_handlers_full.txt` 里有一个**合成家族** `(sender)`（**1248** 行，正是 `packet_senders.txt`
    的 `put_header` 行），它**不在** dispatchers 目录里 ⇒ 家族交集是 238 而非 239。
    统计时不要把它当成一个真实家族。
"""
from __future__ import annotations

import csv
import re
from dataclasses import dataclass
from pathlib import Path

READ = re.compile(r"name=PacketBuf::get_")
WRITE = re.compile(r"name=(?:InterfacePacketBuf|PacketBuf)::put_")

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
```

- [ ] **Step 6: 跑测试确认通过**

Run: 同 Step 4
Expected: `OK`（**5 个**测试全过 —— Task 1 Step 3 的代码栅栏里就是 5 个 `test_*` 方法；
本行原先误写"8 个"，已按实测与审查裁决更正）

- [ ] **Step 7: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/
git commit -m "盘点：覆盖度分档库与源对账（含黄金夹具）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/
```

---

### Task 2: 分档实现与逐条落盘

**Files:**
- Modify: `work/coverage_tier.py`（追加分档函数，**不改** Task 1 的既有函数签名）
- Modify: `work/tests/test_coverage_tier.py`（追加分档测试）
- Create: `work/coverage_report.py`
- Create: `work/out/coverage-by-opcode.tsv`
- Create: `work/out/coverage-summary.md`

**Interfaces:**
- Consumes: Task 1 的 `load_opcodes` / `load_dispatcher_families` / `load_handler_rows` / `count_sources` / `SourceCounts`
- Produces:
  - `classify(opcodes: list[dict], handler_rows: dict[str, list[str]]) -> list[dict]`，每项在 opcode 六个键之外追加 `tier:str, read_count:int, write_count:int`
  - `TIER_ORDER: tuple[str, ...]` = `("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields", "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write")`
  - `summarize(classified: list[dict]) -> dict`（`{tier: 条数}`，且含 `total`）
  - `write_by_opcode(path: Path, classified: list[dict]) -> None`
  - `write_summary(path: Path, counts: SourceCounts, summary: dict, ext: dict) -> None`

- [ ] **Step 1: 写失败测试（分档：夹具里每一档各一例 + 总数守恒）**

追加到 `work/tests/test_coverage_tier.py`：

```python
from coverage_tier import (TIER_ORDER, classify, load_dispatcher_families,
                           load_handler_rows, load_opcodes, summarize)


class TestClassify(unittest.TestCase):
    def setUp(self):
        self.rows = load_opcodes(FIX / "opcode-map.tsv")
        self.fams, _skipped = load_handler_rows(FIX / "catalog_handlers_full.txt")
        self.got = {r["name"]: r for r in classify(self.rows, self.fams)}

    def test_every_opcode_gets_exactly_one_tier_from_the_vocabulary(self):
        self.assertEqual(len(self.got), len(self.rows))
        for name, r in self.got.items():
            self.assertIn(r["tier"], TIER_ORDER, name)

    def test_login_is_read_plus_write(self):
        r = self.got["LOGIN"]
        self.assertEqual(r["tier"], "T4_fields-read+write")
        self.assertGreaterEqual(r["read_count"], 1)
        self.assertGreaterEqual(r["write_count"], 1)

    def test_setip_is_read_only(self):
        self.assertEqual(self.got["SET_UDP_IP_PORT"]["tier"], "T3_fields-read-only")
        self.assertEqual(self.got["SET_UDP_IP_PORT"]["write_count"], 0)

    def test_exit_is_write_only(self):
        self.assertEqual(self.got["EXIT"]["tier"], "T3b_fields-write-only")
        self.assertEqual(self.got["EXIT"]["read_count"], 0)

    def test_select_character_is_t4_because_put_header_counts_as_write(self):
        self.assertEqual(self.got["SELECT_CHARACTER"]["tier"], "T4_fields-read+write")

    def test_lonely_no_handler_is_t1(self):
        self.assertEqual(self.got["LONELY_NO_HANDLER"]["tier"],
                         "T1_dispatcher-no-handlerdata")

    def test_lonely_no_dispatcher_is_t0(self):
        self.assertEqual(self.got["LONELY_NO_DISPATCHER"]["tier"], "T0_no-evidence")

    def test_noti_with_blank_dispatcher_is_t0(self):
        self.assertEqual(self.got["DONT_CARE"]["tier"], "T0_no-evidence")

    def test_unused_with_blank_dispatcher_is_t0(self):
        self.assertEqual(self.got["UNUSED_ONE"]["tier"], "T0_no-evidence")

    def test_summary_total_equals_row_count(self):
        s = summarize(list(self.got.values()))
        self.assertEqual(s["total"], len(self.rows))
        self.assertEqual(sum(v for k, v in s.items() if k != "total"), len(self.rows))
```

> 说明：`SELECT_CHARACTER` 在夹具里有一行 `source=put_header`（**没有** `name=` 字段）。
> 本测试的期望是 **T4**，因为写判定只看 `name=...::put_`，而 `put_header` 行**不**匹配 `WRITE`。
> ⇒ 该夹具样本**实测会落 T3**（只有 `get_str` 读）。**因此这条测试的期望必须改成 T3**，
> 并在报告里记明"`source=put_header` 不算字段写"这一判据。

- [ ] **Step 2: 修正上一步的期望值（先跑再改，不留矛盾）**

先把 `test_select_character_is_t4_because_put_header_counts_as_write` 改名为
`test_put_header_alone_is_not_a_field_write` 并把期望改成 `"T3_fields-read-only"`，
理由写进注释：`put_header` 是"构造帧头"，**不是**字段写入；字段写只认 `name=...::put_`。

- [ ] **Step 3: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work
python -m unittest discover -s tests -v 2>&1 | Select-Object -First 25
```
Expected: FAIL —— `ImportError: cannot import name 'classify'`

- [ ] **Step 4: 实现分档（追加到 `work/coverage_tier.py`）**

```python
TIER_ORDER: tuple[str, ...] = (
    "T0_no-evidence",
    "T1_dispatcher-no-handlerdata",
    "T2_handlerdata-no-fields",
    "T3b_fields-write-only",
    "T3_fields-read-only",
    "T4_fields-read+write",
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
```

- [ ] **Step 5: 跑测试确认通过**

Run: 同 Step 3
Expected: `OK`（Task1 的 5 个 + 本任务 10 个 = **15** 个全过；原写"18"是笔误，已按实测更正）

- [ ] **Step 6: 写真库产物并当场对账（**这一步的断言是这份计划的验收核心**）**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work"
cd $W
python coverage_report.py --out-dir out 2>&1 | Select-Object -First 40
```
Expected（逐条对上，对不上就**不许**往下走）：
```
CMD=608 NOTI=563 total=1171
mapped=428 no-dispatcher=741 unused=2
dispatcher_families=425 handler_families=239 handler_rows=7273 handler_skipped=0
mapped_dispatchers=423 mapped_dispatchers_without_handlerdata=186
tier_total=1171  (各档之和必须等于 1171)
```

- [ ] **Step 7: 把汇总写进 `work/out/coverage-summary.md`（含"这数字不能证明什么"）**

报告必须包含四节，且**必须**有下列句子（逐字）：

```
[静态] 本覆盖率的分母 = opcode-map.tsv 的 1171 条 (CMD 608 / NOTI 563)。
[静态] T3/T4 只表示"能在 handlers 目录里看到字段读/写调用"，不等于"已读出有序字段规格"；
       字段布局仍须逐家族反编译读取（不在本计划范围）。
[静态] catalog_handlers_full.txt 只覆盖 239/425 个 dispatcher 家族，而"mapped opcode 用到的
       dispatcher"有 423 个 ⇒ 其中 186 个家族没有字段调用数据，落到 T1 的 opcode 是 187 条
       （差 1 因为 StartGame 家族有 2 条 mapped opcode）；这是"没有字段调用数据"，不是"没有字段"。
[未验证] 外部证据档（byte_verified / field_read）首版为空；在它有内容之前，
       本覆盖率**不得**被引用为"字段级已完成多少"。
```

> ⚠️ 第三句原稿写的是"T1 的 186 条"，**186 是家族数、187 才是 opcode 条数**，
> 两个数混用会把读者引到一个偏小的分母上。Task 2 实测校正为上面这版
> （`StartGame` 家族有 2 条 mapped opcode ⇒ 187 − 186 = 1）。

- [ ] **Step 8: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_report.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/ \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-by-opcode.tsv \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-summary.md
git commit -m "盘点：1171 条 opcode 字段级覆盖度分档与汇总" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_report.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/ \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-by-opcode.tsv \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-summary.md
```

---

### Task 3: 外部证据档（人工标注的校验与合并）+ 已知锚点回填

**Files:**
- Modify: `work/coverage_tier.py`（追加 `load_external_marks` / `apply_external_marks`）
- Modify: `work/tests/test_coverage_tier.py`
- Modify: `work/tests/fixtures/opcode-map.tsv`（**不需要**改；外部标注走独立 TSV）
- Create: `work/out/tiers-external.tsv`
- Modify: `work/coverage_report.py`
- Modify: `work/out/coverage-summary.md`

**Interfaces:**
- Consumes: Task 2 的 `classify` / `TIER_ORDER` / `summarize`
- Produces:
  - `load_external_marks(path: Path) -> dict[tuple[str, int], dict]`，值含 `mark:str, evidence:str`
  - `apply_external_marks(classified: list[dict], marks: dict) -> list[dict]`（追加 `ext_mark`/`ext_evidence`，**未知 (kind,id) 抛错**）
  - `EXTERNAL_MARKS: tuple[str, ...]` = `("byte_verified", "field_read", "name_only")`

- [ ] **Step 1: 写失败测试（校验：未知 opcode 必须抛错；已知锚点必须被标上）**

```python
from coverage_tier import EXTERNAL_MARKS, apply_external_marks, load_external_marks


class TestExternalMarks(unittest.TestCase):
    def test_unknown_opcode_raises(self):
        marks = {("CMD", 99999): {"mark": "field_read", "evidence": "x"}}
        rows = load_opcodes(FIX / "opcode-map.tsv")
        with self.assertRaises(KeyError):
            apply_external_marks(rows, marks)

    def test_bad_mark_value_raises(self):
        marks = {("CMD", 1): {"mark": "probably", "evidence": "x"}}
        rows = load_opcodes(FIX / "opcode-map.tsv")
        with self.assertRaises(ValueError):
            apply_external_marks(rows, marks)

    def test_known_login_mark_is_applied(self):
        fams, _ = load_handler_rows(FIX / "catalog_handlers_full.txt")
        rows = classify(load_opcodes(FIX / "opcode-map.tsv"), fams)
        out = apply_external_marks(
            rows, {("CMD", 1): {"mark": "byte_verified", "evidence": "login-plain 10/10"}})
        login = [r for r in out if r["kind"] == "CMD" and r["id"] == 1][0]
        self.assertEqual(login["ext_mark"], "byte_verified")

    def test_unmarked_rows_get_name_only(self):
        rows = load_opcodes(FIX / "opcode-map.tsv")
        out = apply_external_marks(rows, {})
        self.assertTrue(all(r["ext_mark"] == "name_only" for r in out))
```

- [ ] **Step 2: 跑测试确认失败**

Run: 同 Task 2 Step 3
Expected: FAIL —— `ImportError: cannot import name 'EXTERNAL_MARKS'`

- [ ] **Step 3: 实现 + 建 `work/out/tiers-external.tsv`（只回填**已有硬证据**的行）**

实现要点：`load_external_marks` 读 TSV（跳过 `#`），要求列 `kind,id,mark,evidence`；
`apply_external_marks` 用 `(kind,int(id))` 建索引，**未知键 `raise KeyError(键)`**，
`mark not in EXTERNAL_MARKS` 时 `raise ValueError(mark)`，未标注的行填 `"name_only"`。

首版 `work/out/tiers-external.tsv` **只写这 5 行**（每条都指向本项目已固定的产物）：

```
# kind	id	mark	evidence
CMD	1	byte_verified	work/out/login-plain-dump.json 10/10 自检 PASS；CURSOR=388/PLAIN_LEN=400（verify-login-tail.txt）
CMD	1	field_read	login-tail-static.txt：Dispatcher_MouseRegister/decryptPassword/checkValiedPassword/PacketBuf::get_*
NOTI	1	byte_verified	work/out/noti1-evidence-locked.txt；334 B key + channel_name/seed/random 字段序（cipher-callchain-audit.txt L3342-3517）
NOTI	561	byte_verified	work/out/noti561-closure.md（57 帧：54 type17 + 3 type19）
CMD	1	byte_verified	work/out/static-analysis-vm-login-2026-10-06.md：频道 id10/id3 明文（zlib(AES)）逐字节一致
```

> ⚠️ 同一个 `(kind,id)` **允许多行**（不同证据各自一行）；`load_external_marks` 取**最强**的 `mark`
> （强度序 `byte_verified > field_read > name_only`）并把 `evidence` 用 `" | "` 合并。

> **`[作废]` 上面这份"首版 5 行"是历史快照，已被两轮裁决改写 —— 原文保留不删：**
> - `NOTI 1` 的 `byte_verified` → **`field_read`**（依据见下面 Step 5 的裁决段；落地于 `e93a1fe`）；
> - `NOTI 561` 的 `byte_verified` → **`field_read`**（依据：`out/noti561-closure.md` 自述"54 个内层业务字段语义**已降级**"，
>   密码/封装闭合不等于字段被逐字段钉住；落地于最终 fix wave）；
> - `CMD 1` 的 "`login-plain-dump.json` 10/10 自检 PASS" → 收窄为"**产出时 10 对全部自检 PASS，本仓库可复跑 2 对**
>   （`captures/streams-live`、`captures/streams-live2`）"；其余 8 对引用的 `captures/streams`(2)、
>   `captures/streams-e2e`(5)、`captures/streams-vm`(1) **目录未落盘**（同样落地于最终 fix wave）。
>
> **当前生效值一律以 `work/out/tiers-external.tsv` 为准。**

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Task 2 Step 3
Expected: `OK`（**20** 个全过 = Task1 的 5 + Task2 的 10 + Task3 的 4 + 实现者为"多行合并"补的 1；
原写"18 + 4 = 22"是把 Task 1 的 5 误记成 8，已按实测更正）

- [ ] **Step 5: 重生成产物并核对"锚点确实被标上"**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work"
cd $W
python coverage_report.py --out-dir out --external out/tiers-external.tsv 2>&1 | Select-Object -First 40
Select-String -Path out/coverage-by-opcode.tsv -Pattern '^CMD\t1\t' | ForEach-Object { $_.Line }
```
Expected：
- 汇总里出现 **`ext_byte_verified=1` / `ext_field_read=2` / `ext_name_only=1168`**
  （`byte_verified` 只覆盖 **`CMD 1`**；**`NOTI 1` 与 `NOTI 561` 都是 `field_read`** ——
  见下方裁决与最终 fix wave 的 F-1）；**仍然按实际输出核对，不要预设数字**。
  > 口径说明（**行 ≠ opcode**）：上表统计的是**逐条产物里的 opcode 条数**（1171 行 = 1171 个 opcode），
  > 不是 `tiers-external.tsv` 的**标注行数**（5 行）。`CMD 1` 在标注里有 2 行 `byte_verified` +
  > 1 行 `field_read`，合并后仍是**一个** opcode ⇒ `byte_verified` 记 **1**。本行的期望值曾按
  > "5 行标注"写成 `2 / 3 / 1166`，与实测不符，已按实测更正为 `1 / 2 / 1168`（三者之和仍是 1171）。
  > `[作废]` 本节更早的原写是 "`ext_field_read=1` / `ext_name_only=1168`
  > （`byte_verified` 只覆盖 `CMD 1` 与 `NOTI 561`）"，已被 `NOTI 561` 的降级推翻
  > （`out/noti561-closure.md` 自述 54 个内层业务字段语义"已降级"）；原数字保留在此行，
  > 当前生效值以 `work/out/tiers-external.tsv` 与产物为准。
- **`byte_verified` 的门槛（裁决，写进产物）**：该 opcode 的**线上字节被逐字段钉住并落盘可复跑**。
  `NOTI 1` 目前只有"静态字段序（`cipher-callchain-audit.txt L3342-3517` 的 `CUser::InitOnAccept` →
  `put_binary(334)`/`get_channel_name`/seed/random）+ 抓包两样本前 334 B 相同"，
  **没有**把 334 B 载荷逐字段对上；且项目自己两处反向记录
  （`cipher-audit-corrections.md:70` 的 `[作废]`、`dynamic-evidence-progress-2026-10-07.md:368` 的
  "[真机] 未取的：334 B key 本体"）⇒ 它够 `field_read`，不够 `byte_verified`。
  **代价（若错）**：`NOTI 1` 的证据等级被低估一档，引用时偏保守。
- **`NOTI 561` 的门槛复核（最终 fix wave 的 F-1，追加裁决）**：同样**不满足** `byte_verified`。
  `out/noti561-closure.md` 自述："类型17密码/封装 **已闭合**；类型19密码/封装结构 **已闭合**，
  BinCount=5 的业务作用 **已降级**；**54 个内层业务字段语义已降级**" ⇒ 它闭合的是**密码/封装**，
  不是"线上字节被逐字段钉住"⇒ 降为 **`field_read`**（`evidence` 保留 `57/54/3` 三个数字并补记该自述限定）。
  降级后 `byte_verified` **只覆盖 `CMD 1`**；实测 **`ext_byte_verified=1` / `ext_field_read=2` / `ext_name_only=1168`**
  （按**opcode 条数**计，不是按 `tiers-external.tsv` 的标注行数；三者之和 = 1171）。
- `CMD 1` 那行 `ext_mark=byte_verified`，且 `evidence` 含两个来源（`|` 合并）。

- [ ] **Step 6: 在 `coverage-summary.md` 追加"锚点回填 vs 纯推导"的对照小节**

必须含这句（逐字）：
```
[静态] 本表 ext_mark=byte_verified 的条目，其 evidence 全部指向本项目已落盘产物；
       未出现在 tiers-external.tsv 的 opcode 一律 ext_mark=name_only（即"只有名字"）。
```

- [ ] **Step 7: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_report.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/ \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/tiers-external.tsv \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-by-opcode.tsv \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-summary.md
git commit -m "盘点：外部证据档标注与合并（含已有硬证据锚点回填）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_tier.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/coverage_report.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/tests/ \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/tiers-external.tsv \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-by-opcode.tsv \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-summary.md
```

---

### Task 4: 独立复核（**必须由没参与前三个任务的人做**）

**Files:**
- Create: `work/out/coverage-review.md`

**Interfaces:**
- Consumes: Task 2/3 的 `work/out/coverage-by-opcode.tsv`、`work/out/coverage-summary.md`、`work/coverage_tier.py`
- Produces: `work/out/coverage-review.md`（复核结论；发现问题就回填 Task 2/3 并重跑）

- [ ] **Step 1: 用**独立命令**复算三个总数（不许复用 `coverage_report.py`）**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work\out"
# 分母
(Get-Content "$O\opcode-map.tsv").Count
# 分档行数（去掉表头）
(Get-Content "$O\coverage-by-opcode.tsv").Count
# 各档计数（用独立的一行命令，不复用报告脚本）
Get-Content "$O\coverage-by-opcode.tsv" | Select-Object -Skip 1 | ForEach-Object { ($_ -split "`t")[5] } | Group-Object | Sort-Object Count -Descending | Select-Object Count,Name
```
Expected：分母 1172（含表头）；分档表 1172（含表头）；各档之和 = 1171。

- [ ] **Step 2: 抽样反查（**至少 5 条，且每档至少 1 条**）**

对每条被抽到的 opcode，到 `catalog_handlers_full.txt` 里用家族名 grep 出它的行，**手数** 读/写调用条数，
与 `coverage-by-opcode.tsv` 的 `read_count`/`write_count` 对齐。把 5 条对照逐条写进 `coverage-review.md`。

- [ ] **Step 3: 抽一条走**另一条独立路径**验证分档语义**

取 `CMD 1`（LOGIN），用既有 `work/verify-login-tail.py` 重跑，确认输出里有
`CURSOR=388 PLAIN_LEN=400 UNCONSUMED=12 UNCONSUMED_ALL_ZERO=True`。
这条证明"LOGIN 的 `ext_mark=byte_verified` 不是自封的"，而是有可复跑的字节级产物支撑。

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work
python verify-login-tail.py 2>&1 | Select-String -Pattern 'streams-live\\2343-10013' -Context 0,2
```
Expected：出现 `CURSOR=388 PLAIN_LEN=400`。

- [ ] **Step 4: 写 `work/out/coverage-review.md` 并给出**通过与不通过**的明确结论**

必须含：三条独立复算结果、5 条抽样对照表、`CMD 1` 的独立路径结果，
以及一句总结（逐字模板）：
```
复核结论：<通过|不通过>。独立复算的三个总数与 coverage-summary.md 一致；抽样 5 条逐条吻合；
CMD 1 的 byte_verified 有可复跑字节级证据。未发现静默丢弃或档位越界。
```

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-review.md
git commit -m "盘点：覆盖率独立复核结论" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-review.md
```

---

## 明确不做（防止范围蔓延）

- **不读任何 handler 函数体**（不反编译、不追 `PacketBuf::get_*` 的调用顺序）。那是第二步（批量读处理器体），需要单独计划。
- **不动 1171 条 opcode 的行为分析**（handoff §6 明令）。
- **不引入新依赖**（不加 `pefile`/`capstone` 到这条流水线——本计划不需要它们）。
- **不修改 `src/`、`tests/`（产品那套）、`STATUS.md`、`server.json`**。
- **不改已签收的 `2026-10-06-gameplay-evidence-closure` 计划**；本计划产物与它并列，不并入。
- **不做 bridge / dbmw / 辅助服务 / 模块间加密 / 反外挂**的覆盖度（它们是"范围暂缓"，本表只**标注**不统计）。
