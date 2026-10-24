# 本机多客户端验收平台（S0）

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
