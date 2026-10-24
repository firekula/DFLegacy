"""战斗键验收门：判据是纯函数，先把边界钉死再谈真机。

红线用例是 `test_t11_reverse_case_must_fail`：真机实测 `X` 3.04% 与同轮待机 3.15%
同量级，若判据把它算通过，那这条门就没有意义（t11 的教训）。
"""
from acceptance import combat_gate as cg


def _action(name="普通攻击 X", vk=0x58, pct=0.0, server=0, frames=0, control=False):
    return cg.ActionEvidence(name=name, vk=vk, action_pct=pct,
                             server_events=server, netdump_new_frames=frames,
                             is_control=control)


def test_t11_reverse_case_must_fail():
    """动作 3.04% vs 同轮待机 3.15% ⇒ fail（不是 pass，也不是 needs-human）。"""
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.15, actions=(_action(pct=3.04),), plan_done=True))
    assert verdict["result"] == cg.FAIL


def test_action_far_above_control_passes():
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0, actions=(_action(pct=20.0),), plan_done=True))
    assert verdict["result"] == cg.PASS


def test_server_event_alone_is_enough():
    """服务端伤害事件是机器可判证据，像素差再小也算通过。"""
    verdict = cg.judge(cg.GateEvidence(
        control_pct=9.0, actions=(_action(pct=1.0, server=2),), plan_done=True))
    assert verdict["result"] == cg.PASS
    assert verdict["actions"][0]["server_events"] == 2


def test_netdump_frame_alone_is_enough():
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0, actions=(_action(pct=0.0, frames=3),), plan_done=True))
    assert verdict["result"] == cg.PASS


def test_plan_not_executed_is_missing_not_fail():
    """计划没被取走 = 基础设施问题，不能算产品失败。"""
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0, actions=(_action(pct=0.0),), plan_done=False))
    assert verdict["result"] == cg.MISSING


def test_missing_evidence_short_circuits():
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0, actions=(_action(pct=0.0),), plan_done=True,
        missing=("无服务端日志",)))
    assert verdict["result"] == cg.MISSING
    assert any("无服务端日志" in reason for reason in verdict["reasons"])


def test_threshold_boundary_is_inclusive():
    thresholds = cg.DEFAULT_THRESHOLDS
    required = thresholds.required_pct(3.15)
    assert cg.judge(cg.GateEvidence(
        control_pct=3.15, actions=(_action(pct=required),), plan_done=True)
    )["result"] == cg.PASS
    assert cg.judge(cg.GateEvidence(
        control_pct=3.15, actions=(_action(pct=required - 0.01),), plan_done=True)
    )["result"] == cg.FAIL


def test_required_pct_grows_with_control_noise():
    thresholds = cg.Thresholds()
    assert thresholds.required_pct(1.0) == thresholds.min_action_pct
    assert thresholds.required_pct(10.0) > thresholds.required_pct(1.0)


def test_control_probe_never_passes_alone():
    """对照项（跳跃）不参与战斗判定：只有它时不判 pass。"""
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0,
        actions=(_action(name="跳跃 C（对照项）", vk=0x43, pct=80.0, control=True),),
        plan_done=True))
    assert verdict["result"] == cg.NEEDS_HUMAN


def test_combat_failure_dominates_control_probe():
    verdict = cg.judge(cg.GateEvidence(
        control_pct=3.0,
        actions=(_action(pct=1.0),
                 _action(name="跳跃 C（对照项）", vk=0x43, pct=50.0, control=True)),
        plan_done=True))
    assert verdict["result"] == cg.FAIL


def test_parse_netdump_classes_takes_tx_only():
    sample = (
        "2026-10-23 14:06:08.221 T=1 TX TCP 127.0.0.1:1 -> 127.0.0.1:2 "
        "len=11 hex=000B2B0000000000000001\n"
        "2026-10-23 14:06:08.222 T=1 RX TCP 127.0.0.1:2 -> 127.0.0.1:1 "
        "len=11 hex=000C2F0000000000000001\n")
    classes = cg.parse_netdump_classes(sample)
    assert len(classes) == 1
    assert classes.pop()[:2] == ("TCP", "11")


def test_parse_netdump_classes_ignores_headers_and_garbage():
    text = ("# DFLegacy.Ijl15 net dump: 2026-10-23 pid=612\n"
            "# columns: timestamp thread direction protocol local -> remote len hex\n"
            "not a frame line\n")
    assert cg.parse_netdump_classes(text) == set()


def test_count_server_events_matches_damage_only():
    assert cg.count_server_events("entrance listener ready\nheartbeat ok\n") == 0
    assert cg.count_server_events("monster 1234 took 250 damage\n") == 1
    assert cg.count_server_events("hp 1000 -> 750\n") == 1


def test_image_diff_pct_identical_and_opposite(tmp_path):
    from PIL import Image

    black = tmp_path / "black.png"
    white = tmp_path / "white.png"
    Image.new("RGB", (20, 20), (0, 0, 0)).save(black)
    Image.new("RGB", (20, 20), (255, 255, 255)).save(white)

    assert cg.image_diff_pct(black, black) == 0.0
    assert cg.image_diff_pct(black, white) == 100.0


# 命令行构造的回归钉（真机咬过：`-Command <script> -- <args>` 在 PS 5.1 下把 `--`
# 吃进脚本，`dnf_pid()` 恒返 0 ⇒ 战斗门在真机上必然假阴性）。
def test_powershell_command_never_passes_double_dash():
    cmd = cg._powershell_command("script", ["-Pid2", "1"], script_path="probe.ps1")
    assert "--" not in cmd
    assert "-File" in cmd
    assert cmd[-2:] == ["-Pid2", "1"]


def test_powershell_command_rejects_args_without_a_script_file():
    """带参数却没有脚本文件 = 只能退化成旧的 `-Command ... --` 形态，必须直接报错。"""
    import pytest

    with pytest.raises(ValueError):
        cg._powershell_command("script", ["-Pid2", "1"])


def test_powershell_command_without_args_uses_command_switch():
    cmd = cg._powershell_command("Get-Process", [])
    assert "-Command" in cmd and "-File" not in cmd


def _fake_live(monkeypatch, tmp_path, *, probes, diff_pct=1.0):
    """运行真实 live 编排，只替换时钟、截图和客户端文件契约。"""
    from types import SimpleNamespace
    from acceptance import auto_input

    clock = SimpleNamespace(now=0.0)
    captures = []
    comparisons = []

    def sleep(seconds):
        clock.now += seconds

    def capture(pid, path):
        captures.append((pid, path.name))
        clock.now += 0.05
        return f"saved={path} foreground=True"

    def image_diff(before, after):
        comparisons.append((before.name, after.name))
        return diff_pct

    monkeypatch.setattr(cg, "time", SimpleNamespace(
        monotonic=lambda: clock.now, sleep=sleep))
    monkeypatch.setattr(cg, "capture_screen", capture)
    monkeypatch.setattr(cg, "image_diff_pct", image_diff)
    monkeypatch.setattr(cg, "dnf_pid", lambda: 999)
    monkeypatch.setattr(auto_input, "write_plan", lambda *args, **kwargs: None)
    monkeypatch.setattr(auto_input, "wait_ready", lambda *args, **kwargs: True)

    def done(*args, **kwargs):
        sleep(0.5)
        return True

    monkeypatch.setattr(auto_input, "await_done", done)
    standby = (
        "2026-10-23 21:48:56.191 T=1 TX UDP 0.0.0.0:5063 -> 127.0.0.1:7002 "
        "len=19 hex=02010000000A000000000000D18A6CD42BAA2B\n")
    hp_log = ("UDP protocol 12 sent to 127.0.0.1:5063: sequence=1 sender=0 "
              "object=529:1 hp=100 secondary=0\n")
    server = tmp_path / "server.log"
    dump = tmp_path / "netdump-123.log"
    # 保证旧的两条自动通过路径确实会被这些普通候选事件触发。
    assert cg.parse_netdump_classes(standby)
    assert cg.count_server_events(hp_log) == 1
    monkeypatch.setattr(cg, "_tail_from", lambda path, offset:
                        hp_log if path == server else standby)
    result = cg.run_live(tmp_path, tmp_path / "out", server, dump,
                         probes=probes, pid=123)
    return result, captures, comparisons


def test_live_standby_tx_and_unscoped_hp_cannot_pass(monkeypatch, tmp_path):
    result, captures, _ = _fake_live(
        monkeypatch, tmp_path, probes=(cg.ActionProbe("X", 0x58),))
    assert result["result"] == cg.FAIL
    assert result["actions"][0]["server_events"] == 0
    assert result["actions"][0]["netdump_new_frames"] == 0
    assert result["live_evidence_limits"]
    assert result["pid"] == 123
    assert all(pid == 123 for pid, _ in captures)


def test_live_preserves_each_keys_own_before_and_equal_idle_window(monkeypatch, tmp_path):
    result, captures, comparisons = _fake_live(
        monkeypatch, tmp_path, probes=(cg.ActionProbe("X", 0x58),
                                      cg.ActionProbe("Z", 0x5A)))
    for vk in ("58", "5A"):
        assert (f"combat-before-{vk}.png", f"combat-action-{vk}.png") in comparisons
        assert sum(name == f"combat-before-{vk}.png" for _, name in captures) == 1
        assert (f"combat-local-{vk}-a.png", f"combat-local-{vk}-b.png") in comparisons
    assert not any(before == "combat-before.png" for before, _ in comparisons)
    for capture in result["captures"]:
        assert capture["action_window_seconds"] == capture["local_control_window_seconds"]
        assert capture["action_window_seconds"] > 0.8


def test_pixel_threshold_uses_three_times_larger_control_in_report():
    """STATUS 的 3× 门槛也应覆盖局部对照，结果字段不能报全局较低门槛。"""
    assert cg.DEFAULT_THRESHOLDS.control_scale == 3.0
    verdict = cg.judge(cg.GateEvidence(
        control_pct=1.0,
        actions=(cg.ActionEvidence("X", 0x58, action_pct=14.99,
                                   local_control_pct=5.0),),
        plan_done=True))
    assert verdict["result"] == cg.FAIL
    assert verdict["actions"][0]["required_pct"] == 15.0
