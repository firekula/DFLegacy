"""把每个源的产物归到对应 actor 目录，并记录时钟偏移。

spec §2.2 纪律 1：**不追求单一交错日志** —— 每 actor/每进程各写各的文件，
用 `offsets.json` 记录偏移，join 阶段事后按时间对齐（这也是 S4 上局域网要用的机制）。
"""
from __future__ import annotations

import datetime as dt
import json
import re
import shutil
from pathlib import Path
from typing import Optional

from .model import RunPaths

NETDUMP_GLOB = "netdump-*.log"
# 方括号可选：旧样本是 `[YYYY-MM-DD HH:MM:SS.mmm] ...`，真机 net_dump.cpp 输出
# `YYYY-MM-DD HH:MM:SS.mmm T=... ...`（无方括号）。# 注释行不带 .mmm，天然不匹配。
_TS = re.compile(r"^\[?(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})\.(\d{3})\]?")


def find_netdumps(client_dirs: dict[str, Path]) -> dict[str, Path]:
    """actor 名 -> netdump 路径。**每客户端一个文件**（`STATUS.md` L1424）；
    找到多于一个就报错 —— 不许猜哪一个属于本次 run。"""
    out: dict[str, Path] = {}
    for actor, d in client_dirs.items():
        found = sorted(Path(d).glob(NETDUMP_GLOB))
        if not found:
            raise ValueError(f"actor {actor!r} 的目录 {d} 下没有 {NETDUMP_GLOB}")
        if len(found) > 1:
            raise ValueError(
                f"actor {actor!r} 的目录 {d} 下有 {len(found)} 个 netdump，无法判定哪一个属于本次 run："
                + ", ".join(p.name for p in found))
        out[actor] = found[0]
    return out


def parse_netdump_t0(path: Path) -> Optional[dt.datetime]:
    """取第一条带时间戳的行；没有就 None（**不猜**）。

    返回 naive **本地时间**（net_dump.cpp:232-233 用 GetLocalTime 写日志）；
    转 UTC 在输出处做（collect_actor.t0_utc / cli offsets），不许在这里贴 Z。
    """
    with Path(path).open(encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = _TS.match(line)
            if m:
                d, t, ms = m.groups()
                base = dt.datetime.strptime(f"{d} {t}", "%Y-%m-%d %H:%M:%S")
                return base.replace(microsecond=int(ms) * 1000)
    return None


def collect_actor(rp: RunPaths, actor: str, netdump: Optional[Path],
                  extra: list[Path] = ()) -> dict:
    dest_dir = rp.actor_dir(actor)
    dest_dir.mkdir(parents=True, exist_ok=True)
    rec: dict = {"actor": actor, "netdump": None, "t0_utc": None, "bytes": 0}
    if netdump is not None:
        netdump = Path(netdump)
        dest = dest_dir / netdump.name
        shutil.copy2(netdump, dest)
        t0 = parse_netdump_t0(netdump)
        rec.update({
            "netdump": dest.name,
            # t0 是本地时间 ⇒ astimezone 从本机时区换算成 UTC（禁止 replace(tzinfo=UTC) 误标）
            "t0_utc": t0.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
                      if t0 else None,
            "bytes": dest.stat().st_size,
        })
    for e in extra:
        shutil.copy2(e, dest_dir / Path(e).name)
    return rec


def clock_offset(server_t0: dt.datetime, actor_t0: Optional[dt.datetime]) -> Optional[float]:
    """actor 相对服务端的偏移（秒）。无法判定就 None —— **不许默认 0**。"""
    if actor_t0 is None:
        return None
    return (server_t0 - actor_t0).total_seconds()


def collect_server(rp: RunPaths, log_dir: Path) -> dict:
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    n = 0
    for p in sorted(Path(log_dir).glob("*.log")):
        shutil.copy2(p, rp.server_dir / p.name)
        n += 1
    return {"files": n, "dir": str(log_dir)}


def record_offsets(rp: RunPaths, offsets: dict[str, Optional[float]]) -> Path:
    out = rp.run_dir / "offsets.json"
    out.write_text(json.dumps(offsets, ensure_ascii=False, indent=2, sort_keys=True),
                   encoding="utf-8")
    return out