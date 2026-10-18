# 协议地图与进度账

总 opcode：**1171**（CMD 608 / NOTI 563）

## kind 分布

| kind | 条数 |
|---|---|
| CMD | 608 |
| NOTI | 563 |
| **合计** | **1171** |

## 分档（六档之和必须等于总数）

| 档位 | 条数 |
|---|---|
| T0_no-evidence | 743 |
| T1_dispatcher-no-handlerdata | 187 |
| T2_handlerdata-no-fields | 64 |
| T3b_fields-write-only | 16 |
| T3_fields-read-only | 109 |
| T4_fields-read+write | 52 |
| **合计** | **1171** |

## status 分布

| status | 条数 |
|---|---|
| mapped | 428 |
| no-dispatcher | 741 |
| unused | 2 |
| **合计** | **1171** |

## 外部证据轴（人工标注，与六档正交）

| ext_mark | 条数 |
|---|---|
| byte_verified | 1 |
| field_read | 2 |
| name_only | 1168 |

## 真机 run

| run_id | status | pass | needs-human | missing | fail | 证据类 |
|---|---|---|---|---|---|---|
| 20261015T013525Z-0ae1675-town-two-clients | green-with-human | 3 | 1 | 0 | 0 | [真机] |
| 20261018T091429Z-2724238-town-two-clients | green-with-human | 3 | 1 | 0 | 0 | [真机] |
| 20261018T091611Z-2724238-town-two-clients | green-with-human | 3 | 1 | 0 | 0 | [真机] |
| 20261018T091814Z-2724238-town-two-clients | green-with-human | 3 | 1 | 0 | 0 | [真机] |
| 20261018T091951Z-2724238-town-two-clients | green-with-human | 3 | 1 | 0 | 0 | [真机] |

## 边界（不得误读）

1. `T3/T4` 只表示"能在 handlers 目录里看到字段读/写调用"，**不等于**已读出有序字段布局。
2. `write_count` 含 `put_header`（129 行）；排除后换档数为 0，但家族级计数会偏大。
3. handlers 目录只覆盖 239/425 家族 ⇒ 186 个家族无字段调用数据（落 T1），是"没有数据"不是"没有字段"。
4. `[存在]` 只证明帧在场；S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"，`pass` 不得读作"该 opcode 已实现"。
5. `runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻。
6. `party-invite` / `trade-open` / `town-position-sync` 是占位骨架，有 netdump 时必然 red。
7. `ext_mark` 是人工轴，与六档正交；当前 byte_verified/field_read 极少，不得引用为"字段级进度"。
