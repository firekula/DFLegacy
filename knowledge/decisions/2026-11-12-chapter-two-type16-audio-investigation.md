# 第二章 Type16 通知音效调查

2026-11-12 初始只读原生调查；当时未实现音效本地补丁、未发送聊天、未录音或操作游戏。后续实现与离线证据见末节，现场结果仍须单独记录。

## 已确认的缺口

第一章 `message_audio.cpp` 在聊天声音调用前检查消息类型：Type16 文本仍显示，但不调用 `CHAT_MESSAGE`；其他类型尾跳原声音函数，保留声音参数。该功能由 `DisableType16ChatMessageSound` 启用。

第二章原始映像 SHA256 `9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb` 的 `926D70` 包装器：

1. `[ebp+0xC]` 为消息类型，`926D86 -> 9257D0` 先显示文本，后者在 `925BCC` 以 `ret 0x14` 返回。
2. `926D8B` 起压入 `0,0,-1,"CHAT_MESSAGE"`，`926D96 -> 43C160` 无条件发声音事件；随后 `add esp,0x10`，包装器 `ret 0xC`。
3. 包装器直接调用点为 `92742C/9274EC/92756D`，来自聊天入口 `9272D0`。Type16 不属于 13/14/15 的特殊处理分支，走这个正常文字及声音路径。

因此“缺少 C1 同等类型门控”有原生代码证据；“当前隔离宿主实际会响”仍要声音现场证据，静态调用本身不证明设备输出。`43C160` 全映像有大量其他调用，不能全局静音。现有 loot hook 仅消费精确私有 Type16 标记，普通 Type16 不在其范围内。

反汇编在 `dist/c2-parity-next-20261112/external-strings/type16-*.asm`，由同目录 `collect_evidence.py` 对原图重新生成。

## 可重复、不会切换掉落状态的现场样本

当前服务端已经提供一条测试账号的本地反馈路径：`DebugChatCommands.IsDropEverythingCommand` 只允许已有 `test` 测试账号精确输入 `掉落测试`（无斜杠）。`EntranceService.cs:6689` 消费命令，不按普通聊天广播。

**限定人在城镇、没有 dungeonInstance。** `ToggleDropDebugAllAsync` 在 `3233` 判断该条件，回本人“请先进入副本…”后立即返回，未执行后面的掉落开关。`SendDebugNoticeAsync` 使用 `NOTI 12 / Type16Brown / target uid 0`，CP936 正文，并走现有 TX 记录。这是可反复生成的普通 Type16，不是 loot 私有标记。其他账号输入同一句会按普通聊天处理，不能当 Type16 样本；在副本中输入会切换掉落策略，也不能用于此无状态样本。

不满足该账号条件时，可用已有真实结算流程的“n 秒后自动返回城镇”通知：`DungeonPartyService.cs:571–578` 只给本人发送 Type16，最后 10 秒逐秒产生。不要为此伪造超重、发公告或改变真实角色数据。

## 具体测量与通过标准

先用本轮隔离会话，开启游戏音效，并让其他应用安静。普通聊天正对照可在隔离空城中由同一测试角色发送，现有服务端回 `NOTI12 / Type03White`（`EntranceService.cs:6778`）。推荐顺序：普通 Type3 → 间隔两秒 → 城镇上述 Type16 → 间隔两秒 → 普通 Type3。

每条样本同时保存：带类型的接收/TX 包时间、聊天文字截图、进程/声音设置，以及系统输出回环音频。计数诊断应在 `926D96` 调用边界记录消息 type 与事件名，并统计实际进入原 `43C160` 的 `CHAT_MESSAGE`，正常样本尾跳原函数、保持原参数；记录到固定容量内存或现有诊断流，不能在声音热路径打印或调用声音 API。此处只给诊断设计，未安装诊断或静音 hook。

基线只有在 Type3 正对照确实可闻时，Type16 的录音才有解释力。若 Type3 无声，先查声音设置/素材/设备；不能把两个都无声当静音补丁成功。

候选静音实施只需在 `926D96` 放类型 gate：入口 `[esp+0x20]` 为原类型；16 直接 `ret` 让原调用者清理四个声音参数，其他类型尾跳 `43C160`。需校验包装器、显示函数 `ret 0x14`、三个上游调用点和事件字符串。此点不修改 `9272D0`，与 loot hook 可共存。

通过标准：补丁前 Type16 显示且产生一次原事件，补丁后同样文字显示但原事件增量为 0；两个普通 Type3 控制样本仍各增加一次原事件且仍可闻。录音辅助排除“事件有计数但音频从未输出”的假阳性。离线还须实际执行原包装器，检查 16/非16 的文字调用次数、参数、栈平衡及 callee-saved 寄存器；这些测试和正式 hook 均尚未实施。

## 本轮本地补丁与离线证据

`ChapterTwoType16Audio.h/.cpp` 仅替换 `926D96` 的五字节 CALL。安装前精确校验整个 `926D70–926DA1` 包装器（CALL 可为原始目标或当前同模式 gate）、原生显示入口和 `ret 14h`、三个上游调用及其传参、`CHAT_MESSAGE` 含终止零，以及共享声音函数完整函数体。公共安装接口只接受真实加载基址 `400000` 和映像长度 `E57000`。正文、发送者、显示顺序、其他声音调用者都不修改。正常模式仅 Type16 直接返回到原调用者；其他类型恢复入口全部通用寄存器/flags 后尾跳原 `43C160`。

热路径只维护固定 257 桶的原子计数：0–255 为实际类型，最后一桶合并其他有符号值；`originalCalls` 记录即将无条件尾跳原函数的调用，`skipped` 记录直接返回。计数是 uint32 累计值（模 2^32）；不重置，不分配、不读时钟、不做 I/O。声音设备仍可能静音，因此 original 计数不单独证明可闻输出。

`ReadType16AudioDiagnostics()` 可安全读取计数快照。`LogType16AudioDiagnostics()` 只供单一慢路径调用，每次至多输出16个变化桶；日志含 `event=CHAT_MESSAGE type=... original=... skipped=... delta_original=... delta_skipped=...`，未变化的桶不输出。普通进程仍由本地 broker 捕获 OutputDebugString 到会话 `chapter-two-client*.log`，由宿主现有每两秒诊断分段调用日志函数；不得在音效回调内调用它。

`InstallType16Audio(image, size)` 默认开启本地补丁。第三参数显式 `false` 只用于隔离基线测试，保留每条 Type16 的原声音调用并收集相同计数；无环境变量或默认关闭开关。已经安装不同模式后再次安装会拒绝，不能在运行中切换。每次重复安装仍校验周围原生上下文。写入失败、写后失败、假成功和部分写入均返回失败并尝试恢复原始 CALL，夹具验证恢复后的完整映像逐字节一致。

本轮先修正了旧离线夹具的真实错误：字体 vtable `+20h` 的测量函数只消费 text/length，下面两个已压参数保留给 `+28h` 换行函数；旧夹具将四参数都弹出，导致带发送者分支访问非法输出地址。修正后才取得有效 RED，不能将此前崩溃视为 Type16 回归失败。

离线构建在 `dist/c2-parity-continuation-20261112/audio/`，使用本机 `.tools/cmake-runtime/cmake/data/bin/cmake.exe`、VS 18 2026 x86、Release、`/W4 /WX`。原始映像 SHA256 仍为 `9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB`。

- `type16_audio_red.exe image`：99 checks、4 failures，四个失败全部是两个 Type16 各有/无发送者仍触发原声音。其余原生文字、发送者、声音参数和寄存器检查通过。
- `type16_audio_green.exe image`：365 checks、0 failures，真实原生流程追加12行，Type16原调用0/跳过4，Type3原调用2/跳过0；总原声音分发9（含一个不通过此包装器的直接调用）。
- `type16_audio_green.exe image --observe`：373 checks、0 failures，同样12行，Type16原调用4/跳过0，Type3原调用2/跳过0；总原声音分发13。

夹具执行原包装器、原文字显示/入历史和原声音分发；替换的只有无设备字体边界、时钟及 CRT 内存/格式化边界。另有独立 ABI 边界探针临时替换声音入口以核对 gate 的7个通用寄存器、算术/控制 flags、ESP；涵盖16、3、0、255、负值、256和两端有符号整数。安装检查逐个突变已校验上下文的223字节，确认未知映像零写入；还验证五字节范围、重复安装、模式冲突以及四种写入失败恢复。离线通过不等同实际声音/界面现场验收，现场由主任务另行执行并保存同一通知的前后样本。

集成需要将 `ChapterTwoType16Audio.cpp` 加入 `chapter_two_presentation` 源表，生成宿主 include 头文件并在既有启动安装阶段调用默认接口；每两秒慢路径调用诊断输出。正式 native 回归目标 `chapter_two_type16_audio_tests` 使用 `type16_audio_tests.cpp`，链接 `chapter_two_presentation`，继承 C++17、`/W4 /WX /utf-8 /EHsc`，链接选项 `/BASE:0x10000 /DYNAMICBASE:NO`；构建脚本在其他原生用例之后以原图路径运行该测试。共享构建文件由主任务统一改动，本子任务未改动它们。
