"""Retain matched optimized native symbols privately; never part of public assets.
PE layout: https://learn.microsoft.com/windows/win32/debug/pe-format
PDB stream 1: https://github.com/microsoft/microsoft-pdb/blob/master/PDB/dbi/pdb1.h
"""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import struct
import uuid


def _u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def pe_identity(data):
    if data[:2] != b"MZ":
        raise ValueError("Not a PE image")
    pe = _u32(data, 60)
    if data[pe:pe+4] != b"PE\0\0":
        raise ValueError("Invalid PE signature")
    sections = struct.unpack_from("<H", data, pe+6)[0]
    optional_size = struct.unpack_from("<H", data, pe+20)[0]
    optional = pe+24
    magic = struct.unpack_from("<H", data, optional)[0]
    if magic not in (0x10b, 0x20b):
        raise ValueError("Unsupported PE optional header")
    debug_rva, debug_size = struct.unpack_from("<II", data, optional+(96 if magic == 0x10b else 112)+48)
    table = optional+optional_size
    directory = None
    for index in range(sections):
        virtual_size, rva, raw_size, raw = struct.unpack_from("<IIII", data, table+index*40+8)
        if rva <= debug_rva < rva+min(virtual_size, raw_size):
            directory = raw+debug_rva-rva
    if directory is None or debug_size > 4096 or debug_size % 28:
        raise ValueError("Missing or invalid debug directory")
    for offset in range(directory, directory+debug_size, 28):
        kind, size, _, pointer = struct.unpack_from("<IIII", data, offset+12)
        if kind != 2:
            continue
        record = data[pointer:pointer+size]
        if record[:4] != b"RSDS" or len(record) < 25:
            raise ValueError("Missing RSDS symbols")
        name = record[24:].split(b"\0", 1)[0].decode("utf-8")
        if "/" in name or "\\" in name or ":" in name or not name.endswith(".pdb"):
            raise ValueError("Public image leaks a non-leaf PDB path")
        return str(uuid.UUID(bytes_le=record[4:20])), _u32(record, 20), name
    raise ValueError("Image has no native CodeView entry")


def pdb_identity(data):
    if data[:32] != b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0":
        raise ValueError("Only MSF 7 native PDB is supported")
    block, _, pages, size, _, block_map = struct.unpack_from("<IIIIII", data, 32)
    if block not in (512, 1024, 2048, 4096) or pages*block != len(data) or size > 16*1024*1024:
        raise ValueError("Invalid bounded MSF directory")
    blocks = (size+block-1)//block
    page_ids = struct.unpack_from("<"+"I"*blocks, data, block_map*block)
    if any(page >= pages for page in page_ids):
        raise ValueError("Invalid MSF page")
    directory = b"".join(data[page*block:(page+1)*block] for page in page_ids)[:size]
    streams = _u32(directory, 0)
    if streams < 2 or streams > 65536:
        raise ValueError("Invalid PDB streams")
    sizes = struct.unpack_from("<"+"I"*streams, directory, 4)
    cursor = 4+4*streams
    for index, length in enumerate(sizes):
        count = 0 if length == 0xffffffff else (length+block-1)//block
        ids = struct.unpack_from("<"+"I"*count, directory, cursor)
        cursor += count*4
        if any(page >= pages for page in ids):
            raise ValueError("Invalid PDB stream page")
        if index == 1:
            stream = b"".join(data[page*block:(page+1)*block] for page in ids)[:length]
            if len(stream) < 28:
                raise ValueError("PDB identity stream is missing")
            return str(uuid.UUID(bytes_le=stream[12:28])), _u32(stream, 8)
    raise ValueError("PDB identity not found")


def retain(pairs, private_dir, source_sha):
    if not re.fullmatch("[0-9a-f]{40}", source_sha):
        raise ValueError("Full source SHA is required")
    records = []
    names = set()
    for image, symbols in pairs:
        image, symbols = pathlib.Path(image), pathlib.Path(symbols)
        for path in (image, symbols):
            if path.name.lower() in names or path.stat().st_size > 512*1024*1024:
                raise ValueError("Duplicate name or oversized artifact")
            names.add(path.name.lower())
        image_bytes, symbol_bytes = image.read_bytes(), symbols.read_bytes()
        guid, age, embedded_name = pe_identity(image_bytes)
        if (guid, age) != pdb_identity(symbol_bytes) or embedded_name.lower() != symbols.name.lower():
            raise ValueError("PE/PDB GUID+Age or filename mismatch")
        records.append({"image":image.name,"pdb":symbols.name,"guid":guid,"age":age,
                        "imageSha256":hashlib.sha256(image_bytes).hexdigest(),"pdbSha256":hashlib.sha256(symbol_bytes).hexdigest()})
    if not records:
        raise ValueError("At least one production pair is required")
    private_dir = pathlib.Path(private_dir)
    private_dir.mkdir(parents=True, exist_ok=False)
    for image, symbols in pairs:
        for path in (image, symbols):
            shutil.copyfile(path, private_dir/pathlib.Path(path).name)
    # Reverify the retained bytes, guarding changes between inspection and copy.
    for item in records:
        for name, digest in ((item["image"],item["imageSha256"]),(item["pdb"],item["pdbSha256"])):
            if hashlib.sha256((private_dir/name).read_bytes()).hexdigest() != digest:
                raise ValueError("Artifact changed during private retention")
    result = {"format":"DFLegacy-native-symbol-provenance-1","sourceSha":source_sha,"modules":records}
    (private_dir/"provenance.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pair", nargs=2, action="append", required=True, metavar=("PE", "PDB"))
    parser.add_argument("--private-dir", required=True)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    result = retain(args.pair, args.private_dir, args.source_sha)
    print("Verified and privately retained", len(result["modules"]), "matched PE/PDB pairs for", args.source_sha)

if __name__ == "__main__":
    main()
