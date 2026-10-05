#!/usr/bin/env python3
r"""replay-30011-live.py —— 把抓到的游戏面 C 流首帧发给一个**活靶子**并解析回复。

## 与 selftest-30011.py 的分工

`selftest-30011.py` 是**判据**：它从 B0 抓包里抽期望值，再和重放结果比对，输出 PASS/FAIL。
本脚本是**观测**：只做"发一帧、收回来、把帧切开打印"，不做判定。
分开的理由：现在（2026-10-05 晚）VM 靶子刚被我重新拉起来，我要先看清它**实际**回什么，
而不是先假定它该回什么 —— 先观测再定判据，避免把期望值当成事实。

## 用法

    replay-30011-live.py <stream.bin> --host H --port P [--first-frame-bytes N] [--timeout S]
"""
from __future__ import annotations

import argparse
import socket
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="把抓到的游戏面首帧发给活靶子并解析回复")
    ap.add_argument("stream", type=Path, help="C 流文件（如 streams-vm/13681-10013.bin）")
    ap.add_argument("--host", required=True)
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--first-frame-bytes", type=int, default=None,
                    help="只发前 N 字节（默认发整条 C 流）")
    ap.add_argument("--timeout", type=float, default=8.0)
    ap.add_argument("--out-raw", type=Path, default=None)
    a = ap.parse_args(argv)

    blob = a.stream.read_bytes()
    frames, used = dnfdrive.parse_frames(blob, GAME)
    if not frames:
        print(f"FAIL: {a.stream} 切不出帧")
        return 2
    payload = blob[:a.first_frame_bytes] if a.first_frame_bytes else blob
    print(f"源流    : {a.stream.name}  {len(blob)} B  {len(frames)} 帧  "
          f"（铺满={used == len(blob)}）")
    print(f"首帧    : cat=0x{frames[0].cat:02x} id={frames[0].fid} size={frames[0].size}  "
          f"头={frames[0].raw[:GAME.hdr].hex(' ')}")
    print(f"本次发送: {len(payload)} B -> {a.host}:{a.port}")
    print()

    try:
        s = socket.create_connection((a.host, a.port), timeout=a.timeout)
    except Exception as e:
        print(f"连不上: {type(e).__name__}: {e}")
        return 1
    got = b""
    try:
        s.sendall(payload)
        s.settimeout(a.timeout)
        try:
            while True:
                chunk = s.recv(65536)
                if not chunk:
                    break
                got += chunk
        except (socket.timeout, TimeoutError):
            pass
    finally:
        s.close()

    print(f"收到    : {len(got)} B")
    if a.out_raw:
        a.out_raw.write_bytes(got)
        print(f"-> {a.out_raw}")
    if not got:
        print("  （零回复 —— 服务端一个字节都没发）")
        return 0
    rf, rused = dnfdrive.parse_frames(got, GAME)
    print(f"切帧    : {len(rf)} 帧，消费 {rused}/{len(got)} B（铺满={rused == len(got)}）")
    for f in rf:
        kind = "CMD" if f.cat == 1 else ("NOTI" if f.cat == 0 else f"?0x{f.cat:02x}")
        print(f"  {kind:<5} id={f.fid:<4} size={f.size:<6} "
              f"hdr[7:10]={f.raw[7:10].hex(' ')}  payload={f.raw[GAME.hdr:GAME.hdr + 24].hex(' ')}")
    print(f"\n原始前 64 字节: {got[:64].hex(' ')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
