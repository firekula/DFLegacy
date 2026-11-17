# DFLegacy 项目规范

## 接手与文档导航

新 agent 先读 `START-HERE.md` 和 `knowledge/handbook/current-state.md`，再按任务只读对应专题。完整目录在 `knowledge/handbook/README.md`；执行与交接步骤在 `knowledge/handbook/agent-workflow.md`。这些文件不依赖特定模型、插件或历史聊天。

涉及 PVFStudio、NPKStudio 或资源格式解析/修改时，先读 `knowledge/handbook/resource-subprojects.md`，再读取对应独立仓库的 `AGENTS.md`、`START-HERE.md` 和格式/写回手册；不要假设它们属于此仓库或使用相同的构建工具。

先确定第一章 ACT1、第二章 Chapter Two 或共用模块，记录当前 Git 提交与已有改动。历史报告中的成功只适用于记录的构建、环境和场景，不等于当前版本或线上实例已验证。不要覆盖已有工作、用户存档或账号配置。缺少外部客户端、PVF、符号或原生工具链时，明确记录缺失依赖并完成其余可验证工作。

修改代码时同步受影响的配置、操作、验收和限制文档。长期说明放在 `knowledge/handbook/`，具体决策与验收放在 `knowledge/decisions/`。新文档必须确认出现在 Git 中；`docs/` 默认被忽略，已有部分文件是显式跟踪的例外。测试命令以手册和工程入口为准，本项目多套 .NET 测试需要 `dotnet run --project ...`，不能仅凭 `dotnet test` 零退出码声称执行了测试。

## 桌面窗口

项目自有的新窗口、对话框和启动提示采用对应章节的原版 DNF 风格。按现有登录器的窗口边框、像素字体、金棕配色和按钮状态制作。WPF 窗口优先复用 `src/DFLegacy.Launcher/Views/ClassicStyles.xaml`、`ClassicDialogFrame`、`PixelText` 和 `SkinButton`。普通消息和确认框使用 `ClassicMessage`。原生启动提示以 `src/DFLegacy.Startup/startup_prompt.cpp` 为参考。

交付前按窗口实际内容验证布局、缩放和键盘焦点。检查标题、按钮、关闭操作及长文本在常用显示缩放下可见、可操作。

## 启动器版本

登录页从正在运行的托管程序获取版本，只显示发行号，例如 `v1.0.12`。Git 提交号保存在构建元数据里，便于核对源码。

第二章默认发行号读取 `src/DFLegacy.Launcher/LauncherVersion.txt`。发行时用同一版本号构建原生启动入口、托管登录器和更新器。每个发行 EXE 都要有与实际角色对应的文件说明、原始文件名、产品名、发布者、版权、文件版本和产品版本。在最终产物上检查 Windows 文件属性和实际启动。固定入口更新遵守现有安装清单的校验规则，入口哈希改变的版本走新启动包或已经验证的迁移流程。
