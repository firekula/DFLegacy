"""Generate the Chapter Two client resource changes and matching server PVF."""

import argparse
import json
from pathlib import Path
import re
import struct

from port import Archive, field, list_rows, require, sha, verify_views, write_views


DUNGEON = 'dungeon/act1/lorien.dgn'
FIXED_IMAGE = 'sprite/interface/selectdungeonevent.img'
EXPECTED_OVERLAY = '72074a99d04211d383238671feb7a2a2e3cc280aca17206388034001124ce344'
PORTRAITS = {'characterillust.img', 'npc_dialog_face.img', 'comiccutswordman.img'}
TITLE_DIRECTORIES = {'title', 'titleeffect', 'title_effect'}


def add_third_maze(first, second):
    marker = rb'(?mi)^\[maze info\][ \t]*\r?$'
    mazes = list(re.finditer(marker, first))
    require(len(mazes) == 3 and len(re.findall(marker, second)) == 2, 'Unexpected Lorien maze count')
    separator = b'\r\n' if b'\r\n' in second else b'\n'
    return second + separator + first[mazes[2].start():]


def verify_lorien_dependencies(first, second, maze):
    ordinary = re.findall(rb'(?mi)^\[map specification\]\s+(\d+)\s+(\d+)\s+(\d+)\s+', maze)
    boss = re.findall(rb'(?mi)^\[boss map specification\]\s+(\d+)\s+(\d+)\s+(\d+)\s+', maze)
    require(ordinary == [(b'0', b'0', b'1'), (b'1', b'0', b'2'), (b'1', b'1', b'3'),
                         (b'1', b'2', b'4'), (b'2', b'2', b'5')]
            and boss == [(b'2', b'2', b'6416')], 'Unexpected Lorien room specifications')
    old_maps, new_maps = (list_rows(archive.read('map/map.lst')) for archive in (first, second))
    for identifier in (1, 2, 3, 4, 5, 6416):
        require(old_maps[identifier] == new_maps[identifier], f'Chapter Two map {identifier} differs in identity')
        second.read('map/' + new_maps[identifier])
    boss_file = second.read('map/' + new_maps[6416])
    require(re.search(rb'(?m)^1077\s+', field(boss_file, 'monster')) is not None, 'Chapter Two map 6416 must contain Piper')


def image_index(data):
    require(len(data) >= 20 and data[:16] == b'NeoplePack_Bill\0', 'Invalid NPK header')
    count, = struct.unpack_from('<I', data, 16)
    end = 20 + 264 * count
    require(end <= len(data), 'Truncated NPK index')
    rows = {}
    for index, (offset, size, raw) in enumerate(struct.iter_unpack('<II256s', memoryview(data)[20:end])):
        if size in (0, 0xffffffff):
            continue
        require(end <= offset and offset + size <= len(data), 'NPK image range is invalid')
        name = raw.split(b'\0', 1)[0].decode('cp949').replace('\\', '/').lower()
        require(name not in rows, f'Duplicate NPK image name: {name}')
        rows[name] = (20 + 264 * index, memoryview(data)[offset:offset + size])
    return rows


def use_variant(name):
    *folders, filename = name.split('/')
    if filename in PORTRAITS or 'illust' in filename:
        return False
    if TITLE_DIRECTORIES.intersection(folders):
        return True
    if 'effect' in folders or 'skill' in folders or filename.startswith('ef_'):
        return False
    return folders[0] == 'sprite'


def image_header(data):
    require(len(data) >= 32 and bytes(data[:9]) == b'Neople Im', 'Invalid image record')
    return struct.unpack_from('<II', data, 24)


def rewrite_npk(data, fixes=None):
    entries = image_index(data)
    replacements = dict(fixes or {})
    selected, incomplete = [], []
    for variant, (_, content) in entries.items():
        dirname, _, filename = variant.rpartition('/')
        if not filename.startswith('(tn)'):
            continue
        base = dirname + '/' + filename[4:]
        if base not in entries or not use_variant(base) or base in replacements:
            continue
        format_id, count = image_header(content)
        base_format, base_count = image_header(entries[base][1])
        if format_id != base_format or count < base_count:
            incomplete.append(base)
        elif entries[base][1] != content:
            replacements[base] = content
            selected.append(base)
    if not replacements:
        return data, selected, incomplete
    result = bytearray(data)
    for name, content in replacements.items():
        directory_offset, _ = entries[name]
        struct.pack_into('<II', result, directory_offset, len(result), len(content))
        result.extend(content)
    expected = image_index(result)
    require(expected.keys() == entries.keys(), 'NPK image index changed')
    for name, (_, original) in entries.items():
        require(expected[name][1] == replacements.get(name, original), f'Unexpected NPK change: {name}')
    return result, selected, incomplete


def export(first_path, client, overlay, output):
    first = Archive(first_path, validate_entry_crc=False)
    second = Archive(client / 'Script.pvf')
    require(first.sha == '288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f',
            'Unexpected Chapter One evidence')
    require(second.sha == 'd0b4ac799896398bfd97a5cfcf12e8be0a603a44a965fd5478ab8c43a99b8c9b',
            'Expected the Chapter Two v1.0.6 resource baseline')
    overlay_data = overlay.read_bytes()
    require(sha(overlay_data) == EXPECTED_OVERLAY, 'Unexpected local UI repair')
    original_dungeon = second.read(DUNGEON)
    dungeon = add_third_maze(first.read(DUNGEON), original_dungeon)
    verify_lorien_dependencies(first, second, dungeon[len(original_dungeon):])
    changes = {DUNGEON: dungeon}
    write_views(second, changes, output)
    report = {'pvf': verify_views(second, changes, output), 'npk': {}, 'unmatchedFrames': []}
    images = output / 'ImagePacks2'
    images.mkdir()
    for path in sorted((client / 'ImagePacks2').glob('*.npk')):
        data = path.read_bytes()
        fixes = None
        if path.name.lower() == 'sprite_interface.npk':
            target = image_index(overlay_data)[FIXED_IMAGE][1]
            previous = image_index(data)[FIXED_IMAGE][1]
            require(image_header(target) == image_header(previous), 'UI repair changed frame count or format')
            require(previous != target, 'UI repair has already been applied')
            fixes = {FIXED_IMAGE: target}
        edited, selected, incomplete = rewrite_npk(data, fixes)
        report['unmatchedFrames'].extend(incomplete)
        if edited is not data:
            (images / path.name).write_bytes(edited)
            report['npk'][path.name] = {'sourceSha256': sha(data), 'sha256': sha(edited),
                                      'tnImages': selected, 'fixedImages': list(fixes or ())}
    require(any(row['fixedImages'] for row in report['npk'].values()), 'UI repair was not applied')
    (output / 'resources.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ('first-pvf', 'chapter-two-client', 'ui-repair', 'output'):
        parser.add_argument('--' + arg, type=Path, required=True)
    args = parser.parse_args()
    result = export(args.first_pvf, args.chapter_two_client, args.ui_repair, args.output)
    print(f"Verified {result['pvf']['entriesChecked']} PVF entries and {len(result['npk'])} changed NPK archives")
