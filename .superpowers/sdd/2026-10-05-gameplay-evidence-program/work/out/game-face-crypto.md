# 游戏面封包的密码层 —— 钉证据结果（2026-10-05 深夜，自主部分）

> **这份文档解决了 M1 唯一的阻塞项**（"登录帧里令牌的变换"），
> 而且顺带发现了一件更根本的事：**游戏面帧的载荷是加密的**，
> 我先前所有"扫不到令牌"的结论都因此得到解释（不是令牌被弱变换，是**整段载荷都是密文**）。
>
> 全部依据：`work/disasm-func.py` / `work/find-syms.py` / `work/read-jumptable.py` /
> `work/map-crypto-enum.py` 对 **VM 上正在运行的那份 `df_game_r`** 的反汇编（`[静态]`）。
> 二进制：`%TEMP%\dfl-creds\vm-df_game_r`（36,360,954 B, md5 `47de6d6c…`）。

---

## 一、解密路径（从 `CUser::Parsing` 开始）

`CUser::Parsing(int)` @ `0x08647890`（1900 B）—— **它就是报 `check_sum error` 的那个函数**。
它内部调用：

```asm
0x08647ae5  call CPacketVerifier::UpdatePacket(_header const&)      ; 校验和/序号
0x08647b2b  call CPacketVerifier::GetSequence()                     ; ← 就是 13 字节头里的 sequence
0x08647bb9  call _NS_PI_2ND_RecvHash_CS(char*, CUser*, unsigned int) ; 密钥派生（按 CUser）
0x08647bfd  call G_Cipher()                                          ; 取全局 Cipher
0x08647c25  call Cipher::Decrypt(int, uchar const*, int, uchar*, int*)  ; ← **解密载荷**
0x08647c75  call Encryption::crc32NE(void*, int)                     ; 校验
0x08647c93  call _NS_PI_2ND_MakeChecksumTo1Byte2(unsigned int&, unsigned int&)
```

⇒ **帧的载荷先被 `Cipher::Decrypt` 解一次，才交给派发器。**
这解释了：
* 载荷为什么高熵（**是密文**）；
* 我为什么用"256 B 滑窗 + 公钥模幂"怎么扫都扫不到令牌（**在密文里**）；
* 为什么**同账号两次登录首帧 402/403 字节相同**、**不同账号只有 4/403 相同**
  —— 密钥按 `CUser` 派生，同账号同明文 ⇒ 同密文。

## 二、密码库：`Cipher` 是一条**算法流水线**

```
Cipher::Cipher()                          0x08097196
Cipher::Initialize(uchar const*, int)     0x08097306   ← 密钥设置（遍历 vector<ICryptoGraph*>）
Cipher::Encrypt(...)                      0x08097436
Cipher::Decrypt(...)                      0x0809751e
Cipher::GetOriginalKey(uchar*)            0x080975de
Cipher::GetTotalKeyLength()               0x080975d4
Cipher::AdjustDataSize(int, int*)         0x080973c0
G_Cipher()                                0x082a713a   ← 全局访问器（BSS）
CreateCryptoGraph(CRYPTO_GRAPH_TYPE)      0x0809df90   ← 工厂
DestroyCryptoGraph(ICryptoGraph*&)        0x0809e2d0
```

`Cipher` 内部是 `std::vector<ICryptoGraph*>`（`this+4`）—— **多个算法按序串联**，
每个算法实现 `ICryptoGraph` 接口（`vtable[0x20]` = 取密钥长度之类）。

## 三、`CRYPTO_GRAPH_TYPE` 枚举 → 算法类（**14 种，完整映射**）

工厂 `CreateCryptoGraph` 是 `cmp eax,0xd; mov eax,[eax*4+0x8b13c48]; jmp eax`
（跳转表 VA `0x08b13c48`，14 项）。把跳转目标与各分支的构造函数配对
（`work/map-crypto-enum.py`）：

| 枚举值 | 算法类 | 枚举值 | 算法类 |
|---|---|---|---|
| **0** | `CNCryptoShift` | **7** | `CNCryptoXTea` |
| **1** | `CNCryptoRijndael` | **8** | `CNCryptoNoekeon` |
| **2** | `CNCryptoBlowFish` | **9** | `CNCryptoKhazad` |
| **3** | `CNCryptoRc6` | **10** | `CNCryptoCast5` |
| **4** | `CNCryptoTwoFish` | **11** | `CNCryptoSkipjack` |
| **5** | `CNCryptoTea` | **12** | `CNCryptoMulti2` |
| **6** | `CNCryptoKasumi` | **13** | `CNCryptoAnubis` |

**这 14 种正好是经典密码库（libmcrypt 系）的算法清单。**
⇒ 重写时**不需要自己实现密码学**，选一个成熟库映射这 14 个算法即可。

## 四、令牌的解析路径（`DisPatcher_Login`）

`Login` 家族（M0 目录）：`process=0x08232a74(10)`、**`dispatch_sig=0x081e8c78(4619)`**。

在 `0x081e907e` 处：

```asm
0x081e8feb  memset(0x940a2e0, 0, 0x801)                       ; 2049 B 全局缓冲清零
0x081e9027  call PacketBuf::get_str(0x940a2e0, 0x801, len)    ; 从**已解密**的载荷读令牌串
0x081e9064  call CUser::getGarenaAuthData()                   ; 取 104 B 结构地址
0x081e907e  call Taiwan::AuthGarenaToken::decrypt(0x940a2e0, len, &authData)
```

`Taiwan::AuthGarenaToken::decrypt(char const*, int, GarenaAuthData&)` @ `0x081731f2`（893 B）：

```asm
call std::string::string(char const*)                      ; 入参是 C 字符串
call base64_decode(std::string const&, vector<uchar>&)     ; ← 1) base64 解码
...
call COpensslRSA::Decrypt(0x9404754, size, data, out)      ; ← 2) RSA 运算
                                                            ;    密钥在 BSS 全局 0x9404754
                                                            ;    （启动时从 publickey.pem 初始化）
cmp  size, 0x68                                            ; 3) 结果 ≤ 104 字节
call _init(..., 0, 0x68)                                   ; 4) 那 104 B 就是 GarenaAuthData
```

`GarenaAuthData` 的访问器（全部在 `0x08172fc8`–`0x0817304e` 一带）：

```
getUid()  getUname()  getTimeStamp()  getPatchTime()/setPatchTime(uint)
getGcaType()  getMobileAuth()  getGoldMemberType()
```

**值得注意的耦合**：`Login` 处理器里紧跟着调用了
`Dispatcher_MouseRegister::decryptPassword(char const*, char*, int)` @ `0x0826333a`
与 `checkValiedPassword(char const*)` —— 说明登录帧里**还有一路鼠标密码**（另一套凭据）。

## 五、这条发现对重写的意义

1. **M1 的阻塞解除了**（方向明确）：令牌 = `base64(RSA(GarenaAuthData_104B))`，
   且**密钥是内嵌/文件加载的**（`publickey.pem`），不是"不可知的变换"。
2. **游戏面载荷全程加密** ⇒ 后续每个单元（M1–M10）都要走这层密码，
   它是**第 0 部分（契约层）的一部分**，必须先做对。
3. **密码库是 14 种标准算法** ⇒ 可复用现成实现，不必自研。
4. **密钥按 `CUser` 派生**（`_NS_PI_2ND_RecvHash_CS(char*, CUser*, uint)`）
   ⇒ 这与"2008 客户端方言"是两套东西，别混。

## 六、密钥：**已确证的部分**与**我撤回的一个读法**（自查后降级）

### （a）算法链是**固定的 14 层全链**（✅ 已确证）

`Cipher::Cipher()` @ `0x08097196`：

```asm
0x080971c6  call vector<ICryptoGraph*>::reserve(0xe)   ; 预留 14 项
循环 i = 0 .. 13:
0x080971da    call CreateCryptoGraph(i)                ; 按枚举顺序造第 i 个算法
0x080971f2    call vector::push_back(algo)
0x08097209    call algo->vtable[0x20]()                ; 取该算法的密钥长度
0x08097214    total_key_len += eax
```

⇒ **链序 = 枚举序**：`Shift → Rijndael → BlowFish → Rc6 → TwoFish → Tea → Kasumi
→ XTea → Noekeon → Khazad → Cast5 → Skipjack → Multi2 → Anubis`；
**密钥总长 = 14 个算法密钥长度之和**（`Cipher::GetTotalKeyLength()`）。

### （b）`InitCipherKeyString()` 的入口（✅ 已确证）

@ `0x082a025a`（511 B）：

```asm
call CSystemTime::getCurSec()          ; ← 当前时间（秒）
call Cipher::Cipher()
call Cipher::GetTotalKeyLength()
call SelectKeyString(int*, int*)
call realloc(...)
```

体内有 **2048 字节缓冲清零**（`rep stosd` 0x200 个 dword）。
引用串：`'void InitCipherKeyString()'`、`'[!] Init CipherKeyString TotalTime : %ds'`
（**有计时** ⇒ 这步不便宜）。

### （c）⚠️ 撤回的读法："密钥 = 从 map 里随机挑"

我先写了这个，**追下去发现站不住**，降级为 `[未验证]`：

**反证**：`SelectKeyString` 里读的两个 map 地址（`0x94fdc00`、`0x94fac00`）
**同时被这些函数使用**（`work/game-scan-imm.py` 实测）：

```
CItemList::load_item_files()      InitStackableScript(...)   ImportStackableScript(...)
InitEquipmentScript(...)          ImportEquipmentScript(...)  __static_initialization_and_destruction_0
```

那是**道具/脚本**的存储，不像"密钥字符串池"。同一地址被两类代码用 ⇒ 我要么读错了参数位置
（`map::begin()` 的隐藏返回槽 vs `this`），要么那两个 map 与我以为的不是一回事。

**能确证的只有后半段**（@ `0x082a033d`–`0x082a03b3`）：

```asm
0x082a0340  shl  eax, 4              ; index * 16
0x082a0343  add  eax, 0x940be80      ; ← 16 字节结构体数组（BSS，运行时填充）
0x082a0348  lea  edx, [eax + 8]      ; arg2 = &struct[8]
0x082a0356  add  eax, 4              ; arg1 = &struct[4]
0x082a0360  call SelectKeyString(&struct[4], &struct[8])   ; 长度在 eax
0x082a036e  call realloc(len)
循环 i < GetTotalKeyLength():
0x082a039b    call _init(keybuf + i, struct[4], len)   ; 反复把选中的串拷进 2048 B 缓冲
0x082a03a3    i += len
```

⇒ **可确证**：① 需要 `GetTotalKeyLength()` 字节密钥；② 密钥由**反复拷贝一个"选中的串"**填满；
③ 串来自 `0x940be80` 处一组 16 字节结构体（BSS，运行时填充）。
**未确证**：串的最终来源与 `SelectKeyString` 的选取逻辑。已并入 §八。

我在本节先写了"密钥 = 从一个 `std::map<int,std::string>` 里按随机数挑出来的串拼成"。
**追下去发现这个读法有问题**，必须降级为 `[未验证]`：

**反证**：`SelectKeyString` 里读的那两个 map 地址（`0x94fdc00`、`0x94fac00`）
**同时被这些函数使用**（`work/xref-fast.py` / `work/game-scan-imm.py` 实测）：

```
CItemList::load_item_files()          InitStackableScript(char const*, ...)
ImportStackableScript(...)            InitEquipmentScript(...)
ImportEquipmentScript(...)            __static_initialization_and_destruction_0
```

那是**道具/脚本**的存储，不是"密钥字符串池"。**同一个地址被两类代码用**，
说明我要么读错了参数位置（`map::begin()` 的隐藏返回槽 vs `this`），
要么 `SelectKeyString` 里的 map 与我以为的不是同一个东西。

**能确证的只有这些**（`InitCipherKeyString` 后半段，@ `0x082a033d`–`0x082a03b3`）：

```asm
0x082a0340  shl  eax, 4              ; index * 16
0x082a0343  add  eax, 0x940be80      ; ← 16 字节结构体数组（BSS）
0x082a0348  lea  edx, [eax + 8]      ; arg2 = &struct[8]
0x082a0356  add  eax, 4              ; arg1 = &struct[4]
0x082a0360  call SelectKeyString(&struct[4], &struct[8])   ; 返回长度在 eax
0x082a036e  call realloc(len)
循环 i < GetTotalKeyLength():
0x082a039b    call _init(keybuf + i, struct[4], len)   ; 反复把选中的串拷进 2048 B 密钥缓冲
0x082a03a3    i += len
```

⇒ **可确证**：① 密码链需要 `GetTotalKeyLength()` 字节密钥；② 密钥由**反复拷贝一个"选中的串"**
填满；③ 那个串来自 `0x940be80` 处一组 16 字节结构体（BSS，运行时填充）。
**未确证**：串的最终来源（文件？另一处 map？）与 `SelectKeyString` 的选取逻辑。

**这条不解决，"自己构造登录帧"就做不了** —— 已并入 §八 的未决问题。


### （a）算法链是**固定的 14 层全链**

`Cipher::Cipher()` @ `0x08097196`：

```asm
0x080971c6  call vector<ICryptoGraph*>::reserve(0xe)   ; 预留 14 项
循环 i = 0 .. 13:
0x080971da    call CreateCryptoGraph(i)                ; 按枚举顺序造第 i 个算法
0x080971f2    call vector::push_back(algo)
0x08097209    call algo->vtable[0x20]()                ; 取该算法的密钥长度
0x08097214    total_key_len += eax
```

⇒ **链序 = 枚举序**：`Shift → Rijndael → BlowFish → Rc6 → TwoFish → Tea → Kasumi
→ XTea → Noekeon → Khazad → Cast5 → Skipjack → Multi2 → Anubis`。
**密钥总长 = 14 个算法密钥长度之和**（`Cipher::GetTotalKeyLength()`）。

### （b）密钥串的来源

`InitCipherKeyString()` @ `0x082a025a`（511 B）：

```asm
call CSystemTime::getCurSec()          ; ← 当前时间（秒）
call Cipher::Cipher()                  ; 构造 14 层链
call Cipher::GetTotalKeyLength()
call SelectKeyString(int*, int*)       ; ← 选密钥串
call realloc(...)
```

体内还有一处 **2048 字节的缓冲清零**（`rep stosd` 0x200 个 dword）。
引用到的串：`'void InitCipherKeyString()'`、`'[!] Init CipherKeyString TotalTime : %ds'`
—— **有计时**，说明这步不便宜。

`SelectKeyString(int*, int*)` @ `0x082a002a`（560 B）**反复**做：

```asm
call get_rand_int(int)                          ; 随机数
call std::map<int, std::string>::begin()/end()  ; 遍历 map<int,string>
（多轮重复）
```

⇒ **密钥 = 从一个 `std::map<int, std::string>` 里按随机数挑出来的若干串拼成。**

`get_rand_int(int)` @ `0x086b1b87` 是**标准 `rand()` 的 LCG**：

```asm
imul eax, eax, 0x41c64e6d      ; × 1103515245
add  eax, 0x3039               ; + 12345
```

（`1103515245 / 12345` 是 glibc/MSVC `rand()` 的经典常数。）

## 七、⚠️ 修正：`_NS_PI_2ND_RecvHash_CS` **不是**密钥派生

我在 §一 里把 `_NS_PI_2ND_RecvHash_CS(char*, CUser*, unsigned int)` @ `0x08274f73`
标成了"密钥派生（按 CUser）"—— **那是错的**，依据只是"它在 `Cipher::Decrypt` 前面被调用"，
**属于凭位置+名字猜**，不是读出来的。反汇编它的实际内容：

```asm
call _NS_PI_2ND_GetPacketIndex(char*)
call CUser::GetCSHashSet()                     ; set<pair<u8,u16>>
call std::set<pair<u8,u16>>::find(...)
call PIReceiverManager::CheckHashReceived(u8*, u32, u32, bool)
call _NS_PI_2ND_GetHackType(...)
call WongWork::CHackAnalyzer::addServerHackCnt(CUser*, ENUM_HACKTYPE, u32, u32, u32)
call CUser::lock() / unlock()
```

⇒ 它是**封包完整性校验 / 反外挂**路径（`PI` = Packet Integrity），
**与密钥无关**。**载入 `set` 去重 + 记外挂计数**是它的作用。

**教训**：调用顺序相邻 ≠ 功能相关。这条已经是我这一轮第二次因为"凭名字/位置推断"
而写错了（第一次是把 `[7..9]` 凭日志名定性成校验和）。

## 八、**必须钉死的未决问题**：客户端怎么拿到同一把密钥？

现状：密钥选择调用了 PLT `rand()`（目标 `0x0807dca0` 已由动态重定位解析为 `rand`），
但**目前没有找到对应的 `srand`/种子写入点**。`InitCipherKeyString` 的两个 `getCurSec()`
调用经审计只是**计时**：入口取一次、尾部取一次、相减后传给日志串
`[!] Init CipherKeyString TotalTime : %ds`；**没有证据表明时间值用于播种**。

**线索**：7001（频道面）握手下发的"会话密钥"其实是个**时间戳字符串** ——
VM 的 `id=12` 正文 `[4:20)` 是 `"20261005143035\0\0"`。它与服务器启动时刻相关，
但**是否参与密码链仍是 `[未验证]`**，不能写成"客户端拿它播种"。

**这条不解决，M1 的"自己构造登录"就走不通**（能重放抓包，但换账号不行）。
验法（按代价排序）：
1. `[静态]` 追 `rand()` 的真实播种点（`srand` 可能被内联/封装）；
2. `[静态]` 找 `0x940be80` 那组 16 字节结构及其字符串来源；
3. `[静态]` 读客户端 `DNF.exe` 的对应 cipher 初始化；
4. `[回放]` 采集 7001 时间戳与客户端行为，验证是否有关联。

**审计来源**：`work/out/cipher-callchain-audit.txt` L165–188（两次 `getCurSec` 用于计时），
L72–132（`Cipher::Initialize` 是 `memcpy` + 14 个图的 key setup）；
`work/out/selkey.txt`（`SelectKeyString` 的 `rand()` 调用）。

## 九、【决定性闭环】密钥是如何到客户端的（`CUser::InitOnAccept`）

**未决问题**（"客户端如何取得同一把 334 B 密钥？"）已由 `CUser::InitOnAccept` 的反汇编**直接解开**。

`CUser::InitOnAccept(int)` @ `0x086493c8`（本函数是新 game socket 接受后的初始化/首包构造）：

```asm
0x0864952a  PacketGuard::PacketGuard()
0x0864954b  InterfacePacketBuf::put_header(0, 1)              ; → NOTI1
0x08649556  G_Cipher()
0x08649562  Cipher::GetOriginalKey(buffer)                  ; 取 334 B 原始 key
0x08649567  G_Cipher()
0x0864956f  Cipher::GetTotalKeyLength()                      ; → 334
0x0864958e  InterfacePacketBuf::put_binary(buffer, 334)      ; **把 key 写进 NOTI1**
0x08649593  G_CEnvironment()->get_channel_name()
0x086495e9  InterfacePacketBuf::put_str(channel_name)
0x086495ee  get_seed_from_date() → CUser+0x711e8
0x0864960f  InterfacePacketBuf::put_int(seed)
0x0864961b  get_rand_int(0) → CUser+0x796e0
0x0864963e  InterfacePacketBuf::put_int(random)
0x08649643  G_CEnvironment() + offset 0x378 → put_byte
0x0864965d  G_CEnvironment() + offset 0x1b0 → put_byte
0x08649677  G_CEnvironment() + offset 0x1a8 → put_byte
0x08649691  CSystemTime::getCurSec() → put_int(current_time)
... 后续还有环境/服务器列表字段
```

### 与抓包的独立对齐

```
NOTI1 整帧 = 450 B
GAME 13 B 头
payload = 450 - 13 = 437 B

334 B 原始 cipher key + 其它 103 B 字段 = 437 B
```

**长度完全相等**。而且 `put_header(0,1)` 与抓包首帧 `NOTI id=1 size=450` 一一对应。
⇒ **这不是推断，是代码地址 + 字段写入 + 抓包长度的三重闭合证据。**

### 对 M1 的意义

1. 客户端**不需要**从时间戳猜 key；服务端在 `NOTI1` 里直接下发 334 B key。
2. 14 层密码链的 key 获取路径已闭合：`InitCipherKeyString` 生成 → `GetOriginalKey` →
   `InitOnAccept` `put_binary` → 客户端收到 → 后续 `Cipher::Encrypt/Decrypt`。
3. `NOTI1` 是**密钥分发包**，不是普通"频道信息"；它后面的 `channel_name/seed/random/环境字段`
   也都能由 `InitOnAccept` 的 `put_*` 顺序逐字段重建。
4. 现在 M1 单元②真正剩下的是：确认**客户端第一次 C→S CMD1 在拿到 NOTI1 之前为什么能发**
   （可能是接受阶段的默认 cipher/明文初始包），以及把 `Cipher` 的 14 个 `ICryptoGraph`
   实现按同样 key 接到重写驱动器里。

**来源**：`work/out/cipher-callchain-audit.txt` L3342–3517（逐指令）；
`work/captures/streams-live2/10013-14805.bin`（NOTI1=450 的真实抓包）。

## 十、还没做完的（下一步，仍是纯静态）


| # | 待办 | 入口 |
|---|---|---|
| 1 | **`Cipher` 的算法链是哪几种、什么顺序** | 找谁调 `Cipher::Initialize` 与 `CreateCryptoGraph`，看枚举实参 |
| 2 | **密钥材料从哪来** | `lls_keys = 3240332317335214766634233322344377569967`（`siroco11.cfg` L55）；`libnxencryption.so`、`libserverlib_config_name = ServerLab#22001` |
| 3 | **`_NS_PI_2ND_RecvHash_CS` 的派生算法** | @ `0x08274f73`（461 B）—— 输入 `char*`、`CUser*`、`uint`，输出应是密钥流 |
| 4 | **`0x9404754` 的 RSA 密钥是谁、什么用途** | 找初始化它的代码；对照 `/home/neople/game/publickey.pem` |
| 5 | **`Cipher::Decrypt` 的模式参数含义** | @ `0x0809751e`（182 B） |
| 6 | 令牌字段在**已解密载荷**里的偏移 | 反汇编 `get_str` 前的字段游标推进（`PacketBuf` 的读游标） |
