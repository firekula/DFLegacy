"""副本内战斗键验收门（B′ 键盘控制，业主要求：战斗只能在副本里验）。

**为什么不在城镇验**：城镇不接受攻击/技能动作。t11 实测 `X`（普通攻击）3.04%、
`Z`（技能）1.51%~2.72%，而同轮待机对照 3.15% —— 同量级，拿"没崩"或"有点变化"当结论
都是错的。判据必须在副本里取，且必须带同轮待机对照。

**一条命令**（两个客户端已组队、队长已进入副本之后）：

    cd tools
    python -m acceptance.combat_gate --client-dir G:\\Game\\DNF_ACT1\\DF \\
        --server-log <服务端日志> --netdump G:\\Game\\DNF_ACT1\\DF\\netdump-<pid>.log \\
        --out-dir <run_dir>

它按客户端自带「键盘设置」的绑定注入 `X`（普通攻击）/`Z`（技能）/`C`（跳跃，对照），
采集三类证据并给出 pass / needs-human / fail / missing-evidence。

**自检**（不需要游戏，CI 与提交前都该跑）：

    cd tools && python -m acceptance.combat_gate --self-check

判据（`Thresholds` 与 `judge`）是纯函数、无 I/O，单测在
`acceptance/tests/test_combat_gate.py`。
"""
from __future__ import annotations

import argparse
import dataclasses
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import time
from typing import Iterable, Optional, Sequence

# ----------------------------------------------------------------------------
# 纯逻辑：判据
# ----------------------------------------------------------------------------

PASS = "pass"
NEEDS_HUMAN = "needs-human"
MISSING = "missing-evidence"
FAIL = "fail"


@dataclasses.dataclass(frozen=True)
class Thresholds:
    """像素判据的阈值。

    `min_action_pct`：动作帧相对"完全静止"的最低门槛。
    `control_scale`/`control_margin_pct`：动作帧必须显著高于**同轮待机对照**。
    t11 的反例（动作 3.04% vs 待机 3.15%）必须判 fail —— 这条是回归红线。
    """

    min_action_pct: float = 8.0
    control_scale: float = 3.0
    control_margin_pct: float = 4.0

    def required_pct(self, control_pct: float) -> float:
        return max(
            self.min_action_pct,
            control_pct * self.control_scale,
            control_pct + self.control_margin_pct,
        )


DEFAULT_THRESHOLDS = Thresholds()


@dataclasses.dataclass(frozen=True)
class ActionProbe:
    """一个要注入的动作键。绑定取自客户端「键盘设置」（真机实拍 2026-10-23）。"""

    name: str
    vk: int
    dur_ms: int = 150
    # 对照项：不期望产生战斗效果，用来证明"像素差判据不是因为随便动一下就过"。
    control: bool = False


DEFAULT_PROBES: tuple[ActionProbe, ...] = (
    ActionProbe("普通攻击 X", 0x58),
    ActionProbe("技能 Z", 0x5A, dur_ms=200),
    ActionProbe("跳跃 C（对照项）", 0x43, dur_ms=120, control=True),
)


@dataclasses.dataclass(frozen=True)
class ActionEvidence:
    name: str
    vk: int
    action_pct: float
    server_events: int = 0
    netdump_new_frames: int = 0
    is_control: bool = False
    # 本动作注入前就地测的静止对照。副本里怪物会自己走，全局那一次对照不够用，
    # 所以每个动作各带一个局部对照，判据取两者较大值。
    local_control_pct: float = 0.0


@dataclasses.dataclass(frozen=True)
class GateEvidence:
    control_pct: float
    actions: tuple[ActionEvidence, ...]
    plan_done: bool
    missing: tuple[str, ...] = ()


def _judge_action(action: ActionEvidence, control_pct: float,
                  thresholds: Thresholds) -> tuple[str, list[str]]:
    """单个动作键的判定：机器可判的证据优先，像素差只作最后一档。"""
    if action.is_control:
        # 对照项不参与"战斗是否生效"的判定；只要求它**不**独自构成通过证据。
        return NEEDS_HUMAN, ["对照项，不参与战斗判定"]

    if action.server_events > 0:
        return PASS, [f"服务端出现 {action.server_events} 条伤害/HP 事件"]
    if action.netdump_new_frames > 0:
        return PASS, [f"netdump 出现 {action.netdump_new_frames} 类待机窗口没有的上行帧"]
    effective_control = max(control_pct, action.local_control_pct)
    required = thresholds.required_pct(effective_control)
    if action.action_pct >= required:
        return PASS, [f"像素差 {action.action_pct:.2f}% ≥ 门槛 {required:.2f}%"
                      f"（同轮待机 {effective_control:.2f}%）"]
    return FAIL, [f"像素差 {action.action_pct:.2f}% < 门槛 {required:.2f}%"
                  f"（同轮待机 {effective_control:.2f}%），且服务端/netdump 均无新事件"]


def judge(evidence: GateEvidence, thresholds: Thresholds = DEFAULT_THRESHOLDS) -> dict:
    """把采集到的证据折算成判定。**纯函数**：无 I/O、无时间、无进程。"""
    result: dict = {"result": NEEDS_HUMAN, "reasons": [], "actions": []}

    if evidence.missing:
        result["result"] = MISSING
        result["reasons"] = [f"缺证据：{name}" for name in evidence.missing]
        for action in evidence.actions:
            result["actions"].append(
                {"name": action.name, "vk": action.vk, "result": MISSING})
        return result

    if not evidence.plan_done:
        # 计划都没被执行（客户端失焦/未取走），此时任何"没效果"都不能算产品问题。
        result["result"] = MISSING
        result["reasons"] = ["指令文件未被客户端取走并执行（plan_done=False）"]
        for action in evidence.actions:
            result["actions"].append(
                {"name": action.name, "vk": action.vk, "result": MISSING})
        return result

    outcomes: list[str] = []
    for action in evidence.actions:
        verdict, why = _judge_action(action, evidence.control_pct, thresholds)
        outcomes.append(verdict)
        result["actions"].append({
            "name": action.name,
            "vk": action.vk,
            "action_pct": round(action.action_pct, 2),
            "required_pct": round(thresholds.required_pct(
                max(evidence.control_pct, action.local_control_pct)), 2),
            "server_events": action.server_events,
            "netdump_new_frames": action.netdump_new_frames,
            "result": verdict,
            "reasons": why,
        })
        result["reasons"].extend(f"{action.name}: {line}" for line in why)

    combat = [o for a, o in zip(evidence.actions, outcomes) if not a.is_control]
    if FAIL in combat:
        result["result"] = FAIL
    elif combat and all(o == PASS for o in combat):
        result["result"] = PASS
    else:
        result["result"] = NEEDS_HUMAN
    return result


# ----------------------------------------------------------------------------
# 证据解析（纯函数，带单测）
# ----------------------------------------------------------------------------

_NETDUMP_LINE = re.compile(
    r"^(?P<stamp>\S+ \S+) T=(?P<thread>\d+) (?P<dir>\w+) (?P<proto>\S+) "
    r"(?P<local>\S+) -> (?P<remote>\S+) len=(?P<len>\d+) hex=(?P<hex>[0-9A-Fa-f]*)")

#: 服务端日志里能证明"这一下打到了东西"的行。宁可窄也不要宽：
#: 宽模式（比如只匹配 hp）会把心跳、登录等噪声算进来，等于自己造恒真判定。
SERVER_EVENT_PATTERNS: tuple[re.Pattern, ...] = (
    re.compile(r"(?i)\bdamage\b"),
    re.compile(r"(?i)\bhp\b[^\n]*\b\d+\b"),
    re.compile(r"(?i)受到伤害|造成伤害|命中"),
)


def parse_netdump_classes(text: str, direction: str = "TX") -> set[tuple]:
    """把 netdump 文本折成"帧类"集合：(protocol, len, hex 前 16 字符)。

    只取一个方向的帧：战斗动作是**上行**（客户端→服务端），拿 RX 会把广播算进来。
    """
    classes = set()
    for line in text.splitlines():
        match = _NETDUMP_LINE.match(line.strip())
        if not match or match.group("dir") != direction:
            continue
        classes.add((match.group("proto"), match.group("len"),
                     match.group("hex")[:16].upper()))
    return classes


def count_server_events(text: str,
                        patterns: Sequence[re.Pattern] = SERVER_EVENT_PATTERNS) -> int:
    return sum(1 for line in text.splitlines() if any(p.search(line) for p in patterns))


def image_diff_pct(path_a: os.PathLike, path_b: os.PathLike,
                   channel_threshold: int = 30, step: int = 2) -> float:
    """两张同尺寸 PNG 的变化像素百分比（每 step 像素取样）。

    口径与真机取证脚本一致：单像素三通道差之和 > channel_threshold 记为"变了"。
    """
    from PIL import Image  # 延迟导入：只跑纯判据的调用方不需要 Pillow

    with Image.open(path_a) as img_a, Image.open(path_b) as img_b:
        a = img_a.convert("RGB")
        b = img_b.convert("RGB")
        width = min(a.width, b.width)
        height = min(a.height, b.height)
        a_px, b_px = a.load(), b.load()
        changed = total = 0
        for y in range(0, height, step):
            for x in range(0, width, step):
                pa, pb = a_px[x, y], b_px[x, y]
                total += 1
                if (abs(pa[0] - pb[0]) + abs(pa[1] - pb[1]) + abs(pa[2] - pb[2])
                        > channel_threshold):
                    changed += 1
    return 0.0 if total == 0 else 100.0 * changed / total


# ----------------------------------------------------------------------------
# live：注入 + 采集
# ----------------------------------------------------------------------------

_CAPTURE_PS = r"""
param([int]$Pid2, [string]$OutPath)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class CGWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
}
"@
$proc = Get-Process -Id $Pid2 -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { throw "no main window" }
[CGWin]::ShowWindow($hwnd, 9) | Out-Null
for ($i = 0; $i -lt 5; $i++) {
  if ([CGWin]::GetForegroundWindow() -eq $hwnd) { break }
  [uint32]$fgPid = 0
  $fgThread = [CGWin]::GetWindowThreadProcessId([CGWin]::GetForegroundWindow(), [ref]$fgPid)
  $cur = [CGWin]::GetCurrentThreadId()
  $attached = $false
  if ($fgThread -ne 0 -and $fgThread -ne $cur) { $attached = [CGWin]::AttachThreadInput($cur, $fgThread, $true) }
  [CGWin]::BringWindowToTop($hwnd) | Out-Null
  [CGWin]::SetActiveWindow($hwnd) | Out-Null
  [CGWin]::SetForegroundWindow($hwnd) | Out-Null
  if ($attached) { [CGWin]::AttachThreadInput($cur, $fgThread, $false) | Out-Null }
  Start-Sleep -Milliseconds 250
}
$rect = New-Object CGWin+RECT
[CGWin]::GetClientRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
if ($w -le 0 -or $h -le 0) { throw "empty client area" }
$origin = New-Object CGWin+POINT
[CGWin]::ClientToScreen($hwnd, [ref]$origin) | Out-Null
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($origin.X, $origin.Y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved=$OutPath foreground=$([CGWin]::GetForegroundWindow() -eq $hwnd)"
"""


def _powershell_command(script_text: str, args: Iterable[str],
                        script_path: Optional[os.PathLike] = None) -> list[str]:
    """构造 powershell 命令行。**纯函数**，因为它被真机咬过一次。

    事故（2026-10-23）：早先的实现拼成 `powershell -Command <script> -- <args>`，
    Windows PowerShell 5.1 会把 `--` 当成脚本的一部分，于是带参数的辅助脚本
    （`capture_screen`）直接失败、`dnf_pid()` 恒返 0 —— 战斗门在真机上必然报
    "没有正在运行的 DNF 客户端"（假阴性），而 `--self-check` 与单测都发现不了。
    规则：**有参数就走 `-File`（脚本落盘 + 参数走 param 绑定），无参数才用 `-Command`，
    且永远不把 `--` 拼进命令行。**
    """
    if args:
        if not script_path:
            raise ValueError("带参数的调用必须先给出脚本文件路径（-File 语义）")
        return ["powershell", "-NoProfile", "-File", str(script_path)] + list(args)
    return ["powershell", "-NoProfile", "-Command", script_text]


def _write_script(text: str, name: str) -> pathlib.Path:
    """把辅助脚本落到 temp；BOM 是必需的 —— PS 5.1 没有 BOM 会按本地代码页读文件。"""
    target = pathlib.Path(tempfile.gettempdir()) / name
    target.write_text(text, encoding="utf-8-sig")
    return target


def _run(command: list[str], timeout: int) -> str:
    proc = subprocess.run(command, capture_output=True, text=True, timeout=timeout,
                          creationflags=0x08000000 if os.name == "nt" else 0)
    return (proc.stdout + proc.stderr).strip()


def _ps(command: str, args: Iterable[str] = (), timeout: int = 120) -> str:
    return _run(_powershell_command(command, list(args)), timeout)


def capture_screen(pid: int, out_path: os.PathLike) -> str:
    """抓客户端客户区（桌面截取；PrintWindow 对本 D3D8 窗口会周期性返回全黑图）。"""
    script = _write_script(_CAPTURE_PS, "dfl-combat-capture.ps1")
    return _run(_powershell_command(_CAPTURE_PS, ["-Pid2", str(pid), "-OutPath",
                                                  str(out_path)], script), 120)


def dnf_pid() -> int:
    out = _ps("(Get-Process DNF -ErrorAction SilentlyContinue | "
              "Where-Object { $_.MainWindowHandle -ne 0 } | "
              "Select-Object -First 1).Id")
    return int(out) if out.strip().isdigit() else 0


def _read_text(path: Optional[os.PathLike]) -> Optional[str]:
    if not path:
        return None
    try:
        return pathlib.Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None


def _tail_from(path: Optional[os.PathLike], offset: int) -> Optional[str]:
    """读 offset 之后的追加内容（服务端日志/netdump 都在长）。"""
    if not path:
        return None
    try:
        with open(path, "rb") as handle:
            handle.seek(offset)
            return handle.read().decode("utf-8", errors="replace")
    except OSError:
        return None


def _size(path: Optional[os.PathLike]) -> int:
    if not path:
        return 0
    try:
        return pathlib.Path(path).stat().st_size
    except OSError:
        return 0


def run_live(client_dir: pathlib.Path, out_dir: pathlib.Path,
             server_log: Optional[pathlib.Path], netdump: Optional[pathlib.Path],
             probes: Sequence[ActionProbe] = DEFAULT_PROBES,
             control_seconds: float = 2.0,
             thresholds: Thresholds = DEFAULT_THRESHOLDS,
             pid: Optional[int] = None) -> dict:
    """注入动作键并采集证据。前置：客户端已在副本里（由上层驱动保证）。"""
    from acceptance import auto_input  # 本包内的契约实现，原子写计划/等完成标记

    pid = dnf_pid() if pid is None else pid
    if not pid:
        raise RuntimeError("没有正在运行的 DNF 客户端；请先由上层脚本把客户端带进副本")
    out_dir.mkdir(parents=True, exist_ok=True)

    shots = {
        "control-a": out_dir / "combat-control-a.png",
        "control-b": out_dir / "combat-control-b.png",
    }
    if capture_screen(pid, shots["control-a"]).find("saved=") < 0:
        raise RuntimeError("截取失败（控制组 A）")
    time.sleep(control_seconds)
    capture_screen(pid, shots["control-b"])
    control_pct = image_diff_pct(shots["control-a"], shots["control-b"])

    missing: list[str] = []
    actions: list[ActionEvidence] = []
    captures: list[dict] = []
    for probe in probes:
        before_shot = out_dir / f"combat-before-{probe.vk:02X}.png"
        action_shot = out_dir / f"combat-action-{probe.vk:02X}.png"
        local_a = out_dir / f"combat-local-{probe.vk:02X}-a.png"
        local_b = out_dir / f"combat-local-{probe.vk:02X}-b.png"
        # 每个键保留自己的基准，不能把上一个动作或怪物位移累加给下一个键。
        before_started = time.monotonic()
        capture_screen(pid, before_shot)
        log_offset = _size(server_log)
        dump_offset = _size(netdump)
        auto_input.write_plan(client_dir, [
            {"t": 300, "op": "key", "vk": probe.vk, "dur": probe.dur_ms},
        ])
        started = time.monotonic()
        taken = auto_input.wait_ready(client_dir, timeout_s=15)
        done = auto_input.await_done(client_dir, timeout_s=30)
        time.sleep(0.3)
        action_started = time.monotonic()
        capture_screen(pid, action_shot)

        action_window = action_started - before_started
        # 动作结束后先等动画收尾，再采同长局部待机窗口。用两次捕获的调用
        # 起点配平，把截图调用本身的耗时也计入；若一次捕获就超过目标时长，
        # 对照窗口只会更长，并把实际时长留在结果里供复核。
        time.sleep(0.6)
        local_started = time.monotonic()
        capture_screen(pid, local_a)
        time.sleep(max(0.0, action_window - (time.monotonic() - local_started)))
        local_finished = time.monotonic()
        capture_screen(pid, local_b)
        local_control_pct = image_diff_pct(local_a, local_b)
        action_pct = image_diff_pct(before_shot, action_shot)
        server_slice = _tail_from(server_log, log_offset)
        dump_slice = _tail_from(netdump, dump_offset)
        # 现有帧类包含 sequence，日志 HP/damage 行也没有攻击者/实例归属。
        # 它们只能供人工追查，不能作为本动作的自动通过证据。纯 judge 仍接受
        # 调用者已核实的 server_events/netdump_new_frames。
        actions.append(ActionEvidence(
            name=probe.name, vk=probe.vk, action_pct=action_pct,
            server_events=0, netdump_new_frames=0, is_control=probe.control,
            local_control_pct=local_control_pct))
        captures.append({
            "vk": probe.vk,
            "before": str(before_shot), "after": str(action_shot),
            "local_control_a": str(local_a), "local_control_b": str(local_b),
            "action_window_seconds": round(action_window, 4),
            "local_control_window_seconds": round(local_finished - local_started, 4),
            "local_control_pct": round(local_control_pct, 2),
        })
        if not (taken and done):
            missing.append(f"{probe.name} 的计划未被取走/未完成"
                           f"（taken={taken}, done={done}, "
                           f"{time.monotonic() - started:.1f}s）")
        if server_slice is None:
            missing.append("无服务端日志（--server-log 未给或不可读）")
        if dump_slice is None:
            missing.append("无 netdump（--netdump 未给或不可读）")

    # 去重：同一个缺失原因不要重复十遍
    deduped = tuple(dict.fromkeys(missing))
    evidence = GateEvidence(control_pct=control_pct, actions=tuple(actions),
                            plan_done=not any("计划未被取走" in m for m in deduped),
                            missing=deduped)
    verdict = judge(evidence, thresholds)
    verdict["control_pct"] = round(control_pct, 2)
    verdict["pid"] = pid
    verdict["client_dir"] = str(client_dir)
    verdict["captures"] = captures
    verdict["live_evidence_limits"] = [
        "本次 live 仅按每键独立基准和同长待机对照判定画面动作。",
        "netdump 帧类含 sequence，未核实攻击/技能语义，不自动计为新动作帧。",
        "服务端 HP/damage 日志未核实攻击者及实例归属，不自动计为伤害证据。",
        "伤害结算仍为未验证；捕获窗口按截图调用起点配平，实际图像时刻受捕获延迟影响。",
    ]
    (out_dir / "combat-gate.json").write_text(
        json.dumps(verdict, ensure_ascii=False, indent=2), encoding="utf-8")
    return verdict


# ----------------------------------------------------------------------------
# 自检：把判据的边界钉死在命令里（不需要游戏）
# ----------------------------------------------------------------------------

def self_check() -> int:
    """跑一组固定的判定向量；返回进程退出码。"""
    failures: list[str] = []
    th = DEFAULT_THRESHOLDS

    def expect(name: str, verdict: dict, want: str) -> None:
        got = verdict["result"]
        if got != want:
            failures.append(f"{name}: 期望 {want}，实得 {got} — {verdict['reasons']}")

    # 1) t11 的反例：动作 3.04% vs 同轮待机 3.15% ⇒ 必须 fail（回归红线）
    expect("t11 反例（X 3.04 / 待机 3.15）", judge(GateEvidence(
        control_pct=3.15,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=3.04),),
        plan_done=True)), FAIL)

    # 2) 显著高于对照 ⇒ pass
    expect("动作显著高于对照（20 vs 3）", judge(GateEvidence(
        control_pct=3.0,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=20.0),),
        plan_done=True)), PASS)

    # 3) 服务端伤害事件 ⇒ 直接 pass（pixel 再小也不影响）
    expect("服务端伤害事件", judge(GateEvidence(
        control_pct=9.0,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=1.0, server_events=2),),
        plan_done=True)), PASS)

    # 4) netdump 新上行帧 ⇒ pass（像素无变化也能判）
    expect("netdump 新上行帧", judge(GateEvidence(
        control_pct=3.0,
        actions=(ActionEvidence("技能 Z", 0x5A, action_pct=0.0, netdump_new_frames=3),),
        plan_done=True)), PASS)

    # 5) 计划未被取走 ⇒ missing-evidence，不是 fail（基础设施问题 ≠ 产品问题）
    expect("计划未被执行", judge(GateEvidence(
        control_pct=3.0,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=0.0),),
        plan_done=False)), MISSING)

    # 6) 缺证据 ⇒ missing-evidence
    expect("缺服务端日志", judge(GateEvidence(
        control_pct=3.0,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=0.0),),
        plan_done=True, missing=("无服务端日志",))), MISSING)

    # 7) 阈值边界：恰好等于门槛 ⇒ pass；差一点点 ⇒ fail
    required = th.required_pct(3.15)
    expect("恰好等于门槛", judge(GateEvidence(
        control_pct=3.15,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=required),),
        plan_done=True)), PASS)
    expect("低于门槛一点", judge(GateEvidence(
        control_pct=3.15,
        actions=(ActionEvidence("普通攻击 X", 0x58, action_pct=required - 0.01),),
        plan_done=True)), FAIL)

    # 8) 对照项不参与战斗判定：只有对照项时不判 pass
    control_only = judge(GateEvidence(
        control_pct=3.0,
        actions=(ActionEvidence("跳跃 C（对照项）", 0x43, action_pct=80.0,
                                is_control=True),),
        plan_done=True))
    if control_only["result"] == PASS:
        failures.append("对照项: 只有对照项时不应判 pass")

    # 9) netdump 解析：只取 TX，且按 (proto,len,hex 前缀) 分类
    sample = (
        "2026-10-23 14:06:08.221 T=1 TX TCP 127.0.0.1:1 -> 127.0.0.1:2 "
        "len=11 hex=000B2B0000000000000001\n"
        "2026-10-23 14:06:08.222 T=1 RX TCP 127.0.0.1:2 -> 127.0.0.1:1 "
        "len=11 hex=000C2F0000000000000001\n")
    classes = parse_netdump_classes(sample)
    if len(classes) != 1:
        failures.append(f"netdump 解析: 期望 1 类 TX，实得 {len(classes)}")

    # 10) 服务端事件计数不能把普通行算进来
    if count_server_events("entrance listener ready\nheartbeat ok\n") != 0:
        failures.append("服务端事件计数: 普通行被误计")
    if count_server_events("monster 1234 took 250 damage\n") != 1:
        failures.append("服务端事件计数: 伤害行未被识别")

    for line in failures:
        print(f"SELF-CHECK FAIL: {line}")
    if failures:
        return 1
    print("combat gate self-check: 10 assertions passed")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="副本内战斗键验收门")
    parser.add_argument("--self-check", action="store_true", help="只跑判定向量，不需要游戏")
    parser.add_argument("--client-dir", default=r"G:\Game\DNF_ACT1\DF")
    parser.add_argument("--server-log", default=None)
    parser.add_argument("--netdump", default=None)
    parser.add_argument("--out-dir", default=None)
    parser.add_argument("--pid", type=int, default=None, help="指定本次注入和截图的 DNF 进程")
    parser.add_argument("--control-seconds", type=float, default=2.0)
    args = parser.parse_args(argv)

    if args.self_check:
        return self_check()

    client_dir = pathlib.Path(args.client_dir)
    out_dir = pathlib.Path(args.out_dir) if args.out_dir else client_dir / "combat-gate"
    verdict = run_live(
        client_dir=client_dir,
        out_dir=out_dir,
        server_log=pathlib.Path(args.server_log) if args.server_log else None,
        netdump=pathlib.Path(args.netdump) if args.netdump else None,
        control_seconds=args.control_seconds,
        pid=args.pid,
    )
    print(json.dumps(verdict, ensure_ascii=False, indent=2))
    return 0 if verdict["result"] == PASS else 1


if __name__ == "__main__":
    sys.exit(main())
