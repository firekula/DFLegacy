# 协议字段级覆盖度盘点（1171 条 opcode）

## 1. 源对账（只读真产物）

| 项 | 值 |
|---|---|
| opcode 行数 | 1171 |
| CMD / NOTI | 608 / 563 |
| status=mapped | 428 |
| status=no-dispatcher | 741 |
| status=unused | 2 |
| dispatchers 目录家族数 | 425 |
| handlers 目录家族数 | 239 |
| handlers 目录行数 | 7273 |
| handlers 被跳过的行（字段数<3） | 0 |
| mapped dispatchers（distinct） | 423 |
| 其中无 handler 数据 | 186 |

## 2. 分档结果（各档之和必须等于 1171）

| 档位 | 条数 |
|---|---|
| T0_no-evidence | 743 |
| T1_dispatcher-no-handlerdata | 187 |
| T2_handlerdata-no-fields | 64 |
| T3b_fields-write-only | 16 |
| T3_fields-read-only | 109 |
| T4_fields-read+write | 52 |
| **合计** | **1171** |

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

[静态] 本覆盖率的分母 = opcode-map.tsv 的 1171 条 (CMD 608 / NOTI 563)。
[静态] T3/T4 只表示"能在 handlers 目录里看到字段读/写调用"，不等于"已读出有序字段规格"；
       字段布局仍须逐家族反编译读取（不在本计划范围）。
[静态] catalog_handlers_full.txt 只覆盖 239/425 个 dispatcher 家族，而"mapped opcode 用到的
       dispatcher"有 423 个 ⇒ 其中 186 个家族没有字段调用数据，落到 T1 的 opcode 是 187 条
       （差 1 因为 StartGame 家族有 2 条 mapped opcode）；这是"没有字段调用数据"，不是"没有字段"。
[未验证] 外部证据档（byte_verified / field_read）首版为空；在它有内容之前，
       本覆盖率**不得**被引用为"字段级已完成多少"。

## 4. 外部证据档状态（**独立轴**，不混进上表六档）

人工标注源：`out/tiers-external.tsv`（本次运行传入 `--external`）。

| ext_mark | 条数 |
|---|---|
| byte_verified | 1 |
| field_read | 2 |
| name_only | 1168 |
| **合计** | **1171** |

被标注（出现在 `tiers-external.tsv`）的 opcode = 3 条；其余 1168 条
`ext_mark=name_only`（即"只有名字"）。

本次运行的 stdout 对账行：`ext_byte_verified=1`、`ext_field_read=2`、`ext_name_only=1168`。

### `ext_mark` 的分级门槛（定义）

- `byte_verified` —— **该 opcode 的线上字节被逐字段钉住、落盘且可复跑**；只有达到这条才配 `byte_verified`。
- `field_read` —— 只有**静态字段序**（反汇编里的读/写次序）或**未经逐字段核对的抓包观测**。
- `name_only` —— 没有任何字段级证据，只有名字。

每个非 `name_only` 的判定依据写在**该 opcode 的 `evidence` 列**（`out/tiers-external.tsv`）；
本报告只做校验与合并，**不自动生成依据**。

[静态] 当前 `byte_verified` 只有 CMD 1（1 条）；`field_read` 只有 NOTI 1、NOTI 561（2 条）。

[作废] `NOTI 1` 曾标 `byte_verified`（原文见 `8334e23` 的 `out/tiers-external.tsv`，**不删**），
       Task 3 的 Fix 1 按证据分级降为 `field_read`：它的证据只有 `cipher-callchain-audit.txt` L3342-3517 的
       **静态字段序**（`CUser::InitOnAccept` 里 `put_binary(334)`/`get_channel_name`/seed/random 的写入次序）
       与"抓包两样本前 334 B 相同"这条观测，**未**把 334 B 载荷逐字段对上。项目内两处反向记录指向推翻它：
       `cipher-audit-corrections.md:70` 的 `[作废]`（"334B key+103B 字段已逐字段闭合" —— "没有解开 NOTI1，
       也没有逐字段比对"）、`dynamic-evidence-progress-2026-10-07.md:368`（"[真机] 未取的：334 B key 本体"）。
       该行 `evidence` 原文保留 —— 它就是降级的依据。
       上面那个提交指针指向**本分支**的提交；若历史被改写，以 `out/tiers-external.tsv` 的当前内容为准。

[作废] `NOTI 561` 曾标 `byte_verified`（原文见 `09e2b75` 的 `out/tiers-external.tsv`，**不删**），
       本轮 fix wave（F-1）按证据分级降为 `field_read`：`out/noti561-closure.md` 自述
       "类型17密码/封装 **已闭合**；类型19密码/封装结构 **已闭合**，BinCount=5 的业务作用 **已降级**；
       54 个内层业务字段语义 **已降级**" ⇒ 密码/封装闭合，但**字段语义没有被逐字段钉住**，
       不满足本表的 `byte_verified` 门槛。该行 `evidence` 保留 `57/54/3` 三个数字，并补记该文件的自述限定；
       上面那个提交指针指向**本分支**的提交；若历史被改写，以 `out/tiers-external.tsv` 的当前内容为准。

[静态] 本表 ext_mark=byte_verified 的条目，其 evidence 全部指向本项目已落盘产物；
       未出现在 tiers-external.tsv 的 opcode 一律 ext_mark=name_only（即"只有名字"）。

## 5. 锚点回填 vs 纯推导（对照）

| | 纯推导（tier 轴，§2） | 锚点回填（ext_mark 轴，§4） |
|---|---|---|
| 判据来源 | handlers 目录里的 `name=...::get_/put_` 调用 | **人工**标注的已落盘产物（字节级自检 / 抓包 / 静态反汇编） |
| 覆盖 | 1171 条全部 | 只有 3 条有标注 |
| 能证明什么 | 只到"字段读/写调用的候选" | `byte_verified` 的那几条才有字节级产物支撑 |

两条轴**正交**：ext_mark 不参与 tier 判定 —— NOTI 1、NOTI 561 在 tier 轴同样没有可达处理器，按分级门槛只到 `field_read`。"没有 dispatcher"不等于"没有外部证据"。

[作废] §3 第 4 句"[未验证] 外部证据档…首版为空"写于外部证据档为空时；本任务的
       `out/tiers-external.tsv` 已回填 3 行，该句的"首版为空"已被推翻。
       原文按证据纪律**保留不删**，此处指向推翻它的产物。

## 6. 可复跑命令（路径相对 `work/` 目录）

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
