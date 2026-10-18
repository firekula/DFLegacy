import json
from pathlib import Path

from protocol import render

RUNS_HEADER = ("run_id\tgit_sha\tcreated_utc\tstatus\tpass\tneeds_human\tmissing_evidence\t"
               "fail\tevidence_class\tverdict_digest_sha256\n")


def _base(tmp_path: Path, map_text: str, runs_text: str = RUNS_HEADER) -> Path:
    base = tmp_path
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(map_text.encode("utf-8"))
    (base / "ledger" / "runs.tsv").write_bytes(runs_text.encode("utf-8"))
    (base / "ledger" / "scenarios.tsv").write_bytes(b"scenario_id\n")
    (base / "ledger" / "opcodes.tsv").write_bytes(b"kind\tid\n")
    return base


def test_manifest_has_no_timestamp_and_keys_sorted(tmp_path):
    (tmp_path / "map").mkdir()
    (tmp_path / "map" / "opcode-map.tsv").write_bytes(b"h\n1\n")
    manifest = render.build_manifest(tmp_path)
    text = render.dump_manifest(manifest)
    assert "2026" not in text and "time" not in text.lower()
    assert list(manifest["files"]) == ["map/opcode-map.tsv"]
    assert text.endswith("}\n")
    assert text.index('"baselines"') < text.index('"files"')  # sort_keys


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
