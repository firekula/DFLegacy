# 测试矩阵与证据交接

本文说明如何选择测试、收集证据和解释结论。本次编写仅核对源码/文档，不把历史验收或文件存在当成本次测试通过。构建参数见 [构建与发行](build-test-release.md)，真实服务操作约束见 [运维](operations.md)。

## 1. 先回答测试范围

每次验证先写清：改了什么、ACT1/第二章、目标平台/架构、预期行为、最小回归路径、是否需要真实 PVF/NPK/客户端、是否会启动进程/监听端口/写隔离数据。仅文档修改通常做路径/命令/格式核对；不为了得到“测试通过”额外启动真实游戏或修改存档。

测试执行结果分开写：

- **通过**：本次确实运行指定命令，退出码和断言结果满足要求，证据可定位。
- **失败**：实际执行且不满足；保留首个失败日志与环境，不以改断言/skip 掩盖。
- **未执行**：缺工具、资源、权限、硬件或本次不在范围；列具体缺项。
- **跳过**：执行器明确跳过的用例，列数量与原因；不能算通过该覆盖。
- **历史参考**：旧日期、旧提交、旧 DLL/PVF/进程的证据，不给新构建背书。

## 2. 测试矩阵

| 层级 / 入口 | 环境与资源 | 副作用 / 能证明什么 |
| --- | --- | --- |
| `dotnet build` Server/Manager | .NET SDK；首次 NuGet restore 可能联网 | 编译/依赖成立；不证明运行或协议正确 |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release` | 跨平台；默认可生成 fixture | 写测试临时数据，部分用例有本机网络/进程；证明被执行的服务端协议/领域断言 |
| Smoke `--deployment-hosting` | 本机进程、监听环境 | 托管/就绪/关闭合同；不连接真实部署 |
| `DFLegacy.Manager.Tests --http --server-dir ...` | 已构建真实 Server，回环端口可用 | 临时管理身份、合成 PVF、实际子进程和 HTTP；不是生产账号/数据验收 |
| `node --test tests/DFLegacy.Manager.Tests/*.test.cjs` 的指定文件 | Node；源码脚本 | JS 请求/页面行为模拟；不证明真实浏览器布局、下载体验 |
| `release.py test` / `pytest tools/release/tests` | Python；本机 socket/临时目录 | 清单、归档、发布 API 模拟及保护规则；不发布真实 Release |
| `pytest tools/acceptance/tests tools/debug/test_drop_control.py` | 锁定 Python 依赖，`PYTHONPATH=tools` | 模拟进程、回环 socket、fixture；不需要完整游戏 |
| `dotnet run --project tests/DFLegacy.UpdateCore.Tests` | .NET；临时目录/测试网络 | V4 清单/安装/恢复等核心合同；不证明玩家远端更新成功 |
| Ijl15/Debug、RandomNative、Startup 原生测试 | Windows、VS C++、CMake；部分测试需编译产物 | 原生模型、ABI、实际临时子进程/锁等；不证明真机 UI/进图 |
| 完整 Launcher.Tests | Windows/WPF、匹配原生 fixture、锁定旧补丁归档等 | 托管/原生交接；缺外部 fixture 时不能称完整通过 |
| Launcher 专项参数 | Windows/WPF；由具体入口确定额外输入 | 仅该专项，例如更新、故障、第二章设置；不替代完整套件 |
| ACT1 完整 Python | Windows 原生探针变量/再生 fixture，视用例需要 | 默认回放可离线；缺 DLL/PVF 的 skip 必须单列 |
| 第二章宿主/broker 构建自带测试 | 锁定外部源码、重建 EXE、实际 NPK/PVF | 校验真实映像补丁/资源边界；不替代图形客户端 |
| Smoke `--chapter-two-* <PVF>` | 对应真实服务端 PVF/其他参数 | 指定资源规则；不要给默认 smoke 填生产数据目录 |
| `deploy/chapter-two/smoke-container.py` | Linux Docker daemon、已构建镜像、兼容服务端 PVF | 创建隔离容器/匿名卷并注册测试账号，完成后清理；会写测试存档，不是只读检查 |
| `acceptance.cli --dry-run` | Python/YAML | 创建 run 与缺证据判定，不起游戏；不能判“游戏通过” |
| `acceptance.cli run` / native control / 实机报告脚本 | Windows、合法客户端/资源、明确隔离副本/测试账号/端口 | 启动或控制真实客户端，写测试数据、日志和证据；需相应测试授权 |
| V4 `verify --remote` / 下载验收 | 真实公开来源可达，已存在正确附件 | 匿名回读、大小/摘要/Range；不等于实际安装/回滚完整验收 |

真实资源专项入口在 [SmokeTests/Program.cs](../../tests/DFLegacy.SmokeTests/Program.cs) 顶部按参数分流；例如 `--chapter-two-cemetery <PVF>`、`--chapter-two-tower <PVF>`、`--chapter-two-recipe <PVF>`、`--chapter-two-item-use <PVF>`。不要推断其他参数；先读对应分支及测试类，核对读取/写入位置和资源格式。

## 3. 可复制的无完整游戏测试流程

Windows 在新虚拟环境安装锁定依赖：

```powershell
py -3.12 -m venv artifacts/python-test-env
if ($LASTEXITCODE) { throw 'venv failed' }
& ./artifacts/python-test-env/Scripts/python.exe -m pip install --require-hashes --only-binary=:all: -r tools/release/requirements-ci.txt
if ($LASTEXITCODE) { throw 'dependency install failed' }
$env:PYTHONPATH = Join-Path $PWD 'tools'
& ./artifacts/python-test-env/Scripts/python.exe -m pytest tools/acceptance/tests tools/debug/test_drop_control.py -q
if ($LASTEXITCODE) { throw 'tool tests failed' }
& ./artifacts/python-test-env/Scripts/python.exe -m pytest tools/release/tests -q
if ($LASTEXITCODE) { throw 'release tests failed' }
```

已有该虚拟环境时先核对用途/依赖，不删除重建别人的环境。初次安装会访问包源；`--require-hashes --only-binary` 失败时调查版本/平台 wheel，不去掉哈希限制凑成功。

Bash 对应：

```bash
python3 -m venv artifacts/python-test-env
artifacts/python-test-env/bin/python -m pip install --require-hashes --only-binary=:all: -r tools/release/requirements-ci.txt
PYTHONPATH="$PWD/tools" artifacts/python-test-env/bin/python -m pytest tools/acceptance/tests tools/debug/test_drop_control.py -q
PYTHONPATH="$PWD/tools" artifacts/python-test-env/bin/python -m pytest tools/release/tests -q
```

逐条核对退出码。可以根据改动选择必要范围；不是每次文字修订都必须跑全套。

托管和 JS：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests -c Release
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --deployment-hosting
dotnet run --project tests/DFLegacy.Manager.Tests -c Release -- --http --server-dir "$PWD/src/DFLegacy.Server/bin/Release/net10.0"
dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release
node --test tests/DFLegacy.Manager.Tests/manager-browser.test.cjs tests/DFLegacy.Manager.Tests/client-pay-browser.test.cjs tests/DFLegacy.Manager.Tests/gm-management-browser.test.cjs
```

这些独立命令每条后都应检查 `$LASTEXITCODE`；不要只检查最后一条。Manager 的 server-dir 应是前面构建得到的目录，不是发行线上目录。`gm-mail-browser.test.cjs` 也存在，邮件功能改动需审查该测试；当前 Gitea 工作流没有包含它，不能声称工作流已覆盖全部 `*.test.cjs`。

## 4. CI 实际覆盖与历史门禁

当前 [.gitea/workflows/release.yaml](../../.gitea/workflows/release.yaml) 名为 `source-tests`，使用 `dflegacy-linux-x64` runner。顺序为锁定 Python 工具测试、默认 smoke、Manager HTTP、上节三个 Node 文件、`release.py test`。推送/PR/手动触发跑同一测试，checkout 不保留凭据，Release/Packages 权限为 none。

它**没有** Windows runner job、WPF 实机验收、第二章外部宿主构建、Docker build/运行或远端发布。 [BUILDING.md](../../BUILDING.md) 的 Windows/全 Python 门禁与 [ci.py](../../tools/release/ci.py) 可作为手动发行流程参考，不能当当前 YAML 已执行的证据。

`ci.py windows` 实际包含原生测试、完整 Launcher 测试和资源组包，需要锁定旧补丁下载；没有理由只看到脚本名就写“完整 Python 已执行”。[native_fixtures.py](../../tools/release/native_fixtures.py) 和 [fixture 说明](../../tests/fixtures/native-debug/README.md) 给出了独立原生 fixture 流程。每次测试报告以实际命令/结果为准。

## 5. Windows 原生 / WPF 回归

完成 [构建与发行](build-test-release.md#4-windows-原生与不可变资源) 的原生构建后，登录器专项示例：

```powershell
$ijl15 = Join-Path $PWD 'artifacts/native/ijl15/Release/ijl15.dll'
dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release -p:BuildNativeAssets=false "-p:Ijl15PrebuiltPath=$ijl15" -- --update-v4-tests
```

`--incident-tests`、`--startup-entry-migration-tests`、`--chapter-two-settings-tests` 等真实入口见 [Launcher tests Program.cs](../../src/DFLegacy.Launcher/tests/Program.cs)。先读分支需要的环境/文件，不能以专项通过替代完整套件。`--update-public-smoke` 会访问真实来源，与离线单元测试不同。

Startup 原生入口可对 x86/x64 分别执行：

```powershell
pwsh -NoProfile -File src/DFLegacy.Startup/build-native.ps1 -Architecture x86 -RunTests -BuildDirectory 'artifacts/startup-tests-x86-new'
pwsh -NoProfile -File src/DFLegacy.Startup/build-native.ps1 -Architecture x64 -RunTests -BuildDirectory 'artifacts/startup-tests-x64-new'
```

测试实际临时子进程、文件锁、路径/链接拒绝、自替换与恢复，不联络更新服务器。新输出应有对应日志及测试结果。它不证明与既存玩家入口哈希兼容。

ACT1 完整 Python 原生路径需要本次 DLL，例如 `DFLEGACY_GAMEPLAY_PROBE_DLL`、`DFLEGACY_NATIVE_LOOP_DLL`；城镇控制器资源用 `DFL_TOWN_ROUTE_CONTROLLER_PVF`。查看 [原始指令 fixture](../../tests/fixtures/act1-motion-horizon/README.md)、[城镇 fixture](../../tests/fixtures/act1-town-navigation/README.md) 和 [debug README](../../tools/debug/README.md)。已有 JSON fixture 回放只证明解析/模型/序列化，不证明正在运行的游戏加载了本次 DLL。

[crash_capture/test_capture.py](../../tools/debug/crash_capture/test_capture.py)、[Ijl15 incident 脚本](../../src/DFLegacy.Ijl15/tests/test_incidents.py) 等需要明确原生可执行参数；普通 pytest 无参数收集不是实际事故进程验收。

所有项目窗口必须遵守 [AGENTS.md](../../AGENTS.md) 的原版 DNF 风格。WPF 优先复用 `ClassicStyles.xaml`、`ClassicDialogFrame`、`PixelText`、`SkinButton`，普通消息用 `ClassicMessage`，原生提示参考 `startup_prompt.cpp`。实际最终产物验证：标题、长文本、按钮状态、Tab/Enter/Escape、默认焦点、关闭操作；至少记录 100% 与常见 125%/150% 显示缩放的可见/可操作结果。测试窗口截图不能替代最终登录器启动，也不能只看 XAML 就宣称没有截断。

## 6. 实机测试必须先隔离

未得到实机/真实进程操作授权时，仅完成静态检查和本地安全测试计划。已有授权时继续，不重复要求同范围确认。无论是否授权，先做到：

1. 使用独立客户端目录、独立服务端测试数据，确认绝对路径；不要复用日常存档。记录源备份哈希、目标路径和准备方式。
2. 每个 actor 绑定实际 PID、进程启动时间、完整 EXE 路径、加载模块哈希。仅相同 PID 不足以排除复用。
3. 保存实际服务端配置及有效环境差异，日志中不暴露密码/Token。起服后确认 ready/必要端口，再进入 UI 测试。
4. 测试前后核对原始日常数据没有变化，停止**本次拥有**的进程，保留测试证据。不把测试档复制回日常档。
5. 真实修复/破坏测试只对隔离副本做，例如删 DLL、制造下载中断、改坏配置；不对唯一玩家目录试错。

[acceptance.cli](../../tools/acceptance/cli.py) 的 orchestration 会将存档复制至 run 私有目录，通过绝对 `DFLegacy__DataPath` 给 Server。它仍会启动真实服务端/客户端并写私有档，不能因“隔离”就误标只读。默认 `town-test` 路径是历史约定，调用时应显式传入已核对的 launcher/server 路径，禁止让它猜到日常副本。

无客户端预演：

```powershell
$env:PYTHONPATH = Join-Path $PWD 'tools'
& ./artifacts/python-test-env/Scripts/python.exe -m acceptance.cli run --scenario town-two-clients --actors 2 --dry-run --runs-dir 'artifacts/acceptance-dry-runs'
```

它创建 run/manifest/判定，不启动游戏。预期可出现 `missing-evidence` / `needs-human`，这是预演能力验证，不是双人进城通过。

实际运行参数/人工步骤参考 [RUNBOOK](../../tools/acceptance/RUNBOOK.md)，但其中硬编码账号、路径、旧 relay 容器及历史外部聚焦方法不能直接照搬。当前 [acceptance README](../../tools/acceptance/README.md) 要求独立客户端目录和本次内置输入身份；用户要求“只用内置输入”时，不能调用含外部鼠标/聚焦的历史脚本。`native_control.py` 还依赖现存运行 journal 与 driver，不是空目录即可使用的通用启动器。

## 7. 证据如何判读

`runs/<runId>/` 一般包含 `manifest.json`、`steps.json`、actor 日志、server 日志、`offsets.json`、`verdict.json` 和 digest。runId 由工具生成，不要手工覆盖已有 run。`judge` 复判会写输出，若需不可变证据，先复制原 run 并记录原始摘要。

| 结果 | 准确含义 |
| --- | --- |
| `pass` | 声明的字节/帧条件出现；不自动表示登录/进城/视觉语义正确 |
| `needs-human` | 语义项未由机器裁决，必须另有观察证据 |
| `missing-evidence` | 缺该类产物，不能当作“不发生”或“通过” |
| `fail` | 有证据但不符合断言 |
| `green-with-human` | 没有 fail；可能 `pass:0`，不表示人工语义已通过 |

时间对齐：`manifest.created_utc` 是控制端创建 run 时间，不是服务端 accept 时间。旧 `offsets.json` 用“控制端时间减 actor 首帧时间”，混有真实启动经过时间，不是已测得的跨机钟差。netdump 使用本地时间，须按实际时区转换 UTC，不能直接贴 UTC 标签。需要连接 T0 时读取同轮服务端明确连接/就绪日志。

验证判定器应同时能复判一致并在反例中变红；反例在复制的 fixture/临时场景中做，不能污染基准场景或篡改正式 run 来制造想要的结果。战斗行为在实际副本确认加载完成后测，保留同轮待机对照；跳跃/像素变化不能替代服务器伤害/HP或特定新上行帧证据。具体阈值以 [combat_gate.py](../../tools/acceptance/combat_gate.py) 为准，其 `--self-check` 只是判定器自检。

截图优先留关键验收状态；普通轮询使用原生状态/日志。原生 PrintScreen 本身可能造成停顿，不把测量动作引入的帧尖峰归咎于游戏。截取完整原图并记录 PID/时间/场景，另做标注副本，不能只交裁剪图掩盖窗口状态。

## 8. 发行与更新验收矩阵

每次选择的发行变体至少记录：章节、x86/x64、normal/test、旧安装基线、新 tag/sequence、fixed entry 哈希、资源/PVF身份、来源。第二章独立 x64 测试包不能充当四变体正式更新证明。

| 路径 | 要验证的结果 |
| --- | --- |
| 全新安装 | final package 自身完整、三程序/辅助工具角色正确，实际启动并显示正确发行号 |
| 同章升级 / 跨版直达 | 完整目标集合、退役与累计迁移正确，账号/地址/未知配置保留 |
| 跨章 / 固定入口桥接 | 正确渠道、baseline、sequence 下限，逐步骤来源可用且入口迁移恢复成功 |
| 同版本 / 离线启动 | 满足可信已装记录条件才放行；状态如实显示未确认最新 |
| 损坏检测 / 修复 | DLL/EXE/资源/配置分别覆盖，修复前不执行不可信 helper，损坏配置原字节保留 |
| 中断恢复 / 回滚 | 下载、安装、提交记录等阶段恢复一致；备份原本损坏时不误称旧版健康 |
| 文件占用 / 取消 | 不自动杀无关进程；取消不混淆成功或 crash |
| 公开下载 | 全变体匿名回读、长度/摘要、Range或全量回退；不靠发布者登录态 |
| 窗口/UI | 原版风格、长文本/高 DPI/键盘/关闭、实际安装和恢复窗口切换 |

摘要、PE 属性和签名状态均针对最终字节；详见 [版本与三个 EXE](build-test-release.md#5-版本三个-exe-和最终文件属性)。未签名应直接记未签名。历史的“已验四变体”需要对应历史 tag/提交，不能继承为本次新字节的结论。

## 9. 最小可审计报告模板

报告放在本次选定且可保留的位置；原始私密日志留受控目录，提交仓库前脱敏。不要依赖未跟踪/被忽略的 artifacts 作为唯一公开交接。建议包含：

```text
日期与时区：
任务 / 行为 / 明确不在范围项：
源码 HEAD / 工作区未提交差异：
章节 / 平台 / 架构 / variant / render mode：
构建及运行目录（公开报告脱敏）：
工具版本：
实际 EXE/DLL/PVF/NPK SHA-256 / PE角色版本：
服务端版本/digest / 有效配置差异 / 测试数据来源与隔离证明：
命令、开始/结束时间、退出码、日志相对路径：
passed / failed / skipped 与具体原因：
实机步骤、PID+启动时间、关键截图/原生日志：
预期与实际、未验证边界、恢复/清理结果：
```

验收结论应能回答“这次执行了什么、什么字节在运行、看到什么、还缺什么”。不能以旧报告数字、新文件时间戳、`dotnet test` 空成功、单次 health、没有崩溃或 `green-with-human` 代替这个链条。
