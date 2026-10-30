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
from acceptance.runner import (PortBusy, Runner, ServerStartupError, _can_connect,
                               apply_config_overrides, isolate_state, parse_server_endpoints,
                               plan_config_overrides, preflight_ports, probe_host,
                               wait_for_listen)

SERVER_JSON = {
    "DFLegacy": {
        "Admin": {"//Host": "注释不该被当值", "Host": "::", "Port": 8081},
        "Entrance": {"Host": "127.0.0.1", "Port": 2311},
        "Channel": {"Port": 7001},
        "GameplayDatagram": {"AdvertisedHost": "192.168.3.177", "Port": 7002},
    }
}

# 真机 `DF/town-test/server.json` 的真实形态：Entrance 与 CharacterDatagram **同为 2311**，
# 但一个是 TCP 一个是 UDP（Program.cs:335-340 的启动横幅：CharacterDatagram 带 `/udp` 后缀；
# CharacterDatagramService.cs:20 用 UdpClient，EntranceService.cs:171 用 TcpListener）。
SERVER_JSON_WITH_UDP = {
    "DFLegacy": {
        **SERVER_JSON["DFLegacy"],
        "CharacterDatagram": {"Enabled": True, "Host": "0.0.0.0", "Port": 2311},
    }
}


def _write_server_json(p, payload=None):
    p.write_text(json.dumps(payload if payload is not None else SERVER_JSON,
                            ensure_ascii=False), encoding="utf-8")
    return p


def _endpoints_for(tmp_path, services):
    """把一份最小 server.json（服务名 → 配置）落盘再解析成端点：
    被测的是真实的 parse → preflight 链路，而不是手工拼出来的端点字典。"""
    return parse_server_endpoints(
        _write_server_json(tmp_path / "server.json", {"DFLegacy": services}))


@pytest.fixture
def synthetic_server_ports(monkeypatch):
    """Python sleeper lifecycle tests do not bind the declared game ports.

    Keep them independent of a running manual game session. Real port
    preflight remains covered separately by its socket/probe tests.
    """
    monkeypatch.setattr("acceptance.runner.preflight_ports", lambda endpoints: None)


def test_parse_server_endpoints_skips_comment_keys(tmp_path):
    sj = _write_server_json(tmp_path / "server.json")
    eps = parse_server_endpoints(sj)
    assert (eps["Admin"].host, eps["Admin"].port) == ("::", 8081)
    assert (eps["Entrance"].host, eps["Entrance"].port) == ("127.0.0.1", 2311)
    assert not any("//" in k for k in eps)


def test_parse_server_endpoints_carries_protocol_alongside_host_and_port(tmp_path):
    """端点声明必须带协议：同端口不同协议（Entrance 2311/TCP vs CharacterDatagram 2311/UDP）
    只按端口号判断就会互相误报，所以协议必须参与预检。"""
    eps = parse_server_endpoints(_write_server_json(tmp_path / "server.json", SERVER_JSON_WITH_UDP))
    assert (eps["Entrance"].host, eps["Entrance"].port,
            eps["Entrance"].protocol) == ("127.0.0.1", 2311, "tcp")
    assert (eps["CharacterDatagram"].host, eps["CharacterDatagram"].port,
            eps["CharacterDatagram"].protocol) == ("0.0.0.0", 2311, "udp")
    assert eps["Admin"].protocol == "tcp" and eps["Channel"].protocol == "tcp"


def test_preflight_probes_each_endpoint_with_its_own_protocol(tmp_path):
    """注入假 probe：声明为 UDP 的端点必须按 udp 探测；占用时 PortBusy 消息要能看出
    **服务名 + 端口 + 协议**，且同端口的 TCP 条目不跟着误报。"""
    eps = _endpoints_for(tmp_path, {
        "Entrance": {"Host": "0.0.0.0", "Port": 2311},
        "CharacterDatagram": {"Enabled": True, "Host": "0.0.0.0", "Port": 2311},
    })
    seen: list[tuple[str, int, str]] = []

    def fake_probe(host: str, port: int, protocol: str) -> bool:
        seen.append((host, port, protocol))
        return protocol == "udp"  # 只有 UDP 那条被占

    with pytest.raises(PortBusy) as ei:
        preflight_ports(eps, probe=fake_probe)
    message = str(ei.value)
    assert "CharacterDatagram" in message and "2311" in message and "udp" in message
    assert "Entrance" not in message, "TCP 2311 空闲就不该被报成占用"
    assert ("0.0.0.0", 2311, "udp") in seen and ("0.0.0.0", 2311, "tcp") in seen


def test_preflight_detects_busy_udp_with_a_real_dgram_socket(tmp_path):
    """真 socket（不注入 probe）：UDP 端口被占必须报 PortBusy，释放后必须通过。
    用 SOCK_STREAM 探测查的是另一个协议名字空间 ⇒ 会把"已占用"误报成空闲，故本测试非恒真。"""
    holder = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    holder.bind(("127.0.0.1", 0))
    port = holder.getsockname()[1]
    eps = _endpoints_for(tmp_path, {"CharacterDatagram": {"Host": "127.0.0.1", "Port": port}})
    try:
        with pytest.raises(PortBusy) as ei:
            preflight_ports(eps)
        assert "CharacterDatagram" in str(ei.value) and str(port) in str(ei.value)
    finally:
        holder.close()
    preflight_ports(eps)  # 释放后空闲 ⇒ 不抛


def test_preflight_ports_raises_with_offender_named(tmp_path):
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.listen(1)
    try:
        with pytest.raises(PortBusy) as ei:
            preflight_ports(_endpoints_for(tmp_path, {"Entrance": {"Host": "127.0.0.1", "Port": port}}))
        assert "Entrance" in str(ei.value) and str(port) in str(ei.value)
    finally:
        s.close()


def test_preflight_ports_passes_when_free(tmp_path):
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    preflight_ports(_endpoints_for(tmp_path, {"Entrance": {"Host": "127.0.0.1", "Port": port}}))  # 不抛


# ===== 等服务端真的开始监听（真机 2026-10-15 缺陷：起完服务端立刻起 actor 必然失败）=====

def test_probe_host_maps_wildcard_bind_address_to_loopback():
    """`0.0.0.0`/`::` 是绑定用的通配地址，不能拿来做 connect 目标。"""
    assert probe_host("0.0.0.0") == "127.0.0.1"
    assert probe_host("::") == "127.0.0.1"
    assert probe_host("") == "127.0.0.1"
    assert probe_host("127.0.0.1") == "127.0.0.1"
    assert probe_host("192.168.3.7") == "192.168.3.7"


def test_wait_for_listen_returns_as_soon_as_the_probe_succeeds():
    """等待逻辑：注入的探测器一旦成功就返回，且**不重复探测**（不再等下一轮）。"""
    calls: list[tuple[str, int, str]] = []

    def probe(host, port, protocol):
        calls.append((host, port, protocol))
        return True

    t0 = time.monotonic()
    wait_for_listen("0.0.0.0", 2311, timeout_s=5.0, probe=probe)
    assert time.monotonic() - t0 < 0.5
    assert calls == [("127.0.0.1", 2311, "tcp")], f"通配地址要先换成回环再探测：{calls}"


def test_wait_for_listen_polls_until_the_probe_succeeds():
    """非恒真：前两次探测失败、第三次成功 ⇒ 必须继续轮询并在第三次返回。"""
    seen: list[int] = []

    def probe(host, port, protocol):
        seen.append(1)
        return len(seen) >= 3

    wait_for_listen("127.0.0.1", 2311, timeout_s=5.0, probe=probe, poll_interval_s=0.01)
    assert len(seen) == 3, f"应在第 3 次探测成功后返回，实际探测 {len(seen)} 次"


def test_wait_for_listen_emits_progress_ticks_while_waiting():
    """等待期间必须定期回报进展：冷启动时日志是"0 字节"，操作者只能靠这行分辨死活。

    非恒真：若实现不调用 on_tick（例如只设一次 deadline 就 sleep），ticks 为空 ⇒ 失败。
    """
    ticks: list[float] = []

    def probe(host, port, protocol):
        return time.monotonic() - t_start >= 0.35  # 约 0.35s 后就绪

    t_start = time.monotonic()
    wait_for_listen("127.0.0.1", 2311, timeout_s=5.0, poll_interval_s=0.02,
                    probe=probe, on_tick=ticks.append, tick_interval_s=0.1)
    assert ticks, "等待超过 tick_interval_s 却没有回报进度"
    assert ticks[0] >= 0.1, f"首个 tick 的已等秒数应 >= 0.1，得到 {ticks[0]}"


def test_wait_for_listen_raises_within_the_timeout_when_nothing_listens():
    """非恒真：把超时设小，探测器永远失败必须抛 ServerStartupError 且不超出超时太多。"""
    t0 = time.monotonic()
    with pytest.raises(ServerStartupError):
        wait_for_listen("127.0.0.1", 9, timeout_s=0.4, poll_interval_s=0.05,
                        probe=lambda host, port, protocol: False)
    assert time.monotonic() - t0 < 3.0


def test_wait_for_listen_reports_early_process_exit_instead_of_waiting():
    """服务端进程在监听前就退出 ⇒ 立刻明确失败（不空等到超时）。

    依据真机现象：服务端日志停在"Indexed 48343 script files"就被回收，
    而在它监听之前 launcher 已经只探一次端口并抛错。
    """
    t0 = time.monotonic()
    with pytest.raises(ServerStartupError) as ei:
        wait_for_listen("127.0.0.1", 9, timeout_s=30.0,
                        should_stop=lambda: True, poll_interval_s=0.05)
    assert "退出" in str(ei.value)
    assert time.monotonic() - t0 < 3.0, "进程已退出就不该继续等"


def _scenario():
    """最小场景对象（`wait_until_listening` 只需要它来兜底解析 server.json 路径）。"""
    return Scenario(id="s", goal="g", expect_t0="server-accept",
                    actors=(ActorSpec("A", "real-client"),),
                    steps=(Step("A", "do", (Expect("x", pattern="AB"),)),))


def test_wait_until_listening_parses_the_entrance_port_and_reports_it(tmp_path):
    """端口的唯一来源是 `server.json` 的 `Entrance`；报告要能看出等了哪个 host:port。"""
    sj = tmp_path / "server.json"
    _write_server_json(sj, {"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": 2311}}})
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    seen: list[tuple[str, int, str]] = []

    def fake_probe(host, port, protocol):
        seen.append((host, port, protocol))
        return True

    report = r.wait_until_listening(rp, _scenario(), server_json=sj, probe=fake_probe)
    assert report["skipped"] is False
    assert (report["host"], report["port"]) == ("127.0.0.1", 2311), "通配地址要换成可探测地址"
    assert seen == [("127.0.0.1", 2311, "tcp")], f"应按 Entrance 声明探测，实际 {seen}"


def test_wait_until_listening_reports_skipped_when_entrance_is_absent(tmp_path):
    """`server.json` 没声明 Entrance 时明确报 skipped，不假装等过（也不抛）。"""
    sj = tmp_path / "server.json"
    _write_server_json(sj, {"DFLegacy": {"Channel": {"Port": 7001}}})
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    report = r.wait_until_listening(rp, _scenario(), server_json=sj)
    assert report["skipped"] is True and "Entrance" in report["reason"]


def test_wait_until_listening_raises_with_log_tail_when_never_listens(tmp_path):
    """永不监听 ⇒ ServerStartupError，消息带服务端日志尾部（真机靠它定位）。"""
    sj = tmp_path / "server.json"
    _write_server_json(sj, {"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": 2311}}})
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    (rp.server_dir / "server-game.log").write_text("fake server: never listening\n",
                                                   encoding="utf-8")
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    with pytest.raises(ServerStartupError) as ei:
        r.wait_until_listening(rp, _scenario(), server_json=sj, timeout_s=0.5,
                               probe=lambda host, port, protocol: False)
    message = str(ei.value)
    assert "没有在 127.0.0.1:2311 开始监听" in message
    assert "never listening" in message, f"错误消息应带日志尾部：{message}"


def test_wait_until_listening_polls_the_entrance_declared_in_server_json(tmp_path):
    """端到端接线：`wait_until_listening` 从 `server.json` 取 Entrance，
    把 `host:port` 交给等待器，并如实报告等了多久。

    这里注入探测序列（前两次未就绪、第三次就绪），因此**不依赖**本机对合成监听者的
    连接行为，只钉住"端口来源 + 报告形状 + 真的在轮询"。
    """
    sj = tmp_path / "server.json"
    _write_server_json(sj, {"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": 24011}}})
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    rp, _ = r.prepare(_scenario(), git_sha="abc1234")
    attempts: list[tuple[str, int, str]] = []

    def probe(host, port, protocol):
        attempts.append((host, port, protocol))
        return len(attempts) >= 3

    report = r.wait_until_listening(rp, _scenario(), server_json=sj, timeout_s=5.0,
                                   probe=probe)
    assert report["skipped"] is False
    assert (report["host"], report["port"]) == ("127.0.0.1", 24011), report
    assert len(attempts) == 3 and all(a == ("127.0.0.1", 24011, "tcp") for a in attempts), attempts


def test_can_connect_is_false_for_a_port_with_no_listener_and_is_time_bounded():
    """真机关键事实：判断"就绪"用连接，且**总耗时**受超时约束。

    [真机 2026-10-15] 服务端启动中 SYN 被丢弃时，`socket.create_connection` 会因
    "每次尝试"超时 + Windows 重试而阻塞约 20 秒，把就绪门卡死并抛 TimeoutError
    （实测 traceback：socket.py:850 → runner.py 探针）。这里钉住"快速返回 False"。
    """
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()  # 关掉 ⇒ 该端口无人监听
    t0 = time.monotonic()
    assert _can_connect("127.0.0.1", port) is False
    elapsed = time.monotonic() - t0
    assert elapsed < 2.0, f"探针必须在超时内返回，实际 {elapsed:.2f}s"


def test_wait_for_listen_survives_a_probe_that_raises():
    """探针抛 OSError（网络栈瞬时错误）不得崩掉整条等待；应继续轮询直到成功。"""
    seen: list[int] = []

    def flaky(host, port, protocol):
        seen.append(1)
        if len(seen) == 1:
            raise OSError("transient")
        return True

    wait_for_listen("127.0.0.1", 2311, timeout_s=5.0, probe=flaky, poll_interval_s=0.01)
    assert len(seen) == 2, f"第一次抛错后应继续探测，实际 {len(seen)} 次"


@pytest.mark.usefixtures("synthetic_server_ports")
def test_start_server_rejects_relative_data_path(tmp_path):
    """`DFLegacy__DataPath` 必须是绝对路径，否则**启动前**响亮失败。

    真机 2026-10-15 实测：服务端按 `AppContext.BaseDirectory`（exe 目录）解析该值，
    相对路径 `..\\runs\\<runId>\\server\\state.json` 被写到了 `DF\\runs\\...`，
    而 run 私有副本原封不动 ⇒ 存档隔离静默失效。宁可在起进程前报错。
    """
    sj = tmp_path / "server.json"
    _write_server_json(sj)
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    with pytest.raises(ValueError) as ei:
        r.start_server(rp, exe=Path(sys.executable), workdir=tmp_path, server_json=sj,
                       env={"DFLegacy__DataPath": r"..\runs\RID\server\state.json"},
                       server_args=_FAKE_SERVER_ARGS)
    assert "绝对路径" in str(ei.value)
    assert r._procs == [], "校验失败不得启动任何进程"


@pytest.mark.usefixtures("synthetic_server_ports")
def test_wait_until_listening_fails_fast_when_the_real_server_process_exits(tmp_path):
    """真进程装配：服务端秒退必须**立刻**报"退出"，而不是拖满超时报"超时"。

    [真机/实测 2026-10-18 终审 Important] 旧实现用 `os.kill(pid,0)` 判活，而 `Runner`
    持有 Popen 句柄时已退出的子进程仍被判活 ⇒ 快速失败分支永不触发，
    默认 300s 下秒退的服务端要等 5 分钟。本测试用**真实 Popen**（不是注入 lambda）
    覆盖生产装配。

    非恒真的变异（已由终审实测）：把 `start_server` 里的 `self._server_proc = p` 删掉，
    或让 `should_stop` 改回"按 pid 判活"，本测试即红（退化为约 20–60s 的超时文案）。
    仅把某个辅助函数改成常量**不会**让本测试变红 —— 因为现在只有 `Popen` 这一条判活路径。
    """
    sj = tmp_path / "server.json"
    _write_server_json(sj, {"DFLegacy": {"Entrance": {"Host": "0.0.0.0", "Port": 2311}}})
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path, runs_dir=tmp_path / "runs")
    rp, _ = r.prepare(_scenario(), git_sha="abc1234")
    try:
        r.start_server(rp, exe=Path(sys.executable), workdir=tmp_path, server_json=sj,
                       log_path=rp.server_dir / "server.log",
                       server_args=["-c", "import sys; sys.exit(3)"])
        t0 = time.monotonic()
        with pytest.raises(ServerStartupError) as ei:
            r.wait_until_listening(rp, _scenario(), server_json=sj, timeout_s=60.0,
                                   probe=lambda host, port, protocol: False)
        elapsed = time.monotonic() - t0
        message = str(ei.value)
        assert "退出" in message, f"应报'进程提前退出'，实际：{message}"
        assert elapsed < 10.0, f"秒退的进程必须立刻失败，实际等了 {elapsed:.1f}s"
    finally:
        r.stop_all()


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
    # This stdlib-only fixture must terminate the actual leaf interpreter,
    # including when the suite itself uses a Windows venv redirector.
    interpreter = getattr(sys, "_base_executable", sys.executable)
    recs = r.start_actors(rp, sc, launcher_cmd=[interpreter, "-c", "import time; time.sleep(30)"])
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


@pytest.mark.usefixtures("synthetic_server_ports")
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
        # 条件等待：解释器启动快慢不定，固定 sleep 会偶发读到空文件（机器繁忙时更甚）
        deadline = time.monotonic() + 15.0
        content = ""
        while "MY_KEY=hello" not in content and time.monotonic() < deadline:
            time.sleep(0.05)
            if log_path.exists():
                content = log_path.read_text(encoding="utf-8")
        assert log_path.exists(), "log_path 文件必须已创建"
        assert "MY_KEY=hello" in content, f"env 必须传进子进程、写进 log；实际内容 {content!r}"
        assert isinstance(rec["pid"], int)
    finally:
        r.stop_all()


@pytest.mark.usefixtures("synthetic_server_ports")
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


@pytest.mark.usefixtures("synthetic_server_ports")
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


@pytest.mark.usefixtures("synthetic_server_ports")
def test_start_server_explicit_private_save_removes_all_inherited_path_aliases(tmp_path, monkeypatch):
    r=Runner(workspace=tmp_path,acceptance_dir=tmp_path/'acc',runs_dir=tmp_path/'runs')
    sc=Scenario(id='s',goal='g',expect_t0='server-accept',actors=(ActorSpec('A','synthetic'),),steps=())
    rp,_=r.prepare(sc,git_sha='private')
    sj=_write_server_json(rp.server_dir/'server.json');out=rp.server_dir/'env.json'
    script=rp.server_dir/'env_server.py'
    script.write_text("import os,json,sys\nfrom pathlib import Path\nPath(sys.argv[1]).write_text(json.dumps({k:v for k,v in os.environ.items() if k.replace('__',':').casefold()=='dflegacy:datapath'}))\n",encoding='utf-8')
    monkeypatch.setenv('DFLegacy:DataPath',str(tmp_path/'normal.json'))
    monkeypatch.setenv('dflegacy__datapath',str(tmp_path/'stale.json'))
    private=str(rp.server_dir/'state.json')
    try:
        r.start_server(rp,exe=Path(sys.executable),workdir=rp.server_dir,server_json=sj,
                       env={'DFLegacy__DataPath':private},server_args=[str(script),str(out)])
        deadline=time.monotonic()+10
        while not out.exists() and time.monotonic()<deadline:time.sleep(.025)
        actual=json.loads(out.read_text(encoding='utf-8'))
        assert len(actual)==1 and set(actual.values())=={private}
    finally:r.stop_all()
