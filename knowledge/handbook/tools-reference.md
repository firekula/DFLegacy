# 工具与脚本导航

用途：回答“哪个脚本负责这件事、能不能直接运行”。全部受跟踪路径见 [files.tsv](reference/files.tsv)，参数以脚本的 `argparse`、PowerShell `param` 或 README 为准。不要对未知脚本一律运行 `--help`：没有参数解析的脚本可能直接执行工作。

## 发布和制包：`tools/release`

| 文件/组 | 职责 | 运行影响 |
| --- | --- | --- |
| [release.py](../../tools/release/release.py) | 测试、托管发布、客户端资源、跨平台发行包与验证 | 根据子命令运行构建、生成文件 |
| [ci.py](../../tools/release/ci.py) | 可复用 CI 驱动、构建身份和打包 smoke | 会构建，部分模式启动临时服务 |
| [update_v4.py](../../tools/release/update_v4.py) | V4 清单、逐文件包、更新源上传与验证 | 部分子命令会访问或写远端发布源 |
| [chapter_package.py](../../tools/release/chapter_package.py) | 第二章完整包及专用清单 | 写输出包；严格验证章别和资源 |
| [bootstrap_package.py](../../tools/release/bootstrap_package.py)、[bundle.py](../../tools/release/bundle.py) | 固定启动包/打包底层实现 | 生成或读取发行物 |
| [github_publish.py](../../tools/release/github_publish.py) | GitHub 发布辅助 | 可修改公开发行资源，需按任务授权使用 |
| [retain_native_symbols.py](../../tools/release/retain_native_symbols.py) | 按构建身份保存原生符号 | 写私有符号归档；不把私有符号顺带公开 |
| [native_fixtures.py](../../tools/release/native_fixtures.py) | 为发行测试准备原生夹具 | 生成测试资源，不是正式客户端 |
| [Enable-Gitea-Test.ps1](../../tools/release/Enable-Gitea-Test.ps1)、[Enable-Gitee-Test.ps1](../../tools/release/Enable-Gitee-Test.ps1) | 测试源配置切换 | 修改目标配置，先确认目标目录 |
| [tests](../../tools/release/tests) | 发布规则回归和包探针 | 有的探针依赖 dotnet/原生产物；不能只算 Python 项数 |

具体运行顺序、固定入口更新限制和版本策略见 [构建发布](build-test-release.md)。当前 Gitea 工作流仅跑源码测试，不能将“CI绿”当作客户端或镜像已经发布。

## 自动化验收：`tools/acceptance`

| 文件/组 | 用途 |
| --- | --- |
| [README](../../tools/acceptance/README.md)、[RUNBOOK](../../tools/acceptance/RUNBOOK.md) | 完整运行约定；操作前先读 |
| `cli.py`、`orchestrate.py`、`runner.py` | 场景驱动、进程编排和结果收集；可能启动/停止服务和客户端 |
| `model.py`、`collector.py`、`verdict.py` | run 模型、证据采集和判定 |
| `scenarios/`、`verdicts/` | 场景声明和受跟踪结论；占位场景不代表已完成业务验证 |
| `actor_pid.py`、`window_layout.py` | 参与客户端身份和窗口布局 |
| `auto_input.py`、`native_control.py`、`combat_gate.py` | 原生输入计划、控制和战斗门控，不能当只读观察器 |
| `state_seed.py`、`town_fixture.py`、`party_dungeon.py`、`inventory_tab_fixture.py` | 隔离测试前置状态和场景；可能修改测试存档/角色 |
| `act1_gameplay_evidence.py`、`act1_stream_timing.py`、`party_fallback_evidence.py` | 第一章网络证据、流时序与回退链检查 |
| `motion_replay_fixture.py`、`native_fixtures.py` | 回放/原生夹具；回放不是当次真机 |
| `audit_chapter_transition.py`、`test_chapter_two_profile_routing.py` | 章节迁移和路由审计 |
| `ChapterTwo*Fixture/`、`ChapterTwoMigrationProbe/` | 第二章 .NET 隔离数据构造/迁移探针；先读 Program 参数与写入目标 |
| `Install-ChapterTwoShopCandidate.py` | 安装候选资源，属于写入操作 |
| `Build-ChapterTwo*Report.py` | 从验收数据生成报告；不能生成不存在的实测结果 |
| `Test-NormalServerStartup.ps1` | 正常服务启动流程检查；先读目标路径和副作用 |
| `tests/`、`chapter_two_*_test.py` | 编排、模型、ABI、原生夹具和章别策略的测试 |

## 调试：`tools/debug`

入口手册是 [tools/debug/README](../../tools/debug/README.md)。先区分只读状态、输入控制和测试专用服务端操作。

| 文件/组 | 用途 |
| --- | --- |
| `client_state.py` | 解析/展示原生结构化状态，处理版本及新鲜度 |
| `client_route.py`、`client_native_route.py`、`client_navigation.py`、`town_navigation.py` | 导航状态、路线与原生移动接口 |
| `client_party.py` | 组队控制/观察接口 |
| `drop_control.py`、`test_control.py`、`chapter_two_test_control.py` | 隔离测试操作；检查租约、角色和章别前置条件 |
| `gameplay_trace.py`、`motion_test_trace.py`、`provider_trace.py` | 对应探针事件解码 |
| `frame_profile.py`、`native_loop_profile.py` | 帧/回调时序分析；不能凭局部耗时判断全游戏不卡顿 |
| `watch_dungeon.py`、`verify_pvp_channels.py` | 副本观察与决斗频道核查 |
| `test_*.py` | 大部分为对应模块回归；`test_control.py` 本身是控制工具，不要仅凭前缀判断 |
| [crash_capture](../../tools/debug/crash_capture/README.md) | Windows 崩溃捕获、本地转储、符号/报告辅助；可能附加进程 |

不能将测试控制注入得到的状态写成自然玩家操作已成功；尤其是走门、掉落、死亡、复活、换房等验收要记录是否使用夹具。

## 资源、运行及其他

- `tools/pvf/`：完整说明见 [资源与 PVF](resources-and-pvf.md)。Java 工具需要外部 PVFStudio 类，第二章 Python 导出有固定输入守卫。
- PVFStudio/NPKStudio 是同级独立仓库，解析、编辑、写回 API 与完整手册见 [资源子项目协作](resource-subprojects.md)；本机 `pvf-dump` 的限制见 [专门说明](pvf-dump.md)。
- [tools/runtime/Start-NormalServer.ps1](../../tools/runtime/Start-NormalServer.ps1) 及 `.cmd`：运行实例启动器，先确认配置和存档位置；不是无副作用的环境检查。
- [tools/patch-client-pay-auth.ps1](../../tools/patch-client-pay-auth.ps1)：客户端充值认证相关修补，先读实际匹配/写入规则。
- [tools/key-magenta.ps1](../../tools/key-magenta.ps1)：图像品红色键处理；对副本输出使用。
- [tools/build_classic_launcher_assets.py](../../tools/build_classic_launcher_assets.py)：登录器资产生成。
- `deploy/chapter-two/`：专用启动、构建和网络桥，读 [第二章部署](../../deploy/chapter-two/README.md) 及 [运维手册](operations.md)。`local_bridge.py` 和 `Build-LocalChapterChannel.py` 属运行链而非文档生成。
- `protocol/s1.py`：协议证据 import/ledger/render/verify。只有 verify 是正式只读自检；import 依赖外部历史输入，ledger 除 run 证据外还需要 `--raw-dir` 下原始 `tiers-external.tsv`，render 会写受跟踪台账产物。缺原始输入时先做 verify，不伪造外部证据。

## 新增一个脚本时的交付要求

写明运行目录、依赖、参数、输入格式/版本、输出路径、是否联网/写数据/启停进程、失败退出码和一条可复现验证命令。默认避免覆盖输入；需要覆盖时说明备份与恢复办法。将脚本加入本页对应分组并刷新文件索引。
