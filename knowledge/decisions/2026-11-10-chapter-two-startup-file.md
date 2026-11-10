# 第二章新副本缺失登录文件的启动修复

## 已有原生证据与根因

昨日独立新副本 `G:/Project/DNF2-native-five-rarity-QA-20261109/DNFS1C2` 没有 `loginInfo.inf`。普通和 QA 资源、旧和新宿主均出现 `EXIT c0000005`，因此不能把这一启动崩溃归因于 HUD 新宿主或 QA 资源。

离线原生追踪已经确认：第二章 `96C53C` 以 `wt` 打开缺失的 `loginInfo.inf`，RX 隔离 profile 无法创建文件，`fopen` 返回空指针；随后 `fprintf A61765` 经 `_lock_file A61CD0` 进入 `RtlEnterCriticalSection`，`ecx=0x24`。补 `DFighter.ini` 没有解决该问题，不是这次修复的依据。

以根进程权限预置一个空 `loginInfo.inf` 后，普通宿主实际到达频道并稳定运行，最终通过原生菜单正常退出。证据是 `dist/chapter-two-native-retest-20261109/commerce/five-rarity-environment-prep-DO-NOT-SHIP/control-Baseline-20261109-214122-482`、实际客户端日志 `chapter-two-client-A-20261109-214122-890-6.log` 的 `EXIT 00000000` 以及主记录的 native-evidence76。此前试验中的 RW grant 使用了与运行 A 实例不符的 SID，之后已清理；不能声称扩大文件 ACL 解决了崩溃。最小解释是空文件使原生走已有文件的 `rt` 分支，无需创建文件。

## 生产改动

仅修改 `src/DFLegacy.Launcher/ChapterTwo/Start-Client.ps1`：

- 在受控 Game 目录、启动 broker 之前预置空 `loginInfo.inf`，使用 `CreateNew`，已有文件直接保留，不打开、不截断，也不复制历史登录信息或密码。
- 正常并发创建时，只忽略精确的 Windows 文件已存在错误 80/183，并再次确认目标为文件。目录冲突、父目录缺失、拒绝写入等错误向上传播；没有新建父目录或扩大权限。
- 保留原有 profile RX grant，不更改 `DFighter.ini`。
- broker 退出 0 后仍必须取得有效目标 `EXIT` 记录，而且每个有效记录均为 0，才能输出成功日志。无记录、超出 uint32 的记录或原生非零退出均失败。原有导入错误检测、48 次 preferred-base 重试和候选文件清理保留。

broker 退出状态与被调试游戏退出状态不同，昨日实际 `EXIT c0000005` 样本可以伴随 broker 退出 0；以前 helper 会误报成功。本次只修正结果判定，不修改 broker。

## RED / GREEN 与测试边界

新增 `src/DFLegacy.Launcher/tests/ChapterTwoStartup.Tests.ps1`，不依赖第三方测试模块。所有运行输出位于 `dist/chapter-two-main-sync-20261110/startup-file`。

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File src/DFLegacy.Launcher/tests/ChapterTwoStartup.Tests.ps1
```

测试复制完整生产 helper 到各自的新建测试目录，只替换 mutex 名称以隔离真实游戏槽位。以精确路径的 PowerShell 函数拦截 broker 与 `icacls`，通过标准输入提供合成凭据；从不启动真实 broker、游戏或服务。宿主夹具仅为 128 字节不可执行数据。磁盘创建、文件锁、字节保存、日志落盘、候选清理、错误传播及实际启动控制流均执行生产代码；并非只校验源码文本。

退出 fixture 按昨日真实日志逐行保存了原生崩溃、正常退出和 preferred-base 三组关键记录；来源与原日志 SHA256 位于 `fixture-provenance.json`。没有保存登录命令或明文凭据。8 个独立 runspace 还通过 AST 提取并执行实际文件初始化函数，验证并发调用均不误报且最后只留下空文件。拒写测试只在测试拥有的目录暂加当前用户的 CreateFiles deny，并在 finally 恢复；实际拒绝控制和初始化函数的拒绝传播均验证。

先对未修改 helper 保存有效 RED：52 检查、8 失败。最终同一测试源对 `Start-Client.RED.ps1` 再跑为 61 检查、10 失败，记录在 `RED-final.log`。其中 9 个失败来自完整生产脚本的可观察错误行为，另 1 个说明旧脚本没有供并发测试调用的初始化函数，并非用缺函数错误替代行为 RED。首轮修后还增加了“无效 EXIT 后跟正常 EXIT”的独立失败回归，`RED-exit-guard.log` 为 74 检查、1 失败，随后才收紧格式校验。

修后最终 GREEN 为 74 检查、零失败，记录在 `GREEN-final.log`。与最终 RED 相比，检查数增加来自初始化函数存在后才执行的父目录缺失、拒写和 8 个并发调用检查。覆盖正常成功、崩溃但 broker 0、broker 非零且无 EXIT、broker 0 且无 EXIT、超范围 EXIT、无效或非零 EXIT 后跟正常 EXIT、导入错误、preferred-base 后成功及 48 次耗尽；文件覆盖已有且被独占打开时不变、新建空文件、目录冲突、拒写和并发。

本专项只证明受控文件系统与脚本控制流。Windows broker/profile 实际权限、真实游戏 `rt` 路径、完整交付集成以及新副本原生启动由主任务继续验收，不能据此宣称新 helper 已通过真机。全仓与 Launcher 回归由同时执行主线同步验证的主任务统一记录。本次没有操作 UI、启动客户端/服务、git add 或提交，也没有修改其他生产源码、CMake 或测试 Program。
