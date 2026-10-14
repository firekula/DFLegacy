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
    # 场景另外要有一条**能失败**的期望才算有效（`[语义]` 永远不 fail，见 I-4）
    sc = _sc(Step("A", "走到 B 附近", (Expect("A 能看到 B", level="[语义]"),
                                       Expect("握手帧在场", pattern="hex=00"))))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "needs-human"
    assert v.entries[0].level == "[语义]"
    assert v.status() == "green-with-human"


def test_semantic_only_scenario_is_invalid_not_a_fake_green(tmp_path):
    """零可失败期望的场景必须判**无效**，不得报 "green-with-human" 的假绿（终审 I-4 实测 P3）。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "走到 B 附近", (Expect("A 能看到 B", level="[语义]"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.status() == "invalid"
    assert v.invalid_reason, "只有 [语义] 期望的场景必须有 invalid_reason"
    assert "能失败" in v.invalid_reason


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


# ===== 证据分类：launcher.log 是平台元数据，永不算证据（终审 I-2） =====

_LAUNCHER_LINE = "Started DFLegacy client process 12345;\n"  # 真实格式见 LaunchFlow.cs:41-43


def test_only_launcher_log_is_missing_evidence_not_fail(tmp_path):
    """一条命令路径下 actor 目录里**必有** launcher.log（cli.py:184 写的 PID 归因中间产物）。
    netdump 没归集到时必须判 missing-evidence（缺证据），而不是 fail（"证据在、但不符"）。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "launcher.log").write_text(_LAUNCHER_LINE, encoding="utf-8")
    for level in ("[存在]", "[字节]"):
        sc = _sc(Step("A", "登录", (Expect("c2s:login-ok", level=level,
                                          pattern="000B2B0000000000000001",
                                          evidence="[抓包]"),)))
        v = judge(sc, {"A": d}, tmp_path / "server")
        assert v.entries[0].result == "missing-evidence", (level, v.entries[0].detail)
        assert v.status() == "green-with-human", "缺证据不是红"


def test_pattern_inside_launcher_log_never_yields_pass(tmp_path):
    """反向通道也必须堵死：pattern 只出现在 launcher.log 里**不得**判 pass
    （否则平台自己的日志能把判定刷绿，detail 会写成 launcher.log:1）。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "launcher.log").write_text("hit 000B2B0000000000000001\n", encoding="utf-8")
    for evidence in ("[抓包]", None):
        sc = _sc(Step("A", "登录", (Expect("c2s:login-ok", pattern="000B2B0000000000000001",
                                          evidence=evidence),)))
        v = judge(sc, {"A": d}, tmp_path / "server")
        assert v.entries[0].result == "missing-evidence", (evidence, v.entries[0].detail)


def test_netdump_without_pattern_still_fails_with_launcher_log_present(tmp_path):
    """`fail` 的既有语义必须保住：真证据（netdump）在盘上、pattern 不在 ⇒ fail/red。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "launcher.log").write_text(_LAUNCHER_LINE, encoding="utf-8")
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("c2s", pattern="DEADBEEF", evidence="[抓包]"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "fail"
    assert v.status() == "red"


def test_capture_expects_are_searched_in_netdump_only(tmp_path):
    """`GREP_SOURCES` 必须是活的（不能是又一个"声明了没人用"的常量）：
    `[抓包]` 的来源是 netdump ⇒ pattern 出现在别的日志里也不算该证据 ⇒ missing-evidence。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "launcher.log").write_text(_LAUNCHER_LINE, encoding="utf-8")
    (d / "other-capture.log").write_text("000B2B0000000000000001\n", encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("c2s:login-ok", pattern="000B2B0000000000000001",
                                     evidence="[抓包]"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "missing-evidence", v.entries[0].detail


def test_undeclared_evidence_still_searches_every_evidence_log(tmp_path):
    """未声明证据类的期望（4 个骨架场景都是这种）不得被收窄：仍搜全部非平台元数据日志。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "launcher.log").write_text(_LAUNCHER_LINE, encoding="utf-8")
    (d / "client-stdout.log").write_text("hex=000B2B0000000000000001\n", encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("c2s:login-ok", pattern="000B2B0000000000000001"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "pass", v.entries[0].detail


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