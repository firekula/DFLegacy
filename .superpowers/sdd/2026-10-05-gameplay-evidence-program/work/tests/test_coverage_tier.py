import unittest
from pathlib import Path

from coverage_tier import (SourceCounts, count_sources, load_dispatcher_families,
                           load_handler_rows, load_opcodes)
from coverage_tier import (TIER_ORDER, classify, load_dispatcher_families,
                           load_handler_rows, load_opcodes, summarize)

FIX = Path(__file__).resolve().parent / "fixtures"


class TestLoadAndReconcile(unittest.TestCase):
    def test_load_opcodes_has_exact_keys_and_types(self):
        rows = load_opcodes(FIX / "opcode-map.tsv")
        self.assertEqual(len(rows), 8)
        self.assertEqual(
            set(rows[0]),
            {"kind", "id", "name", "rodata_va", "dispatcher", "status"},
        )
        self.assertEqual(rows[0]["kind"], "CMD")
        self.assertEqual(rows[0]["id"], 1)          # int，不是 str
        self.assertEqual(rows[0]["name"], "LOGIN")
        self.assertEqual(rows[0]["dispatcher"], "Login")

    def test_blank_dispatcher_becomes_empty_string_not_none(self):
        rows = load_opcodes(FIX / "opcode-map.tsv")
        lonely = [r for r in rows if r["name"] == "LONELY_NO_DISPATCHER"]
        self.assertEqual(len(lonely), 1)
        self.assertEqual(lonely[0]["dispatcher"], "")

    def test_handler_rows_preserve_order_and_drop_comments(self):
        fams, skipped = load_handler_rows(FIX / "catalog_handlers_full.txt")
        self.assertEqual(skipped, 0)
        self.assertNotIn("#", fams)
        self.assertEqual(list(fams)[:2], ["Login", "SetIP"])
        self.assertEqual(len(fams["Login"]), 3)
        self.assertIn("PacketBuf::get_int", fams["Login"][0])   # 保序：第一行仍是第一行

    def test_counts_match_fixture(self):
        c = count_sources(
            FIX / "opcode-map.tsv",
            FIX / "catalog_dispatchers_full.txt",
            FIX / "catalog_handlers_full.txt",
        )
        self.assertIsInstance(c, SourceCounts)
        self.assertEqual(c.opcode_rows, 8)
        self.assertEqual(c.cmd, 7)
        self.assertEqual(c.noti, 1)
        self.assertEqual(c.mapped, 6)
        self.assertEqual(c.no_dispatcher, 2)
        self.assertEqual(c.unused, 0)
        self.assertEqual(c.dispatcher_families, 5)
        self.assertEqual(c.handler_families, 4)
        self.assertEqual(c.handler_rows, 7)      # Login 3 + SetIP 1 + Exit 1 + SelectCharac 2
        self.assertEqual(c.handler_skipped, 0)

    def test_counts_on_real_sources_match_documented_baseline(self):
        """真产物对账（**不**在夹具上做）：静默丢弃只有在这条断言上才抓得到。"""
        real = Path(
            r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work\out"
        )
        if not (real / "opcode-map.tsv").exists():
            self.skipTest("真产物不在本机（可选断言）")
        c = count_sources(real / "opcode-map.tsv",
                          real / "catalog_dispatchers_full.txt",
                          real / "catalog_handlers_full.txt")
        self.assertEqual(c.opcode_rows, 1171)
        self.assertEqual(c.cmd, 608)
        self.assertEqual(c.noti, 563)
        self.assertEqual(c.mapped, 428)
        self.assertEqual(c.no_dispatcher, 741)
        self.assertEqual(c.unused, 2)
        self.assertEqual(c.dispatcher_families, 425)
        self.assertEqual(c.handler_families, 239)
        self.assertEqual(c.handler_rows, 7273)
        self.assertEqual(c.handler_skipped, 0)


class TestClassify(unittest.TestCase):
    def setUp(self):
        self.rows = load_opcodes(FIX / "opcode-map.tsv")
        self.fams, _skipped = load_handler_rows(FIX / "catalog_handlers_full.txt")
        self.got = {r["name"]: r for r in classify(self.rows, self.fams)}

    def test_every_opcode_gets_exactly_one_tier_from_the_vocabulary(self):
        self.assertEqual(len(self.got), len(self.rows))
        for name, r in self.got.items():
            self.assertIn(r["tier"], TIER_ORDER, name)

    def test_login_is_read_plus_write(self):
        r = self.got["LOGIN"]
        self.assertEqual(r["tier"], "T4_fields-read+write")
        self.assertGreaterEqual(r["read_count"], 1)
        self.assertGreaterEqual(r["write_count"], 1)

    def test_setip_is_read_only(self):
        self.assertEqual(self.got["SET_UDP_IP_PORT"]["tier"], "T3_fields-read-only")
        self.assertEqual(self.got["SET_UDP_IP_PORT"]["write_count"], 0)

    def test_exit_is_write_only(self):
        self.assertEqual(self.got["EXIT"]["tier"], "T3b_fields-write-only")
        self.assertEqual(self.got["EXIT"]["read_count"], 0)

    def test_put_header_alone_is_not_a_field_write(self):
        # 判据：`source=put_header` 是"构造帧头"，**不是**字段写入；字段写只认
        # `name=...::put_`（WRITE 正则）。夹具里 SelectCharac 只有 get_str 读 +
        # 一条无 `name=` 的 put_header ⇒ 落 T3，不是 T4。
        self.assertEqual(self.got["SELECT_CHARACTER"]["tier"], "T3_fields-read-only")
        self.assertEqual(self.got["SELECT_CHARACTER"]["write_count"], 0)

    def test_lonely_no_handler_is_t1(self):
        self.assertEqual(self.got["LONELY_NO_HANDLER"]["tier"],
                         "T1_dispatcher-no-handlerdata")

    def test_lonely_no_dispatcher_is_t0(self):
        self.assertEqual(self.got["LONELY_NO_DISPATCHER"]["tier"], "T0_no-evidence")

    def test_noti_with_blank_dispatcher_is_t0(self):
        self.assertEqual(self.got["DONT_CARE"]["tier"], "T0_no-evidence")

    def test_unused_with_blank_dispatcher_is_t0(self):
        self.assertEqual(self.got["UNUSED_ONE"]["tier"], "T0_no-evidence")

    def test_summary_total_equals_row_count(self):
        s = summarize(list(self.got.values()))
        self.assertEqual(s["total"], len(self.rows))
        self.assertEqual(sum(v for k, v in s.items() if k != "total"), len(self.rows))


if __name__ == "__main__":
    unittest.main()
