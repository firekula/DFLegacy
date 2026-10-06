# Mouse CTEA key 静态来源

[静态] `GlobalData::Init` 在 `cipher-callchain-audit.txt` L1524–1532 将全局 CTEA 对象写入 `0x0941f700`，随后调用 `CTEA::Initialize`：key pointer=`0x08c179e7`，key length=16，两个模式参数为0。

目标 ELF 的 `.rodata` 对 `0x08c179e7` 读取为16字节 ASCII：

```text
tjsghTlWkdajtwla
```

该字符串是二进制静态常量，不是运行实例 singleton key，也不等于游戏面334B Cipher key。它仅是 LOGIN mouse password CTEA 的静态材料。

来源/复跑：
```powershell
python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/static-cipher-details.py $env:TEMP/dfl-creds/vm-df_game_r 0x80c339c 0x826333a
```
并用 ELF `va_to_off(0x08c179e7)` 读取16字节；目标SHA256固定为 `486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`。

[作废] 任何把该16B mouse key当作G_Cipher原始key、NOTI1 key或时间播种值的说法均无证据支持。
