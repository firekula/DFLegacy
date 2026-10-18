"""把 .superpowers 里的原始协议产物规范化成 protocol/map/opcode-map.tsv。

只做 join / 合并 / 排序 / 落盘；不新增逆向，不改语义。
"""
from __future__ import annotations

import csv
from pathlib import Path

MAP_COLUMNS: tuple[str, ...] = (
    "kind", "id", "name", "rodata_va", "dispatcher", "status",
    "tier", "read_count", "write_count", "ext_mark",
)

MARK_PRIORITY: tuple[str, ...] = ("byte_verified", "field_read", "name_only")
_KIND_ORDER = {"CMD": 0, "NOTI": 1}


def load_coverage(raw_dir: Path) -> list[dict]:
    """读 coverage-by-opcode.tsv（真产物）；缺文件即报错，不静默给空表。"""
    p = Path(raw_dir) / "coverage-by-opcode.tsv"
    if not p.is_file():
        raise FileNotFoundError(f"缺原始产物：{p}")
    with p.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in row.items()} for row in csv.DictReader(fh, delimiter="\t")]


def load_external(raw_dir: Path) -> dict[tuple[str, int], list[tuple[str, str]]]:
    """读 tiers-external.tsv；首行以 '#' 开头的表头行按注释跳过。"""
    p = Path(raw_dir) / "tiers-external.tsv"
    if not p.is_file():
        raise FileNotFoundError(f"缺原始产物：{p}")
    out: dict[tuple[str, int], list[tuple[str, str]]] = {}
    with p.open(encoding="utf-8", newline="") as fh:
        for line in fh:
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = line.rstrip("\r\n").split("\t")
            if len(parts) < 4:
                raise ValueError(f"tiers-external.tsv 行格式非法：{line!r}")
            kind, raw_id, mark, evidence = parts[0], parts[1], parts[2], parts[3]
            if mark not in MARK_PRIORITY:
                raise ValueError(f"未知 ext_mark：{mark!r}")
            out.setdefault((kind, int(raw_id)), []).append((mark, evidence))
    return out


def _highest(marks: list[str]) -> str:
    for candidate in MARK_PRIORITY:
        if candidate in marks:
            return candidate
    return "name_only"


def merge_rows(coverage: list[dict], external: dict) -> list[dict]:
    rows: list[dict] = []
    for raw in coverage:
        key = (raw["kind"], int(raw["id"]))
        pairs = external.get(key, [])
        marks = [m for m, _ in pairs]
        evidence = [e for _, e in pairs if e]
        rows.append({
            "kind": raw["kind"],
            "id": str(key[1]),
            "name": raw.get("name", ""),
            "rodata_va": raw.get("rodata_va", ""),
            "dispatcher": raw.get("dispatcher", ""),
            "status": raw.get("status", ""),
            "tier": raw.get("tier", ""),
            "read_count": raw.get("read_count", "0"),
            "write_count": raw.get("write_count", "0"),
            "ext_mark": _highest(marks) if marks else "name_only",
            "_ext_evidence": "; ".join(evidence),
        })
    rows.sort(key=lambda r: (_KIND_ORDER.get(r["kind"], 9), int(r["id"])))
    return rows


def write_tsv(path: Path, columns: tuple[str, ...], rows: list[dict]) -> None:
    """LF + UTF-8 无 BOM + 末尾单个换行；字段含制表符/换行即报错（不做静默清洗）。"""
    path = Path(path)
    for row in rows:
        for col in columns:
            value = str(row.get(col, ""))
            if "\t" in value or "\n" in value or "\r" in value:
                raise ValueError(f"列 {col!r} 的值含制表符或换行：{value!r}")
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = ["\t".join(columns)]
    lines += ["\t".join(str(r.get(c, "")) for c in columns) for r in rows]
    path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))


def import_map(raw_dir: Path, out_dir: Path) -> dict:
    rows = merge_rows(load_coverage(raw_dir), load_external(raw_dir))
    target = Path(out_dir) / "map" / "opcode-map.tsv"
    write_tsv(target, MAP_COLUMNS, rows)
    return {"rows": len(rows), "path": target}
