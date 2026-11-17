# DFOLauncher 单启动器布局设计

日期：2026-11-17
状态：设计已分节确认，待实施
范围：第二章 Windows x64 普通版，基于 v1.0.13-r2 客户端做**纯打包**升级（发行 `v1.0.13-r3`），不改客户端二进制、清单、收据或更新序列。

## 背景与目标

当前安装根目录同时存在固定入口、托管登录器、更新器、诊断、崩溃捕获、`DNF.exe` 和运行时提取的 `DNF-rebuilt-v3-probe.exe`，玩家容易点错 EXE（公司电脑曾双击 `DFLegacy.Updater.exe`，更新在安装阶段被父进程校验拒绝并循环回退）。目标：

- 外层只保留一个可执行文件 `DFOLauncher.exe`，其余客户端文件全部收进 `client\` 子文件夹。
- 这次不改客户端版本、清单和更新逻辑；`client\` 内部仍是现有 v1.0.13-r2 安装根，自动更新、入口迁移、收据校验照常工作。
- 现有安装不需要重新下载 1.1 GB：一个几十 KB 的启动器在本地完成一次性目录整理。

## 已确认的决策

| 决策 | 选择 |
| --- | --- |
| 布局 | A：`DFOLauncher.exe` 在外层，整个客户端移入 `client\` |
| 迁移 | 启动器自身完成一次性同盘改名迁移，自带回滚 |
| 发行身份 | 纯打包 `v1.0.13-r3`；客户端三程序/清单/收据保持 `1.0.13` / `v1.0.13-r2` 不变 |
| 启动器实现 | 原生 C++（无 .NET 运行时依赖），约 100–200 KB |
| 内部 EXE 改名 | 推迟到下一个客户端版本（v1.0.14），随新构建和入口迁移一起做 |

## 目标布局

```text
<安装目录>\
  DFOLauncher.exe                 # 原生启动器，固定不更新
  client\                         # 现有客户端，整目录原样
    DFLegacy.Launcher.exe
    DFLegacy.Launcher.App.exe
    DFLegacy.Updater.exe
    DFLegacy.Bootstrap.ini
    DFLegacy.Chapter.json
    DNF.exe
    Script.pvf
    ImagePacks2\ / ImagePacks4\ / ...
    .dflegacy\v4\installed.json
    .dflegacy\v4\validated.json
    .dflegacy\chapter-two\settings\DNF_CHINA.cfg
    launcher.chapter2.json
    Config.ini
    Logs\...
```

- `client\` 就是当前安装根：固定入口、描述符、清单、收据、`validated.json`、游戏资源、`.dflegacy` 全部保持相对路径不变。
- `DNF-rebuilt-v3-probe.exe` 是第二章实际运行的重建游戏镜像，由客户端在启动时从内嵌资源提取到 `client\DNF-rebuilt-v3-probe.exe`；它随 `client\` 一起移动，不需要单独管理。
- `client` 默认可见（便于支持和排查），不加隐藏属性；外层视觉上只有一个 EXE 和一个文件夹。
- `DFOLauncher.exe` 不在更新清单里，不被更新器管理，也不更新自己。

## DFOLauncher.exe 设计

### 角色与边界

- 只做两件事：启动 `client\DFLegacy.Launcher.exe`；首次运行时把旧布局整理进 `client\`。
- 不读取更新清单、不联网、不校验客户端文件、不修改收据；客户端自己的校验和更新链保持原样。
- 进程单实例：用命名互斥量（键为安装目录哈希）或 `%TEMP%` 下的锁文件互斥，优先命名互斥量，避免在外层留下常驻文件；拿不到锁说明另一个启动器正在运行/迁移，直接退出。

### 启动模式

1. 取自身目录（`GetModuleFileNameW` 后规范化）。
2. 若 `client\DFLegacy.Launcher.exe` 存在：以 `client` 为工作目录 `CreateProcessW` 启动它，透传命令行参数；启动成功后自己退出。
3. 若不存在，但当前目录存在旧布局（`DFLegacy.Launcher.exe` + `DFLegacy.Launcher.App.exe` + `DFLegacy.Updater.exe` + `DFLegacy.Chapter.json`）：进入首次迁移。
4. 两者都不满足：经典风格提示“客户端不完整”，说明重新解压启动包或修复，不移动任何文件。

### 首次迁移

1. 拿命名互斥量。
2. 枚举进程，若存在映像路径位于当前目录下的 `DFLegacy*`、`DNF.exe`、`DNF-rebuilt-v3-probe.exe` 等客户端/游戏进程（排除自身）：提示先关闭客户端，直接退出，不做任何改动。
3. 经典风格确认框：把当前客户端整理到 `client\`，以后统一从 `DFOLauncher.exe` 启动。
4. 写迁移日志 `DFOLauncher.migrate`：魔数/版本 + 待移动的顶层条目名单 + 目标 `client`；先写临时文件再原子改名并 flush。
5. 创建 `client\`；按名单逐项 `MoveFileW` 移入（同卷改名）。名单排除 `DFOLauncher.exe` 和迁移日志自身。任一项失败：按日志逆序把已移动条目移回外层，删除 `client` 和日志，保留原布局并报告失败项。
6. 校验 `client\` 内入口、App、Updater、描述符、`DFLegacy.Chapter.json`、`DNF.exe`、`Script.pvf` 存在；缺项按失败回滚。
7. 删除迁移日志。
8. 尝试更新桌面和开始菜单中指向旧 `<目录>\DFLegacy.Launcher.exe` 的快捷方式，改为指向 `DFOLauncher.exe`（`IShellLink` 读写）；找不到就只提示用户以后从 `DFOLauncher.exe` 启动。
9. 以 `client` 为工作目录启动 `client\DFLegacy.Launcher.exe`。若 `CreateProcessW` 失败，或子进程在约 10 秒内以非零码退出：提示“新版启动失败，是否恢复旧布局？”，确认后把 `client\` 内容移回外层并恢复旧入口。
10. 另提供显式恢复：`DFOLauncher.exe --restore` 在客户端关闭时把 `client\` 内容移回外层；用于迁移后过一段时间才发现新版无法使用、启动失败提示已经错过的场景。

### 中断与恢复

- 启动时发现 `DFOLauncher.migrate`：若 `client\` 已包含完整客户端（入口、App、Updater、描述符、`DFLegacy.Chapter.json`、`DNF.exe`、`Script.pvf` 齐备），视为迁移已完成，删除日志后直接启动；否则按日志把 `client\` 中对应条目移回外层，删除日志，再重新进入迁移；不把半迁移状态当成完成。
- `client` 存在但其中 `DFLegacy.Launcher.exe` 缺失且没有迁移日志：视为异常或人工改坏，提示并停止，不覆盖。
- `client` 或自身目录是符号链接/目录联接/重解析点：拒绝迁移并提示；迁移只在普通本地目录、同卷内进行。
- 回滚使用同一套日志逆序逻辑；任何一步失败都保留现场并给出可恢复的提示。

### 窗口与文案

- 迁移进度窗口沿用 `src/DFLegacy.Startup/startup_prompt.cpp` 的原生窗口风格：金棕边框、像素字体、标题“DFLegacy”，显示“正在整理客户端（n/m）”；移动是改名，通常一闪而过，延迟约 200ms 再显示避免闪烁。
- 确认/错误提示使用同一原生风格的简单对话框；文案中文，说明“以后请从 DFOLauncher.exe 启动”。
- 迁移不可取消；用户若关闭窗口，按失败处理并回滚。

### 产物与属性

- 项目：`src/DFLegacy.LauncherStub/`（CMake + MSVC x64，Win32，无运行时依赖），构建脚本 `build-native.ps1`，测试目标单独输出。
- 输出文件名 `DFOLauncher.exe`；`.rc` 版本资源：文件说明 “DFLegacy Launcher”、原始文件名 `DFOLauncher.exe`、产品名 `DFLegacy`、发布者 `DFLegacy`、版权按现有 EXE、文件版本 `1.0.13.0`、产品版本 `1.0.13+<提交>`。
- 图标复用现有启动器图标；未签名，发行说明附 SHA-256。
- 启动器不进入 787 文件清单，也不进入客户端的更新管理；将来要改只能换一个新的小 EXE。

## 客户端与更新链

- 客户端三程序、清单、收据、序列全部保持 v1.0.13-r2；`client\` 内部路径与当前安装根一致，自动更新、修复、入口迁移不改一行代码。
- 迁移后首次启动，`validated.json` 的绝对根路径不再匹配，客户端会做一次完整校验并重写 `validated.json`；这是预期行为，不影响收据和账号。
- 账号、显示设置、`DNF_CHINA.cfg`、`Config.ini` 随目录移动，不需要额外复制。
- 后续更新（入口哈希不变的版本）在 `client\.dflegacy\v4` 内照常自动下载安装；入口哈希再变的版本继续用已验证的入口迁移对。

## 打包与发行

### 新布局启动包

- `DFLegacy-ChapterTwo-v1.0.13-r3-win-x64.zip`：
  - 外层 `DFOLauncher.exe`
  - `client\`：现有 v1.0.13-r2 客户端目录逐字节复制（含 `.dflegacy\v4\installed.json` 和 `validated.json`）
- 按镜像各生成一份（收据 `source` 不同）；复用现有 Build-Starters 产出的客户端目录再套一层目录，不重建客户端二进制。
- 打包脚本校验 `client\` 内 787 个文件的哈希与清单一致，并校验 stub 的哈希。

### 现有安装过渡

- `v1.0.13-r3` 发行附件只放 `DFOLauncher.exe`（几十 KB）和说明；玩家把它放进当前客户端目录、双击迁移，不强制重新下载 1.1 GB。
- 旧 v1.0.13-r2 完整启动包继续可用于全新安装；装完后运行 stub 即得到新布局。

### 镜像与大文件

- 这次不向 GitHub 上传新的 1.1 GB 启动包（此前大文件上传后本机 IP 触发限流）；新布局包只上 GitCode。
- GitHub 侧放 stub、发行说明和 SHA-256；GitHub 恢复后再决定是否补传新启动包。
- Stub 本体很小，两个镜像都发布并做匿名回读。

### 版本与文档

- 发行标签 `v1.0.13-r3` 指向包含 stub 源码、构建脚本、发行说明和本设计的公共提交。
- 发行说明新增 r3 段落：如何从现有安装迁移、如何全新安装、未签名与校验值、已知边界。
- 决策与验收记录放在 `knowledge/decisions/`；实现完成后更新本设计的状态。

## 测试与验收

### 原生单元/夹具测试

- 布局识别：已迁移、旧布局、都没有。
- 迁移日志：写入、原子性、中断后恢复、回滚逆序。
- 移动失败：文件被占用、目标无权限、`client` 已存在但无日志。
- 路径安全：拒绝重解析点、拒绝跨卷（理论上同目录不会发生，仍要明确报错）。
- 参数透传、启动失败路径、`--restore` 显式恢复。

### 集成验收

- 用真实 v1.0.13-r2 固定安装副本：运行 stub → 核对 `client\` 布局、收据仍 `v1.0.13-r2`/seq 21、登录页 `v1.0.13`、账号/显示/`DNF_CHINA.cfg` 保留、更新检查仍正常。
- 迁移后进入游戏，确认 `DNF-rebuilt-v3-probe.exe` 在 `client\` 内提取并运行。
- 新布局启动包：解压 → 运行 stub → 登录 → 更新检查。
- 故障注入：迁移中途杀进程（下次启动恢复）、模拟新版启动失败（回滚到旧布局）。
- 公司电脑真实迁移一次，确认账号和设置还在。
- 最终 EXE 属性检查：文件说明、原始文件名、产品名、发布者、版权、文件版本、产品版本；实际启动截图。

## 已知边界

- `client` 被用户改名/搬走后，stub 只报错提示，不自动找回；不支持从网络映射、外置盘或联接目录迁移（检测到就提示先复制到本地）。
- 迁移瞬间需要关闭客户端和游戏；stub 只检测、不强制结束进程。
- stub 未签名，部分杀软可能对新 EXE 移动文件告警；发行说明给出来源和 SHA-256。
- 内部 EXE 改名为 2009 Win32 风格（候选：`DNFStart.exe`/`DNFLauncher.exe`/`DNFUpdater.exe`，或更朴素的 `Launcher.exe`/`Updater.exe`）延后到 v1.0.14，随新客户端构建、新清单和新的入口迁移对一起做。
- GitHub 新启动包上传延后到限流恢复；不影响现有安装过渡。

## 实施拆解（供 writing-plans 细化）

1. 原生 stub 项目：布局识别、启动、锁、进程检查、迁移/回滚日志、快捷方式更新、原生窗口与文案、版本资源。
2. 构建脚本与测试夹具：CMake/MSVC 构建、单元测试、真实目录夹具。
3. 打包脚本：从现有 v1.0.13-r2 客户端目录生成新布局启动包，逐文件校验；stub 产物加入发行。
4. 发行：`v1.0.13-r3` 提交、说明、GitCode 包、GitHub stub 与说明、匿名回读。
5. 验收：固定安装副本迁移、新包安装、故障注入、公司电脑实机、EXE 属性。
6. 文档：发行说明 r3、决策/验收记录、本设计状态更新。

## 2026-11-17 追加：代码签名与 v1.0.14

用户要求 stub 和所有自有 EXE 都有完整代码签名和详细信息，作者改为 FireKula，用于降低杀软误报。设计与计划的调整：

- 签名改变 EXE 字节 → 固定入口/托管登录器/更新器/诊断/崩溃捕获的哈希变化 → 现有 v1.0.13-r2 清单、收据、描述符和入口迁移不再匹配；客户端必须重新构建为 **v1.0.14**。
- 发行身份从 `v1.0.13-r3`（纯打包）改为 **`v1.0.14`（签名客户端 + DFOLauncher 布局 + stub）**。
- 签名必须发生在清单/摘要/打包之前：构建 → 填 FireKula 元数据 → 签名 → `signtool verify /pa /all` → 计算哈希/清单 → 打包。
- 需要受信任的 OV/EV 代码签名证书和时间戳；自签名证书不能解决误报。证书/密码只从构建环境读取，不入仓库、日志或 URL。
- 签名范围（建议）：`DFOLauncher.exe`、固定入口、托管登录器、更新器、`DFLegacy.Diagnostics.exe`、`DFLegacy.CrashCapture.exe`；第二章重建游戏镜像不在我们的签名范围。
- 作者字段：`CompanyName` 和 `LegalCopyright` 改为 FireKula，`ProductName` 保持 DFLegacy；签名证书 subject 应与 FireKula 一致。
- 现有 v1.0.13-r2 玩家需要新的入口迁移/桥接对：签名后的非入口文件也变化，不能复用现有 entry-only 迁移；可能同时需要布局迁移（stub）。
- 分阶段执行：**Phase A**（stub + 布局迁移，不依赖证书）先做；**Phase B**（签名客户端 v1.0.14 + 新清单/迁移 + 发行）在证书、签名范围与内部命名决策确认后执行。
- 内部 2009 风格改名：原计划留到 v1.0.14；因签名已触发重建，可一并做，但需要固定命名方案并纳入新的迁移对，作为 Phase B 的显式任务。
- 已知限制：任何签名都不能保证杀软零误报；还需提交样本给厂商、保持二进制稳定、避免加壳/持久化。
