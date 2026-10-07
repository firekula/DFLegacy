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

## 3. 这数字不能证明什么（限定语句）

[静态] 本覆盖率的分母 = opcode-map.tsv 的 1171 条 (CMD 608 / NOTI 563)。
[静态] T3/T4 只表示"能在 handlers 目录里看到字段读/写调用"，不等于"已读出有序字段规格"；
       字段布局仍须逐家族反编译读取（不在本计划范围）。
[静态] catalog_handlers_full.txt 只覆盖 239/425 个 dispatcher 家族 ⇒ T1 的 186 条是
       "没有字段调用数据"，不是"没有字段"。
[未验证] 外部证据档（byte_verified / field_read）首版为空；在它有内容之前，
       本覆盖率**不得**被引用为"字段级已完成多少"。

## 4. 外部证据档状态

外部证据档（`byte_verified` / `field_read`）首版为空（Task 3 才建 `tiers-external.tsv`）；
在它有内容之前，上表的 T3/T4 只表示"handler 目录里能看到字段调用"，不表示"字段级已完成"。
