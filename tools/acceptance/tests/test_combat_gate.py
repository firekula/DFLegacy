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
