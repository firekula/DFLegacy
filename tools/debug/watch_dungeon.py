"""Read-only watcher for server/debug.dll diagnostics. Never sends game input."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import struct
import time

HEADER = struct.Struct('<IIQQHHHHBBBBBBH')
MONSTER = struct.Struct('<HHBBBBHH')
CONTROL = struct.Struct('<BBBBq')
STATES = ['待进房', '未释放', '已释放', '已死亡']


def decode(data):
    if len(data) < HEADER.size:
        raise ValueError('short header')
    magic, version, file_time, run, uid, dungeon, current_map, boss_map, x, y, bx, by, clear, at_boss, count = HEADER.unpack_from(data)
    if magic != 0x31474244 or version not in (1, 2) or not run or not uid or count > 255 \
            or clear > 1 or at_boss > 1 \
            or len(data) != HEADER.size + MONSTER.size * count + (CONTROL.size if version == 2 else 0):
        raise ValueError('unsupported or partial snapshot')
    hidden = []
    seen = set()
    for i in range(count):
        monster_uid, monster, level, box, state, reserved, px, py = MONSTER.unpack_from(data, HEADER.size + i * MONSTER.size)
        if state > 3 or reserved or monster_uid in seen:
            raise ValueError('bad monster state')
        seen.add(monster_uid)
        hidden.append(dict(uid=monster_uid, monster=monster, level=level, box=box,
                           state=STATES[state], x=px, y=py))
    control = dict(supported=False, enabled=False, allowed=False, result=0, request_id=0)
    if version == 2:
        enabled, allowed, result, reserved, request_id = CONTROL.unpack_from(data, len(data) - CONTROL.size)
        if enabled > 1 or allowed > 1 or result > 3 or reserved or request_id < 0:
            raise ValueError('bad control state')
        control = dict(supported=True, enabled=bool(enabled), allowed=bool(allowed), result=result, request_id=request_id)
    return dict(run=run, user_id=uid, dungeon=dungeon, current_map=current_map,
                boss_map=boss_map, room=[x, y], boss_room=[bx, by], clear=bool(clear),
                at_boss=bool(at_boss), hidden=hidden, drop_test=control), file_time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--once', action='store_true')
    args = parser.parse_args()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    previous = {}
    print('只读监听已启动；游戏操作由玩家完成。Ctrl+C 停止。', flush=True)
    try:
        with args.out.open('a', encoding='utf-8') as output:
            while True:
                active = {}
                for path in args.directory.glob('dfdebug-*.bin'):
                    try:
                        value, stamp = decode(path.read_bytes())
                        age = time.time() - (stamp / 10_000_000 - 11_644_473_600)
                        if not 0 <= age < 2:
                            continue
                        key = str(value['user_id'])
                        active[key] = value
                        if previous.get(key) == value:
                            continue
                        event = dict(at=datetime.now().astimezone().isoformat(timespec='milliseconds'), **value)
                        line = json.dumps(event, ensure_ascii=False)
                        output.write(line + '\n')
                        output.flush()
                        print(line, flush=True)
                    except (OSError, ValueError, struct.error):
                        continue
                for key in previous.keys() - active.keys():
                    line = json.dumps(dict(at=datetime.now().astimezone().isoformat(), user_id=key, state='已离开副本或数据中断'), ensure_ascii=False)
                    output.write(line + '\n')
                    output.flush()
                    print(line, flush=True)
                previous = active
                if args.once:
                    break
                time.sleep(0.25)
    except KeyboardInterrupt:
        print('监听已停止。', flush=True)


if __name__ == '__main__':
    main()
