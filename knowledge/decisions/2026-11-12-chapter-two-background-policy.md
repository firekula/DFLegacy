# 第二章后台策略与启动窗口事件

2026-11-12。在 `codex/chapter-two` 上只读核对第二章原始重建映像，并新增独立安装器及离线测试。本文记录的阶段**没有启动游戏、操作 UI、部署客户端或更改主线**；真机行为须由后续隔离宿主逐项验证，不能将模拟执行结果记为现场帧率或窗口行为验收。

原始映像为 `dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DNF-rebuilt-v3-probe.exe`，SHA256 `9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB`，ImageBase `0x400000`，SizeOfImage `0xE57000`。未改写此文件。

## 原生证据与第一章对应关系

| 第一章条目 | 第二章已确认原逻辑 | 独立适配 |
| --- | --- | --- |
| `dnf_patches.cpp` 后台主循环等待 | `0xA3CEE8` 读取窗口对象 `+0x20` 激活值；`0xA3CEED` 的 `ja +0xA` 仅在非零时跳过 `Sleep([edi+0x0C])`；`0xA3CEF9` 后继续引擎时钟及帧回调 | `0xA3CEED` 从 `77` 改为 `EB`，仅跳过等待，不伪造焦点 |
| 失焦仍用正常优先级 | `0xA3D330` 窗口处理器分派消息 6。`0xA3D386` 取 `LOWORD(wParam)`，`0xA3D38B` 写回真实激活字段；`0xA3D38E` 分支选择 Normal/0ms 或 BelowNormal/15ms | `0xA3D38E` 从 `76 1B` 改为 `90 90`；保留字段写回、原 `SetPriorityClass(...,0x20)` 调用与事件后续处理 |
| 不向 Shell 发送启动最小化 | `0x40D410` 的 type1 构造分支查找 `Shell_TrayWnd`，随后在 `0x40D48C` 发送 `WM_COMMAND=0x111,wParam=0x19F` | `0x40D47E` 从 `74` 改为 `EB`，沿原退出路径跳过发送；其他构造逻辑保留 |
| 不创建全局启动最小化事件 | CRT 表 `0xCB2088` 指向 `0xAE0F80`；该入口以 type1 构造对象 `0xD564B0`，再用 `atexit` 注册 `0xAE6D90`。后者转入析构 `0x40CDA0`，type1 分支发送 `WM_COMMAND/0x1A0` | `0xAE0F80` 从 `6A 01` 改为 `C3 90`，同时跳过启动事件和配对退出注册，避免无配对的 Shell 恢复 |

这些位置由第二章自身导入表、指令与交叉引用得到，没有套用第一章绝对地址。`SetPriorityClass` 的其余调用位于 `0xA04A60/0xA04B10` 原生时钟校准过程，保存并恢复原进程/线程优先级；不修改这些临时校准调用。第二章现有 `MeasureMainLoopSleep` 只测量后调用原 Sleep，不能当作已具备后台不限速。

## 安装边界

新增 `src/DFLegacy.ChapterTwo.Native/ChapterTwoBackgroundPolicy.{h,cpp}`：

- `InstallBackgroundPolicy(image, bytes)` 安装后台等待与优先级两项。
- `InstallStartupWindowPolicy(image, bytes)` 安装启动窗口事件两项。
- 每组先验证完整的相关指令上下文；启动组额外验证 Shell 类名、CRT 表引用和退出 thunk。任一不匹配或一半已修改时零写入拒绝。
- 每组仅改 3 个字节；完整重复安装无写入成功。注入写入失败的测试覆盖复制前/后的两种失败，并恢复本组全部已触及字节。

接线时把 `ChapterTwoBackgroundPolicy.cpp` 加入原生库、包含其头文件，并在原映像入口执行前调用这两个独立入口。**后台入口必须早于 `InstallSlowFrameTiming`**：它核对原始 Sleep 指令，后者会改写 `0xA3CEF3`；后台安装本身仅改 `0xA3CEED`，不会破坏后者从 `0xA3CEEF` 开始的 Sleep 签名。不要声称可以在计时适配之后重新安装后台组。

本阶段未改共享 CMake/Build-PresentationHost 接线。宿主源码中原映像映射、补丁安装均在调用原始入口之前，适合安装静态初始化抑制。两个安装结果应分别记录，以支持单独启用、单独现场验收和明确失败归属。

## 离线验证

`background_policy_tests.cpp` 由 MSVC x86 `/std:c++17 /W4 /WX` 编译，与实际生产 `memory_patch.cpp` 共同运行：48 项、0 失败。覆盖真实 PE 映射、上下文损坏零写入、两处写入前后失败回滚、仅改预定 3 字节、完整重复安装、部分安装拒绝、两个独立生产入口和映像导出。

`tools/acceptance/chapter_two_background_policy_test.py` 使用 Unicorn 执行原映像的 x86 指令。只有 Windows API 和 `atexit` 边界用有界返回桩替代，记录调用参数；没有调用真实 Windows Shell、Sleep 或 SetPriorityClass。

| 输入与预期 | 结果 |
| --- | --- |
| 未修改原图，要求全部新策略 | RED：16 项中 5 项失败，分别为后台 Sleep、两种 LOWORD 为 0 的失焦优先级、静态启动事件、直接构造最小化 |
| 生产安装器同时安装两组 | GREEN：16 项、0 失败 |
| 仅后台组，要求启动事件保持原逻辑 | GREEN：16 项、0 失败 |
| 仅启动组，要求后台等待/优先级保持原逻辑 | GREEN：16 项、0 失败 |

激活检查使用 `wParam=0,1,2,0x10000,0x10001`，验证真实低字激活字段保留；静态启动检查覆盖 `atexit` 是否注册；直接构造检查保留事件 type1。原图 RED 在编写生产安装器前执行。项目全量原生回归及宿主构建由接线阶段统一执行，本阶段不宣称全量 suite 通过。

证据在 `dist/c2-parity-next-20261112/background/`：`original-evidence.json`、七份 `.asm`、`red-original.json`、`green-installed.json`、`green-background-only.json`、`green-startup-only.json`、`installer-tests.log`。`analyze_original.py` 可重新生成导入/交叉引用/反汇编；安装器测试的第三个可选参数为映像导出前缀。

复现示例（仓库根目录）：

```powershell
& dist/c2-parity-next-20261112/background/background_policy_tests.exe `
  dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DNF-rebuilt-v3-probe.exe `
  dist/c2-parity-next-20261112/background/installed
python tools/acceptance/chapter_two_background_policy_test.py `
  --image dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DNF-rebuilt-v3-probe.exe `
  --mapped dist/c2-parity-next-20261112/background/installed-all.bin `
  --result dist/c2-parity-next-20261112/background/green-installed.json
```

## 现场验收仍待

后台组：同一场景/客户端/配置对照前后台原生帧率与进程优先级，验证失焦和重新激活、键鼠输入及后台逻辑仍推进。FPS 实际数值不能由移除 15ms 等待推算。现有性能面板会暂停后台采样，现场必须使用独立只读取样或原生计数，不以面板“采样暂停”推断客户端仍被限速。

启动组：隔离启动前后记录一个可控的其他普通窗口是否被最小化，正常退出后确认没有不必要的全局恢复。需区分能力受限容器原先已阻止 Shell 命令与本次消除命令路径；原路径会发送命令的静态事实不等于当前容器中已经观察到其他窗口被最小化。
