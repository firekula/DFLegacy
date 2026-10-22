"""契约层：纯数据、无 I/O。"""
from datetime import datetime, timezone

import pytest

from acceptance.model import (LEVELS, RESULT_LEVELS, ActorSpec, Expect, Manifest,
                              Scenario, Step, deterministic_view, new_run_id,
                              parse_expect, load_scenario, RunPaths)
from pathlib import Path


def test_level_vocabularies_are_exact():
    assert LEVELS == ("[存在]", "[字节]", "[语义]")
    assert RESULT_LEVELS == ("pass", "needs-human", "missing-evidence", "fail")


def test_new_run_id_is_auto_and_carries_sha_and_scenario():
    rid = new_run_id("town-two-clients", "abc1234",
                     now=datetime(2026, 10, 7, 6, 0, 0, tzinfo=timezone.utc))
    assert rid == "20261007T060000Z-abc1234-town-two-clients"


def test_new_run_id_never_reuses_a_human_serial():
    a = new_run_id("s", "abc1234", now=datetime(2026, 10, 7, 6, 0, 0, tzinfo=timezone.utc))
    b = new_run_id("s", "abc1234", now=datetime(2026, 10, 7, 6, 0, 1, tzinfo=timezone.utc))
    assert a != b


def test_parse_expect_shorthand_defaults_to_existence_level():
    e = parse_expect("s2c:noti1")
    assert e.description == "s2c:noti1"
    assert e.level == "[存在]"
    assert e.pattern is None


def test_parse_expect_mapping_carries_level_and_evidence():
    e = parse_expect({"description": "s2c:noti1", "level": "[字节]",
                      "pattern": "0001", "evidence": "[抓包]"})
    assert (e.level, e.pattern, e.evidence) == ("[字节]", "0001", "[抓包]")


def test_parse_expect_rejects_unknown_level():
    with pytest.raises(ValueError):
        parse_expect({"description": "x", "level": "[猜的]"})


def test_scenario_and_actors_round_trip(tmp_path):
    p = tmp_path / "s.yaml"
    p.write_text(
        "id: town-two-clients\n"
        "goal: 两客户端进城互见\n"
        "expect_t0: server-accept\n"
        "actors:\n"
        "  - {name: A, driver: real-client}\n"
        "  - {name: B, driver: synthetic}\n"
        "steps:\n"
        "  - {actor: A, action: 登录进城, expect: [c2s:login-ok]}\n",
        encoding="utf-8",
    )
    sc = load_scenario(p)
    assert isinstance(sc, Scenario)
    assert sc.id == "town-two-clients"
    assert [a.name for a in sc.actors] == ["A", "B"]
    assert sc.actors[1].driver == "synthetic"
    assert sc.steps[0].expects[0].description == "c2s:login-ok"


def test_run_paths_are_addressed_by_actor_name(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    assert rp.actor_dir("A") == tmp_path / "RID" / "actor-A"
    assert rp.verdict.name == "verdict.json"
    assert rp.verdict_digest.name == "verdict.digest.json"


def test_semantic_only_scenario_has_no_falsifiable_expect():
    """`[语义]` 档在 verdict.judge 里只出 needs-human、**永远不 fail**（spec §3.2）
    ⇒ 它不算"能失败的期望"：只有 `[语义]` 期望的场景判定必须无效（spec §5.4 / 硬约束 7 / I-4）。"""
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "real-client"),),
                  steps=(Step("A", "走到 B 附近", (Expect("A 能看到 B", level="[语义]"),)),))
    assert sc.has_falsifiable_expect() is False


def test_scenario_without_expects_has_no_falsifiable_expect():
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "real-client"),),
                  steps=(Step("A", "do", ()),))
    assert sc.has_falsifiable_expect() is False


def test_any_non_semantic_expect_makes_the_scenario_falsifiable():
    """`[存在]`/`[字节]` 都可能 fail ⇒ 有一条就算有效（哪怕同场景还有 `[语义]` 项）。"""
    for level in ("[存在]", "[字节]"):
        sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                      actors=(ActorSpec("A", "real-client"),),
                      steps=(Step("A", "do", (Expect("x", level=level),)),
                             Step("A", "do2", (Expect("A 能看到 B", level="[语义]"),))))
        assert sc.has_falsifiable_expect() is True, level


def test_deterministic_view_drops_time_and_id_but_keeps_roster():
    m = Manifest(run_id="20261007T060000Z-abc1234-s", git_sha="abc1234",
                 created_utc="2026-10-07T06:00:00Z", scenario_id="s",
                 expect_t0="server-accept",
                 actors=[{"name": "A", "driver": "real-client", "pid": 123}],
                 servers={"game": {"exe": "DFLegacy.Server.exe", "pid": 456}},
                 config_overrides=[{"key": "GameplayDatagram.AdvertisedHost", "old": "x", "new": "127.0.0.1"}])
    v = deterministic_view(m)
    assert "created_utc" not in v and "run_id" not in v
    assert v["actors"] == [{"name": "A", "driver": "real-client"}]
    assert v["servers"]["game"]["exe"] == "DFLegacy.Server.exe" and "pid" not in v["servers"]["game"]
    assert v["config_overrides"] == [{"key": "GameplayDatagram.AdvertisedHost", "old": "x", "new": "127.0.0.1"}]
