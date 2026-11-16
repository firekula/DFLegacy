import struct
import unittest

import client_resources as resources


def image(marker, count=1):
    return b'Neople Image File'.ljust(24, b'\0') + struct.pack('<II', 1, count) + bytes([marker])


def pack(entries):
    offset = 20 + 264 * len(entries)
    directory = bytearray()
    body = bytearray()
    for name, data in entries:
        directory.extend(struct.pack('<II256s', offset + len(body), len(data), name.encode()))
        body.extend(data)
    return b'NeoplePack_Bill\0' + struct.pack('<I', len(entries)) + directory + body


class ChapterTwoResourcesTest(unittest.TestCase):
    def test_only_third_maze_is_copied(self):
        first = b'[maze info]\r\n1\r\n[maze info]\r\n2\r\n[maze info]\r\n3\r\n'
        second = b'[maze info]\r\n1\r\n[maze info]\r\n2\r\n[other]\r\nkeep\r\n'
        self.assertEqual(resources.add_third_maze(first, second), second + b'\r\n[maze info]\r\n3\r\n')

    def test_tn_selection_allows_only_ui_and_titles(self):
        cases = [
            ('sprite/interface/monstercard/', 'cards.img', True),
            ('sprite/interface/cashshop/', 'monsteravatar.img', True),
            ('sprite/character/common/effect/title/', 'ef_title.img', True),
            ('sprite/item/title/', 'badge.img', True),
            ('sprite/character/swordman/effect/frenzy/', 'sword.img', False),
            ('sprite/interface/', 'npc_dialog_face.img', False),
            ('sprite/interface/tutorial02/', 'comiccutswordman.img', False),
            ('sprite/character/priest/', 'body.img', False),
            ('sprite/character/gunner/equipment/avatar/', 'hat.img', False),
            ('sprite/character/swordman/equipment/weapon/', 'sword.img', False),
            ('sprite/monster/darkelf/', 'idle.img', False),
            ('sprite/monster/title/', 'arbitrary.img', False),
            ('sprite/creature/rabbit/', 'move.img', False),
            ('sprite/map/cutscene/', 'intro.img', False),
            ('sprite/map/title/', 'region.img', False),
            ('sprite/object/', 'item.img', False),
        ]
        entries = [(path + prefix + name, image(marker))
                   for path, name, _ in cases for prefix, marker in [('', 1), ('(tn)', 2)]]
        result, selected, _ = resources.rewrite_npk(pack(entries))
        index = resources.image_index(result)
        self.assertEqual(len(selected), sum(enabled for _, _, enabled in cases))
        for path, name, enabled in cases:
            self.assertEqual(index[path + name][1][-1], 2 if enabled else 1)
            self.assertEqual(index[path + '(tn)' + name][1][-1], 2)

    def test_shorter_variant_cannot_replace_a_referenced_frame(self):
        result, selected, skipped = resources.rewrite_npk(pack([
            ('sprite/interface/control.img', image(1, 2)),
            ('sprite/interface/(tn)control.img', image(2)),
        ]))
        self.assertEqual(resources.image_index(result)['sprite/interface/control.img'][1], image(1, 2))
        self.assertFalse(selected)
        self.assertEqual(skipped, ['sprite/interface/control.img'])

    def test_overlay_only_replaces_the_fixed_image(self):
        target = resources.FIXED_IMAGE
        original = pack([(target, image(1)), ('sprite/interface/other.img', image(3))])
        result, _, _ = resources.rewrite_npk(original, {target: image(2)})
        self.assertEqual(resources.image_index(result)[target][1], image(2))
        self.assertEqual(resources.image_index(result)['sprite/interface/other.img'][1], image(3))


if __name__ == '__main__':
    unittest.main()
