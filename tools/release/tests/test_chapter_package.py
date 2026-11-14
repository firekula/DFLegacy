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
        if chapter == 'chapter2':
            (root / 'ImagePacks3').mkdir()
            (root / 'ImagePacks4').mkdir()
            (root / 'ImagePacks4/a.npk').write_bytes(b'second chapter resource')
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

    def test_second_chapter_carries_filter_license_and_source_location(self):
        first = self.stage('chapter1', 'c1')
        baseline = self.build(first, 'chapter1', 'baseline')
        second = self.stage('chapter2', 'c2')
        licenses = second / 'Licenses'
        licenses.mkdir()
        (licenses / 'xbrz-1.9.txt').write_bytes(b'upstream license')
        (licenses / 'NativeSource.txt').write_bytes(b'corresponding source location')
        result = self.build(second, 'chapter2', 'release',
                            baseline=self.base / 'baseline' / baseline['manifestAsset'])
        names = {item['path'] for item in result['manifest']['files']}
        self.assertTrue({'Licenses/xbrz-1.9.txt', 'Licenses/NativeSource.txt'} <= names)
        archive = self.base / 'client.zip'
        package.create_starter(archive, second, self.base / 'release')
        with zipfile.ZipFile(archive) as client:
            self.assertEqual(client.read('Licenses/xbrz-1.9.txt'), b'upstream license')
            configuration = json.loads(client.read('launcher.chapter2.json'))
            self.assertEqual(configuration['ChapterTwoPostProcess'], 'none')

    def test_second_chapter_refuses_server_format_pvf_before_packaging(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'Script.pvf').write_bytes(b'DNF_SCRIPT_PACK' + bytes(64))
        with self.assertRaisesRegex(v4.ReleaseError, 'server-format PVF'):
            self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        self.assertFalse((self.base/'b').exists())

    def test_second_chapter_preserves_distinct_image_groups_in_manifest_and_starter(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'ImagePacks3/a.npk').write_bytes(b'third group')
        b = self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        names = {row['path'] for row in b['manifest']['files']}
        for group in ('ImagePacks2', 'ImagePacks3', 'ImagePacks4'):
            self.assertIn(group+'/a.npk', names)
        package.create_starter(self.base/'client.zip', second, self.base/'b')
        with zipfile.ZipFile(self.base/'client.zip') as archive:
            for group in ('ImagePacks2', 'ImagePacks3', 'ImagePacks4'):
                name = group+'/a.npk'
                self.assertEqual(archive.read(name), (second/name).read_bytes())

    def test_public_second_chapter_rejects_stage_missing_original_image_group(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'ImagePacks4/a.npk').unlink()
        with self.assertRaisesRegex(v4.ReleaseError, 'ImagePacks4'):
            self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        self.assertFalse((self.base/'b').exists())

    def test_starter_rejects_resources_added_after_manifest_was_built(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        (second/'ImagePacks4/new.npk').write_bytes(b'cannot silently omit')
        with self.assertRaisesRegex(v4.ReleaseError, 'resource.*manifest'):
            package.create_starter(self.base/'client.zip', second, self.base/'b')
        self.assertFalse((self.base/'client.zip').exists())

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
            self.assertIn('ImagePacks3/', archive.namelist())
        (second/'debug.dll').write_bytes(b'old patch')
        with self.assertRaisesRegex(v4.ReleaseError, 'First Chapter module'):
            self.build(second, 'chapter2', 'bad', baseline=self.base/'a'/a['manifestAsset'])

    def test_mismatched_baseline_variant_is_rejected(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        manifest = a['manifest']; manifest['architecture'] = 'x86'
        with self.assertRaisesRegex(v4.ReleaseError, 'baseline'):
            self.build(self.stage('chapter2','c2'), 'chapter2','b',baseline=manifest)

    def test_official_starter_keeps_github_updates_on_and_debug_off(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        (second/'launcher.chapter2.json').write_text(json.dumps(dict(
            ChapterTwoDebugEnabled=True, ChapterTwoTestMuteEnabled=True, Account='private-player', Password='private')))
        self.build(second, 'chapter2', 'b', baseline=self.base/'a'/a['manifestAsset'])
        package.create_starter(self.base/'standalone.zip', second, self.base/'b')
        with zipfile.ZipFile(self.base/'standalone.zip') as archive:
            config=json.loads(archive.read('launcher.chapter2.json'))
            self.assertFalse(config['DisableGitHubUpdates'])
            self.assertFalse(config['ChapterTwoDebugEnabled'])
            self.assertFalse(config['ChapterTwoTestMuteEnabled'])
            self.assertFalse(config['StartServer'])
            self.assertEqual(config['ChapterTwoServerHost'], 'games.firekula.site')
            self.assertNotIn('Account', config)
            self.assertNotIn('Password', config)
            self.assertIn('.dflegacy/v4/installed.json', archive.namelist())

    def test_second_chapter_patch_reuses_previous_resources_and_preserves_retirements(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        baseline = self.base/'a'/a['manifestAsset']
        b = self.build(second, 'chapter2', 'b', baseline=baseline)
        (second/'ImagePacks4/new.npk').write_bytes(b'new resource')
        c = package.build(second, self.base/'c', chapter='chapter2', architecture='x64', flavor='normal',
            version='v1.0.2', commit='b'*40, sequence=6, baseline=baseline,
            previous=self.base/'b'/b['manifestAsset'])
        self.assertEqual(c['manifest']['retired'], b['manifest']['retired'])
        self.assertEqual(c['manifest']['minimumSequence'], 4)
        self.assertEqual(len(c['assets']), 1)
        for row in c['manifest']['files']:
            self.assertEqual({part['release'] for part in row['parts']},
                {'v1.0.2' if row['path']=='ImagePacks4/new.npk' else 'v1.0.1'})
        package.create_starter(self.base/'patch-client.zip', second, self.base/'c')
        with zipfile.ZipFile(self.base/'patch-client.zip') as archive:
            self.assertEqual(archive.read('ImagePacks4/new.npk'), b'new resource')

    def test_second_chapter_previous_must_have_same_variant_and_increasing_sequence(self):
        first = self.stage('chapter1', 'c1')
        a = self.build(first, 'chapter1', 'a')
        second = self.stage('chapter2', 'c2')
        baseline = self.base/'a'/a['manifestAsset']
        b = self.build(second, 'chapter2', 'b', baseline=baseline)
        for arch, sequence in (('x86', 6), ('x64', 5)):
            previous = dict(b['manifest'], architecture=arch)
            with self.assertRaises(v4.ReleaseError):
                package.build(second, self.base/f'bad-{arch}', chapter='chapter2', architecture='x64',
                    flavor='normal', version='v1.0.2', commit='b'*40, sequence=sequence,
                    baseline=baseline, previous=previous)
            self.assertFalse((self.base/f'bad-{arch}').exists())

if __name__ == '__main__': unittest.main()
