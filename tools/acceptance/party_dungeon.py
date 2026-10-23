"""组队 + 进本的驱动与判据（B′ 键盘控制，2026-10-23 真机命中后固化）。

## 这些坐标是怎么来的（全部真机实测，客户区 640×480）
| 步骤 | 动作 |
|---|---|
| 进城 | `click(127,97)` → `(320,448)` → `(318,267)` → `(110,140)` → `(320,448)` |
| 城门区 → 城镇本体 | `key DOWN` 800 ms（5/1 → 5/0） |
| 分开站位 | `key RIGHT` 800 ms（两人重叠时没法点对方） |
| **点对方角色** | **OS 左键 (330,282)** ← 必须在"分站后"这个相机状态下；命中会弹玩家菜单 |
| 菜单「邀请组队」 | `(390,290)`（菜单第一项） |
| 队伍信息图标 / 创建队伍 / 确认 | `(567,97)` / `(325,295)` / `(325,325)` |
| 城镇(5/0) → 门口区(5/3) | `key LEFT` 6000 ms |
| 走进进图门 | `key LEFT` 3000 ms（门矩形 `x 14..33, y 354..479`；**`state=2` 才继续**） |
| 选图「前往地下城」 | `(447,395)` |

## 三条方法学结论（别再重犯）
1. **世界内的点击必须走 OS 级**（`SetCursorPos`+`mouse_event`）；窗口消息坐标只对 UI 按钮有效。
2. **判别"是否打中角色"看角色有没有走动**：走动 = 被当成点地面；不走动且出菜单 = 命中。
3. **菜单是左键出的**（右键那条路是仓库旧结论，已作废）。

## 一条命令
    cd tools
    python -m acceptance.party_dungeon --server-log <run>\\server\\server-A.log \\
        --a-pid <pid> --b-pid <pid> --out-dir <dir> \\
        [--accept-x 300 --accept-y 300]        # B 侧应答窗的"接受"按钮，量一次即可复用

不给 `--accept-*` 时，本工具会做到"A 已发出邀请"并把 **B 的画面截下来**再以
`needs-human` 收口（接受按钮的坐标必须实测，不能猜）。
"""
from __future__ import annotations

import argparse
import dataclasses
import json
import os
import pathlib
import re
import sys
import time
from typing import Optional, Sequence

from acceptance import auto_input
from acceptance.combat_gate import _run, _write_script, dnf_pid

# ----------------------------------------------------------------------------
# 坐标表（真机实测，客户区坐标）
# ----------------------------------------------------------------------------
ENTER_TOWN: tuple[dict, ...] = (
    {"t": 500, "op": "click", "x": 127, "y": 97},     # 频道行
    {"t": 1700, "op": "click", "x": 320, "y": 448},   # 开始（进频道）
    {"t": 3200, "op": "click", "x": 318, "y": 267},   # 公告确认（无公告则无害）
    {"t": 4200, "op": "click", "x": 110, "y": 140},   # 角色槽
    {"t": 5200, "op": "click", "x": 320, "y": 448},   # 开始 → 城镇
)
DOWN_TO_TOWN_CENTER: tuple[dict, ...] = ({"t": 300, "op": "key", "vk": 0x28, "dur": 800},)
SEPARATE: tuple[dict, ...] = ({"t": 300, "op": "key", "vk": 0x27, "dur": 800},)
WALK_TO_GATE: tuple[dict, ...] = ({"t": 300, "op": "key", "vk": 0x25, "dur": 6000},)
WALK_INTO_PORTAL: tuple[dict, ...] = ({"t": 300, "op": "key", "vk": 0x25, "dur": 3000},)
PEER_CLICK = (330, 282)          # 分站后对方角色的位置（OS 左键）
INVITE_MENU_ITEM = (390, 290)    # 菜单第一项「邀请组队」
DUNGEON_SELECT_GO = (447, 395)   # 选图界面「前往地下城」
ACCEPT_YES = (505, 383)          # 对方侧「是否接受组队」应答窗的「是」

# ----------------------------------------------------------------------------
# 判据（纯函数，跑在服务端日志文本上）
# ----------------------------------------------------------------------------
_ROSTER = re.compile(r"Party roster\b.*?(\d+)\s*member\(s\)")
_START_MAP = re.compile(r"protocol=16\b")


def max_party_members(server_text: str) -> int:
    """服务端广播过名册，取其中最大的成员数。"""
    return max((int(m.group(1)) for m in _ROSTER.finditer(server_text)), default=0)


def start_map_count(server_text: str) -> int:
    """`protocol=16`（START_MAP）出现次数：双人进本应为 2。"""
    return len(_START_MAP.findall(server_text))


def judge(server_text: str, want_members: int = 2) -> dict:
    members = max_party_members(server_text)
    starts = start_map_count(server_text)
    party_ok = members >= want_members
    dungeon_ok = starts >= 2
    if party_ok and dungeon_ok:
        result = "pass"
    elif starts > 0 and not party_ok:
        result = "fail"      # 单人进了本 = 组队没生效
    elif party_ok and starts == 0:
        result = "fail"      # 成队但没进本
    elif members == 0 and starts == 0:
        result = "missing-evidence"
    else:
        result = "needs-human"
    return {"result": result, "max_members": members, "start_map_count": starts,
            "party_ok": party_ok, "two_player_dungeon": dungeon_ok}


# ----------------------------------------------------------------------------
# live：OS 级点击 + 计划驱动
# ----------------------------------------------------------------------------
_OS_CLICK_PS = r"""
param([int]$Pid2, [int]$ClientX, [int]$ClientY, [string]$Button = "left")
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class PdClick {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
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
[PdClick]::SetWindowPos($hwnd, [IntPtr](-1), 0,0,0,0, 0x0002 -bor 0x0001 -bor 0x0010) | Out-Null
[PdClick]::SetWindowPos($hwnd, [IntPtr](-2), 0,0,0,0, 0x0002 -bor 0x0001 -bor 0x0010) | Out-Null
for ($i = 0; $i -lt 5; $i++) {
    if ([PdClick]::GetForegroundWindow() -eq $hwnd) { break }
    [uint32]$fgPid = 0
    $fg = [PdClick]::GetWindowThreadProcessId([PdClick]::GetForegroundWindow(), [ref]$fgPid)
    $cur = [PdClick]::GetCurrentThreadId()
    $att = $false
    if ($fg -ne 0 -and $fg -ne $cur) { $att = [PdClick]::AttachThreadInput($cur, $fg, $true) }
    [PdClick]::BringWindowToTop($hwnd) | Out-Null
    [PdClick]::SetActiveWindow($hwnd) | Out-Null
    [PdClick]::SetForegroundWindow($hwnd) | Out-Null
    if ($att) { [PdClick]::AttachThreadInput($cur, $fg, $false) | Out-Null }
    Start-Sleep -Milliseconds 200
}
$pt = New-Object PdClick+POINT
$pt.X = $ClientX; $pt.Y = $ClientY
[PdClick]::ClientToScreen($hwnd, [ref]$pt) | Out-Null
[PdClick]::SetCursorPos($pt.X, $pt.Y) | Out-Null
Start-Sleep -Milliseconds 250
$down = if ($Button -eq "left") { 0x0002 } else { 0x0008 }
$up = if ($Button -eq "left") { 0x0004 } else { 0x0010 }
[PdClick]::mouse_event($down, 0,0,0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 70
[PdClick]::mouse_event($up, 0,0,0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 350
Write-Host ("os-click pid={0} client=({1},{2}) screen=({3},{4}) button={5}" -f $Pid2, $ClientX, $ClientY, $pt.X, $pt.Y, $Button)
"""

_CAPTURE_PS = r"""
param([int]$Pid2, [string]$OutPath)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class PdCap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
}
"@
$proc = Get-Process -Id $Pid2 -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
$rect = New-Object PdCap+RECT
[PdCap]::GetClientRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$pt = New-Object PdCap+POINT
[PdCap]::ClientToScreen($hwnd, [ref]$pt) | Out-Null
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($pt.X, $pt.Y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved=$OutPath"
"""


def os_click(pid: int, point: tuple[int, int], button: str = "left") -> str:
    script = _write_script(_OS_CLICK_PS, "dfl-pd-click.ps1")
    return _run(_powershell_command(
        _OS_CLICK_PS, ["-Pid2", str(pid), "-ClientX", str(point[0]),
                       "-ClientY", str(point[1]), "-Button", button], script), 120)


def capture(pid: int, out_path: pathlib.Path) -> str:
    script = _write_script(_CAPTURE_PS, "dfl-pd-capture.ps1")
    return _run(_powershell_command(
        _CAPTURE_PS, ["-Pid2", str(pid), "-OutPath", str(out_path)], script), 120)


def _powershell_command(script_text: str, args: Sequence[str],
                        script_path: Optional[os.PathLike] = None) -> list[str]:
    """与 combat_gate 同一条规则：有参数走 -File，永不拼 `--`（PS 5.1 会吃掉它）。"""
    if args:
        if not script_path:
            raise ValueError("带参数必须给脚本文件路径")
        return ["powershell", "-NoProfile", "-File", str(script_path)] + list(args)
    return ["powershell", "-NoProfile", "-Command", script_text]


def _plan(pid: int, steps: Sequence[dict], label: str, done_timeout: int = 90) -> bool:
    os_click(pid, (30, 30))  # 聚焦 + 唤醒输入（左上角不会点到 NPC）
    auto_input.write_plan(client_dir(), list(steps))
    taken = auto_input.wait_ready(client_dir(), timeout_s=15)
    done = auto_input.await_done(client_dir(), timeout_s=done_timeout)
    print(f"  [plan] {label} pid={pid}: taken={taken} done={done}", flush=True)
    return bool(taken and done)


def client_dir() -> pathlib.Path:
    return pathlib.Path(os.environ.get("DFL_CLIENT_DIR", r"G:\Game\DNF_ACT1\DF"))


def run_live(a_pid: int, b_pid: int, server_log: pathlib.Path,
             out_dir: pathlib.Path, accept_point: Optional[tuple[int, int]] = None) -> dict:
    out_dir.mkdir(parents=True, exist_ok=True)
    log = pathlib.Path(server_log)
    steps: list[str] = []

    steps.append(f"A 进城: {_plan(a_pid, ENTER_TOWN, 'A-enter-town')}")
    time.sleep(12)
    steps.append(f"B 进城: {_plan(b_pid, ENTER_TOWN, 'B-enter-town')}")
    time.sleep(12)
    steps.append(f"A→5/0: {_plan(a_pid, DOWN_TO_TOWN_CENTER, 'A-down')}")
    time.sleep(2)
    steps.append(f"B→5/0: {_plan(b_pid, DOWN_TO_TOWN_CENTER, 'B-down')}")
    time.sleep(2)
    capture(a_pid, out_dir / "01-A-town.png")
    capture(b_pid, out_dir / "02-B-town.png")

    steps.append(f"A 分站: {_plan(a_pid, SEPARATE, 'A-separate')}")
    time.sleep(2)
    capture(a_pid, out_dir / "03-A-separated.png")

    print(f"  [os-click] 点对方角色 {PEER_CLICK}", flush=True)
    os_click(a_pid, PEER_CLICK)
    capture(a_pid, out_dir / "04-A-peer-menu.png")

    print(f"  [os-click] 点「邀请组队」{INVITE_MENU_ITEM}", flush=True)
    os_click(a_pid, INVITE_MENU_ITEM)
    time.sleep(1)
    capture(a_pid, out_dir / "05-A-after-invite.png")
    capture(b_pid, out_dir / "06-B-invite-dialog.png")

    verdict: dict = {"steps": steps, "accept_point": accept_point}
    if accept_point is not None:
        print(f"  [os-click] B 接受 {accept_point}", flush=True)
        os_click(b_pid, accept_point)
        time.sleep(2)
        capture(b_pid, out_dir / "07-B-after-accept.png")
        text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
        verdict["party"] = judge(text)
        if verdict["party"]["party_ok"]:
            print("  === 成队，队长进本", flush=True)
            _plan(a_pid, WALK_TO_GATE, "A-walk-gate")
            _plan(a_pid, WALK_INTO_PORTAL, "A-walk-portal")
            time.sleep(2)
            capture(a_pid, out_dir / "08-A-map-select.png")
            os_click(a_pid, DUNGEON_SELECT_GO)
            time.sleep(12)
            capture(a_pid, out_dir / "09-A-dungeon.png")
            capture(b_pid, out_dir / "10-B-dungeon.png")
    else:
        verdict["party"] = {"result": "needs-human",
                            "reason": "未给 --accept-x/--accept-y：已发出邀请，等人工确认 B 侧应答窗按钮坐标"}

    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    verdict["log"] = judge(text)
    (out_dir / "party-dungeon.json").write_text(
        json.dumps(verdict, ensure_ascii=False, indent=2), encoding="utf-8")
    return verdict


# ----------------------------------------------------------------------------
# 自检
# ----------------------------------------------------------------------------
_SAMPLE_PASS = """
Party roster 1 published: nameSel 6->6, 1 member(s).
Party roster 1 published: nameSel 6->6, 2 member(s).
RX 6879107d type=1 protocol=16 length=16 records=30 crc=ED984599 valid=True
RX 7a8b type=1 protocol=16 length=16 records=31 crc=11 valid=True
"""
_SAMPLE_ONE_PLAYER = """
Party roster 1 published: nameSel 6->6, 1 member(s).
RX 6879107d type=1 protocol=16 length=16 records=30 crc=ED984599 valid=True
"""
_SAMPLE_PARTY_ONLY = """
Party roster 1 published: nameSel 6->6, 2 member(s).
"""
_SAMPLE_EMPTY = "Entrance listener ready at 0.0.0.0:7001\n"


def self_check() -> int:
    failures: list[str] = []

    def expect(name: str, text: str, want: str) -> None:
        got = judge(text)["result"]
        if got != want:
            failures.append(f"{name}: 期望 {want} 实得 {got}")

    expect("双人成队+两次 START_MAP", _SAMPLE_PASS, "pass")
    expect("只有一个 16（单人进本）", _SAMPLE_ONE_PLAYER, "fail")
    expect("成队没进本", _SAMPLE_PARTY_ONLY, "fail")
    expect("什么都没有", _SAMPLE_EMPTY, "missing-evidence")

    if max_party_members(_SAMPLE_PASS) != 2:
        failures.append("名册成员数解析错")
    if start_map_count(_SAMPLE_PASS) != 2:
        failures.append("protocol=16 计数错")

    for line in failures:
        print("SELF-CHECK FAIL: " + line)
    if failures:
        return 1
    print("party_dungeon self-check: 4 verdicts + 2 parsers passed")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="组队 + 双人进本驱动与判据")
    parser.add_argument("--self-check", action="store_true")
    parser.add_argument("--server-log", default=None)
    parser.add_argument("--a-pid", type=int, default=0)
    parser.add_argument("--b-pid", type=int, default=0)
    parser.add_argument("--out-dir", default=None)
    parser.add_argument("--accept-x", type=int, default=None)
    parser.add_argument("--accept-y", type=int, default=None)
    args = parser.parse_args(argv)

    if args.self_check:
        return self_check()

    a_pid = args.a_pid or dnf_pid()
    b_pid = args.b_pid
    if not a_pid or not b_pid:
        print("需要 --a-pid 与 --b-pid（或先跑一个客户端让 dnf_pid 找到 A）")
        return 2
    accept = (args.accept_x, args.accept_y) if args.accept_x is not None and args.accept_y is not None else None
    out_dir = pathlib.Path(args.out_dir) if args.out_dir else client_dir() / "party-dungeon"
    verdict = run_live(a_pid, b_pid, pathlib.Path(args.server_log) if args.server_log
                       else pathlib.Path("server.log"), out_dir, accept)
    print(json.dumps(verdict, ensure_ascii=False, indent=2))
    return 0 if verdict["log"]["result"] == "pass" else 1


if __name__ == "__main__":
    sys.exit(main())
