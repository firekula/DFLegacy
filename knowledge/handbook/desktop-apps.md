# 第二章弹窗

WPF 普通提示和原生启动失败提示共用 286×170 的石纹面板皮肤，确认按钮居中放在正文下方。WPF 保留完整正文并让长消息滚动。原生入口按所在显示器的 DPI 绘制预乘 Alpha 位图，确认按钮的命中区随之缩放。遇到皮肤加载、渲染失败或正文超出面板时，原生入口以 Windows 错误提示显示完整文案。

以 100%、125%、150% 核对透明圆角、完整边框和按钮命中区域。检查 Enter、Esc、Tab 和关闭后的焦点。WPF 与原生分别从最终产物核对面板及三个确认按钮状态图。

[窗口实现](../../src/DFLegacy.Launcher/Views/ClassicMessageWindow.xaml) · [原生实现](../../src/DFLegacy.Startup/startup_prompt.cpp) · [已验收的画面和边界](../decisions/2026-11-22-local-test-handoff.md)
