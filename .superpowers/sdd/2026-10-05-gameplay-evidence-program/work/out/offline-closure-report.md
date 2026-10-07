# DFLegacy 客户端密码/封装证据收尾账本

状态：T1 账本已冻结（提交 `c4a74467e7592fb0595c6cbc028c46e56f6f5cae`）；本轮按 `docs/superpowers/plans/2026-10-06-gameplay-evidence-closure.md` 执行。只允许静态分析与已有字节回放；本轮未启动客户端、未连接 VM、未附加运行进程。

**状态枚举（计划原文）：** `已闭合` = 当前判据与原始输出已足够；`已降级` = 结论有限且缺口明确；`动态阻塞` = 必须取得运行实例/DB/真实状态；`范围暂缓` = 计划明确排除的后续系统。未使用“继续看看”。

## 执行环境与输入身份

唯一复跑基线命令（PowerShell）：

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $E="$env:TEMP/dfl-creds/vm-df_game_r"; $SO='G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'
python -c "import sys,hashlib; from pathlib import Path; print('python='+sys.version); [print(str(Path(p)),Path(p).stat().st_size,hashlib.sha256(Path(p).read_bytes()).hexdigest()) for p in [r'$E',r'$SO']]"
python -c "import capstone,elftools,unicorn,Crypto,cryptography; print('capstone='+capstone.__version__); print('pyelftools='+elftools.__version__); print('unicorn='+unicorn.__version__); print('pycryptodome='+Crypto.__version__); print('cryptography='+cryptography.__version__)"
```

[静态] 本轮输出：Python 3.12.10；Capstone 5.0.6；pyelftools 0.32；Unicorn 2.1.4；PyCryptodome 3.23.0；cryptography 45.0.7。ELF 输入为 36,360,954 B / SHA256 `486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`；SO 输入为 31,904,293 B / SHA256 `8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7`。原始输入路径与输出均固定于本报告命令/路径，不依赖运行库。

## 总账

| 项目 | 证据与可重跑命令 | 等级 | 结论/退出状态 |
|---|---|---|---|
| 目标 ELF 身份 | `Get-FileHash -Algorithm SHA256 $env:TEMP/dfl-creds/vm-df_game_r`；`Get-Item` | [静态] | SHA256 `486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`、36,360,954 B；**已闭合** |
| 官方 `libantisvrimport.so` 身份 | `Get-FileHash -Algorithm SHA256 'G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'` | [静态] | SHA256 `8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7`、31,904,293 B；**已闭合** |
| NOTI1 | `python work/verify-static-password.py` | [回放] | 11 文件对/8 唯一内容对；AES 双实现、OpenSSL raw/PKCS1、CRC、CMD1 往返及错误 key 负例通过；**已闭合** |
| 普通 Cipher / C→S PI | `python work/verify-inbound-pi.py`；`python work/verify-pi-boundary.py` | [回放] | 三 VM C 流 63 帧；CRC 与边界负例通过；**已闭合** |
| CMD1/RSA | `python work/verify-static-password.py` | [回放] | CMD1 ECB key slice `[8:24]`；RSA type-1；Python/OpenSSL 逐字节一致；**已闭合（不等于登录成功）** |
| LOGIN 尾字段 | `python work/verify-login-tail.py` | [回放] | 400 B 消费 388 B，尾 12 B 全零；生产者未证实；**已降级** |
| CTEA/mouse | `python work/emulate-cipher-boundaries.py $env:TEMP/dfl-creds/vm-df_game_r` | [回放] | mouse key、XTEA/CTEA 轮函数、长度/边界通过，仅输出 hash；**已闭合** |
| 入站/出站 PI | `python work/verify-inbound-pi.py`；`python work/verify-pi-boundary.py` | [回放] | 输入流覆盖与负例通过；sequence 仅观察值，checksum 算法未解；**已降级** |
| NOTI561 type17 | `python work/decrypt-noti561-fixed.py 'G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'` | [回放] | 57 wrapper 中 54 type17：GenKey/Cipher/CRC/再加密/内层长度/正文 CRC/解压通过；**已闭合（业务字段另列）** |
| NOTI561 type19 | 同上；`python work/verify-noti561-envelope.py` | [回放] | 3 帧原 SO `DPInterData::unpack` 通过；length=7/kind=5/flag=0/BinCount=5；作用未验证；**已降级** |
| inner opcode 命名空间 | `noti561-inner-coverage.tsv` + `opcode-map.tsv` | [静态]+[回放] | 9 个 (category,opcode) 组合；category=1 按 CMD，应答不是 NOTI 名称；**已闭合（语义有限）** |
| G_Cipher singleton | `python work/verify-cipher-snapshot.py work/out/peek-key2.txt` | [静态] | pool 快照不是 singleton；运行实例逐字节值缺失；**动态阻塞** |
| restriction 实际行值 | `work/out/login-restriction-static.txt` | [静态] | SQL/解析/默认分支已知，DB 实际值未读取；**动态阻塞** |
| bridge | `Get-Content .superpowers/sdd/2026-10-05-gameplay-evidence-program/RESUME.md`（§6.5）；`Get-Content .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/dbmw-recon-report.md` | [静态] | 仅保留后续进程/协议边界；本表不扩展；**范围暂缓** |
| dbmw | `Get-Content .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/dbmw-recon-report.md` | [静态]+[抓包] | UDP 业务/TCP 心跳边界已记账，字段级协议不属本表；**范围暂缓** |
| 辅助服务 | `Get-Content .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/client-packet-coverage.md` | [未验证] | 未采集协议不阻塞客户端密码收尾；本表不扩展；**范围暂缓** |
| 反外挂本体 | `work/out/offline-coverage-final.md` | [静态] | 排除 gunnersvr/secagent/zergsvr；仅闭合客户端 SO 边界；**范围暂缓** |
| LOGIN 真实接受/拒绝 | 本轮不重新连接；仅引用既有原始回放输出 | [真机] | 本表不把明文/结构验证写成成功登录；真实状态机单列；**动态阻塞** |

## 历史错误的显式更正

- [作废] “尚未解出 CMD1”——被 `work/out/all-verify-static-password.txt` 的 CMD1/RSA 回放推翻；准确说法是输入结构与密码运算已闭合，真实接受仍未由本表断言。
- [作废] “NOTI561 只有 432B 失败序列”——被三条 VM S 流 57 wrapper 的 `verify-noti561.py`/`decrypt-noti561-fixed.py` 推翻；432B 早期样本不得代表 type17/19 全集。
- [作废] “所有离线均已完成”——被 `offline-coverage-final.md` 与本账本列出的 singleton、restriction、LOGIN 生产者/状态机缺口推翻。
- [作废] 历史“pool[0] 就是 singleton”——被 `cipher-audit-corrections.md` 与 snapshot verifier 推翻。

## 硬门状态

H01 仅完成 PowerShell 身份初核，尚待第二方式复核与环境清单；H02–H08 均待本轮执行验收。上表的通过计数均为历史报告转述，不是本轮 fresh PASS；尚未复核的转述一律 [未验证]，不得用于签收。

最终状态：`BLOCKED`。所有上表技术状态是待验收草稿，不是本轮完成声明。T1尚无合格的逐项命令/输出绑定与提交；T2仅执行验证器；T3两路径未完成；T4只执行了13条命令、完整审阅未完成；T5仅草稿；T6只交付阻塞报告。限制项不会被改写为已解决。
