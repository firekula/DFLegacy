"""Add one stackable or equipment item to Sinda's shop.

Read-only on the inputs; writes a new paired native/server Script.pvf and a
verification report. The shop layout is 7-int rows inside one ``[sell item]``
block; the first free ``-1`` slot is replaced in place so the rest of the
script keeps its original bytes.

Usage:
  python -B tools/pvf/chapter-two/sinda_shop_item.py \
      --native <client Script.pvf> --server <server Script.pvf> \
      --output <new-directory> --item 27600 [--shop itemshop/equipmentshop2.shp]
"""
import argparse
import json
import re
import struct
import zlib
from pathlib import Path

from port import Archive, clean, field, list_rows, rotate, sha

ITEM = 7274
SHOP = "itemshop/equipmentshop2.shp"  # itemshop.lst 3 -> EquipmentShop2.shp, [NPC] 4 = Sinda
LISTS = (("stackable/stackable.lst", "stackable/"), ("equipment/equipment.lst", "equipment/"))


def require(ok, message):
    if not ok:
        raise ValueError(message)


def read_archive(path):
    try:
        return Archive(path)
    except ValueError:
        return Archive(path, validate_entry_crc=False)


def resolve_item(arc, item):
    for list_path, prefix in LISTS:
        rows = list_rows(arc.read(list_path))
        name = rows.get(item)
        if not name:
            continue
        path = name.lower()
        if not path.startswith(prefix):
            path = prefix + path
        require(path in arc.entries, f"item {item} points at a missing script: {path}")
        return path, list_path
    raise ValueError(f"item {item} is not in stackable.lst or equipment.lst")


def shop_rows(data):
    blocks = list(re.finditer(rb"\[sell item\]([\s\S]*?)\[/sell item\]", clean(data)))
    require(len(blocks) == 1, "expected one [sell item] block")
    rows = [int(x) for x in re.findall(rb"-?\d+", blocks[0][1])]
    require(rows and len(rows) % 7 == 0, "shop rows are not 7-int rows")
    return rows


def patch_shop(data, item):
    require(data.count(b"[sell item]") == 1 and data.count(b"[/sell item]") == 1, "ambiguous shop tags")
    start = data.index(b"[sell item]") + len(b"[sell item]")
    end = data.index(b"[/sell item]")
    block = data[start:end]
    rows = shop_rows(data)
    require(item not in rows, f"item {item} is already sold in this shop")
    require(field(data, "NPC").strip() == b"4", "shop NPC is not Sinda (4)")
    # Locate the first standalone -1 token on a non-comment line.
    token = re.compile(rb"(?<![\d-])-1(?!\d)")
    patched = None
    offset = 0
    for line in block.splitlines(keepends=True):
        if not line.lstrip().startswith(b"//"):
            match = token.search(line)
            if match:
                patched = block[:offset] + line[:match.start()] + str(item).encode() + line[match.end():] \
                    + block[offset + len(line):]
                break
        offset += len(line)
    require(patched is not None, "shop has no free -1 slot")
    result = data[:start] + patched + data[end:]
    expected = rows.copy()
    expected[expected.index(-1)] = item
    require(shop_rows(result) == expected, "patched shop layout differs")
    return result, rows, expected


def write_view(source, changes, output, native_format):
    body = bytearray(source.body)
    records = list(source.records)
    for name in sorted(changes.keys() - source.entries.keys()):
        ident = max(row[0] for _, row in records) + 1
        records.append((name, (ident, name.encode("cp949"), 0, 0, 0)))
    rewritten = []
    for name, (ident, path, size, key, offset) in records:
        if name in changes:
            content = changes[name]
            padded = content + b"\0" * ((-len(content)) % 4)
            key = zlib.crc32(padded, ident)
            offset, size = len(body), len(content)
            body.extend(rotate(padded, key, encrypt=True))
        rewritten.append((name, (ident, path, size, key, offset)))
    tree = bytearray()
    for _, (ident, name, size, key, offset) in rewritten:
        if native_format:
            tree.extend(struct.pack("<II", ident, len(name)) + name + struct.pack("<III", size, key, offset))
        else:
            tree.extend(struct.pack("<5I", ident, offset, size, key, len(name)) + name)
    tree.extend(b"\0" * ((-len(tree)) % 4))
    key = zlib.crc32(tree, len(rewritten))
    prefix = source.raw[:44] if native_format else b"DNF_SCRIPT_PACK"
    header = struct.pack("<3I", len(tree), key, len(rewritten))
    with output.open("xb") as stream:
        stream.write(prefix + header + rotate(tree, key, encrypt=True))
        stream.write(body)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--server", type=Path,
                        help="optional current server view; used only as a content cross-check")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--item", type=int, default=ITEM)
    parser.add_argument("--shop", default=SHOP)
    args = parser.parse_args()
    require(not args.output.exists(), "choose a new output directory")

    native = read_archive(args.native)
    require(native.native, "native input is not the client container")
    server_extras = []
    if args.server is not None:
        server = read_archive(args.server)
        require(not server.native, "server input is not the DNF_SCRIPT_PACK container")
        extras = server.entries.keys() - native.entries.keys()
        require(not extras, "server view has entries the client view lacks: " + ", ".join(sorted(extras)))
        server_extras = sorted(native.entries.keys() - server.entries.keys())
        for name in server.entries:
            require(native.read(name) == server.read(name), "decoded content differs: " + name)

    item_path, item_list = resolve_item(native, args.item)
    item_script = native.read(item_path)
    price = field(item_script, "price").strip()
    require(price.isdigit() and int(price) > 0, f"item {args.item} has no positive price")
    if item_path.startswith("equipment/"):
        item_kind = "equipment"
        require(field(item_script, "equipment type").lstrip(b"`").startswith(b"["),
                f"item {args.item} has no equipment type")
    else:
        item_kind = "stackable"
        require(field(item_script, "stackable type").lstrip(b"`").startswith(b"["),
                f"item {args.item} has no stackable type")

    shop_before = native.read(args.shop)
    shop_after, rows_before, rows_after = patch_shop(shop_before, args.item)
    changes = {args.shop: shop_after}

    args.output.mkdir(parents=True)
    native_out = args.output / "Script.chapter-two.native.pvf"
    server_out = args.output / "Script.chapter-two.server.pvf"
    write_view(native, changes, native_out, native_format=True)
    write_view(native, changes, server_out, native_format=False)

    native_check = Archive(native_out)
    server_check = Archive(server_out)
    require(native_check.entries.keys() == server_check.entries.keys() == native.entries.keys(),
            "output inventory changed")
    differences = []
    for name in native.entries:
        expected = changes.get(name, native.read(name))
        require(native_check.read(name) == server_check.read(name) == expected,
                "output bytes differ: " + name)
        if expected != native.read(name):
            differences.append(name)
    require(differences == [args.shop], "change allowlist differs")
    native.unchanged()
    if args.server is not None:
        server.unchanged()

    report = {
        "item": args.item,
        "itemPath": item_path,
        "itemList": item_list,
        "itemKind": item_kind,
        "itemPrice": int(price),
        "itemType": field(item_script, "equipment type" if item_kind == "equipment" else "stackable type").decode("cp949"),
        "shop": args.shop,
        "shopNpc": field(shop_after, "NPC").decode("cp949"),
        "rowsBefore": rows_before,
        "rowsAfter": rows_after,
        "changedEntry": args.shop,
        "nativeInputSha256": native.sha,
        "serverInputSha256": server.sha if args.server is not None else None,
        "serverOnlyMissingEntries": server_extras,
        "nativeOutputSha256": sha(native_out.read_bytes()),
        "serverOutputSha256": sha(server_out.read_bytes()),
        "allOtherEntriesEqual": True,
        "inGameVerified": False,
    }
    (args.output / "verification.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in
                      ("item", "itemPrice", "itemType", "shop", "nativeOutputSha256", "serverOutputSha256")},
                     ensure_ascii=False, indent=2))
    print("PASS only " + args.shop + " changed; inputs unchanged; native/server content equal")


if __name__ == "__main__":
    main()
