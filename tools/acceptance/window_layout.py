"""Arrange four journal-bound DNF windows in A/B, C/D order without resizing.

Importing this module performs no OS calls. Only arrange() / the CLI moves windows;
there is no keyboard, mouse, focus, capture, launch, or process termination API.
"""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes as w
import datetime as dt
import json
import ntpath
import os
from pathlib import Path
import re
import sys

ROLES = ("A", "B", "C", "D")
GAME_TITLES = {"地下城与勇士", "dungeon & fighter"}
MOVE_FLAGS = 0x0001 | 0x0004 | 0x0010  # NOSIZE | NOZORDER | NOACTIVATE


def plan_grid(work_area, sizes):
    """Pure physical-coordinate planner; None means native sizes do not fit."""
    left, top, right, bottom = work_area
    if right <= left or bottom <= top or set(sizes) != set(ROLES):
        raise ValueError("Invalid work area or four-role sizes")
    if any(len(size) != 2 or min(size) <= 0 for size in sizes.values()):
        raise ValueError("Invalid native window size")
    col_width = max(sizes["A"][0], sizes["C"][0])
    row_height = max(sizes["A"][1], sizes["B"][1])
    width = col_width + max(sizes["B"][0], sizes["D"][0])
    height = row_height + max(sizes["C"][1], sizes["D"][1])
    if width > right - left or height > bottom - top:
        return None
    gap_x = min(8, right - left - width)
    gap_y = min(8, bottom - top - height)
    return {
        "A": [left, top],
        "B": [left + col_width + gap_x, top],
        "C": [left, top + row_height + gap_y],
        "D": [left + col_width + gap_x, top + row_height + gap_y],
    }


def _filetime(value):
    # Preserve the seventh decimal digit emitted by .NET's round-trip format.
    match = re.fullmatch(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d{1,7}))?(Z|\+00:00)", value)
    if not match:
        raise ValueError("StartTimeUtc must be an exact UTC timestamp")
    delta = dt.datetime.fromisoformat(match[1]) - dt.datetime(1601, 1, 1)
    return (delta.days * 86400 + delta.seconds) * 10_000_000 + int((match[2] or "").ljust(7, "0"))


def _path_key(value):
    return ntpath.normcase(ntpath.normpath(value.removeprefix("\\\\?\\")))


def _targets(journal):
    clients = journal["Clients"]
    if not isinstance(clients, list) or len(clients) != 4:
        raise ValueError("Expected exactly four journal clients")
    result = {}
    for client in clients:
        role, pid, path = client["Role"], client["Id"], client["Path"]
        if role not in ROLES or role in result:
            raise ValueError("Expected unique client roles A through D")
        if type(pid) is not int or not 0 < pid <= 0xFFFFFFFF:
            raise ValueError("Invalid client PID")
        if not isinstance(path, str) or len(path) > 32767 or not ntpath.isabs(path) or ntpath.basename(path).casefold() != "dnf.exe":
            raise ValueError("Client path must be an absolute DNF.exe path")
        result[role] = {"role": role, "pid": pid, "path": _path_key(path), "started": _filetime(client["StartTimeUtc"])}
    if len({client["pid"] for client in result.values()}) != 4:
        raise ValueError("Expected four distinct client PIDs")
    return result


class _MonitorInfo(ctypes.Structure):
    _fields_ = [("cbSize", w.DWORD), ("rcMonitor", w.RECT), ("rcWork", w.RECT),
                ("dwFlags", w.DWORD), ("szDevice", w.WCHAR * 32)]


class _Win32:
    def __init__(self):
        if os.name != "nt":
            raise OSError("Window layout requires Windows")
        self.k = ctypes.WinDLL("kernel32", use_last_error=True)
        self.u = ctypes.WinDLL("user32", use_last_error=True)
        self.callback_type = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
        signatures = [
            (self.k, "OpenProcess", [w.DWORD, w.BOOL, w.DWORD], w.HANDLE),
            (self.k, "CloseHandle", [w.HANDLE], w.BOOL),
            (self.k, "GetProcessTimes", [w.HANDLE] + [ctypes.POINTER(w.FILETIME)] * 4, w.BOOL),
            (self.k, "GetExitCodeProcess", [w.HANDLE, ctypes.POINTER(w.DWORD)], w.BOOL),
            (self.k, "QueryFullProcessImageNameW", [w.HANDLE, w.DWORD, w.LPWSTR, ctypes.POINTER(w.DWORD)], w.BOOL),
            (self.u, "EnumWindows", [self.callback_type, w.LPARAM], w.BOOL),
            (self.u, "IsWindow", [w.HWND], w.BOOL),
            (self.u, "IsWindowVisible", [w.HWND], w.BOOL),
            (self.u, "IsIconic", [w.HWND], w.BOOL),
            (self.u, "IsZoomed", [w.HWND], w.BOOL),
            (self.u, "GetWindow", [w.HWND, w.UINT], w.HWND),
            (self.u, "GetWindowThreadProcessId", [w.HWND, ctypes.POINTER(w.DWORD)], w.DWORD),
            (self.u, "GetWindowTextW", [w.HWND, w.LPWSTR, ctypes.c_int], ctypes.c_int),
            (self.u, "GetWindowRect", [w.HWND, ctypes.POINTER(w.RECT)], w.BOOL),
            (self.u, "MonitorFromWindow", [w.HWND, w.DWORD], w.HANDLE),
            (self.u, "GetMonitorInfoW", [w.HANDLE, ctypes.POINTER(_MonitorInfo)], w.BOOL),
            (self.u, "SetThreadDpiAwarenessContext", [w.HANDLE], w.HANDLE),
            (self.u, "SetWindowPos", [w.HWND, w.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, w.UINT], w.BOOL),
        ]
        for dll, name, args, result in signatures:
            method = getattr(dll, name)
            method.argtypes, method.restype = args, result

    @staticmethod
    def check(result, operation):
        if not result:
            raise OSError(ctypes.get_last_error(), operation + " failed")
        return result

    def verify_process(self, target, handle):
        def creation():
            times = [w.FILETIME() for _ in range(4)]
            self.check(self.k.GetProcessTimes(handle, *(ctypes.byref(t) for t in times)), "GetProcessTimes")
            return times[0].dwLowDateTime | (times[0].dwHighDateTime << 32)
        before = creation()
        buffer = ctypes.create_unicode_buffer(32768)
        length = w.DWORD(len(buffer))
        self.check(self.k.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(length)), "QueryFullProcessImageNameW")
        exit_code = w.DWORD()
        self.check(self.k.GetExitCodeProcess(handle, ctypes.byref(exit_code)), "GetExitCodeProcess")
        if before != target["started"] or creation() != target["started"] or exit_code.value != 259 or _path_key(buffer.value) != target["path"]:
            raise ValueError("Client " + target["role"] + " process identity changed or exited")

    def is_game_window(self, hwnd, pid):
        owner_pid = w.DWORD()
        self.u.GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
        if owner_pid.value != pid or not self.u.IsWindow(hwnd) or not self.u.IsWindowVisible(hwnd) or self.u.GetWindow(hwnd, 4):
            return False
        title = ctypes.create_unicode_buffer(512)
        self.u.GetWindowTextW(hwnd, title, len(title))
        return title.value.strip().casefold() in GAME_TITLES

    def windows(self, targets):
        found = {role: [] for role in ROLES}
        @self.callback_type
        def callback(hwnd, _):
            for role in ROLES:
                if self.is_game_window(hwnd, targets[role]["pid"]):
                    found[role].append(hwnd)
            return True
        self.check(self.u.EnumWindows(callback, 0), "EnumWindows")
        if any(len(windows) != 1 for windows in found.values()):
            raise ValueError("Expected exactly one visible unowned game window per PID; counts=" + str({r: len(v) for r, v in found.items()}))
        return {role: found[role][0] for role in ROLES}

    def rect(self, hwnd):
        rect = w.RECT()
        self.check(self.u.GetWindowRect(hwnd, ctypes.byref(rect)), "GetWindowRect")
        return [rect.left, rect.top, rect.right, rect.bottom]


def arrange(journal):
    """Move journal Clients A..D, returning a bounded JSON-compatible receipt.

    journal may be a deployment.json path or a decoded mapping. Path inputs are
    checked for changes before every move and after verification. Failures may
    leave partial moves; original rectangles are retained in the receipt.
    """
    receipt = {"status": "error", "verified": False, "mutated": False, "clients": []}
    api, old_dpi, handles = None, None, {}
    try:
        source = Path(journal) if isinstance(journal, (str, os.PathLike)) else None
        raw = source.read_bytes() if source else None
        if raw is not None and len(raw) > 1_048_576:
            raise ValueError("Journal exceeds 1 MiB")
        data = json.loads(raw.decode("utf-8-sig")) if source else journal
        targets = _targets(data)
        def unchanged():
            if source and source.read_bytes() != raw:
                raise ValueError("Deployment journal changed during window layout")
        api = _Win32()
        old_dpi = api.check(api.u.SetThreadDpiAwarenessContext(w.HANDLE(-4)), "SetThreadDpiAwarenessContext")
        for role in ROLES:
            handles[role] = api.check(api.k.OpenProcess(0x1000, False, targets[role]["pid"]), "OpenProcess")
            api.verify_process(targets[role], handles[role])
        windows = api.windows(targets)
        for role in ROLES:
            hwnd = windows[role]
            if api.u.IsIconic(hwnd) or api.u.IsZoomed(hwnd):
                raise ValueError("Client " + role + " must have a normal, non-minimized window")
            rect = api.rect(hwnd)
            receipt["clients"].append({"role": role, "pid": targets[role]["pid"], "hwnd": hex(hwnd), "before": rect, "after": None, "position": None, "verified": False})
        monitor = api.check(api.u.MonitorFromWindow(windows["A"], 2), "MonitorFromWindow")
        info = _MonitorInfo()
        info.cbSize = ctypes.sizeof(info)
        api.check(api.u.GetMonitorInfoW(monitor, ctypes.byref(info)), "GetMonitorInfoW")
        area = [info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom]
        receipt["monitor"] = {"handle": hex(monitor), "device": info.szDevice, "work_area": area}
        sizes = {row["role"]: [row["before"][2] - row["before"][0], row["before"][3] - row["before"][1]] for row in receipt["clients"]}
        positions = plan_grid(area, sizes)
        unchanged()
        for role in ROLES:
            api.verify_process(targets[role], handles[role])
        if positions is None:
            receipt["status"] = "does_not_fit"
            receipt["error"] = "Four native window sizes do not fit the work area of client A's monitor"
            for row in receipt["clients"]:
                row["after"] = api.rect(windows[row["role"]])
                api.verify_process(targets[row["role"]], handles[row["role"]])
            unchanged()
            return receipt
        for row in receipt["clients"]:
            role, hwnd = row["role"], windows[row["role"]]
            row["position"] = positions[role]
            unchanged()
            api.verify_process(targets[role], handles[role])
            if not api.is_game_window(hwnd, targets[role]["pid"]) or api.rect(hwnd) != row["before"]:
                raise ValueError("Client " + role + " window changed before placement")
            x, y = positions[role]
            if row["before"][:2] != [x, y]:
                # These flags preserve the native D3D surface, focus and Z order.
                api.check(api.u.SetWindowPos(hwnd, None, x, y, 0, 0, MOVE_FLAGS), "SetWindowPos")
                receipt["mutated"] = True
            row["after"] = api.rect(hwnd)
        if api.windows(targets) != windows:
            raise ValueError("Game window identity changed after placement")
        for row in receipt["clients"]:
            role = row["role"]
            api.verify_process(targets[role], handles[role])
            row["after"] = api.rect(windows[role])
            x, y = positions[role]
            width, height = sizes[role]
            row["verified"] = row["after"] == [x, y, x + width, y + height]
        unchanged()
        receipt["verified"] = all(row["verified"] for row in receipt["clients"])
        receipt["status"] = "arranged" if receipt["verified"] else "verification_failed"
        return receipt
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        receipt["error"] = str(error)[:512]
        return receipt
    finally:
        if api:
            for handle in handles.values():
                api.k.CloseHandle(handle)
            if old_dpi and not api.u.SetThreadDpiAwarenessContext(old_dpi):
                receipt.update(status="error", verified=False, error="Failed to restore thread DPI context")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--journal", type=Path, required=True)
    parser.add_argument("--out", type=Path, help="New receipt path; existing files are never overwritten")
    args = parser.parse_args(argv)
    try:
        # Reserve output first so an existing receipt prevents any window move.
        output = args.out.open("x", encoding="utf-8") if args.out else None
        try:
            receipt = arrange(args.journal)
            payload = json.dumps(receipt, ensure_ascii=True, separators=(",", ":"))
            if output:
                output.write(payload + "\n")
        finally:
            if output:
                output.close()
        print(payload)
        return 0 if receipt["verified"] else 2
    except OSError as error:
        print(json.dumps({"status": "error", "verified": False, "mutated": False, "error": str(error)[:512]}, ensure_ascii=True))
        return 2


if __name__ == "__main__":
    sys.exit(main())
