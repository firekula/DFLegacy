"""Read-only checks of actual C1/C2 shop and jar resources, with saved evidence."""
import argparse
import json
import re
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "pvf" / "chapter-two"))
from port import Archive, canonical, field, list_rows, shop_slots


def verify(source, target, packages=False):
    checks, failures, jars, shops = 0, [], [], []
    def check(ok, label):
        nonlocal checks
        checks += 1
        if not ok:
            failures.append(label)
    c1 = Archive(source, validate_entry_crc=False)
    c2 = Archive(target)
    old_index = list_rows(c1.read("stackable/stackable.lst"))
    new_index = list_rows(c2.read("stackable/stackable.lst"))
    indexed_ids = set(new_index) | set(list_rows(c2.read("equipment/equipment.lst")))
    for n in range(1, 6):
        path = f"itemshop/regacy{n}.shp"
        old, new = c1.read(path), c2.read(path)
        check(shop_slots(old) == shop_slots(new), f"shop {n}: original ordered sale slots")
        check(field(old, "NPC") == field(new, "NPC"), f"shop {n}: NPC identity")
        ids = [i for i in shop_slots(new) if i > 0]
        check(all(i in indexed_ids for i in ids), f"shop {n}: all sale items indexed")
        shops.append({"path": path, "items": ids})
    for ident, relative in old_index.items():
        before = c1.read("stackable/" + relative)
        if field(before, "stackable type") != b"`[upgradable legacy]` 1":
            continue
        check(new_index.get(ident) == relative, f"jar {ident}: identity path")
        if new_index.get(ident) != relative:
            continue
        after = c2.read("stackable/" + relative)
        check(field(after, "stackable type") == field(before, "stackable type"), f"jar {ident}: usable")
        for tag in ("price", "int data", "minimum level", "icon", "lottery use cost"):
            check(canonical(field(after, tag)) == canonical(field(before, tag)), f"jar {ident}: {tag} preserved")
        jars.append({"id": ident, "path": relative, "name": field(before, "name").decode("cp936").strip("`"),
                     "price": field(after, "price").decode(), "type": field(after, "stackable type").decode()})
    if packages:
        section, products = "", {}
        for raw in c2.read("etc/newcashshop.etc").decode("cp936", errors="replace").splitlines():
            line = raw.split("//", 1)[0].strip()
            if line.startswith("[") and line.endswith("]"):
                section = line[1:-1]
            elif line and section in ("item", "package"):
                parts = line.split("`", 1)[0].split()
                if parts and parts[0].isdigit():
                    ident = int(parts[0])
                    check(ident not in products, f"shop product {ident}: unique")
                    products[ident] = (int(parts[1]), int(parts[4 if section == "package" else 5]))
                    if ident in (109038, 109039, 109040, 109041, 109052, 109053, 109054, 109055, 109056):
                        # Native listing predicate rejects sticker 99 outside its
                        # historical event. Restored permanent products must show.
                        suffix = line.rsplit("`", 1)[-1].split()
                        check(bool(suffix) and suffix[0] == "0", f"package {parts[1]}: visible outside historical event")
        for ident in (38, 39, 40, 41, 49, 52, 53, 54, 55, 56):
            expected_price = 29800 if ident in (38, 39, 40, 41) else 13550 if ident == 49 else 22200
            check(products.get(109000 + ident) == (ident, expected_price), f"package {ident}: unique restored product and historical price")
        for ident in (9, 11, 27, 63, 64, 65, 66, 67, 69, 70, 71, 72, 73):
            check(not any(p[0] == ident for p in products.values()), f"unsupported entitlement package {ident}: not sold")
    c1.unchanged()
    c2.unchanged()
    return {"checks": checks, "failures": failures, "sourceSha256": c1.sha, "targetSha256": c2.sha,
            "shops": shops, "jars": jars}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("target")
    parser.add_argument("report", type=Path)
    parser.add_argument("--packages", action="store_true")
    args = parser.parse_args()
    if args.report.exists():
        raise FileExistsError("Never overwrite prior evidence")
    result = verify(args.source, args.target, args.packages)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Resource checks: {result['checks']}; failures: {len(result['failures'])}; jars: {len(result['jars'])}")
    for failure in result["failures"][:8]:
        print(failure)
    raise SystemExit(bool(result["failures"]))
