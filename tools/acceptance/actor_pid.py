"""从 launcher.log 追加行中归因客户端 PID（不猜、不输出密码）。

仅读 start_offset 之后新增的 UTF-8 行，忽略历史。
恰好一条启动事件 → 返回 (pid, 原始行, 读取后偏移)。
零条等到超时 → PIDTimeoutError；多条 → AmbiguousPIDError；Launcher failed → LauncherFailedError。
实际 Launcher 可在写入启动行后退出，故纯读文件、不依赖进程存活。
"""
from __future__ import annotations

import re
import time
from pathlib import Path

_START_RE = re.compile(r"Started DFLegacy client process (\d+);")
_FAILED_RE = re.compile(r"Launcher failed")


class AmbiguousPIDError(RuntimeError):
    """多条启动事件，无法判定哪条属于本次 run。"""


class LauncherFailedError(RuntimeError):
    """Launcher 启动失败。"""


class PIDTimeoutError(RuntimeError):
    """超时未等到启动事件。"""


def wait_for_client_pid(
    log_path: Path,
    start_offset: int,
    timeout_s: float = 30.0,
    poll_interval_s: float = 0.1,
) -> tuple[int, str, int]:
    """从 log_path 的 start_offset 之后等待恰好一条启动事件。

    返回 (pid, 原始行, 读取后偏移)。
    """
    deadline = time.monotonic() + timeout_s
    while True:
        with open(log_path, "rb") as fh:
            fh.seek(start_offset)
            new_bytes = fh.read()
            new_offset = fh.tell()

        new_text = new_bytes.decode("utf-8", errors="replace")

        if _FAILED_RE.search(new_text):
            raise LauncherFailedError(f"Launcher failed in {log_path}")

        matches: list[tuple[int, str]] = []
        for line in new_text.splitlines():
            m = _START_RE.search(line)
            if m:
                matches.append((int(m.group(1)), line))

        if len(matches) == 1:
            pid, line = matches[0]
            return pid, line, new_offset
        if len(matches) > 1:
            raise AmbiguousPIDError(
                f"Found {len(matches)} start events in {log_path}, cannot determine which is current"
            )

        if time.monotonic() >= deadline:
            raise PIDTimeoutError(
                f"No start event found in {log_path} after {timeout_s}s"
            )

        time.sleep(poll_interval_s)
