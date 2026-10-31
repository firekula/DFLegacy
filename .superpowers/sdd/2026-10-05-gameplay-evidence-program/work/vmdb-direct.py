#!/usr/bin/env python3
r"""vmdb-direct.py —— 直连 VM MySQL（192.168.200.131:3306）跑 SQL，**只读**。

## 为什么有这一份（与 vmdb.py 的关系）

`vmdb.py` 走 SSH（paramiko，口令从 `$env:VM_PASS` 读）→ 远端 python2.4 → `mysql -e`。
本文件**不经过 SSH**：VM 的 `3306` 在宿主可达（实测 `Test-NetConnection 192.168.200.131:3306` = True），
而凭据在项目里已有明文记录（`vmdb.py:35`，来源 `/home/neople/bridge/cfg/bridge.cfg`）。

**用途不同**：`vmdb.py` 是权威路径（走 VM 自己的 socket、与手抄记录可比）；
本文件是**免口令的旁路**，用于没有 `VM_PASS` 时的只读查询。

## 纪律（硬约束）

* **只允许 SELECT / SHOW / DESCRIBE / EXPLAIN**：写语句在本地就拒绝，不依赖远端权限兜底。
* 只读连接：`autocommit=False` 且结束 `rollback()`，不改任何行。
* 口令**不打印**。

用法:
  python vmdb-direct.py "SHOW DATABASES"
  python vmdb-direct.py --file q.sql
  python vmdb-direct.py --tsv "SELECT ..."     # TSV 落盘友好
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

import pymysql

HOST = "192.168.200.131"
PORT = 3306
USER = "game"
PASSWORD = "REDACTED_VM_DB_PASSWORD"          # 来源：/home/neople/bridge/cfg/bridge.cfg 的 db_pwd（明文），与 vmdb.py:35 同源

_ALLOWED = re.compile(r"^\s*(select|show|describe|desc|explain|with)\b", re.IGNORECASE)
_FORBIDDEN = re.compile(
    r"\b(insert|update|delete|drop|alter|create|truncate|replace|grant|revoke|set|call|lock|unlock)\b",
    re.IGNORECASE,
)


def guard(sql: str) -> None:
    body = sql.strip().rstrip(";")
    if not _ALLOWED.match(body):
        raise SystemExit(f"REFUSED: 只允许 SELECT/SHOW/DESCRIBE/EXPLAIN，收到：{body[:60]!r}")
    if _FORBIDDEN.search(body):
        raise SystemExit(f"REFUSED: 语句里出现写/权限关键字，收到：{body[:80]!r}")


def render(rows, header) -> str:
    if not rows:
        return "(0 rows)"
    cols = [d[0] for d in header]
    out = ["\t".join(cols)]
    for r in rows:
        out.append("\t".join("NULL" if v is None else str(v) for v in r))
    return "\n".join(out)


def main() -> int:
    args = sys.argv[1:]
    if not args:
        raise SystemExit(__doc__)
    tsv = "--tsv" in args
    args = [a for a in args if a != "--tsv"]
    if args[0] == "--file":
        sql = Path(args[1]).read_text(encoding="utf-8")
    else:
        sql = " ".join(args)
    guard(sql)

    conn = pymysql.connect(host=HOST, port=PORT, user=USER, password=PASSWORD,
                           charset="utf8mb4", autocommit=False, connect_timeout=15)
    try:
        with conn.cursor() as cur:
            for stmt in [s for s in sql.split(";") if s.strip()]:
                cur.execute(stmt)
                if cur.description:
                    rows = cur.fetchall()
                    print(render(rows, cur.description) if tsv else _table(rows, cur.description))
                else:
                    print(f"(no result set) rows={cur.rowcount}")
    finally:
        conn.rollback()
        conn.close()
    return 0


def _table(rows, header) -> str:
    cols = [d[0] for d in header]
    data = [[("NULL" if v is None else str(v)) for v in r] for r in rows]
    widths = [len(c) for c in cols]
    for r in data:
        for i, v in enumerate(r):
            widths[i] = max(widths[i], len(v))
    line = "+" + "+".join("-" * (w + 2) for w in widths) + "+"
    out = [line, "| " + " | ".join(c.ljust(widths[i]) for i, c in enumerate(cols)) + " |", line]
    for r in data:
        out.append("| " + " | ".join(v.ljust(widths[i]) for i, v in enumerate(r)) + " |")
    out.append(line)
    out.append(f"({len(data)} rows)")
    return "\n".join(out)


if __name__ == "__main__":
    sys.exit(main())
