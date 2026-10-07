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
| byte_verified | 3 |
| field_read | 0 |
| name_only | 1168 |
| **合计** | **1171** |

被标注（出现在 `tiers-external.tsv`）的 opcode = 3 条；其余 1168 条
`ext_mark=name_only`（即"只有名字"）。

本次运行的 stdout 对账行：`ext_byte_verified=3`、`ext_field_read=0`、`ext_name_only=1168`。

[静态] 本表 ext_mark=byte_verified 的条目，其 evidence 全部指向本项目已落盘产物；
       未出现在 tiers-external.tsv 的 opcode 一律 ext_mark=name_only（即"只有名字"）。

## 5. 锚点回填 vs 纯推导（对照）

| | 纯推导（tier 轴，§2） | 锚点回填（ext_mark 轴，§4） |
|---|---|---|
| 判据来源 | handlers 目录里的 `name=...::get_/put_` 调用 | **人工**标注的已落盘产物（字节级自检 / 抓包 / 静态反汇编） |
| 覆盖 | 1171 条全部 | 只有 3 条有标注 |
| 能证明什么 | 只到"字段读/写调用的候选" | `byte_verified` 的那几条才有字节级产物支撑 |

两条轴**正交**：`NOTI 1` / `NOTI 561` 在 tier 轴是 T0（没有可达处理器），在 ext_mark 轴是
`byte_verified` —— "没有 dispatcher"不等于"没有外部证据"。

[作废] §3 第 4 句"[未验证] 外部证据档…首版为空"写于外部证据档为空时；本任务的
       `out/tiers-external.tsv` 已回填 3 行，该句的"首版为空"已被推翻。
       原文按证据纪律**保留不删**，此处指向推翻它的产物。
