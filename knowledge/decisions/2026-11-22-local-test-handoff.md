# 2026-11-22 本地测试客户端与未完成项交接

v1.0.18 本地副本已通过进入游戏、六个限时称号到期消失、仓库转移与城镇装扮销毁的真机验收。仓库和装扮结果见第 2.3 至 2.6 节。正式 r3 Updater 报错、邮件和离线回收仍缺少本轮真机验证。后续重复测试按[第二章本地真机测试流程](../handbook/chapter-two-local-qa.md)准备角色、地图和夹具，下文保留当时的排查过程。

## 继续接手记录（2026-11-22）

- 用户确认限时称号到期测试通过，范围为本次本地测试；不要据此扩大为其他位置/场景全部验收通过。
- 新建 v1.0.18 测试副本 `G:\DFLegacy-v1.0.18-local-qa-20261122`，从原 `.tmp/v1018-release` 用 `robocopy /COPY:DAT` 复制，未复制旧 Logs、根运行缓存和任务脚本，保留 `client/.dflegacy/v4/installed.json` 及 `validated.json`；目录和固定入口没有低完整性标签。两份测试配置改用 `ChapterTwoServerIpv4=192.168.3.177`、禁用联网更新；当前真实有线地址是 `192.168.3.177`，原 `192.168.203.101` 已不在本机。QA 服务配置的两个对外地址已同步，原配置备份 `.tmp/chapter-two-local-qa/server.before-ip-refresh-20261122-125925.json`，服务 PID 18076 入口和频道已监听；之前测试存档备份 `.tmp/chapter-two-local-qa/state.before-v1018-qa-20261122-125845.json`。这些操作未执行客户端或游戏测试，副本启动/登录、升级器校验和实际输入仍待用户验收。先前测试副本仍配置旧 IP，需要再使用时自行同步配置。
- 只读核对该现成 v1.0.18 测试包文件属性：外层入口、App、Updater 为 1.0.18，但固定入口 `client/DFLegacy.Launcher.exe` 为 1.0.14；不满足当前统一发行版本规范。保留原固定入口字节与安装校验，不能将该副本称为符合发行规范的最终包。需要新启动包或经过验证的迁移流程处理，未直接替换入口。
- IPv4 直连后用户仍卡频道：服务日志明确记录客户端到达 `7100`，但 `RX-ENC` 的第一章频道解码无法识别第二章握手。隔离服务绑定 LAN 地址，既不满足仅限双端 loopback 的试验密钥条件，也未设置 `ChapterTwoRemote`。已备份配置至 `.tmp/chapter-two-local-qa/server.before-chapter-two-remote-20261122.json`，在 QA 配置启用 `ChapterTwoRemote=true` 并重启服务 PID 47668；启动日志 `.tmp/chapter-two-local-qa/chapter-two-remote-20261122-025323.out.log`，入口和频道监听就绪。尚未由用户重新登录验证；重启会断开先前卡住的会话。六个测试称号仍在存档，原定 02:58:35 至 03:03:35 到期。
- 用户进入频道选择后连接卡住：入口 42312 请求已通过，但没有观察到 47001 转发连接。测试服下发频道 IP 为 `192.168.203.101`，旧测试宿主的本地 Relay 模式仅接受 `127.0.0.1/127.0.0.2` 作为请求地址，因此与该配置不一致；这是基于源码与现有日志的原因推断，尚待真机确认。将独立副本 `G:\DFLegacy-local-qa-20261122-medium\launcher.chapter2.json` 改为 `ChapterTwoServerIpv4=192.168.203.101`（已有旧版 IPv4 直连路径），移除 `ChapterTwoServerHost`；原配置备份在同目录 `launcher.chapter2.before-ipv4-20261122.json`。需关闭当前游戏和登录器后重新启动，保留服务和存档。未执行用户测试。
- 用户要求重设后，停止隔离测试服检查发现六个测试称号已回收并写入墓碑；我此前启动服务前未先重设，未能保护原测试实例。已备份当前存档至 `.tmp/chapter-two-local-qa/state.before-title-reset-20261122-024835.json`，在原测试槽位重新创建六个新实例，保留旧墓碑和永久对照。首次至最后到期时间分别为本地 02:58:35 至 03:03:35，每隔一分钟；它们从设置时开始计时，不随登录延迟。测试服 PID 41852 已重新启动，日志 `.tmp/chapter-two-local-qa/title-reset-server-20261122-024843.out.log`，入口与频道已监听。
- 用户启动新副本后截图显示 `192.168.203.101` 无可连接入口；当时测试服未运行。已按本交接配置启动隔离测试服 PID 52716，日志 `.tmp/chapter-two-local-qa/manual-server-20261122-024626.out.log` / `.err.log`，启动日志与监听表确认 `0.0.0.0:42312`、`47001`、`48090` 和 Admin `48091` 已监听。未代用户重试客户端，截图发生在启动游戏前，不能据此认定 DirectInput 已通过。测试存档的限时称号到期时间未重设，登录可能立即结算并移除，见第 4 节。
- 用户第三版探针确认进程完整性 RID `0x1000`（低），可读但不能创建 DirectInput 注册表子项，`CoCreateInstance` 返回 `0x80004005`。只读检查项目根和 `.tmp` 的文件/目录发现继承 Low Mandatory Level；旧测试客户端、1.0.18 副本以及在其中创建的宿主均受此环境影响。Explorer、用户 Windows Terminal 和 PowerShell 的进程完整性是 `0x2000`（中）。探针低完整性能解释观察到的注册表访问差异，但尚需用户确认独立副本及游戏启动结果。
- 对原 `.tmp` 测试客户端目录和探针尝试设置中完整性时均收到拒绝访问，未改变其权限。另用 `robocopy /COPY:DAT` 复制旧 `client-a` 到新目录 `G:\DFLegacy-local-qa-20261122-medium`（排除 `.dflegacy` 缓存及 `Logs`，792 个文件、0 个失败；robocopy 状态 1 代表有文件复制）。副本目录及其登录器、探针未显示低完整性标签；副本配置 `ClientPath=DNF.exe`、测试服 `192.168.203.101`、不启动内置服务、Relay 禁用，账号字段沿用 QA 副本。服务端当前未运行；称号测试仍须按第 4 节先处理已过期时间。未运行新副本或探针，不声称游戏已修复；正式客户端未改。
- 进一步只读核对：`HKCU\Software\Microsoft\DirectInput` 的 ACL 允许 `INTERACTIVE` 查询及创建子项，且当前代理进程的交互组已启用；用户探针请求相同权限却返回 5。该差异涉及不同进程令牌或其他访问条件，当前未证明原因。第三版独立探针分开记录查询/创建访问及进程完整性、AppContainer 和交互组状态；仅编译、未运行，也未更改注册表。老第二章宿主保留相同 DirectInput 句柄转换，尚无证据支持修改宿主行为。
- 用户运行第二版探针：`DirectInput_registry_open=5`、`CoInitializeEx=0`、`CoCreateInstance_DirectInput8=0x80004005`、`DirectInput8Create=0x80004005`。注册表访问被拒和 COM 创建失败之间的因果关系尚未证明；未修改权限。
- 回查旧测试：11 月 12 日第二章普通进程曾以隔离客户端成功进城，11 月 19 日调试/本地构建经用户验证商城等操作。旧宿主和当前 `.tmp/chapter-two-local-qa/host/host/fixed-loader/main.cpp` 均把重建镜像句柄映射到真实宿主句柄后调用原生 DirectInput，并未发现靠跳过失败或特殊输入设置启动。10 月 27 日切回原路径成功的记录属于第一章，不能当作第二章的验证。旧成功不证明现有本机权限状态下同一个客户端仍能运行。
- 用户在交互桌面运行 x86 独立 DirectInput 探针，窗口前台时 `DirectInput8Create=0x80004005`，发生在任何设备创建/获取之前；这是独立进程观测，尚不能断言游戏内失败调用完全相同。只读检查显示 x86 COM 注册指向系统 `SysWOW64/dinput8.dll`，签名有效，HID 服务运行。更新探针以分别记录 COM 创建、Initialize 和注册表访问，等待用户运行；没有修改系统注册表或服务。
- 再次排查：`ff81308` 的临时 bootstrap worker 在 `RunEntryAsync` 的参数解析后已经调用 `ClientLayout.Configure(request.Directory)`，因此仅凭临时进程目录不能解释退出 1；之前相反的初步判断已撤销。`client` 下的已安装清单确实包含上级目录的游戏文件，但需要进一步证据确定具体失败阶段。服务端工作区 `fix/timed-title-expiry` 增加了 `bootstrap-chapter-guard` 早期拒绝记录，尚未构建成发布包，不能解释原版二进制的历史失败。
- 增加独立 x86 DirectInput 探针 `tools/diagnostics/chapter-two-directinput-probe.cpp`，构建产物 `.tmp/chapter-two-directinput-probe.exe`；没有运行探针。交互桌面 PowerShell 中运行 `& '.tmp/chapter-two-directinput-probe.exe' | Tee-Object '.tmp/chapter-two-directinput-probe.log'`，点击弹出的诊断窗口，关闭后保留日志。它输出独立进程的 keyboard/mouse HRESULT；成功不能证明游戏宿主初始化成功，失败也不能单凭它确定游戏失败原因。
- 主工作区起点 `48d68532463e95c38a6cbb3a954389ebed0c9db8`；提示窗分层重写和两份手册改动、两份未跟踪设计文档均为接手前已有工作。
- 修正 `startup_prompt.cpp` 中交接列出的四处编译错误，并在 `WM_PAINT` 中验证绘制区域，避免未验证的重绘消息持续产生。x64 原生构建退出码 0，产物位于 `.tmp/startup-prompt-build/x64/Release/DFLegacy.Launcher.exe`。按用户要求未运行自动测试或窗口验收；透明、缩放、长文本、焦点及交互仍待用户验证。固定入口哈希改变，不能将该产物直接替换到受管理的正式安装中。
- 随后按参考图修正原生与 WPF 提示框的正文、居中按钮和透明圆角。`ctest -C Release` 启动真实原生入口，在隔离桌面验证外框尺寸、鼠标命中、Enter、Esc 和退出码。`dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release … -- --update-ui-tests` 验证 WPF 普通提示、长正文滚动和更新确认。两项均退出 0。两套绘制均在 100%、125%、150% 验证透明圆角和边框。截图分别位于 `.tmp/build-temp/phase1-native-message.png` 和 `.tmp/test-temp/phase1-wpf-message.png`，来自生产绘制函数或 WPF 实际视觉树。原生视觉检查在离屏位图完成，跨显示器切换和整合包安装未做真机验收。
- 只读检查 `.tmp/full-client-1.0.17/client/Logs/DFLegacy/Incidents` 发现 01:43–01:53 的日志是 `gui-startup`/`headless-startup` 因第二章远程地址不可访问而失败，并非正常 r3 包 Updater 退出 1 的根因证据。正常包未找到 incident 日志。缺失 `validated.json` 会使启动流程进入修复路径，不能单凭此认定修复失败原因；该问题和 DirectInput 根因仍未确定，未修改正常包或回执。

## 1. 当前结论

服务端两项功能已经完成并通过定向 smoke test：

- 城镇装扮栏丢弃/销毁（CMD18 `listType=1`）
- 限时装备/称号到期回收（含装备槽、主背包、仓库、邮件）

以下为城镇装扮销毁补丁安装前的启动排查记录。

- 正常 v1.0.17-r3 包的 Updater 校验失败，原生/外层启动器都提示“更新或自动修复未完成”。
- QA `client-a` 和 v1.0.18 包能启动登录器，但游戏宿主在 DirectInput 初始化处失败。
- Smart App Control 处于开启状态，资源管理器双击未签名启动器会被静默拦截。

启动提示窗的分层透明与 WPF 布局修复已通过本机专项验证，用户确认可以进入下一阶段。固定入口的新启动包和更新迁移待单独验收，见第 5 节。

## 2. 已完成的服务端改动

### 2.1 城镇装扮栏销毁

- 工作区：`.tmp/worktrees/avatar-destroy-v1017`
- 分支：`fix/timed-title-expiry`（基于 `fix/avatar-destroy-v1017`）
- 提交：`23f87fb Fix town avatar destroy on the v1.0.17 release line`
- 主要改动：`src/DFLegacy.Server/EntranceService.cs`
  - CMD18 由只接受 `listType=0` 放宽为接受 `0/1`。
  - 装扮栏删除只持久化装扮栏，回 `listType=1` 并补发完整快照。
  - 主背包和金币销毁逻辑保持不变。
- 回归测试：`tests/DFLegacy.SmokeTests/AvatarDestroySmokeTests.cs`
- 结果：`--avatar-destroy` 0 failures；基线 `ff81308` 上为 RED（`no reply type=1 id=18`）。
- 进入城镇操作阶段时在 `d1f14ba6` 工作区复跑。CMD18 确认、装扮栏快照、落盘和错误物品拒绝的断言通过。测试使用独立夹具。

### 2.2 限时装备/称号到期回收

- 分支：`fix/timed-title-expiry`
- 提交：`d1f14ba Expire limited equipment and titles on the v1.0.17 release line`
- 主要改动：
  - `src/DFLegacy.Server/ItemCatalog.cs`：解析 `[usable period]`，新增 `UsablePeriodDays`。
  - `src/DFLegacy.Server/AvatarLifetimePolicy.cs`：从“只处理装扮”扩展为“装扮 + 带期限装备”；首次归一化锚定 `now + 天数 * 86400`，到期移除并写墓碑。
  - `src/DFLegacy.Server/AvatarLifetimeSessionRefresh.cs`：合并主背包、仓库并刷新。
  - `src/DFLegacy.Server/EntranceService.cs`：登录/选择角色刷新；装备槽 10 到期触发现有外观/属性刷新。
  - `src/DFLegacy.Server/JsonGameStore.AvatarLifetime.cs`
- 测试：`tests/DFLegacy.SmokeTests/TimedTitleLifetimeSmokeTests.cs`、`AvatarLifetimeSmokeTests.cs`
- 结果：
  - `--timed-title`：0 failures
  - `--avatar-lifetime`：0 failures
  - `--avatar-destroy`：0 failures
  - `--chapter-two-random-box`：0 failures
  - `--town-gold-discard`：0 failures
  - `--active-drop`：0 failures
- 已知限制：
  - 升级后已有的限时称号会在第一次归一化时重新获得一个完整期限，无法补算已流逝时间。
  - 客户端没有做称号倒计时，`add_info` 仍是品质种子；本轮只保证服务端到期移除。
  - 全量 smoke tests 未通过：被无关的 `IdleCeraRefreshSmokeTests` 固定端口 `127.0.0.44:47001` 连接拒绝阻断，重复运行可复现。不要把它算作功能失败，也不要声称全量通过。

### 2.3 单件右键入库与宿主配对

- 发布线 `da2100ea` 已修正数量为 1 的物品与单件装备。角色仓库开启时，普通右键直接转移单件，只有可拆分的多件堆叠打开数量框。
- `d1f14ba6` 工作区的 `--c2-quick-store` 通过 293 项断言。原生 `chapter_two_quick_store_tests.exe` 对受校验的第二章镜像通过 130 项，覆盖单件、装备、多件提示和指定数量。
- 更新前的 v1.0.18 QA 安装实际解出的宿主 SHA-256 为 `BBFB487C33C208C431962C73B21EE7F67DB70E82059D2E0E99D145B55648D6E3`，是 `da2100ea` 之前的版本。同一摘要见于原 v1.0.18 和本地 v1.0.19 构建的 App 内嵌资源。QA 日志的 `warehouse quick store installed` 仅表示钩子安装成功。
- `.tmp/fullstarter-roles-1.0.17` 中的标准角色构建嵌入了带修复的宿主 `7FC0080F8E03E532CDB911800C24D45BF0818E715966BAD7315BD30DA2770D83`，符合该发布线的 `Build-LauncherRoles.ps1` 校验。重打 QA 安装时校验最终 App 嵌入的宿主和配对 broker，按包清单交付。
- v1.0.18 QA 副本已通过 UpdateCore V4 安装 `v1.0.18-qa-quickstore`（本地 sequence 29，登录器仍显示 v1.0.18）。与基线相比只更新 App 和绑定其摘要的 `DFLegacy.Bootstrap.ini`，固定入口继续使用已安装的原版。V4 事务核验全部 787 个受管文件，提交后无 pending。核对最终单文件 App 的内嵌宿主为 `7FC0080F…`，broker 为 `0AA08275…`。
- 构建源为已有 `.tmp/upgrade-wt` 的 `5c8feb0c`，校验过的本地包和基线回执备份位于 `.tmp/chapter-two-local-qa/build-temp/quickstore-v1018`。该包只交给当前隔离安装，更新开关沿用本地 QA 设置。

### 2.4 本轮真机夹具

入口为 `G:/DFLegacy-v1.0.18-local-qa-20261122/DFOLauncher.exe`，沿用 QA 账号 `localqa1` 的角色「拼音验收甲」。QA 服务已重启，服务进程 PID 41940，入口 42312、频道 47001、地区端口 7100 和账号 48091 已监听。原存档备份为 `.tmp/chapter-two-local-qa/state.before-quickstore-fixtures-20261122-141110-009733.json`。只在该角色的空槽加入下列样本。

| 样本 | 初始位置 | 预期 |
| --- | --- | --- |
| 护额（39000） | 装扮栏槽 0 | 城镇丢弃后立即消失，重登后仍消失 |
| 蛇眼护额（39001） | 装扮栏槽 1 | 对照装扮仍在 |
| 破损的铁剑（27000） | 背包装备槽 11 | 普通右键直接入库 |
| 烤硬的黑面包（1001） | 背包槽 43 有 1 个，仓库槽 0 有 2 个 | 普通右键直接合并，仓库变成 3 个 |
| 朗姆酒（1002） | 背包槽 44 有 5 个 | 普通右键可选数量，存 2 个后背包还剩 3 个 |

用户已真机确认单件装备直接入库、单件堆叠直接合并、多件堆叠弹数量框。服务端日志记录 27000 数量 1、1001 数量 1 和 1002 部分数量 2 的正常仓库转移。首次测试时，装扮样本拖到游戏窗口内的城镇空地，图标跟随鼠标，确认框未出现。该会话没有收到 CMD18 或 CMD50，当时两件装扮均留在存档。修复与复测见第 2.5 节。

### 2.5 城镇装扮拖出入口

- 当前第二章镜像的装扮栏拾取函数写入拖放标记 7，穿戴栏写入标记 6。空地分发函数未处理 7，导致没有弹窗和协议请求。
- 发布线工作区 `.tmp/upgrade-wt` 在 `ChapterTwoAvatarDiscard.cpp` 给标记 7 增加城镇分支。根据当前装扮栏槽位和拖放物品 ID 查找物品，复用原生销毁确认。确认后的 CMD18 按原协议交给已有的服务端处理。
- 保留原本的普通背包、宠物和穿戴栏分发，复用本轮已通过的快速仓库逻辑。离线校验了游戏镜像的补丁位置和编译后 x86 分支，运行时结果由用户真机复验。
- 修订宿主 SHA-256 为 `6AC7DBA3FBD4F20C83E75DDEEF3C07A6EBC894A33EF95A54366FB7CA49AA9E22`。在用户退出 QA 客户端后，通过 V4 安装 `v1.0.18-qa-avatar-discard`（本地 sequence 30，登录器仍显示 v1.0.18）。校验了 787 个文件，用户存档、账号与游戏配置、外层与固定入口、Updater 共 7 项在安装前后哈希一致。原样本可直接复验，QA 服务健康检查为 HTTP 200。
- 构建和增量清单位于 `.tmp/chapter-two-local-qa/build-temp/quickstore-v1018/avatar-next`。用户确认真机通过。15:50:25 的日志收到 CMD18 `listType=1`，移除槽 0 的护额 39000，发送成功回包和装扮快照。15:50:28 重登，存档仅保留槽 1 的蛇眼护额 39001，仓库仍有已验证的铁剑与合并后的 3 个黑面包。

### 2.6 交付时遇到的坑

| 现象 | 根因与排查方法 |
| --- | --- |
| 单件仍弹数量框，日志却显示已安装快速仓库 | App 内嵌的是旧宿主 `BBFB487C…`，初始化日志只能说明钩子接线成功。先核对最终单文件 App 的内嵌资源，再核对真实会话解出的宿主摘要。核对版本号或源码分支会漏掉旧二进制。 |
| 装扮图标跟随鼠标，拖到城镇空地却没有确认 | 原生空地处理漏掉装扮栏拖放标记 7。服务端已有 CMD18 处理且自动化通过，但在没有客户端请求时起不到作用。先在同一会话找 CMD18，再决定改客户端还是服务端。穿戴栏标记 6 和副本 CMD50 各有自己的路径。 |
| 新旧 App 混装后，启动器进入修复或更新失败 | 分体布局的 App、Updater、固定入口和回执在 `client`，游戏资源及音量设置在上层游戏目录。替换 App 需要一起更新描述文件的 App 摘要与 V4 回执。两次 QA 增量均只变更 App 与描述文件，事务在校验全部 787 个文件后提交回执。固定入口换字节时另走新整包或已验证迁移。 |
| 看起来像音量丢失，重新启动却能保存 | 同目录的 V4 清单和整合包没有覆盖音量配置。重新解压到不同目录会改为读取新目录的设置。排查时先固定实际 `DNF.exe` 根目录，区分覆盖文件和替换整个目录。 |

## 3. 本地测试环境

### 3.1 服务端

- 测试服务端：`.tmp/avatar-destroy-v1017/server-publish/DFLegacy.Server.exe`
- 配置：`.tmp/chapter-two-local-qa/server.json`
- 存档：`.tmp/chapter-two-local-qa/state.json`
- 日志：`.tmp/chapter-two-local-qa/timed-title-test-server.out.log` / `.err.log`
- 账号：`localqa1`
- 角色：`拼音验收甲`（60 级鬼剑士）
- 当前配置绑定：
  - Admin `0.0.0.0:48091`
  - Entrance `0.0.0.0:42312`，AdditionalPorts `48090`
  - CharacterDatagram `0.0.0.0:42312`
  - GameplayDatagram `0.0.0.0:47012/47013`，AdvertisedHost `192.168.203.101`
  - Channel `0.0.0.0:47001`
  - FallbackRelay `0.0.0.0:47005`，AdvertisedHost `192.168.203.101`
  - PvP disabled；GameProbe disabled；Gm 面板 `127.0.0.1:48192`
- 健康检查：`http://192.168.203.101:48091/health`
- 本地服务端已停止；交接后需要重新启动。
- `ScriptPvfPath` 指向 `G:/Project/DNF2/Server/resources/Script.pvf`，不要指错。

启动命令：

```powershell
& 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish\DFLegacy.Server.exe' `
  --config 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\server.json'
```

### 3.2 用户正常客户端（只读，不要修改）

- 路径：`G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3`
- 原始配置已恢复：`ChapterTwoServerHost=games.firekula.site`、GitCode 更新、Relay IPv6、`DisableGitHubUpdates=false`。
- 配置备份：`launcher.chapter2.json.before-local-test-20261122-020956`
- 我临时加的 `client\launcher.chapter2.json`、`launcher.json`、任务脚本和计划任务已删除。
- 该包的 `.dflegacy` 状态：
  - 有 `v4/installed.json`：tag `v1.0.17`，manifest `e4da14c27a0ad67049a3829fba841e64d38c0555f32da523b3b81a63fbcc8512`
  - 有 `bootstrap/recovery.ini`
  - 缺 `v4/validated.json` 和 `bootstrap/startup.dat`
- 现象：原生 `client\DFLegacy.Launcher.exe` 和根目录 `DFOLauncher.exe` 都会启动 Updater；Updater 退出 1，`client/.dflegacy/bootstrap/bootstrap.log` 只有 `Verified updater failed.`。
- 这是交接后的第一优先客户端问题。不要在用户正常包上继续改配置；用副本排查。

### 3.3 测试客户端与已确认差异

| 包 | 启动器 / 宿主 | 现象 |
| --- | --- | --- |
| `.tmp/chapter-two-local-qa/client-a` | 独立 `DFLegacy.Chapter2.Launcher.exe` + `s1c2_chapter2_host` | 登录器能开；游戏宿主 DirectInput 初始化失败；d3d11/d3d8 都失败 |
| `.tmp/full-client-1.0.17` | 原生 `DFLegacy.Launcher.exe` | 与正常包 Launcher/Updater/App 哈希完全一致（`8CFB…` / `E1CF…` / `59B7…`），但缺安装记录；提示“更新或自动修复未完成”；测试后已恢复原启动器 |
| `.tmp/v1018-release/DFLegacy-ChapterTwo-v1.0.18-win-x64` | v1.0.18 完整安装包 | 有 `installed.json`，登录窗口能开；游戏宿主 DirectInput 失败；App/Updater 哈希 `1B4C…` / `AA95…`，与 v1.0.17 不同；测试配置指向 LAN/测试账号 |
| 用户正常 r3 包 | v1.0.17 生产宿主 | 有 `installed.json` 和 `recovery.ini`，缺 `validated.json`/`startup.dat`；Updater 退出 1 |

已确认的宿主日志特征：

- `ORDINARY_DESKTOP_PROCESS appcontainer=0`
- `C:\Windows\SysWOW64\dinput8.dll` 已加载
- 没有 CodeIntegrity/Defender 拦截记录
- 游戏窗口标题 `地下城与勇士`，错误对话框 `公告`
- Smart App Control：`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState=1`

DirectInput 根因尚未证明。已排除/未排除项：

- 不是 AppContainer。
- 不是 dinput8.dll 缺失。
- 可能是游戏窗口在 DirectInput 初始化时不在前台、宿主/启动代理的输入桌面限制，或 Smart App Control 对输入相关代码的拦截。
- 下一步建议：优先修复正常包的安装校验，或使用一个干净安装、平时能正常玩的客户端；其次用最小 DirectInput 探针区分“环境不支持”和“游戏宿主问题”。

## 4. 限时称号测试数据

测试称号位置（`localqa1` / `拼音验收甲`）：

| 位置 | 物品 ID | 名称 | 用途 |
| --- | --- | --- | --- |
| 装备槽 10 | 26040 | 斗士 | 装备槽到期 |
| 背包 10 | 26015 | 幸福恋人 | 背包到期 |
| 背包 11 | 26029 | 彩虹色希望 | 背包到期 |
| 背包 12 | 26034 | 玛尔战争英雄 | 背包到期 |
| 仓库 0 | 26038 | 瞬发者 | 仓库到期 |
| 仓库 1 | 26039 | 测验达人 | 仓库到期 |
| 背包 13 | 26001 | 卡妮娜的希望☆ | 永久对照 |

当前 `state.json` 里这些物品还在，但到期时间已经全部是过去（交接时服务端已停止，未结算）。启动服务端前先重设到期时间，否则登录时会被立即移除。

重设脚本（必须先停止服务端）：

```powershell
$state = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\state.json'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$j = Get-Content $state -Raw | ConvertFrom-Json
$c = ($j.Accounts | Where-Object { $_.UserName -eq 'localqa1' }).Characters[0]
$map = @{ 26040 = 600; 26015 = 660; 26029 = 720; 26034 = 780; 26038 = 840; 26039 = 900 }
foreach ($item in @($c.Inventory) + @($c.Equipment) + @($c.Warehouse)) {
    if ($map.ContainsKey([int]$item.ItemId)) {
        $item.AvatarExpiresAtUnixSeconds = $now + $map[[int]$item.ItemId]
    }
}
$j | ConvertTo-Json -Depth 30 | Set-Content $state -Encoding utf8
Remove-Item "$state.lock" -Force -ErrorAction SilentlyContinue
```

说明：

- 这批物品是直接赋 `AvatarExpiresAtUnixSeconds`，用于验证到期移除/刷新，不是 `[usable period]` 模板路径。
- 模板解析/首次归一化锚定由 `--timed-title` smoke test 覆盖。
- 装备槽到期后先从装备栏消失；背包/仓库到期后分别刷新；仓库需要打开仓库窗口查看。
- 永久称号 26001 用来确认没有误删。

## 5. 启动提示窗修复（本地验证通过）

### 5.1 已验证的版本

- 主工作区 `fix/startup-prompt-transparency` 基线为 `48d68532`。原生启动提示使用预乘 Alpha 的分层窗口，WPF 使用同一款 286×170 面板，两者的确认按钮均置于正文下方居中。
- 原生回归 29 项通过，包含真实入口的鼠标命中、Enter 和 Esc。WPF `--update-ui-tests` 通过，包含更新确认和长正文滚动。100%、125%、150% 的渲染检查保留透明圆角与完整边框。
- 用户看过结果并确认推进。原生普通 GDI 绘制以不透明底色垫在圆角后方，单独设置品红色键无法让 Windows 合成透明边。分层窗口按预乘 Alpha 输出，四角 RGBA 为零，同时按 DPI 缩放画面和确认按钮命中区。WPF 使用同款面板，正文独立滚动，避免长文盖住按钮。跨显示器现场验证和新版整合包安装待后续发布验收。固定入口哈希已变化，新产物需要由新启动包或已验证的迁移事务交付。
- 本地音量正常保存。只读检查 v1.0.17 和 v1.0.18 清单以及 v1.0.18 ZIP，确认原地更新和普通覆盖解压保留设置。更换游戏目录会使用新目录的设置，本轮跳过音量改动。

### 5.2 历史中间版本

- 主工作区分支：`fix/startup-prompt-transparency`
- 已提交：`0e10a8b Fix startup prompt transparency and clipping`
  - 这是中间版本：GDI+ 颜色键 + 去掉窗口区域，窗口变成矩形，四角深色不透明。
  - 用户已明确拒绝：要圆角透明，并且内容要完整、布局正确。
- 当时工作区还有未提交的**分层窗口重写**：`src/DFLegacy.Startup/startup_prompt.cpp`
  - 使用 `UpdateLayeredWindow` + 32bpp PARGB DIB，目标是真正的 per-pixel alpha。
  - 当时未通过编译，后续已修正并按 5.1 节验收。

#### 中间版本的编译问题

1. `startup_prompt.cpp` 约 106 行：`Gdiplus::PixelFormat32bppPARGB`
   - `PixelFormat32bppPARGB` 是宏，不能加 `Gdiplus::` 前缀。
   - 改成不带命名空间的 `PixelFormat32bppPARGB`。
2. 约 116 行：`const auto* button = prompt.held ? prompt.pressed.image : prompt.confirm.image;`
   - 推导成 `const Gdiplus::Bitmap*`，`DrawSkin` 需要 `Gdiplus::Image*`。
   - 改成 `Gdiplus::Image* button = ...;`。
3. 约 140 行：`static_cast<REAL>(...)`
   - 当前作用域没有 `REAL`。
   - 改成 `static_cast<float>(...)`。
4. 约 232 行：`new (std::nothrow) Gdiplus::Font`
   - GDI+ 的 `GdiplusBase::operator new` 不接受 nothrow 重载。
   - 改成普通 `new Gdiplus::Font(...)`，用 `try { ... } catch (...) { prompt.font = nullptr; }` 包住；`ShowStartupPrompt` 是 `noexcept`。

#### 中间版本的布局草案

以 WPF `ClassicMessageWindow` 为准：

- 窗口 `286x170`，`WS_EX_LAYERED`，per-pixel alpha，不要 `SetWindowRgn`。
- 面板图 `classic-message-panel.png` 按原始 `286x170` 绘制在 `(0,0)`。
- 不要额外画深色消息面板；面板图自身带纹理，额外填充会破坏内容。
- 文本区域与 WPF 一致：`Margin="20,44,20,38"`，即 `RectF(20,44,246,88)`，水平/垂直居中。
- 文本颜色 `#F4DCAF`，1px 深色描边；字体宋体，GDI+ 用 14px 近似 WPF `FontSize=13`。
- 按钮 `80x29`，位置 `(98,137)`；使用 `classic-message-confirm0.png` / `confirm2.png`，不要先填深色矩形。
- 键盘 Enter/Escape 确认；鼠标点击按钮；按钮悬停手型光标。
- 不要任务栏图标（`WS_EX_TOOLWINDOW`）。

### 5.3 构建与发布验证

构建环境：

- CMake：`G:\Game\DNF_ACT1\DFLegacy\.tmp\cmake-venv\Scripts\cmake.exe`（pip 安装的 CMake 4.4.5）
- Visual Studio：`D:\Program Files\Microsoft Visual Studio\18\Community`
- MSBuild 需要可写 `%TEMP%`；沙箱下会访问 `C:\Users\Firekula\AppData\Local\Temp` 失败，必须用非沙箱并设：

```powershell
$env:CMAKE_EXE = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\cmake-venv\Scripts\cmake.exe'
$env:TEMP = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\build-temp'
$env:TMP = $env:TEMP
./src/DFLegacy.Startup/build-native.ps1 -Architecture x64 -RunTests `
  -BuildDirectory 'G:\Game\DNF_ACT1\DFLegacy\.tmp\startup-prompt-build\x64'
```

通过标准：

- 构建 0 错误。
- `bootstrap_policy_and_process` 测试通过。
- 在隔离测试安装中通过新启动包或经过验证的入口迁移安装对应产物。触发提示窗，截图确认：
  - 四角真正透明，不再有紫色。
  - 面板边框完整，右侧/底部不裁剪。
  - 文本在面板内容区居中，按钮完整，没有深色按钮底框。
- 中间版本截图 `.tmp/prompt-fixed.png` 是矩形深色角版本，不作为验收。

## 6. 未完成项

1. **客户端安装校验/DirectInput**（第一优先）
   - 正常 r3 包缺 `validated.json`/`startup.dat`，Updater 退出 1。
   - 用同版干净整合包走正常安装或修复流程，或者使用已验收的独立 QA 安装。
   - DirectInput 仍可能在干净客户端上复现；需要最小探针或前台/输入桌面排查。
2. **启动提示窗发布验收**
   - 按第 5 节验证新整合包首启、已有安装迁移及跨显示器显示。
3. **设置跟随账号同步**
   - 尚未实现，只做了分析。
   - 需要设计：账号级设置存储、登录/登出同步、本地缓存合并、冲突策略、离线/缓存失效处理。
   - 相关用户反馈：游戏内设置和启动器游戏设置不跟随账号，本地缓存有时失效。
4. **装扮和背包容量**
   - 生产仍是 105。
   - 用户确认第三阶段目标为装扮栏 999,999 格，装备、消耗品、材料、任务等背包分类各 99,999 格。
   - 先验证客户端显示、滚动、协议槽号和持久化，再定寻址方案。
5. **限时称号客户端倒计时**
   - 服务端到期回收已完成。
   - 客户端 `add_info` 仍是品质种子；未做倒计时 UI。如需要，另行设计。
6. **真机验收**
   - 重跑六个已通过的称号用例时，按本轮开测时间重新设置相对到期时间。城镇装扮销毁及重登已验收，见第 2.5 节。

## 7. 复现命令

服务端构建/发布（在服务端工作区）：

```powershell
Set-Location 'G:\Game\DNF_ACT1\DFLegacy\.tmp\worktrees\avatar-destroy-v1017'
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release `
  -o 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish'
```

定向 smoke tests：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --timed-title
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --avatar-lifetime
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --avatar-destroy
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --chapter-two-random-box
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --town-gold-discard
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --active-drop
```

本地服务端启动：

```powershell
& 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish\DFLegacy.Server.exe' `
  --config 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\server.json'
```

客户端本地配置（用副本，不要改正常包）：

```json
{
  "ClientPath": "..\\DNF.exe",
  "StartServer": false,
  "ChapterTwoServerHost": "192.168.203.101",
  "Relay": { "Enabled": false, "Preference": "ipv4" }
}
```

## 8. 禁止事项与风险

- 不要修改 `G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3` 的游戏文件、`.dflegacy`、账号配置；需要测试就用副本。
- 不要覆盖用户存档、账号配置或正式服状态。
- 不要在正式服上验证称号/装扮改动。
- Smart App Control 开启时，资源管理器双击未签名启动器会被静默拦截；需要计划任务交互令牌或临时关闭/加排除（关闭不可逆，需用户明确同意）。
- 本地 QA 服务端绑定 `0.0.0.0` 并广播 `192.168.203.101`，只用于本机测试；注意端口冲突和防火墙。
- 全量 smoke tests 被 `IdleCeraRefreshSmokeTests` 固定端口 `127.0.0.44:47001` 阻断；不要声称全量通过。
- 终端沙箱会限制 `%TEMP%` 和 MSBuild；原生构建必须在非沙箱、可写 `%TEMP%` 下执行。

## 9. 关键路径索引

| 内容 | 路径 |
| --- | --- |
| 服务端工作区 | `.tmp/worktrees/avatar-destroy-v1017` |
| 服务端发布 | `.tmp/avatar-destroy-v1017/server-publish` |
| 本地 QA 配置/存档 | `.tmp/chapter-two-local-qa/server.json`、`state.json` |
| 本地 QA 日志 | `.tmp/chapter-two-local-qa/timed-title-test-server.out.log` |
| 正常 r3 包 | `G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3` |
| 正常包配置备份 | `launcher.chapter2.json.before-local-test-20261122-020956` |
| 1.0.17 测试副本 | `.tmp/full-client-1.0.17` |
| v1.0.18 测试包 | `.tmp/v1018-release/DFLegacy-ChapterTwo-v1.0.18-win-x64` |
| 原生启动源码 | `src/DFLegacy.Startup/startup_prompt.cpp` |
| 原生启动构建 | `.tmp/startup-prompt-build/x64` |
| 原生启动构建日志 | `.tmp/startup-prompt-build/x64/build.log` |
| CMake venv | `.tmp/cmake-venv` |
| 中间版本提示窗截图 | `.tmp/prompt-fixed.png`（矩形版本，不作验收） |
| 本交接文档 | `knowledge/decisions/2026-11-22-local-test-handoff.md` |

## 10. 建议交接顺序

1. 修正常 r3 包的安装校验（`validated.json`/`startup.dat`/Updater 退出 1），或换一个干净可用的客户端。
2. 用第 4 节重设称号到期时间，启动本地服，完成限时称号和装扮栏销毁的真机验收。
3. 验证单件右键入库和多件堆叠数量框，按第二章本地真机测试手册留存证据。
4. 按发布计划验收新版启动包的提示窗。
5. 做装扮和背包分类的容量探针。
6. 视需要补称号客户端倒计时。
