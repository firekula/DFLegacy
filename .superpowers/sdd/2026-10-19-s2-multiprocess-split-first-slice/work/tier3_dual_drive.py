"""S2 Task 11 Tier 3 driver: dual-client town entry, ZERO manual UI.

Focus mechanism (fully automatic): the client module only pumps while its
process owns the foreground window (the game's own polling semantics), so
the driver hands focus to the target client by performing a REAL mouse
click at the window center (focus.ps1: z-order top without activating,
SetCursorPos, mouse_event down/up). The click also delivers the real input
event that wakes DNF's DirectInput polling -- plain SetForegroundWindow
changes focus but generates no input, so the game never re-acquires.

One shared exe directory is safe: the module's focus gate means an
unfocused client neither consumes a plan nor fires edges.

Usage: python tier3_dual_drive.py
"""
import datetime
import subprocess
import sys
import time

sys.path.insert(0, r"G:\Game\DNF_ACT1\DFLegacy\tools")

from acceptance import auto_input  # noqa: E402
from acceptance import cli as acc_cli  # noqa: E402

CLIENT_DIR = r"G:\Game\DNF_ACT1\DF"
WORK_DIR = r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work"
CAPTURE = r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-22-client-auto-input\work\capture.ps1"
FOCUS = r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\focus.ps1"

ENTER_TOWN = [
    {"t": 500, "op": "click", "x": 127, "y": 97},    # channel row
    {"t": 1700, "op": "click", "x": 320, "y": 448},  # 开始 (join channel)
    {"t": 3200, "op": "click", "x": 318, "y": 267},  # 确认 (notice dialog; no-op if absent)
    {"t": 4200, "op": "click", "x": 110, "y": 140},  # character slot
    {"t": 5200, "op": "click", "x": 320, "y": 448},  # 开始 -> town
    # Same-screen comes from the SHARED spawn room fixture (server.json
    # PrivateAreas=[]), NOT from walking: the client always spawns in 1/1 and
    # the synthetic arrow keys did not move it (runbook §4.3). These walk
    # steps are vestigial -- they fire harmlessly after town load.
    {"t": 18000, "op": "key", "vk": 37, "dur": 4000},  # LEFT 4 s (no-op)
    {"t": 24000, "op": "key", "vk": 39, "dur": 4000},  # RIGHT 4 s (no-op)
]
WALK = [
    {"t": 300, "op": "key", "vk": 37, "dur": 4000},   # vestigial (see above)
    {"t": 6000, "op": "key", "vk": 39, "dur": 4000},
]

_state = {"phase": 0}


def run_powershell(script):
    return subprocess.run(
        ["powershell", "-NoProfile", "-Command", script],
        capture_output=True, text=True, timeout=60)


def client_pids():
    """Both DNF client pids, launcher order (A first, B second)."""
    proc = run_powershell(
        "Get-Process DNF | Where-Object { $_.MainWindowHandle -ne 0 }"
        " | Sort-Object StartTime | ForEach-Object { Write-Output $_.Id }")
    pids = [int(line) for line in proc.stdout.split() if line.strip().isdigit()]
    if len(pids) != 2:
        raise RuntimeError(f"expected two DNF clients, got {pids}")
    return pids


def click_focus(pid):
    proc = run_powershell(f"& '{FOCUS}' -Pid2 {pid}")
    print(f"  [driver] click-focus {pid}: {proc.stdout.strip()}", flush=True)
    return "ok" in proc.stdout


def screenshot(tag, pid):
    try:
        proc = run_powershell(
            f"& '{CAPTURE}' -Pid2 {pid} -OutPath '{WORK_DIR}\\tier3-{tag}-{pid}.png'")
        print(f"  [driver] screenshot {tag}-{pid}: {proc.stdout.strip()}", flush=True)
    except Exception as error:
        print(f"  [driver] screenshot {tag} failed: {error}", flush=True)


def dump_diag(tag):
    try:
        proc = run_powershell(
            r"Get-Content 'G:\Game\DNF_ACT1\DF\auto-input-diag.log' | Select-Object -Last 10")
        print(f"  [driver] diag[{tag}]:", flush=True)
        for line in proc.stdout.splitlines():
            print(f"    {line}", flush=True)
    except Exception as error:
        print(f"  [driver] diag dump failed: {error}", flush=True)


def drive_plan_for(pid, steps, label, done_timeout=90):
    """Click-focus the target, write the plan, re-focus every 10 s until
    the done marker appears (focus can be stolen; re-clicking re-wakes the
    polling)."""
    click_focus(pid)
    time.sleep(1)
    auto_input.write_plan(CLIENT_DIR, steps)
    print(f"  [driver] {label}: written for {pid} {datetime.datetime.now().isoformat()}", flush=True)
    taken = auto_input.wait_ready(CLIENT_DIR, timeout_s=15)
    print(f"  [driver] {label}: taken={taken} {datetime.datetime.now().isoformat()}", flush=True)
    if not taken:
        raise RuntimeError(f"{label}: wait_ready failed for {pid}")
    deadline = time.monotonic() + done_timeout
    while time.monotonic() < deadline:
        if auto_input.await_done(CLIENT_DIR, timeout_s=10):
            print(f"  [driver] {label}: done=True {datetime.datetime.now().isoformat()}", flush=True)
            return
        print(f"  [driver] {label}: re-focusing {pid} (plan not done yet)", flush=True)
        click_focus(pid)
    raise RuntimeError(f"{label}: did not complete for {pid} in {done_timeout}s")


def drive(message):
    _state["phase"] += 1
    print(f"[driver] phase {_state['phase']}: {message}", flush=True)
    pids = client_pids()
    actor_a, actor_b = pids[0], pids[1]

    if _state["phase"] == 1:
        # Scenario step "A 登录→选角→进城": click-focus A, let it consume.
        time.sleep(8)
        screenshot("1-channel-select", actor_a)
        drive_plan_for(actor_a, ENTER_TOWN, "A enter-town")
        time.sleep(10)
        screenshot("2-town-a", actor_a)
        return

    if _state["phase"] == 2:
        # Scenario step "B 登录→选角→进城": hand focus to B, same plan again.
        drive_plan_for(actor_b, ENTER_TOWN, "B enter-town")
        time.sleep(12)
        screenshot("3-town-b", actor_b)
        dump_diag("both-in-town")
        return

    if _state["phase"] == 3:
        # Scenario step "A 走到 B 附近": A walks; both clients already share
        # the same town area from their saved positions.
        drive_plan_for(actor_a, WALK, "A walk")
        time.sleep(5)
        screenshot("4-walked", actor_a)
        return

    # Final collect confirmation.
    return


def main():
    acc_cli._confirm = drive
    rc = acc_cli.main([
        "run",
        "--scenario", "town-two-clients",
        "--actors", "2",
        "--actor-account", "A=test,B=shizuku",
        "--client-dir", CLIENT_DIR,
        "--runs-dir", r"G:\Game\DNF_ACT1\DFLegacy\runs",
    ])
    print(f"[driver] orchestrate rc={rc}", flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())
