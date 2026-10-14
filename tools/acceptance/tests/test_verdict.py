"""verdict：三分判定 + 归一化摘要 + 防恒真判定。"""
from pathlib import Path

import pytest

from acceptance.model import ActorSpec, Expect, Scenario, Step
from acceptance.model import RunPaths
from acceptance.verdict import (RESULT_LEVELS, Verdict, VerdictEntry, digest, judge,
                                write_verdict)


def _sc(*steps):
    return Scenario(id="s", goal="g", expect_t0="server-accept",
                    actors=(ActorSpec("A", "real-client"), ActorSpec("B", "synthetic")),
                    steps=steps)


def test_counts_keys_are_exactly_the_four_result_levels():
    v = Verdict(scenario_id="s", entries=[VerdictEntry("A", "do", "d", "[存在]", "pass")])
    assert set(v.counts()) == set(RESULT_LEVELS)


def test_existence_level_passes_when_pattern_found_in_actor_netdump(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("[2026-10-07 06:00:01.000] TX len=43 hex=F2F7F142\n",
                                     encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("F2F7F142"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "pass"
    assert v.status() == "green"


def test_existence_level_fails_loudly_when_absent(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("DEADBEEF"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "fail"
    assert v.status() == "red"


def test_semantic_level_is_needs_human_not_pass(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "走到 B 附近", (Expect("A 能看到 B", level="[语义]"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "needs-human"
    assert v.status() == "green-with-human"


def test_byte_level_requires_the_declared_pattern_not_just_the_description(tmp_path):
    """`[字节]` 与 `[存在]` 的差别：`[字节]` 必须命中 `pattern`；
    若把 pattern 指向一个不存在的串，即使 description 能命中，也必须 fail。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=43 hex=F2F7F142\n", encoding="utf-8")
    ok = judge(_sc(Step("A", "登录", (Expect("登录帧", level="[字节]", pattern="F2F7F142"),))),
               {"A": d}, tmp_path / "server")
    assert ok.entries[0].result == "pass"
    bad = judge(_sc(Step("A", "登录", (Expect("登录帧", level="[字节]", pattern="DEADBEEF"),))),
                {"A": d}, tmp_path / "server")
    assert bad.entries[0].result == "fail"


def test_missing_artifact_is_missing_evidence_not_fail(tmp_path):
    sc = _sc(Step("A", "登录", (Expect("F2F7F142"),)))
    v = judge(sc, {"A": tmp_path / "nope"}, tmp_path / "server")
    assert v.entries[0].result == "missing-evidence"


def test_scenario_without_any_expect_is_invalid(tmp_path):
    sc = _sc(Step("A", "do", ()))
    v = judge(sc, {"A": tmp_path}, tmp_path / "server")
    assert v.status() == "invalid"
    assert v.invalid_reason and "没有" in v.invalid_reason


def test_digest_drops_variable_detail_but_keeps_results():
    v = Verdict(scenario_id="s", entries=[
        VerdictEntry("A", "do", "d", "[存在]", "pass", detail="第 12 行命中"),
        VerdictEntry("B", "do2", "d2", "[语义]", "needs-human"),
    ])
    dg = digest(v)
    assert dg["scenario_id"] == "s" and dg["status"] == "green-with-human"
    assert dg["counts"]["pass"] == 1 and dg["counts"]["needs-human"] == 1
    assert all("detail" not in e for e in dg["entries"])
    assert [e["result"] for e in dg["entries"]] == ["pass", "needs-human"]


def test_write_verdict_writes_both_full_and_digest(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    v = Verdict(scenario_id="s", entries=[VerdictEntry("A", "do", "d", "[存在]", "pass")])
    full, dg = write_verdict(rp, v)
    assert full.name == "verdict.json" and dg.name == "verdict.digest.json"
    assert "detail" in full.read_text(encoding="utf-8")