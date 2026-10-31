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
2. NOTI561候选流已固定为Inter_Antibot_SendBufferClient路径的432B NOTI1/后续31B帧；其真正的反外挂payload和随机状态仍需静态追取，不能当作普通14算法S帧。
3. restriction policy的实际数据库行值及完整虚表业务语义；静态只闭合查询、字段和默认分支。
4. 14算法目标ELF roundtrip及现有S/C流的算法调用已回放；仍未有独立已知答案/字段语义，不能把同一二进制自洽等同于独立密码兼容证明。
5. LOGIN成功状态机在真实服务端上的接受/拒绝；离线只证明输入结构和密码运算。
6. 未配对的5个流明确跳过，不计为通过。

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

## 覆盖限制补充

[回放] 14算法脚本处理三对VM流，共63个C帧与270个S帧。C帧解密明文CRC全部通过，覆盖11种算法；S帧校验密文CRC低字节，普通非空包做加解密往返，覆盖14种算法。**S方向NOTI1及NOTI561被排除于普通算法往返**；NOTI1有专用验证，NOTI561外层仅验CRC，不能宣称其封装正文已解密。空正文不做算法往返。脚本当前输出的90帧是扫描数，不是90帧全部解密数。

[作废] 本报告最初的“当前离线路径已集中跑完；第一硬缺口singleton”若理解为所有可离线工作已完成，是过度结论。准确状态：现有验证器已集中复跑，并新增14算法目标ELF回放；仍有NOTI561封装、独立算法答案、LOGIN零尾生产者、策略消费语义等可离线工作。本轮不请求真机，不以singleton缺口掩盖这些待办。

新增复跑：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/verify-all-cipher-algorithms.py $env:TEMP/dfl-creds/vm-df_game_r`。
