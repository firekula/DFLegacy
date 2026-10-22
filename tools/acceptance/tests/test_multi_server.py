"""多服务端进程（game + channel）编排：逐进程 manifest、端点 disjoint、门场景、server_dir 真读。"""
import json
import sys
from pathlib import Path

from acceptance import model, runner
from acceptance.model import ActorSpec, Expect, Scenario, Step
from acceptance.runner import Runner

SCEN = Path(__file__).resolve().parents[1] / "scenarios" / "handshake-login-town.yaml"

_FAKE_SERVER_ARGS = ["-c", "import time; time.sleep(3600)"]


def _scenario():
    return Scenario(id="s", goal="g", expect_t0="server-accept",
                    actors=(ActorSpec("A", "synthetic"),), steps=())


def _write_server_json(path, payload):
    path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    return path


def test_manifest_keeps_one_entry_per_process(tmp_path, monkeypatch):
    """start_server 按进程名写 `servers[name]`：第二次启动不覆写第一次，旧单数 `server` 键消失。"""
    monkeypatch.setattr(runner, "preflight_ports", lambda endpoints, probe=None: None)
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    rp, _ = r.prepare(_scenario(), git_sha="abc1234")
    sj = _write_server_json(tmp_path / "server.json", {"DFLegacy": {
        "Entrance": {"Host": "127.0.0.1", "Port": 25311},
        "Channel": {"Host": "127.0.0.1", "Port": 25312},
    }})
    try:
        r.start_server(rp, exe=Path(sys.executable), workdir=tmp_path, server_json=sj,
                       name="channel", server_args=_FAKE_SERVER_ARGS)
        r.start_server(rp, exe=Path(sys.executable), workdir=tmp_path, server_json=sj,
                       name="game", server_args=_FAKE_SERVER_ARGS)
        mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
        assert set(mf["servers"]) == {"game", "channel"}
        assert "server" not in mf, "旧的单数 server 键必须消失，否则两处记法会分叉"
        assert mf["servers"]["channel"]["pid"] != mf["servers"]["game"]["pid"]
    finally:
        r.stop_all()


def test_server_process_specs_are_disjoint_by_host_port_protocol(tmp_path):
    """disjoint 判据是 (host, port, protocol)：2311 在 channel 是 TCP、game 是 UDP，合法共存，
    只按端口号查重会误报冲突（草案的 test_server_specs_declare_disjoint_ports 就踩了这个）。"""
    sj = _write_server_json(tmp_path / "server.json", {"DFLegacy": {
        "Entrance": {"Host": "127.0.0.1", "Port": 2311, "AdditionalPorts": [7001, 8080]},
        "Channel": {"Host": "127.0.0.1", "Port": 7001},
        "CharacterDatagram": {"Enabled": True, "Host": "127.0.0.1", "Port": 2311},
        "GameplayDatagram": {"Host": "127.0.0.1", "PrimaryPort": 7002, "SecondaryPort": 7003},
        "Processes": {"Channel": {"Enabled": True, "LinkPort": 7004}},
    }})
    specs = runner.server_process_specs(sj)
    seen = {}
    for name, eps in specs.items():
        for ep in eps:
            key = (ep.host, ep.port, ep.protocol)
            assert key not in seen, f"{name} 与 {seen[key]} 都声明 {key}"
            seen[key] = name
    keys = set(seen)
    assert ("127.0.0.1", 2311, "tcp") in keys and ("127.0.0.1", 2311, "udp") in keys, \
        "2311 必须同时以 TCP(channel) 与 UDP(game) 出现，只按端口号查重会误报冲突"
    assert seen[("127.0.0.1", 7001, "tcp")] == "game", "Channel.Port 归 game"
    link = [k for k, owner in seen.items() if k[1] == 7004 and k[2] == "tcp"]
    assert link and all(seen[k] == "channel" for k in link), "Processes.Channel.LinkPort 归 channel"


def test_gate_scenario_declares_a_falsifiable_expect():
    """本刀门场景必须至少有一条能失败的期望（防恒真判定），且含服务端侧 [静态] 期望。"""
    sc = model.load_scenario(SCEN)
    assert sc.has_falsifiable_expect(), "本刀的门场景必须至少有一条能失败的期望"
    assert any(ex.evidence == "[静态]" for s in sc.steps for ex in s.expects), \
        "门场景必须含服务端侧期望（evidence=[静态]）"


def test_judge_reads_server_dir_for_static_server_expect(tmp_path):
    """server_dir 必须真的被读：evidence=[静态] 的服务端侧期望从 server-*.log 搜 pattern，
    而不是去 actor 目录的 netdump 里找。"""
    from acceptance.verdict import judge
    d = tmp_path / "actor-A"
    d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    server_dir = tmp_path / "server"
    server_dir.mkdir()
    (server_dir / "server-game.log").write_text(
        "Entrance listener ready at 127.0.0.1:2311\n", encoding="utf-8")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "real-client"),),
                  steps=(Step("A", "握手", (Expect("服务端入口就绪", pattern="Entrance listener ready",
                                                   evidence="[静态]"),)),))
    v = judge(sc, {"A": d}, server_dir)
    assert v.entries[0].result == "pass", v.entries[0].detail
    assert v.status() == "green"
