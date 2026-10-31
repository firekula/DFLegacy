# 客户端密码/封装总回归

状态：T4 已验收；客户端可见密码/封装覆盖有明确分母、正例、负例与动态边界。服务器模块间加密仍为范围暂缓。

## 执行与原始输出

计划冻结清单13条按顺序执行，均 exit=0；对应完整 stdout/stderr：`t4-callchain.txt`、`t4-selector.txt`、`t4-pool-snapshot.txt`、`t4-noti1.txt`、`t4-login-rsa.txt`、`t4-outbound-pi.txt`、`t4-inbound-pi.txt`、`t4-login-tail.txt`、`t4-elf-boundaries.txt`、`t4-all-algorithms.txt`、`t4-noti561-inventory.txt`、`t4-noti561-envelope.txt`、`t4-noti561-decrypt.txt`（stderr 同名 `.stderr`）。重跑命令就是计划文件 T4 Step 1 的 PowerShell foreach，或逐条：

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $W="$B/work"; $O="$W/out"; $E="$env:TEMP/dfl-creds/vm-df_game_r"; $SO='G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'
python "$W/audit-cipher-callchain.py" $E > "$O/t4-callchain.txt"
python "$W/verify-cipher-audit.py" $E > "$O/t4-selector.txt"
python "$W/verify-cipher-snapshot.py" "$O/peek-key2.txt" > "$O/t4-pool-snapshot.txt"
python "$W/lock-noti1-evidence.py" $E > "$O/t4-noti1.txt"
python "$W/verify-static-password.py" > "$O/t4-login-rsa.txt"
python "$W/verify-pi-boundary.py" > "$O/t4-outbound-pi.txt"
python "$W/verify-inbound-pi.py" > "$O/t4-inbound-pi.txt"
python "$W/verify-login-tail.py" > "$O/t4-login-tail.txt"
python "$W/emulate-cipher-boundaries.py" $E > "$O/t4-elf-boundaries.txt"
python "$W/verify-all-cipher-algorithms.py" $E > "$O/t4-all-algorithms.txt"
python "$W/verify-noti561.py" > "$O/t4-noti561-inventory.txt"
python "$W/verify-noti561-envelope.py" > "$O/t4-noti561-envelope.txt"
python "$W/decrypt-noti561-fixed.py" $SO > "$O/t4-noti561-decrypt.txt"
```

## 分母与判定

|硬门|本轮结果|证据|
|---|---|---|
|H01|PASS|`t4-callchain.txt` L1–2、输入 hash 初核；依赖版本与路径见 `offline-closure-report.md`|
|H02|PASS（离线判据）|`t4-selector.txt`：5 PLT + 65536/65536 `%14`；`t4-pool-snapshot.txt` 明确 pool[0]≠singleton|
|H03|PASS|`t4-login-rsa.txt`：11 paired / 8 unique；5 `SKIP_NO_PAIRED_LOGIN`；双 AES、OpenSSL raw/PKCS1、wrong-key negative|
|H04|PASS|`t4-inbound-pi.txt`：3 C流×21=63；`t4-all-algorithms.txt`：3 S流×90=270，且脚本明确普通S正文排除 NOTI1/NOTI561/空正文；不能把270说成全部正文解密|
|H05|PASS（限定）|`t4-all-algorithms.txt`：14 factory/init/encrypt/decrypt roundtrip；C CRC；`t4-elf-boundaries.txt` mouse/PI/边界。独立官服已知答案仍[未验证]|
|H06|PASS|`t4-noti561-inventory.txt`：3 VM流各19=57，type17=54/type19=3；`t4-noti561-decrypt.txt` L120 count54，双CRC/解压/再加密；type19原SO unpack|
|H07|PASS（名称有限）|`t4-noti561-decrypt.txt` L121：9组；`opcode-map.tsv` 提供 category=1 的 CMD 名称；业务字段仍降级|
|H08|PASS（尾部限定）|`t4-login-tail.txt`：400→388+12且全零；`login-offline-closure.md` 静态消费者/生产者两路径；尾12B不叫padding|

## 负例与限制

- [回放] RSA wrong-key、PI两种突变、NOTI1错误 rotation、mouse七个边界、CTEA 256块往返、selector全域均有输出。
- [静态] singleton 槽位 `0x0941f8a4` 与 pool[0] 证据分离；当前运行实例值为动态阻塞。
- [未验证] 14算法独立官服已知答案、restriction 实际 DB 行值、真实 LOGIN 接受/拒绝；[范围暂缓] bridge/dbmw字段级/辅助服务/服务器模块间加密。
- [回放] `t4-outbound-pi.txt` 有两个空正文 `opcode=124`，明确标为 random hash not predicted；不能扩大为正文成功验证。

T4 退出：客户端可见密码层已按分母闭合；动态项单列，不得再用“密码层未完成”泛化全部协议。
