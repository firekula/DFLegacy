# LOGIN 离线字段与尾部收尾

状态：T3 已验收；真实服务端接受/拒绝仍为动态阻塞，尾12B生产者未证实。

## 重跑命令与原始输出

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $W="$B/work"; $O="$W/out"; $E="$env:TEMP/dfl-creds/vm-df_game_r"
python "$W/verify-static-password.py" > "$O/t3-login-rsa.txt"
python "$W/verify-login-tail.py" > "$O/t3-login-tail.txt"
python "$W/emulate-cipher-boundaries.py" $E > "$O/t3-login-boundaries.txt"
# 路径A：只读既有精确函数反汇编
Get-Content "$O/login-full.txt","$O/login-decrypt-ctx.txt","$O/login-calls.txt","$O/login-tail-static.txt"
# 路径B：盘点对应客户端发送端资产（只读）
Get-ChildItem "$W",".superpowers/sdd/2026-10-05-official-login-channel/work" -Recurse -File | Where-Object { $_.Name -match 'client|DNF|game_r|bin|so|dll|exe' }
```

本轮 fresh 命令 exit：`verify-static-password.py=0`、`verify-login-tail.py=0`、`emulate-cipher-boundaries.py=0`。对应原始输出为 `t3-login-rsa.txt`、`t3-login-tail.txt`、`t3-login-boundaries.txt`；路径A输入文件完整存在，路径B盘点结果见会话命令输出，未找到有身份依据的对应客户端发送端可执行资产。

## 字段消费表

|顺序|字段|明文偏移/宽度|静态消费者证据|回放证据|退出|
|---:|---|---:|---|---|---|
|1|patch|0/4|`login-full.txt` @ `0x081e8ee3` `PacketBuf::get_int`|`t3-login-tail.txt`游标|已闭合|
|2|token length/token|4/4、8/变长|`login-decrypt-ctx.txt` @ `0x081e9027` `get_str`，随后 @ `0x081e907e` `AuthGarenaToken::decrypt`|RSA/Cipher回放|已闭合|
|3|两个空字符串|352/4、356/4|`login-full.txt` @ `0x081e921b`、`0x081e924c`|长度均0|已闭合|
|4|flag|360/1|`login-full.txt` @ `0x081e9376`，值分支0/1/2 @ `0x081e93ad`–`0x081e9400`|回放值在允许域|已闭合|
|5|mouse length/data|361/4、365/8|`login-full.txt` @ `0x081e941a`、`0x081e94b2`；解密 @ `0x081e950b`，检查 @ `0x081e956d`|CTEA/XTEA边界回放|已闭合|
|6|DataManager比较值|373/4|`login-full.txt` @ `0x081e9632` 读取 `G_CDataManager()+0x3c0`，比较 @ `0x081e9643`|样本值20080418|已闭合（角色命名未验证）|
|7|u32写入 `SIG_LOGIN_DATA+0x3a34`|377/4|`login-full.txt` @ `0x081e97cc`–`0x081e98b9` 解析短字段；`0x081e9bb7` 写 `[SIG_LOGIN_DATA+0x3a34]`|样本值30000401/28000201|已闭合（版本/日期命名未验证）|
|8|server group|381/2|`login-full.txt` @ `0x081e988e` 读 `SIG_LOGIN_DATA+0x3a38`，范围校正 @ `0x081e98c8`–`0x081e9910`；调用 `SetServerGroup` @ `0x081e99cf`|样本值3|已闭合|
|9|local IP|383/4|调用 `set_local_ip_address` @ `0x081e9a35`；存储目标 `CUser+0x8d254` 见 `login-restriction-static.txt` @ `0x0822fd00`–`0x0822fd10`|样本仅hash|已闭合|
|10|loginout state|387/1|调用 `SetLogInOutState` @ `0x081e9a78`；存储目标 `CUser+0x71ba8` 见 `login-restriction-static.txt` @ `0x0822fe22`–`0x0822fe3c`|方向样本值237/1|已闭合|
|11|未消费尾部|388..399/12|接收函数无后续读取；完整函数边界见 `login-full.txt` 末尾|`t3-login-tail.txt`: 12B全零|已降级|

## 尾部生产者两路径

### 路径A：接收方精确反汇编

[静态] `DisPatcher_Login::dispatch_sig` 精确范围 `0x081e8c78..0x081e9e82`（`login-full.txt`）；密文解密/进入 `SIG_LOGIN_DATA` 的上下文见 `login-decrypt-ctx.txt`；调用摘要见 `login-calls.txt`。该函数在字段10后继续使用 UID、timestamp、环境/IP及服务代理状态，随后结束；没有读取 `SIG_LOGIN_DATA` 偏移388..399的指令。`Cipher::Decrypt` 与 `CUser::Parsing` 的调用路径资料见 `closure-callchain.txt`。

结论：接收方消费388B，观测明文400B尾12B全零；未从接收方找到尾部生产者或去填充操作。

### 路径B：对应客户端发送端资产盘点

[未验证] 盘点到的是服务端 ELF/SO、抓包流及分析脚本；没有发现带有身份依据、属于同一客户端代际的发送端 EXE/DLL/标注库。因此不能用2008客户端替代，也不能声称生产者检索已恢复规则。

## 最终限定结论

[回放] 400B 明文按上述字段消费388B，尾12B观测全零；[静态]读取序、消费者与存储目标已核对。尾12B严格表述为：**接收方消费388B、观测尾12B全零，生产者身份/入口未取得，来源未验证**。禁止命名为padding。

[静态]+[回放] LOGIN 密码/RSA、mouse CTEA/XTEA、字段消费与尾部观察已闭合/降级；这不等于真实服务端接受，也不等于可以构造成功登录。restriction 查询的实际 DB 行值仍为动态/DB输入阻塞。
