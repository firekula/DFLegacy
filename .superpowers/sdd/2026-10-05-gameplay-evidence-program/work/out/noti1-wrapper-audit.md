# NOTI1 离线探索接续（未闭环）

本次已复跑三个既有 cipher 审计脚本；ELF 大小 36360954、SHA256 486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043 与审计目标匹配。尚未做新动态内存观测、未发登录重放。

## 已证实 / 已否证 / 尚未闭环

| 状态 | 本次结果 |
|---|---|
| [静态] 已证实 | 精确符号 Encryption::Encrypt @0x0848da70，size=237。状态引用为零时保持局部常量 0x04453eb5，不调用 SeedRand；仅非零时调用一次 SeedRand。取常量的第二字节 &7，即6；每个字节先 XOR 0xb5 再 ROR8(6)。循环中没有更新状态。 |
| [抓包] 已观测 | 两份 NOTI1 的 frame[15:450] 使用 ROL8(6) XOR 0xb5 逆变换后，offset334 得到 u32=5，后面 ch.11；其后包含三份192.168.200.131以及尾部606、562。前334字节两份相同，SHA256=40b8a8230548024ca85a514f8dd7c4ad5e2549f9d6042a82a47f8c177611aab2。只能称候选key，尚无singleton快照核对。 |
| [回放] 已否证 | 普通zlib CRC以及直接按0x4db89129逐位右移所得CRC，处理候选正文后都不等于线路[7:11]；不能宣称校验覆盖闭合。 |
| [未验证] 尚未闭环 | 当前singleton与候选key逐字节比对；发送头变化及自定义CRC表行为；CMD1解密、base64字段与RSA恢复。 |

## 固化验收（本轮）

可重跑入口：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/lock-noti1-evidence.py $env:TEMP/dfl-creds/vm-df_game_r`。
产物：[noti1-evidence-locked.txt](<noti1-evidence-locked.txt>)，含目标ELF及两条原始流的SHA256、精确函数大小、Encrypt完整指令和原始机器码、双样本正例、仅改变旋转量的负例、前向查表逆解和逐字节密文往返检查。查表与逆表达式是同一假设的独立实现，不是新的官服真值或singleton证据。

[作废] 上轮最终答复称“第二字节0xb5”：正确为低字节0xb5用于XOR，第二字节0x3e的低3位用于旋转（=6）。来源：固化产物0x0848da79、0x0848daa1、0x0848dabf及0x0848dae4..0x0848dae9。不能混淆这两项参数。

本轮三个旧审计脚本完整重跑exit=0，不再把生产者接到Select-Object -First。原全量审计与两份verify产物仍是静态入口与pool[0]边界依据，不是singleton验证。

本轮固定的是当前NOTI1包装候选的可重现证据，**不是所有玩法/密码证据已经闭合**；CRC旧负例尚未纳入此验收脚本，仍保留其探索性限定。

## 重跑候选解包

工作区根执行以下Python（纯离线；不打印完整key/token）：

```python
from pathlib import Path
import struct, hashlib
base=Path('.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/captures')
for d,f in [('streams-live','10013-2343.bin'),('streams-live2','10013-14805.bin')]:
    b=(base/d/f).read_bytes()[:450]
    assert b[:3]==b'\x00\x01\x00' and struct.unpack_from('<I',b,3)[0]==450
    p=bytes((((x<<6)|(x>>2))&255)^0xb5 for x in b[15:])
    assert struct.unpack_from('<I',p,334)[0]==5 and p[338:343]==b'ch.11'
    print(d,len(p),hashlib.sha256(p[:334]).hexdigest(),repr(p[334:]))
```

静态复核方法：用 work/elfrecon.py 的 Elf.func_symbols() 查精确地址对应size，va_to_off 映射，再用 capstone x86-32 从函数入口反汇编；不要用最近符号猜PLT。需要固化为可重跑静态报告的地址：0x0848da70（Encrypt）、0x0848d51a（SeedRand）、0x0848d3e4（CRC表生成）、0x0848d472（crc32）、0x0848d4c8（crc32NE）、0x0808cc17（修改校验低字节）、0x0858d548（finalize）、0x086485ba（Send）。Send下一跳为0x0864809c，仍待读。

## 本轮纠错留痕

[作废] 本文件最初落盘版本将零状态分支写成“逐字节更新SeedRand”、并说字段边界不匹配。该版本是本轮整理错误：实际反汇编0x0848da85..0x0848da97跳过SeedRand，循环0x0848dace..0x0848db4f没有状态更新；上面固定旋转的两样本实验能够恢复ch.11等字段。保留此处说明，不沿用错误描述。

凭据：已移除 work/wait-and-collect-dbmw.ps1 的SSH密码fallback，改为缺VM_PASS即报错；未改写git历史。其他脚本与历史文档的凭据脱敏审计尚未完成。RESUME历史正文也含凭据风险，后续不要原样转录。
