# LOGIN 解密明文尾字段离线回放

## [静态]

`DisPatcher_Login @0x081e8c78` 在 token 解密后继续读取：

1. `patch` 前置字段已由 token 解密段读入；
2. `token_length:u32`；
3. token NUL字符串，最大目标容量2049；
4. 后续 `string1_length:u32` + 最大24B字符串；
5. `string2_length:u32` + 最大132B字符串；
6. `flag:u8`，只接受0/1/2；
7. `mouse_length:i32`，必须为8；
8. 8B mouse encrypted binary，经 `decryptPassword` 后以字符串校验；
9. `DataManager+0x3c0` 比较字段；
10. version/环境字段；
11. `server_group:i16`；
12. local IP `i32`；
13. login/out state `u8`。

来源：[login-tail-static.txt](<login-tail-static.txt>)、[static-cipher-details.txt](<static-cipher-details.txt#L2880-L3419>)。

## [回放]

`verify-login-tail.py` 对全部已有可配对 C→S/NOTI1 流逐条解密并按上述游标消费：

- 每份明文均为400B；
- token length=344；
- string1/string2 length均为0；
- flag=0；
- mouse length=8；
- 8B mouse encrypted binary跨同部署重复；
- 容器部署：DataManager值 `20080418`、version `30000401`、server group 3、loginout state 237；
- VM部署：DataManager值 `20080418`、version `28000201`、server group 3、loginout state 1；
- 游标消费至388，余12B全零；
- token 与部署公钥一致：容器 UID accessor=3、VM UID accessor=18000008（来自独立token字段报告）；
- 余12B只能记为全零尾部，不能在没有生产者指令时命名为 padding。

来源：[verify-login-tail.py](<../verify-login-tail.py>)；本轮输出：[verify-login-tail.txt](<verify-login-tail.txt>)。

## 仍未闭合

- `decryptPassword` 的实际 mouse password 算法依赖 `0x941f700` 对象虚表，尚未从静态实现追完；
- `DataManager+0x3c0` 与 version 常量的部署语义尚未命名；
- 余12B全零尾部的发送端字段来源尚未定位；
- LOGIN 成功/拒绝状态机及服务端接受条件仍需同部署运行证据。

重跑：
```powershell
python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/verify-login-tail.py
```
