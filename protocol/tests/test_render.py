import json
from pathlib import Path

import pytest

from protocol import render

RUNS_HEADER = ("run_id\tgit_sha\tcreated_utc\tstatus\tpass\tneeds_human\tmissing_evidence\t"
               "fail\tevidence_class\tverdict_digest_sha256\n")


def _base(tmp_path: Path, map_text: str, runs_text: str = RUNS_HEADER) -> Path:
    base = tmp_path
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(map_text.encode("utf-8"))
    (base / "map" / "s2s-map.tsv").write_bytes(
        "kind\tid\tname\trodata_va\tdispatcher\tstatus\ttier\tread_count\twrite_count\text_mark\n"
        "S2S\t4\tNOTICE_CHANNEL_SERVER\t-\tDFLegacy.InterProcess.InterProcessLink\t"
        "mapped\tT4_fields-read+write\t1\t1\t-\n".encode("utf-8"))
    (base / "ledger" / "runs.tsv").write_bytes(runs_text.encode("utf-8"))
    (base / "ledger" / "scenarios.tsv").write_bytes(b"scenario_id\n")
    (base / "ledger" / "opcodes.tsv").write_bytes(b"kind\tid\n")
    return base


def test_manifest_has_no_timestamp_and_keys_sorted(tmp_path):
    """清单 6 项齐全、键有序、无时间戳、单换行结尾（终审 M5 后清单不得缺受跟踪产物）。"""
    base = _base(tmp_path,
                 "kind\tid\ttier\text_mark\nCMD\t11\tT4_fields-read+write\tname_only\n")
    render.render_all(base)
    text = (base / "manifest.json").read_text(encoding="utf-8")
    manifest = json.loads(text)
    assert "2026" not in text and "time" not in text.lower()
    assert len(manifest["files"]) == 6
    assert list(manifest["files"]) == sorted(manifest["files"])  # sort_keys
    assert text.endswith("}\n")
    assert text.index('"baselines"') < text.index('"files"')     # sort_keys


def test_render_is_idempotent(tmp_path):
    base = _base(tmp_path,
                 "kind\tid\ttier\text_mark\nCMD\t11\tT4_fields-read+write\tname_only\n",
                 RUNS_HEADER +
                 "RID\tabc1234\t2026-10-18T09:19:51Z\tgreen-with-human\t3\t1\t0\t0\t[真机]\tdeadbeef\n")
    first = render.render_all(base)
    index_after_first = (base / "map" / "index.md").read_bytes()
    manifest_after_first = (base / "manifest.json").read_bytes()
    second = render.render_all(base)
    assert first["manifest_sha256"] == second["manifest_sha256"]
    assert (base / "map" / "index.md").read_bytes() == index_after_first
    assert (base / "manifest.json").read_bytes() == manifest_after_first


def test_index_contains_tier_table_and_boundaries(tmp_path):
    base = _base(tmp_path,
                 ("kind\tid\ttier\text_mark\n" + "".join(
                     f"CMD\t{i}\t{t}\tname_only\n" for i, t in enumerate(
                         ["T0_no-evidence", "T1_dispatcher-no-handlerdata",
                          "T2_handlerdata-no-fields", "T3b_fields-write-only",
                          "T3_fields-read-only", "T4_fields-read+write"]))))
    render.render_all(base)
    md = (base / "map" / "index.md").read_text(encoding="utf-8")
    for tier in ("T0_no-evidence", "T4_fields-read+write"):
        assert tier in md
    assert "T3/T4" in md and "不等于" in md


def test_index_contains_kind_and_status_distribution(tmp_path):
    base = _base(tmp_path,
                 "kind\tid\tstatus\ttier\text_mark\n"
                 "CMD\t1\tmapped\tT3_fields-read-only\tname_only\n"
                 "NOTI\t2\tno-dispatcher\tT0_no-evidence\tname_only\n")
    render.render_all(base)
    md = (base / "map" / "index.md").read_text(encoding="utf-8")
    for status in ("mapped", "no-dispatcher", "unused"):
        assert status in md
    assert "CMD 1" in md and "NOTI 1" in md


def test_generated_files_are_lf_without_bom_and_single_trailing_newline(tmp_path):
    base = _base(tmp_path,
                 "kind\tid\tstatus\ttier\text_mark\n"
                 "CMD\t1\tmapped\tT4_fields-read+write\tbyte_verified\n")
    render.render_all(base)
    for rel in ("map/index.md", "manifest.json"):
        raw = (base / rel).read_bytes()
        assert not raw.startswith(b"\xef\xbb\xbf"), rel
        assert b"\r" not in raw, rel
        assert raw.endswith(b"\n") and not raw.endswith(b"\n\n"), rel
        raw.decode("utf-8")


def test_render_failure_does_not_touch_index(tmp_path, monkeypatch):
    """N3：render 先算后写 —— manifest 构建阶段失败时，`map/index.md` 不得被改写。"""
    base = _base(tmp_path,
                 "kind\tid\ttier\text_mark\nCMD\t11\tT4_fields-read+write\tname_only\n",
                 RUNS_HEADER + "RID\tabc1234\t2026-10-18T09:19:51Z\tgreen\t3\t0\t0\t0\t"
                               "[真机]\t" + "a" * 64 + "\n")
    render.render_all(base)
    before = (base / "map" / "index.md").read_bytes()
    # 改 runs 让 index 内容"本应变化"，确保"未改写"不是巧合
    (base / "ledger" / "runs.tsv").write_bytes(
        (RUNS_HEADER + "RID2\tabc1234\t2026-10-19T00:00:00Z\tgreen\t9\t0\t0\t0\t"
                       "[真机]\t" + "b" * 64 + "\n").encode("utf-8"))

    def boom(*args, **kwargs):
        raise RuntimeError("manifest build failed")

    monkeypatch.setattr(render, "build_manifest", boom)
    with pytest.raises(RuntimeError):
        render.render_all(base)
    assert (base / "map" / "index.md").read_bytes() == before
