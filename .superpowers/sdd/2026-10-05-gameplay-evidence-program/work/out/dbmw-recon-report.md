# dbmw 侦察报告（第 4 部分的取证起点）

> 依据全部是 VM 实测：`work/probe-dbmw-recon.sh`（只读）→ `work/out/dbmw-recon.txt`。
> 二进制分析：`work/dbmw-survey2.py`（demangle）、`work/dbmw-protocol.py`。
> 采于 2026-10-05 15:5x。

---

## 一、结论速览

| # | 事实 | 证据 |
|---|---|---|
| 1 | **三个 dbmw 是同一份二进制**（md5 `0d70e931…`，4,041,084 B，未 strip，2013-06-29），角色由**配置**决定 | `[真机]` md5sum |
| 2 | **game 不连 dbmw**；连它的是 `monitor`(→20203) 与 `guild`(→20403)；`statics`(20303) 当时无连接 | `[真机]` netstat |
| 3 | 每个 dbmw 的 `table/server_config.tbl` 只列**一个对端** | `[真机]` 文件内容 |
| 4 | **协议词汇表在符号里自解释**：119 种 `Packet_*` + 151 个 `CDBManager::*` 方法 | `[静态]` 符号表（demangle 后） |
| 5 | **两套 id 命名空间**：packet id（线协议）与 query id（DB 查询） | `[真机]` 日志 |
| 6 | **packet id 是全局的**（4202 在 mnt 与 guild 都出现） | `[真机]` 日志 |
| 7 | query id 全部落在 **20xxx** 段 | `[真机]` 日志 |
| 8 | 查询**具名**（`selectCollectItems`），且日志会打印 SQL | `[真机]` 日志 |

**对第 4 部分的意义**：这份 bin 的符号**自带语义**（方法名 = 操作名、参数类型 = 包名），
比 game 那套（要读伪码推行为）**好啃得多**。词汇表已经可以直接枚举出来了。

---

## 二、拓扑修正（回答 open question #2）

**先前我在评估里写错过一次**（说"game 不直连 DB、都走 dbmw"）。实测的真相是：

```
df_game_r   ──TCP──> MySQL:3306   （直连；siroco11.cfg 有 master_db_acc/pwd，db_thread_count=3）
df_monitor_r ──TCP──> df_dbmw_r:20203 ──> MySQL:3306
df_guild_r   ──TCP──> df_dbmw_r:20403 ──> MySQL:3306
df_statics_r ──TCP──> df_dbmw_r:20303 ──> MySQL:3306   （当时未连）
```

⇒ **dbmw 是辅助进程（monitor/guild/statics）的 DB 代理**，game 自己直连。
**所以第 4 部分与第 3 部分是两条独立的持久化路径**，都要重写、但互不阻塞。

`table/server_config.tbl`（每实例一个对端）：

```
dbmw_mnt   ←  3  3  201  127.0.0.1  30303      （对应 monitor）
dbmw_guild ←  5  3  203  127.0.0.1  30403      （对应 guild）
dbmw_stat  ←  7  3  205  127.0.0.1  30503      （对应 statics）
```

（列头：`Type | Server Group | Index | Server IP | Server UDP Port`。
注意 `Type` 与日志里 `OnTcpServerLogin(TYPE:n)` 的 n **对不上**（mnt 日志是 10、guild 是 9），
⇒ 两者可能是**不同的枚举**，待查。）

---

## 三、两套 id 命名空间（实测样本）

### 3.1 packet id（线协议）

| 实例 | 观测到的 packet id |
|---|---|
| dbmw_mnt | 4152, **4202**, 6072, 9024, 10218 |
| dbmw_guild | 1043, 1505, 1655, 4000, 4200, **4202**, 7103 |
| dbmw_stat | 2511, 2513, 3111, 3145, 4010, 4017, 6085, 10209 |

* **`4202` 在 mnt 与 guild 都高频出现**（169 / 372 次）⇒ **packet id 是全局命名空间**，
  同一个 id 在不同角色的进程里含义相同（4202 像是心跳/通用包）。
* id 的范围横跨 1xxx–10xxx。

### 3.2 query id（DB 查询）

| 实例 | 观测到的 query id |
|---|---|
| dbmw_mnt | 20012, 20036, 20186, 20187, 20204, 20244, 20300 |
| dbmw_guild | 20012, 20016, 20036, 20054, 20056, 20068, 20096, 20229 |
| dbmw_stat | 20090, 20104, 20105, 20114, 20164, 20189, 20210, 20262 |

* **全部落在 20xxx**；`20012`、`20036` 在多个实例出现（通用查询，如日志写入）。
* 日志 `MysqlErr` 会打印 SQL 原文，且 SQL 里带 `q_id`：
  `inSert into log_query_stat(occ_time,q_id,total,response_time) values(now(),20036,2,96)`
* `DBQueryErr` 会打印**查询名**：`selectCollectItems(10631)`（括号里是行号）。

---

## 四、词汇表（从符号表枚举，`[静态]`）

`work/dbmw-protocol.py` 产出（详见 `work/out/dbmw-protocol.txt`）：

* **119 种 `Packet_*` 类型**，按第二段分 32 个域，主要：
  `DB`(35) / `DBMW`(32) / `Frame`(10) / `Result`(4) / `Guild`(3) / `Item`(3) / `Monitor`(3) …
* **151 个 `CDBManager::*` 方法** = 操作清单，例如：
  `GuildSecede` / `QueryMember` / `QueryGuildCreate` / `QueryPartyStatisticCreate` /
  `GuildJoin` / `QueryGuild` / `AddBuddy` / `RegisterToBlackList` / `AwardGuildCoinByMail` /
  `SaveMemberInsert` / `QueryIPCounter` / `InsertFrameLagStatistics` …
* **关键枚举**：`ENUM_DB_HANDLE_IDX`（313 处引用 —— 就是 query id 的枚举）、
  `ENUM_SERVER_GROUP`、`ENUM_DB_KIND`
* 角色类：`CDBManager` / `CStatisticsServer` / `CPacketTranslater` / `CPacketDecoder` /
  `CPacketTracer` / `CApplication` …

**方法签名里直接写着包类型**，例如：

```
CDBManager::GuildSecede(Packet_DB_Request_Guild_Secede*, unsigned int&, unsigned int&, unsigned int&)
CDBManager::QueryMember(unsigned int, Packet_DB_Reply_Query_Member&)
CDBManager::QueryGuildCreate(Packet_DBMW_Request_Guild_Create*, unsigned int&, unsigned int&)
CDBManager::QueryPartyStatisticCreate(Packet_DBMW_Dungeon_Statistic_Party*)
CDBManager::InsertFrameLagStatistics(Packet_Frame_Lag_Statistic_Write_Lag_Index*, CStatisticsServer*)
```

⇒ **"packet id ↔ 包类型 ↔ CDBManager 方法"这三者的对应关系是可以从静态侧建起来的**，
外加抓包做同源交叉。

## 四之二、**处理器表与头类型**（本轮最有价值的两条）

### （a）`CPacketTranslater` 有 **122 个 `On*(PacketHeader*)`** = 处理器表

```
CPacketTranslater::OnQueryGuild(PacketHeader*)             0x080936ea
CPacketTranslater::OnGuildJoin(PacketHeader*)              0x08096dae
CPacketTranslater::OnGuildSecede(PacketHeader*)            0x08097542
CPacketTranslater::OnSaveGuild(PacketHeader*)              0x08093c9c
CPacketTranslater::OnSendGuildLetter(PacketHeader*)        0x080967ee
CPacketTranslater::OnRequestIPCounterList(PacketHeader*)   0x0809b958
CPacketTranslater::OnPcRoomPlayTimeReward(PacketHeader*)   0x080a06c0
CPacketTranslater::onQueryTowerFullRank(PacketHeader*)     0x080970dc
…（共 122 个，`work/dbmw-survey2.py <elf> CPacketTranslater::` 可列全）
```

**全部收同一个参数类型 `PacketHeader*`** ⇒ 这就是 dbmw 的**报派发面**，
与 game 那套的 `DisPatcher_*::dispatch_sig` 等价，但**名字自解释**（`OnGuildJoin` 就是处理入帮）。

### （b）`PacketHeader` 由**两个 `unsigned short`** 构造

```
PacketHeader::PacketHeader(unsigned short, unsigned short)
CTcpServer::makePacketHeader(unsigned short, unsigned short)
CPacketDecoder::MsgDecode(PacketHeader*)          ← 解码/派发入口
CServerHandler::SendAllTcpServer(PacketHeader*)
```

⇒ 头的前两个字段各 **2 字节**。日志里的 packet id（4202 / 6072 / 10218 / 1043 / 2511 …）
**全部 < 65536**，与 `unsigned short` 吻合 ⇒ 其中一个 u16 就是 **packet id**，
另一个很可能是**长度**（或子类型）。**待抓包确认哪个是哪个**。

**注意**：这条与 game 那套的 13 字节头**未必相同** —— dbmw 是**另一套框架**
（`CTcpServer`/`CServerHandler`/`CPacketDecoder` vs game 的 `PacketDispatcher`），
**不要假定两者一致**（这正是 spec §4.2 "不许让一个驱动器同时说两代" 的同类陷阱）。


---

## 四之三、【已定案】dbmw 线格式 = 4 字节头（两个 u16 LE）

**抓包实测**（`work/dbmw-wire.py`，`[抓包]`）：

```
monitor → dbmw_mnt  : 11 帧, 消费 121/121 **恰好铺满**, id 命中日志已知集合 11/11
guild   → dbmw_guild: 24 帧, 消费 264/264 **恰好铺满**, id 命中日志已知集合 24/24
```

字节流是完美重复的 11 字节模式：

```
6a 10 | 0b 00 | 00 00 10 00 00 00 0a
 4202    11      └──── 7 B 载荷 ────┘
(id, LE) (size 含头)
```

| 偏移 | 宽 | 字段 | 说明 |
|---|---|---|---|
| 0..1 | 2 | **packet_id** | `u16 LE`（4202 = 0x106a） |
| 2..3 | 2 | **size** | `u16 LE`，**含 4 字节头** |
| 4.. | n | payload | |

**三重印证**（这是"同源之外还有独立来源"的典型）：
1. **静态**：`PacketHeader::PacketHeader(unsigned short, unsigned short)`
   与 `CTcpServer::makePacketHeader(unsigned short, unsigned short)` —— 头由两个 u16 构造；
2. **抓包**：按 `u16 LE id + u16 LE size(含头)` 切帧，两条流**逐字节恰好铺满**；
3. **日志**：切出来的 35 个 id **全部**落在 `PacketProcess` 日志记录过的 id 集合里
   （白名单 = 1043/1505/1655/4000/4200/4202/4152/7103/6072/9024/10218/2511/2513/3111/3145/4010/4017/6085/10209）。

**这是与 game 那套（13 字节头）完全不同的框架** —— 再次印证"不要假定两代/两套一致"。

**本次抓到的都是心跳**（id 4202 占满两条流，正好对应日志里 `id(4202)` 的高频计数）。
**要拿全协议还需在"有实际操作"时抓**（如一次登录会触发 guild 的 `SaveGuildMember`）。
抓包命令已就绪：`PORTS="20203 20303 20403" bash work/capture-login.sh start`。


| # | 缺的东西 | 怎么补 | 状态 |
|---|---|---|---|
| 1 | `packet id ↔ Packet_* 类型` 的映射 | 找注册表（像 game 的 `PacketDispatcher::get_dispatcher`）；签名里已有类型名可对 | 未做 |
| 2 | `query id ↔ SQL/查询名` 的映射 | `ENUM_DB_HANDLE_IDX` 的枚举值 + `CDBManager::Open` 附近的字符串表 | 未做 |
| 3 | **线格式**（帧头/字段布局） | **抓包**（已在跑：`tcp port 20203 or 20303 or 20403`，pid 7928） | 抓包中 |
| 4 | 握手/登录（`OnInnerPacketLogin` / `OnTcpServerLogin`） | 抓包 + 静态（`CPacketTranslater`） | 未做 |
| 5 | `Type` 枚举（table 的 3/5/7 vs 日志的 9/10） | 静态找枚举定义 | 未做 |
| 6 | `ENUM_DB_HANDLE_IDX` 与三个实例的关系 | 静态 + 配置 | 未做 |

**下一步**：等抓包攒够数据（monitor↔dbmw 每 2 分钟有轮询、guild↔dbmw 有周期性保存），
然后做"静态词汇表 × 抓包线格式"的对齐。

## 五、还差什么（第 4 部分的取证清单）

