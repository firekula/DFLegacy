# 第二章原生日志明文负载与隔离归档（2026-11-12）

第二章已有宿主/性能日志，但原生 `DNF.trc`、`NeopleEngine.LOG` 仍在日志写入点调用 ROR2/XOR 0x9D 变换；游戏目录在标准启动链中只有 AppContainer RX 授权，单纯移除变换不能保证原生日志落盘。

本项以原始 `DNF-rebuilt-v3-probe.exe` SHA256 `9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB` 为准。`dist/c2-parity-next-20261112/triage/c2-native-snippets.txt` 保留原指令；没有沿用第一章固定地址。

## 原生接入

`ChapterTwoNativeLog.cpp` 仅在七个确认的日志调用点把五字节 CALL 改为 NOP：

| 原生VA | 用途 |
| --- | --- |
| A094C1 | DNF缓冲日志逐字节变换；C2比第一章六点表多此路径 |
| A09962 | DNF主写入变换 |
| A09A21 / A09A79 / A09B01 / A09B58 | DNF轮转标题变换 |
| A457F5 | NeopleEngine写入变换 |

全部保留参数准备、原cdecl清栈、缓冲边界、轮转和写入逻辑。共享变换函数 A455F0 完整校验但不修改，其他调用者仍保持原行为。每个目标的完整指令上下文和原生fopen薄包装一并校验后才写入；任一写入失败，包括已经写入但保护恢复/刷新失败，都尝试逆序恢复本次改动。已有补丁不会在回滚时被撤销；允许原始/已安装位置混合后补齐，事务承诺是恢复进入本次调用前的状态。

在 A610D0 的原生fopen薄包装使用六字节跳转，精确识别大小写不敏感的 `DNF.trc` / `NeopleEngine.LOG` 基名。二者写到 broker 传入的 `DFL_S1C2_CONTAINER_DATA/Temp/NativeLogs/<当前PID>/`。该环境值来自已验证启动器的 `GetAppContainerFolderPath`；模块本身不独立证明容器身份。目录逐层拒绝重解析点，PID目录必须新建，写入探测成功后才安装。准备或签名失败只清理自己刚建的空目录，因此同进程可重试。

其他文件和所有模式原样交给 A61074 的原生 `_fsopen`，共享模式0x40保持原fopen值；FILE对象仍由游戏CRT创建。未授予游戏目录额外写权限。路径不能无损转换到原生ANSI代码页时拒绝安装。

**“明文”指日志负载不再变换。** DNF缓冲流原有四字节零头及NUL终止仍保留，并不承诺文件是完全无二进制封装的文本。Neople写入器原有 `%s\n` 附加换行也保留。

## 隔离归档

`Start-Client.ps1` 从已经验证的容器SID独立调用系统API得到容器根；不信任客户端日志里打印的路径或PID。每个attempt的finally、profile cleanup之前，从broker独占写入的events文件读取唯一首行 `START <uint32 PID>`，只读该PID目录内上述两种文件，归档到游戏 `Logs/DFLegacy/chapter-two-native-<A/B>-<run>-<attempt>-<原文件名>`。

归档限制为16MiB/文件、单硬链接、普通文件；容器根至PID目录无重解析点。打开后用句柄再次核对最终路径、文件属性、长度和本次attempt之后的写入时间；排斥仍在写入/删除的共享访问。目标CreateNew，不覆盖旧证据。归档失败只产生warning，原有真实退出码、重试和异常路径继续生效。

## 已验证与尚未验证

证据目录 `dist/c2-parity-next-20261112/native-log/`：

- `red.txt`：直接映射原始C2映像、执行其Neople写入函数和DNF缓冲函数，7检查中恰好两项明文断言失败；旧Neople字节解码后与原文及原附加换行完全一致。
- `green-review.txt`：同类真实原指令夹具及生产安装器51检查通过；包括七个编码点和fopen的签名/失败回滚、重复安装、准备失败重试、实际双日志落盘，以及无关fopen实写透传。夹具仅替换外部CRT/路径依赖，不运行游戏入口。
- `archive-green.txt`：实际归档文件系统18检查通过，覆盖固定名称、字节保持、目标不覆盖、伪造/缺失/溢出/多START、旧PID、超限、占用、错误PID、目录重解析点、硬链接和非零退出日志。
- `startup-green.txt`：原完整启动脚本测试212检查通过；测试伪SID的系统目录查询warning是预期，不改变原崩溃/早退/重试判断。

上述不冒充完整游戏会话。最终接线宿主需由root构建后在真实AppContainer登录/进城/退出，核对trusted PID、容器原生输出及归档字节一致；轮转仍缺真实会话触发证据。没有主动制造游戏崩溃，也没有开启远程上传。后续现场结果应独立追加构建摘要和证据，不能把夹具进程标为真游戏进程。

2026-11-12 13:11，生产候选PID42136已进城。只读核对该会话trusted `START 42136`、完整宿主EXE路径及创建时间后，从该进程token读取AppContainer SID，再调用系统 `GetAppContainerFolderPath` 定位容器；未枚举其他容器。七个实际日志调用点均为NOP，fopen已跳转。固定目录 `Temp/NativeLogs/42136/DNF.trc` 为16384字节，SHA256 `9eef3a9f0b179b0fc3e5c320ab22f82f3a16cd48c24a1d69656026e6209aec93`，含未变换的静态文案 `Log File Opened`，同时保留原生二进制封装。`NeopleEngine.LOG` 尚未产生，退出归档尚未在本次读取时发生。证据在 `dist/c2-parity-next-20261112/creature-tabs/live-page-and-logs-42136-131127.json`；未改写、移动或提前归档文件。
