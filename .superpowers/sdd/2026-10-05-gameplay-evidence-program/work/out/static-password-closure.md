# 游戏面密码层：可由静态分析确定的部分

日期：2026-10-05

## 证据对象与复跑

目标 ELF：`%TEMP%/dfl-creds/vm-df_game_r`

- SHA256：`486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`
- 大小：`36,360,954 B`
- 静态原始输出：[static-cipher-details.txt](<static-cipher-details.txt>)
- 发送/字段原始输出：[static-cipher-send-fields.txt](<static-cipher-send-fields.txt>)
- 复跑：

```powershell
$B = '.superpowers/sdd/2026-10-05-gameplay-evidence-program'
$ELF = "$env:TEMP/dfl-creds/vm-df_game_r"
python "$B/work/static-cipher-details.py" $ELF > "$B/work/out/static-cipher-details.txt"
python "$B/work/static-cipher-details.py" $ELF 0x858df30 0x82751ee 0x858d240 0x82746f3 0x858e248 0x8698744 0x8172fc8 0x8172fde 0x8172fea 0x8173008 0x8173022 0x817304e > "$B/work/out/static-cipher-send-fields.txt"
```

## A. NOTI1 专用包装（[静态]）

### A.1 分支条件和边界

`PacketBuf::encrypt_packet @ 0x0858d86a`（符号 size 446）从 `buffer + 1` 读取 uint16 opcode；分类为 0、opcode 为 1 时跳到专用分支 `0x0858d9eb`，最终在 `0x0858da12` 调 `Encryption::Encrypt`。专用调用参数由指令构造为：输入地址 `buffer + 0x0f`，长度 `PacketBuf.length - 0x0f`，输出/状态引用为局部变量。普通分支则调用 `G_Cipher()->AdjustDataSize` 与 `G_Cipher()->Encrypt`。

### A.2 算法

`Encryption::Encrypt @ 0x0848da70`（237 B）设局部状态为 `0x04453eb5`。若状态引用的初始值非零，才调用 `SeedRand @ 0x0848d51a`；零状态路径直接进入循环。零状态路径中：

- 读取状态值加 1 的地址，得到字节 `0x3e`；`0x3e & 7 = 6` 为旋转量；
- `0x0848dae4..0x0848dae9` 先将当前字节与 `0xb5` XOR 并原地写回；随后 `0x0848dafc..0x0848db3e` 从同一地址重新读取已修改字节，再进行右旋 6；
- 因而最终输出为 `ROR8(original XOR 0xb5, 6)`；线路逆变换是 `ROL8(wire, 6) XOR 0xb5`；
- 循环没有更新状态。

[作废] 上一条对话曾称“重新读的是原始字节，因此先旋转再 XOR”。该说法忽略原地写入；精确机器码推翻它。已锁定脚本使用的 `ROL8(wire,6) XOR 0xb5` 保持不变。

完整机器码见 `noti1-evidence-locked.txt` L11–94。

### A.3 线路头和长度的静态边界

`PacketBuf::finalize @ 0x0858d548` 先计算正文crc32NE，写长度[3:7]；按需压缩后再次计算正文crc32NE。this+0的内部字节序标志为0时，两结果分别小端写入[7:11]及[11:15]；这里的内部标志不能与packet[0]分类混称。`encFinalize @0x0858df30` 在加密后的正文上重算并覆盖这些字段。

`CUser::Send @ 0x086485ba` 调 `AntibotSend @0x0864809c`。后者在普通加密分支复制PacketBuf → encrypt_packet → encFinalize → NS_PI_2ND_SendHash_SC → CNetwork::send_packet。encFinalize在加密之后，所以头[7:11]此时覆盖密文，而非最早的明文；SendHash随后改写[11:15]，不是重复CRC字段。这是客户端兼容所需的边界契约，不扩展为反外挂进程实现。

因此：`+0x0f` 是出站服务端 PacketBuf 的内部缓冲起点，不能直接等同于线路捕获文件的公共头长度；线路捕获仍需以发送链和真实字节共同确认。

来源：`static-cipher-details.txt` L210–456；`static-cipher-send-fields.txt` L3–218、L220–271。

## B. CRC 算法（[静态]）

`crc32 @ 0x0848d472`：初始调用值为 0；函数内部先取反；每字节索引为 `(crc XOR sign-extended byte) & 0xff`；新值为 `crc >> 8 XOR table[index]`；处理完再取反返回。

`make_crc_table @ 0x0848d3e4` 使用多项式 `0x4db89129`，表地址 `0x0943d580`。

`crc32NE @ 0x0848d4c8` 执行：

```text
crc = crc32(0, NULL, 0)  # 空输入初始化，不是第一段正文
crc = crc32(crc, pointer, length)
crc = MakeChecksumTo1Byte(crc)
return crc
```

`MakeChecksumTo1Byte @ 0x0808cc17` 只修改低字节：`low = b0 XOR b1 XOR b2 XOR 0x18 XOR b3`；高 24 位不变。

`PacketBuf::finalize` 和 `encFinalize` 的 CRC 参数均由 `packet_buffer + 0x0f` 与 `packet_length - 0x0f` 构造；第二次调用发生在可能压缩之后。代码级算法、分段调用和写入字节序属于 `[静态]`；将其作用于抓包并匹配属于 `[回放]`。

来源：`static-cipher-details.txt` L88–209、L210–456；`static-cipher-send-fields.txt` L3–218。

## C. Cipher 选择和 key 切片（[静态]）

### C.1 单包算法选择

`Cipher::Decrypt @ 0x0809751e` 将 opcode 送入 `GetCryptoType @ 0x08097160`，按返回值访问 `vector<ICryptoGraph*>`，只调用一个对象的虚函数，不遍历 14 个对象。独立全域复核：`verify-cipher-audit.py` 对全部 65536 个 uint16 输入得到 `n % 14`。

CMD1 选择枚举 1，即 `CNCryptoRijndael`。这是 `[静态]`，不是 14 层串联。

### C.2 14 个 key 段

各 `GetKeySize()` 精确返回值及累计切片（334 B）如下：

| 枚举 | 算法 | key size | 累积范围 |
|---:|---|---:|---|
| 0 | Shift | 8 | 0..7 |
| 1 | Rijndael | 16 | 8..23 |
| 2 | BlowFish | 56 | 24..79 |
| 3 | Rc6 | 60 | 80..139 |
| 4 | TwoFish | 32 | 140..171 |
| 5 | Tea | 16 | 172..187 |
| 6 | Kasumi | 16 | 188..203 |
| 7 | XTea | 16 | 204..219 |
| 8 | Noekeon | 16 | 220..235 |
| 9 | Khazad | 16 | 236..251 |
| 10 | Cast5 | 16 | 252..267 |
| 11 | Skipjack | 10 | 268..277 |
| 12 | Multi2 | 40 | 278..317 |
| 13 | Anubis | 16 | 318..333 |

合计 334 B。`Cipher::Initialize @ 0x08097306` 先复制原始 key，再循环取每项 key size、调用 Initialize，并前移 key 指针；该循环直接支持上表。

### C.3 Rijndael 参数

`CNCryptoRijndael::GetKeySize @ 0x0809c404` 返回 16，`GetBlockSize @ 0x0809c40e` 返回 16。其 Initialize 调用 `CRijndael::Initialize`，静态传入 16 B key、16 B block、模式值 0、以及地址 `0x08b22f20`。具体参数语义不以名字猜测。

`CRijndael::Initialize @0x080b6220` 在0x080b62b1将mode参数存于this+0x10。这里传0；`Decrypt @0x080b8028` 中mode1是解块后XOR链、mode2是加密链后XOR输入；mode0进入0x080b81e1，每块独立DecryptBlock，无链XOR，即ECB形态。输入长度需整除16。对本次CMD1，PyCryptodome与cryptography两路AES-128-ECB随后离线解得相同400B（见下），不是只凭算法名字断言兼容。

来源：`static-cipher-details.txt` L1456–1517、L1579–1787、L1789–1949、L1951 起、L3978–4158。

## D. CMD1 静态字段边界（[静态]）

`CUser::Parsing @ 0x08647890` 复制 13 B 入站头，从复制缓冲 `+1` 读取 uint16 opcode，并将 `raw+0x0d`、`frame_length-0x0d` 传给 `Cipher::Decrypt`。因此 CMD1 的静态解密输入为：

```text
algorithm = opcode % 14 = 1
cipher_input = captured_frame[13:frame_length]
```

对两份已有流离线回放：`verify-static-password.py` 用候选NOTI1 key的[8:24]切片，以ECB解CMD1；两路AES实现逐字节相同，明文400B，前两个u32为0和344，token长度344。该结果标为`[回放]`，不是静态单独证明。

来源：`parsing-full-audit.txt`；`static-cipher-details.txt` L1016–1455、L1518–1577。

## E. LOGIN / base64 / RSA（[静态]）

`DisPatcher_Login::dispatch_sig @ 0x081e8c78` 先读取两个 uint32；第二个作为 `PacketBuf::get_str` 的长度参数。token 被读入 `0x0940a2e0`，容量为 `0x801`，随后调用 `AuthGarenaToken::decrypt(buffer, token_length, authData)`。

`PacketBuf::get_str @ 0x0858d2bc` 从当前游标逐字节读取，遇到 NUL 停止，并在目标末尾写 NUL；边界不足返回失败。

`AuthGarenaToken::decrypt @ 0x081731f2`：

1. C 字符串构造 `std::string`；
2. `base64_decode` 到 byte vector；
3. 调 `COpensslRSA::Decrypt`；
4. 检查恢复长度是否大于 `0x68`；
5. 清零 0x68 B 的目标结构，再复制 `恢复vector.size()-1` 字节（0x0817342e 的 lea ebx,[eax-1]）。末字节为什么被排除不能凭猜测命名。

`COpensslRSA::Decrypt @ 0x0816c532` 调 `RSA_public_decrypt`，padding 参数为 1，并按 RSA block size 分块处理。`0x68` 是容量/上限，不是每条 token 固定明文长度。

来源：`static-cipher-details.txt` L2565–2636、L2638–2827、L3626–3855、L3857–3976；`login-decrypt-ctx.txt` L8–38。

## F. 不能由静态单独决定的事项

1. 某次运行实例 `G_Cipher` singleton 的具体 334 B 内容；静态只能确定槽、初始化和生成路径。
2. NOTI1 候选 key 与该次 singleton 是否逐字节相等。
3. 静态 CRC 作用于某条抓包候选正文后的具体匹配结果（这是 `[回放]`）。
4. 当前两份 CMD1 的实际明文结构已通过离线 `[回放]`：明文400B、token长度344、RSA裸块256B、RSA type-1恢复46B；但候选key与singleton仍未交叉，且OpenSSL独立实现尚未复核。
5. 具体 token 的 UID、用户名和时间值（字段 accessor 是 `[静态]`，实际数值是 `[回放]`）。
6. 客户端真实时序是否接受构造包（这是 `[真机]`）。

## 状态

- `[静态]` 已补齐：NOTI1 分支与包装、CRC 算法与分段、发送路径、14 项 key 切片、CMD1 选择与输入偏移、token 字符串边界、base64→RSA→容量检查。
- `[回放]` 已有：NOTI1 双样本正例、错误旋转负例、前向查表往返；两份CMD1均完成候选key[8:24]的双AES实现ECB解密、NOTI1密文CRC闭合、base64长度344、RSA type-1 256B与46B恢复。OpenSSL独立RSA核对及singleton逐字节交叉仍待完成。
- `[未验证]` 保留：singleton逐字节交叉、RSA独立OpenSSL复核、完整CMD1后续字段与padding边界、完整发送PI头重构、其余13算法的已知答案；不能把剩余可静态追踪项称为“静态不能确定”。

## 离线交叉输出与未完成清单

运行：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/verify-static-password.py`。
来源：[verify-static-password.txt](<verify-static-password.txt>)，含公钥与四条输入流哈希，不输出完整token或账户身份。

[作废] 之前以明文CRC不匹配头[7:11]认为覆盖尚无解释：这次encFinalize静态路径说明应校验密文，两份抓包均匹配。明文负例本身保留，但不再阻断此字段结论。

本轮是静态补证与两样本离线交叉。新增PI静态输出：[pi-boundary-static.txt](<pi-boundary-static.txt>)、[pi-makehash-static.txt](<pi-makehash-static.txt>)、[pi-superfast-static.txt](<pi-superfast-static.txt>)：SendHash使用随机字节、MD5/SuperFastHash及低位重构；位布局全域回放通过，但90帧流中出现一帧空正文，已作为例外记录，不能把简化递推写成完整协议。仍可继续静态审计其余13算法、LOGIN尾字段和base64非法输入语义；已有目录/调用链不等于行为已全量闭合。

[作废] 本稿早期描述crc32NE为“两段正文”：第一调用实参为(0,NULL,0)，只是初始化。另早期描述AuthGarenaToken复制全部恢复字节，实际为size-1。均以本稿B/E的精确调用参数为准。

本报告不声称密码层已破解，不声称任意账号登录帧可构造，也不启动产品代码重写。
