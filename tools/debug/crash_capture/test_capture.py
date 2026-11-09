"""Windows integration checks; fixture is a separately compiled x86 DNF.exe.
Usage: python test_capture.py CAPTURE_EXE FIXTURE_EXE OUTPUT_DIRECTORY
"""
import json
import re
import struct
import subprocess
import sys
import tempfile
import time
from pathlib import Path

def main():
    tool, fixture, root = (Path(v).resolve() for v in sys.argv[1:])
    root.mkdir(parents=True, exist_ok=True)
    root = Path(tempfile.mkdtemp(prefix='validation-', dir=root))

    def streams(path):
        data = path.read_bytes()
        assert data[:4] == b'MDMP', path
        count, offset = struct.unpack_from('<II', data, 8)
        items = {}
        for i in range(count):
            kind, length, rva = struct.unpack_from('<III', data, offset + i * 12)
            assert rva + length <= len(data)
            items[kind] = data[rva:rva + length]
        return data, items

    def run(case, mode='monitor', stop=False):
        directory = root / case
        directory.mkdir(exist_ok=False)
        with (directory/'fixture.txt').open('w') as out, (directory/'tool.txt').open('w') as log:
            game = subprocess.Popen([str(fixture), case], stdout=out, stderr=subprocess.STDOUT)
            watcher = subprocess.Popen([str(tool), mode, str(game.pid), str(directory)], stdout=log, stderr=subprocess.STDOUT)
            try:
                if stop:
                    deadline = time.time() + 8
                    while 'MONITOR READY' not in (directory/'tool.txt').read_text():
                        assert time.time() < deadline, 'watcher did not attach'
                        time.sleep(.05)
                    (directory/'STOP').touch()
                    assert watcher.wait(8) == 0
                    assert game.poll() is None, 'detach killed the target'
                else:
                    assert watcher.wait(45) == 0
                exit_code = game.wait(12)
            finally:
                # Clean up only child fixtures created by this test, never live games.
                if watcher.poll() is None:
                    (directory/'STOP').touch()
                    try: watcher.wait(5)
                    except subprocess.TimeoutExpired: watcher.kill()
                if game.poll() is None: game.kill()
        text = (directory/'exceptions.txt').read_text(encoding='utf-8-sig')
        dumps = sorted(directory.glob('*.dmp'))
        if case == 'handled':
            assert exit_code == 0
            assert 'fixture-handled-first-chance' in (directory/'fixture.txt').read_text()
            assert len(dumps) == 1
            data, entries = streams(dumps[0])
            exception = entries[6]
            code, = struct.unpack_from('<I', exception, 8)
            address, = struct.unpack_from('<Q', exception, 24)
            count, = struct.unpack_from('<I', exception, 32)
            access, bad_address = struct.unpack_from('<QQ', exception, 40)
            context_size, context_rva = struct.unpack_from('<II', exception, 160)
            eip, = struct.unpack_from('<I', data, context_rva + 184)
            assert code == 0xC0000005 and count == 2
            assert access == 1 and bad_address == 0x123
            assert eip == address == int(re.search(r'address=([0-9A-F]+)', text)[1], 16)
            assert context_size >= 204 and 3 in entries and 4 in entries
            assert 'DNF.exe' in text and 'offset=' in text
            assert re.search(r'instruction_bytes=[0-9A-F]{64}', text)
        elif case == 'unhandled':
            assert exit_code == 0xC0000005
            assert 'first_chance=0' in text and len(dumps) == 2
        elif case == 'rotate':
            assert exit_code == 0 and len(dumps) == 16
            assert text.count('result=saved') == 18
        elif mode == 'snapshot':
            assert exit_code == 0 and len(dumps) == 1
            _, entries = streams(dumps[0])
            assert 3 in entries and 4 in entries and 6 not in entries
        else:
            assert exit_code == 0 and len(dumps) == 0
        return {'case':case, 'exit_code':exit_code, 'dumps':len(dumps), 'passed':True}

    results = [run('handled'), run('unhandled'), run('detach',stop=True),
               run('snapshot',mode='snapshot'), run('rotate')]
    (root/'results.json').write_text(json.dumps(results,indent=2))
    print(json.dumps(results,indent=2))


if __name__ == "__main__":
    main()
