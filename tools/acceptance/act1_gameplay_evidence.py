"""Bounded offline ACT1 gameplay records; no network or client-state operations.

CRC validity identifies a supported representation, not native acceptance. A GP42
body remains an opaque plugin wrapper even when its outer CRC can be checked.
"""
from __future__ import annotations
from dataclasses import dataclass
import hashlib
import struct
import zlib

MAX_SIZE = 65535


@dataclass(frozen=True)
class DecodedApplication:
    mode: int
    protocol: int
    payload: bytes | None
    codec: str
    crc_valid: bool
    wire_sha256: str

    @property
    def wrapper_opaque(self):
        return self.protocol == 42


@dataclass(frozen=True)
class MtApplicationRecord:
    block_type: int
    sequence: int | None
    sender_slot: int | None
    flag: int | None
    record_index: int | None
    application: DecodedApplication | None


@dataclass(frozen=True)
class MovementIdentity:
    object_kind: int
    object_uid: int
    player_kind: int
    player_uid: int
    room: tuple[int, int]
    movement_flags: int


def _bounded_bytes(raw):
    if not isinstance(raw, (bytes, bytearray, memoryview)):
        raise ValueError('Expected bounded byte content')
    size = raw.nbytes if isinstance(raw, memoryview) else len(raw)
    if size > MAX_SIZE:
        raise ValueError('Expected bounded byte content')
    return bytes(raw)


def decode_application(raw):
    raw = _bounded_bytes(raw)
    if len(raw) < 7:
        raise ValueError('Incomplete application header')
    mode, protocol, expected = struct.unpack_from('<BHI', raw)
    body = raw[7:]
    candidates = [
        ('plain', body),
        ('rol1-xor56', bytes((((value << 1) | (value >> 7)) & 255) ^ 0x56 for value in body)),
        ('rol2-xor14', bytes((((value << 2) | (value >> 6)) & 255) ^ 0x14 for value in body)),
    ]
    matches = [(name, payload) for name, payload in candidates if zlib.crc32(payload) == expected]
    if len({payload for _, payload in matches}) > 1:
        raise ValueError('Ambiguous application representation')
    codec, payload = matches[0] if matches else ('opaque', None)
    return DecodedApplication(mode, protocol, payload, codec, bool(matches), hashlib.sha256(raw).hexdigest())


def decode_mt_blocks(raw):
    raw = _bounded_bytes(raw)
    records = []
    offset = 0
    while offset < len(raw):
        kind = raw[offset]
        if kind == 0:
            if offset + 8 > len(raw):
                raise ValueError('Incomplete MT ACK header')
            end = offset + 8 + (raw[offset + 6] >> 3)
            if end > len(raw):
                raise ValueError('Incomplete MT ACK bitmap')
            records.append(MtApplicationRecord(0, None, None, None, None, None))
            offset = end
            continue
        if kind not in (1, 2) or offset + 9 > len(raw):
            raise ValueError('Invalid MT block header')
        sequence, length, slot, flag = struct.unpack_from('<IHBB', raw, offset + 1)
        end = offset + 9 + length
        if end > len(raw) or slot >= 8:
            raise ValueError('Invalid MT block length or sender slot')
        body = raw[offset + 9:end]
        offset = end
        if not body:
            records.append(MtApplicationRecord(kind, sequence, slot, flag, None, None))
        elif kind == 2:
            records.append(MtApplicationRecord(kind, sequence, slot, flag, 0, decode_application(body)))
        else:
            cursor = 0
            index = 0
            while cursor < len(body):
                if cursor + 2 > len(body):
                    raise ValueError('Incomplete reliable application length')
                length = struct.unpack_from('<H', body, cursor)[0]
                cursor += 2
                if length < 7 or cursor + length > len(body):
                    raise ValueError('Incomplete or invalid reliable application record')
                application = decode_application(body[cursor:cursor + length])
                records.append(MtApplicationRecord(kind, sequence, slot, flag, index, application))
                cursor += length
                index += 1
    return records


def parse_gp4_identity(payload):
    payload = _bounded_bytes(payload)
    if len(payload) < 14:
        raise ValueError('Incomplete GP4 identity and movement flags')
    kind, uid, player_kind, player_uid, x, y, flags = struct.unpack_from('<HHHHBBI', payload)
    return MovementIdentity(kind, uid, player_kind, player_uid, (x, y), flags)


def parse_gp6_identity(payload):
    payload = _bounded_bytes(payload)
    if len(payload) != 6:
        raise ValueError('GP6 requires exactly three u16 fields')
    return struct.unpack('<HHH', payload)
