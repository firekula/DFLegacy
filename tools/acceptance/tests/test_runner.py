"""runner：建 run / 状态隔离 / 端口预检 / 配置改动计划（全部用假 actor，不启动游戏）。"""
import json
import os
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

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
        procs = list(r._procs)  # 快照：stop_all 会清空 _procs，断言必须对快照做
        r.stop_all()
    assert all(p.poll() is not None for p in procs), "stop_all 必须真的结束子进程"


# ===== one-command Runner 接口（controller ruling，供 CLI 后续接线） =====

_FAKE_SLEEP = [sys.executable, "-c", "import time; time.sleep(3600)"]
_FAKE_SERVER_ARGS = ["-c", "import time; time.sleep(3600)"]


def test_start_server_passes_env_and_writes_log(tmp_path):
    """start_server 的 env 传给子进程环境变量，log_path 接收 stdout/stderr。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    sj = _write_server_json(rp.server_dir / "server.json")
    log_path = rp.server_dir / "server.log"
    # 假服务端：打印环境变量，然后 sleep
    script = rp.server_dir / "fake_server.py"
    script.write_text(
        'import os, sys, time\n'
        'print("MY_KEY=" + os.environ.get("MY_KEY", "MISSING"), flush=True)\n'
        'time.sleep(3600)\n',
        encoding="utf-8")
    rec = r.start_server(rp, exe=Path(sys.executable), workdir=rp.server_dir,
                         server_json=sj, env={"MY_KEY": "hello"}, log_path=log_path,
                         server_args=[str(script)])
    try:
        time.sleep(0.5)
        assert log_path.exists(), "log_path 文件必须已创建"
        content = log_path.read_text(encoding="utf-8")
        assert "MY_KEY=hello" in content, "env 必须传进子进程、写进 log"
        assert isinstance(rec["pid"], int)
    finally:
        r.stop_all()


def test_start_server_default_params_keep_existing_behavior(tmp_path):
    """env=None, log_path=None 时行为与改动前完全一致（stdout=DEVNULL）。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    sj = _write_server_json(rp.server_dir / "server.json")
    rec = r.start_server(rp, exe=Path(sys.executable), workdir=rp.server_dir,
                         server_json=sj, server_args=_FAKE_SERVER_ARGS)
    try:
        assert isinstance(rec["pid"], int)
    finally:
        r.stop_all()


def test_start_actors_actor_commands_and_on_started_callback(tmp_path):
    """actor_commands 给每个 actor 独立命令；on_started 返回 client_pid 记入 rec 和 manifest，
    且按 actor 顺序调用（A 先 B 后）。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"), ActorSpec("B", "synthetic")),
                  steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    cb_calls: list[str] = []
    def on_started_cb(actor_spec, proc):
        cb_calls.append(actor_spec.name)
        return {"A": 11111, "B": 22222}[actor_spec.name]
    cmds = {"A": list(_FAKE_SLEEP), "B": list(_FAKE_SLEEP)}
    recs = r.start_actors(rp, sc, actor_commands=cmds, on_started=on_started_cb)
    try:
        assert len(recs) == 2
        assert recs[0]["name"] == "A" and recs[0]["client_pid"] == 11111
        assert recs[1]["name"] == "B" and recs[1]["client_pid"] == 22222
        assert cb_calls == ["A", "B"], "on_started 按 actor 顺序调用"
        # Manifest 也记了 client_pid
        mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
        by_name = {a["name"]: a for a in mf["actors"]}
        assert by_name["A"]["client_pid"] == 11111
        assert by_name["B"]["client_pid"] == 22222
    finally:
        r.stop_all()


def test_start_actors_on_started_returning_none_omits_client_pid(tmp_path):
    """on_started 返回 None 时 rec 不含 client_pid（仅记录 launcher pid）。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    recs = r.start_actors(rp, sc, actor_commands={"A": list(_FAKE_SLEEP)},
                          on_started=lambda a, p: None)
    try:
        assert "client_pid" not in recs[0]
        assert isinstance(recs[0]["pid"], int)
    finally:
        r.stop_all()


def test_start_actors_error_on_extra_actor_in_commands(tmp_path):
    """actor_commands 包含场景里没有的 actor ⇒ ValueError，且不启动任何进程。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"), ActorSpec("B", "synthetic")),
                  steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    with pytest.raises(ValueError, match="多余|不匹配"):
        r.start_actors(rp, sc, actor_commands={
            "A": list(_FAKE_SLEEP), "C": list(_FAKE_SLEEP)})
    assert len(r._procs) == 0, "校验失败不应启动任何进程"


def test_start_actors_error_on_missing_actor_in_commands(tmp_path):
    """actor_commands 缺少场景里的 actor ⇒ ValueError，不启动进程。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"), ActorSpec("B", "synthetic")),
                  steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    with pytest.raises(ValueError, match="缺失|不匹配"):
        r.start_actors(rp, sc, actor_commands={"A": list(_FAKE_SLEEP)})
    assert len(r._procs) == 0, "校验失败不应启动任何进程"


def test_stop_all_reverse_order_and_log_cleanup(tmp_path):
    """stop_all 逆序终止（先 actor 后 server）并关闭 log 句柄。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    sj = _write_server_json(rp.server_dir / "server.json")
    log_path = rp.server_dir / "server.log"
    r.start_server(rp, exe=Path(sys.executable), workdir=rp.server_dir,
                   server_json=sj, log_path=log_path, server_args=_FAKE_SERVER_ARGS)
    r.start_actors(rp, sc)
    assert len(r._procs) == 2
    server_proc, actor_proc = r._procs[0], r._procs[1]
    # 钉住"先 actor 后 server"的顺序：记录 terminate 调用次序（实现若正序就会失败）
    order: list[str] = []
    for label, proc in (("server", server_proc), ("actor", actor_proc)):
        original_terminate = proc.terminate

        def recording_terminate(label=label, original_terminate=original_terminate):
            order.append(label)
            return original_terminate()

        proc.terminate = recording_terminate
    r.stop_all()
    assert order == ["actor", "server"], f"必须先关 actor 再关 server，实际 {order}"
    assert server_proc.poll() is not None and actor_proc.poll() is not None
    assert len(r._procs) == 0
    # log 句柄已关闭（文件可再次正常打开）
    with open(log_path, "r", encoding="utf-8") as f:
        f.read()
    assert len(r._log_handles) == 0


def test_stop_all_cleans_up_even_if_actor_start_fails(tmp_path, monkeypatch):
    """某个 actor 启动失败后，已启动的进程不得泄漏（必须已退出）。"""
    import acceptance.runner as runner_mod

    started: list[subprocess.Popen] = []
    real_popen = runner_mod.subprocess.Popen

    def recording_popen(*args, **kwargs):
        proc = real_popen(*args, **kwargs)
        started.append(proc)
        return proc

    monkeypatch.setattr(runner_mod.subprocess, "Popen", recording_popen)
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"), ActorSpec("B", "synthetic")),
                  steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    # A 正常，B 的命令会立即报错（不存在的 exe）。
    with pytest.raises((FileNotFoundError, OSError)):
        r.start_actors(rp, sc, actor_commands={
            "A": list(_FAKE_SLEEP),
            "B": ["this_exe_does_not_exist_12345"],
        })
    assert len(started) == 1, "A 已启动，B 启动失败"
    assert all(p.poll() is not None for p in started), "已启动的进程必须被回收，不得泄漏"


def test_start_actors_before_start_called_before_popen_in_order(tmp_path):
    """before_start 在每个 actor 的 Popen 之前调用，顺序契约：
    before(A) → Popen(A) → on_started(A) → before(B) → Popen(B) → on_started(B)。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"), ActorSpec("B", "synthetic")),
                  steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    events: list[tuple[str, str]] = []

    def before(actor_spec):
        events.append(("before", actor_spec.name))

    def on_started(actor_spec, proc):
        events.append(("started", actor_spec.name))
        return None

    cmds = {"A": list(_FAKE_SLEEP), "B": list(_FAKE_SLEEP)}
    try:
        recs = r.start_actors(rp, sc, actor_commands=cmds,
                              before_start=before, on_started=on_started)
        assert len(recs) == 2
        assert events == [("before", "A"), ("started", "A"),
                          ("before", "B"), ("started", "B")]
    finally:
        r.stop_all()


def test_record_run_config_writes_config_overrides_to_manifest(tmp_path):
    """record_run_config 读回 manifest、设 config_overrides、写回（UTF-8）。"""
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=())
    rp, _ = r.prepare(sc, git_sha="abc1234")
    entries = [{"key": "DFLegacy__DataPath", "old": "C:\\data", "new": "/run/1/data"}]
    r.record_run_config(rp, entries)
    mf = json.loads(rp.manifest.read_text(encoding="utf-8"))
    assert mf["config_overrides"] == entries