"""组队 + 进本的驱动与判据（B′ 键盘控制，判据按当前服务端协议核对）。

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
_ROSTER = re.compile(r"Party roster (?P<party>\d+) published:[^\n]*?(?P<count>\d+)\s*member\(s\)")
# GameProtocolEngine: CMD 16 = SELECT_DUNGEON, NOTI 29 = START_MAP, CMD 40 = FINISH_LOADING.
# 队长只发一条 CMD 16；队员由服务端 PostEntryRequests 信箱直接入场，不必再上行 CMD 16。
# `partyMode` 是 CurrentHellPartyMode（普通副本为 0），不能据此判断是否组队。
# 严格匹配方向、type、完整 protocol 数字，排除交易回执 16 和反外挂心跳 161。
_SESSION = r"[0-9a-fA-F-]+"
_SELECT_DUNGEON = re.compile(
    rf"\bRX\s+(?P<session>{_SESSION})\s+type=1\s+protocol=16\s+length=16\b")
_START_MAP = re.compile(
    rf"\bTX\s+(?P<session>{_SESSION})\s+game-server\s+type=0\s+protocol=29\s+"
    r"length=\d+\b[^\n]*?\bpayload=(?P<payload>[0-9a-fA-F]+)")
_FINISH_LOADING = re.compile(
    rf"\bRX\s+(?P<session>{_SESSION})\s+type=1\s+protocol=40\s+length=\d+\b")
_INSTANCE_READY = re.compile(
    r"Dungeon (?P<dungeon>\d+) party instance (?P<party>\d+) ready; "
    r"entry requests posted to (?P<count>\d+) member\(s\)\.")
_MEMBER_ENTERED = re.compile(
    r"Party member [^\n]+? \(uid (?P<uid>\d+), slot (?P<slot>\d+)\) entered dungeon "
    r"(?P<dungeon>\d+) instance (?P<party>\d+) room "
    r"\((?P<x>\d+),(?P<y>\d+)\) map (?P<map>\d+)\.")
_ALL_LOADED = re.compile(
    r"Dungeon (?P<dungeon>\d+) party instance (?P<party>\d+): "
    r"all (?P<count>\d+) member\(s\) finished loading;[^\n]*?"
    r"then NOTI 30 broadcast to the whole party\.")


def max_party_members(server_text: str) -> int:
    """服务端广播过名册，取其中最大的成员数。"""
    return max((int(m["count"]) for m in _ROSTER.finditer(server_text)), default=0)


def start_map_count(server_text: str) -> int:
    """实际下行 START_MAP（TX type=0 protocol=29）的数量；数量本身不构成双人通过。"""
    return len(_START_MAP.findall(server_text))


def _completed_party_entry(server_text: str, want_members: int) -> dict:
    """关联最近一轮选图、共享实例、同房间 START_MAP 和各会话的加载完成，防止跨轮拼证。"""
    selections = list(_SELECT_DUNGEON.finditer(server_text))
    if not selections:
        return {}
    selection = selections[-1]
    round_start = selection.start()
    ready_events = list(_INSTANCE_READY.finditer(server_text, round_start))
    if not ready_events:
        return {}
    ready = ready_events[-1]
    party, dungeon = ready["party"], ready["dungeon"]
    if int(ready["count"]) != want_members - 1:
        return {}
    for loaded in _ALL_LOADED.finditer(server_text, ready.end()):
        if (loaded["party"], loaded["dungeon"], int(loaded["count"])) != (
                party, dungeon, want_members):
            continue
        rosters = [m for m in _ROSTER.finditer(server_text, 0, loaded.start()) if m["party"] == party]
        if not rosters or int(rosters[-1]["count"]) < want_members:
            continue
        entries = [m for m in _MEMBER_ENTERED.finditer(server_text, round_start, loaded.start())
                   if (m["party"], m["dungeon"]) == (party, dungeon)]
        if len({m["uid"] for m in entries}) < want_members - 1:
            continue
        # START_MAP 前九字节 = x/y/随机种子/state/map id；两端必须得到同一房间快照。
        # 使用每会话本轮最后一条，重复发给同一个人不能凑成两人，换房也不能沿用旧加载证据。
        starts = {m["session"].lower(): m
                  for m in _START_MAP.finditer(server_text, round_start, loaded.start())}
        groups: dict[str, dict] = {}
        for session, start in starts.items():
            payload = start["payload"]
            if len(payload) >= 18 and payload[12:14] != "00":
                groups.setdefault(payload[:18].upper(), {})[session] = start
        for snapshot, sessions in groups.items():
            if len(sessions) != want_members or selection["session"].lower() not in sessions:
                continue
            header = bytes.fromhex(snapshot)
            room = (header[0], header[1], int.from_bytes(header[7:9], "little"))
            if any((int(m["x"]), int(m["y"]), int(m["map"])) != room for m in entries):
                continue
            finished = {m["session"].lower()
                        for m in _FINISH_LOADING.finditer(server_text, round_start, loaded.start())
                        if m["session"].lower() in sessions
                        and m.start() > sessions[m["session"].lower()].start()}
            if finished == set(sessions):
                return {"instance_id": int(party), "dungeon_id": int(dungeon),
                        "start_map_sessions": sorted(sessions), "loaded_sessions": sorted(finished)}
    return {}


def judge(server_text: str, want_members: int = 2) -> dict:
    members = max_party_members(server_text)
    starts = start_map_count(server_text)
    party_ok = members >= want_members
    completed = _completed_party_entry(server_text, want_members)
    dungeon_ok = bool(completed)
    if party_ok and dungeon_ok:
        result = "pass"
    elif starts > 0 and not party_ok:
        result = "fail"      # 单人进了本 = 组队没生效
    elif party_ok:
        result = "fail"      # 成队但缺少本轮双人共享实例与全员加载证据
    elif members == 0 and starts == 0:
        result = "missing-evidence"
    else:
        result = "needs-human"
    return {"result": result, "max_members": members, "start_map_count": starts,
            "party_ok": party_ok, "two_player_dungeon": dungeon_ok, **completed}


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
# 合成正例，不代表真机已经通过：包格式见 EntranceService.LogPacket，29/40 的实际示例
# 来自 work/scout/server3.log:144/149；共享实例业务行逐字对应当前 EntranceService。
_SAMPLE_PASS = """
Party roster 1 published: nameSel 6->6, 1 member(s).
Party roster 1 published: nameSel 6->6, 2 member(s).
RX 6879107d-24d5-4086-adb3-a2a74c7256a7 type=1 protocol=16 length=16 records=30 crc=ED984599 valid=True body=1E0028000000
TX 6879107d-24d5-4086-adb3-a2a74c7256a7 game-server type=0 protocol=29 length=54 payload=00033FC5B5E901E11504000000F4032D000000010100F4032D000000020200F4032D000000030300F4032D0000000000
Dungeon 40 party instance 1 ready; entry requests posted to 1 member(s).
Dungeon 40 selected maze 0, start (0,3), boss (0,0), start map 5601, monsters=4, hellMode=False, partyMode=0, ticket=0.
TX 9f6f7c0c-8865-4b70-882b-a191a59c5c95 game-server type=0 protocol=29 length=54 payload=00033FC5B5E901E11504000000F4032D000000010100F4032D000000020200F4032D000000030300F4032D0000000000
Party member mageco (uid 2, slot 1) entered dungeon 40 instance 1 room (0,3) map 5601.
RX 6879107d-24d5-4086-adb3-a2a74c7256a7 type=1 protocol=40 length=20 records=31 crc=A2D6EACF valid=True body=1F006330343329040533
Dungeon 40 party instance 1: Summoner finished loading; waiting for the remaining member(s).
RX 9f6f7c0c-8865-4b70-882b-a191a59c5c95 type=1 protocol=40 length=20 records=32 crc=FCA7C9CB valid=True body=2000000038F85C3409F8
Dungeon 40 party instance 1: all 2 member(s) finished loading; roster replayed for 1 other member(s), then NOTI 30 broadcast to the whole party.
"""
_SAMPLE_ONE_PLAYER = """
Party roster 1 published: nameSel 6->6, 1 member(s).
RX 6879107d-24d5-4086-adb3-a2a74c7256a7 type=1 protocol=16 length=16 records=30 crc=ED984599 valid=True
TX 6879107d-24d5-4086-adb3-a2a74c7256a7 game-server type=0 protocol=29 length=54 payload=00033FC5B5E901E11504000000F4032D000000010100F4032D000000020200F4032D000000030300F4032D0000000000
RX 6879107d-24d5-4086-adb3-a2a74c7256a7 type=1 protocol=40 length=20 records=31 crc=A2D6EACF valid=True body=1F006330343329040533
"""
_SAMPLE_PARTY_ONLY = """
Party roster 1 published: nameSel 6->6, 2 member(s).
"""
_SAMPLE_EMPTY = "Entrance listener ready at 0.0.0.0:7001\n"
# 真机 2026-10-23 的假信号：交易回执、SELECT_DUNGEON 和 partyMode 都不能证明双人加载完成。
_SAMPLE_TRADE_TX_ONLY = """
Party roster 1 published: nameSel 0->1, 2 member(s).
TX 522ae4e1 game-server type=1 protocol=16 length=8 payload=0000
TX fce11ca1 game-server type=1 protocol=16 length=8 payload=0000
RX 522ae4e1 type=1 protocol=16 length=16 records=30 crc=AA valid=True
Dungeon 40 selected maze 0, start (0,3), boss (0,0), start map 5601, monsters=4, partyMode=0
"""
# `protocol=161`（反外挂心跳）不能被当成 `protocol=16`。
_SAMPLE_HEARTBEAT_ONLY = """
Party roster 1 published: nameSel 0->1, 2 member(s).
RX 522ae4e1 type=1 protocol=161 length=28 records=80 crc=BB valid=True
"""


def self_check() -> int:
    failures: list[str] = []

    def expect(name: str, text: str, want: str) -> None:
        got = judge(text)["result"]
        if got != want:
            failures.append(f"{name}: 期望 {want} 实得 {got}")

    expect("共享实例两会话 START_MAP 与全员加载完成", _SAMPLE_PASS, "pass")
    expect("单人 START_MAP 与加载完成", _SAMPLE_ONE_PLAYER, "fail")
    expect("成队没进本", _SAMPLE_PARTY_ONLY, "fail")
    expect("什么都没有", _SAMPLE_EMPTY, "missing-evidence")
    expect("TX protocol=16（交易回执）不算 START_MAP", _SAMPLE_TRADE_TX_ONLY, "fail")
    expect("protocol=161 心跳不算 START_MAP", _SAMPLE_HEARTBEAT_ONLY, "fail")
    expect("重复给同会话发 START_MAP 不算双人", _SAMPLE_PASS.replace(
        "TX 9f6f7c0c-8865-4b70-882b-a191a59c5c95", "TX 6879107d-24d5-4086-adb3-a2a74c7256a7"), "fail")
    expect("一端加载证据缺失", "\n".join(line for line in _SAMPLE_PASS.splitlines()
        if not line.startswith("RX 9f6f7c0c-8865-4b70-882b-a191a59c5c95 type=1 protocol=40")), "fail")

    if max_party_members(_SAMPLE_PASS) != 2:
        failures.append("名册成员数解析错")
    if start_map_count(_SAMPLE_PASS) != 2:
        failures.append("TX NOTI 29 计数错")
    if start_map_count(_SAMPLE_TRADE_TX_ONLY) != 0:
        failures.append("TX protocol=16 被误计为 START_MAP")
    if start_map_count(_SAMPLE_HEARTBEAT_ONLY) != 0:
        failures.append("protocol=161 被误计为 START_MAP")

    for line in failures:
        print("SELF-CHECK FAIL: " + line)
    if failures:
        return 1
    print("party_dungeon self-check: 8 verdicts + 4 parsers passed")
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
