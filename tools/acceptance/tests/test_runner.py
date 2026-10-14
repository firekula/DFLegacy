"""runner：建 run / 状态隔离 / 端口预检 / 配置改动计划（全部用假 actor，不启动游戏）。"""
import json
import socket
import threading

import pytest

from acceptance.model import ActorSpec, Scenario, Step, Expect, RunPaths
from acceptance.runner import (PortBusy, Runner, apply_config_overrides, isolate_state,
                               parse_server_endpoints, plan_config_overrides,
                               preflight_ports)

SERVER_JSON = {
    "DFLegacy": {
        "Admin": {"//Host": "注释不该被当值", "Host": "::", "Port": 8081},
        "Entrance": {"Host": "127.0.0.1", "Port": 2311},
        "Channel": {"Port": 7001},
        "GameplayDatagram": {"AdvertisedHost": "192.168.3.177", "Port": 7002},
    }
}


def _write_server_json(p):
    p.write_text(json.dumps(SERVER_JSON, ensure_ascii=False), encoding="utf-8")
    return p


def test_parse_server_endpoints_skips_comment_keys(tmp_path):
    sj = _write_server_json(tmp_path / "server.json")
    eps = parse_server_endpoints(sj)
    assert eps["Admin"] == ("::", 8081)
    assert eps["Entrance"] == ("127.0.0.1", 2311)
    assert not any("//" in k for k in eps)


def test_preflight_ports_raises_with_offender_named():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.listen(1)
    try:
        with pytest.raises(PortBusy) as ei:
            preflight_ports({"Entrance": ("127.0.0.1", port)})
        assert "Entrance" in str(ei.value) and str(port) in str(ei.value)
    finally:
        s.close()


def test_preflight_ports_passes_when_free():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    preflight_ports({"Entrance": ("127.0.0.1", port)})  # 不抛


def test_isolate_state_copies_and_never_points_at_the_live_save(tmp_path):
    live = tmp_path / "data" / "state.json"
    live.parent.mkdir(parents=True)
    live.write_text('{"characters":[]}', encoding="utf-8")
    dest = tmp_path / "run" / "server"
    dest.mkdir(parents=True)
    out = isolate_state(live, dest)
    assert out == dest / "state.json"
    assert out.read_text(encoding="utf-8") == '{"characters":[]}'
    out.write_text('{"characters":[1]}', encoding="utf-8")
    assert live.read_text(encoding="utf-8") == '{"characters":[]}', "活存档必须没被改"


def test_plan_config_overrides_reports_old_and_new_without_writing(tmp_path):
    sj = _write_server_json(tmp_path / "server.json")
    before = sj.read_text(encoding="utf-8")
    plan = plan_config_overrides(sj, {"GameplayDatagram.AdvertisedHost": "127.0.0.1"})
    assert plan == [{"key": "GameplayDatagram.AdvertisedHost",
                     "old": "192.168.3.177", "new": "127.0.0.1"}]
    assert sj.read_text(encoding="utf-8") == before, "plan 阶段不得写盘"


def test_apply_config_overrides_changes_only_target_value_and_preserves_bytes(tmp_path):
    """Ruling 3：apply 是字节级定向替换 —— 只有目标值变，`//` 注释键与其余字节原样保留。"""
    sj = tmp_path / "server.json"
    text = ('{\n'
            '  "DFLegacy": {\n'
            '    "Admin": { "//Host": "注释不该被当值", "Host": "::", "Port": 8081 },\n'
            '    "GameplayDatagram": { "AdvertisedHost": "192.168.3.177", "Port": 7002 }\n'
            '  }\n'
            '}\n')
    sj.write_text(text, encoding="utf-8")
    plan = plan_config_overrides(sj, {"GameplayDatagram.AdvertisedHost": "127.0.0.1"})
    apply_config_overrides(sj, plan)
    after = sj.read_text(encoding="utf-8")
    assert '"//Host": "注释不该被当值"' in after, "// 注释键必须原样保留"
    assert after == text.replace('"AdvertisedHost": "192.168.3.177"',
                                '"AdvertisedHost": "127.0.0.1"'), "除目标值外的字节必须原样保留"


def test_runner_prepare_creates_addressed_run_dir(tmp_path):
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=(Step("A", "do", (Expect("x"),)),))
    rp, mf = r.prepare(sc, git_sha="abc1234")
    assert rp.run_dir.parent == tmp_path / "runs"
    assert rp.run_dir.name.startswith("20") and "abc1234" in rp.run_dir.name and rp.run_dir.name.endswith("-s")
    assert mf.scenario_id == "s" and mf.git_sha == "abc1234"
    assert rp.manifest.exists(), "prepare 必须落 manifest.json"
    assert [a["name"] for a in mf.actors] == ["A"]


def test_runner_starts_and_stops_a_fake_actor_and_records_pid(tmp_path):
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=(Step("A", "do", (Expect("x"),)),))
    rp, _ = r.prepare(sc, git_sha="abc1234")
    recs = r.start_actors(rp, sc, launcher_cmd=["cmd", "/c", "ping -n 30 127.0.0.1"])
    try:
        assert len(recs) == 1
        assert recs[0]["name"] == "A" and isinstance(recs[0]["pid"], int)
        assert rp.actor_dir("A").exists()
    finally:
        r.stop_all()
    assert all(p.poll() is not None for p in r._procs), "stop_all 必须真的结束子进程"