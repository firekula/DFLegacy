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
- Task 0 验证结果：**3 passed**（pytest 报告有一次 `.pytest_cache` 权限 warning，不影响测试结果）

后续任务据此使用 Python 3.12、pytest 与 PyYAML，不新增依赖。
