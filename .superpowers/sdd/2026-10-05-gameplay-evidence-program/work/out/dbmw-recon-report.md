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

## 四之四、心跳与"业务报文没抓到"的排查（含一个我自己踩的坑）

### （a）心跳帧的形状（`[抓包]`，两次独立抓包一致）

```
guild   → dbmw_guild : id=4202  size=11  payload = 00 00 0b 00 00 00 09   （509 帧，载荷**恒定**）
monitor → dbmw_mnt   : id=4202  size=11  payload = 00 00 10 00 00 00 0a   （231 帧，载荷**恒定**）
```

载荷末字节 **`0x09` / `0x0a`** 正好等于各自日志里的 `OnTcpServerLogin(TYPE:9)` /
`(TYPE:10)` ⇒ **心跳载荷里带本连接的对端类型**。周期约 **5 秒**。

**方向性**：两次抓包中，**反向一律是 0 载荷的纯 ACK**（509/509、231/231）。
⇒ 至少心跳这条路径是**单向 fire-and-forget**。

### （b）坑：我第一次抓 dbmw 用了 `tcp port ...`，可能漏了 UDP

`dbmw` 的 cfg 里 **`tcp_port` 与 `udp_port` 是同一个号**，且 netstat 证实它
**同时监听两者**：

```
udp  0.0.0.0:20403  3766/df_dbmw_r
udp  0.0.0.0:20303  3777/df_dbmw_r
udp  0.0.0.0:20203  3771/df_dbmw_r
```

**已修**：`work/capture-login.sh` 的过滤器默认**不再限定 tcp**（要只抓 TCP 就设 `PROTO=tcp`）。

### （c）【已判定】**假设 A 成立：业务报文走 UDP**

**判定方式**：用**不限协议**的过滤器抓满一整轮（覆盖 `~17:03` 的周期批次），
然后**单独分析 UDP 载荷**（`work/analyze-udp.py`）。

**结果**（`work/captures/dbmw-long.pcap`，协议分布 TCP 881 / **UDP 71**）：

| UDP 方向 | 包/字节 | packet id 直方图 |
|---|---|---|
| `127.0.0.1:42024 → :20203`(mnt) | 28 包 / 28272 B | **6072×18, 10218×9, 4152×1** |
| `127.0.0.1:56993 → :20303`(stat) | 40 包 / 3522 B | **10209×18, 3111×18, 6085×2, 2511×1, 4010×1** |
| `127.0.0.1:58379 → :20403`(guild) | 3 包 / 322 B | **9007×1, 1061×1, 1069×1** |

**三条流按 4 字节头切帧全部"恰好铺满"**（`消费 == 流长`）。

⇒ **`dbmw` 的传输分工是**：
* **TCP** = 心跳（id 4202，每 ~5 秒，载荷恒定）；
* **UDP** = **业务**（真正的查询/应答）。

**这解释了我先前两轮"只看到心跳"的全部现象** —— 我用了 `tcp port ...` 过滤器。
**第四个独立印证**：这批 UDP 的 packet id 与日志 `PacketProcess` 记录的 id **逐个吻合**
（6072 / 10218 / 4152 / 10209 / 3111 / 6085 …）。

### （d）样本载荷结构（`[抓包]`，可直接用于后续取证）

```
3111  (11 B) ×18 : 27 0c 0b 00 00 00 00 00 00 00 0c
                   27 0c 0b 00 00 00 00 00 00 00 0d    ← 末字节逐包递增 0c/0d/0e…
                   27 0c 0b 00 00 00 00 00 00 00 0e
10218 (20 B) ×9  : ea 27 14 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 00 00
10209 (63 B) ×18 : e1 27 3f 00 00 …（后接全 0）
6072 (1560 B) ×18: b8 17 18 06 00 …（大段 0）… 01 00 00 00 01 00 00 00
4152  (12 B) ×1  : （见 work/out/udp-analysis.txt）
```

**可读出的结构**：`<u32 时间戳?><u8/参数区><大段 0 填充><尾部几个 1>`；
`3111` 的末字节是**递增计数**（像轮询序号）。
**未做**：字段级语义（那是第 4 部分六件套的活）。


时间对齐（**注意时区**：VM 本地时间是 UTC+8，宿主是 UTC-4，差 12 小时）：

```
抓包窗口（VM 本地）: 16:03:32 .. 16:45:52
dbmw 执行查询的时刻 : 16:32:34            ← 落在窗口内
```

| 假设 | 内容 | 支持它的证据 |
|---|---|---|
| **A** | 业务走 **UDP**，是我的 `tcp` 过滤器漏了 | dbmw 确实同时监听 UDP；那批查询在窗口内 |
| **B** | 那批查询是 dbmw 的**内部定时刷盘**，不需要线协议触发 | 查询时刻 15:00:34 / 15:31:34 / 16:01:34 / 16:32:34 是**严格 ~31 分钟周期**，像内部定时器而非玩家操作；两次抓包反向全 0 载荷 |

**判定方法（已在进行）**：用**不限协议**的过滤器抓一整轮。若下一批（约 17:03）出现
**非 4202 的帧** ⇒ A；若**依然只有 4202** ⇒ B。
**在判定之前，不许把"dbmw 的业务走 UDP"写成结论。**

### （d）要拿真正的业务报文，最可靠的触发方式

`dbmw_guild` 日志里出现过：

```
[14:42:58] SaveGuildMember(698): CDBManager::SaveGuildMember(SAVE_LOGOUT flag(1), grade(1), guildMemPoint(0), g(2), s(3), c(6))
```

⇒ **登出会触发 guild 成员存盘**（当时是在有公会角色登录之后）。
所以"用一个**有公会**的角色登录 → 做点公会相关操作 → 登出"是最可靠的触发链。
（本次会话没抓到，可能因为该角色此轮没有公会数据变更。）

## 五、还差什么（第 4 部分的取证清单）

