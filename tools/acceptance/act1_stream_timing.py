"""Offline socket-hook log bounds; they never prove native dispatch or latency."""
from dataclasses import dataclass
from collections import deque
import hashlib
import struct

from .act1_gameplay_evidence import _bounded_bytes
from .party_fallback_evidence import RelayFrame, RelayStream


@dataclass(frozen=True)
class SocketFrameBounds:
    frame: RelayFrame
    socket_first_us: int
    socket_last_us: int
    first_line: int
    last_line: int
    offset_start: int
    offset_end: int

    @property
    def wire_sha256(self):
        frame = self.frame
        raw = struct.pack('<HHII', frame.opcode, len(frame.payload) + 12, frame.sender, frame.recipient) + frame.payload
        return hashlib.sha256(raw).hexdigest()


class TimedRelayStream:
    def __init__(self):
        self._stream = RelayStream()
        self._last_us = -1
        self._last_line = 0
        self._offset = 0
        self._failed = False
        self._segments = deque()

    def feed(self, raw, *, timestamp_us, line):
        if self._failed: raise ValueError('Relay timing stream previously failed')
        try:
            raw = _bounded_bytes(raw)
            if type(timestamp_us) is not int or timestamp_us < 0 or type(line) is not int or line <= self._last_line:
                raise ValueError('Invalid socket-log timestamp/line metadata')
            if timestamp_us < self._last_us: raise ValueError('Socket-log timestamp regressed')
            self._last_us, self._last_line = timestamp_us, line
            if not raw: return []
            self._segments.append([len(raw), timestamp_us, line])
            result = []
            for frame in self._stream.feed(raw):
                remaining = 12 + len(frame.payload)
                end = self._offset + remaining
                first_us, first_line = self._segments[0][1:]
                while remaining:
                    segment = self._segments[0]
                    count = min(remaining, segment[0])
                    last_us, last_line = segment[1:]
                    remaining -= count
                    segment[0] -= count
                    if not segment[0]: self._segments.popleft()
                result.append(SocketFrameBounds(frame, first_us, last_us, first_line, last_line, self._offset, end))
                self._offset = end
            return result
        except ValueError:
            self._failed = True
            raise

    def finish(self):
        if self._failed: raise ValueError('Relay timing stream previously failed')
        try:
            self._stream.finish()
            if self._segments: raise ValueError('Incomplete socket-log byte provenance')
        except ValueError:
            self._failed = True
            raise
