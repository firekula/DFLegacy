"""Read-only watcher for server/debug.dll diagnostics. Never sends game input."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import struct
import time

HEADER = struct.Struct('<IIQQHHHHBBBBBBH')
MONSTER = struct.Struct('<HHBBBBHH')
STATES = ['待进房', '未释放', '已释放', '已死亡']


def decode(data):
    if len(data) < HEADER.size:
        raise ValueError('short header')
    magic, version, file_time, run, uid, dungeon, current_map, boss_map, x, y, bx, by, clear, at_boss, count = HEADER.unpack_from(data)
    if magic != 0x31474244 or version != 1 or count > 255 or len(data) != HEADER.size + MONSTER.size * count:
        raise ValueError('unsupported or partial snapshot')
    hidden = []
    for i in range(count):
        monster_uid, monster, level, box, state, reserved, px, py = MONSTER.unpack_from(data, HEADER.size + i * MONSTER.size)
        if state > 3 or reserved:
            raise ValueError('bad monster state')
        hidden.append(dict(uid=monster_uid, monster=monster, level=level, box=box,
                           state=STATES[state], x=px, y=py))
    return dict(run=run, user_id=uid, dungeon=dungeon, current_map=current_map,
                boss_map=boss_map, room=[x, y], boss_room=[bx, by], clear=bool(clear),
                at_boss=bool(at_boss), hidden=hidden), file_time


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
