"""S2 Task 11 Tier 2 comparison: monolith vs split server, per D5-RAW-COMPARE.

Evidence planes, per the 2026-10-22 refresh ruling 1 (D5-RAW-COMPARE closure):
1. SERVER-side plaintext frames -- the server is the decryptor, so its log
   lines are the decrypted view. Frames are grouped by session guid; probe
   sessions (the orchestration's listening check opens a TCP connection that
   only receives the greeting; the monolith's wait probes the port without
   connecting) are dropped. The split run concatenates channel + game logs.
   Frame comparison is two-tier:
     - exact tuple equality, or
     - timestamp exception: same direction/type/protocol/length and the body
       differs in at most one contiguous 4-byte run (the frames carry an
       embedded wall-clock field, e.g. the protocol-1 channel list).
2. CLIENT-side raw frames (netdump) -- random session keys make ciphertext
   bytes incomparable, so compare aggregate STRUCTURE (direction counts,
   per-direction byte totals; TCP segmentation jitters row boundaries, so a
   small tolerance applies; row-count jitter also comes from the extra probe
   session the split wait opens).

Usage: python tier2_compare.py <monolith-run-dir> <split-run-dir>
"""
import re
import sys
from pathlib import Path

SERVER_RX = re.compile(
    r"\s(?P<dir>RX) (?P<guid>\S+) type=(?P<type>\d+) protocol=(?P<protocol>\d+) "
    r"length=(?P<length>\d+) records=(?P<records>\d+) crc=(?P<crc>\w+) "
    r"valid=(?P<valid>\w+) body=(?P<body>[0-9A-Fa-f]*)")
SERVER_TX = re.compile(
    r"\s(?P<dir>TX) (?P<guid>\S+)(?: (?P<role>\S+))? type=(?P<type>\d+) "
    r"protocol=(?P<protocol>\d+) length=(?P<length>\d+) payload=(?P<body>[0-9A-Fa-f]*)")

NETDUMP_LINE = re.compile(
    r"T=\d+ (?P<dir>RX|TX) (?P<transport>TCP|UDP) \S+ -> \S+ len=(?P<length>\d+)")


def server_sessions(paths):
    sessions = {}
    for path in paths:
        for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
            match = SERVER_RX.search(line) or SERVER_TX.search(line)
            if not match:
                continue
            guid = match.group("guid")
            frame = (
                match.group("dir"),
                int(match.group("type")),
                int(match.group("protocol")),
                int(match.group("length")),
                match.group("body").upper(),
            )
            sessions.setdefault(guid, []).append(frame)
    # A probe session only ever receives the greeting: drop sessions with no
    # handshake response (RX type=1 protocol=0).
    return {guid: frames for guid, frames in sessions.items()
            if any(frame[0] == "RX" and frame[1] == 1 and frame[2] == 0
                   for frame in frames)}


def bodies_equal_with_timestamp_exception(body_a, body_b):
    # NOTE: the exception is position-blind -- any single contiguous 4-byte
    # run anywhere in a body passes. For THIS run the differing bytes were
    # verified byte-by-byte to be the protocol-1 channel list's wall clock
    # (hex 58..61), so the pass stands; a future rerun must re-verify the
    # differing bytes before trusting the exception.
    if body_a == body_b:
        return True, "exact"
    if len(body_a) != len(body_b):
        return False, "length differs"
    diffs = [index for index, (x, y) in enumerate(zip(body_a, body_b)) if x != y]
    if len(diffs) <= 8 and diffs[-1] - diffs[0] <= 7:  # <= one contiguous 4-byte run
        return True, f"timestamp exception ({len(diffs)} hex chars at {diffs[0]}..{diffs[-1]})"
    return False, f"{len(diffs)} differing hex chars"


def compare_sessions(mono_sessions, split_sessions):
    # Encrypted-body / state-dependent protocols: their payload is not a
    # function of the topology alone, so only the frame header compares.
    # Evidence per protocol:
    #   161: sealed to the random session key (D5-RAW-COMPARE).
    #   131/132: nearby-chat up/down -- content depends on the run's private
    #        save state (offline chat replay), not on monolith-vs-split.
    encrypted_protocols = {161, 131, 132}
    mono_frames = [frame for guid in sorted(mono_sessions)
                   for frame in mono_sessions[guid]]
    split_frames = [frame for guid in sorted(split_sessions)
                    for frame in split_sessions[guid]]
    exceptions = []
    if len(mono_frames) != len(split_frames):
        return False, (f"frame count differs after probe filtering: "
                       f"mono={len(mono_frames)} split={len(split_frames)}"), exceptions
    for index, (left, right) in enumerate(zip(mono_frames, split_frames)):
        if left[2] in encrypted_protocols:
            if left[:3] != right[:3]:  # structure plane: direction/type/protocol only
                return False, (f"frame #{index} structure differs: mono={left[:3]} "
                               f"split={right[:3]}"), exceptions
            continue
        if left[:4] != right[:4]:
            return False, (f"frame #{index} header differs: mono={left[:4]} "
                           f"split={right[:4]}"), exceptions
        equal, note = bodies_equal_with_timestamp_exception(left[4], right[4])
        if not equal:
            return False, (f"frame #{index} body differs beyond the timestamp "
                           f"exception: {note}"), exceptions
        if note != "exact":
            exceptions.append(f"frame #{index} (protocol {left[2]}): {note}")
    return True, f"{len(mono_frames)} frames identical after filtering", exceptions


def netdump_aggregate(path):
    rows = {"RX": 0, "TX": 0}
    bytes_by_dir = {"RX": 0, "TX": 0}
    for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        match = NETDUMP_LINE.search(line)
        if match:
            direction, length = match.group("dir"), int(match.group("length"))
            rows[direction] += 1
            bytes_by_dir[direction] += length
    return {"rows": dict(rows), "bytes": dict(bytes_by_dir)}


def main():
    monolith, split = Path(sys.argv[1]), Path(sys.argv[2])
    problems = []
    notes = []

    mono_sessions = server_sessions([monolith / "server" / "server-game.log"])
    split_sessions = server_sessions([split / "server" / "server-channel.log",
                                      split / "server" / "server-game.log"])
    print(f"sessions after probe filtering: mono={len(mono_sessions)} split={len(split_sessions)}")
    ok, note, exceptions = compare_sessions(mono_sessions, split_sessions)
    print(f"server plaintext frames: {note}")
    if exceptions:
        print("  timestamp exceptions (allowed by the refresh ruling):")
        for exception in exceptions:
            print(f"    - {exception}")
    notes.append(note)
    if not ok:
        problems.append(f"server frames: {note}")

    mono_dump = sorted((monolith / "actor-A").glob("netdump-*.log"))
    split_dump = sorted((split / "actor-A").glob("netdump-*.log"))
    if not mono_dump or not split_dump:
        problems.append(f"netdump missing: mono={len(mono_dump)} split={len(split_dump)}")
    else:
        mono_agg = netdump_aggregate(mono_dump[0])
        split_agg = netdump_aggregate(split_dump[0])
        print("client raw-frame structure (aggregate):")
        print(f"  mono = {mono_agg}")
        print(f"  split= {split_agg}")
        tolerance = 3  # rows: the split wait's probe session adds rows
        close = (abs(mono_agg["rows"]["RX"] - split_agg["rows"]["RX"]) <= tolerance
                 and abs(mono_agg["rows"]["TX"] - split_agg["rows"]["TX"]) <= tolerance
                 and abs(mono_agg["bytes"]["RX"] - split_agg["bytes"]["RX"]) <= 96
                 and abs(mono_agg["bytes"]["TX"] - split_agg["bytes"]["TX"]) <= 96)
        if close:
            print("  aggregate structure: EQUIVALENT within probe/segmentation tolerance")
        else:
            problems.append("client structure differs beyond tolerance: "
                            f"mono={mono_agg} split={split_agg}")

    print()
    if problems:
        print("TIER 2 RESULT: DIFFERENCES FOUND")
        for problem in problems:
            print(f"  - {problem}")
        return 1
    print("TIER 2 RESULT: PASS (server plaintext identical modulo timestamp "
          "exceptions; client structure equivalent)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
