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

## 六、还没做完的（下一步，仍是纯静态）

| # | 待办 | 入口 |
|---|---|---|
| 1 | **`Cipher` 的算法链是哪几种、什么顺序** | 找谁调 `Cipher::Initialize` 与 `CreateCryptoGraph`，看枚举实参 |
| 2 | **密钥材料从哪来** | `lls_keys = 3240332317335214766634233322344377569967`（`siroco11.cfg` L55）；`libnxencryption.so`、`libserverlib_config_name = ServerLab#22001` |
| 3 | **`_NS_PI_2ND_RecvHash_CS` 的派生算法** | @ `0x08274f73`（461 B）—— 输入 `char*`、`CUser*`、`uint`，输出应是密钥流 |
| 4 | **`0x9404754` 的 RSA 密钥是谁、什么用途** | 找初始化它的代码；对照 `/home/neople/game/publickey.pem` |
| 5 | **`Cipher::Decrypt` 的模式参数含义** | @ `0x0809751e`（182 B） |
| 6 | 令牌字段在**已解密载荷**里的偏移 | 反汇编 `get_str` 前的字段游标推进（`PacketBuf` 的读游标） |
