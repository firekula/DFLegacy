#!/usr/bin/env python3
r"""vmrun.py —— 把一段脚本推到 VM 上执行（绕开 PowerShell/引号地狱）。

## 为什么需要它

要在 VM 上跑的查询越来越长（多行 SQL、`find`、`awk`），而我们的调用链是
PowerShell -> ssh -> bash -> mysql，每一层都要吃掉一层引号。实测这会在
`--socket=...`、`"..."`、`;` 上反复失败（本会话已踩 3 次）。

做法：把脚本写成**本地文件**（用编辑工具写、带完整语法），本工具用 SFTP 传上去，
再用 `bash` 执行。这样脚本内容**一个字符都不经过 shell 解析**。

## 用法

    python vmrun.py <本地脚本> [传给脚本的参数...]
    python vmrun.py --sql "select 1"            # 便捷：把 SQL 直接喂给 mysql

环境变量 `VM_PASS` 提供口令（见 vmssh.py 的纪律说明）。
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vmssh import connect                      # noqa: E402

REMOTE_DIR = "/tmp/dfl"

MYSQL = "mysql -uroot -p88888888 --socket=/opt/lampp/var/mysql/mysql.sock"


def run_script(script: str, args: list[str]) -> int:
    cli = connect()
    try:
        sftp = cli.open_sftp()
        try:
            sftp.mkdir(REMOTE_DIR) if REMOTE_DIR not in sftp.listdir("/tmp") else None
        except OSError:
            pass
        remote = f"{REMOTE_DIR}/probe.sh"
        with sftp.file(remote, "w") as fh:
            fh.write(script)
        sftp.chmod(remote, 0o755)
        sftp.close()

        cmd = f"bash {remote}" + (" " + " ".join(f"'{a}'" for a in args) if args else "")
        _in, out, err = cli.exec_command(cmd, timeout=180)
        so = out.read().decode("utf-8", "replace")
        se = err.read().decode("utf-8", "replace")
        rc = out.channel.recv_exit_status()
        sys.stdout.write(so)
        if se.strip():
            sys.stdout.write(f"--- stderr ---\n{se}")
        sys.stdout.write(f"--- exit={rc} ---\n")
        return rc
    finally:
        cli.close()


def run_sql(sql: str) -> int:
    """把 SQL 写到远端文件再喂给 mysql —— SQL 里的引号不会被任何 shell 吃掉。"""
    cli = connect()
    try:
        sftp = cli.open_sftp()
        try:
            sftp.mkdir(REMOTE_DIR)
        except OSError:
            pass
        remote = f"{REMOTE_DIR}/q.sql"
        with sftp.file(remote, "w") as fh:
            fh.write(sql)
        sftp.close()
        cmd = f"{MYSQL} --table < {remote} 2>&1 | grep -v 'Using a password'"
        _in, out, err = cli.exec_command(cmd, timeout=120)
        sys.stdout.write(out.read().decode("utf-8", "replace"))
        se = err.read().decode("utf-8", "replace")
        if se.strip():
            sys.stdout.write(f"--- stderr ---\n{se}")
        return out.channel.recv_exit_status()
    finally:
        cli.close()


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "--sql":
        sys.exit(run_sql(sys.argv[2]))
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    p = Path(sys.argv[1])
    sys.exit(run_script(p.read_text(encoding="utf-8"), sys.argv[2:]))
