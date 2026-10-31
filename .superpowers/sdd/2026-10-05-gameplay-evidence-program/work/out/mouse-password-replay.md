# LOGIN mouse password / CTEA 离线复核

## [静态]

`Dispatcher_MouseRegister::decryptPassword @0x0826333a` 从全局 `0x0941f700` 取得 CTEA 对象虚表，传入密文、输出缓冲、长度。`CTEA::Initialize @0x080c339c` 的目标 key 取自静态地址 `0x08c179e7`，key length=16，模式参数=0、0（GlobalData::Init @0x0829b465/0x0829b46d，来源cipher-callchain-audit.txt L1525–1532）；constructor固定 block size=8、key size=16。

`checkValiedPassword @0x082632ca`：

- strlen 必须 <=6；
- 每个字符允许 `'a'` 或 `'0'..'9'`：0x082632fd先比较0x61，命中直接通过；否则执行 `> '/' && <= '9'` 的有符号字节比较；
- 空串会通过循环，不能把“数字密码”推广为非空约束。

LOGIN dispatcher 在解密后读取 mouse_length，要求等于8；然后调用 decryptPassword，再调用 checkValiedPassword；失败发送 `SendCmdErrorPacket(1,1)`。

来源：[login-tail-static.txt](<login-tail-static.txt>)；[mouse-ctea-static.txt](<mouse-ctea-static.txt>)；[static-cipher-details.txt](<static-cipher-details.txt#L3074-L3127>)。

## 块实现补证

[静态] [ctea-block-static.txt](<ctea-block-static.txt>) 的0x080c3804/0x080c3724显示：每块8B，按大端装载两个u32，key按大端四个u32；32轮，delta=0x9e3779b9，sum索引key分别用sum&3与(sum>>11)&3。这是XTEA形式的轮函数，不是凭CTEA类名认定标准TEA。

[回放] 新增独立Python轮函数与ELF逐块解密对照256个固定输入，并逐块ELF重新加密恢复原输入；抓包8B字段也与Python逐字节一致。直接调用目标checkValiedPassword验证抓包结果，并测试空串、六位数字、六个a、超长、字母b和边界字符共7例。均通过；凭据明文不输出。

复跑：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/emulate-cipher-boundaries.py $env:TEMP/dfl-creds/vm-df_game_r`。

## [回放]

采用固定目标 ELF 做有界 Unicorn 指令回放，只提供已解析的 memcpy/memset PLT stub，不执行宿主函数、不联网、不访问进程：

```text
MOUSE_CTEA_ELF_DECRYPT=PASS
OUTPUT_SHA256=2bd74b35b0ce2a14110428557ea41aa9eaf6f06184e4f48d408147b7f8b633fc
```

同一脚本还对 SuperFastHash 259种长度、NOTI1逐字节包装、三条入站PI流逐帧进行了 ELF 与 Python 对照；输出见：[emulate-cipher-boundaries.txt](<emulate-cipher-boundaries.txt>)。

[回放] 只能证明目标 ELF 当前静态路径在给定离线输入上执行到成功返回，不能把 mouse 输出命名为明文密码，也不能证明服务端接受/拒绝某账号。
