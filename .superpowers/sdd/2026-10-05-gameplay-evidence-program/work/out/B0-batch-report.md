# B0 批次报告 —— 官服全链路已知答案采集

> 日期：2026-10-05（本地 20:06 – 22:5x）。计划：`docs/superpowers/plans/2026-10-05-gameplay-evidence-program-m0-m1.md` Task 8。
> **本批次的实际形态与计划预写不同**：计划假定靶子是 docker 容器 `dnf`；实测该容器**拒绝了我们的客户端**，
> 于是按所有者指示改抓**VM 里的另一套官服栈**（`192.168.200.131`），那条链路**成功进城镇**。
> 两份证据都保留：**失败路径（含服务端拒绝原因）+ 成功路径（含游戏面首帧模型）**。

---

## 0. 结论摘要（先看这个）

1. **拿到了成功路径**：`b0-vm-success.pcap`（169 KB，784 包）含 **7001 握手**与**游戏面完整登录→进城镇**（下行 46,193 B / 90 帧）。
2. **拿到了游戏面帧模型**：实测为 **10 字节头**（`[0]cat [1]id [2] [3..6]size(LE) [7..9]`），
   **与 7001 那套 11 字节头不同**；六条流**全部恰好铺满**（帧长度之和 == 流长度）。这是 Task 11 的直接输入。
3. **M0 的 opcode 映射被真实流量印证**：`CMD 1 = LOGIN`、`NOTI 1 = CHANNELINFO`、`CMD 2 = SET_UDP_IP_PORT`、
   `NOTI 199 = DISCONNECT_REASON`…（见 §4）。
4. **docker 容器的失败被定位到帧级**：服务端最后发的是 **`NOTI 199 DISCONNECT_REASON`**，与客户端"连接失败"一致；
   上游根因见 §5。
5. **两条会绊倒此后每一次 L2a 的暗礁**（§6）：`member_join_info` 主键、`geo_allow` IP 白名单。

---

## 1. 批次前 / 批次后 端口占用对照

| 项 | 批次前（20:06） | 批次中 | 批次后（恢复） |
|---|---|---|---|
| 宿主 `7001/tcp`、`2311/udp` | pid **17428** = `DFLegacy.Server` | 已停（让给容器） | **已起回**：pid 91588 = `DFLegacy.Server`（新 pid，同角色） |
| 容器 `dnf` 的 `7001/tcp`、`2311/udp` | 均为 `[]`（未绑上） | `docker restart dnf` 后**绑上了**（`0.0.0.0:7001`、`2311/udp`） | **Docker 引擎已停**（见 §7），容器未恢复 |
| `netsh portproxy` | **空** | 未能添加（需管理员，见 §2 写操作 4） | **空**（与批次前一致） |
| 宿主 `30011` | 无监听 | 由本批次起的**用户态中继**监听 | **已释放**（中继进程已杀） |
| 宿主 `30021/31021` 等容器发布口 | 在 | 在 | 随 Docker 停止而消失 |

---

## 2. 本批次做的写操作（逐条，含撤销）

| # | 写操作 | 目的 | 撤销 / 现状 |
|---|---|---|---|
| 1 | `Stop-Process DFLegacy.Server`（pid 17428） | 让出宿主 7001/2311 | ✅ 已按计划 Step 3 起回（pid 91588） |
| 2 | `docker restart dnf` | 只有重启才会重新绑 7001/2311 | 容器已被 Docker 引擎停止连带带走（§7） |
| 3 | `docker run --name dfl-sniff --network container:dnf --cap-add=NET_RAW --cap-add=NET_ADMIN -v <repo>:/w debian:trixie-slim sleep infinity` + 容器内 `apt-get install tcpdump iproute2 netcat-openbsd` | 抓包（`dnf` 里没有 tcpdump） | 随 Docker 停止消失；**未动 `dfl-recon`**（见偏离 B） |
| 4 | `netsh interface portproxy add v4tov4 listenport=30011 ...` | 计划 Step 4 的宿主转发 | **失败**：`The requested operation requires elevation`（无管理员权限）⇒ 改用编号 5 |
| 5 | 起用户态中继 `work/batch-b0-relay.py`（`127.0.0.1:30011 → 127.0.0.1:30021`）——计划外 | 等价实现编号 4 的意图（免提权、可随时撤） | ✅ 进程已杀、端口已释放 |
| 6 | `DELETE FROM d_taiwan.member_join_info WHERE m_id=3`（**单行**，所有者已授权） | 该行导致游戏服登录时裸 `insert` 主键冲突 | 服务端下次登录会自己插回（已实测：删后新行出现）。**删除前的整行内容**：`m_id=3, reg_date=0, ip='', contry_code=0, login_time=0, error_type=0, login_ip='', game_use_history=0` |
| 7 | VM 内 `tcpdump`（所有者代跑）+ 公钥装入 VM `/root/.ssh/authorized_keys`（**两把**：ed25519 与 RSA，RSA 指纹 `MD5:c2:ed:ee:6c:57:5d:f4:27:9e:0c:17:06:2b:88:ea:f6`） | 抓 VM 那条成功链路 | ⚠️ **公钥仍在 VM 里，建议批次收尾删除该行**；本机密钥文件 `<用户>\.ssh\id_rsa_b0`、`id_ed25519_b0` |

**未做**：没有改产品代码、没有改官服容器/VM 的任何配置、没有插桩、没有伪造任何"官方游戏面"结论。

---

## 3. 抓到的流清单与首帧十六进制

产物（`work/captures/`）：`e2e-official-to-game.pcap`（docker，端口过滤，61 KB）、
`internal-during-retry.pcap`（docker，全 TCP，2.6 MB）、**`b0-vm-success.pcap`（VM，169 KB）**。
单向流目录：`streams/`（docker）、`streams-vm/`（VM，50 个文件）。

### 3.1 VM（成功路径）

| 流 | 字节 | 帧数 | 首 32 B（hex） |
|---|---|---|---|
| `7159-7001.bin`（C→S） | 92 | 4 | `00 0b 2b 00 00 00 00 00 00 00 01 f2 f7 f1 42 f7 de a0 49 9f 6b f6 12 9f e6 18 4f 4c b0 e4 73 b0` |
| `7001-7159.bin`（S→C） | 7449 | 4 | `7c 0c 2f 00 00 00 00 00 00 00 01 00 00 00 00 32 30 32 36 31 30 30 35 31 32 32 38 33 30 00 00 00` |
| `13681-10013.bin`（C→S，游戏面） | 1125 | **21** | `01 01 00 9d 01 00 00 11 bc 5f c1 00 00 9d 6a 10 25 ff ac d8 b3 36 af 48 c1 05 c8 00 30 57 dc db` |
| `10013-13681.bin`（S→C，游戏面） | 46193 | **90** | `00 01 00 c2 01 00 00 2e 3b 8c f0 2e d3 83 46 20 d1 fb 07 e0 d5 78 18 be 53 54 ee ea 30 81 2b 6b` |
| `1625-10013.bin` / `10013-1625.bin` | 782 / 76 | 6 / 1 | 另一条连接（含 `CMD 3 EXIT`，疑似上一会话尾部） |

**7001 握手形状（两台都用 11 字节头，帧边界可复算）**：

| 靶子 | C→S 帧长 / id | S→C 帧长 / id | S 流合计 |
|---|---|---|---|
| VM | `43 / 27 / 11 / 11`，id `11 / 5 / 9 / 1` | `47 / 43 / 7220 / 139`，id `12 / 6 / 10 / 3` | 7449 ✓ |
| docker | `43 / 27 / 11 / 11`，id `11 / 5 / 9 / 1`（与前 28 字节逐字节相同） | `47 / 43 / **7235** / **140**`，同 id | 7465 ✓ |

⇒ 两条 C 流**完全同形**，差异只在 S 流的第 3 帧（15 B）与第 4 帧（1 B）——即两台服务端**不是同一份配置/版本**。
第 1 帧 id=12 正文首 4 字节 `status=0`，其后是 ASCII `20261005122830`（时间戳）。
VM 的第 3 帧 id=10 载荷是 zlib（7209 → **13312 B** = 104×128，**仍是 AES 密文**）；
第 4 帧 id=3 载荷 zlib（128 → **128 B**，= 8 个 AES 块），与计划里记的"id=3 正文 = zlib(AES(128 B 明文))"吻合。

> ⚠️ **不要**把 7001 那套 `cat=0` 的 id 用游戏的 `ENUM_NOTIPACKET` 表去解名 —— 那是**另一层协议**
> （登录/频道层），与游戏面不是同一张表。本批次对它们**只记 id 与长度**，不给名字。

### 3.2 docker（失败路径）

| 流 | 字节 | 帧数 | 首 32 B（hex） |
|---|---|---|---|
| `58840-7001.bin` | 92 | 4 | 与 VM 的 7001 C 流**前 28 字节完全相同**（末 4 字节不同，是客户端随机/序号） |
| `7001-58840.bin` | 7465 | 4 | 帧长 `47/43/7235/140`（VM 是 `47/43/7220/139`），id 相同 |
| `46386-30011.bin` | 413 | 1 | `01 01 00 9d 01 00 00 a7 20 e2 91 00 00 ad fd 0b d4 a4 45 37 e1 75 a0 3e e5 ef 66 69 ec a4 b6 26` |
| `30011-46386.bin` | 556 | 5 | `00 01 00 b0 01 00 00 93 2e be 87 93 5f 7a b8 d1 98 a9 db e7 8a 0c 7e 3d dd 30 ed 76 f2 bb c6 97` |

---

## 4. ★ 游戏面帧模型（本批次最重要的可复用产出）

**实测模型：10 字节头**

```
[0]   category     0x01 = CMD（客户端→服务端请求 / 服务端应答）  0x00 = NOTI
[1]   id           opcode 号（与 opcode_cmd.txt / opcode_noti.txt 同一张表）
[2]   0x00（观测到的取值恒为 0；语义未定 → [未验证]）
[3..6] size        uint32 LE，**含头**
[7..9] 3 字节      每个会话/每条流内变化（疑似校验或序号；语义未定 → [未验证]）
[10..] payload
```

> **[作废] 上表 `[7..9]` 那一行（2026-10-05 晚）**：`[7..9]` **不是**校验和，也**不是**"每个会话内变化"。
> 后续实测（`work/out/frame-structure-nailed.md`，脚本 `work/nail-frame-structure.py` /
> `work/nail-checksum.py` / `work/nail-varying-bytes2.py`）：
> * S→C 方向 **`hdr[7] == payload[1]` 180/180 成立**（两条独立抓包各 90 帧），
>   C→S 方向 0/42 —— 有方向性；⇒ `hdr[7]` 是 `payload[1]` 的**副本**。
> * 16 个标准校验和算法在 90 帧上的命中率仅 1–2%（随机水平）⇒ **没有一种是它**。
> * `hdr[7:10]` 对**多数逻辑帧是跨会话恒定的**（如 `NOTI53` 恒为 `25 23 72`），
>   只在像 `NOTI1(450)` 这种"载荷开头几字节随会话变"的帧上才跟着变。
> 原文保留不动（矛盾留痕），处置见上方链接。
> **未解**：`hdr[8]`/`hdr[9]` 的算法与语义（与载荷[2..3]、帧长、帧序号均无关）。
> **不阻塞**：原样重放整帧可通过。

**验证方式（可证伪）**：对 6 条流按该模型切帧，**帧长之和 == 流长度**、无残字节 —— 全部 `恰好铺满=True`。
（对照：计划的 11 字节头模型在 7001 上成立，在游戏面上**不成立**：会把 `size` 读成 10 万级。）

**id → 语义（用 M0 的 `opcode-map.tsv` 解，全部自然吻合）**：

| 流 | 帧序列（前若干） |
|---|---|
| docker S→C 556 B | `NOTI 1 CHANNELINFO(432)` → `NOTI 183 AUCTION_NOTIFY_AUCTION_SERVICE(31)`×2 → `CMD 1 LOGIN(31)` → **`NOTI 199 DISCONNECT_REASON(31)`** |
| VM C→S 1125 B | `CMD 1 LOGIN(413)` → `CMD 174 CLIENT_SPEC_STATISTIC(53)` → `CMD 8 GET_USERINFO(29)` → `CMD 186(13)` → `CMD 38 SET_USER_AREA(25)` → `CMD 93 ENTER_WARROOM(125)` → `CMD 91 MOVE_TO_GATE(125)` → `CMD 2 SET_UDP_IP_PORT(45)` …共 21 帧 |
| VM S→C 46193 B | `NOTI 1 CHANNELINFO(450)` → `NOTI 183(31)`×2 → `NOTI 160 POWER_WAR_END(23)` → `NOTI 173 GAME_OPTION(703)` → `CMD 1 LOGIN(95)` → `NOTI 16(23)` → `NOTI 53 CERA(39)` …共 90 帧 |

> **证据等级**：帧边界与长度是 `[抓包]`（原始字节可复算）；id → 名字的映射来自 M0 的
> `[静态]` 表，此处是**两路证据互证**（同一 id 的语义与它在流中的位置相符）。
> 少数 id 的名字与位置看起来不搭（如登录流程里出现 `CMD 186 AUCTION_REGIST_ITEM`）——
> 两种可能：该栈的 id 表与 docker 那份存在**版本差异**，或该名字在官服里就是那个号。
> **标 `[未验证]`，留给 Task 9/11 用它自己的双向自检去定**，本报告不擅自改判。

---

## 5. docker 容器为什么会拒绝（根因链，逐条有证据）

客户端（宿主机、2012 客户端）→ 容器：`7001` 握手**完整成功**（92 ↔ 7465 B），随后 `30011` 交换
**四次尝试字节数完全一致**：客户端 **413 B** → 服务端 **432 B + 124 B = 556 B**，结尾是
**`NOTI 199 DISCONNECT_REASON`**，客户端显示"连接失败"并退出。

服务端自己的日志（`/home/neople/game/log/siroco11/`）：

```
[20:44:10] decrypt: [Taiwan, Auth] Success!! Uid:3 ...        ← 令牌解密+鉴权成功
[20:44:10] DisPatcher_Login::dispatch_sig: USER LOGINED! ...   ← 登录派发器接受了
[20:44:11] Inter_LoginInfo::dispatch_sig: Abnormal LoginError: SIG_LOGIN_DATA::ERROR_OK(15)
[20:44:11] CUser::DisConnSig(DISCONN_SIG, bool, int): from (14) ... uid[6], ip(172.17.0.1)  ← 主动断连
```

反汇编定位（`df_game_r`，`Inter_LoginInfo::dispatch_sig` @`0x084c5294`）：

```asm
0x084c5a02  mov eax, [eax + 0xb8]     ; 取登录数据结构 +0xb8 字段
0x084c5a08  test eax, eax
0x084c5a0a  je   0x84c5add           ; == 0 → 正常路径
            ; 非 0：发包给客户端 → 打日志（%d = 该字段 = 15）→ CUser::DisConnSig(.., 0xe=14)
```

**两个已确认的暗礁**（都在登录路径上，且症状都只有客户端一句"连接失败"）：

1. **`d_taiwan.member_join_info` 主键**（`PRIMARY KEY(m_id)`）：登录路径执行**裸 `insert`**
   （`DB_Login::IsThisIPLoginPermissionGranted` @`0x0844b982`，SQL 原文
   `insert into member_join_info(m_id, reg_date, ip) values(%d, now(), '%s')`），
   该 `m_id` 已存在即主键冲突 → `[QUERY_ERROR]` → 登录被判异常。**已用删行修复**（写操作 6）。
2. **`d_taiwan.geo_allow` IP 白名单**：同一函数先执行
   `seLect count(*) from geo_allow where allow_ip = '%s'`（**精确匹配**，那行 `*` 不是通配），
   命中才置 `+0xb5` 标志。白名单内容 = `*` / `127.0.0.1` / `192.168.200.0` / `192.168.200.1` / 容器 IPv6；
   而容器看到的客户端 IP 是 **`172.17.0.1`**（docker 网关）⇒ **不在名单内**。
   **标 `[推断]`**：白名单结果写的是 `+0xb5`，而断连判的是 `+0xb8`，两者不是同一字段，
   所以"白名单 → 15"这条链**本批次没有闭合**；闭合它需要在服务间流量里看上游到底把什么塞进 `+0xb8`。

**上游来源未定**：`log20261005.cri` 显示该错误在**修复 `member_join_info` 之后**仍出现（20:44、21:30 两次），
且 `NotI 199` 确实发给了客户端 ⇒ 断连是真的、但"15 由谁写入"**仍未验证**。

---

## 6. 对后续所有 L2a 实验的标准前置条件（本批次的直接副作用）

> 任何"用驱动器/客户端打官服容器"的实验，**开始前**必须处理这两条，否则会在游戏面登录被踢，
> 而客户端只会说"连接失败"：

1. 清掉本次账号在 `d_taiwan.member_join_info` 的行（或每轮换账号）；
2. 确认客户端 IP 在 `d_taiwan.geo_allow` 里（容器看到的通常是 `172.17.0.1`；VM 那套栈看到的是 `192.168.200.1`，**在名单内**）。

---

## 7. 三栏结果

### 预期到了
- `7001` 握手抓到，且**形状与既有记录一致**：C→S 4 帧 id **11/5/9/1**、S→C 4 帧 id **12/6/10/3**，
  含 `status=0` 与时间戳、zlib 段落 —— 与计划 Task 9 记的"7001 的 11/5/9/1"**吻合**。
- **游戏面非空流量**（批次的核心里程碑）：既拿到失败路径（413/556），也拿到成功路径（1125/46193）。
- 容器 `7001/tcp`、`2311/udp` 在 `docker restart dnf` 后**确实重新绑上**（计划的假设成立）。

### 预期没到
- **计划设想的"从 docker 容器一路进游戏"没有达成**：容器在游戏面登录拒绝（§5），根因链只闭合到
  "IP 白名单不在名单内（`[推断]`）"，**上游写入 `+0xb8=15` 的那一步未取证**。
- **计划 Step 4 的 `netsh` 转发没做成**（无管理员权限）⇒ 走了用户态中继（等价，但方法已偏离计划）。
- **计划 Step 6 的"重建 `dfl-recon` 来抓包"没做**（前提有误，见下）。
- **`31011/udp` 玩法面没有采集**（本批次按计划只做 TCP；且客户端根本没走到副本）。

### 预期外来了（本批次真正的收获）
1. **成功路径在另一台靶子上**：VM（`192.168.200.131`）里的官服栈**接受同一个客户端**并**进到城镇**。
   抓包 169 KB / 784 包。
2. **游戏面帧模型 = 10 字节头**（与 7001 的 11 字节头**不同**），六条流全部恰好铺满（§4）。
3. **`NOTI 199 DISCONNECT_REASON`** 这一帧的存在 —— 客户端"连接失败"的服务端对应物，
   对 Task 11 的失败路径自检是现成靶子。
4. **两套栈并非同一配置**：VM 的游戏面端口是 **`10013`**（`30011/31011` 都不开），且 7001 的 S 流
   帧长不同（`47/43/7220/139` vs docker 的 `47/43/7235/140`）。
5. **计划的两处前提不成立**（已记入 `work/captures/B0-batch-state.txt`）：
   A. `netsh` 需管理员；B. 抓包 sidecar 不是 `dfl-recon`（它在 bridge 网络、不在 `dnf` 的 netns 里，
   重建它只会毁掉 `c++filt`）——实测抓包用的是独立的 `dfl-sniff`。
6. **Docker 引擎在本批次中途停止**（`npipe` 不存在；`dnf`/`dfl-sniff` 随之消失）。
   失败路径的 pcap 已先落盘，未丢失。

---

## 8. 未决项 / 下一步

| # | 未决项 | 怎么才能验 |
|---|---|---|
| 1 | 写入 `SIG_LOGIN_DATA +0xb8 = 15` 的上游是谁、15 是什么 | 在全 TCP 抓包（已存 `internal-during-retry.pcap`，2.6 MB）里找那份 LOGIN_INFO 包；或对 VM 里**能成功**的那套栈抓同样的内部流量做对照（同一字段在成功时是什么值） |
| 2 | `geo_allow` 与断连的因果 | 给容器 `geo_allow` 加 `172.17.0.1` 后重试（单行、可逆）；若仍被踢则因果不成立 |
| 3 | 游戏面 `[2]` 与 `[7..9]` 三字节的语义 | Task 9/11 的双向自检会暴露（改一字节看谁校验失败） |
| 4 | 少数 id 的语义与位置不搭（如 `CMD 186`） | 用 VM 那套栈的 `df_game_r` 复核 id 表版本差异；或按 Task 11 的自检定位 |
| 5 | VM 里那两把公钥 | 批次收尾请所有者删除 `/root/.ssh/authorized_keys` 中 `dfl-b0-batch` 的两行 |
| 6 | Docker 引擎需重新启动 | 所有者启动 Docker Desktop；容器 `dnf` 起来后按 §1 恢复端口对照 |

**给 Task 9/11 的直接输入**：§4 的帧模型 + §3 的首帧十六进制 + `streams-vm/` 里的 90 帧成功序列
（`CMD 1 LOGIN` 的 413 B 请求与 95 B 应答、`NOTI 1 CHANNELINFO` 的 450 B、`CMD 2 SET_UDP_IP_PORT` 的 45 B）
可以当**已知答案**；失败路径的 5 帧序列（含 `NOTI 199`）可以当**拒绝路径**的已知答案。

---

## 9. 追加实验（2026-10-05 23:1x）：`geo_allow` 假设**否证** + 根因读到代码级

### 9.1 单变量实验与结果

| 项 | 值 |
|---|---|
| 假设 | `d_taiwan.geo_allow` 里没有客户端 IP（`172.17.0.1`）⇒ 登录被判异常（`err_code=15`） |
| 唯一变量 | `INSERT INTO d_taiwan.geo_allow (allow_ip, allow_c_code) VALUES ('172.17.0.1','CN')`（1 行） |
| 刻意不动 | `member_join_info` 的 m_id=3（与最近两次失败时同状态） |
| 结果 | **仍然失败**：`USER LOGINED!` → `Inter_LoginInfo::dispatch_sig: Abnormal LoginError: ...ERROR_OK(15)` → `CUser::DisConnSig(.., 14)` |
| 结论 | **白名单假设被排除**（实验后该行已删除，名单恢复原状） |

客户端表现：**进到"选择角色"界面**并弹出公告框（"请稍等几分钟后再尝试登录 / 详情请联系客服"）——
那句公告就是服务端在断连前发来的错误包（反汇编显示该路径先 `CUser::Send` 再 `DisConnSig`），
**不是**一个新的、更晚的失败点。

### 9.2 根因（读代码得出，非推断）

`SIG_LOGIN_DATA` 的字段布局（由断言串与代码共同确认）：`+0xa1` = IP 字符串、`+0xb5` = geo 白名单命中标志、
**`+0xb8` = `err_code`**。`Inter_LoginInfo::dispatch_sig` @`0x084c5294` 对它的判别：

```asm
0x084c534a  mov eax, [eax + 0xb8]     ; err_code == 15（实测值）
0x084c5350  cmp eax, 1   ; je 0x84c5363        ← 有专门分支
0x084c5355  cmp eax, 8   ; je 0x84c5423        ← 有专门分支
0x084c535e  jmp 0x84c54a7
0x084c54b0  cmp eax, 9   ; jle 0x84c55b9       ← 10..12 有专门分支
0x084c54c2  cmp eax, 0xc ; jg  0x84c55b9
            ...（其后是一串逐值 if 链，各分支各有断言串：
                 E_CANTFIND@行6922 / PUNISH_REASON@7015 / E_AUTH@7031 /
                 E_AUTH_MOUSE@7072 / E_AUTH_MOUSE_DENY@7082 / E_PASSPAD_WRONG_VERSION@7092）...
0x084c5a02  mov eax,[eax+0xb8] ; test eax,eax ; je 0x84c5add   ← **只有 0 走正常路径**
0x084c5a6a  mov ebx,[eax+0xb8]                                  ← 15 落到这里
0x084c5a70  ... cMyTrace("…Abnormal LoginError: SIG_LOGIN_DATA::ERROR_OK(%d)", ebx=15)
0x084c5ace  call CUser::DisConnSig(.., 0xe=14)
```

⇒ **`err_code = 15` 在这个 build 里没有任何 handler**，一路落到 fallback（行 7154，晚于所有已知 case）
⇒ 打 `Abnormal LoginError(15)` 并把客户端断连。

**这不是客户端的问题、不是 IP 白名单、也不是 `member_join_info` 主键（伴生错误）。**
往游戏服发这份 `SIG_LOGIN_DATA` 的上游是 **gate**——而容器里的 gate 是 **Rust 重写的 llnut**
（日志：`Starting DNF Login Server...`、`Database: game@127.0.0.1:4000/d_taiwan`），
它和 2016 年的官服 `df_game_r` 二进制**不共享同一份 `err_code` 枚举**。
这与本报告 §7"两台栈不是同一配置/版本"是同一件事的两个侧面。

### 9.3 对靶子选择的结论（写进 RESUME）

| 用途 | 靶子 |
|---|---|
| Task 9 的 7001 已知答案自检 | **docker 容器**（握手本来就成功；期望值 7465 B / 帧 47-43-7235-140） |
| Task 11 的游戏面**成功**自检 | **VM 那套**（只有它接受客户端；期望值用它 10013 的 21/90 帧） |
| **拒绝路径**已知答案 | docker 的 5 帧（末帧 `NOTI 199 DISCONNECT_REASON`） |

**docker 的游戏面要能进，得先修 gate↔game 的 `err_code` 契约**——那属于官服栈自身/重写 gate 的不一致，
**不在 M0/M1 范围**，记为 `[未验证]`（怎么验：对 gate 二进制查它发送的 `err_code` 取值集合，
或对 VM 那套能成功的栈抓同样的服务间流量做对照）。
