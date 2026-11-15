import hashlib
from pathlib import Path
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
import zipfile

import local_bridge


def fake_pe(path):
    image = bytearray(512)
    image[:2] = b'MZ'
    image[60] = 64
    image[64:70] = b'PE\0\0\x64\x86'
    image[88:90] = b'\x0b\x02'
    path.write_bytes(image)


class LocalBridgeTests(unittest.TestCase):
    def test_legacy_backup_requires_all_original_helpers_in_bridge(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            old,new=root/'old',root/'new'
            for directory in (old,new):
                directory.mkdir();fake_pe(directory/'DFLegacy.Launcher.exe')
                (directory/'DFLegacy.Client.dll').write_bytes(b'patch')
            for name in local_bridge.HELPERS:(old/name).write_bytes(b'old helper')
            with self.assertRaisesRegex(ValueError,'original runtime'):
                local_bridge.build(old,new,root/'output','old','bridge','a'*40,12)

    def test_hosted_bundle_and_resumable_v4_are_read_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            old, new, output = [root / name for name in ('old', 'new', 'source')]
            for directory in (old, new):
                directory.mkdir()
                fake_pe(directory / 'DFLegacy.Launcher.exe')
                (directory / 'DFLegacy.Client.dll').write_bytes(directory.name.encode())
                (directory / 'Script.pvf').write_bytes(b'protected script')
                (directory / 'launcher.json').write_bytes(b'private')
            snapshot = {file: file.read_bytes() for directory in (old, new) for file in directory.iterdir()}
            receipt = local_bridge.build(old, new, output, 'v0.1.11', 'local-bridge', 'a' * 40, 123)
            archive = output / 'v1' / 'x64' / 'normal' / (receipt['sha256'] + '.zip')
            with zipfile.ZipFile(archive) as package:
                self.assertEqual(set(package.namelist()), {'DFLegacy.Launcher.exe', 'DFLegacy.Client.dll', 'update-manifest.json'})
                self.assertEqual(package.read('DFLegacy.Client.dll'), b'new')
            self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), receipt['sha256'])
            self.assertEqual(snapshot, {file: file.read_bytes() for file in snapshot})
            part = output / 'c2' / 'releases' / 'latest' / 'part.bin'
            part.parent.mkdir(parents=True)
            part.write_bytes(b'012345')
            (output / 'c2' / 'launcher.json').write_bytes(b'private')
            with local_bridge.create_server(output, 0) as server:
                self.assertEqual(server.server_address[0], '127.0.0.2')
                worker = threading.Thread(target=server.serve_forever)
                worker.start()
                try:
                    base = f'http://127.0.0.2:{server.server_address[1]}'
                    client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
                    with client.open(base + '/v1/x64/normal/latest') as response:
                        self.assertIn(receipt['sha256'].encode(), response.read())
                    with client.open(urllib.request.Request(base + '/c2/releases/latest/part.bin', headers={'Range': 'bytes=2-'})) as response:
                        self.assertEqual((response.status, response.headers['Content-Range'], response.read()), (206, 'bytes 2-5/6', b'2345'))
                    for forbidden in ('/c2/launcher.json', '/c2/releases/latest/../part.bin', '/v1/x86/normal/latest', '/c2/releases/latest/%2e%2e/part.bin'):
                        with self.assertRaises(urllib.error.HTTPError) as caught:
                            client.open(base + forbidden)
                        self.assertEqual(caught.exception.code, 404)
                finally:
                    server.shutdown()
                    worker.join()


if __name__ == '__main__':
    unittest.main()
