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
