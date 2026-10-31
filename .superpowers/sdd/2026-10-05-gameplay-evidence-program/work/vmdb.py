#!/usr/bin/env python3
r"""vmdb.py —— 在 VM 上执行 SQL，**不经过 shell 传参**。

## 为什么要单独写一个

在 VM 上跑 mysql 的正常姿势是
    mysql -ugame -p'REDACTED_VM_DB_PASSWORD' --socket=... -e "SQL"
但这条链要穿过：PowerShell -> paramiko -> bash -> mysql，每一层都会吃掉一层引号。
本会话实测踩到两类失败：
  1. PowerShell 解析 `\"` 失败（`ParserError`）；
  2. 把命令存进 shell 变量再展开时，**引号不会被重新解析**，于是 `'REDACTED_VM_DB_PASSWORD'`
     连引号一起被当作口令 ⇒ `Access denied ... (using password: YES)`。
     （这是最阴的一种：错误信息看着像口令错，其实是引号错。）

做法：写一个**只做一件事**的远端脚本，把口令与 SQL 都用 **JSON** 传进去，
远端用 python 解析 JSON 再 `os.execv` 调 mysql —— 参数以 argv 数组形式递交，
**完全绕开 shell 的词法分析**。

## 用法

    python vmdb.py "select 1"                    # 跑一条 SQL
    python vmdb.py --file q.sql                  # 跑一个 SQL 文件
环境变量 `VM_PASS` 提供 SSH 口令（见 vmssh.py）。
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vmssh import connect                      # noqa: E402

DB_USER = "game"
DB_PASS = "REDACTED_VM_DB_PASSWORD"          # 来源：/home/neople/bridge/cfg/bridge.cfg 的 db_pwd（明文）
DB_SOCK = "/opt/lampp/var/mysql/mysql.sock"

# 远端脚本：读 **NUL 分隔**的三段（user / pass / sql）-> 以 argv 数组调 mysql。
# 为什么不用 JSON：VM 上的 python 是 **2.4**（CentOS 5），没有 `json` 模块（2.6 才有）。
# NUL 分隔对任何 Python 版本都成立，且 argv 以数组递交 ⇒ 口令与 SQL 永不经过 shell 词法分析。
REMOTE = r'''
import os
f = open("/tmp/dfl/q.bin", "rb")
user, pw, sql = f.read().split("\x00")
argv = ["mysql", "-u" + user, "-p" + pw,
        "--socket=/opt/lampp/var/mysql/mysql.sock", "--table",
        "--default-character-set=utf8", "-e", sql]
os.execv("/usr/bin/mysql", argv)
'''


def run_sql(sql: str) -> int:
    cli = connect()
    try:
        sftp = cli.open_sftp()
        try:
            sftp.mkdir("/tmp/dfl")
        except OSError:
            pass
        with sftp.file("/tmp/dfl/q.bin", "wb") as fh:
            fh.write(DB_USER.encode() + b"\x00" + DB_PASS.encode() + b"\x00" + sql.encode())
        with sftp.file("/tmp/dfl/runq.py", "w") as fh:
            fh.write(REMOTE)
        sftp.close()

        _in, out, err = cli.exec_command("python /tmp/dfl/runq.py", timeout=120)
        so = out.read().decode("utf-8", "replace")
        se = err.read().decode("utf-8", "replace")
        rc = out.channel.recv_exit_status()
        sys.stdout.write(so)
        se = "\n".join(l for l in se.splitlines() if "Using a password" not in l)
        if se.strip():
            sys.stdout.write(f"--- stderr ---\n{se}\n")
        sys.stdout.write(f"--- exit={rc} ---\n")
        return rc
    finally:
        cli.close()


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "--file":
        sys.exit(run_sql(Path(sys.argv[2]).read_text(encoding="utf-8")))
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    sys.exit(run_sql(sys.argv[1]))
