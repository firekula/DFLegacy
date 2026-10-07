#!/usr/bin/env python3
r"""vmexec.py —— 把本地文件推到 VM 并在 VM 上跑命令（**不经过 shell 引号地狱**）。

为什么又写一份：`vmssh.py` 把整条命令拼成一个字符串交给远端 shell，
在 PowerShell 里再套一层引号就非常容易断（本次实测踩到 `SyntaxError: unterminated string literal`）。
本文件把「命令」当**argv 数组**逐项递交，且可以先把本地文件 sftp 上去。

用法:
  python vmexec.py --put work/peek-ro.c:/tmp/peek-ro.c -- gcc -O2 -o /tmp/peek-ro /tmp/peek-ro.c
  python vmexec.py -- /tmp/peek-ro 3567 /tmp/report.txt
  python vmexec.py --get /tmp/report.txt:work/out/vm-peek-report.txt -- true
"""
from __future__ import annotations

import argparse
import shlex
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vmssh import connect  # noqa: E402


def parse_put(spec: str):
    local, _, remote = spec.partition(":")
    if not remote:
        raise SystemExit(f"--put 需要 <local>:<remote>，收到 {spec!r}")
    return Path(local), remote


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--put", action="append", default=[])
    ap.add_argument("--get", action="append", default=[])
    ap.add_argument("--timeout", type=int, default=600)
    ap.add_argument("--raw", action="store_true")
    ap.add_argument("cmd", nargs=argparse.REMAINDER)
    args = ap.parse_args()

    cmd = [c for c in args.cmd if c != "--"]
    if not cmd:
        raise SystemExit("需要一条命令（用 -- 与选项分隔）")

    cli = connect()
    try:
        if args.put or args.get:
            sftp = cli.open_sftp()
            try:
                for spec in args.put:
                    local, remote = parse_put(spec)
                    sftp.put(str(local), remote)
                    print(f"[put] {local} -> {remote}", file=sys.stderr)
                for spec in args.get:
                    remote, local = (spec.split(":", 1) + [""])[:2] if ":" in spec else (spec, "")
                    if not local:
                        raise SystemExit(f"--get 需要 <remote>:<local>，收到 {spec!r}")
                    Path(local).parent.mkdir(parents=True, exist_ok=True)
                    sftp.get(remote, local)
                    print(f"[get] {remote} -> {local}", file=sys.stderr)
            finally:
                sftp.close()

        remote_cmd = " ".join(shlex.quote(c) for c in cmd)
        _in, out, err = cli.exec_command(remote_cmd, timeout=args.timeout)
        so = out.read().decode("utf-8", "replace")
        se = err.read().decode("utf-8", "replace")
        rc = out.channel.recv_exit_status()
        sys.stdout.write(so)
        if not args.raw and se.strip():
            sys.stdout.write(f"--- stderr ---\n{se}")
        if not args.raw:
            sys.stdout.write(f"--- exit={rc} ---\n")
        return rc
    finally:
        cli.close()


if __name__ == "__main__":
    raise SystemExit(main())
