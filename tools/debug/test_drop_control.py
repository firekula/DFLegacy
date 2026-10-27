from pathlib import Path
import struct
import time

import pytest
from watch_dungeon import decode, HEADER, MONSTER


def snapshot(version=2, enabled=1, allowed=1, result=1, request=123):
    now = time.time_ns() // 100 + 116444736000000000
    header = HEADER.pack(0x31474244, version, now, 456, 77, 3, 1, 2, 0, 0, 1, 1, 0, 0, 0)
    return header + (struct.pack('<BBBBq', enabled, allowed, result, 0, request) if version == 2 else b'')


def test_v2_control_state_and_v1_compatibility():
    value, _ = decode(snapshot())
    assert value['drop_test'] == dict(supported=True, enabled=True, allowed=True, result=1, request_id=123)
    old, _ = decode(snapshot(version=1))
    assert old['drop_test']['supported'] is False
    assert old['drop_test']['allowed'] is False


@pytest.mark.parametrize('field,value', [('enabled', 2), ('allowed', 2), ('result', 4), ('request', -1)])
def test_bad_control_trailer(field, value):
    with pytest.raises(ValueError):
        decode(snapshot(**{field: value}))


@pytest.mark.parametrize('rejected_future_reply', [False, True])
def test_helper_waits_for_matching_real_file_acknowledgment(tmp_path, rejected_future_reply):
    from drop_control import set_drop_test
    import threading
    path = tmp_path / 'dfdebug-77.bin'
    path.write_bytes(snapshot(enabled=0, result=3, request=2**63-1) if rejected_future_reply else snapshot(enabled=0))
    observed = []

    def server():
        command = tmp_path / 'dfdebug-drop-77.bin'
        deadline = time.monotonic() + 2
        while not command.exists() and time.monotonic() < deadline:
            time.sleep(.01)
        data = command.read_bytes()
        time.sleep(.1)  # leave the previous reply visible until the next service tick
        magic, version, request, run, uid, enabled = struct.unpack('<IIqqH4xB1x', data)
        observed.append((magic, version, run, uid, enabled))
        temporary = tmp_path / 'reply.tmp'
        temporary.write_bytes(snapshot(enabled=enabled, request=request))
        temporary.replace(path)
        command.unlink()

    worker = threading.Thread(target=server)
    worker.start()
    result = set_drop_test(tmp_path, 77, True, timeout=2)
    worker.join()
    assert observed == [(0x31504444, 1, 456, 77, 1)]
    assert result['enabled'] is True and result['request_id'] != 123


def test_helper_refuses_stale_or_unauthorized_snapshot(tmp_path):
    from drop_control import set_drop_test
    path = tmp_path / 'dfdebug-77.bin'
    path.write_bytes(snapshot(allowed=0))
    with pytest.raises(RuntimeError, match='test'):
        set_drop_test(tmp_path, 77, True, timeout=.1)
    old = bytearray(snapshot())
    struct.pack_into('<Q', old, 8, 1)
    path.write_bytes(old)
    with pytest.raises(RuntimeError, match='stale'):
        set_drop_test(tmp_path, 77, True, timeout=.1)
    assert not (tmp_path / 'dfdebug-drop-77.bin').exists()
