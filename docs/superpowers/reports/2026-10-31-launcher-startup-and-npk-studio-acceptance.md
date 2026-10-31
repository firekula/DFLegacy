# 客户端启动修复与 NPK Studio 验收记录

> 后续：用户已处理防火墙弹窗，原生UI实测、新版0.1.1与CI13结果见 [最终复测报告](2026-10-31-native-ui-followup-acceptance.md)。以下保留上一轮历史记录。

日期：2026-10-31。代码提交 `4349634be904ac43c5c067e1568e23fe2565ee80`。用户授权分析本机客户端、独立测试、修复后提交推送，并要求独立 NPK 编辑器项目。

## 实际客户端问题与修复

用户目录 `G:/dnf_local/DNF_2008` 的原始 DNF.exe 是旧壳程序，启动后发生 `0xC0000005`，尚未加载 ijl15。私有 Win32 调试记录显示其自解包代码读取当前 Windows kernel32 映像未提交的 RVA 0x3240；系统 .text 已从 RVA 0x10000 开始。这解释了原登录器记录 Process.Start 成功但看不到游戏的现象。

原 EXE SHA256 `35911F29C62197840403A60CF14B10D03FC608A6E782535F54F97448ED4A8D49`。只在验证备份后，使用用户本机已有的同版本展开 EXE（来自 `G:/Game/DNF_ACT1/Loot4_A/DNF.exe`，SHA256 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`）修复本地目录；该游戏 EXE 没有提交或进入 Release。

同资源、兼容 EXE 的第二个阻塞是缺少 `Plugin/dnf.str`。旧补丁强制读取外部字符串表。现在仅在文件确实不存在时调用原游戏内置提取器；有效外置 CP936 文件继续优先，已有空文件、损坏、编码错误或不可读文件仍报错，避免掩盖运营配置问题。

登录器增加已知不兼容旧壳的 PE 结构预检、真实子进程五秒存活观察及早退错误码。进程创建成功不再被当作启动成功；取消或失败回收本次拥有的游戏进程，成功且无需中继时保留游戏运行。GUI 失败写入 launcher.log，并增加可见且可聚焦的“注册账号”按钮，复用已有注册对话框。最终程序的真实 UI Automation 树确认 RegistrationButton、说明及可用账号服务状态；截图仍被系统防火墙弹窗遮挡，未把结构检查当作人工点击通过。

## 本机最终验证

- 最终复核单文件登录器发布成功，并部署到实际客户端目录。真实启动器退出码 0，创建的游戏窗口标题为“地下城与勇士”，游戏在启动器退出后仍运行。测试结束仅终止本次拥有的游戏进程。
- 先在隔离副本以原生调试确认到达频道选择，再在用户实际目录验证游戏窗口。没有把“可拉起”夸大为角色创建、进入城镇或完整副本验收。
- 原始 425 个 NPK 与 Script.pvf 共 426 项 SHA256 全部保持一致；Config.ini 和 launcher.json 与备份一致。
- 原 EXE、登录器、ijl15.dll、配置的已验证备份：`G:/dnf_local/DNF_2008/.dflegacy-backups/20261031-startup-recovery`，包含逐文件 SHA256.json。
- 用户正在运行的管理器和服务端未停止或替换。
- 当前8081账号服务实测接受既有playtest账号：HTTP200、success=true、普通玩家role0。令牌不写入记录，不把headless启动误称为GUI认证通过。
- 原版旧壳的预检失败、真实早退 0/非零、持续运行、取消、成功移交及非成功回收都有真实子进程测试；完整 Launcher 测试和本机原生 13/13 已通过。Unicode TEMP 缺陷先复现，再修复后通过。

最终本机登录器 SHA256：`F568C78887737F9A0210D234072E4794938F66E00798C26AB8D31749C9456DF1`；补丁 DLL SHA256：`23E1A18C0B873D4949CFAAA711B1C8C07F641B323B6EF366E04A68B41E85C67C`。私有证据：`D:/DFLegacyCI/validation/launcher-startup-20261031`，包括 reviewed-final-game-verification.json、原生崩溃追踪、RED/GREEN 日志和426项原始摘要。

## 独立 NPK Studio

独立项目：`G:/Game/DNF_ACT1/NPKStudio`。私有仓库：[firekula/NPKStudio](http://firekula.site:3000/firekula/NPKStudio)；[v0.1.0 便携预览版](http://firekula.site:3000/firekula/NPKStudio/releases/tag/v0.1.0)。发布源码 a9e2812，产品代码 c58f89f；之后仅更新验收文档。

使用 IntelliJ Community 平台独立产品壳，参考现有 PVFStudio；便携包自带 Java，无需安装 IDEA。提供 ImagePacks2 全库懒加载文件树、IMG/帧预览、缩放/原点/动画、PNG 导入与导出、XY/画布修改、撤销重做、草稿恢复、带自动备份的 NPK 导出。采用固定版本完整 VSCode 键位与平台 Keymap 路由，可自行重新绑定；不是仅设置若干 Swing 快捷键。

47项测试（核心35、产品12）通过，0跳过。实际425包、12087IMG、1485268帧、2删除槽：全部425个无修改导出与原字节一致，7个修改输出经独立解析核对。解压后的原生 EXE 实际完成控件修改、PNG导入/导出、平台撤销重做/保存、双标签延迟恢复及关联帧警告取消；Ctrl+S经真实平台事件分发证明恰好执行一次。

便携 ZIP 为243202227字节，SHA256 `5e8a8062bc22cf06bda492535afa19b0f6add16b0f41afe664222b8e46b08db7`。负责人独立核对JUnit、ZIP CRC、运行入口、legal、无游戏资源，并从远端独立认证下载两个附件核对字节与摘要。完整记录见该项目 docs/release-acceptance.md 与 docs/validation.md。游戏素材、截图和修改样本都只留在私有验证目录。

## 复核决定与边界

- 独立架构由构建者依据已有跨夜实施授权选择；没有把异步偏好问题的沉默当作批准。后续若改架构需要对应返工。
- 本次 NPK 支持实际库的 IMG v1 编辑，v2只读，未知内容保留；非Windows、现代格式写入、安装器、自动更新和真实游戏加载修改包不列入已验证能力。
- 本地 Windows 原子提交和常见竞争失败路径已测；最终摘要检查到重命名不是跨进程 CAS，断电目录持久性和网络文件系统没有保证。不支持所需原子操作时拒绝写入。
- 当前 Windows 安全中心防火墙授权弹窗需要用户自行处理；没有自动允许、取消、关闭 OS 安全界面。物理键盘/鼠标注册流程、全部多段快捷键、显示器DPI与更深游戏流程保留人工验收。内部平台事件/控件测试不能替代这些项目。
- 一次独立整支复核后修复所有重要问题：启动器取消所有权和Unicode路径；NPK六项跨文件覆盖、陈旧选择、当前键位路由、延迟会话恢复、锁与IO隔离、关联帧影响提示。均有失败再通过证据，没有把问题降级延期。

## CI 与最终发布

实际 [Gitea CI run12](http://firekula.site:3000/firekula/DFLegacy/actions/runs/12) 对应产品源码 `4349634be904ac43c5c067e1568e23fe2565ee80`，4/4个工作通过。Windows全量705测试+592子用例（JUnit1297记录），0失败/错误/跳过；原生13+1通过；管理器100、发行工具46、登录器及两端协议烟测通过；Docker17项运行检查通过。

首轮在Docker17通过后，双架构OCI导出访问Docker Hub认证端点发生EOF，自动发布被阻止。确认端点恢复HTTP200后，只重跑同一提交的失败工作，保留已通过的Windows/Linux结果；没有修改产品或削弱门禁。首轮失败与重跑日志均保存。

[最新服务端与登录器发行版](http://firekula.site:3000/firekula/DFLegacy/releases/tag/dev-20261030T190825Z-4349634be904)：`dev-20261030T190825Z-4349634be904`。10个远端附件经过独立认证下载，依清单核对SHA256、五平台包内容及Unix执行权限。Windows/Linux x64有实际运行证据；Linux ARM64、macOS x64/ARM64为交叉构建与包内容校验，不冒称对应平台真机已测。

私有镜像 `firekula.site:3000/dflegacy-ci/dflegacy@sha256:fa39ad6196e6d7c486efc358f843461091e28b706db120c6d2d4fc0414b61352` 的amd64/arm64索引、子清单、配置摘要和源码标签逐个核对，与发布前OCI摘要一致；容器非root运行，匿名拉取被拒绝。私有日志、JUnit及OCI元数据：`D:/DFLegacyCI/validation/run-12`；远端附件与validation-summary.json：当前工作树 `artifacts/launcher-startup-release/published/dev-20261030T190825Z-4349634be904`。

两个Runner恢复测试前的暂停状态，保留注册、缓存和数据卷；用户原管理器与游戏服务不停止。main原3个已跟踪文件的未提交内容及163条状态记录逐字节保护。验收文档另作 `[skip ci]` 提交，不改变已经验证的产品代码。
