#!/usr/bin/env python3
r"""analyze-udp.py —— 把抓包里的 **UDP** 载荷单独抽出来分析。

## 为什么要这一步

`dbmw` 同时监听 TCP 与 UDP（同号端口）。前几轮我只分析了 TCP 流，结果"全是心跳、
没有业务报文"。最近一次抓包（**过滤器不限协议**）实测协议分布 `TCP:881 / UDP:71`
—— **有 71 个 UDP 包**，它们就在 dbmw / 游戏面的端口上。

这 71 个包是判定"业务是否走 UDP"的关键：若其中有**非 4202** 的帧 ⇒ 业务走 UDP（假设 A）；
若只有心跳 ⇒ 那批查询是 dbmw 内部定时刷盘（假设 B）。

## 做法

按 (sport,dport) 聚合 UDP 载荷，拼成"单向流"再用 dbmw 的 4 字节头切帧，
打印 id 直方图与样本。同时也把原始十六进制打出来（UDP 包通常很小）。

用法: analyze-udp.py <pcap>
"""
from __future__ import annotations

import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(r".superpowers/sdd/2026-10-05-official-login-channel/work")))
from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location(
    "ps", r".superpowers/sdd/2026-10-05-official-login-channel/work/pcap-streams.py")
m = module_from_spec(_spec)
sys.modules["ps"] = m
_spec.loader.exec_module(m)

DBMW_HEADER = 4


def parse_udp(linktype, pkt):
    o = m.l3_offset(linktype, pkt)
    if len(pkt) < o + 20:
        return None
    vihl = pkt[o]
    if vihl >> 4 != 4:
        return None
    ihl = (vihl & 0xF) * 4
    if pkt[o + 9] != 17:          # UDP
        return None
    total_len = struct.unpack_from(">H", pkt, o + 2)[0]
    src = ".".join(str(b) for b in pkt[o + 12:o + 16])
    dst = ".".join(str(b) for b in pkt[o + 16:o + 20])
    t = o + ihl
    if len(pkt) < t + 8:
        return None
    sport, dport, ulen = struct.unpack_from(">HHH", pkt, t)
    payload = pkt[t + 8:o + total_len]
    return src, sport, dst, dport, payload


def main() -> int:
    pcap = sys.argv[1]
    streams = defaultdict(list)
    n_udp = 0
    for ts, lt, pkt in m.read_packets(pcap):
        r = parse_udp(lt, pkt)
        if not r:
            continue
        src, sport, dst, dport, payload = r
        n_udp += 1
        streams[(src, sport, dst, dport)].append((ts, payload))

    print(f"UDP 包共 {n_udp} 个，{len(streams)} 个方向\n")
    for key, chunks in sorted(streams.items(), key=lambda kv: -sum(len(p) for _t, p in kv[1])):
        src, sport, dst, dport = key
        blob = b"".join(p for _t, p in chunks)
        print("=" * 78)
        print(f"{src}:{sport} -> {dst}:{dport}   {len(chunks)} 包  {len(blob)} B")
        print("=" * 78)
        # 每包单独看（UDP 有边界，不必拼接）
        sizes = Counter(len(p) for _t, p in chunks)
        print(f"  包长分布: {dict(sorted(sizes.items()))}")
        # 用 dbmw 的 4 字节头试切
        fr, off = [], 0
        while off + DBMW_HEADER <= len(blob):
            fid, size = struct.unpack_from("<HH", blob, off)
            if size < DBMW_HEADER or off + size > len(blob):
                break
            fr.append((fid, blob[off:off + size]))
            off += size
        if fr:
            hist = Counter(f for f, _ in fr)
            print(f"  按 dbmw 4 字节头切: {len(fr)} 帧, 消费 {off}/{len(blob)}  "
                  f"铺满={off == len(blob)}")
            print(f"  packet id 直方图: {dict(hist.most_common(12))}")
            non4202 = [(f, b) for f, b in fr if f != 4202]
            if non4202:
                print(f"  *** 非 4202 的帧 {len(non4202)} 个 —— 假设 A 成立 ***")
                for f, b in non4202[:6]:
                    print(f"      id={f:<6} size={len(b):<5} {b.hex(' ')}")
            else:
                print("  只有 4202")
        else:
            print("  切不出 dbmw 帧（可能是别的协议）")
        # 前 3 包原始 hex
        print("  前 3 包原始:")
        for _t, p in chunks[:3]:
            print(f"    {p.hex(' ')}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
