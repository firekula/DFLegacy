"""一条命令编排接线：用 FakeRunner 验证顺序与参数，不启动任何真实进程。"""
import json
import socket
from pathlib import Path
from types import SimpleNamespace

import pytest

from acceptance import cli
from acceptance.actor_pid import AmbiguousPIDError, PIDTimeoutError
from acceptance.cli import orchestrate
from acceptance.model import RunPaths
from acceptance.runner import ServerStartupError
from acceptance.verdict import Verdict

SCEN = Path(__file__).resolve().parents[1] / "scenarios" / "town-two-clients.yaml"
_PORT_RESERVATIONS: dict[int, socket.socket] = {}


@pytest.fixture(autouse=True)
def _release_test_ports():
    yield
    for listener in _PORT_RESERVATIONS.values():
        listener.close()
    _PORT_RESERVATIONS.clear()


def _reserve_tcp_port() -> int:
    listener = socket.socket()
    try:
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        listener.bind(("127.0.0.1", 0))
    except BaseException:
        listener.close()
        raise
    port = listener.getsockname()[1]
    # Keep the OS-assigned port reserved until FakeRunner starts listening;
    # closing and rebinding would let a parallel test claim it in between.
    _PORT_RESERVATIONS[port] = listener
    return port


def _client_entrance_port(client: Path) -> int:
    config = json.loads((client / "town-test" / "server.json").read_text(encoding="utf-8"))
    return config["DFLegacy"]["Entrance"]["Port"]


class FakeRunner:
    """记录调用参数，按最小契约产出 run 目录与 manifest，不启动进程。"""

    instances: list["FakeRunner"] = []

    def __init__(self, workspace, acceptance_dir, runs_dir):
        self.workspace = Path(workspace)
        self.acceptance_dir = Path(acceptance_dir)
        self.runs_dir = Path(runs_dir)
        self.calls: list[tuple[str, object]] = []
        self.stopped = 0
        self.raise_on_wait = False
        self._listener = None
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
            "servers": {}, "config_overrides": [],
        }
        rp.manifest.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
        self.calls.append(("prepare", run_id))
        self.last_rp = rp
        return rp, SimpleNamespace()

    def start_server(self, rp, exe, workdir, server_json=None, env=None, log_path=None,
                     listen_timeout_s=None, **kw):
        self.calls.append(("start_server", {"exe": Path(exe), "workdir": Path(workdir),
                                            "server_json": server_json, "env": env,
                                            "log_path": Path(log_path) if log_path else None,
                                            "listen_timeout_s": listen_timeout_s}))
        self.listen_timeout_s = listen_timeout_s
        return {"exe": Path(exe).name, "pid": 111, "workdir": str(workdir)}

    def wait_until_listening(self, rp, scenario, server_json=None, timeout_s=None,
                             fake_server_pid=None, probe=None, on_tick=None):
        """模拟"服务端已开始监听"：使用 fixture 已由 OS 分配并保留的临时端口。

        固定游戏端口可能被Windows保留；这里验证参数接线，不要求占用真实游戏端口。
        真实监听／就绪探测由test_runner的回环测试覆盖。

        端口从 `server.json` 解析（即真实实现该走的路），并回报给测试断言。
        `ServerStartupError.raise_on_wait` 置位时改为抛出，用于测"永不监听"的失败路径。
        """
        from acceptance.runner import parse_server_endpoints
        sj = Path(server_json) if server_json else Path.cwd() / "server.json"
        endpoints = parse_server_endpoints(sj)
        self.waiting_for = endpoints["Entrance"]
        port = endpoints["Entrance"].port
        self.calls.append(("wait_until_listening", port))
        if self.raise_on_wait:
            raise ServerStartupError(f"服务端在 60.0s 内没有在 127.0.0.1:{port} 开始监听")
        if port:
            self._listener = _PORT_RESERVATIONS[port]
            self._listener.listen(1)
        return {"skipped": False, "host": "127.0.0.1", "port": port, "waited_ms": 1}

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
        listener = getattr(self, "_listener", None)
        if listener is not None:
            listener.close()
            self._listener = None
        self.calls.append(("stop_all", None))


def _make_client_dir(tmp_path: Path, *, dump_network: str = "1") -> Path:
    port = _reserve_tcp_port()
    client = tmp_path / "client"
    (client / "town-test" / "data").mkdir(parents=True, exist_ok=True)
    (client / "DNF.exe").write_bytes(b"stub")
    (client / "ijl15.dll").write_bytes(b"stub")
    (client / "Config.ini").write_text(f"[Features]\nDumpNetwork={dump_network}\n",
                                       encoding="utf-8")
    (client / "town-test" / "DFLegacy.Launcher.exe").write_bytes(b"stub")
    (client / "town-test" / "DFLegacy.Server.exe").write_bytes(b"stub")
    (client / "town-test" / "server.json").write_text(
        json.dumps({"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": port}}}), encoding="utf-8")
    (client / "town-test" / "data" / "state.json").write_text('{"characters":[]}',
                                                              encoding="utf-8")
    (client / "town-test" / "launcher.log").write_text("", encoding="utf-8")
    return client


def _paths(client: Path) -> dict:
    return {"client_dir": client,
            "launcher_exe": client / "town-test" / "DFLegacy.Launcher.exe",
            "server_exe": client / "town-test" / "DFLegacy.Server.exe",
            "channel_exe": client / "town-test" / "DFLegacy.Channel.exe",
            "launcher_log": client / "town-test" / "launcher.log"}


def test_orchestrate_rejects_actor_count_mismatch(tmp_path, capsys, monkeypatch):
    def boom(*args, **kwargs):
        raise AssertionError("--actors 不符时不应构造 Runner")

    monkeypatch.setattr(cli, "Runner", boom)
    client = _make_client_dir(tmp_path)
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234", actor_accounts="A=x,B=y",
                     paths=_paths(client), expected_actors=3)
    assert rc == 2
    assert "--actors 3" in capsys.readouterr().err
    assert not (tmp_path / "runs").exists()


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


def test_orchestrate_rejects_duplicate_account_aliases_before_starting_anything(tmp_path, capsys,
                                                                                monkeypatch):
    """`A=shizuku,B=shizuku` ⇒ 启动任何进程（也不建 run 目录）之前就报错 rc=2：
    账号名与 actor 必须一一对应且不同（one-command-amendment.md:22）。"""
    def boom(*args, **kwargs):
        raise AssertionError("账号重复时不应构造 Runner，更不应启动进程")

    monkeypatch.setattr(cli, "Runner", boom)
    client = _make_client_dir(tmp_path)
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=shizuku,B=shizuku", paths=_paths(client))
    assert rc == 2
    assert "账号" in capsys.readouterr().err
    assert not (tmp_path / "runs").exists(), "参数校验失败不应建 run 目录"


def test_orchestrate_wires_server_actors_steps_and_collect(tmp_path, monkeypatch, capsys):
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    port = _client_entrance_port(client)
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
        f"2026-10-15 00:00:01.000 T=1 TX TCP 127.0.0.1:1 -> 127.0.0.1:{port} len=1 hex=00\n",
        encoding="utf-8")
    (client / "netdump-7002.log").write_text(
        f"2026-10-15 00:00:02.000 T=2 TX TCP 127.0.0.1:1 -> 127.0.0.1:{port} len=1 hex=00\n",
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
    assert server["log_path"] == rp.server_dir / "server-game.log"
    assert (rp.server_dir / "state.json").is_file(), "必须复制出 run 私有存档"
    assert (rp.server_dir / "server.json").is_file(), "必须留 server.json provenance 副本"
    assert json.loads(rp.manifest.read_text(encoding="utf-8"))["config_overrides"] == [
        {"key": "DFLegacy__DataPath", "old": str((client / "town-test/data/state.json").resolve()),
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


def test_orchestrate_waits_for_the_server_to_listen_before_starting_actors(tmp_path, monkeypatch,
                                                                          capsys):
    """真机 2026-10-15 缺陷回归：一条命令必须**等服务端开始监听**再起 actor。

    实测服务端从启动到监听 2311 约 5.4 秒（索引 48343 个脚本 + 各 catalog），而 launcher 的
    `EnsureServerReadyAsync` 在 `StartServer=false` 时**只探一次端口就抛错**
    （src/DFLegacy.Launcher/Program.cs:336-347）⇒ 不等待就必然报
    "编排失败：Launcher failed ... 入口服务 127.0.0.1:2311 未监听，且 StartServer=false"。
    """
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (7001 + offset % 7, "fake start line", 0))
    # 绝不能把假判定提升进 git 跟踪的 verdicts/（真机判定才允许落那里）
    monkeypatch.setattr(cli, "promote_digest",
                        lambda rp, acc: Path(acc) / "verdicts" / f"{rp.run_dir.name}.json")
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=x,B=y", paths=_paths(client))
    assert rc == 0, "等待是成功路径的一部分，不应失败"
    names = [name for name, _ in FakeRunner.instances[-1].calls]
    assert names.index("start_server") < names.index("wait_until_listening") < names.index("start_actors"), \
        f"顺序必须是 起服务端 → 等监听 → 起 actor，实际 {names}"
    port = _client_entrance_port(client)
    assert f"等待服务端在 127.0.0.1:{port} 开始监听" in capsys.readouterr().out


def test_orchestrate_waits_for_the_entrance_port_declared_in_server_json(tmp_path, monkeypatch):
    """回归钉子：等待的目标端口必须从 `server.json` 的 `Entrance.Port` 解析得来。

    把端口改成非 2311 的值：等待若写死 2311，这里就抓不到（非恒真）。
    """
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    port = _reserve_tcp_port()
    (client / "town-test" / "server.json").write_text(
        json.dumps({"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": port}}}),
        encoding="utf-8")
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (7101 + offset % 7, "fake start line", 0))
    monkeypatch.setattr(cli, "promote_digest",
                        lambda rp, acc: Path(acc) / "verdicts" / f"{rp.run_dir.name}.json")
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=x,B=y", paths=_paths(client))
    assert rc == 0
    port_arg = dict(FakeRunner.instances[-1].calls)["wait_until_listening"]
    assert port_arg == port, f"等待的端口应取自 server.json 的 Entrance.Port，得到 {port_arg}"


def test_orchestrate_reports_server_that_never_listens_as_clear_failure(tmp_path, monkeypatch,
                                                                       capsys):
    """服务端起得来但永不监听 ⇒ 走友好失败路径（rc=1 + "编排失败"），不是 traceback。"""
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    original_init = FakeRunner.__init__

    def init_raising(self, *args, **kwargs):
        original_init(self, *args, **kwargs)
        self.raise_on_wait = True

    monkeypatch.setattr(FakeRunner, "__init__", init_raising)
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=x,B=y", paths=_paths(client))
    assert rc == 1
    err = capsys.readouterr().err
    port = _client_entrance_port(client)
    assert "编排失败" in err and f"没有在 127.0.0.1:{port} 开始监听" in err, err
    assert FakeRunner.instances[-1].stopped == 1, "失败也要回收已启动的服务端"


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
    err = capsys.readouterr().err
    assert "编排失败" in err and "no start line" in err, err


def test_orchestrate_reports_ambiguous_pid_as_clear_failure_not_traceback(tmp_path, monkeypatch, capsys):
    """launcher.log 本次新增**多条**启动行 ⇒ 必须走既有友好失败路径（"编排失败：…" + rc=1），
    不得让 AmbiguousPIDError 逃逸成 traceback（终审 I-3；账本 L290：拿不到唯一 PID 就明确失败、绝不猜）。"""
    FakeRunner.instances.clear()
    client = _make_client_dir(tmp_path)
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    monkeypatch.setattr(cli, "capture_client_pid",
                        lambda log, offset, timeout_s=60.0: (_ for _ in ()).throw(
                            AmbiguousPIDError("Found 2 start events in launcher.log")))
    rc = orchestrate(SCEN, tmp_path / "runs", "abc1234",
                     actor_accounts="A=x,B=y", paths=_paths(client))
    assert rc == 1
    err = capsys.readouterr().err
    assert "编排失败" in err, err
    assert "Found 2 start events" in err, err
    assert FakeRunner.instances[-1].stopped == 1, "PID 归因失败也要回收进程（finally: stop_all）"


def test_orchestrate_carries_explicit_closed_seed_and_records_exact_source(tmp_path, monkeypatch):
    client = _make_client_dir(tmp_path)
    prior = tmp_path / "closed-private.json"
    prior.write_text('{"Skills":[{"Slot":4,"SkillId":3}]}', encoding="utf-8")
    raw = prior.read_bytes()
    monkeypatch.setattr(cli, "Runner", FakeRunner)
    monkeypatch.setattr(FakeRunner, "wait_until_listening", lambda self, *a, **kw: {"skipped": False, "host": "127.0.0.1", "port": 1, "waited_ms": 0})
    monkeypatch.setattr(cli, "_confirm", lambda message: None)
    pids = iter([7701, 7702])
    monkeypatch.setattr(cli, "capture_client_pid", lambda *a, **kw: (next(pids), "fake", 0))
    monkeypatch.setattr(cli, "promote_digest", lambda *a: None)
    rc = orchestrate(SCEN, tmp_path / "runs", "seed123", actor_accounts="A=one,B=two",
                     paths=_paths(client), seed_state=prior)
    assert rc == 0
    runner = FakeRunner.instances[-1]
    rp = runner.last_rp
    provenance = json.loads((rp.server_dir / "state-seed.json").read_text(encoding="utf-8"))
    assert (rp.server_dir / "state.json").read_bytes() == raw == prior.read_bytes()
    assert provenance["source"] == str(prior.resolve()) and provenance["selection"] == "explicit"
    call = next(payload for name, payload in runner.calls if name == "start_server")
    assert call["env"]["DFLegacy__DataPath"] == str(rp.server_dir / "state.json")


def test_cli_forwards_explicit_seed_option(tmp_path, monkeypatch):
    captured = {}
    def run(*args, **kwargs):
        captured.update(kwargs)
        return 0
    monkeypatch.setattr(cli, "orchestrate", run)
    seed = tmp_path / "closed.json"
    assert cli.main(["run", "--scenario", "town-two-clients", "--client-dir", str(tmp_path),
                     "--actor-account", "A=one,B=two", "--seed-state", str(seed)]) == 0
    assert captured["seed_state"] == seed
