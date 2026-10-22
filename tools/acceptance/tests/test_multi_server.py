"""多服务端进程（game + channel）编排：逐进程 manifest、端点 disjoint、门场景、server_dir 真读。"""
import json
import socket
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

from acceptance import cli, model, runner
from acceptance.cli import orchestrate, _orchestration_paths
from acceptance.model import ActorSpec, Expect, RunPaths, Scenario, Step
from acceptance.runner import Runner
from acceptance.verdict import Verdict

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


def _free_port() -> int:
    """bind 0 让系统分配一个空闲端口，避开本机排除端口段（尤其 TCP 2311，bind 恒 WinError 10013）。"""
    s = socket.socket()
    try:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]
    finally:
        s.close()


def _split_server_payload(entrance: int, channel: int, link: int) -> dict:
    return {"DFLegacy": {
        "Entrance": {"Host": "127.0.0.1", "Port": entrance},
        "Channel": {"Host": "127.0.0.1", "Port": channel},
        "Processes": {"Channel": {"Enabled": True, "LinkPort": link}},
    }}


def _make_client_dir(tmp_path: Path, server_payload: dict) -> Path:
    """搭一条命令需要的最小客户端目录（exe 全用 stub，不启动真实游戏进程）。"""
    client = tmp_path / "client"
    town = client / "town-test"
    (town / "data").mkdir(parents=True, exist_ok=True)
    (client / "DNF.exe").write_bytes(b"stub")
    (client / "ijl15.dll").write_bytes(b"stub")
    (client / "Config.ini").write_text("[Features]\nDumpNetwork=1\n", encoding="utf-8")
    (town / "DFLegacy.Launcher.exe").write_bytes(b"stub")
    (town / "DFLegacy.Server.exe").write_bytes(b"stub")
    (town / "DFLegacy.Channel.exe").write_bytes(b"stub")
    (town / "server.json").write_text(json.dumps(server_payload, ensure_ascii=False),
                                      encoding="utf-8")
    (town / "data" / "state.json").write_text('{"characters":[]}', encoding="utf-8")
    (town / "launcher.log").write_text("", encoding="utf-8")
    return client


class OrchestrateFakeRunner:
    """记录 orchestrate 的 start_server 调用，并真起占位进程 + 写 manifest + 建逐进程日志。

    钉的是「拆分模式起 channel+game 双进程」的**接线**（orchestrate 以何顺序、何 exe、何日志调
    start_server）；真实 start_server 的逐进程 manifest/日志行为已由本文件其它用例单独钉住。
    """

    instances: list["OrchestrateFakeRunner"] = []

    def __init__(self, workspace, acceptance_dir, runs_dir):
        self.workspace = Path(workspace)
        self.acceptance_dir = Path(acceptance_dir)
        self.runs_dir = Path(runs_dir)
        self.started: list[tuple[str, Path, Path]] = []
        self.stopped = 0
        self._procs: list[subprocess.Popen] = []
        self._handles: list = []
        self.last_rp: RunPaths | None = None
        OrchestrateFakeRunner.instances.append(self)

    def prepare(self, scenario, git_sha):
        run_id = f"20261022T000000Z-{git_sha}-{scenario.id}"
        rp = RunPaths.for_run(self.runs_dir, run_id)
        rp.server_dir.mkdir(parents=True, exist_ok=True)
        for a in scenario.actors:
            rp.actor_dir(a.name).mkdir(parents=True, exist_ok=True)
        manifest = {
            "run_id": run_id, "git_sha": git_sha, "created_utc": "2026-10-22T00:00:00Z",
            "scenario_id": scenario.id, "expect_t0": scenario.expect_t0,
            "actors": [{"name": a.name, "driver": a.driver, "workdir": None}
                       for a in scenario.actors],
            "servers": {}, "config_overrides": [],
        }
        rp.manifest.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
        self.last_rp = rp
        return rp, SimpleNamespace()

    def start_server(self, rp, exe, workdir, name="game", server_json=None, env=None,
                     log_path=None, **kw):
        exe, log_path = Path(exe), Path(log_path)
        self.started.append((name, exe, log_path))
        log_path.parent.mkdir(parents=True, exist_ok=True)
        handle = log_path.open("wb")
        p = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(3600)"],
                             stdout=handle, stderr=subprocess.STDOUT)
        self._handles.append(handle)
        self._procs.append(p)
        mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
        mf["servers"][name] = {"exe": exe.name, "pid": p.pid, "workdir": str(workdir)}
        rp.manifest.write_text(json.dumps(mf, ensure_ascii=False), encoding="utf-8")
        return {"exe": exe.name, "pid": p.pid, "workdir": str(workdir)}

    def wait_until_listening(self, rp, scenario, server_json=None, timeout_s=None,
                             probe=None, on_tick=None):
        return {"skipped": False, "host": "127.0.0.1", "port": 0, "waited_ms": 1,
                "processes": [
                    {"name": "channel", "host": "127.0.0.1", "port": 0, "waited_ms": 1},
                    {"name": "game", "host": "127.0.0.1", "port": 0, "waited_ms": 1},
                ]}

    def record_run_config(self, rp, entries):
        mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
        mf["config_overrides"] = entries
        rp.manifest.write_text(json.dumps(mf, ensure_ascii=False), encoding="utf-8")

    def start_actors(self, rp, scenario, launcher_cmd=None, actor_commands=None,
                     before_start=None, on_started=None):
        recs = []
        for a in scenario.actors:
            if before_start is not None:
                before_start(a)
            client_pid = on_started(a, None) if on_started is not None else None
            rec = {"name": a.name, "driver": a.driver, "pid": 1000}
            if client_pid is not None:
                rec["client_pid"] = client_pid
            recs.append(rec)
        return recs

    def stop_all(self):
        self.stopped += 1
        for p in self._procs:
            if p.poll() is None:
                p.terminate()
                try:
                    p.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    p.kill()
        for h in self._handles:
            try:
                h.close()
            except Exception:
                pass


def test_orchestration_paths_derives_channel_exe(tmp_path):
    """`--channel-exe` 缺省取 `server_exe 同目录 / DFLegacy.Channel.exe`，显式给定时覆盖。"""
    client = tmp_path / "client"
    args = SimpleNamespace(client_dir=str(client), launcher_exe=None, server_exe=None,
                           channel_exe=None, launcher_log=None)
    paths = _orchestration_paths(args)
    assert paths.get("channel_exe") == paths["server_exe"].parent / "DFLegacy.Channel.exe"

    override = tmp_path / "custom" / "DFLegacy.Channel.exe"
    args2 = SimpleNamespace(client_dir=str(client), launcher_exe=None, server_exe=None,
                            channel_exe=str(override), launcher_log=None)
    assert _orchestration_paths(args2)["channel_exe"] == override


def test_orchestrate_split_mode_starts_channel_and_game(tmp_path, monkeypatch):
    """拆分模式（Processes.Channel.Enabled=true）的 orchestrate 先起 channel 再起 game：
    两个占位进程都真起、manifest.servers 里有 game 与 channel 两条、逐进程日志各一份。

    全程不 bind 2311：server.json 的端口用 bind 0 分到的空闲端口（避开本机排除段）。
    """
    client = _make_client_dir(tmp_path, _split_server_payload(_free_port(), _free_port(),
                                                              _free_port()))
    town = client / "town-test"
    paths = {"client_dir": client,
             "launcher_exe": town / "DFLegacy.Launcher.exe",
             "server_exe": town / "DFLegacy.Server.exe",
             "channel_exe": town / "DFLegacy.Channel.exe",
             "launcher_log": town / "launcher.log"}

    OrchestrateFakeRunner.instances.clear()
    monkeypatch.setattr(cli, "Runner", OrchestrateFakeRunner)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (7001, "started-line", offset))
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    monkeypatch.setattr(cli, "collect_actor", lambda rp, name, source: None)
    monkeypatch.setattr(cli, "judge",
                        lambda sc, dirs, server_dir: Verdict(scenario_id=sc.id, entries=[]))
    monkeypatch.setattr(cli, "write_verdict", lambda rp, v: (rp.verdict, rp.verdict_digest))
    monkeypatch.setattr(cli, "promote_digest",
                        lambda rp, acc: Path(acc) / "verdicts" / f"{rp.run_dir.name}.json")

    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234", actor_accounts="A=x", paths=paths)
    assert rc == 0
    fake = OrchestrateFakeRunner.instances[-1]
    rp = fake.last_rp

    # 顺序：channel 先、game 后；exe 与日志各按进程名
    assert [(name, exe.name) for name, exe, _ in fake.started] == [
        ("channel", "DFLegacy.Channel.exe"), ("game", "DFLegacy.Server.exe")]
    assert [log.name for _, _, log in fake.started] == ["server-channel.log",
                                                        "server-game.log"]
    assert [log.parent for _, _, log in fake.started] == [rp.server_dir, rp.server_dir]

    mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
    assert set(mf["servers"]) == {"channel", "game"}
    assert mf["servers"]["channel"]["pid"] != mf["servers"]["game"]["pid"]
    assert (rp.server_dir / "server-channel.log").is_file(), "channel 进程日志必须落在 run 目录"
    assert (rp.server_dir / "server-game.log").is_file(), "game 进程日志必须落在 run 目录"
    assert fake.stopped == 1, "必须回收进程（finally: stop_all）"
