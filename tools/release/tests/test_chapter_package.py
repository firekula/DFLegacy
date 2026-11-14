import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import chapter_package as package
import update_v4 as v4


class ChapterPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)

    def stage(self, chapter, name):
        root = self.base / name
        root.mkdir()
        required = ['DFLegacy.Launcher.exe', 'DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe',
                    'DFLegacy.Bootstrap.ini', 'DNF.exe', 'Script.pvf']
        if chapter == 'chapter1': required.append('DFLegacy.Client.dll')
        for path in required: (root / path).write_bytes(path.encode())
        (root / 'ImagePacks2').mkdir()
        (root / 'ImagePacks2/a.npk').write_bytes(b'public resource')
        (root / 'DFLegacy.Chapter.json').write_text(json.dumps(dict(schema=1, chapter=chapter,
            entry='DFLegacy.Launcher.exe', requiredFiles=required)))
        return root

    def build(self, root, chapter, name, **kwargs):
        return package.build(root, self.base / name, chapter=chapter, architecture='x64', flavor='normal',
            version='v0.1.12' if chapter=='chapter1' else 'v1.0.1', commit='a'*40,
            sequence=4 if chapter=='chapter1' else 5, **kwargs)

    def test_full_transition_owns_resources_and_retires_old_modules(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        b = self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        self.assertEqual(b['manifest']['channel'], 'chapter-two')
        self.assertEqual(b['manifest']['minimumSequence'], 4)
        self.assertIn('DFLegacy.Client.dll', [x['path'] for x in b['manifest']['retired']])
        self.assertIn('ImagePacks2/a.npk', [x['path'] for x in b['manifest']['files']])
        v4.verify_local(self.base/'b')

    def test_changed_fixed_entry_is_rejected(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'DFLegacy.Launcher.exe').write_bytes(b'changed')
        with self.assertRaisesRegex(v4.ReleaseError, 'fixed entry'):
            self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])

    def test_private_files_and_debug_never_ship_in_official_second_chapter(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'launcher.chapter2.json').write_text('{"Password":"never-publish"}')
        (second/'logs').mkdir()
        (second/'logs/account.txt').write_text('never-publish')
        b = self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        path = self.base/'client.zip'
        package.create_starter(path, second, self.base/'b')
        with zipfile.ZipFile(path) as archive:
            config = json.loads(archive.read('launcher.chapter2.json'))
            self.assertFalse(config['ChapterTwoDebugEnabled'])
            self.assertFalse(config['ChapterTwoTestMuteEnabled'])
            self.assertEqual(config['ChapterTwoServerHost'], 'games.firekula.site')
            self.assertNotIn('Password', config)
            self.assertNotIn('logs/account.txt', archive.namelist())
            self.assertIn('.dflegacy/v4/installed.json', archive.namelist())
        (second/'debug.dll').write_bytes(b'old patch')
        with self.assertRaisesRegex(v4.ReleaseError, 'First Chapter module'):
            self.build(second, 'chapter2', 'bad', baseline=self.base/'a'/a['manifestAsset'])

    def test_mismatched_baseline_variant_is_rejected(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        manifest = a['manifest']; manifest['architecture'] = 'x86'
        with self.assertRaisesRegex(v4.ReleaseError, 'baseline'):
            self.build(self.stage('chapter2','c2'), 'chapter2','b',baseline=manifest)

if __name__ == '__main__': unittest.main()
