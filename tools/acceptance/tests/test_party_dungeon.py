
"""组队+进本判据：纯函数（服务端日志文本 → 判定），不碰进程。"""
import pytest

from acceptance import party_dungeon as pd


def test_pass_needs_two_members_and_two_start_maps():
    v = pd.judge(pd._SAMPLE_PASS)
    assert v["result"] == "pass"
    assert v["max_members"] == 2
    assert v["start_map_count"] == 2
    assert v["two_player_dungeon"] is True


def test_single_player_dungeon_is_fail_not_pass():
    """只有一个 protocol=16 ⇒ 单人进本，组队没生效，必须 fail。"""
    v = pd.judge(pd._SAMPLE_ONE_PLAYER)
    assert v["result"] == "fail"
    assert v["max_members"] == 1


def test_party_without_dungeon_is_fail():
    v = pd.judge(pd._SAMPLE_PARTY_ONLY)
    assert v["result"] == "fail"
    assert v["start_map_count"] == 0


def test_nothing_at_all_is_missing_evidence():
    assert pd.judge(pd._SAMPLE_EMPTY)["result"] == "missing-evidence"


def test_parsers_ignore_unrelated_lines():
    text = "Entrance listener ready\nGameplay UDP relay enabled\n"
    assert pd.max_party_members(text) == 0
    assert pd.start_map_count(text) == 0


def test_powershell_command_never_passes_double_dash():
    cmd = pd._powershell_command("script", ["-Pid2", "1"], script_path="x.ps1")
    assert "--" not in cmd and "-File" in cmd
    assert pd._powershell_command("Get-Process", [])[-2] == "-Command"
