"""S0 环境前置：确认测试框架与 YAML 解析可用（不可用就必须换选型，别硬写）。"""
import importlib
import sys


def test_python_version_at_least_312():
    assert sys.version_info[:2] >= (3, 12), f"需要 3.12+，实际 {sys.version.split()[0]}"


def test_pytest_importable():
    import pytest  # noqa: F401


def test_yaml_available():
    mod = None
    for name in ("yaml",):
        try:
            mod = importlib.import_module(name)
            break
        except ImportError:
            continue
    assert mod is not None, "没有 YAML 解析器：改用 JSON 场景（把 scenarios/*.yaml 改 *.json）"
