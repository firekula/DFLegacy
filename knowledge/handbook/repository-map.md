# 目录与模块地图

用途：由需求快速找到负责代码，避免只修改最显眼的入口。先读 [当前基线](current-state.md)；文件级检索使用 [files.tsv](reference/files.tsv)。下表覆盖本轮基线的全部 19 个 `src` 子目录。

同级独立仓库 PVFStudio、NPKStudio 及本机 pvf-dump 不包含在这19个模块中；解析、编辑、写回与协作入口见 [资源子项目](resource-subprojects.md)。

## 模块分工

| 目录（均在 `src/`） | 责任 | 阅读手册 |
| --- | --- | --- |
| [DFLegacy.Server](../../src/DFLegacy.Server) | ASP.NET 主进程、游戏 TCP/UDP、玩法、PVF、存档、账号和 Web API | [服务端](server-architecture.md) |
| [DFLegacy.Protocol](../../src/DFLegacy.Protocol) | 客户端协议帧、请求、响应、通知、二进制字段 | [协议与数据](protocol-and-data.md) |
| [DFLegacy.Channel](../../src/DFLegacy.Channel) | 可选独立入口握手；接收 Server 主动连接后的注册并交换状态/通知，不访问游戏存档 | [服务端](server-architecture.md) |
| [DFLegacy.InterProcess](../../src/DFLegacy.InterProcess) | 进程间消息和链路基础 | [服务端](server-architecture.md) |
| [DFLegacy.Manager](../../src/DFLegacy.Manager) | 独立部署管理器、配置、服务进程、资源和客户端包导出 | [Web](web-and-admin.md)、[运维](operations.md) |
| [DFLegacy.AccountClient](../../src/DFLegacy.AccountClient) | 桌面程序调用账号 HTTP API | [桌面应用](desktop-apps.md) |
| [DFLegacy.Launcher](../../src/DFLegacy.Launcher) | WPF 登录器、设置、账号、章节启动、升级和修复 | [桌面应用](desktop-apps.md) |
| [DFLegacy.Startup](../../src/DFLegacy.Startup) | 无托管运行时前置的原生固定入口和启动提示 | [桌面应用](desktop-apps.md) |
| [DFLegacy.UpdateCore](../../src/DFLegacy.UpdateCore) | 更新清单、校验、事务、恢复等共用逻辑 | [桌面应用](desktop-apps.md) |
| [DFLegacy.ClientPackager](../../src/DFLegacy.ClientPackager) | 独立客户端包处理工具 | [构建发布](build-test-release.md) |
| [DFLegacy.Diagnostics.Core](../../src/DFLegacy.Diagnostics.Core) | 诊断模型、队列、设置和报告处理 | [桌面应用](desktop-apps.md) |
| [DFLegacy.Diagnostics](../../src/DFLegacy.Diagnostics) | 独立诊断助手入口 | [桌面应用](desktop-apps.md) |
| [DFLegacy.Relay.Core](../../src/DFLegacy.Relay.Core) | 可嵌入的本地 TCP/UDP IPv4/IPv6 中继 | [桌面应用](desktop-apps.md) |
| [DFLegacy.Relay](../../src/DFLegacy.Relay) | 独立中继命令行程序 | [配置](configuration.md) |
| [DFLegacy.Client](../../src/DFLegacy.Client) | 第一章独立 x86 补丁 DLL、bootstrap、玩家运行配置 | [原生层](client-and-native.md) |
| [DFLegacy.Ijl15](../../src/DFLegacy.Ijl15) | 第一章兼容补丁核心、原生钩子、旧 IJL 载入链及测试 | [原生层](client-and-native.md) |
| [DFLegacy.Debug](../../src/DFLegacy.Debug) | 可选 debug.dll、状态导出、浮动面板、隔离测试控制 | [原生层](client-and-native.md)、[测试](testing-and-evidence.md) |
| [DFLegacy.ChapterTwo.Native](../../src/DFLegacy.ChapterTwo.Native) | 第二章启动代理、显示宿主、原生补丁和测试控制 | [原生层](client-and-native.md) |
| [DFLegacy.RandomNative](../../src/DFLegacy.RandomNative) | 可选硬件随机种子桥接，有托管回退 | [原生层](client-and-native.md) |

`DFLegacy.Launcher` 内的更新器角色不一定对应独立同名源码项目。不要因为发行物叫某个 EXE，就假设存在同名 `.csproj`。解决方案也没有囊括所有独立工具和第二章测试；完整构建矩阵见 [构建手册](build-test-release.md)。

## 顶层目录

| 路径 | 用途/维护边界 |
| --- | --- |
| [tests](../../tests) | 托管测试、浏览器逻辑测试、回归夹具；另有测试分布在各原生模块及 tools 内 |
| [tools](../../tools) | 资源、构建、发布、运行、调试和验收脚本；不是所有脚本都只读 |
| [deploy](../../deploy) | 通用部署模板、启动脚本、第二章本地/远端部署、Runner 示例 |
| [protocol](../../protocol) | 协议证据地图、TSV 台账、生成器及只读自检，与 Protocol 代码库职责不同 |
| [knowledge/decisions](../decisions) | 有日期的技术决策、验收结论和部分原始附件；保留历史口径 |
| [knowledge/handbook](README.md) | 本套长期维护手册和检索表 |
| [docs](../../docs) | 历史设计、计划、发布和验收；忽略规则下有部分显式跟踪的文件 |
| [third_party](../../third_party) | 外部代码/二进制及来源许可证，不能当本项目自有代码统一授权 |
| [.gitea](../../.gitea) | Gitea 源码测试工作流；文件名 release.yaml 不表示会发布 |
| `.superpowers/`、`.recon/`、`.tmp/` | 本机工作记录、研究或临时产物，默认忽略，不是 clone 的必备输入 |
| `dist/`、`artifacts/`、`backups/` | 构建、验收和备份输出；默认忽略，不用作唯一长期文档位置 |
| `DNF2008/`、`client-assets/`、`data/` | 游戏/资源/运行数据可能存在于本机；不要假设 clone 已具备，也不要覆盖用户内容 |

## 顶层配置和历史文档

- [global.json](../../global.json)：.NET SDK 选择。
- [DFLegacy.Emulator.slnx](../../DFLegacy.Emulator.slnx)：混合 C#/C++ 解决方案，不能假设普通 `dotnet build` 能构建所有原生工程。
- [server.json](../../server.json)、[launcher.json](../../launcher.json)：仓库配置实例。新部署优先阅读 `deploy/templates` 或章别模板；不要把本机实例内容当公共默认值复制。
- [Dockerfile](../../Dockerfile)、[compose.yaml](../../compose.yaml)、[.env.example](../../.env.example)：通用容器入口；第二章有独立目录。
- [pytest.ini](../../pytest.ini)：测试发现排除生成目录的规则。
- [BUILDING](../../BUILDING.md)、[DEPLOYING](../../DEPLOYING.md)、[ADMIN_API](../../ADMIN_API.md)、[CLIENT_UPDATES_V4](../../CLIENT_UPDATES_V4.md)：详细既有手册，配合本套手册里的日期/范围说明阅读。
- `HANDOFF-*`、`HOTFIX-*`、`STATUS.md`、`CHANGELOG.md`：时间切片；新结论可以补充旧结论，但不要悄悄改写历史验收结果。

## 第三方和资源来源

项目自有代码的根许可证为 [0BSD](../../LICENSE)。以下内容各自保留许可证和来源：

| 内容 | 来源记录 |
| --- | --- |
| Detours | [PROVENANCE](../../third_party/detours/PROVENANCE.md)、[LICENSE](../../third_party/detours/LICENSE) |
| xBRZ 1.9 | [PROVENANCE](../../third_party/xbrz/1.9/PROVENANCE.md)、[License](../../third_party/xbrz/1.9/License.txt) |
| d3d8to9 | [说明](../../third_party/d3d8to9/v1.15.1/README.md)、[许可证](../../third_party/d3d8to9/v1.15.1/LICENSE.md) |
| dgVoodoo | [NOTICE](../../third_party/dgvoodoo/NOTICE.md) |
| 第二章 NTSC 上游 | [PROVENANCE](../../src/DFLegacy.ChapterTwo.Native/ntsc-upstream/PROVENANCE.md) |

原版游戏、PVF、NPK 的权利归各自权利人；源码许可不等于游戏资源许可。构建和打包时保留现有通知文件，发布检查见 [工具手册](tools-reference.md)。
