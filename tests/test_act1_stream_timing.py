"""Literal relay/gameplay bytes; timing is socket-log provenance, never dispatch."""
import unittest

from tools.acceptance.act1_stream_timing import TimedRelayStream

# 54-byte native relay envelope with one original GP4 application in MT.
FRAME = bytes.fromhex('01003600290000002a000000020700000021000100'
    '010400ec9626aea32aaa2ba3ab2a2bab2a2b1eff0b4cc0b80917631b8a2b2fa5c9')
RECEIVED = bytes.fromhex('0300') + FRAME[2:]


class StreamTimingTests(unittest.TestCase):
    def test_split_header_and_body_preserve_first_and_last_contributors(self):
        stream = TimedRelayStream()
        self.assertEqual(stream.feed(FRAME[:3], timestamp_us=100, line=10), [])
        self.assertEqual(stream.feed(FRAME[3:15], timestamp_us=150, line=20), [])
        observation, = stream.feed(FRAME[15:], timestamp_us=200, line=21)
        self.assertEqual((observation.socket_first_us, observation.socket_last_us), (100, 200))
        self.assertEqual((observation.first_line, observation.last_line), (10, 21))
        self.assertEqual((observation.offset_start, observation.offset_end), (0, 54))
        self.assertEqual((observation.frame.sender, observation.frame.recipient), (41, 42))
        stream.finish()

    def test_coalesced_frame_starts_in_its_own_chunk_not_previous_frame(self):
        stream = TimedRelayStream()
        stream.feed(FRAME[:10], timestamp_us=100, line=1)
        one, two = stream.feed(FRAME[10:] + FRAME + FRAME[:5], timestamp_us=200, line=2)
        three, = stream.feed(FRAME[5:], timestamp_us=300, line=3)
        self.assertEqual([(r.socket_first_us, r.socket_last_us) for r in [one, two, three]],
                         [(100, 200), (200, 200), (200, 300)])
        self.assertEqual([(r.first_line, r.last_line) for r in [one, two, three]], [(1, 2), (2, 2), (2, 3)])
        self.assertEqual([(r.offset_start, r.offset_end) for r in [one, two, three]], [(0, 54), (54, 108), (108, 162)])
        stream.finish()

    def test_duplicate_frames_remain_separate_observations(self):
        sent = TimedRelayStream()
        one, two = sent.feed(FRAME * 2, timestamp_us=100, line=1)
        received, = TimedRelayStream().feed(RECEIVED, timestamp_us=100, line=1)
        self.assertEqual(one.frame.fingerprint, two.frame.fingerprint)
        self.assertEqual(one.frame.fingerprint, received.frame.fingerprint)
        self.assertNotEqual(one.wire_sha256, received.wire_sha256)
        self.assertEqual((one.offset_start, two.offset_start), (0, 54))
        sent.finish()

    def test_empty_chunks_contribute_no_bytes_or_completion_time(self):
        stream = TimedRelayStream()
        stream.feed(FRAME[:5], timestamp_us=100, line=1)
        self.assertEqual(stream.feed(b'', timestamp_us=200, line=2), [])
        frame, = stream.feed(FRAME[5:], timestamp_us=300, line=3)
        self.assertEqual(stream.feed(b'', timestamp_us=400, line=4), [])
        self.assertEqual((frame.socket_first_us, frame.socket_last_us, frame.first_line, frame.last_line), (100, 300, 1, 3))
        stream.finish()

    def test_malformed_stream_is_terminal_without_resynchronization(self):
        stream = TimedRelayStream()
        with self.assertRaises(ValueError):
            stream.feed(b'bad!' + FRAME, timestamp_us=100, line=1)
        with self.assertRaisesRegex(ValueError, 'failed'):
            stream.feed(FRAME, timestamp_us=200, line=2)
        with self.assertRaisesRegex(ValueError, 'failed'): stream.finish()

    def test_unfinished_capture_is_terminal(self):
        stream = TimedRelayStream()
        stream.feed(FRAME[:3], timestamp_us=100, line=1)
        with self.assertRaisesRegex(ValueError, 'Incomplete'): stream.finish()
        with self.assertRaisesRegex(ValueError, 'failed'):
            stream.feed(FRAME[3:], timestamp_us=200, line=2)

    def test_metadata_is_integral_nonnegative_and_ordered(self):
        for timestamp, line in [(-1, 1), (True, 1), (100, 0), (100, True), (1.5, 1), (100, '1')]:
            with self.subTest(timestamp=timestamp, line=line), self.assertRaises(ValueError):
                TimedRelayStream().feed(FRAME, timestamp_us=timestamp, line=line)
        for timestamp, line in [(99, 2), (100, 1)]:
            stream = TimedRelayStream()
            stream.feed(FRAME[:3], timestamp_us=100, line=1)
            with self.subTest(timestamp=timestamp, line=line), self.assertRaises(ValueError):
                stream.feed(FRAME[3:], timestamp_us=timestamp, line=line)

    def test_chunk_byte_bounds_and_types(self):
        buffer = bytes(65536)
        for raw in [buffer, memoryview(buffer).cast('I'), memoryview(buffer).cast('B', shape=[256, 256]), None, 'bytes']:
            with self.subTest(type=type(raw).__name__), self.assertRaises(ValueError):
                TimedRelayStream().feed(raw, timestamp_us=100, line=1)

    def test_empty_capture_finishes_without_observations(self):
        TimedRelayStream().finish()


if __name__ == '__main__': unittest.main()
