import os
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def _readme() -> str:
    return (REPO_ROOT / "protocol" / "README.md").read_text(encoding="utf-8")


def test_readme_documents_three_vocabularies_and_boundaries():
    md = _readme()
    for token in ("[静态]", "[抓包]", "[真机]", "[回放]", "[推断]", "[未验证]",
                  "T0_no-evidence", "T4_fields-read+write",
                  "pass", "needs-human", "missing-evidence", "fail",
                  "T3/T4", "不等于", "握手在场"):
        assert token in md, token


def test_readme_documents_commands_and_exit_codes():
    md = _readme()
    for token in ("python -m protocol.s1 import", "python -m protocol.s1 render",
                  "python -m protocol.s1 verify", "退出码"):
        assert token in md, token


def test_readme_documents_suite_layout_and_known_collection_errors():
    md = _readme()
    for token in ("python -m pytest protocol -q",
                  "python -m pytest acceptance/tests -q",
                  "既有收集错误",
                  "与 protocol 无关"):
        assert token in md, token


def test_readme_documents_run_ledger_conventions():
    md = _readme()
    for token in ("10 字节",
                  "§8.5",
                  "created_utc 最晚",
                  "<UTC>-<git短哈希>-<scenario_id>",
                  "换机不需要 `runs/`",
                  "一次一个 run",
                  "控制端时钟",
                  "[存在]",
                  # 终审 M2：三个交接必读点 + manifest 的 rows 约定
                  "green-with-human",
                  "证据行号",
                  "出过结论",
                  "rows: 0",
                  # 终审 M3：README 不进清单是明示取舍
                  "文档不是规范数据"):
        assert token in md, token


def test_readme_defines_green_with_human_including_missing_evidence():
    """N2：`green-with-human` 的触发条件是 needs-human **或** missing-evidence（`verdict.py` 的 `status()`）。"""
    line = next(ln for ln in _readme().splitlines() if "green-with-human" in ln)
    assert "needs-human" in line and "missing-evidence" in line, line


def test_cli_verify_help_documents_exit_codes():
    proc = subprocess.run(
        [sys.executable, "-m", "protocol.s1", "verify", "--help"],
        cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8",
        env={**os.environ, "COLUMNS": "200", "PYTHONIOENCODING": "utf-8"})
    assert proc.returncode == 0, proc.stderr
    for token in ("退出码", "1 断言不符", "2 用法错"):
        assert token in proc.stdout, (token, proc.stdout)
