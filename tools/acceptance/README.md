# 本机多客户端验收平台（S0）

最新新增场景：[副本门前N地图右键终点](../../knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)。仅重启原手测客户端加载修复，沿用同一私有服务与存档，原生IJL文件输入及新鲜SDK确认跨区／同区／返回和副本门目标拒绝；保留关键原图，没有重复既有四端矩阵。

当前2026-10-30能力与证据入口见[交接](../../HANDOFF-2026-10-30.md)、[debug状态／CLI](../debug/README.md)及[最新U05验收](../../knowledge/decisions/2026-10-30-u05-resource-closeout.md)。下文Task0及旧人工场景保留其历史环境，不作为当前自动化能力或未闭合项的完整清单。默认使用原生输入与新鲜状态，普通步骤不截屏；四端窗口、私有存档来源和恢复规则见[RUNBOOK](RUNBOOK.md)。

## Task 0 环境前置结论

已在 `G:\Game\DNF_ACT1\DFLegacy` 使用 UTF-8 环境运行：

```powershell
$env:PYTHONIOENCODING='utf-8'
python -m pytest tools/acceptance/tests/test_env.py -v
```

实际环境与选型：

- Python **3.12.10**（`D:\Program Files\Python312\python.exe`）
- `pytest` **9.1.1**：可导入，后续测试继续采用 pytest 风格
- `PyYAML` 可用（模块 `yaml` 可导入）：场景继续采用 **YAML**（`scenarios/*.yaml`），无需改用 JSON
- Task 0 验证结果：**3 passed**（留存输出：`.superpowers/sdd/2026-10-07-local-multiclient-acceptance-s0/task-0-test-output.txt`，无 warning）
- [未验证] 来源：被沙箱权限阻断的前一会话（无留存输出）曾报告一次 `.pytest_cache` 权限 warning；上述留存复跑输出中未出现，仅作历史记录

后续任务据此使用 Python 3.12、pytest 与 PyYAML，不新增依赖。

运行全部工具测试时，从仓库根目录设置模块搜索路径：

```powershell
$env:PYTHONPATH = Join-Path (Get-Location) 'tools'
python -m pytest tools/acceptance/tests -q
```

模拟服务端的进程生命周期测试只启动临时 Python 子进程，独立于实际游戏端口；端口预检由单独的 socket/probe 用例覆盖，不需要关闭正在手动测试的客户端或服务端。

## 仅使用客户端内置输入接口

双客户端使用独立客户端目录，避免共享 `auto-input.json`。启用 `Config.ini` 的
`[Features] EnableAutoInput=1` 后，向目标客户端目录的 `auto-input.focus` 原子写入该进程
的十进制 PID；模块只接受自己的 PID，在确认自己的窗口已激活后将文件改名为
`auto-input.focus.done`，保留 PID 内容。调用方清除旧完成标记并验证本次 PID，随后再写输入计划。
后台激活由模块的 UI 线程定时器处理，输入计划仍只在该客户端位于前台时执行。

按键、点击继续使用 `auto_input.write_plan` 的内置时间线；截图可通过内置 `key`、`vk=44`
触发客户端 PrintScreen，读取该客户端 `ScreenShot` 目录中新生成的图片。
完成证据应核对本次计划内容及对应 PID 的新 `plan armed` 诊断，不能只依赖旧 `.done` 文件。

现有 `party_dungeon.run_live` 和部分历史辅助脚本包含外部聚焦/系统鼠标点击，
不适用于用户要求“只能使用内置输入”的测试。本轮此类实测及驱动见
`.tmp/party-live-20261024/report.md`、`builtin_driver.py`。


## 常规操作默认不截图

自2026-10-29起，绑定运行目录的内置输入使用统一控制器：

```powershell
python tools/acceptance/native_control.py <run-dir> A <fresh-label> '<steps-json>'
```

运行目录须有当前running部署记录和既有 `builtin_driver.py`。控制器验证PID、路径、启动时间及证据标签；普通调用默认不附加PrintScreen，只有关键验收追加 `--screenshot`。旧 `--no-shot` 仍兼容。历史冻结的 `control.py` 有“默认截图”行为，仅保留为旧证据，不作为新运行的默认控制器。

原生截图本身可造成约57～70ms的短停顿；[操作停顿对照与原图](../../knowledge/decisions/2026-10-29-operation-stutter-verification.md)记录了本轮有限路径，未据此排除其他卡顿来源。

## 四客户端窗口排列

按用户偏好，测试启动四个实际 DNF 进程并确认部署身份后，固定调用排布，无需再次请求用户排列窗口：

```powershell
python tools/acceptance/window_layout.py --journal <run-dir>/deployment.json --out <run-dir>/window-layout-001.json
```

也可导入 `window_layout.arrange(journal)`，传入部署记录路径或已解码的记录，返回 JSON 兼容回执。A/B 位于上排，C/D 位于下排；使用 A 所在显示器的工作区及物理坐标，保留原窗口大小、前台焦点和层叠次序。仅匹配记录中角色、PID、完整 DNF.exe 路径及精确启动时间一致的四个进程，每个进程须有唯一可见且无所有者的游戏主窗口。窗口最小化、最大化或身份不明确时拒绝移动；四个原尺寸窗口放不下时返回 `does_not_fit`，不调整大小或显示设置。`--out` 仅创建新文件，已有回执会在任何移动之前阻止执行；退出码 0 表示位置及尺寸复核成功，2 表示未完成。回执保存原位置、目标位置、实际位置及验证结果，出现部分移动失败时应先检查回执。
