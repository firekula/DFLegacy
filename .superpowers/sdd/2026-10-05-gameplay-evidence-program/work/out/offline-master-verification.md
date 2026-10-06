# offline master report generated from fresh commands
# 离线项目总验收（本轮 fresh run）

目标 ELF：`486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`，36,360,954 B。

## [静态] 通过

- relocation PLT：localtime_r/strlen/memcpy/memset/rand 均正确；`GetCryptoType` 全65536输入为 n%14。
- Cipher 14项切片、CMD1选Rijndael、NOTI1固定包装、CRC路径、C→S 13B头、LOGIN token/base64/RSA路径均有精确地址报告。
- mouse CTEA key为 `tjsghTlWkdajtwla`；restriction policy 查询SQL为 `dnf_restrict_state(server_group)`，字段 `category/restrict_code/restrict_value`。

## [回放] 通过

- [all-verify-static-password.txt](<all-verify-static-password.txt>)：11个配对流对、8个唯一内容对；每对通过双AES、OpenSSL raw/PKCS1、密文CRC、CMD1往返、token/RSA结构和错误key负例。
- [all-verify-inbound-pi.txt](<all-verify-inbound-pi.txt>)：三条C→S流各21帧，PI密文链、CMD1 CRC、两个修改负例通过；sequence仅记录为观察值。
- [all-verify-login-tail.txt](<all-verify-login-tail.txt>)：400B明文消费388B，余12B全零；尾字段按静态游标解释。
- [all-emulate-boundaries.txt](<all-emulate-boundaries.txt>)：目标ELF有界执行、SuperFastHash、NOTI1、三条PI流、CTEA 256块往返、mouse边界用例、mobile policy predicate均通过。
- snapshot：pool[0]快照复算通过，但明确不是singleton。
- [回放] [all-cipher-algorithm-roundtrip.txt](<all-cipher-algorithm-roundtrip.txt>)：目标ELF工厂创建、Initialize、Encrypt、Decrypt往返14项全部通过；key/block size均输出。限制：同一目标ELF roundtrip，不是独立已知答案。

## [未验证] 尚未闭合（并非均需真机）

1. 当前运行实例 `G_Cipher` singleton 与历史抓包候选334B key逐字节相等；需要同一运行实例动态内存证据。
2. 其余13算法的逐算法已知答案与所有opcode明文语义；现有CMD1闭环不能代表全部算法。
3. restriction policy的实际数据库行值及完整虚表业务语义；静态只闭合查询、字段和默认分支。
4. LOGIN成功状态机在真实服务端上的接受/拒绝；离线只证明输入结构和密码运算。
5. 未配对的5个流明确跳过，不计为通过。

## 本轮可重跑

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $E="$env:TEMP/dfl-creds/vm-df_game_r"
python "$B/work/audit-cipher-callchain.py" $E > "$B/work/out/all-audit-callchain.txt"
python "$B/work/verify-cipher-audit.py" $E > "$B/work/out/all-verify-cipher-audit.txt"
python "$B/work/verify-cipher-snapshot.py" "$B/work/out/peek-key2.txt" > "$B/work/out/all-verify-snapshot.txt"
python "$B/work/verify-static-password.py" > "$B/work/out/all-verify-static-password.txt"
python "$B/work/verify-pi-boundary.py" > "$B/work/out/all-verify-pi-boundary.txt"
python "$B/work/verify-inbound-pi.py" > "$B/work/out/all-verify-inbound-pi.txt"
python "$B/work/verify-login-tail.py" > "$B/work/out/all-verify-login-tail.txt"
python "$B/work/emulate-cipher-boundaries.py" $E > "$B/work/out/all-emulate-boundaries.txt"
```

结论：当前离线静态/回放路径已集中跑完；剩余第一硬缺口是运行实例singleton交叉，不把它伪装成离线结论。
