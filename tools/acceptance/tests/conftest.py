"""Override tmp_path to use workspace-local temp, avoiding sandbox ACL issues.

Each test gets a fresh directory named with pid + counter, and any leftover from a
crashed run is removed before use, so runs never share state (run isolation).
"""
import itertools
import os
import pathlib
import shutil

import pytest

_counter = itertools.count()
_BASE = pathlib.Path(__file__).resolve().parents[3] / ".tmp" / "pytest-tp"


@pytest.fixture
def tmp_path(request):
    """Use a workspace-local tmp directory instead of system temp."""
    _BASE.mkdir(parents=True, exist_ok=True)
    safe_name = request.node.name.replace("[", "_").replace("]", "_").replace("/", "_")
    d = _BASE / f"{safe_name}_{os.getpid()}_{next(_counter)}"
    shutil.rmtree(d, ignore_errors=True)  # 清掉崩溃残留，防止复用脏目录
    d.mkdir(parents=True, exist_ok=True)
    yield d
    shutil.rmtree(d, ignore_errors=True)
