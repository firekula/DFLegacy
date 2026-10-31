"""orchestrate.py 的 TDD 测试。"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from acceptance.model import RunPaths
from acceptance.orchestrate import (
    parse_actor_accounts,
    launcher_command,
    capture_client_pid,
    write_steps,
)


# ===== parse_actor_accounts =====

def test_parse_actor_accounts_normal():
    assert parse_actor_accounts("A=alice,B=bob", {"A", "B"}) == {"A": "alice", "B": "bob"}


def test_parse_actor_accounts_skips_empty_segments():
    assert parse_actor_accounts("A=alice,,B=bob,", {"A", "B"}) == {"A": "alice", "B": "bob"}


def test_parse_actor_accounts_single():
    assert parse_actor_accounts("A=solo", {"A"}) == {"A": "solo"}


def test_parse_actor_accounts_rejects_no_equals():
    with pytest.raises(ValueError):
        parse_actor_accounts("A_alice", {"A"})


def test_parse_actor_accounts_rejects_unknown_name():
    with pytest.raises(ValueError):
        parse_actor_accounts("X=alice", {"A", "B"})


def test_parse_actor_accounts_rejects_duplicate_name():
    with pytest.raises(ValueError):
        parse_actor_accounts("A=alice,A=charlie", {"A"})


def test_parse_actor_accounts_rejects_the_same_alias_for_two_actors():
    """账号名与 actor 必须一一对应**且不同**（one-command-amendment.md:22）：
    同一别名给两个 actor ⇒ 双客户端同账号，"两个客户端"这个验收本体就失去意义（假绿）。"""
    with pytest.raises(ValueError) as ei:
        parse_actor_accounts("A=shizuku,B=shizuku", {"A", "B"})
    message = str(ei.value)
    assert "shizuku" in message and "账号" in message, message


def test_parse_actor_accounts_accepts_distinct_aliases():
    assert parse_actor_accounts("A=shizuku,B=test", {"A", "B"}) == {"A": "shizuku", "B": "test"}


# ===== launcher_command =====

def test_launcher_command_normal():
    result = launcher_command(Path("DFLegacy.Launcher.exe"), "alice")
    assert result == ["DFLegacy.Launcher.exe", "--headless", "--no-dialog", "--account", "alice"]


def test_launcher_command_absolute_path():
    exe = Path("/usr/bin/launcher")
    result = launcher_command(exe, "bob")
    assert result[0] == str(exe)  # str() normalises path separators per platform
    assert result[-1] == "bob"
    assert "--headless" in result
    assert "--no-dialog" in result
    assert "--account" in result


def test_launcher_command_rejects_empty_account():
    with pytest.raises((TypeError, ValueError)):
        launcher_command(Path("launcher.exe"), "")


# ===== capture_client_pid =====

def test_capture_client_pid_finds_pid(tmp_path):
    log = tmp_path / "launcher.log"
    log.write_text("header\nStarted DFLegacy client process 12345;\n", encoding="utf-8")
    pid, line, offset = capture_client_pid(log, start_offset=0, timeout_s=5.0)
    assert pid == 12345
    assert "12345" in line
    assert offset > 0


def test_capture_client_pid_timeout(tmp_path):
    log = tmp_path / "launcher.log"
    log.write_text("nothing useful here\n", encoding="utf-8")
    from acceptance.actor_pid import PIDTimeoutError
    with pytest.raises(PIDTimeoutError):
        capture_client_pid(log, start_offset=0, timeout_s=0.3)


# ===== write_steps =====

def test_write_steps_writes_json_array(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    entries = [
        {"actor": "A", "index": 1, "action": "登录并进到城镇", "at_utc": "2026-10-15T01:02:03Z"},
    ]
    path = write_steps(rp, entries)
    assert path.name == "steps.json"
    assert path == rp.run_dir / "steps.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data == entries


def test_write_steps_empty_list(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    path = write_steps(rp, [])
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data == []
    assert isinstance(data, list)


def test_write_steps_overwrite(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    write_steps(rp, [{"actor": "A", "index": 1, "action": "x", "at_utc": "T1"}])
    write_steps(rp, [{"actor": "B", "index": 2, "action": "y", "at_utc": "T2"}])
    data = json.loads((rp.run_dir / "steps.json").read_text(encoding="utf-8"))
    assert len(data) == 1
    assert data[0]["actor"] == "B"