"""collector：归集 + 时钟偏移。用**已落盘的 netdump 样本**当输入，不启动游戏。"""
import datetime as dt
from pathlib import Path

import pytest

from acceptance.collector import (NETDUMP_GLOB, clock_offset, collect_actor,
                                  collect_server, find_netdumps, parse_netdump_t0,
                                  record_offsets)
from acceptance.model import RunPaths

SAMPLE = (
    "[2026-10-07 06:00:00.123] connect 127.0.0.1:2311\n"
    "[2026-10-07 06:00:01.456] TX len=43 hex=F2F7F142\n"
    "[2026-10-07 06:00:02.789] RX len=47 hex=0001002F\n"
)

# 真机 netdump（net_dump.cpp `"%04u-..%02u..%03u T=%lu ..."` 格式）逐字样本，
# 取自 G:\Game\DNF_ACT1\DF\netdump-53916.log 头部：**无方括号**，且时间戳是本地时间
# （net_dump.cpp:232-233 用 GetLocalTime）。
REAL_SAMPLE = (
    "# DFLegacy.Ijl15 net dump: 2026-10-05 00:47:24 pid=53916\n"
    "# columns: timestamp thread direction protocol local -> remote len hex\n"
    "2026-10-05 00:47:29.392 T=75376 TX TCP-CONNECT - -> 127.0.0.1:2311 len=10 hex=636F6E6E6563743D6F6B\n"
    "2026-10-05 00:47:29.392 T=75376 TX TCP 127.0.0.1:6325 -> 127.0.0.1:2311 len=11 hex=000B2B0000000000000001\n"
)


def test_find_netdumps_maps_actor_to_its_single_file(tmp_path):
    d1, d2 = tmp_path / "c1", tmp_path / "c2"
    d1.mkdir(); d2.mkdir()
    (d1 / "netdump-1234.log").write_text(SAMPLE, encoding="utf-8")
    (d2 / "netdump-5678.log").write_text(SAMPLE, encoding="utf-8")
    got = find_netdumps({"A": d1, "B": d2})
    assert got["A"].name == "netdump-1234.log" and got["B"].name == "netdump-5678.log"


def test_find_netdumps_reports_ambiguity_instead_of_guessing(tmp_path):
    d = tmp_path / "c1"
    d.mkdir()
    (d / "netdump-1.log").write_text(SAMPLE, encoding="utf-8")
    (d / "netdump-2.log").write_text(SAMPLE, encoding="utf-8")
    with pytest.raises(ValueError) as ei:
        find_netdumps({"A": d})
    assert "A" in str(ei.value) and "2" in str(ei.value)


def test_parse_netdump_t0_reads_first_timestamp(tmp_path):
    p = tmp_path / "netdump-1.log"
    p.write_text(SAMPLE, encoding="utf-8")
    assert parse_netdump_t0(p) == dt.datetime(2026, 10, 7, 6, 0, 0, 123000)


def test_parse_netdump_t0_returns_none_when_no_timestamp(tmp_path):
    p = tmp_path / "netdump-1.log"
    p.write_text("TX len=1 hex=00\n", encoding="utf-8")
    assert parse_netdump_t0(p) is None


def test_collect_actor_copies_into_actor_dir_and_reports_t0(tmp_path):
    d = tmp_path / "c1"; d.mkdir()
    src = d / "netdump-1.log"
    src.write_bytes(SAMPLE.encode("utf-8"))
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.actor_dir("A").mkdir(parents=True)
    rec = collect_actor(rp, "A", src)
    assert (rp.actor_dir("A") / "netdump-1.log").exists()
    assert rec["actor"] == "A" and rec["bytes"] == len(SAMPLE.encode("utf-8"))
    # netdump 时间戳是本地时间（GetLocalTime）⇒ t0_utc 须换算成 UTC，不许直接贴 Z（误标）。
    assert rec["t0_utc"] == dt.datetime(2026, 10, 7, 6, 0, 0, 123000) \
        .astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def test_clock_offset_computes_seconds_and_tolerates_missing():
    s = dt.datetime(2026, 10, 7, 6, 0, 5)
    a = dt.datetime(2026, 10, 7, 6, 0, 0)
    assert clock_offset(s, a) == pytest.approx(5.0)
    assert clock_offset(s, None) is None


def test_record_offsets_writes_json(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    out = record_offsets(rp, {"A": 5.0, "B": None})
    assert out.name == "offsets.json"
    assert '"A": 5.0' in out.read_text(encoding="utf-8")


def test_collect_server_copies_logs(tmp_path):
    logs = tmp_path / "logs"; logs.mkdir()
    (logs / "server-1.log").write_text("x", encoding="utf-8")
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True)
    rec = collect_server(rp, logs)
    assert rec["files"] == 1 and (rp.server_dir / "server-1.log").exists()


# ===== 真机 netdump 时间格式/时区（bugfix） =====

def test_parse_netdump_t0_reads_real_unbracketed_format(tmp_path):
    """真机行 `2026-10-05 00:47:29.392 T=...`（无方括号）必须能取 t0；# 注释行不算。"""
    p = tmp_path / "netdump-53916.log"
    p.write_text(REAL_SAMPLE, encoding="utf-8")
    assert parse_netdump_t0(p) == dt.datetime(2026, 10, 5, 0, 47, 29, 392000)


def test_collect_actor_t0_utc_converts_local_time_to_utc(tmp_path):
    """t0_utc 是本机时区→UTC 的换算结果，不是本地时间贴 Z（见 net_dump.cpp GetLocalTime）。"""
    d = tmp_path / "c1"; d.mkdir()
    src = d / "netdump-53916.log"
    src.write_text(REAL_SAMPLE, encoding="utf-8")
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rec = collect_actor(rp, "A", src)
    local = dt.datetime(2026, 10, 5, 0, 47, 29, 392000)
    assert rec["t0_utc"] == local.astimezone(dt.timezone.utc) \
        .strftime("%Y-%m-%dT%H:%M:%S.%fZ")
    if local.astimezone().utcoffset() != dt.timedelta(0):
        # 本机非 UTC 时必须能区分出旧的"直接贴 Z"误标行为
        assert rec["t0_utc"] != "2026-10-05T00:47:29.392000Z"