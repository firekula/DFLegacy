import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import local_bridge


MODULE = Path(__file__).with_name('Build-LocalChapterChannel.py')
SPEC = importlib.util.spec_from_file_location('local_chapter_channel', MODULE)
channel = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(channel)


class LocalChapterChannelTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.stage = self.root / 'stage'
        self.stage.mkdir()

    def put(self, name, data):
        path = self.stage / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def descriptor(self, chapter, required=None):
        if required is None:
            required = ['DFLegacy.Launcher.exe', 'Script.pvf',
                        'DFLegacy.Client.dll' if chapter == 'chapter1' else 'DNF.exe']
        return self.put('DFLegacy.Chapter.json', json.dumps(
            dict(schema=1, chapter=chapter, entry='DFLegacy.Launcher.exe', requiredFiles=required)).encode())

    def prepare(self, chapter):
        self.put('DFLegacy.Launcher.exe', b'launcher')
        self.put('Script.pvf', b'pvf')
        self.put('DNF.exe', b'game')
        self.descriptor(chapter)
        if chapter == 'chapter1':
            self.put('DFLegacy.Client.dll', b'first patch')

    def build(self, output, chapter='chapter2', version='fixture', **options):
        path = self.root / output
        summary = channel.build(self.stage, path, chapter, version, 'a' * 40, **options)
        return path, summary, json.loads((path / summary['manifest']).read_bytes())

    def test_emits_bounded_assets_with_independent_hashes_and_excludes_private_files(self):
        self.prepare('chapter2')
        self.put('ImagePacks2/objects.npk', bytes(range(96)))
        self.put('Music/track.ogg', b'sound')
        for name in ('launcher.json', 'launcher.chapter2.json', 'Config.ini',
                     'Plugin/launcher.chapter2.json~previous', 'Plugin/trace.log',
                     'ImagePacks2/Accounts/data.json', 'Music/Screenshots/image.png'):
            self.put(name, b'PRIVATE')
        before = {p.relative_to(self.stage): p.read_bytes() for p in self.stage.rglob('*') if p.is_file()}
        with patch.object(channel.v4, 'PART_LIMIT', 32):
            output, summary, manifest = self.build('c2')
        self.assertEqual({file['path'] for file in manifest['files']}, {'DFLegacy.Launcher.exe', 'DNF.exe',
                         'Script.pvf', 'DFLegacy.Chapter.json', 'ImagePacks2/objects.npk', 'Music/track.ogg'})
        self.assertEqual(summary['sha256'], hashlib.sha256((output / summary['manifest']).read_bytes()).hexdigest())
        self.assertEqual(summary['manifest'], 'DFLegacy-client-update-v4-c2-local-normal-win-x64.json')
        self.assertEqual(summary['bytes'], sum(file['size'] for file in manifest['files']))
        assets = set()
        for file in manifest['files']:
            content = bytearray()
            for part in file['parts']:
                self.assertEqual(part['release'], 'fixture')
                path = output / 'releases' / part['release'] / part['asset']
                body = path.read_bytes()
                self.assertLessEqual(len(body), 32)
                self.assertEqual(part['size'], len(body))
                self.assertEqual(part['sha256'], hashlib.sha256(body).hexdigest())
                assets.add(path)
                content.extend(body)
            self.assertEqual(content, before[Path(file['path'])])
            self.assertEqual(file['sha256'], hashlib.sha256(content).hexdigest())
        self.assertEqual(assets, set((output / 'releases' / 'fixture').iterdir()))
        self.assertEqual(summary['parts'], len(assets))
        self.assertEqual(before, {p.relative_to(self.stage): p.read_bytes() for p in self.stage.rglob('*') if p.is_file()})

    def test_rejects_invalid_descriptor_and_required_file_absent_from_managed_assets(self):
        self.prepare('chapter2')
        self.descriptor('chapter1')
        with self.assertRaisesRegex(ValueError, 'Chapter identity'):
            self.build('wrong')
        self.descriptor('chapter2', ['DFLegacy.Launcher.exe', 'Script.pvf', 'DNF.exe', 'Plugin/missing.dll'])
        with self.assertRaisesRegex(ValueError, 'required'):
            self.build('missing')
        self.assertFalse((self.root / 'missing').exists())
        self.descriptor('chapter2', ['DFLegacy.Launcher.exe', 'Script.pvf', 'DNF.exe', 'launcher.chapter2.json'])
        self.put('launcher.chapter2.json', b'private')
        with self.assertRaisesRegex(ValueError, 'protected|private'):
            self.build('private')
        self.descriptor('chapter2')
        partial = self.put('DFLegacy.Launcher.App.exe', b'app')
        with self.assertRaisesRegex(ValueError, 'missing required managed resource'):
            self.build('partial-layout')
        partial.unlink()
        self.put('DFLegacy.Client.dll', b'old')
        with self.assertRaisesRegex(ValueError, 'Chapter One module'):
            self.build('mixed')

    def test_refuses_version_path_escape_before_creating_any_output(self):
        self.prepare('chapter2')
        with self.assertRaises(ValueError):
            self.build('unsafe', version='../../outside')
        self.assertFalse((self.root / 'unsafe').exists())
        self.assertFalse((self.root / 'outside').exists())

    def test_retirement_continuity_and_sequence_bound(self):
        self.prepare('chapter1')
        self.put('Music/old.ogg', b'old track')
        one, one_summary, _ = self.build('one', chapter='chapter1', version='first', sequence=5)
        self.prepare('chapter2')
        (self.stage / 'DFLegacy.Client.dll').unlink()
        (self.stage / 'Music/old.ogg').unlink()
        self.put('Music/new.ogg', b'new track')
        two, two_summary, target = self.build('two', version='second', sequence=6, minimum_sequence=5,
                                              previous=one / one_summary['manifest'])
        self.assertEqual({(row['path'], row['action'], row['sequence']) for row in target['retired']},
                         {('DFLegacy.Client.dll', 'delete', 6), ('Music/old.ogg', 'delete', 6)})
        with self.assertRaisesRegex(ValueError, 'sequence'):
            self.build('replay', version='third', sequence=6, previous=two / two_summary['manifest'])
        self.assertFalse((self.root / 'replay').exists())
        with self.assertRaisesRegex(ValueError, 'version'):
            self.build('reuse-version', version='second', sequence=7, minimum_sequence=5,
                       previous=two / two_summary['manifest'])
        with self.assertRaisesRegex(ValueError, 'minimum sequence'):
            self.build('lower-floor', version='third', sequence=7, minimum_sequence=4,
                       previous=two / two_summary['manifest'])
        _, _, later = self.build('later', version='third', sequence=7, minimum_sequence=5,
                                 previous=two / two_summary['manifest'])
        self.assertEqual(later['retired'], target['retired'])
        self.prepare('chapter1')
        with self.assertRaisesRegex(ValueError, 'chapter'):
            self.build('reverse', chapter='chapter1', version='reverse', sequence=8,
                       previous=two / two_summary['manifest'])

    def test_only_c1_can_emit_bound_configuration_migration(self):
        self.prepare('chapter1')
        migration = self.root / 'migration.json'
        migration.write_text(json.dumps([local_bridge.bridge_migration(48123, 'c1-bridge')]))
        _, _, manifest = self.build('one', chapter='chapter1', migrations=migration)
        self.assertEqual(manifest['migrations'][0]['value']['repository'], 'http://127.0.0.2:48123/c1')
        self.prepare('chapter2')
        (self.stage / 'DFLegacy.Client.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'migration'):
            self.build('two', migrations=migration)


if __name__ == '__main__':
    unittest.main()
