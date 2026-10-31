
"""组队+进本判据：纯函数（服务端日志文本 → 判定），不碰进程。"""
import pytest

from acceptance import party_dungeon as pd


ALICE = "6879107d-24d5-4086-adb3-a2a74c7256a7"
BOB = "9f6f7c0c-8865-4b70-882b-a191a59c5c95"


def test_pass_needs_shared_instance_and_both_sessions_loaded():
    v = pd.judge(pd._SAMPLE_PASS)
    assert v["result"] == "pass"
    assert v["max_members"] == 2
    assert v["start_map_count"] == 2
    assert v["two_player_dungeon"] is True
    assert v["instance_id"] == 1
    assert v["dungeon_id"] == 40
    assert v["start_map_sessions"] == sorted([ALICE, BOB])
    assert v["loaded_sessions"] == v["start_map_sessions"]
    # Only the leader selects a dungeon. Ordinary shared-party entry has HellPartyMode=0.
    assert len(pd._SELECT_DUNGEON.findall(pd._SAMPLE_PASS)) == 1
    assert "partyMode=0" in pd._SAMPLE_PASS


def test_single_player_dungeon_is_fail_not_pass():
    """单会话 START_MAP/FINISH_LOADING 不能证明双人共享副本。"""
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


@pytest.mark.parametrize("sample", [pd._SAMPLE_TRADE_TX_ONLY, pd._SAMPLE_HEARTBEAT_ONLY])
def test_trade_reply_and_anticheat_heartbeat_never_count_as_start_map(sample):
    assert pd.start_map_count(sample) == 0
    assert pd.judge(sample)["result"] == "fail"


def test_two_independent_solo_dungeons_never_pass():
    second_solo = pd._SAMPLE_ONE_PLAYER.replace(ALICE, BOB)
    text = pd._SAMPLE_PARTY_ONLY + pd._SAMPLE_ONE_PLAYER + second_solo
    assert pd.start_map_count(text) == 2
    assert pd.judge(text)["two_player_dungeon"] is False


def test_duplicate_start_maps_to_one_session_never_pass():
    text = pd._SAMPLE_PASS.replace(f"TX {BOB}", f"TX {ALICE}")
    assert pd.start_map_count(text) == 2
    assert pd.judge(text)["result"] == "fail"


@pytest.mark.parametrize("session", [ALICE, BOB])
def test_each_start_map_recipient_must_finish_loading(session):
    text = "\n".join(line for line in pd._SAMPLE_PASS.splitlines()
                     if not line.startswith(f"RX {session} type=1 protocol=40"))
    assert pd.judge(text)["result"] == "fail"


def test_loading_from_an_unrelated_session_cannot_complete_the_pair():
    text = pd._SAMPLE_PASS.replace(f"RX {BOB} type=1 protocol=40",
                                   "RX aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb type=1 protocol=40")
    assert pd.judge(text)["result"] == "fail"


@pytest.mark.parametrize("old,new", [
    ("entered dungeon 40 instance 1", "entered dungeon 40 instance 2"),
    ("entered dungeon 40 instance 1", "entered dungeon 41 instance 1"),
    ("instance 1: all 2", "instance 2: all 2"),
    ("instance 1: all 2", "instance 1: all 1"),
    ("room (0,3) map 5601", "room (1,3) map 5601"),
    ("room (0,3) map 5601", "room (0,3) map 5602"),
])
def test_instance_dungeon_room_and_loaded_count_must_agree(old, new):
    assert pd.judge(pd._SAMPLE_PASS.replace(old, new))["result"] == "fail"


def test_two_start_maps_must_describe_the_same_room_snapshot():
    text = pd._SAMPLE_PASS.replace(
        f"TX {BOB} game-server type=0 protocol=29 length=54 payload=00033FC5B5E9",
        f"TX {BOB} game-server type=0 protocol=29 length=54 payload=000340C5B5E9")
    assert pd.judge(text)["result"] == "fail"


@pytest.mark.parametrize("omitted", [
    "party instance 1 ready;",
    "Party member mageco (uid 2, slot 1) entered dungeon",
    "all 2 member(s) finished loading",
])
def test_shared_instance_lifecycle_evidence_is_required(omitted):
    text = "\n".join(line for line in pd._SAMPLE_PASS.splitlines() if omitted not in line)
    assert pd.judge(text)["result"] == "fail"


def test_old_success_cannot_supply_missing_evidence_for_a_later_attempt():
    text = pd._SAMPLE_PASS + pd._SAMPLE_ONE_PLAYER
    assert pd.judge(text)["result"] != "pass"


def test_latest_roster_for_the_matched_party_must_still_have_two_members():
    text = pd._SAMPLE_PASS.replace(
        "Dungeon 40 party instance 1: all 2",
        "Party roster 1 published: nameSel 6->6, 1 member(s).\nDungeon 40 party instance 1: all 2")
    assert pd.max_party_members(text) == 2
    assert pd.judge(text)["result"] == "fail"


def test_loading_before_own_start_map_cannot_complete_entry():
    loading = next(line for line in pd._SAMPLE_PASS.splitlines()
                   if line.startswith(f"RX {BOB} type=1 protocol=40"))
    text = pd._SAMPLE_PASS.replace(loading, "").replace(
        f"TX {BOB} game-server", loading + f"\nTX {BOB} game-server")
    assert pd.judge(text)["result"] == "fail"


@pytest.mark.parametrize("old,new", [
    ("game-server type=0 protocol=29", "game-server type=1 protocol=29"),
    ("game-server type=0 protocol=29", "game-server type=0 protocol=291"),
    (f"TX {BOB} game-server", f"RX {BOB} game-server"),
])
def test_start_map_direction_type_and_complete_protocol_id_are_required(old, new):
    assert pd.judge(pd._SAMPLE_PASS.replace(old, new))["result"] == "fail"


def test_powershell_command_never_passes_double_dash():
    cmd = pd._powershell_command("script", ["-Pid2", "1"], script_path="x.ps1")
    assert "--" not in cmd and "-File" in cmd
    assert pd._powershell_command("Get-Process", [])[-2] == "-Command"
