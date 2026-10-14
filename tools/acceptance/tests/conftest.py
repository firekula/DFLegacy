"""Override tmp_path to use workspace-local temp, avoiding sandbox ACL issues."""
import pathlib
import shutil
import itertools
import pytest

_counter = itertools.count()
_BASE = pathlib.Path(r"G:\Game\DNF_ACT1\DFLegacy\.tmp\pytest-tp")


@pytest.fixture
def tmp_path(request):
    """Use a workspace-local tmp directory instead of system temp."""
    _BASE.mkdir(parents=True, exist_ok=True)
    safe_name = request.node.name.replace("[", "_").replace("]", "_").replace("/", "_")
    d = _BASE / f"{safe_name}_{next(_counter)}"
    d.mkdir(parents=True, exist_ok=True)
    yield d
    shutil.rmtree(d, ignore_errors=True)