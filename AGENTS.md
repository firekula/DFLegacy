# DFLegacy 项目规范

## 桌面窗口

项目自有的新窗口、对话框和启动提示采用对应章节的原版 DNF 风格。按现有登录器的窗口边框、像素字体、金棕配色和按钮状态制作。WPF 窗口优先复用 `src/DFLegacy.Launcher/Views/ClassicStyles.xaml`、`ClassicDialogFrame`、`PixelText` 和 `SkinButton`。普通消息和确认框使用 `ClassicMessage`。原生启动提示以 `src/DFLegacy.Startup/startup_prompt.cpp` 为参考。

交付前按窗口实际内容验证布局、缩放和键盘焦点。检查标题、按钮、关闭操作及长文本在常用显示缩放下可见、可操作。

## 启动器版本

登录页从正在运行的托管程序获取版本，只显示发行号，例如 `v1.0.12`。Git 提交号保存在构建元数据里，便于核对源码。

第二章默认发行号读取 `src/DFLegacy.Launcher/LauncherVersion.txt`。发行时用同一版本号构建原生启动入口、托管登录器和更新器。每个发行 EXE 都要有与实际角色对应的文件说明、原始文件名、产品名、发布者、版权、文件版本和产品版本。在最终产物上检查 Windows 文件属性和实际启动。固定入口更新遵守现有安装清单的校验规则，入口哈希改变的版本走新启动包或已经验证的迁移流程。
