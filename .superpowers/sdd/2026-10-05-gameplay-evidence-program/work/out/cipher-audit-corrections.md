# Cipher 调用链审计与接续边界

本文件优先于此前 `game-face-crypto.md`、RESUME 中相冲突的结论。保留旧稿用于矛盾留痕，不能沿用其中的完成判断。

## 可复跑（工作区根，纯离线）

```powershell
$B = '.superpowers/sdd/2026-10-05-gameplay-evidence-program'
$ELF = "$env:TEMP/dfl-creds/vm-df_game_r"
python "$B/work/audit-cipher-callchain.py" $ELF
python "$B/work/verify-cipher-audit.py" $ELF
python "$B/work/verify-cipher-snapshot.py" "$B/work/out/peek-key2.txt"
```

目标 SHA256：`486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`，大小 36,360,954 B。
本轮没有启动客户端、没有修改 VM 配置、没有再次附加运行中进程；动态交叉证据来自已存内存快照。

## 1. PLT 误标已定位

[静态] `disasm-func.py` 使用最近的符号范围给 call 命名，未按 PLT relocation 解引用，因此旧输出不可靠。
本轮按 ELF32 relocation/GOT 解析，并以独立 pyelftools 路径核对：

| PLT VA | 真实符号 | 旧误标 |
|---|---|---|
| 0x0807e360 | localtime_r | realloc |
| 0x0807e3b0 | strlen | realloc |
| 0x0807d8a0 | memcpy | _init |
| 0x0807dcc0 | memset | _init |
| 0x0807dca0 | rand | _init |

来源：`cipher-callchain-audit.txt` 开头；独立命令 `verify-cipher-audit.py`。
[作废] 据旧 call 标签推导“realloc 生成/分配密钥”以及将 `_init` 当成已知内存操作的结论。
旧工具尚未统一修复；继续引用时必须先解析 relocation 或给出未知名称。

## 2. 14 个算法对象不是 14 层串联

[静态] `Cipher::Decrypt @0x0809751e`：`0x08097543` 调 `GetCryptoType`，`0x0809755b/0x08097581` 按结果访问 vector，`0x080975b0` 调被选对象的虚方法。没有遍历全部 14 项做解密的循环。
[回放] `verify-cipher-audit.py` 按 `GetCryptoType @0x08097160` 的指令模拟带符号乘法高位/移位运算，对 uint16 全域验证结果是 `n % 14`。
[静态] `PacketBuf::encrypt_packet @0x0858d86a` 从 buffer+1 读取 word 作为 Encrypt 的选择参数；CUser::Parsing 从复制头起点+1 读取 word 作为 Decrypt 的选择参数。

[作废] “每个包按 14 种算法固定串联加密”。正确边界是：构造/初始化准备 14 个实现；调用路径按 opcode 选其中一个。算法具体模式、各段 key 长度尚须逐个复核。
[作废] “算法名字像 libmcrypt，所以可直接用该库”。名字不证明实现来源、模式、字节序或兼容性。

## 3. 两组 Cipher 不得混用

[静态] `G_Cipher @0x082a713a -> GlobalInstance<Cipher>::inst_ptr @0x082ab9da`，后者从 `0x0941f8a4` 读 singleton 指针。
[静态] `GlobalData::Init` 在 `0x0829c54e` 循环调用 rand 取低字节，经 `0x0829c595` 初始化该 singleton。
[静态] `InitCipherKeyString` 在另一处 `0x0940be80 + i*16` 初始化 5000 项（i=0..0x1387）。`SelectKeyString` 返回 c_str 指针；两个 int* 输出分别为所选分支号、map 条目整数键，不是两个字符串指针。
[回放] `verify-cipher-snapshot.py` 复算既有运行态快照：pool[0] 对象长度=334、vector 有14个指针、334字节与 `MapObject/PathGate/Act2/DEDownNormal.obj` 周期重复完全相同。

[作废] “读到 pool[0] 的路径字符串就是登录帧使用的全局 key”。静态 accessor 指向另一全局槽，当前快照只能证明 pool[0]，没有采到 singleton。

## 4. 时间值用于计时，不是本函数的播种证据

[静态] `InitCipherKeyString` 入口 `0x082a026d` 的时间存入 `[ebp-0x2c]`；尾部 `0x082a03fa` 再取时间，`0x082a0401` 相减，只传给耗时日志。
[静态] `get_seed_from_date @0x086b1d25` 的 PLT 是 localtime_r，不是 realloc；该函数由 `CUser::InitOnAccept` 调用。不能把名字中的 seed 等同于全局 srand 播种。
[作废] “7001 时间戳 -> 客户端播种 -> 重建全局封包密钥已闭环”。尚无此数据流证据。

## 5. 密钥分发入口已找到，但线路解包仍未闭环

[静态] `CUser::InitOnAccept @0x086493c8`：
- `0x0864954b`：put_header(0,1)。
- `0x08649562`：G_Cipher()->GetOriginalKey(buffer)。
- `0x0864956f`、`0x0864958e`：取得长度并 put_binary。
- 后续写频道名、日期派生值、随机数、环境字段。

[静态] `PacketBuf::encrypt_packet` 对 classification=0、word opcode=1 有专门分支；`0x0858da12` 调 `Encryption::Encrypt`，不是普通 G_Cipher 算法选择路径。
[静态] 该函数使用 buffer+0x0f 和 length-0x0f；入站 `CUser::Parsing` 使用 +0x0d 与 length-0x0d。

[作废] “450-13=437，所以 334B key+103B 字段已逐字段闭合”。没有解开 NOTI1，也没有逐字段比对；总长度可凑齐不构成独立验证。
[作废] “入站与出站一律 13B 头已定案”。入站正文起点 +13 有指令依据；出站代码出现 +15，必须分别跟完整发送路径；本轮不再凭 cfg 或铺满调整公共模型。
[作废] “header[2] 是 garvage”。该接收路径将 [1:3] 作为 word opcode；旧脚本把高字节误当独立字段，导致高 opcode 被截断。

## 6. RSA 阶段

[静态] `COpensslRSA::Decrypt @0x0816c532` 的 `0x0816c5cf` 调 RSA_public_decrypt，padding实参=1。与 AuthGarenaToken 中 base64_decode 调用衔接。
[作废] “GarenaAuthData 104B 容量等于每条 token 的明文长度”。函数里 0x68 是容量上限/清零大小，不能替代实际恢复长度。

## 静态补证更新

见 [静态密码分析](<static-password-closure.md>) 和 [离线交叉输出](<verify-static-password.txt>)。两份现有NOTI1密文CRC已匹配[7:11]；CMD1用key[8:24]经双AES实现ECB解得相同400B，base64长度344，RSA type-1恢复46B。仍无singleton快照逐字节核对。旧“未解出CMD1”描述作为历史状态保留，不再代表最新进度。其他算法和完整PI/LOGIN尾字段仍有静态待办。

## 接下来按此顺序继续（无需人工登录）

1. 静态读 NOTI1 特殊 `Encryption::Encrypt`，确定可逆包装、输入偏移和校验覆盖；逐指令用完整符号/relocation 核实。
2. 离线解已有 NOTI1，恢复 key；以频道名/长度字段等结构做正例验证，错误偏移/错误参数做负例。
3. 安全动态取 singleton（先按当前 PID/映射/指针检查，不使用旧 heap 地址）；与恢复的 key 逐字节交叉验证。工具必须保证 detach；不能继续用缺退出保障的旧探针。
4. 用 opcode%14 选择实现解已有 CMD1，确认 base64 字段，再做 RSA 公钥运算。全部通过前，构造任意账号登录仍是未完成。

因此当前是“关键静态入口定位 + 旧内存快照复算”，不是“密码层全部破解”或“可开始重写”。
