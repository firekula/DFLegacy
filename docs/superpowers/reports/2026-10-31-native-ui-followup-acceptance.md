# 防火墙处理后原生界面复测与最终交付

日期：2026-10-31。本次用户明确批准 NPKStudio 与 PVFStudio 同级、独立 IntelliJ 桌面框架及草稿/备份方案，并已手动处理 Windows 防火墙弹窗。原生鼠标与键盘测试现已继续；没有更改系统安全设置。本文更新 [上一轮验收](2026-10-31-launcher-startup-and-npk-studio-acceptance.md) 的物理 UI 待办，保留先前证据与历史。

## 客户端与注册入口

原始游戏旧壳导致的启动崩溃、缺少外部字符串表时的回退和真实启动观察修复见上一轮。此次实际点击及 Tab→Space 均能打开注册页面；发现 QQ 行拉伸和测试账号文字裁切，已在 `2540228ab89d85390ef4445bdcaf9ffee7e79bce` 修复为自动高度与独立行。最终单文件程序实测所有字段、复选框、提交按钮完整显示；没有填写或提交注册资料。

最终源通过完整 Launcher 测试。初次本地构建因 CMake 未在默认 PATH 上失败；明确配置已有 CMAKE_EXE 后，原生构建与完整默认测试均通过，没有修改测试门禁。已备份并只更新 `G:/dnf_local/DNF_2008/DFLegacy.Launcher.exe`，SHA256 `6274989179d79f45e86fae63288bd2a5cdf5f64e4cb10c8de94713e1f8f241b3`。本次五文件备份：`G:/dnf_local/DNF_2008/.dflegacy-backups/20261031-registration-ui-110238`，逐项摘要验证通过。

最终单文件启动器退出 0，实际游戏 PID23564（11:02:42 启动）在启动器退出后继续运行。原生窗口看到 `ch01新手－洛兰` / 畅通，频道连接建立后已到职业选择/创建角色页面。期间检测到用户输入，随后只观察最终界面，不把全部频道操作归为自动化执行。游戏窗口保留给用户验收；没有创建角色、进城或刷图，不声称完整玩法已验收。

原始 425 个 NPK + Script.pvf 共426项 SHA256 与本轮开始快照全部相同；DNF.exe、ijl15.dll、Config.ini、launcher.json 与本轮开始摘要相同。游戏 EXE 来自用户已有的兼容同版本副本，本轮未再次更换；原旧壳备份仍保留。没有游戏资产、截图、草稿或凭据进入仓库和发行包。

## NPKStudio 0.1.1

独立项目：`G:/Game/DNF_ACT1/NPKStudio`；源码 `84e11a84b86369a098e8fd46145e21111da38407` 已合并并推送 main。[下载 Windows x64 便携版](http://firekula.site:3000/firekula/NPKStudio/releases/tag/v0.1.1)，自带运行时，无需安装 IDEA/Java。

用户随后提供 GitHub 仓库，已新增 `github` 远程并原子推送 [firekula/NPKStudio](https://github.com/firekula/NPKStudio) 的main与v0.1.0/v0.1.1标签；两远端引用逐项核对一致。原Gitea origin及main上游保持；GitHub本次接收源码和标签，便携版附件发布在上面的Gitea Release。

- ZIP：`NPKStudio-0.1.1-windows-x64-portable.zip`，243203292 字节。
- SHA256：`3c398f1c51d8eb3acbd88d138b74f91eb7338770a05b194d8e635380909ec06c`。
- 48 / 48 测试通过（核心35、产品13），14个JUnit文件，零失败、错误、跳过。425真实原包逐字节导出一致；7个修改输出独立解析通过。
- 原生鼠标/物理按键实测：完整库文件树及预览、换帧、XY修改、撤销/重做、Ctrl+S草稿、PNG文件选择器导入/导出、自动备份NPK导出与独立回读、Ctrl+P搜索、标签切换/关闭/重开、Ctrl+F跳帧、Ctrl+K S及Ctrl+K Ctrl+W多段按键。
- 新包恢复两标签、选定帧及草稿像素/偏移；最大化/还原通过。PNG输出与2×2RGBA输入83字节完全相同，JSON几何属性准确；实际生成NPK由独立解析器证明只修改指定帧，其余帧、不透明数据保持原状。
- 修复命令面板缩放受残留文本焦点阻止：失败回归后修复，新包经面板从400%切到100%；输入框中的物理Ctrl+1仍不会抢走文本上下文，显式100%按钮正常。
- 默认PNG导出改为Ctrl+Alt+Shift+P，避开本机QQ全局Ctrl+Alt+E；新配置实测有效，Keymap编辑应用通过。已有用户键位保持，手动改绑见项目键位文档；不保证任意第三方全局热键均无冲突。

负责人核对最终ZIP CRC、版本/许可/产品身份、无游戏资源、测试过的class字节与最终包一致（重链接原生EXE只有时间戳/校验和改变），并独立从远端重新下载两附件核对SHA256。测试实例已正常关闭，用户游戏保留。完整当前记录见 NPKStudio `docs/validation.md` 顶部。

## CI与发布

[Gitea run13](http://firekula.site:3000/firekula/DFLegacy/actions/runs/13) 对应 `2540228ab89d85390ef4445bdcaf9ffee7e79bce`，4/4工作全部成功：Windows/Linux测试、跨平台打包与双架构镜像、自动发布。Windows全量705测试+592子用例（JUnit1297记录）零失败/错误/跳过；native13+1、Manager100、release46及Docker17检查通过。

[服务端与客户端最新发行](http://firekula.site:3000/firekula/DFLegacy/releases/tag/dev-20261031T023453Z-2540228ab89d)：10个远端附件独立认证下载，SHA256、五RID包内容与Unix执行权限复核通过。Windows/Linux x64有实际运行证据；Linux ARM64/macOS x64及ARM64只有交叉构建与包验证，没有真机运行声明。

私有镜像 `firekula.site:3000/dflegacy-ci/dflegacy@sha256:1bf95c9e4ea13b74946c225309b1a0323a18623b681cac7e3852d0e816ab7b63` 的amd64/arm64索引、子清单、配置摘要、源码标签与发布前OCI记录一致；匿名访问被拒绝。完整证据在 `D:/DFLegacyCI/validation/run-13` 及本工作树 `artifacts/launcher-startup-release/published/dev-20261031T023453Z-2540228ab89d`。

独立只读评审：没有Critical/Important遗留，Minor的NOTICE版本已修正。评审未判断项由负责人裁定并记录：保留自定义键位、只证明本机热键情况；注册不提交；旧报告保留并补充当前证据；完整主题/高对比度/DPI组合与更深玩法仍未穷举。IMG v1编辑/v2只读；修改包尚未加载进游戏测试，NPKStudio本轮仅Windows x64，其他原子写入和格式边界见项目验证记录。

两个Runner在远端验证后恢复暂停，注册、缓存和数据卷保留。用户原管理器、服务端和当前游戏不停止。main既有3个已跟踪文件修改及163条状态记录逐字节保护。本文另作[skip ci]文档提交，不改变已验证的产品代码。

私有本轮证据：`D:/DFLegacyCI/validation/ui-followup-20261031/`，含原生UI、PNG/NPK交叉验证、48项JUnit汇总、原包摘要、最终启动进程和远端下载验证记录。
