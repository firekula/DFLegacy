"""CLI 干跑：不启动任何游戏进程，验证 run→collect→judge→promote 链路。"""
import datetime as dt
import json
from pathlib import Path

import pytest

from acceptance.cli import dry_run, main, promote_digest
from acceptance.model import load_scenario

SCEN = Path(__file__).resolve().parents[1] / "scenarios" / "town-two-clients.yaml"


def test_all_scenarios_parse_and_have_falsifiable_expects():
    """每个场景都必须能被解析，且至少有一条**能失败**的期望（否则判定恒真）。

    新增场景要同步这份清单 —— 这是有意的：场景集变了，必须有人看一眼。
    """
    base = Path(__file__).resolve().parents[1] / "scenarios"
    ids = sorted(p.stem for p in base.glob("*.yaml"))
    assert ids == ["handshake-login-town", "party-dungeon", "party-invite",
                   "town-position-sync", "town-two-clients", "trade-open"]
    for p in sorted(base.glob("*.yaml")):
        sc = load_scenario(p)
        assert sc.has_falsifiable_expect(), f"{sc.id} 没有任何能失败的期望 ⇒ 判定会无效"


def test_dry_run_produces_missing_evidence_not_a_fake_pass(tmp_path):
    sc = load_scenario(SCEN)
    rp, v = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    assert rp.run_dir.exists() and rp.manifest.exists()
    c = v.counts()
    # 干跑没有任何产物 ⇒ 只能出 missing-evidence / needs-human，**不许**出现 pass
    assert c["pass"] == 0
    assert c["missing-evidence"] > 0
    assert v.status() in ("green-with-human",)


def test_run_twice_yields_identical_digest(tmp_path):
    _, v1 = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    _, v2 = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    from acceptance.verdict import digest
    assert digest(v1) == digest(v2), "同一场景两次干跑的归一化判定必须一致"


def test_main_replay_is_read_only_and_returns_zero(tmp_path, capsys):
    rp, _ = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    rc = main(["replay", "--run-dir", str(rp.run_dir)])
    assert rc == 0
    out = capsys.readouterr().out
    assert "status" in out


def test_promote_digest_copies_only_the_normalized_judgement(tmp_path):
    rp, _ = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    acc = tmp_path / "acc"
    dest = promote_digest(rp, acc)
    assert dest == acc / "verdicts" / "town-two-clients.json"
    payload = json.loads(dest.read_text(encoding="utf-8"))
    assert set(payload) == {"scenario_id", "status", "invalid_reason", "counts", "entries"}
    assert all("detail" not in e for e in payload["entries"])


def test_main_run_dry_run_does_not_start_any_process(tmp_path, monkeypatch):
    from acceptance import cli

    monkeypatch.setattr(cli, "_git_sha", lambda _: "abc1234")

    def forbid_process(*args, **kwargs):
        raise AssertionError("--dry-run 不得启动任何进程")

    monkeypatch.setattr(cli.subprocess, "Popen", forbid_process)
    rc = main(["run", "--scenario", str(SCEN), "--actors", "2",
               "--dry-run", "--runs-dir", str(tmp_path / "runs")])
    assert rc == 0


# ===== 授权新增（controller ruling）：--collect 归集链路端到端（超出简报的 6 个测试） =====

def _prepare_run(tmp_path):
    """建一个带 manifest 的 run，返回 (rp, runs_dir, client_dir)。"""
    from acceptance.runner import Runner
    sc = load_scenario(SCEN)
    runs = tmp_path / "runs"
    r = Runner(workspace=runs.parent, acceptance_dir=tmp_path / "acc", runs_dir=runs)
    rp, _ = r.prepare(sc, git_sha="abc1234")
    client = tmp_path / "client"
    client.mkdir(parents=True, exist_ok=True)
    return rp, runs, client


def test_run_collect_wires_netdumps_offsets_and_verdict(tmp_path, monkeypatch):
    """--collect 必须真归集：netdump 进 actor 目录、offsets.json、判定产物齐（授权新增）。"""
    from acceptance import cli
    monkeypatch.setattr(cli, "HERE", tmp_path / "acc")  # 判定提升写进 tmp，不污染仓库
    rp, runs, client = _prepare_run(tmp_path)
    (client / "netdump-111.log").write_text(
        "2026-10-05 00:47:29.392 T=75376 TX TCP-CONNECT - -> 127.0.0.1:2311 len=11 hex=000B2B0000000000000001\n"
        "2026-10-05 00:47:29.392 T=75376 RX TCP-CONNECT - -> 127.0.0.1:2311 len=11 hex=000C2F0000000000000001\n",
        encoding="utf-8")
    (client / "netdump-222.log").write_text(
        "2026-10-05 00:47:30.392 T=75377 TX TCP-CONNECT - -> 127.0.0.1:2311 len=11 hex=000B2B0000000000000001\n",
        encoding="utf-8")
    rc = main(["run", "--scenario", str(SCEN), "--collect",
               "--run-id", rp.run_dir.name,
               "--client-dir", str(client),
               "--actor-pid", "A=111,B=222",
               "--runs-dir", str(runs)])
    assert rc == 0
    assert (rp.actor_dir("A") / "netdump-111.log").exists()
    assert (rp.actor_dir("B") / "netdump-222.log").exists()
    offsets = json.loads((rp.run_dir / "offsets.json").read_text(encoding="utf-8"))
    assert set(offsets) == {"A", "B"}
    assert offsets["A"] is not None and offsets["B"] is not None
    assert rp.verdict.exists() and rp.verdict_digest.exists()
    v = json.loads(rp.verdict.read_text(encoding="utf-8"))
    # 收进去的日志必须能被判定搜到 ⇒ 3 个 [存在] 期望 pass（否则归集环节没接上）
    assert v["counts"]["pass"] == 3
    assert v["counts"]["missing-evidence"] == 0


def test_run_collect_missing_netdump_is_missing_evidence_not_crash(tmp_path, monkeypatch):
    """授权新增：某 actor 的 netdump 缺失 ⇒ 不崩、该 actor 判 missing-evidence、偏移 None（不许默认 0）。"""
    from acceptance import cli
    monkeypatch.setattr(cli, "HERE", tmp_path / "acc")
    rp, runs, client = _prepare_run(tmp_path)
    (client / "netdump-111.log").write_text(
        "2026-10-05 00:47:29.392 T=75376 TX TCP-CONNECT - -> 127.0.0.1:2311 len=11 hex=000B2B0000000000000001\n"
        "2026-10-05 00:47:29.392 T=75376 RX TCP-CONNECT - -> 127.0.0.1:2311 len=11 hex=000C2F0000000000000001\n",
        encoding="utf-8")
    # B 的 netdump-222.log 故意不存在
    rc = main(["run", "--scenario", str(SCEN), "--collect",
               "--run-id", rp.run_dir.name,
               "--client-dir", str(client),
               "--actor-pid", "A=111,B=222",
               "--runs-dir", str(runs)])
    assert rc == 0
    v = json.loads(rp.verdict.read_text(encoding="utf-8"))
    b_results = [e["result"] for e in v["entries"] if e["actor"] == "B"]
    assert b_results and all(x == "missing-evidence" for x in b_results)
    assert v["counts"]["fail"] == 0
    a_results = [e["result"] for e in v["entries"] if e["actor"] == "A"]
    assert "pass" in a_results
    offsets = json.loads((rp.run_dir / "offsets.json").read_text(encoding="utf-8"))
    assert offsets["B"] is None  # 缺 t0 ⇒ None，不是 0


def test_run_collect_without_run_id_returns_rc2(tmp_path, capsys):
    runs = tmp_path / "runs"
    rc = main(["run", "--scenario", str(SCEN), "--collect",
               "--client-dir", str(tmp_path), "--actor-pid", "A=111,B=222",
               "--runs-dir", str(runs)])
    assert rc == 2
    assert "--run-id" in capsys.readouterr().err


def test_run_collect_missing_explicit_server_log_rejects_before_copy(tmp_path, monkeypatch, capsys):
    from acceptance import cli

    monkeypatch.setattr(cli, "HERE", tmp_path / "acc")
    rp, runs, client = _prepare_run(tmp_path)
    (client / "netdump-111.log").write_text("TX c2s:login-ok\n", encoding="utf-8")
    rc = main(["run", "--scenario", str(SCEN), "--collect",
               "--run-id", rp.run_dir.name,
               "--client-dir", str(client), "--actor-pid", "A=111,B=222",
               "--server-log", str(tmp_path / "does-not-exist.log"),
               "--runs-dir", str(runs)])
    assert rc == 2
    assert "--server-log" in capsys.readouterr().err
    assert not (rp.actor_dir("A") / "netdump-111.log").exists()
    assert not rp.verdict.exists()


# ===== 真机 netdump 时间格式/时区（bugfix） =====

def test_run_collect_offsets_convert_local_time_to_utc(tmp_path, monkeypatch):
    """真机格式（无方括号）+ 本地时间→UTC：offsets 的 actor t0 须按本机时区换算成 UTC 再与
    server_t0 相减，**禁止** replace(tzinfo=UTC) 把本地时间冒充 UTC。"""
    from acceptance import cli
    monkeypatch.setattr(cli, "HERE", tmp_path / "acc")
    rp, runs, client = _prepare_run(tmp_path)
    # 逐字取自真机 netdump-53916.log / netdump-49916.log 的首条数据行（本地时间）
    (client / "netdump-111.log").write_text(
        "2026-10-05 00:47:29.392 T=75376 TX TCP-CONNECT - -> 127.0.0.1:2311 len=10 hex=636F6E6E6563743D6F6B\n",
        encoding="utf-8")
    (client / "netdump-222.log").write_text(
        "2026-10-04 19:47:58.873 T=52404 TX TCP-CONNECT - -> 127.0.0.1:2311 len=10 hex=636F6E6E6563743D6F6B\n",
        encoding="utf-8")
    rc = main(["run", "--scenario", str(SCEN), "--collect",
               "--run-id", rp.run_dir.name,
               "--client-dir", str(client),
               "--actor-pid", "A=111,B=222",
               "--runs-dir", str(runs)])
    assert rc == 0
    offsets = json.loads((rp.run_dir / "offsets.json").read_text(encoding="utf-8"))
    server_t0 = dt.datetime.strptime(
        json.loads(rp.manifest.read_text(encoding="utf-8"))["created_utc"],
        "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=dt.timezone.utc)
    a_t0_utc = dt.datetime(2026, 10, 5, 0, 47, 29, 392000).astimezone(dt.timezone.utc)
    assert offsets["A"] == pytest.approx((server_t0 - a_t0_utc).total_seconds())
    b_t0_utc = dt.datetime(2026, 10, 4, 19, 47, 58, 873000).astimezone(dt.timezone.utc)
    assert offsets["B"] == pytest.approx((server_t0 - b_t0_utc).total_seconds())