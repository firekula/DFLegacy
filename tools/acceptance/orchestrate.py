"""编排入口的纯辅助函数（B 节 one-command amendment）。

无进程启动、无 I/O 副作用（write_steps 是唯一的文件写入，且幂等可覆盖）。
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .actor_pid import wait_for_client_pid
from .model import RunPaths


def parse_actor_accounts(spec: str, known: set[str]) -> dict[str, str]:
    """解析 `A=alice,B=bob` 为 {actor_name: account_alias}。

    空段跳过；无 `=`、名字不在场景、同名字重复出现都 ValueError。
    返回账号别名（不含密码）。
    """
    result: dict[str, str] = {}
    for segment in spec.split(","):
        segment = segment.strip()
        if not segment:
            continue
        if "=" not in segment:
            raise ValueError(f"条目不含 '='：{segment!r}")
        name, account = (x.strip() for x in segment.split("=", 1))
        if not name:
            raise ValueError(f"actor 名为空：{segment!r}")
        if name not in known:
            raise ValueError(f"actor {name!r} 不在场景中；已声明 {sorted(known)}")
        if name in result:
            raise ValueError(f"actor {name!r} 重复出现")
        result[name] = account
    return result


def launcher_command(launcher_exe: Path, account: str) -> list[str]:
    """构造 Launcher 命令行。account 为空时 ValueError。"""
    if not account:
        raise ValueError("account 不能为空")
    return [str(launcher_exe), "--headless", "--no-dialog", "--account", account]


def capture_client_pid(
    launcher_log: Path,
    start_offset: int,
    timeout_s: float = 60.0,
) -> tuple[int, str, int]:
    """薄封装 actor_pid.wait_for_client_pid，转发参数。"""
    return wait_for_client_pid(launcher_log, start_offset, timeout_s=timeout_s)


def write_steps(rp: RunPaths, entries: list[dict[str, Any]]) -> Path:
    """写 steps.json（UTF-8、ensure_ascii=False、可重复覆盖），返回路径。"""
    dest = rp.run_dir / "steps.json"
    rp.run_dir.mkdir(parents=True, exist_ok=True)
    dest.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return dest