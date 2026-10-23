"""B' T3 gate driver: run handshake-login-town with ZERO manual UI.

Reuses acceptance.cli.orchestrate wholesale (server isolation, launcher,
PID attribution, netdump collection, judging) and replaces only the human
confirmation hook with the synthetic-input driver: each scenario step is
executed by writing auto-input.json plans through acceptance.auto_input
and waiting on the taken/done markers.

Run from anywhere: python t3_drive.py
"""
import datetime
import subprocess
import sys
import time

sys.path.insert(0, r"G:\Game\DNF_ACT1\DFLegacy\tools")

from acceptance import auto_input  # noqa: E402
from acceptance import cli as acc_cli  # noqa: E402

CLIENT_DIR = r"G:\Game\DNF_ACT1\DF"
WORK_DIR = r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-22-client-auto-input\work"
CAPTURE = WORK_DIR + r"\capture.ps1"

_state = {"phase": 0}


def screenshot(tag):
    """Best-effort screen capture for the runbook evidence trail."""
    try:
        cmd = ("$p = Get-Process DNF | Where-Object { $_.MainWindowHandle -ne 0 }"
               " | Select-Object -First 1; if ($p) { & '" + CAPTURE + "' -Pid2 $p.Id"
               " -OutPath '" + WORK_DIR + "\\t3-" + tag + ".png' }")
        proc = subprocess.run(
            ["powershell", "-NoProfile", "-Command", cmd],
            capture_output=True, text=True, timeout=30)
        print(f"  [driver] screenshot {tag}: {proc.stdout.strip()} {proc.stderr.strip()}",
              flush=True)
    except Exception as error:  # evidence capture must never break the run
        print(f"  [driver] screenshot {tag} failed: {error}", flush=True)


def drive_plan(steps, label, done_timeout=60):
    auto_input.write_plan(CLIENT_DIR, steps)
    print(f"  [driver] {label}: written {datetime.datetime.now().isoformat()}", flush=True)
    taken = auto_input.wait_ready(CLIENT_DIR, timeout_s=15)
    print(f"  [driver] {label}: taken={taken}", flush=True)
    done = auto_input.await_done(CLIENT_DIR, timeout_s=done_timeout)
    print(f"  [driver] {label}: done={done} {datetime.datetime.now().isoformat()}", flush=True)
    if not (taken and done):
        raise RuntimeError(f"{label} did not complete (taken={taken}, done={done})")


def drive(message):
    """Replaces cli._confirm: runs the scripted UI for each scenario step."""
    _state["phase"] += 1
    print(f"[driver] phase {_state['phase']}: {message}", flush=True)

    if _state["phase"] == 1:
        # Scenario step "完成握手并登录": the launcher already authenticated the
        # account and the client sits on the channel-select screen with the
        # entrance handshake frames in the netdump. Wait for the screen to
        # settle; no UI action needed.
        time.sleep(10)
        screenshot("1-channel-select")
        return

    if _state["phase"] == 2:
        # Scenario step "选角并进入城镇": channel select -> character select
        # -> town, entirely through synthetic input.
        drive_plan([
            {"t": 500, "op": "click", "x": 127, "y": 97},    # channel row
            {"t": 1600, "op": "click", "x": 320, "y": 448},  # 开始 (join)
        ], "channel-join")
        time.sleep(7)  # channel join + character-select load (+ notice dialog)
        screenshot("2-character-select")
        drive_plan([
            {"t": 400, "op": "click", "x": 318, "y": 267},   # 确认 (notice dialog; no-op if absent)
            {"t": 1100, "op": "click", "x": 110, "y": 140},  # character slot (summoner)
            {"t": 1900, "op": "click", "x": 320, "y": 448},  # 开始 -> town
        ], "character-and-town", done_timeout=60)
        print("  [driver] town entry requested; waiting for the load", flush=True)
        time.sleep(15)
        screenshot("3-town")
        return

    # Final "采集结束" confirmation: nothing to do, let the CLI collect.
    return


def main():
    acc_cli._confirm = drive
    rc = acc_cli.main([
        "run",
        "--scenario", "handshake-login-town",
        "--actors", "1",
        "--actor-account", "A=test",
        "--client-dir", CLIENT_DIR,
        "--runs-dir", r"G:\Game\DNF_ACT1\DFLegacy\runs",
    ])
    print(f"[driver] orchestrate rc={rc}", flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())
