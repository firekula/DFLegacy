"""Literal gameplay fixtures contain no login/authentication data."""
import unittest

from tools.acceptance.act1_gameplay_evidence import (
    decode_application, decode_mt_blocks, parse_gp4_identity, parse_gp6_identity,
)

# Original fallback-v3 B TX, monster3 in room1,2. Expected plaintext is pinned
# separately; this fixture is not produced by an encoder under test.
GP4 = bytes.fromhex('010400ec9626aea32aaa2ba3ab2a2bab2a2b1eff0b4cc0b80917631b8a2b2fa5c9')
GP4_PLAIN = bytes.fromhex('11020300110102000102006aa940ced727447890604300081dc5')
# Synthetic plaintext GP6: object kind529, objectUID3, selected playerUID2.
GP6 = bytes.fromhex('02060091ee0b23110203000200')
UNRELIABLE = bytes.fromhex('020700000021000100') + GP4
RELIABLE = bytes.fromhex('0109000000320000010d00') + GP6 + bytes.fromhex('2100') + GP4


class ApplicationTests(unittest.TestCase):
    def test_original_gp4_cipher_and_identity(self):
        message = decode_application(GP4)
        self.assertEqual((message.mode, message.protocol, message.codec), (1, 4, 'rol1-xor56'))
        self.assertTrue(message.crc_valid)
        self.assertEqual(message.payload, GP4_PLAIN)
        identity = parse_gp4_identity(message.payload)
        self.assertEqual((identity.object_kind, identity.object_uid), (529, 3))
        self.assertEqual((identity.player_kind, identity.player_uid, identity.room), (273, 2, (1, 2)))
        self.assertEqual(identity.movement_flags, 0x40A96A00)

    def test_plaintext_gp6_exact_body(self):
        message = decode_application(GP6)
        self.assertEqual((message.protocol, message.codec, message.payload), (6, 'plain', bytes.fromhex('110203000200')))
        self.assertEqual(parse_gp6_identity(message.payload), (529, 3, 2))
        for body in [message.payload[:-1], message.payload+b'\0']:
            with self.assertRaises(ValueError): parse_gp6_identity(body)

    def test_empty_crc_is_one_decoded_value(self):
        message = decode_application(bytes.fromhex('01010000000000'))
        self.assertEqual((message.payload, message.codec, message.crc_valid), (b'', 'plain', True))

    def test_managed_server_cipher_literal(self):
        message = decode_application(bytes.fromhex('02060091ee0b234185c5058505'))
        self.assertEqual((message.codec, message.payload), ('rol2-xor14', bytes.fromhex('110203000200')))

    def test_distinct_crc_collision_is_not_a_cipher_choice(self):
        # Both 3e43102921000000 and 2ad0760414565656 have CRC 570f7677;
        # the two supported representations produce distinct plaintexts.
        with self.assertRaises(ValueError):
            decode_application(bytes.fromhex('01040077760f573e43102921000000'))

    def test_native_gp42_outer_crc_does_not_decode_the_plugin_body(self):
        raw = bytes.fromhex('002a0084c3c476002900704143c300002900704143c301ffff01')
        message = decode_application(raw)
        self.assertTrue(message.crc_valid)
        self.assertTrue(message.wrapper_opaque)
        self.assertEqual((message.protocol, message.codec, message.payload), (42, 'plain', raw[7:]))
        self.assertFalse(decode_application(GP6).wrapper_opaque)

    def test_unknown_or_corrupt_cipher_stays_opaque(self):
        raw = bytes.fromhex('02060000000000110203000200')
        message = decode_application(raw)
        self.assertEqual((message.protocol, message.payload, message.codec, message.crc_valid), (6, None, 'opaque', False))
        with self.assertRaises(ValueError): parse_gp4_identity(None)

    def test_application_bounds_and_gp4_header(self):
        for raw in [b'', GP4[:6], b'x'*65536]:
            with self.assertRaises(ValueError): decode_application(raw)
        with self.assertRaises(ValueError): parse_gp4_identity(GP4_PLAIN[:13])

    def test_memoryview_limit_counts_bytes_before_conversion(self):
        buffer = bytes(65536)
        views = [memoryview(buffer).cast('I'), memoryview(buffer).cast('B', shape=[256, 256])]
        for view in views:
            for decoder in [decode_application, decode_mt_blocks]:
                with self.subTest(format=view.format, shape=view.shape, decoder=decoder.__name__):
                    with self.assertRaisesRegex(ValueError, 'bounded'):
                        decoder(view)


class MtBoundaryTests(unittest.TestCase):
    def test_literal_unreliable_record(self):
        records = decode_mt_blocks(UNRELIABLE)
        self.assertEqual(len(records), 1)
        record = records[0]
        self.assertEqual((record.block_type, record.sequence, record.sender_slot, record.flag, record.record_index), (2, 7, 1, 0, 0))
        self.assertEqual(record.application.payload, GP4_PLAIN)

    def test_multiple_reliable_applications_have_length_prefixes(self):
        records = decode_mt_blocks(RELIABLE)
        self.assertEqual([r.application.protocol for r in records], [6, 4])
        self.assertEqual([r.record_index for r in records], [0, 1])
        self.assertEqual([r.sequence for r in records], [9, 9])
        self.assertEqual([r.sender_slot for r in records], [0, 0])

    def test_ack_and_following_block_consumption(self):
        # Native ACK uses 8 + (bitCount >> 3), not a guessed ceil formula.
        ack = bytes.fromhex('0000010000001019aabb')
        records = decode_mt_blocks(ack+UNRELIABLE)
        self.assertEqual((records[0].block_type, records[0].application), (0, None))
        self.assertEqual(records[1].application.payload, GP4_PLAIN)
        odd = bytes.fromhex('0000010000000f19aa')
        self.assertEqual(len(decode_mt_blocks(odd+UNRELIABLE)), 2)

    def test_keepalive_has_no_application(self):
        records = decode_mt_blocks(bytes.fromhex('010700000000000100'))
        self.assertEqual((len(records), records[0].application), (1, None))
        self.assertEqual(decode_mt_blocks(b''), [])

    def test_truncation_and_bad_prefix_never_resynchronize(self):
        for raw in [UNRELIABLE[:-1], RELIABLE[:-1], b'\xff'+UNRELIABLE, b'\0'*7,
                    bytes.fromhex('01090000000f0000010e00')+GP6,
                    bytes.fromhex('0109000000020000010000')]:
            with self.subTest(raw=raw.hex()[:24]), self.assertRaises(ValueError): decode_mt_blocks(raw)

    def test_opaque_record_does_not_hide_the_next_record(self):
        bad = bytes.fromhex('02060000000000110203000200')
        data = bytes.fromhex('01090000001e0000010d00')+bad+bytes.fromhex('0d00')+GP6
        records = decode_mt_blocks(data)
        self.assertEqual([r.application.crc_valid for r in records], [False, True])

    def test_sender_and_total_size_bounds(self):
        bad = bytearray(UNRELIABLE); bad[7] = 8
        for raw in [bad, b'\0'*65536]:
            with self.assertRaises(ValueError): decode_mt_blocks(raw)


if __name__ == '__main__': unittest.main()
