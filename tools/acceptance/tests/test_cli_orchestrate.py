"""一条命令编排接线：用 FakeRunner 验证顺序与参数，不启动任何真实进程。"""
import json
from pathlib import Path
from types import SimpleNamespace

from acceptance import cli
from acceptance.actor_pid import PIDTimeoutError
from acceptance.cli import orchestrate
from acceptance.model import RunPaths
from acceptance.verdict import Verdict

SCEN = Path(__file__).resolve().parents[1] / "scenarios" / "town-two-clients.yaml"


class FakeRunner:
    """记录调用参数，按最小契约产出 run 目录与 manifest，不启动进程。"""

    instances: list["FakeRunner"] = []

    def __init__(self, workspace, acceptance_dir, runs_dir):
        self.workspace = Path(workspace)
        self.acceptance_dir = Path(acceptance_dir)
        self.runs_dir = Path(runs_dir)
        self.calls: list[tuple[str, object]] = []
        self.stopped = 0
        FakeRunner.instances.append(self)

    def prepare(self, scenario, git_sha):
        run_id = f"20261015T000000Z-{git_sha}-{scenario.id}"
        rp = RunPaths.for_run(self.runs_dir, run_id)
        rp.server_dir.mkdir(parents=True, exist_ok=True)
        for a in scenario.actors:
            rp.actor_dir(a.name).mkdir(parents=True, exist_ok=True)
        manifest = {
            "run_id": run_id, "git_sha": git_sha, "created_utc": "2026-10-15T00:00:00Z",
            "scenario_id": scenario.id, "expect_t0": scenario.expect_t0,
            "actors": [{"name": a.name, "driver": a.driver, "workdir": None}
                       for a in scenario.actors],
            "server": {}, "config_overrides": [],
        }
        rp.manifest.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
        self.calls.append(("prepare", run_id))
        self.last_rp = rp
        return rp, SimpleNamespace()

    def start_server(self, rp, exe, workdir, server_json=None, env=None, log_path=None, **kw):
        self.calls.append(("start_server", {"exe": Path(exe), "workdir": Path(workdir),
                                            "server_json": server_json, "env": env,
                                            "log_path": Path(log_path) if log_path else None}))
        return {"exe": Path(exe).name, "pid": 111, "workdir": str(workdir)}

    def record_run_config(self, rp, entries):
        self.calls.append(("record_run_config", entries))
        mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
        mf["config_overrides"] = entries
        rp.manifest.write_text(json.dumps(mf, ensure_ascii=False), encoding="utf-8")

    def start_actors(self, rp, scenario, launcher_cmd=None, actor_commands=None,
                     before_start=None, on_started=None):
        self.calls.append(("start_actors", {"actor_commands": dict(actor_commands or {})}))
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
        self.calls.append(("stop_all", None))


def _make_client_dir(tmp_path: Path, *, dump_network: str = "1") -> Path:
    client = tmp_path / "client"
    (client / "town-test" / "data").mkdir(parents=True, exist_ok=True)
    (client / "DNF.exe").write_bytes(b"stub")
    (client / "ijl15.dll").write_bytes(b"stub")
    (client / "Config.ini").write_text(f"[Features]\nDumpNetwork={dump_network}\n",
                                       encoding="utf-8")
    (client / "town-test" / "DFLegacy.Launcher.exe").write_bytes(b"stub")
    (client / "town-test" / "DFLegacy.Server.exe").write_bytes(b"stub")
    (client / "town-test" / "server.json").write_text('{"DFLegacy": {}}', encoding="utf-8")
    (client / "town-test" / "data" / "state.json").write_text('{"characters":[]}',
                                                              encoding="utf-8")
    (client / "town-test" / "launcher.log").write_text("", encoding="utf-8")
    return client


def _paths(client: Path) -> dict:
    return {"client_dir": client,
            "launcher_exe": client / "town-test" / "DFLegacy.Launcher.exe",
            "server_exe": client / "town-test" / "DFLegacy.Server.exe",
            "launcher_log": client / "town-test" / "launcher.log"}


def test_orchestrate_requires_all_actors(tmp_path, capsys):
    runs = tmp_path / "runs"
    rc = orchestrate(SCEN, runs, "abc1234", actor_accounts="A=only",
                     paths={"client_dir": tmp_path / "c", "launcher_exe": tmp_path / "L.exe",
                            "server_exe": tmp_path / "S.exe",
                            "launcher_log": tmp_path / "launcher.log"})
    assert rc == 2
    assert not runs.exists(), "参数不全时不应建 run 目录"
    assert "覆盖全部 actor" in capsys.readouterr().err


def test_orchestrate_rejects_client_dir_without_dumpnetwork(tmp_path, capsys, monkeypatch):
    def boom(*args, **kwargs):
        raise AssertionError("前置检查未通过时不应构造 Runner")

    monkeypatch.setattr(cli, "Runner", boom)
    client = _make_client_dir(tmp_path, dump_network="0")
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234", actor_accounts="A=x,B=y",
                     paths=_paths(client))
    assert rc == 2
    assert "DumpNetwork=1" in capsys.readouterr().err


def test_orchestrate_wires_server_actors_steps_and_collect(tmp_path, monkeypatch, capsys):
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    pids = iter([7001, 7002])
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (next(pids), "started-line", offset))
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    collected: list[tuple[str, Path | None]] = []
    monkeypatch.setattr(cli, "collect_actor",
                        lambda rp, name, source: collected.append((name, source)))
    monkeypatch.setattr(cli, "judge",
                        lambda sc, dirs, server_dir: Verdict(scenario_id=sc.id, entries=[]))
    monkeypatch.setattr(cli, "write_verdict", lambda rp, v: (rp.verdict, rp.verdict_digest))
    monkeypatch.setattr(cli, "promote_digest",
                        lambda rp, acc: Path(acc) / "verdicts" / f"{rp.run_dir.name}.json")

    (client / "netdump-7001.log").write_text(
        "2026-10-15 00:00:01.000 T=1 TX TCP 127.0.0.1:1 -> 127.0.0.1:2311 len=1 hex=00\n",
        encoding="utf-8")
    (client / "netdump-7002.log").write_text(
        "2026-10-15 00:00:02.000 T=2 TX TCP 127.0.0.1:1 -> 127.0.0.1:2311 len=1 hex=00\n",
        encoding="utf-8")

    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=alias-one,B=alias-two", paths=_paths(client))
    assert rc == 0
    runner = FakeRunner.instances[-1]
    rp = runner.last_rp
    calls = dict((name, payload) for name, payload in runner.calls)

    # 服务端：env 指向 run 私有存档、日志落 run 目录
    server = calls["start_server"]
    assert server["env"]["DFLegacy__DataPath"] == str(rp.server_dir / "state.json")
    assert server["log_path"] == rp.server_dir / "server.log"
    assert (rp.server_dir / "state.json").is_file(), "必须复制出 run 私有存档"
    assert (rp.server_dir / "server.json").is_file(), "必须留 server.json provenance 副本"
    assert json.loads(rp.manifest.read_text(encoding="utf-8"))["config_overrides"] == [
        {"key": "DFLegacy__DataPath", "old": "data/state.json",
         "new": str(rp.server_dir / "state.json")}]

    # actor：各自账号别名、按场景顺序 A→B、真实 PID 写回
    cmds = calls["start_actors"]["actor_commands"]
    assert list(cmds) == ["A", "B"]
    assert "--account" in cmds["A"] and "alias-one" in cmds["A"]
    assert "alias-two" in cmds["B"]

    # steps.json 记录全部场景步骤
    steps = json.loads((rp.run_dir / "steps.json").read_text(encoding="utf-8"))
    assert [s["index"] for s in steps] == [1, 2, 3]
    assert steps[0]["actor"] == "A" and steps[0]["at_utc"].endswith("Z")

    # 归集用的是真实 client_pid 推出的 netdump
    assert collected == [("A", client / "netdump-7001.log"),
                         ("B", client / "netdump-7002.log")]
    assert runner.stopped == 1, "必须停进程（finally）"
    assert (rp.run_dir / "offsets.json").is_file()
    assert "status" in capsys.readouterr().out


def test_orchestrate_stops_processes_when_pid_capture_fails(tmp_path, monkeypatch, capsys):
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (_ for _ in ()).throw(
                            PIDTimeoutError("no start line")))

    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=x,B=y", paths=_paths(client))
    assert rc == 1
    assert FakeRunner.instances[-1].stopped == 1, "PID 归因失败也要回收进程"
    assert "编排失败" in capsys.readouterr().err
