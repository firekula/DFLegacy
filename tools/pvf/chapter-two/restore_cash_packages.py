"""Restore ten verified legacy packages at historical prices in an isolated PVF."""
import argparse
import json
from pathlib import Path
import re
from port import Archive, require, verify_views, write_views

SOURCE_SHA = "12589d68e552ff5f74cd170ee1cb0dd8e37fc419e65d96f4cb3fd85027bbf08f"
SUPPORTED = (38, 39, 40, 41, 49, 52, 53, 54, 55, 56)
SHOP = "etc/newcashshop.etc"


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("source")
    p.add_argument("output", type=Path)
    a = p.parse_args()
    source = Archive(a.source)
    require(source.sha == SOURCE_SHA, "Expected reviewed jar-restoration candidate")
    original = source.read(SHOP)
    section, historical, active = b"", {}, set()
    for raw in original.splitlines():
        line = raw.strip()
        if line.startswith(b"[") and line.endswith(b"]"):
            section = line[1:-1]
        m = re.match(rb"^(//\s*)?(\d+)\s+(\d+)\s+", line)
        if not m:
            continue
        if not m[1]:
            active.add(int(m[2]))
        elif section in (b"item", b"package") and int(m[3]) in SUPPORTED:
            ident = int(m[3])
            require(ident not in historical, f"Ambiguous historical package row {ident}")
            historical[ident] = (section, re.sub(rb"^//\s*", b"", line))
    require(set(historical) == set(SUPPORTED), "Historical package row missing")
    additions, records = {b"item": [], b"package": []}, []
    for ident in SUPPORTED:
        section, row = historical[ident]
        commodity = 109000 + ident
        require(commodity not in active, "New commodity conflicts with existing catalog")
        new_row = re.sub(rb"^\d+", str(commodity).encode(), row, count=1)
        numbers = new_row.split(b"`", 1)[0].split()
        price = int(numbers[4 if section == b"package" else 5])
        require(price == (29800 if ident <= 41 else 13550 if ident == 49 else 22200), "Historical price differs")
        if ident == 49:
            # This row's old display label is Korean bytes in a Chinese table.
            # Localize only the label; the original price/flags stay unchanged.
            new_row = re.sub(rb"`[^`]*`", lambda _: "`达人之契约礼包 (15天)`".encode("cp936"), new_row, count=1)
        else:
            # Sticker 99 is an expired event visibility gate in the Chapter Two
            # client (941191 -> 436030). Keep ordinary restored items visible.
            new_row, changed = re.subn(rb"(`[^`]*`\s+)99(?=\s|$)", rb"\g<1>0", new_row, count=1)
            require(changed == 1, "Expected historical event sticker on restored booster")
        additions[section].append(new_row)
        records.append({"itemId": ident, "commodity": commodity, "price": price, "section": section.decode(),
                        "historicalRowHex": row.hex(), "restoredRowHex": new_row.hex()})
    result = original
    for section, lines in additions.items():
        anchor = rb"(?m)^(\[" + section + rb"\][^\r\n]*\r?\n)"
        result, count = re.subn(anchor, lambda m: m[1] + b"\r\n// DFLegacy verified legacy packages\r\n" + b"\r\n".join(lines) + b"\r\n", result)
        require(count == 1, "Expected one target shop section")
    changes = {SHOP: result}
    write_views(source, changes, a.output)
    report = verify_views(source, changes, a.output)
    report.update(products=records, excludedMissingEntitlements=[9, 11, 27, 63, 64, 65, 66, 67, 69, 70, 71, 72, 73],
                  scope="Ten fully defined old packages only; original rewards and historical prices; new commodity IDs avoid active collisions")
    (a.output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("entriesChecked", "changedEntries", "nativeSha256", "serverSha256")}, indent=2))


if __name__ == "__main__":
    main()
