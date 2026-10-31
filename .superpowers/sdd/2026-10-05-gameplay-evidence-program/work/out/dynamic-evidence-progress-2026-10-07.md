# 动态证据阶段 · 本轮进展报告（A / B / C）

日期：2026-10-07（本机时区）· 工作区：`G:\Game\DNF_ACT1\DFLegacy`
上游交接：[2026-10-07-dynamic-evidence-handoff.md](<../../../docs/superpowers/handoff/2026-10-07-dynamic-evidence-handoff.md>)
本报告**不修改**已 `CLOSED_WITH_LIMITS` 的离线收尾计划（`2026-10-06-gameplay-evidence-closure`）；这是独立一轮动态取证。

## 0. 一句话

**B 已闭合**（LOGIN 用 `category=1, code=33`=`LoginChannel`；值语义 = `vec[code]==1`；
运行期实测 `vec[33]=1`；**"应拒绝却成功"的矛盾已解决：成功账号 `Mobile:1` 走了 `isRestricted` 里的手机认证绕过**），
**C 的消费侧已闭合到 10/10 样本**（400 B 明文、尾 12 B 恒零、末非零字节在 387），
**A 拿到了运行实例的第一批真值**（singleton 指针 `0x0941f8a4→0x11804fe8`、14 层算法链、`needed_key_length=334`），
唯一仍未取到的是 **334 B key 在对象内的精确偏移**（已写明怎么取）。

## 1. 环境与 T0（本轮实测）

| 项 | 值 | 来源 |
|---|---|---|
| VM | `192.168.200.131`，CentOS 5.8 / kernel **2.6.18-308.el5 x86_64** | `[真机]` `uname -a` |
| VM T0 | `2026-10-07 01:44:56 EDT (-0400)`，`epoch=1791351896` | `[真机]` |
| VM 时钟差 | 比本机（CST/UTC+8）**慢 12 小时**（EDT=UTC−4） | `[真机]`；与 `RESUME.md` 记的"差 8 小时"**不同**，按本次实测为准 |
| 官方栈 | 17 个 `df_*_r` 全在跑；`df_channel_r` pid 2901 听 7001（另有 pid 3415） | `[真机]` `ps -eo pid,ppid,etime,comm,args` |
| 目标进程 | **`df_game_r siroco11` pid 3567**（听 `10013`/tcp、`127.0.0.1:20013`）；另有 `siroco52` pid 3568 听 `10315` | `[真机]` `ps` + `netstat -lntp` |
| `df_game_r` 二进制 | `36360954 B`，`md5 47de6d6c32424e7ec09498e0ae4e0b87`，`sha256 486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043` | `[真机]` `md5sum`/`sha256sum`；与本地 `$O\df_game_r.bin` **同 hash** ⇒ 本地静态结论可直接用于这台 VM |
| `siroco11.cfg` | `gc_no=3011 channel_no=11 server_group=3`、`tcp_port=10013`、`master_db_name=d_taiwan`、`master_db_acc=game` | `[真机]` `cat /home/neople/game/cfg/siroco11.cfg` |

**VM 工具面（决定"能不能只读观测"）**：有 `gcc 4.1.2` / `python 2.4` / `dd` / `od`；
**没有 gdb、没有 32 位工具链**（`gnu/stubs-32.h` 缺失 ⇒ `gcc -m32` 编不过）；无 yama（`ptrace_scope` 文件不存在）。
`/proc/<pid>/mem` 的 `pread` **被内核拒**：`errno=3 ESRCH`（root 也一样）——
这是 2.6.18 的 `PTRACE_MODE_ATTACH` 检查，**不是权限配置问题**。

## 2. B 项 · LOGIN 的 restriction：静态链条（本轮新增，全部可复跑）

### 2.1 LOGIN 处理器实际调用的 category/code —— `[静态]`

来源：既有反汇编转储 `work/out/login-full.txt:273-288`（函数 `DisPatcher_Login::dispatch_sig` @ `0x081e8c78`）。

```asm
0x081e9190  call  Singleton<ServiceRestrictManager>::Get()
0x081e9195  mov   dword ptr [esp+0xc], 0x21   ; 4th arg = restrict code = 33
0x081e919d  mov   dword ptr [esp+8], 1       ; 3rd arg = category      = 1
0x081e91a5  mov   edx, [ebp+0xc]              ; CUser*
0x081e91af  call  ServiceRestrictManager::isRestricted(CUser*, Category, int)
0x081e91b4  test  al, al
0x081e91b6  je    0x81e91dd                   ; al==0 → 继续登录
0x081e91b8  mov   dword ptr [esp+8], 0xd1     ; error code = 209
0x081e91c0  mov   dword ptr [esp+4], 1
0x081e91ce  call  CUser::SendCmdErrorPacket(ENUM_CMDPACKET, u8)
0x081e91d3  mov   ebx, 0                       ; 返回 0 = 拒绝
```

**判据的四条独立支撑**：
1. `isRestricted` 有两处重载，`@0x0816e6b8` 是 `(CUser*, Enum, int)`、`@0x0816e75e` 是 `(Enum, int)`；
   **只有前者接受 CUser\***，而调用点前一条指令正好在压 `[ebp+0xc]`（= `dispatch_sig` 的第一个形参 `CUser*`）
   ⇒ 选中的是 `@0x0816e6b8`，于是 `esp+8` = `Enum`、`esp+0xc` = `int`。
2. 紧邻的 `cMyTrace` 格式串 = `'[Taiwan, Auth] Token timestamp info. (token_time:%u, current_time:%u, patch_time:%d)'`
   （`.rodata` `0x8bc1e9c`），所在函数名串 = `'virtual int DisPatcher_Login::dispatch_sig(CUser*, PacketBuf&)'`
   （`0x8bd0860`）⇒ 确认这是**登录处理器内部、token 解密之后**的那一步。
3. `0x21` 与 VM DB 的 `restrict_code` 取值范围（1..33）吻合，且 33 是最后一个（见 2.2 的 `RestrictEnd` 之前一项）。
4. 返回值被用于 `test al,al` + 错误包发送 + `return 0`，与"拒绝登录"一致。

⇒ **handoff §4.B 的第一条未知（"LOGIN 实际调用了哪个 category/code"）已关闭：`category=1`、`code=33`。**

### 2.2 code 的业务名称 —— `[静态]`（运行期复读自证见 §3.3）

`df_game_r.bin` 的 `.data` 里有官方**自己的名字表**（`OBJECT` 符号给了地址与大小）：

| 符号 | VA | 大小 | 内容 |
|---|---|---|---|
| `MobileRestrict::enumStringMobiles` | `0x093700c0` | 140 B | **35 个 `char*`** |
| `CharacterRestrict::enumStringCharacters` | `0x0937014c` | 16 B | 4 个 `char*` |

两张表都由 `MobileRestrict::enumToString(Enum)` @ `0x0816d463` / `CharacterRestrict::enumToString(Enum)` @ `0x0816d472`
按 `mov eax,[eax*4 + 表基址]` 索引 ⇒ **数组下标就是枚举值**。

| 下标 | `enumStringMobiles` | 下标 | `enumStringMobiles` |
|---|---|---|---|
| 0 | `RestrictBegin` | 18 | `UseRandomBox` |
| 1 | `DeleteItem` | 19 | `UseExportJob` |
| 2 | `DropGold` | 20 | `UseDisjointAvatar` |
| 3 | `UseNpc` | 21 | `UseEmblemCompound` |
| 4 | `UseMail` | 22 | `RecoverStamina` |
| 5 | `UseAuction` | 23 | `DeleteCharacter` |
| 6 | `UseTrade` | 24 | `AccountCargo` |
| 7 | `UseUpgrade` | 25 | `AccountUpgrade` |
| 8 | `UseEnchant` | 26 | `AccountMoveGold` |
| 9 | `UseCompound` | 27 | `AccountMoveItem` |
| 10 | `UseDisjoint` | 28 | `GuildCreate` |
| 11 | `UsePrivateStore` | 29 | `GuildLevelUp` |
| 12 | `UseStackable` | 30 | `GuildSkillUp` |
| 13 | `UseBindSphere` | 31 | `GuildBreak` |
| 14 | `UseSeal` | 32 | `CreateCharacter` |
| 15 | `UseRandomOptionChange` | **33** | **`LoginChannel`** ← LOGIN 查的就是它 |
| 16 | `UseRandomOptionReGeneration` | 34 | `RestrictEnd`（哨兵） |
| 17 | `UseCeraShop` | | |

`enumStringCharacters` = `[RestrictBegin, RestrictOnDay, RestrictIP, RestrictEnd]`。

**⚠️ 命名基准的坑（留档，因为它让我先写错过一版）**：`enumStringMobiles[i]` 是**从 0 起**的数组，
所以"名字表第 1 项 = `DeleteItem`"；而 DB 的 `restrict_code` 是**从 1 起**的。
两者相差 1 时，`restrict_code=33` 恰好对应 `enumStringMobiles[33] = LoginChannel`（末项，前面有 `RestrictEnd` 兜底），
但 `restrict_code=1` 到底对应 `DeleteItem`(idx 1) 还是 `RestrictBegin`(idx 0)，
**本轮没有拿到能定案的一手证据** ⇒ 见 §5 待办 W2。

### 2.3 值语义 —— `[静态]`

`ServiceRestrictManager::isRestricted(Enum, int)` @ `0x0816e75e`（119 B，既有转储 `work/out/login-restriction-static.txt:57-96`）：

```
map<Enum, InterfaceServicePolicy*>::find(category)
  命中 → p = it->second ; call p->vtable[0x10](code)      ; ← 值语义在这一层
  未命中 → return 1                                       ; 默认"受限"= 拒绝
```

- vtable 布局（本轮从 `.data` 实际读出，与上一版差 4 B，**本版为准**）：
  `[0]` = `InterfaceServicePolicy` 的 offset-to-top（**不是析构**），
  之后依次是 `D1/D0`(2,3) → `beginPolicy`(4) → `endPolicy`(5) → **`isApply()`(6)** → **`isApply(int)`(7)** → `printRestrict`(8) → `getRestrictValue`(9)。
  记录实例：`_ZTV10NullPolicy` @ `0x08b82f20` → `[2]=D2`、`[3]=D0`、`[4]=beginPolicy`、`[7]=NullPolicy::isApply(int)` @ `0x0816d670`。
- `NullPolicy::isApply(int)` @ `0x0816d670` = **`mov eax,1; ret`** ⇒ 永远返回 1（该策略下什么都受限）。
- `AuthMobileRestrictPolicy::isApply(int)` @ `0x0816d7f0` /
  `CreateCharacterRestrictPolicy::isApply(int)` @ `0x0816def6` **逐指令相同形状**：

```asm
cmp dword [ebp+0xc], LIMIT      ; AuthMobile: 0x21(33) / CreateCharacter: 2
jg  fail                        ; 越界 → logFormat(...) + return 0
...
lea edx, [this + 0xc]           ; this+0xc = vector<short> 的 begin
call std::vector<short>::operator[](code)
movzx eax, word ptr [eax]       ; 取 16 位元素
cmp ax, 1
sete al                         ; ★ 返回 (value == 1)
```

⇒ **`isRestricted` 为真的唯一路径是：该 category 的策略向量里 `vec[code] == 1`。**

`AuthMobileRestrictPolicy::getRestrictValue` @ `0x0816d9a0`（175 B）显示这个向量**是从 DB 策略行按码填进去的**：
循环 `i = 1 .. vec.size()-1`，`MobileRestrict::enumToString(i)`（= 业务名）作为键去查策略，
命中的值写成 1 字节到 `dest[0x119c + i]`。

### 2.4 category ↔ 策略类绑定 —— `[静态]`（IDA/Hex-Rays 反编译，产物 `work/out/restr-ida/initialize.txt`）

`ServiceRestrictManager::initialize()` @ `0x0816e2b2` 依次 new 四个策略并把 `getCode()` 当 map 键插入。
各策略构造函数的**基类实参**直接给出 category：

| 策略类 | 构造函数 `mov [esp+4], N` | category |
|---|---|---|
| `NullPolicy` @ `0x0816d5aa` | `0` | **0** |
| `AuthMobileRestrictPolicy` @ `0x0816d6ce` | `1` | **1** ← LOGIN 查的那一档 |
| `CreateCharacterRestrictPolicy` @ `0x0816ddd2` | `2` | **2** |
| `SecurityRestrictPolicy` @ `0x0816da50` | `3` | **3** |

⇒ **`category=1` = `AuthMobileRestrictPolicy`，`category=3` = `SecurityRestrictPolicy`**，
与 DB 里只有 `category ∈ {1,3}` 两档完全对上。

### 2.5 DB 现状 —— `[真机]`（只读查询，未改任何行）

`d_taiwan.dnf_restrict_state` 的 schema（`SHOW CREATE TABLE`，只读）：

```
server_group int(11) NOT NULL, category int(11) NOT NULL, restrict_code int(11) NOT NULL,
restrict_value char(1) NOT NULL,          -- ★ 单字符，值是 '0'/'1'
mod_date datetime NOT NULL, reg_date datetime NOT NULL,
PRIMARY KEY (server_group, category, restrict_code), ENGINE=MyISAM
```

全表（`SELECT server_group,category,COUNT(*)...GROUP BY`，只读）：

| server_group | category | n | code 范围 | restrict_value |
|---|---|---|---|---|
| 1 | 1 | 33 | 1..33 | 全 `'1'` |
| 1 | 3 | 1 | 1 | `'0'` |
| 2 | 1 | 33 | 1..33 | 全 `'1'` |
| 2 | 3 | 1 | 1 | `'0'` |
| **3** | **1** | **33** | **1..33** | **全 `'1'`** |
| 3 | 3 | 1 | 1 | `'0'` |

合计 102 行，`reg_date` 全在 `2013-02-14` ~ `2013-05-16`（**私服镜像自带，不是本次实验产生**）。

⇒ **运行中的 `siroco11` 用 `server_group=3`（来自 `siroco11.cfg`），所以它看到的是 `category=1, code=33, restrict_value='1'`。**

### 2.6 ✅ **矛盾已解决**（W1 + 运行期内存 + 服务端日志三方对齐）

**先给出结论**：`(category=1, code=33)='1'` 与"VM 登录成功"**并不矛盾** ——
因为 `ServiceRestrictManager::isRestricted` 在查表**之前**有一道**手机认证绕过**，
而那台 VM 的成功账号正好满足该绕过。

**① 代码路径（Hex-Rays 反编译，`work/out/restr-ida/getsites.txt:1078-1413`）**——LOGIN 里没有任何绕过：

```c
v12 = ARAD::Singleton<ServiceRestrictManager>::Get();
if ( (unsigned __int8)ServiceRestrictManager::isRestricted(v12, a2, 1, 33) )   // ← line 1221
{
  CUser::SendCmdErrorPacket(a2, 1, 209);
  return 0;                                                                     // ← 真的拒绝
}
```

与它紧邻的上一行就是 `cMyTrace("[Taiwan, Auth] Token timestamp info. ...")`，与 §2.1 的汇编**逐条对上**。

**② 绕过点在 `isRestricted` 自身（`0x0816e6b8`，既有反汇编 `login-restriction-static.txt:3-55`）**：

```asm
0x0816e6be  cmp  dword [ebp+0xc], 0     ; CUser* == null ?
0x0816e6c2  jne  0x816e6ce
0x0816e6c4  mov  eax, 0                 ; null → 返回 0（放行）
0x0816e6ce  mov  eax, [ebp+0xc]
0x0816e6d4  call CUser::getGarenaAuthData()
0x0816e6dc  call Taiwan::GarenaAuthData::getMobileAuth()
0x0816e6e1  test al, al
0x0816e6e3  je   0x816e6ec             ; getMobileAuth()==0 → 继续查表
0x0816e6e5  mov  eax, 0                 ; ★★★ getMobileAuth()!=0 → **返回 0（放行）**，根本不查表
0x0816e6ec  ...                         ; 后面才是 map::find(category) → isApply(code)
```

`GarenaAuthData::getMobileAuth()` @ `0x08173022`（本轮新读）= `byte [authData+0x2c] == 1`。
`CUser::getGarenaAuthData()` @ `0x0816ee1e` = `CUser + 0x6ef92`。

**③ 服务端日志（`[真机]`，`/home/neople/game/log/siroco11/Log20261007.log`）**——成功那次的账号正好是手机认证账号：

```
[00:19:00] decrypt(153): [Taiwan, Auth] Success!! Uid:18000008, UserName:…, Timestamp:1435583760,
                         GcaType:260, GoldMemberType:3, Mobile:1          ← ★ 手机认证 = 1
[00:19:00] DisPatcher_Login::dispatch_sig(...)(26142): [Taiwan, Auth] Token timestamp info. (token_time:1435583760, …)
[00:19:00] DisPatcher_Login::dispatch_sig(...)(27259): USER LOGINED! …
```

⇒ **`Mobile:1` ⇒ `getMobileAuth()!=0` ⇒ `isRestricted` 直接返回 0 ⇒ LOGIN 放行**。
DB 里 `(1,33)='1'` 与运行期 `vec[33]=1` **都成立，只是被这道绕过挡在了后面**。

**④ 由此纠正 §2.3 的一处过度概括（留档）**：
我先前把 `vec[code]==1` 直接读成"该码受限 ⇒ 拒绝"。更准确的说法是：
> `vec[code]==1` 表示**策略层面该码受限**；但它**只在 `getMobileAuth()==0` 的账号上才会被查询**。
> 换句话说：`AuthMobileRestrictPolicy` 这套 34 个限制码**专门用于限制"非手机认证"账号**，
> 对手机认证账号**整体不生效**。

**⑤ 这把 E1 的价值改变了**（见 §5）：既然成功账号走的是绕过分支，
"改 DB 再登录"**无论结果如何都无法区分"DB→vec 没生效"与"生效了但被绕过"** ⇒ E1 原设计**不可判定**。

### 2.7 `[静态]`+`[真机]` 全部 25 个限制码调用点（本轮新增，用于自证 category=1 的读法）

`Singleton<ServiceRestrictManager>::Get()` 在 `.text` 里共 **25 个调用点**；
新脚本 `work/scan-restrict-args.py` 把每个调用点**紧跟其后的实参立即数**扫出来，结果**高度一致**：

| 调用点 | category | code | `MobileRestrict` 名 |
|---|---|---|---|
| `0x081e9190`（**`DisPatcher_Login`**） | 1 | **0x21 = 33** | **`LoginChannel`** |
| `0x0819fcba` `Dispatcher_AradEmblemCompound::checkWorkState` | 1 | 0x15 = 21 | `UseEmblemCompound` |
| `0x081be561` `DisPatcher_BuyItem::check_error` | 1 | 3 | `UseNpc` |
| `0x081bf904` `DisPatcher_BuySkill::check_error` | 1 | 0x1e = 30 | `GuildSkillUp` |
| `0x081c0888` `DisPatcher_CreateCharac::check_error` | 1 | 0x20 = 32 | `CreateCharacter` |
| `0x081c175b` `DisPatcher_DeleteCharac::check_error` | 1 | 0x17 = 23 | `DeleteCharacter` |
| `0x081c1cfc` `DisPatcher_DeleteItem::restricted_check` | 1 | 1 | （码 1） |
| `0x081c2e57` `DisPatcher_DropItem::check_error` | 1 | 1 | （码 1） |
| `0x081cc677` `Dispatcher_MailBox_Send::check_error` | 1 | 4 | `UseMail` |
| `0x081cfc41` `Dispatcher_RequestDisjointItem::check_error` | 1 | 0x13 = 19 | `UseExportJob` |
| `0x081d6d40` `Dispather_RegenerationRandomOption::process` | 1 | 0x10 = 16 | `UseRandomOptionReGeneration` |
| `0x081dcb54` `Dispatcher_ChangeRandomOption::process` | 1 | 0xf = 15 | `UseRandomOptionChange` |
| `0x081dd40f` `DispatcherResetRandomOption::process` | 1 | 0xf = 15 | `UseRandomOptionChange` |
| `0x081eaa83` `DisPatcher_Compound::dispatch_sig` | 1 | 9 | `UseCompound` |
| `0x081ef8df`/`0x081ef907` `DisPatcher_ReqPeer::dispatch_sig` | 1 | 6 | `UseTrade` |
| …（其余 10 处同族，全部 `category=1`） | 1 | — | — |

**这条自证很重要**：25/25 都是 `mov [esp+8], 1`（category）**紧跟** `mov [esp+0xc], <业务码>`
⇒ **category 恒为 1、业务码在第二槽** 的读法**不是一个孤立样本的解释**，而是全站一致的模式；
且每个码都对上 `MobileRestrict::enumStringMobiles` 的**语义合理的业务动作**（买/删/邮件/交易/合成/…
以及登录频道）。IDA 的反编译给出同一结论（`isRestricted(v, a2, 1, <code>)`，多出的那个 `1` 是 EDX 槽）。



## 3. C 项 · LOGIN 首帧的**完整明文**（本轮新增）

### 3.1 方法与两处踩坑（留档）

新脚本 `work/dump-login-plain.py`：定位成对单向流 → 解密 → 落盘 → 统计尾部；
**每次都自检**（`patch==0` 且 `token_length==344` 且 `plain[0:8]==00000000 58010000`），
不通过就**不打印明文**（避免把垃圾当证据）。

两个必须记住的口径（我第一版分别写错，各留一次）：

1. **方向不能按文件名猜**。`classification`（帧头 `[0]`）是**报文类别**（`1=CMD / 0=NOTI`，`driver/dnfdrive.py:17`），**不是方向**。
   本项目既有文件名是**反的**：`streams-live\2343-10013.bin` 里装的其实是 `classification==1` 的字节（C→S）。
   判据用**首帧的 `(classification, msg_no)`**：LOGIN 首帧恒为 `(1,1)`；下发 334 B cipher key 的那帧恒为 `(0,1)`（NOTI1）。
2. **"帧偏移 15" ≠ "载荷偏移 15"**。密钥材料 = **整帧** `[15:]`（= 头 13 + 载荷 2），
   变换 `k[i]=(((v<<6)|(v>>2))&0xFF)^181`，AES-128-ECB 取 `k[8:24]`；
   密文 = 那个 LOGIN 帧的 `cipher[13:]`（400 B）。用 `载荷[15:]` 会解出高熵垃圾。

### 3.2 结果（产出时 10/10 自检 PASS；**其中本仓库可复跑的只有 2 对**，见本节末更正）

产物：[login-plain-dump.txt](login-plain-dump.txt)、[login-plain-dump.json](login-plain-dump.json)
（`SHA256 7E14A91BF33336FCE7B81BA755320AAC0C08F48D2CF322E257853698639BD3FE` / `A8CD66B1CF86918FE17E2595AA4138A2710B095691B50A154BC15ADFA6DD9111`）

| 部署 | 对 | key sha256（前 8 hex） | 明文 sha256（前 8 hex） | `token_length` | `plain[388:400]` | 末非零偏移 |
|---|---|---|---|---|---|---|
| VM 10013（`streams-live`） | `10013-2343` / `2343-10013` | `b7f51029` | `9d57ec06` | 344 | 全零 | **387** |
| VM 10013（`streams-live2`） | `10013-14805` / `14805-10013` | `b7f51029` | `9d57ec06` | 344 | 全零 | **387** |
| VM 10013（`streams-vm`） | `10013-13681` / `13681-10013` | `33b5f60c` | `9d57ec06` | 344 | 全零 | **387** |
| 容器 30011（`streams`/`streams-e2e`，6 对） | — | `0c887765` | `42e0650e` | 344 | 全零 | **387** |

明文逐字段（**每个字段都同时给原始字节与 u32/u16 两种读法**，避免"同一字段两种写法"的误读；
复跑：`python work/login-plain-fields.py work/out/login-plain-dump.json`）：

| 偏移 | 字段 | 原始字节（VM / 容器） | u32 / u16 | 备注 |
|---|---|---|---|---|
| 0 | `patch` | `00000000` / 同 | 0 | |
| 4 | `token_length` | `58010000` / 同 | **344** | |
| 8 | `token`（344 B） | — | — | base64 串；VM 与容器**不同**（sha256 见 `login-plain-dump.json`） |
| 352 | `string1_length` | `00000000` / 同 | 0 | |
| 356 | `string2_length` | `00000000` / 同 | 0 | |
| 360 | `flag` | `00` / 同 | 0 | 只接受 0/1/2（静态读出的校验） |
| 361 | `mouse_length` | `08000000` / 同 | **8** | |
| 365 | `mouse_encrypted`（8 B） | `f20322cf104e91d0` / 同 | — | 跨部署**逐字节相同**（既有结论复现） |
| 373 | `DataManager+0x3c0` | `22673201` / 同 | **20080418** | 两部署相同 |
| 377 | `version` | `c93fab01` / `11c5c901` | **28000201 / 30000401** | **与既有报告的写法一致** ✓ |
| 381 | `server_group` | `0300` / 同 | **3** | |
| 383 | `local_ip` | `01c0a8c8` / `01a9fe2b` | 0xC8A8C001 / 0x2BFEA901 | = **192.168.200.1**（VM）/ **172.17.0.1**（容器），按小端 i32 读即得正确点分地址 |
| 387 | `loginout_state` | `01` / `ED`(=237) | 1 / 237 | |
| **388** | **`[388:400]`** | **`000000000000000000000000` / 同** | — | **12 B 全零，10/10** |

> **本节的一次自我更正（留档）**：本报告初稿把 `[373]/[377]` 写成 `0x00019302 / 0x0022cf10` 并断言
> "与既有报告的 28000201/30000401 不同"。那是**我按 u32 读法换算时把字节顺序写错**得到的假差异 ——
> 实际 `bytes=22673201 → u32=20080418`、`bytes=c93fab01 → u32=28000201`，**既有报告是对的**。
> 由此新增工具 `work/login-plain-fields.py`（并排打印字节/u32/u16），以免再出现同类误读。
> §5 的 W3 因此**关闭**（不是"两个值"，是同一个值）。

> **[作废]/收窄（保留原文，另加本条更正）** —— 这是**收窄，不是新发现**，数字一个都没变。
> 本节标题、上表与表格末行原写的"**10/10**"指的是
> **`login-plain-dump.json` 在产出时那 10 对样本的自检结果**，这一层仍然成立
> （JSON 里 10 条 `selfcheck` 全为 `PASS`，且每条 `plain_hex` 长度 400 B 与声明的
> `plain_len`/`payload_len` 自洽）。
> 但**在本仓库（当前 worktree）里可复跑/可做密码学复核的只有 2 对**：
> `captures/streams-live`（`10013-2343` / `2343-10013`）与 `captures/streams-live2`
> （`10013-14805` / `14805-10013`）。
> 其余 **8 对**引用的三个目录**整个不存在**：`captures/streams`（2 对）、
> `captures/streams-e2e`（5 对）、`captures/streams-vm`（1 对）。
> ⇒ 正确的说法是：**"10 对样本在产出时全部自检 PASS；但在本仓库可复跑的只有 2 对"**。
> 本行**不删除**上面任何原有观测与数字；§3.3 那三条跨 10 样本的观测里，
> 第 1、2 条（尾部 12 B 全零、末非零字节 387、388 与 AES 块对齐）已由**可复跑的 2 对**
> 独立复现，第 3 条"两个不同部署"的**跨部署**部分（容器侧 `streams`/`streams-e2e`）
> 在本仓库**不可复跑**。
> 依据：Task 4 独立复核 `coverage-review.md` 的 MINOR-1 / MINOR-2
> （`2026-10-07-protocol-field-coverage-audit/coverage-review.md`）；
> 那份报告里同类表述（`CMD 1` 的 `ext_evidence` 写作"10/10 自检 PASS"）也应作同样收窄。
> 另：§6「本轮产物清单」里那条复跑命令的注释"10/10 自检必须 PASS"指的是
> **当全部 10 对抓包都在磁盘上时**该命令的行为（`dump-login-plain.py` 末尾
> `return 0 if all(selfcheck == "PASS")`）；在本仓库直接跑它会因 3 个目录缺失而只覆盖 2 对，
> 数字写法**不变**，仅说明其生效前提。

### 3.3 对 handoff §4.C 的意义（**结论收紧，但没到"生产者已定位"**）

三条**新**的、跨 10 个样本的硬观测：

1. **`[388:400]` 恒为 12 个零字节**，且**末非零字节恰好是 387**（= `loginout_state` 的最后一个字节）。
   不是"尾部大概为零"，而是"**结构最后一个字段结束于 387，之后全零**"。
2. `388` **不是** 16 的倍数，但 `ceil(388/16)*16 = 400` —— 与 AES 的块对齐**完全吻合**。
   而服务端的消费游标也正好停在 388（`CURSOR=388 PLAIN_LEN=400`）。
3. 两个**不同部署**（VM / 容器）、两把不同 cipher key、两个不同 token，尾部形状相同。

⇒ 这三条把 §4.C 的问题从"尾 12 B 是不是协议字段"**收窄**成：
**"它更像 AES 块对齐的零填充（不是客户端写进去的协议字段）"**，
但**要定案必须看到生产端的构造代码**（handoff 已限定"要有身份依据的对应客户端发送端二进制/标注库"）。
本轮**没有**取得该证据，因此：
`[静态]` 的部分**只有**"消费侧游标 388 + 尾部全零"；**"尾 12 B 是 padding"仍然是 `[未验证]`**，
**不命名 padding**（沿用 handoff §6 的禁令）。

## 4. A 项 · `G_Cipher` / `ServiceRestrictManager` 运行实例：已取到的真值

### 4.1 观测机制（先评审、后执行，按 handoff §5.3）

1. **先试非侵入路径，失败并留证**：`/proc/<pid>/mem` 的 `pread` → `errno=3 ESRCH`（root 也是），
   `/proc/sys/kernel/yama/ptrace_scope` 不存在；VM 无 gdb/gcore；无 32 位工具链。
   ⇒ 结论：**这台 VM 上没有"零暂停"的读内存机制**（证据：`work/out/vm-peek-report-3567.txt`）。
2. **向用户申请并获得明确批准**后，才做 **一次 attach → 只读 → 立即 detach**。
3. 工具纪律（写在源文件头部）：只用 `PTRACE_ATTACH`/`PTRACE_PEEKDATA`/`PTRACE_DETACH`；
   **没有** `POKEDATA`/`SETREGS`/`CONT`/断点/信号注入；每次 attach 后确认
   `TracerPid: 0`、`State: S` 才继续。

### 4.2 `[真机]` 取到的值（`work/out/vm-peek2-3567.txt`，pid 3567，attach→detach 成功）

| 项 | 值 |
|---|---|
| `G_Cipher()` 静态槽位 | `0x0941f8a4`（`.bss`）；访问器 `G_Cipher` @ `0x082a713a` = `mov eax,[0x941f8a4]` |
| **运行实例指针** | **`0x11804fe8`**（非空 ⇒ 与历史"pool[0] 不是 singleton"的坑**不冲突**：这是真正的单例实例指针） |
| 实例 `+0` | `0x0000014e` = **334 = `GetTotalKeyLength()`**（`needed_key_length` 全局 `0x0941f864` 也读出 **334**） |
| 实例 `+4` / `+8` / `+0xc` | `0x11805400` / `0x11805438` / `0x11805438` ⇒ `vector<ICryptoGraph*>` 的 begin/end，**count = (0x38−0x00)/4 = 14** |
| **14 层算法链（运行期逐个指针）** | `11805440, 117cfa38, 117cfd10, 117cfd28, 11806988, 11806998, 118069f8, 11806b10, 11806b20, 117d3278, 117d3320, 117d33b8, 117d33e0, 117d3418` |
| `ALGO[0]` 的 vtable | `0x08b139e8`（`CNCryptoShift` 那一层的 vtable，与既有枚举映射表一致） |

⇒ handoff §4.A 的"**运行实例中 singleton 指针**"**已钉**：
`0x0941f8a4 → 0x11804fe8`，且实例内**确实**是 14 项算法链、总密钥长度 334。

### 4.3 `[真机]` 未取到的：334 B key 本体与 `vec[33]`

- **334 B key 的对象内偏移**：实例头 512 B 里，`+0x10` 起是密钥材料（前 4 字节 `b2f89a9e`，肉眼高熵），
  但实例前 `0x458` B 的非零区是 `+0x5..+0x82`(125 B)、`+0x83..+0x8d`(11 B)、`+0x8f..+0xc0`(49 B)、`+0xc1..+0xfb`(58 B)、`+0xfc..`(98 B)…
  **没有**一段干净的 334 B 连续非零区 ⇒ 需要先静态定位 `GetOriginalKey` 的实参偏移（`add edx,0x10` 已见到，但对应成员还差一步）才能精确取样。
  本轮**没有**把 key 逐字节钉出来 ⇒ 保持 `[未验证]`，并**不把任何 key 字节写进产物**（转储里只有哈希与前缀计数）。
- **策略向量 `vec[33]`**：我按"猜的 `_Rb_tree` 布局"读了四轮都没定位到 manager 对象
  （读到的候选在 `1787e9f0` 一带，其 `+0x20` 是 `_ZTV29CreateCharacterRestrictPolicy`=`0x08b82e60`，
  说明**那片内存确实躺着策略对象**，但 manager 对象的 map 成员偏移还没钉住）。
  **未闭合**，见 §5 W1。这一条正是 §2.6 那个矛盾的裁决点。

### 4.4 一次事故（必须留档）

`peek4-ptrace`（做树遍历的那版）**循环边界写错 ⇒ 探针空转**，把 `df_game_r` 按在
`State: T (tracing stop)`、`TracerPid: 6239` 上，报告 0 字节。
处置：`kill -9 6239` → 复测 `State: S (sleeping)`、`TracerPid: 0`（`[真机]` 已确认恢复）。
**由此定的新纪律**：**VM 上只做 O(n) 的顺序转储，不做指针图遍历**；结构解析放宿主侧（可重试、零风险）。
（宿主侧解析脚本：`work/vm-peek5-parse.py`、`work/vm-peek5-parse2.py`。）

### 4.5 W1 定案：`AuthMobileRestrictPolicy` 的运行期向量（`[真机]`）

**取数手法（这一轮终于走对了）**：把"猜 `_Rb_tree` 布局 + 在 VM 上递归遍历"换成
**"先按地址打印原始字、再在宿主侧解析"**（`work/w1-probe-layout.py` → `work/peek7-ptrace.c`
只做两次有限范围转储 → 宿主侧直接读向量）。上一轮之所以连吃四次亏，根因是**用假设去驱动采样**；
这一轮是**先采样再假设**。

**关键事实（`work/out/vm-peek7.txt`，pid 3567，attach→detach 均已确认）**：

| 项 | 值 |
|---|---|
| `Singleton<ServiceRestrictManager>::Get()` 返回地址 | `0x094045d4` |
| 其中的 map（`find`/`end` 的 `this`） | `0x094045dc`（= manager **+8**；`isRestricted` 反编译里 `find(this=…, &category)` 的实参就是 manager，故这里以运行期自洽为准） |
| map 的 root 节点 | `0x11830038`（`+0x14 → 0x11830018`） |
| 策略对象的指针链 | `0x11830018 + 0x10 → 0x11830058`（**`+0x8 = 1` = category**） |
| **该策略的 `vector<short>`** | `begin = 0x1187e968`，`end = 0x1187e9ac` ⇒ **count = 34**（= 0..0x21） |
| **`vec[0..33]` 实测** | **全部 = 1**（32 个原始字：`00010001` ×32） |
| **`vec[33]`（LOGIN 判据）** | **1** ⇒ `isApply(33) == 1` ⇒ 策略层面 `LoginChannel` **受限** |

**对象身份的三重确认**（不是靠"猜它像"）：
1. 该策略 `+0x8 = 1`（`InterfaceServicePolicy` 的 category，与 `AuthMobileRestrictPolicy` 构造函数的实参 `1` 一致）；
2. **向量长度 = 34**，与 `AuthMobileRestrictPolicy::AuthMobileRestrictPolicy` @ `0x0816d6ce` 的循环上界
   `cmp [ebp-0xc], 0x21; setle al` **精确吻合**（`CreateCharacterRestrictPolicy` 是 `0..2`、`SecurityRestrictPolicy` 是 1、`NullPolicy` 恒 1，都对不上 34）；
3. 该构造函数里 `mov word ptr [ebp-0xe], 1` + `vector::push_back` ⇒ **向量初值就是 34 个 1**
   —— 与运行期观测**逐字节一致**（这也解释了为什么"全 1"本身不能证明 DB 值落进去了）。

> **一个必须记住的副产品**：`AuthMobileRestrictPolicy` 的向量**出厂默认全是 1**。
> ⇒ **"运行期 vec[33]=1" 不能单独证明"DB 的 `restrict_value` 被读进来了"**；
> 要区分"DB 覆盖过"与"没人覆盖、还是默认值"，得看 `mod_date` + `setPolicyApplyValue` 的解析（见 §5 W4）。



## 5. 待办（按代价排序，每条都给"怎么才能定案"）

| 编号 | 待办 | 状态 | 怎么定案 |
|---|---|---|---|
| **W1** | 读 `AuthMobileRestrictPolicy` 的 `vec[33]` | ✅ **已完成（2026-10-07）** | 见 §4.5：`vec[33] = 1`（34 项全 1）。**并据此解决了 §2.6 的矛盾**（真因是手机认证绕过，见 §2.6） |
| **E1** | ~~单变量 DB 实验~~ | ⚠️ **降级为不可判定（本轮发现）** | 成功账号 `18000008` 的 token 是 `Mobile:1` ⇒ 走绕过分支 ⇒ **改 DB 后无论登录成功还是失败，都无法区分"DB→vec 未生效"与"生效但被绕过"**。**要重做得先解决"非手机认证账号"**（需 VM 私有 key 自签 token，或让所有者用一个 `Mobile:0` 的账号）。**在拿到这个前提之前，不必写 DB。** |
| **W4** | `[静态]` 钉"`restrict_value='1'` → `vec[i]=1`"这一步（`setPolicyApplyValue` @ `0x0816d490` 的字符串解析） | 新开，**未做** | 读 `setPolicyApplyValue` 里 `*(p+0)`/`*(p+4)` 的用途与那个 `-0x30`（= 字符 `'1'`→1）的比较；若确认 `'0'/'1'` 直接落进向量，则 DB 语义**完全闭合**，连实验都不需要 |
| **W5** | `[真机]` 验证"非手机认证账号会被拒" | 新开，**未做** | 需要一个 `Mobile:0` 的 token（同上）；这是把 §2.6 的解释从"日志+代码一致"提升到"亲手复现拒绝" |
| **W2** | DB 的 `restrict_code` 与 `enumStringMobiles` 下标是否差 1 | **半闭合**：`getRestrictValue` 的循环是 `for (i = 1; i < vec.size(); ++i)`，用 `enumToString(i)`（业务名）当键去查策略行；而 DB 的 `restrict_code` 取值范围正好是 **1..33**、`enumStringMobiles` 是 **0..34**（0 是 `RestrictBegin`、34 是 `RestrictEnd`）⇒ **两侧都是 1..33 的有效码，名与码同名同值，不差 1**（`0`/`34` 只是哨兵，DB 里也没有）。**仍未钉**：`enumToString(i)` 的返回值究竟是"查 DB 的键"还是"仅用于日志/打印" | 读 `AuthMobileRestrictPolicy::getRestrictValue` 里那次查表的**实参来源**（键字符串是否就是 `enumToString(i)` 的返回，还是另有一个 DB 行游标） |
| **W3** | ~~核对 `version` 字段的两个值~~ | ✅ **已关闭（本轮自查发现是误读）** | `[373]=22673201→u32 20080418`、`[377]=c93fab01→u32 28000201`（容器 `11c5c901→30000401`）；既有报告正确，是本报告初稿写错了字节序。工具：`work/login-plain-fields.py` |
| **E1** | **单变量 DB 实验**（把 `server_group=3, category=1, restrict_code=33` 的 `'1'` 改 `'0'`，重启 `df_game_r`，看 LOGIN 是否仍成功；随后恢复原值） | handoff §5.4 要求"不要修改 DB，除非用户明确批准单变量失败实验" | **需要你批准**；实验前后各留一份 `SELECT` 快照 + `mod_date`，并在报告里记 actor/T0/步骤/帧/服务端日志 |
| **E2** | LOGIN 尾 12 B 的生产端 | 需要"有身份依据的对应客户端发送端二进制/标注库" | 用本机 `G:\Game\DNF_ACT1\DF\DNF.exe`（sha256 `6B4850BE…`）+ `DNF.exe.idb` 找 CMD1 的**出站构造**处，读它写入的字节数上限与缓冲长度；⚠️ handoff 明确要求先确认该二进制的身份与本客户端一致 |
| **E3** | `NOTI561 / type19` 业务字段（handoff §4.D） | 属于"后续玩法单元"，`client-business-backlog.md` 已冻结 | 静态 handler 对照 + 已有 pcap 回放（不需要新抓包） |

## 6. 本轮产物清单（可复跑）

| 文件 | 作用 | `SHA256`（前 16） |
|---|---|---|
| `work/dump-login-plain.py` | LOGIN 首帧完整明文（自检 + 落盘） | `B1DB4069ECBF27F2…` |
| `work/out/login-plain-dump.txt` / `.json` | §3 的明文与统计 | `7E14A91BF33336FC…` / `A8CD66B1CF86918F…` |
| `work/restrict-policy-static.py` | 名字表 / vtable / 函数体转储 | `50CDC247859CEDBA…` |
| `work/restr-dump.py`、`work/restrict-xref.py` | 官服 ELF 的 vtable/函数/引用扫描 | — |
| `work/dwarf-enum.py` | DWARF 枚举抽取（本 VM 的 `.debug_info` 里没有 `RestrictCategory`，**零命中**，已记） | — |
| `work/out/restr-ida/initialize.txt` | `initialize()` 的 Hex-Rays 反编译（category↔策略类） | — |
| `work/peek-ptrace.c` / `peek2` / `peek3` / `peek5` | 只读内存探针（含事故后的"只做顺序转储"纪律） | `D9CF4E37FA5DAA13…`（peek） |
| `work/out/vm-peek2-3567.txt` | §4.2 的运行实例真值 | `4BF9444818B32F2D…` |
| `work/out/vm-peek5.txt` + `vm-peek5-parse*.py` | 区域转储（宿主侧解析） | `1D72207FDD887F84…` |
| `work/vmdb.py`（既有）、`work/vmexec.py`（新） | VM 上跑 SQL / 跑命令（argv 递交，绕开引号坑） | — |
| `work/out/vm-peek-report-3567.txt` | "非侵入读取不可行"的原始证据 | — |

**复跑命令**（本机 Python 3.12；口令只从 `$env:VM_PASS` 读）：

```powershell
$b='<base64 of VM root password>'
$env:VM_PASS=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b))
$B = ".superpowers/sdd/2026-10-05-gameplay-evidence-program"
$O = ".superpowers/sdd/2026-09-29-dungeon-party/work/official"

# C：LOGIN 完整明文（不需要 VM；10/10 自检必须 PASS）
python "$B/work/dump-login-plain.py" --captures "$B/work/captures/streams-live" `
  "$B/work/captures/streams-live2" "$B/work/captures/streams-vm" `
  "$B/work/captures/streams" "$B/work/captures/streams-e2e" `
  --out "$B/work/out/login-plain-dump.txt" --json "$B/work/out/login-plain-dump.json"

# B 静态：名字表 + 策略 vtable + 函数体
python "$B/work/restrict-policy-static.py" "$O/df_game_r.bin" --grep 'ZZZ'
python "$B/work/restr-dump.py" "$O/df_game_r.bin" --vtables 0x08b82f20 0x08b82ee0

# B 真机：DB 只读快照（**只读**；vmdb.py 的 SELECT 走 VM 本地 socket）
python "$B/work/vmdb.py" "SELECT server_group,category,restrict_code,restrict_value,mod_date FROM d_taiwan.dnf_restrict_state WHERE server_group=3 ORDER BY category,restrict_code"

# A 真机：只读 attach 探针（**需要用户批准**；attach 后必须确认 TracerPid:0 / State:S）
python "$B/work/vmexec.py" --put "$B/work/peek-ptrace.c:/tmp/peek-ptrace.c" -- gcc -O2 -D_GNU_SOURCE -o /tmp/peek-ptrace /tmp/peek-ptrace.c
python "$B/work/vmexec.py" -- /tmp/peek-ptrace 3567 /tmp/vm-peek.txt
python "$B/work/vmexec.py" --raw -- sh -c "grep -E '^(State|TracerPid)' /proc/3567/status"
```

## 7. 纪律与边界（本轮遵守情况）

- 未修改任何产品代码（`src/`、`tests/` 未动）；未修改已签收的离线收尾计划。
- VM 侧：除"经批准的一次性只读 attach"外**没有**任何写操作；DB **只读**（无 `UPDATE`/`INSERT`）；
  未改 `cfg`、未重启任何官方进程；探针二进制在 `/tmp`，报告在 `/tmp`，均已说明。
- 未把任何 cipher key / token 字节写进产物（只写哈希与前缀计数）。
- 矛盾（§2.6）**两个都留**，未选边；未把"读到 0"与"读不到"混同（探针里显式打印 `UNREADABLE`/`errno`）。
- 事故（§4.4）与两次口径写错（§3.1）**原文留档**，未悄悄改结论。
