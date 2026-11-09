import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("chapter_two_port", Path(__file__).with_name("port.py"))
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)


class SemanticPortTests(unittest.TestCase):
    def test_shop_preserves_own_slots_and_excludes_unpriced(self):
        before = b"[NPC] 2\r\n[sell item]\r\n7089 -1 -1 -1 -1 -1 -1\r\n[/sell item]\r\n"
        after = port.patch_shop(before, {3001, 3002}, {3003})
        self.assertEqual(port.shop_slots(after)[:7], [7089, -1, -1, -1, -1, -1, -1])
        self.assertEqual(port.shop_slots(after)[7:], [3001, 3002, -1, -1, -1, -1, -1])
        self.assertNotIn(3003, port.shop_slots(after))

    def test_invitation_updates_existing_probability_without_duplicate(self):
        before = b"[item]\r\n1002 100\r\n3213 7000 // existing own drop\r\n[/item]\r\n"
        after = port.patch_invitation(before)
        self.assertIn(b"3213 5000 // existing own drop", after)
        self.assertEqual(after.count(b"3213"), 1)
        self.assertIn(b"1002 100", after)

    def test_invitation_5000_remains_byte_identical(self):
        before = b"[item]\n3213 5000\n[/item]\n"
        self.assertEqual(port.patch_invitation(before), before)

    def test_steal_preserves_own_skill_costs(self):
        before = b"[growtype maximum level] 0 20 0 0 0\r\n[purchase cost]\r\n20\r\n[required level] 18\r\n"
        after = port.patch_skill(before, [0, 20, 0, 0, 0], [0, 20, 10, 10, 0])
        self.assertIn(b"[growtype maximum level] 0 20 10 10 0", after)
        self.assertIn(b"[purchase cost]\r\n20", after)
        self.assertIn(b"[required level] 18", after)
        self.assertIn(b"[steel learning skill]\r\n1\r\n50\r\n200\r\n4", after)

    def test_steal_refuses_unknown_caps(self):
        with self.assertRaises(ValueError):
            port.patch_skill(b"[growtype maximum level] 0 30 0 0 0\n", [0, 20, 0, 0, 0], [0, 20, 10, 10, 0])

    def test_recovery_changes_only_expected_table_value(self):
        original = "// comment\r\nname_1>不使用\r\nname_2>保留\r\n".encode("cp936")
        after = port.patch_string_table(original, {"name_1": "真实原文"})
        self.assertEqual(after, "// comment\r\nname_1>真实原文\r\nname_2>保留\r\n".encode("cp936"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
