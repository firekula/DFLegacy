"""actor_pid：从 launcher.log 追加行归因客户端 PID（不猜、不输出密码）。"""
import pytest

from acceptance.actor_pid import (AmbiguousPIDError, LauncherFailedError,
                                  PIDTimeoutError, wait_for_client_pid)

HISTORY = (
    "[2026-10-05T00:36:14.0335872+08:00] Started DFLegacy client process 88136; entrance=127.0.0.1:2311.\n"
    "[2026-10-05T00:47:24.8657701+08:00] Started DFLegacy client process 53916; entrance=127.0.0.1:2311.\n"
)

NEW_LINE = "[2026-10-05T00:52:56.6780492+08:00] Started DFLegacy client process 68360; entrance=127.0.0.1:2311.\n"


def test_history_is_ignored(tmp_path):
    """start_offset 之前的历史行不参与判定。"""
    p = tmp_path / "launcher.log"
    p.write_text(HISTORY, encoding="utf-8")
    offset = p.stat().st_size
    with open(p, "a", encoding="utf-8") as f:
        f.write(NEW_LINE)
    pid, line, new_offset = wait_for_client_pid(p, offset, timeout_s=1.0)
    assert pid == 68360
    assert "68360" in line
    assert new_offset == p.stat().st_size


def test_single_start_line_returns_pid_line_offset(tmp_path):
    """恰好一条启动事件 → 返回 PID/原始行/偏移。"""
    p = tmp_path / "launcher.log"
    p.write_text(NEW_LINE, encoding="utf-8")
    pid, line, new_offset = wait_for_client_pid(p, 0, timeout_s=1.0)
    assert pid == 68360
    assert "Started DFLegacy client process 68360" in line
    assert new_offset == p.stat().st_size


def test_zero_start_lines_times_out(tmp_path):
    """零条启动事件等到超时 → PIDTimeoutError。"""
    p = tmp_path / "launcher.log"
    p.write_text("# no start events here\n", encoding="utf-8")
    with pytest.raises(PIDTimeoutError):
        wait_for_client_pid(p, 0, timeout_s=0.2, poll_interval_s=0.05)


def test_multiple_start_lines_raises_ambiguity(tmp_path):
    """多条启动事件 → AmbiguousPIDError（不能猜）。"""
    p = tmp_path / "launcher.log"
    p.write_text(HISTORY, encoding="utf-8")
    with pytest.raises(AmbiguousPIDError):
        wait_for_client_pid(p, 0, timeout_s=1.0)


def test_launcher_failed_raises(tmp_path):
    """Launcher failed → LauncherFailedError。"""
    p = tmp_path / "launcher.log"
    p.write_text("Launcher failed: connection refused\n", encoding="utf-8")
    with pytest.raises(LauncherFailedError):
        wait_for_client_pid(p, 0, timeout_s=1.0)
