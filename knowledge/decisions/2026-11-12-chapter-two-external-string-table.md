# 第二章可选外置文字表

2026-11-12，`codex/chapter-two`。本项已完成独立模块和原生离线执行验收；启动器接线、受限宿主现场验收由主任务统一完成，本文不将离线结果记为真机通过。

## 用户可见契约

第一章启用 `UseExternalDnfStringTable`：用户可在客户端 EXE 同级放 `Plugin/dnf.str`，使用 CP936 文字表覆盖内置文字；没有文件或 Plugin 目录时仍读原资源。已存在但为空、超过 16 MiB、含 NUL、UTF-8/Unicode BOM 或不合法 CP936 的文件返回错误，不悄悄退回内置。第二章此前只把原生临时 `dnf.str` 放到容器 Temp，未读取这个可选外置文件。

新增 `ChapterTwoExternalStringTable.{h,cpp}` 仅适配第二章提取调用，复用第一章 `external_string_table.cpp` 的 `BuildPluginPath/ExtractOrFallback`。未调用第一章 hook 安装器，也不复制第一章地址。原生解析器继续决定文字表的内容语法；编码合法不等于条目完整。实际翻译文件应以匹配版本的完整第二章表为底稿，保留 ID、格式参数和换行转义。本项不改变第一章既有语法策略。

官方 `ChapterTwo/Start-Client.ps1:173` 将执行宿主复制为 `$Game/s1c2_chapter2_host-*.exe`，随后运行这一副本。因此 `GetModuleFileNameW(nullptr)` 的父目录就是本次客户端根，`Plugin/dnf.str` 不是相对启动器解压缓存或 CWD。适配器拒绝相对 EXE 路径；主入口拒绝非 `0x400000` 基址。临时目标仍由宿主先行设置的 `[0xD49AB8]` 和原生 `0x4016B0` 解析，未在客户端根新增临时表。

这修正了早期 `triage/REPORT.md` 对运行 EXE 目录的不确定描述；日后若启动链改为在缓存目录直接运行，必须一并调整此契约。

## 原生证据

原始映像：`dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DNF-rebuilt-v3-probe.exe`，SHA256 `9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb`。

| 原生位置 | 实际行为 |
| --- | --- |
| `9BFFE0` | 第一次查文字时初始化表、提取、解析，再执行查询 |
| `9C003A -> 9BFBB0` | 唯一替换点；`int __cdecl(const char*)`，调用者 `add esp,4`，返回 `<=0` 走原错误路径 |
| `9BFBB0` / `9BFC40` | 读取 `File` 资源 115，逐字节 XOR `0xBD` 后写目标文件 |
| `9C006C -> 9BFCD0` | 原解析器，保留；`fgets` 缓冲大小 `0x800`，读取十进制 ID 和 `>`，处理文字及 `\\n` |
| `9C00BC` / `9C00A0` | 成功 / 解析失败后的原生临时文件删除，保留 |

资源原始字节是编码后的数据，不能直接当外置表。按原生 XOR 解码后为 118208 字节、4195 行、2886 个唯一数字 ID，无 NUL、合法 CP936，最长行 1018 字节；解码 SHA256 `7608fce8cd9441e07dcdc15a3f2fc8aa575caf3e430c29e2c9a19e1ae932266e`。`dist/c2-parity-next-20261112/external-strings/collect_evidence.py` 可重新生成汇总、七段反汇编和完整表。`single-label-override-dnf.str` 仅将 ID 187 改为 `C2 External fixture <O>`，其他条目来自同一原映像。

安装前检查完整提取/解析/删除上下文、原提取器入口与资源编号、XOR 循环、解析器入口、路径解析器入口和资源类型字符串。只写 `9C003A` 的 5 字节 CALL；已安装同一目标时幂等，未知 CALL 或邻接签名拒绝，写入失败包括“先复制后报错”均恢复原 CALL。

## 离线验收

`external_string_table_tests.cpp` 将校验过的原 PE 映射到 `0x400000`，实际执行完整 `9BFFE0` 查询、`9BFBB0` 提取/XOR、`9BFCD0` 解析、原生字符串/排序/查找/清理代码。仅替换资源 API、路径环境、堆及 CRT 文件边界，未伪造文字解析或查找结果；异常消息框被测试夹具拦截，不操作 UI。

- RED：未安装补丁，6 checks / 1 failure。有效完整外置表仍被原生忽略，指定文字断言失败；内置解析与清理正常。
- GREEN：45 checks / 0 failures，MSVC x86 `/W4 /WX /utf-8`。外置 ID 187 通过原查询返回标记、ID 186 保持“物品栏 <I>”；删除可选表和删除 Plugin 目录分别恢复内置。源文件不被原生清理删除，容器临时表照常删除。
- 边界：空文件、UTF-8、UTF-8/UTF-16 BOM、坏 CP936、NUL、16 MiB + 1、被锁定的源文件拒绝且保留已有 staging；精确 16 MiB 按 C1 契约可提取；目标写失败、空参数、相对 EXE 路径和原资源失败返回值覆盖。
- 安装：逐处签名破坏拒绝，非本模块 CALL 不覆盖，写前/写后失败回滚，幂等以及仅 5 字节范围修改。
- 独立复核：`c2_wheel_parity` 重跑最新 exe，`review-wheel-green-20261112`，exit 0 / 45 checks / 0 failures，无阻塞发现。

日志位于同一 artifact 目录：`red-native-1.txt`、`build-green-final.txt`、`green-native-final.txt`。未修改共享 CMake/Build-PresentationHost；全项目回归由主任务接线后执行。

接线需要把新 `.cpp` 与 `${IJL}/external_string_table.cpp` 编入宿主（`memory_patch.cpp` 已有），在首个原生文字查询前调用 `InstallExternalStringTable(image, bytes)`；和日志 `fopen` 分流兼容，因为后者仅匹配两个日志名。本项不依赖后台策略或滚轮策略。

## 现场最短验收

只在本轮隔离客户端目录中执行；如已有 `Plugin/dnf.str`，先保留其字节、哈希和恢复位置，不能覆盖用户原件。

1. 无外置文件启动本轮宿主，进城并打开常见菜单，保留原文字基线。
2. 将完整 `single-label-override-dnf.str` 放入隔离根 `Plugin/dnf.str`，重启。确认 ID 187 对应“系统设置 <O>”的实际消费者显示标记，同时相邻物品栏文字正常；若该 UI 不使用 187，不以看不见标记判定加载失败，先通过只读查表定位可见消费者，再换一条已确认的菜单键。离线已经证明 187 查询替换，但不冒充已确认具体现场控件。
3. 确认外置源文件哈希不变，容器 staging 被原生删除；退出、移除测试外置表、再启动，确认原文字恢复。恢复先前用户文件。
4. 无效编码/空文件由离线边界覆盖；如现场演示错误路径，只使用独立失败启动，不把原生报错退出当成功启动。需更友好错误提示可另做启动前校验，不在本模块改原解析行为。
