from pathlib import Path

import pytest

from protocol import importer


def _write(p: Path, text: str) -> Path:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(text.encode("utf-8"))
    return p


def _fixture_raw(tmp_path: Path) -> Path:
    raw = tmp_path / "raw"
    _write(raw / "coverage-by-opcode.tsv",
           "kind\tid\tname\tstatus\tdispatcher\ttier\tread_count\twrite_count\trodata_va\text_mark\text_evidence\n"
           "NOTI\t1\tX\tno-dispatcher\t\tT0_no-evidence\t0\t0\t0x01\tname_only\t\n"
           "CMD\t2\tY\tmapped\tDoY\tT3_fields-read-only\t3\t0\t0x02\tname_only\t\n"
           "CMD\t1\tZ\tmapped\tDoZ\tT4_fields-read+write\t5\t6\t0x03\tfield_read\t旧证据-低\n")
    _write(raw / "tiers-external.tsv",
           "# kind\tid\tmark\tevidence\n"
           "CMD\t1\tbyte_verified\t新证据-高\n"
           "CMD\t1\tfield_read\t旧证据-低\n")
    return raw


def test_merge_sorts_by_kind_then_id(tmp_path):
    rows = importer.merge_rows(
        importer.load_coverage(_fixture_raw(tmp_path)),
        importer.load_external(_fixture_raw(tmp_path)))
    assert [(r["kind"], r["id"]) for r in rows] == [("CMD", "1"), ("CMD", "2"), ("NOTI", "1")]


def test_ext_mark_takes_highest_priority_and_keeps_all_evidence(tmp_path):
    raw = _fixture_raw(tmp_path)
    rows = importer.merge_rows(importer.load_coverage(raw), importer.load_external(raw))
    cmd1 = next(r for r in rows if r["kind"] == "CMD" and r["id"] == "1")
    assert cmd1["ext_mark"] == "byte_verified"
    assert cmd1["_ext_evidence"] == "新证据-高; 旧证据-低"


def test_load_external_strips_crlf_line_ending(tmp_path):
    """真产物 `tiers-external.tsv` 实测是 CRLF；证据原文不得带 `\\r`，否则账① 落盘即失败。"""
    raw = tmp_path / "raw"
    _write(raw / "tiers-external.tsv",
           "# kind\tid\tmark\tevidence\r\n"
           "CMD\t1\tbyte_verified\t证据甲\r\n")
    assert importer.load_external(raw)[("CMD", 1)] == [("byte_verified", "证据甲")]


def test_unmarked_opcode_defaults_to_name_only_with_empty_evidence(tmp_path):
    raw = _fixture_raw(tmp_path)
    rows = importer.merge_rows(importer.load_coverage(raw), importer.load_external(raw))
    noti1 = next(r for r in rows if r["kind"] == "NOTI")
    assert noti1["ext_mark"] == "name_only"
    assert noti1["_ext_evidence"] == ""


def test_write_tsv_is_lf_utf8_without_bom(tmp_path):
    p = tmp_path / "t.tsv"
    importer.write_tsv(p, ("a", "b"), [{"a": "1", "b": "2"}])
    raw = p.read_bytes()
    assert raw == "a\tb\n1\t2\n".encode("utf-8")


def test_write_tsv_rejects_tab_in_field(tmp_path):
    with pytest.raises(ValueError):
        importer.write_tsv(tmp_path / "t.tsv", ("a",), [{"a": "x\ty"}])


def test_import_map_on_real_artifacts_hits_baselines():
    raw = Path(".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out")
    if not (raw / "coverage-by-opcode.tsv").exists():
        pytest.skip("原始产物不在本机")
    import tempfile
    with tempfile.TemporaryDirectory() as td:
        stats = importer.import_map(raw, Path(td))
        assert stats["rows"] == 1171
        text = (Path(td) / "map" / "opcode-map.tsv").read_text(encoding="utf-8")
        assert len(text.splitlines()) == 1172  # 含表头
