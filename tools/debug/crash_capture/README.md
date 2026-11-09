# 单人战斗崩溃诊断包

用于追查“剑魂在天空之城使用里鬼剑术接强制上挑，出现 Error launching CrashSender.exe”的故障。本包收集证据，尚未确认或修复原始崩溃。

## 玩家操作

1. 将诊断包完整解压到任意可写目录，例如桌面。保留工具文件在同一文件夹。
2. 正常使用登录器启动游戏，登录角色并进入城镇。
3. 双击 `Start-Capture.cmd`。看到 `MONITOR READY` 后，保留黑色窗口，回到游戏复现。
4. 再次出现崩溃弹窗时，先保留弹窗和游戏。切回黑色窗口，等显示 `dump saved`，按 **Q** 停止抓取。
5. 压缩 `CrashReports` 中本次生成的整个文件夹，私下反馈给维护者，同时说明崩溃时间、地图、武器和最后操作。

如果游戏已经弹窗卡死、此前没开监视，可先双击 `Capture-Open-Dialog.cmd` 保存当前现场，再关闭游戏。这种事后快照不保证仍能还原最初的故障指令，优先使用上面的提前监视流程。

运行多个游戏时需要从列表输入对应 PID。若工具提示访问被拒绝，且游戏以管理员运行，请右键相应 cmd 选择“以管理员身份运行”。

## 抓取内容与边界

- 针对 Windows 10 / 11 的 x86 DNF.exe；可配合 x86 或 x64 登录器。工具使用 Windows 自带的 DbgHelp，无需下载安装 ProcDump。
- 本包不替换客户端 DLL、PVF、登录器或设置，也不连接服务器、上传文件或终止游戏。
- 记录异常码、故障模块与偏移、x86 寄存器、栈及小型内存转储；补充客户端文件哈希、安装清单和现有日志末尾，不读取登录器配置。
- 原生探针也会触发可以正常处理的异常，因此“产生转储”不等于“确认崩溃”。监视持续到按 Q、Ctrl+C 或游戏退出，最近 16 个转储循环保留；真正复现后应及时停止。
- 写转储时游戏会短暂停顿，调试附加也可能改变复现时序。转储的取消检查限制约 5 秒 / 64 MiB，但实际限制粒度取决于 Windows DbgHelp 回调。
- 抓取文件包含进程内存，应私下反馈，不上传到公开仓库。工具退出时关闭“随调试器退出杀死游戏”的行为。
- 本机验证在 Windows 11 上进行；所用 API 支持 Windows 10，Windows 10 实机验证待反馈。

## 维护者

独立构建：CMake 3.24+，Visual Studio，`-A Win32`，Release；静态 CRT。默认不参与服务端或客户端发布。

测试：另行构建本目录的 `tests`（同样使用 `-A Win32`），运行 `python test_capture.py 抓取程序路径 测试DNF.exe路径 证据输出目录`。仅对测试自己启动的子进程检查真实访问冲突、未处理异常、主动脱离、快照和 16 份循环保留。测试产物中的 DNF.exe 不得分发给玩家。

异常通过 `DBG_EXCEPTION_NOT_HANDLED` 交还游戏原本的处理链；只消费附加调试器所产生的首个系统断点。转储从独立进程写出，避免在异常线程内调用 DbgHelp。异常对象和 CONTEXT 已复制至抓取进程，`ClientPointers=FALSE`。

参考：[DebugActiveProcess](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-debugactiveprocess)、[DebugSetProcessKillOnExit](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-debugsetprocesskillonexit)、[MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump)。
