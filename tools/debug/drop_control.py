"""Set the current instance's drop-test switch through the debug panel's local mailbox."""
import argparse
import json
import os
from pathlib import Path
import struct
import time

from watch_dungeon import decode

REQUEST = struct.Struct('<IIqqH4xB1x')


def file_time():
    return time.time_ns() // 100 + 116444736000000000


def read_current(path, user_id):
    with path.open('rb') as stream:
        value, stamp = decode(stream.read(3113))
    if value['user_id'] != user_id or not 0 <= file_time() - stamp <= 15_000_000:
        raise RuntimeError('stale or mismatched debug snapshot')
    if not value['drop_test']['supported']:
        raise RuntimeError('server snapshot does not support drop-test control')
    return value


def set_drop_test(directory, user_id, enabled, timeout=5):
    if not 1 <= user_id <= 65535 or not 0 < timeout <= 30:
        raise ValueError('user_id must be 1..65535 and timeout in (0,30]')
    directory = Path(directory).resolve(strict=True)
    path = directory / f'dfdebug-{user_id}.bin'
    before = read_current(path, user_id)
    if not before['drop_test']['allowed']:
        raise RuntimeError('only the test account may change this switch')
    request_id = file_time()
    request = directory / f'dfdebug-drop-{user_id}.bin'
    temporary = request.with_name(request.name + f'.tmp-{os.getpid()}-{request_id}')
    try:
        temporary.write_bytes(REQUEST.pack(0x31504444, 1, request_id, before['run'], user_id, int(enabled)))
        temporary.replace(request)
    finally:
        temporary.unlink(missing_ok=True)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            value = read_current(path, user_id)
        except (OSError, ValueError):
            time.sleep(.05)
            continue
        if value['run'] != before['run']:
            raise RuntimeError('dungeon run changed before acknowledgment')
        state = value['drop_test']
        if state['request_id'] == request_id:
            if state['result'] != 1 or state['enabled'] != enabled:
                raise RuntimeError(f'server rejected drop-test request: result={state["result"]}')
            return dict(success=True, user_id=user_id, run=value['run'], enabled=state['enabled'], request_id=request_id)
        if state['request_id'] > request_id and state['result'] == 1:
            raise RuntimeError('request superseded by another controller')
        time.sleep(.05)
    raise TimeoutError('server did not acknowledge this drop-test request')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--user-id', type=int, required=True)
    choice = parser.add_mutually_exclusive_group(required=True)
    choice.add_argument('--on', action='store_true')
    choice.add_argument('--off', action='store_true')
    parser.add_argument('--timeout', type=float, default=5)
    args = parser.parse_args()
    try:
        print(json.dumps(set_drop_test(args.directory, args.user_id, args.on, args.timeout)))
        return 0
    except (OSError, ValueError, RuntimeError, TimeoutError) as error:
        print(json.dumps(dict(success=False, error=str(error))))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
