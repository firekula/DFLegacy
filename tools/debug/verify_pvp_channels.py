"""Read-only verification of the ACT1 four-channel PVF contract."""
import argparse
import re
from pathlib import Path
from tools.debug.town_navigation import _Pvf, _decode


def entry(pvf, name):
    offset, size, key = pvf.entries[name]
    start = pvf.body + offset
    return _decode(pvf.raw[start:start + ((size + 3) & ~3)], key)[:size]


def verify(path):
    pvf = _Pvf(path)
    text = entry(pvf, "etc/pvpchannellist.etc").decode("cp936")
    body = re.search(r"\[PVP CHANNEL INFO\]([\s\S]*?)\[/PVP CHANNEL INFO\]", text)[1]
    groups = re.findall(r"(?m)^([1-9]) 4\s*\n((?:`[^`]+` \d+ \d+ \d+\s*\n){4})", body)
    assert len(groups) == 9, "each of nine groups must contain exactly four PvP profiles"
    for group, rows in groups:
        ranges = [tuple(map(int, row)) for row in re.findall(r"`[^`]+` (\d+) (\d+) (\d+)", rows)]
        assert ranges == [(0,9,0), (10,19,1), (20,30,2), (0,30,4)], (group, ranges)
    channel = entry(pvf, "etc/channel_info.etc").decode("cp936")
    for group in re.findall(r"\[server\]([\s\S]*?)\[/server\]", channel):
        number = int(re.search(r"\d+", group)[0])
        if number not in range(1,10):
            continue
        rows = re.findall(r"(?m)^\s*(5\d)\s+(`[^`]+`|<[^>]+>)\s+(\d+)\s+", group)
        assert [(int(n), int(kind)) for n, _, kind in rows] == [(51,4),(52,4),(53,4),(55,5)], (number, rows)
    return pvf


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("pvf", type=Path)
    args = parser.parse_args()
    result = verify(args.pvf)
    print(f"PASS nine groups; four aligned profiles; maximum grade30; SHA256 {result.sha256}")
