# 从这里接手 DFLegacy

这是给新开发者、开源模型和不同 agent 工具的交接入口。不需要历史聊天，也不要求安装特定 agent 插件。文档主要使用中文，代码符号保留原名。

## 先做这五步

1. 阅读 [AGENTS.md](AGENTS.md)，遵守界面、版本和交付规范。
2. 阅读 [当前基线与未完成项](knowledge/handbook/current-state.md)。先确定任务属于第一章 ACT1、第二章 Chapter Two，还是两章共用模块。
3. 阅读下表中与你的任务对应的一页；不要一次把所有历史文档塞入上下文。
4. 在仓库根目录执行下面的只读命令，确认位置、源码身份和现有改动。
5. 按 [agent 工作步骤](knowledge/handbook/agent-workflow.md)完成一个有边界的修改、验证和交接。

```powershell
Get-Location
git status --short
git log -5 --oneline
git rev-parse HEAD
dotnet --version
python --version
```

本项目的 SDK 要求见 [global.json](global.json)。缺少客户端、PVF 或 Windows C++ 工具链时，先做可以完成的代码阅读和独立测试，把缺失依赖写清楚。

可以给新 agent 直接发送下面这段话，并补上实际任务：

```text
请先读取仓库根目录 AGENTS.md 和 START-HERE.md，再读取与任务对应的一页手册。
我的任务是：<填写具体行为或问题>。
先确认章别、Git 状态、相关实现与测试入口，再完成修改和验证。
不要依赖历史聊天，不要编造缺失资源、测试结果或发布状态。
结束时给出改动、实际验证、未完成项和下一步；不执行未获授权的真实发布。
```

## 我需要做什么

| 任务 | 首先阅读 | 下一步定位 |
| --- | --- | --- |
| 不知道项目如何组成 | [目录与模块地图](knowledge/handbook/repository-map.md) | [完整手册目录](knowledge/handbook/README.md) |
| 修登录、组队、副本、交易、商城、存档 | [服务端架构](knowledge/handbook/server-architecture.md) | [协议与数据](knowledge/handbook/protocol-and-data.md) |
| 改登录器、更新器、诊断助手 | [桌面应用](knowledge/handbook/desktop-apps.md) | [配置说明](knowledge/handbook/configuration.md) |
| 把第一章功能/修复迁到第二章，或核对两章差异 | [章别适配](knowledge/handbook/chapter-adaptation.md) | [客户端与原生层](knowledge/handbook/client-and-native.md) |
| 做区域频道、城镇社交、假人玩家 | [区域频道与假人](knowledge/handbook/social-world-and-bots.md) | [服务端架构](knowledge/handbook/server-architecture.md) |
| 修原生客户端、渲染、输入、崩溃 | [客户端与原生层](knowledge/handbook/client-and-native.md) | [测试与证据](knowledge/handbook/testing-and-evidence.md) |
| 改 GM、玩家中心、部署管理页面 | [Web 与管理 API](knowledge/handbook/web-and-admin.md) | [ADMIN_API.md](ADMIN_API.md) |
| 改任务、商店、物品、贴图或 PVF | [资源与 PVF](knowledge/handbook/resources-and-pvf.md) | [工具索引](knowledge/handbook/tools-reference.md) |
| 维护 PVFStudio/NPKStudio 的解析、编辑和写回 | [资源子项目协作](knowledge/handbook/resource-subprojects.md) | 各独立仓库的 `START-HERE.md` 与格式手册 |
| 构建、跑测试、制作发行包 | [构建测试发布](knowledge/handbook/build-test-release.md) | [BUILDING.md](BUILDING.md) |
| 部署、升级、迁移、回滚、排障 | [运维手册](knowledge/handbook/operations.md) | [DEPLOYING.md](DEPLOYING.md) |
| 找旧实现原因、未解决问题 | [当前基线](knowledge/handbook/current-state.md) | [历史文档索引](knowledge/handbook/reference/documents.tsv) |
| 找一个不熟悉的文件 | [源码文件索引](knowledge/handbook/reference/files.tsv) | `rg --files src tools tests deploy protocol` |

## 必须理解的边界

- 源码、构建成功、自动化通过、真实客户端通过、发布成功、线上已部署是六件不同的事。报告中分别陈述。
- 原版客户端和游戏资源不是完整随源码提供的依赖。旧文档中的 `G:/...` 等路径是历史机器环境，不是你的机器上一定存在的路径。
- 第一章与第二章不能靠替换几个文件名互换。协议、原生地址、资源封装、启动流程和发布包都有章别。
- 数据目录、用户账号配置和发布凭据不是测试夹具。测试使用独立目录、独立监听端口和复制后的存档。
- 不要因为 `dotnet test` 返回零就认定跑过测试；本仓库多套 .NET 测试是可执行程序，按手册使用 `dotnet run --project ...`。
- 新窗口使用对应章节的原版 DNF 风格。第二章版本取自 [LauncherVersion.txt](src/DFLegacy.Launcher/LauncherVersion.txt)，发布的启动入口、登录器、更新器必须一致。

## 最小任务交接格式

```text
目标/章别：
开始源码提交与已有改动：
涉及文件和关键符号：
复现步骤与期望行为：
实际修改：
执行的验证命令、退出码、日志路径：
未验证项和原因：
运行中进程、测试数据、配置是否已恢复：
下一步（一个可直接执行的动作）：
```

只做普通开发时无需遍历所有历史报告。只有任务涉及某个旧结论时，才沿专题手册中的链接查看原始证据。
