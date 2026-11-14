"""Restore the verified Chapter Two pet catalog in new, matching PVF copies."""

import argparse
import json
from pathlib import Path
import re

from port import Archive, field, list_rows, require, table, verify_views, write_views


CLIENT_SHA = "06734f9c5b23cf3bce364890403ae1661311447ca8adebd1b79fcfd8e00fab9b"
SERVER_SHA = "9cb02199f61602ca16f9c82e279665a6e77fdb281d9e076226c177a8455f33c7"
SHOP = "etc/newcashshop.etc"
EQUIPMENT = "equipment/equipment.lst"
STACKABLE = "stackable/stackable.lst"
EGGS = {
    105008: (63006, 63000, 9900),
    105009: (63007, 63003, 6600),
    105010: (63013, 63008, 9900),
    105011: (63014, 63009, 9900),
    105012: (63015, 63010, 9900),
    105013: (63016, 63011, 9900),
    105014: (63017, 63012, 9900),
    105015: (63018, 63019, 9900),
    105016: (63022, 63023, 6600),
    105017: (63024, 63025, 6600),
    105018: (63028, 63027, 9900),
    105019: (63031, 63032, 9900),
}
CONSUMABLES = {
    105001: (24, 100, 900),
    105002: (24, 300, 2400),
    105003: (24, 500, 3600),
    105004: (24, 1000, 7200),
    105005: (25, 3, 900),
}
KEEP_DISABLED = {105000, 105006, 105007}
ROW = re.compile(
    rb"(?m)^([ \t]*)(//)?(\d+)([ \t]+\d+(?:[ \t]+\d+){4}[ \t]+`)([^`\r\n]*)"
    rb"(`[ \t]+0[ \t]+0[ \t]*)(?=\r?\r?$)"
)


def approved_products(source):
    equipment = list_rows(source.read(EQUIPMENT))
    stackable = list_rows(source.read(STACKABLE))
    creature_paths = list_rows(source.read("creature/creature.lst"))
    creature_scripts = list_rows(source.read("creature/script/creature.lst"))
    equipment_names = table(source.read("equipment/equipment.chn.str"), "cp936")
    stackable_names = table(source.read("stackable/stackable.chn.str"), "cp936")
    products = {}
    for commodity, (item, hatch, price) in EGGS.items():
        path = "equipment/" + equipment[item]
        egg = source.read(path)
        output = source.read("equipment/" + equipment[hatch])
        require(field(egg, "equipment type") == b"`[creature]` 0"
                and field(egg, "sub type") == b"1" and field(egg, "attach type") == b"`[sealing]`"
                and field(egg, "output index") == str(hatch).encode()
                and field(egg, "name") == f"<3::name_{item}>".encode(), f"Invalid egg {item}")
        require(all(not field(egg, tag) for tag in (
            "usable period", "expiration date", "usable period after unsealing")),
            f"Unexpected lifetime on egg {item}")
        require(field(output, "equipment type") == b"`[creature]` 0"
                and field(output, "sub type") == b"0", f"Invalid creature output {hatch}")
        species = int(field(egg, "creature species"))
        require(field(output, "creature species") == str(species).encode()
                and "creature/" + creature_paths[species] in source.entries
                and "creature/script/" + creature_scripts[species] in source.entries,
                f"Creature resources missing for {item}")
        label = equipment_names[f"name_{item}"]
        require(label == f"宠物蛋 ({equipment_names[f'name_{hatch}']})", f"Name mapping differs for {item}")
        products[commodity] = dict(itemId=item, quantity=1, price=price, label=label,
                                   hatchItemId=hatch, species=species, script=path, tab="egg")
    for commodity, (item, quantity, price) in CONSUMABLES.items():
        path = "stackable/" + stackable[item]
        data = source.read(path)
        kind = b"`[feed]` 1" if item == 24 else b"`[creature]` 1"
        require(field(data, "stackable type") == kind and field(data, "attach type") == b"`[trade]`"
                and field(data, "creature minimum level") == b"1", f"Invalid pet consumable {item}")
        label = f"{stackable_names[f'name_{item}']} ({quantity}个)"
        products[commodity] = dict(itemId=item, quantity=quantity, price=price, label=label,
                                   script=path, tab="consumable")
    return products


def restore_catalog(data, products):
    parts = list(re.finditer(rb"\[creature\](.*?)\[/creature\]", data, re.S))
    require(len(parts) == 1, "Expected one creature section")
    part = parts[0]
    section = part[1]
    rows = list(ROW.finditer(section))
    ids = [int(row[3]) for row in rows]
    require(len(ids) == len(set(ids)) == len(products) + len(KEEP_DISABLED)
            and set(ids) == products.keys() | KEEP_DISABLED, "Historical shop inventory differs")
    require([int(row[3]) for row in rows if not row[2]] == [105009], "Active baseline pet selection differs")
    outside = data[:part.start(1)] + data[part.end(1):]
    for commodity in products:
        require(not re.search(rb"(?m)^[ \t]*" + str(commodity).encode() + rb"[ \t]", outside),
                f"Commodity {commodity} conflicts with an existing shop record or restriction")

    def restore(row):
        commodity = int(row[3])
        if commodity not in products:
            return row[0]
        product = products[commodity]
        require(list(map(int, row[4][:-1].split())) ==
                [product["itemId"], product["quantity"], 0, 0, product["price"]],
                f"Original data differs for {commodity}")
        if commodity in EGGS:
            require(row[5].decode("cp936") == "当前不使用", f"Unexpected egg description {commodity}")
        return row[1] + row[3] + row[4] + product["label"].encode("cp936") + row[6]

    result = ROW.sub(restore, section)
    require({int(row[3]) for row in ROW.finditer(result) if not row[2]} == products.keys(),
            "Active pet catalog differs from the reviewed selection")
    require("当前不使用".encode("cp936") not in result, "Placeholder remains in the restored catalog")
    restored = data[:part.start(1)] + result + data[part.end(1):]
    # Current gift handling supports permanent sealed eggs in this group.
    # Prevent the client from offering consumable gifts the server would reject.
    matches = list(re.finditer(rb"(?m)^\[/gift disallowance\]", restored))
    require(len(matches) == 1, "Expected one gift exclusion section")
    at = matches[0].start()
    restrictions = b"".join(str(commodity).encode() + b"\r\r\n" for commodity in CONSUMABLES)
    return restored[:at] + restrictions + restored[at:]


def localize_consumable(source, path, item, description):
    data = source.read(path)
    expected_names = {
        24: (b"`" + "크리쳐 먹이".encode("cp949") + b"`", "宠物饲料"),
        25: (b"`" + "리네임 카드".encode("cp949") + b"`", "宠物更名卡"),
    }
    korean, chinese = expected_names[item]
    name = field(data, "name")
    old_description = field(data, "explain")
    require(name == korean and old_description.startswith(b"`") and old_description.endswith(b"`")
            and data.count(name) == data.count(old_description) == 1, f"Unexpected source text for {item}")
    return data.replace(name, b"`" + chinese.encode("cp936") + b"`", 1).replace(
        old_description, b"`" + description.encode("cp936") + b"`", 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    client, server = Archive(args.client), Archive(args.server)
    require(client.native and not server.native and client.sha == CLIENT_SHA and server.sha == SERVER_SHA,
            "Expected the reviewed Chapter Two client and server PVF pair")
    require(client.records == server.records and client.body == server.body, "Source views are not identical")
    products = approved_products(client)
    changes = {SHOP: restore_catalog(client.read(SHOP), products)}
    for commodity, description in (
        (105001, "使用后增加当前装备宠物30点饱食度。"),
        (105005, "可更改当前装备宠物的名字一次。输入“没有名字”恢复初始名称。"),
    ):
        product = products[commodity]
        changes[product["script"]] = localize_consumable(client, product["script"], product["itemId"], description)
    write_views(client, changes, args.output)
    report = verify_views(client, changes, args.output)
    server.unchanged()
    report.update(sourceServerSha256=server.sha, products=products, excludedCommodities=sorted(KEEP_DISABLED),
                  consumableGiftDisallowed=sorted(CONSUMABLES),
                  originalEggCount=1, restoredEggCount=len(EGGS), restoredConsumableCount=len(CONSUMABLES))
    (args.output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("entriesChecked", "changedEntries", "nativeSha256", "serverSha256")}, indent=2))


if __name__ == "__main__":
    main()
