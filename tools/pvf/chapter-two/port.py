"""Port verified ACT1 semantics into one pinned Chapter Two archive.

Only Python's standard library is required. Never replaces an existing file.
The native UUID archive and server view share identical encrypted body bytes.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import struct
from uuid import UUID
import zlib

SOURCE_SHA = "0cb607b9502084ecb9e8cd45a67087e1e96f58f4ac5420d3fc673adc73788ae0"
MAIN_SHA = "69aa1a0338408fbcc3eff381924311ba42119a2cd2db4e8ffd2fab369bb9b34b"
SHOP = "itemshop/equipmentshop7.shp"
QUEST_TABLE = "quest/quest.chn.str"
DISABLED = "quest/dflegacy-disabled-quests.lst"
SKILLS = [
    ("mage", 57, "flamestrike", [0,20,0,0,0], [0,20,10,10,0]),
    ("mage", 21, "summonspiritfire", [0,0,20,0,0], [0,10,20,10,0]),
    ("mage", 22, "summonspiritwater", [0,0,20,0,0], [0,10,20,10,0]),
    ("mage", 23, "summonspiritdark", [0,0,20,0,0], [0,10,20,10,0]),
    ("mage", 24, "summonspiritlight", [0,0,20,0,0], [0,10,20,10,0]),
    ("mage", 65, "pokethrow", [0,0,0,20,0], [0,10,10,20,0]),
    ("priest", 47, "chakraofpassion", [0,0,0,10,0], [0,5,5,10,0]),
    ("priest", 48, "chakraofcalmness", [0,0,0,10,0], [0,5,5,10,0]),
    ("priest", 30, "holycounter", [0,0,10,0,0], [0,5,10,5,0]),
    ("priest", 41, "homerun", [0,0,0,20,0], [0,10,0,20,0]),
    ("swordman", 33, "equiplightsword", [0,1,0,0,0], [0,1,1,1,1]),
]
BOSSES = ["monster/dollmaster/masterdogri.mob", "monster/draconian/lakius.mob",
          "monster/evileye/evileye.mob", "monster/flydragon/selion.mob",
          "monster/golem/platany.mob", "monster/lordoflight/lordoflight.mob",
          "monster/soldier/heavenexpeller.mob"]


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def clean(data):
    return re.sub(rb"(?m)//[^\r\n]*", b"", data)


def canonical(data):
    return re.sub(rb"\s+", b" ", clean(data)).strip()


def rotate(data, key, encrypt=False):
    require(len(data) % 4 == 0, "Unaligned cipher data")
    out = bytearray(len(data))
    for i, (word,) in enumerate(struct.iter_unpack("<I", data)):
        if encrypt:
            value = (((word << 6) | (word >> 26)) & 0xffffffff) ^ key
        else:
            value = word ^ key
            value = ((value >> 6) | (value << 26)) & 0xffffffff
        struct.pack_into("<I", out, 4*i, value)
    return bytes(out)


class Archive:
    def __init__(self, path, validate_entry_crc=True):
        self.path = Path(path).resolve(strict=True)
        self.raw = self.path.read_bytes()
        self.sha = sha(self.raw)
        self.validate_entry_crc = validate_entry_crc
        self.native = not self.raw.startswith(b"DNF_SCRIPT_PACK")
        if self.native:
            guid_len = struct.unpack_from("<I", self.raw)[0]
            require(guid_len == 36, "Expected 36-byte native UUID")
            UUID(self.raw[4:40].decode("ascii"))
            self.prefix = self.raw[:44]
            self.version, length, key, count = struct.unpack_from("<4I", self.raw, 40)
            start = 56
        else:
            self.prefix = b"DNF_SCRIPT_PACK"
            self.version = None
            length, key, count = struct.unpack_from("<3I", self.raw, 15)
            start = 27
        require(0 < length <= 16*1024*1024 and length % 4 == 0, "Invalid tree length")
        require(0 < count <= 200000 and start+length <= len(self.raw), "Invalid tree bounds")
        tree = rotate(self.raw[start:start+length], key)
        require(zlib.crc32(tree, count) == key, "Directory CRC mismatch")
        self.body = self.raw[start+length:]
        self.entries = {}
        self.records = []
        pos = 0
        ranges = []
        for _ in range(count):
            if self.native:
                fid, n = struct.unpack_from("<II", tree, pos)
                pos += 8
                name = tree[pos:pos+n]
                pos += n
                size, entry_key, offset = struct.unpack_from("<III", tree, pos)
                pos += 12
            else:
                fid, offset, size, entry_key, n = struct.unpack_from("<5I", tree, pos)
                pos += 20
                name = tree[pos:pos+n]
                pos += n
            path_name = name.decode("cp949").replace("\\", "/").lower().rstrip("\0")
            require(n > 0 and path_name and not path_name.startswith("/")
                    and all(p not in ("", ".", "..") for p in path_name.split("/")), "Unsafe path")
            require(path_name not in self.entries, "Duplicate path: " + path_name)
            require(offset % 4 == 0 and offset+((size+3)&~3) <= len(self.body), "Invalid entry bounds")
            row = (fid, name, size, entry_key, offset)
            self.entries[path_name] = row
            self.records.append((path_name, row))
            if size:
                ranges.append((offset, offset+((size+3)&~3)))
        require(pos <= len(tree) and len(tree)-pos <= 7 and not any(tree[pos:]), "Unexpected tree tail")
        last = 0
        for a,b in sorted(ranges):
            require(a >= last, "Overlapping entry bodies")
            last = b
        self.duplicate_ids = {str(k): v for k,v in Counter(r[0] for _,r in self.records).items() if v>1}

    def read(self, name):
        _, _, size, key, offset = self.entries[name]
        result = rotate(self.body[offset:offset+((size+3)&~3)], key)
        # Original archives use the file number as the CRC seed.
        if self.validate_entry_crc:
            require(zlib.crc32(result, self.entries[name][0]) == key, "Entry CRC mismatch: " + name)
        return result[:size]

    def unchanged(self):
        require(sha(self.path.read_bytes()) == self.sha, "Source changed while exporting")


def field(data, tag):
    m = re.search(rb"(?mi)^\s*\[" + re.escape(tag.encode()) + rb"\][ \t]*", clean(data))
    if not m:
        return b""
    rest = clean(data)[m.end():]
    end = re.search(rb"(?m)^\s*\[", rest)
    return (rest[:end.start()] if end else rest).strip()


def list_rows(data):
    rows = {}
    for m in re.finditer(rb"(?m)^\s*(\d+)\s+`([^`]+)`", clean(data)):
        ident = int(m[1])
        require(ident not in rows, "Duplicate list ID")
        rows[ident] = m[2].decode("cp949").replace("\\", "/").lower()
    return rows


def table(data, codec):
    rows = {}
    for m in re.finditer(rb"(?m)^([a-zA-Z0-9_.-]+)>([^\r\n]*)", data):
        key = m[1].decode("ascii")
        require(key not in rows, "Duplicate string key: " + key)
        rows[key] = m[2].decode(codec).strip()
    return rows


def shop_slots(data):
    blocks = list(re.finditer(rb"\[sell item\]([\s\S]*?)\[/sell item\]", clean(data)))
    require(len(blocks) == 1, "Expected one sell item block")
    return [int(x) for x in re.findall(rb"-?\d+", blocks[0][1])]


def patch_shop(before, priced, unpriced):
    slots = shop_slots(before)
    require(len(slots)%7 == 0 and not (set(slots)&set(unpriced)), "Unexpected original shop")
    new = sorted(set(priced)-set(slots))
    newline = b"\r\n" if b"\r\n" in before else b"\n"
    extra = new + [-1]*((-len(new))%7)
    block = newline+b"// DFLegacy Chapter Two: existing positive material prices only."+newline
    block += newline.join(b"\t".join(str(i).encode() for i in extra[n:n+7]) for n in range(0,len(extra),7))+newline
    require(before.count(b"[/sell item]") == 1, "Ambiguous shop")
    after = before.replace(b"[/sell item]", block+b"[/sell item]")
    require(shop_slots(after) == slots+extra, "Shop layout changed")
    return after


def patch_invitation(before):
    blocks = list(re.finditer(rb"\[item\]([\s\S]*?)\[/item\]", before))
    require(len(blocks) == 1, "Expected one MOB item block")
    block = blocks[0]
    rows = list(re.finditer(rb"(?m)^([ \t]*3213[ \t]+)(\d+)([ \t]*(?://[^\r\n]*)?)(?=\r?$)", block[1]))
    require(len(rows) == 1, "Expected one existing invitation row")
    require(int(rows[0][2]) in (5000,7000), "Unrecognized invitation probability")
    start,end = block.start(1)+rows[0].start(2), block.start(1)+rows[0].end(2)
    return before[:start]+b"5000"+before[end:]


def patch_skill(before, expected, after):
    require(b"[steel learning skill]" not in clean(before), "Existing steal policy needs review")
    pattern = rb"(?m)^(\[growtype maximum level\][ \t]+)([0-9 \t]+)(?=[ \t]*(?://|\r?$))"
    rows = list(re.finditer(pattern, before))
    require(len(rows)==1 and [int(x) for x in rows[0][2].split()] == expected, "Unknown skill caps")
    row = rows[0]
    result = before[:row.start(2)]+b" ".join(str(x).encode() for x in after)+before[row.end(2):]
    newline = b"\r\n" if b"\r\n" in before else b"\n"
    return result+newline+newline.join([b"[steel learning skill]",b"1",b"50",b"200",b"4",b""])


def patch_string_table(before, replacements):
    seen = set()
    def apply(m):
        key = m[1].decode("ascii")
        if key not in replacements:
            return m[0]
        require(key not in seen, "Repeated replacement key")
        require(m[2].decode("cp936").strip() == "不使用", "Expected placeholder text")
        seen.add(key)
        return m[1]+b">"+replacements[key].encode("cp936")
    after = re.sub(rb"(?m)^([a-zA-Z0-9_.-]+)>([^\r\n]*)", apply, before)
    require(seen == replacements.keys(), "Missing replacement keys")
    return after


def patch_channels(before):
    names = [x.encode("cp936") for x in ["初级决斗场","中级决斗场","高级决斗场","自由决斗场"]]
    seen = []
    def group(m):
        body = m[1]
        ident = int(re.search(rb"\d+",body)[0])
        if ident not in range(1,10):
            return m[0]
        rows = list(re.finditer(rb"(?m)^\s*(5\d)\s+<[^>]+>[^\r\n]*(?:\r?\n|$)",body))
        require(len(rows) == (4 if ident==8 else 5), "Unknown PvP channel group layout")
        body = re.sub(rb"(?m)^\s*(5\d)\s+<[^>]+>[^\r\n]*(?:\r?\n|$)",b"",body)
        newline = b"\r\n" if b"\r\n" in body else b"\n"
        for i,n in enumerate([51,52,53,55]):
            body += b"\t"+str(n).encode()+b" `"+names[i]+b"` "+str(5 if i==3 else 4).encode()+b" `[none]` 0 0 0 0 0 0 0 0 0 0 0"+newline
        seen.append(ident)
        return b"[server]"+body+b"[/server]"
    result = re.sub(rb"\[server\]([\s\S]*?)\[/server\]",group,before)
    require(sorted(seen)==list(range(1,10)), "Expected nine server groups")
    return result


def patch_ranks(before):
    names = [x.encode("cp936") for x in ["初级决斗场","中级决斗场","高级决斗场","自由决斗场"]]
    ranges = [b"0 9 0",b"10 19 1",b"20 30 2",b"0 30 4"]
    block = b"[PVP CHANNEL INFO]\r\n"
    for g in range(1,10):
        block += str(g).encode()+b" 4\r\n"+b"".join(b"`"+n+b"` "+r+b"\r\n" for n,r in zip(names,ranges))+b"\r\n"
    block += b"[/PVP CHANNEL INFO]"
    result,n = re.subn(rb"\[PVP CHANNEL INFO\][\s\S]*?\[/PVP CHANNEL INFO\]",lambda _:block,before)
    require(n==1,"Expected one rank table")
    return result


def build_changes(source, evidence, main):
    require(source.sha==SOURCE_SHA and source.native and source.version==42831, "Unexpected Chapter Two source")
    require(len(source.entries)==69398 and len(source.duplicate_ids)==1, "Source inventory changed")
    require(main.sha==MAIN_SHA, "Unexpected main PVF evidence")
    changes,report = {},{"skills":[],"quests":{},"bosses":[],"limitations":[]}
    quests = {i:"quest/"+n for i,n in list_rows(source.read("quest/quest.lst")).items()}
    own_strings = table(source.read(QUEST_TABLE),"cp936")
    own_korean = table(source.read("quest/quest.kor.str"),"cp949")
    main_strings = table(main.read(QUEST_TABLE),"cp936")
    string_paths = list_rows(source.read("string.lst"))
    require({i:string_paths.get(i) for i in (8,11,12,13)} == {
        8:"npc/npc.chn.str",11:QUEST_TABLE,12:"skill/skill.chn.str",13:"stackable/stackable.chn.str"},
        "Chinese string category mapping differs")
    own_npc_names = table(source.read(string_paths[8]),"cp936")
    own_material_names = table(source.read(string_paths[13]),"cp936")
    require(own_npc_names.get("name_1450")=="阿尔伯特" and own_npc_names.get("name_1915")=="赛丽亚"
            and own_material_names.get("name_3213")=="燃烧恶魔的邀请函", "Chinese NPC/item identities differ")
    recover,skipped = {},[]
    for row in evidence["fields"]:
        reason = None
        if quests.get(row["quest_id"]) != row["script"]:
            reason = "Chapter Two quest ID/path differs"
        elif sha(canonical(source.read(row["script"]))) != row["scriptCanonicalSha256"]:
            reason = "Chapter Two quest script differs"
        elif own_strings.get(row["key"]) != row["base"]:
            reason = "Chapter Two Chinese value differs"
        elif own_korean.get(row["key"]) != row["native_korean"]:
            reason = "Chapter Two original Korean value differs"
        elif ("<11::"+row["key"]+">").encode() not in field(source.read(row["script"]),row["field"]):
            reason = "Chapter Two field does not reference this key"
        elif main_strings.get(row["key"]) != row["replacement"]:
            reason = "Main released text differs from portable evidence"
        if reason:
            skipped.append({"questId":row["quest_id"],"key":row["key"],"reason":reason})
        else:
            require(row["key"] not in recover or recover[row["key"]]==row["replacement"], "Conflicting recovery evidence")
            recover[row["key"]] = row["replacement"]
    # A key can be shared by several quests. Every known referencing field must pass.
    blocked_keys = {r["key"] for r in skipped}
    recover = {k:v for k,v in recover.items() if k not in blocked_keys}
    if recover:
        changes[QUEST_TABLE] = patch_string_table(source.read(QUEST_TABLE),recover)
    disabled,disabled_skipped = [],[]
    for row in evidence["disabled"]:
        ident,n = row["quest_id"],row["script"]
        if quests.get(ident)!=n or sha(canonical(source.read(n)))!=row["scriptCanonicalSha256"]:
            disabled_skipped.append({"questId":ident,"reason":"Chapter Two quest definition differs"})
            continue
        # Retired offers are supported by explicit historical original-language evidence.
        # Seasonal source absence is version specific and cannot be inferred from ACT1.
        if row["reason"] != "explicitly_retired":
            disabled_skipped.append({"questId":ident,"reason":"Seasonal material source absence not proven for Chapter Two"})
            continue
        original_fields = [r for r in evidence["fields"] if r["quest_id"]==ident and r["field"]=="condition message"]
        require(original_fields and all(own_korean.get(r["key"])==r["native_korean"] for r in original_fields),
                "Original-language retirement evidence differs")
        disabled.append(ident)
    require(DISABLED not in source.entries,"Existing disabled list needs review")
    changes[DISABLED] = ("// New offers only; original definitions and progress remain.\r\n"+"".join(str(n)+"\r\n" for n in sorted(disabled))).encode("ascii")
    report["quests"] = {"recoveredKeys":sorted(recover),"recoveredKeyCount":len(recover),"skippedFields":skipped,
                         "blockedSharedKeys":sorted(blocked_keys),"disabledIds":sorted(disabled),"disabledSkipped":disabled_skipped}
    for job,ident,name,before,after in SKILLS:
        path = "skill/"+job+"/"+name+".skl"
        mappings = list_rows(source.read("skill/"+job+"skill.lst"))
        mapped = mappings.get(ident,"")
        require(mapped in (name+".skl",job+"/"+name+".skl"), "Skill ID/path mismatch: "+path+" = "+mapped)
        raw = source.read(path)
        require(field(raw,"name")==field(main.read(path),"name"),"Skill name reference mismatch")
        refs = re.findall(rb"<12::([^>]+)>",field(raw,"name"))
        own_names = table(source.read("skill/skill.chn.str"),"cp936")
        main_names = table(main.read("skill/skill.chn.str"),"cp936")
        require(refs and all(own_names.get(k.decode()) == main_names.get(k.decode()) and own_names.get(k.decode()) for k in refs),"Skill Chinese name mismatch")
        changes[path] = patch_skill(raw,before,after)
        report["skills"].append({"job":job,"id":ident,"path":path,"name":own_names[refs[0].decode()],
                                 "ownRequiredLevel":field(raw,"required level").decode("ascii"),
                                 "ownPurchaseCost":field(raw,"purchase cost").decode("ascii"),"caps":after})
    npc = source.read("npc/albert.npc")
    require(list_rows(source.read("npc/npc.lst")).get(27)=="albert.npc", "Albert NPC ID mismatch")
    require(not any(re.search(rb"`\["+j+rb"\]`",clean(npc)) for j in [b"mage",b"priest"]), "Existing Albert lists need review")
    added = b"\r\n[skill]\r\n`[mage]`\r\n57\r\n21\r\n22\r\n23\r\n24\r\n65\r\n[/skill]\r\n[skill]\r\n`[priest]`\r\n47\r\n48\r\n30\r\n41\r\n[/skill]\r\n"
    require(npc.count(b"[name]")==1,"Ambiguous Albert name tag")
    changes["npc/albert.npc"] = npc.replace(b"[name]",added+b"[name]")
    for path in BOSSES:
        before = source.read(path)
        after = patch_invitation(before)
        if after!=before:
            changes[path] = after
        report["bosses"].append({"path":path,"already5000":after==before,"resultRows":[[3213,5000]]})
    require(list_rows(source.read("stackable/stackable.lst")).get(3213)=="material/invitation_burningdevil.stk", "Invitation ID/path mismatch")
    require(field(source.read("stackable/material/invitation_burningdevil.stk"),"name")==b"<13::name_3213>","Invitation name mismatch")
    materials,priced,unpriced = [],set(),set()
    for ident,name in list_rows(source.read("stackable/stackable.lst")).items():
        require(0<ident<=65535,"Unsupported stackable ID")
        path = name if name.startswith("stackable/") else "stackable/"+name
        raw = source.read(path)
        typ = field(raw,"stackable type").split()
        if not typ or typ[0] not in (b"`[material]`",b"`[material",b"[material]",b"[material"):
            continue
        # Parse the complete bracketed type, including the two-word expert-job form.
        m = re.match(rb"`?(\[[^]]+\])`?",field(raw,"stackable type"))
        if not m or m[1] not in (b"[material]",b"[material expert job]"):
            continue
        price = field(raw,"price")
        val = int(price) if re.fullmatch(rb"[+]?\d+",price) else None
        (priced if val and val>0 else unpriced).add(ident)
        name_ref = field(raw,"name").decode("cp936")
        name_key = re.fullmatch(r"<13::([^>]+)>",name_ref)
        display_name = own_material_names.get(name_key[1]) if name_key else name_ref.strip("`")
        require(display_name,"Unresolved material Chinese name: "+path)
        materials.append({"id":ident,"path":path,"price":val,"nameReference":name_ref,"name":display_name})
    require(len(priced)==253 and len(unpriced)==77 and len(shop_slots(source.read(SHOP)))==105, "Chapter Two material inventory changed")
    require(list_rows(source.read("itemshop/itemshop.lst")).get(15)=="equipmentshop7.shp" and field(source.read(SHOP),"NPC")==b"2", "Seria shop identity differs")
    changes[SHOP] = patch_shop(source.read(SHOP),priced,unpriced)
    report["materials"] = {"pricedAdded":len(priced-set(shop_slots(source.read(SHOP)))),"unpricedExcluded":len(unpriced),
                           "originalSlots":105,"resultSlots":len(shop_slots(changes[SHOP])),"inventory":materials}
    require(field(source.read("etc/pvp_ref.etc"),"pvp experience max grade")==b"30", "PvP maximum grade differs")
    changes["etc/channel_info.etc"] = patch_channels(source.read("etc/channel_info.etc"))
    changes["etc/pvpchannellist.etc"] = patch_ranks(source.read("etc/pvpchannellist.etc"))
    report["limitations"] = ["No in-game validation in this export step.","Seasonal disabled IDs without Chapter Two source-absence proof are preserved.",
                            "Quest fields with changed Chapter Two gameplay/original-language evidence are preserved.",
                            "Own Chapter Two skill levels and SP base costs are preserved; stealing applies the ported 200%/+4 rules."]
    return changes,report


def write_views(source, changes, output):
    require(not output.exists(),"Choose a new output directory")
    output.mkdir(parents=True)
    body = bytearray(source.body)
    records = list(source.records)
    for name in sorted(changes.keys()-source.entries.keys()):
        ident = max(r[0] for _,r in records)+1
        records.append((name,(ident,name.encode("cp949"),0,0,0)))
    rewritten = []
    for name,(ident,path,size,key,offset) in records:
        if name in changes:
            content = changes[name]
            padded = content+b"\0"*((-len(content))%4)
            key = zlib.crc32(padded,ident)
            offset,size = len(body),len(content)
            body.extend(rotate(padded,key,encrypt=True))
        rewritten.append((name,(ident,path,size,key,offset)))
    for native,label in [(True,"Script.chapter-two.native.pvf"),(False,"Script.chapter-two.server.pvf")]:
        tree = bytearray()
        for _,(ident,name,size,key,offset) in rewritten:
            if native:
                tree.extend(struct.pack("<II",ident,len(name))+name+struct.pack("<III",size,key,offset))
            else:
                tree.extend(struct.pack("<5I",ident,offset,size,key,len(name))+name)
        tree.extend(b"\0"*((-len(tree))%4))
        key = zlib.crc32(tree,len(rewritten))
        prefix = source.raw[:44] if native else b"DNF_SCRIPT_PACK"
        header = struct.pack("<3I",len(tree),key,len(rewritten))
        with (output/label).open("xb") as stream:
            stream.write(prefix+header+rotate(tree,key,encrypt=True))
            stream.write(body)
    return rewritten


def verify_views(source, changes, output):
    native = Archive(output/"Script.chapter-two.native.pvf")
    server = Archive(output/"Script.chapter-two.server.pvf")
    expected_names = source.entries.keys()|changes.keys()
    require(native.entries.keys()==server.entries.keys()==expected_names,"Output inventory differs")
    require(native.body==server.body,"Native/server encrypted bodies differ")
    require(native.duplicate_ids==source.duplicate_ids==server.duplicate_ids,"Original duplicate ID changed")
    require(native.prefix==source.prefix,"Native header identity/version changed")
    differences=[]
    for name in sorted(expected_names):
        expected = changes[name] if name in changes else source.read(name)
        a,b = native.read(name),server.read(name)
        require(a==b==expected,"Output bytes differ: "+name)
        if name in source.entries:
            require(native.entries[name][:2]==source.entries[name][:2],"Original file ID/path changed")
        if name not in source.entries or source.read(name)!=a:
            differences.append(name)
    require(set(differences)==changes.keys(),"Change allowlist differs")
    source.unchanged()
    return {"entriesChecked":len(expected_names),"changedEntries":differences,"sourceUnchanged":True,
            "nativeServerEveryEntryEqual":True,"encryptedBodySha256":sha(native.body),"sourceSha256":source.sha,
            "nativeSha256":native.sha,"serverSha256":server.sha,"originalDuplicateIdsPreserved":native.duplicate_ids}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--main-evidence-pvf",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args = parser.parse_args()
    source = Archive(args.source)
    # Released ACT1 uses PVFStudio encryption keys that are not file-ID-seeded
    # CRCs. Its entire immutable archive is pinned by MAIN_SHA instead.
    main_archive = Archive(args.main_evidence_pvf,validate_entry_crc=False)
    evidence = json.loads(Path(__file__).with_name("quest-recovery.json").read_text(encoding="utf-8"))
    changes,report = build_changes(source,evidence,main_archive)
    write_views(source,changes,args.output)
    verification = verify_views(source,changes,args.output)
    main_archive.unchanged()
    report.update(verification)
    report["mainEvidenceSha256"] = main_archive.sha
    report["inGameVerified"] = False
    report["toolSha256"] = sha(Path(__file__).read_bytes())
    report["questEvidenceSha256"] = sha(Path(__file__).with_name("quest-recovery.json").read_bytes())
    (args.output/"verification.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({k:report[k] for k in verification},indent=2))
    print("PASS all-entry CRC/content checks; source unchanged; native/server content equal")


if __name__ == "__main__":
    main()
