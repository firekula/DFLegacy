"""Bootstrap packages use explicit public inputs and the real V4 receipt contract."""
import base64
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile

RELEASE = Path(__file__).parents[1]
REPO = RELEASE.parents[1]
sys.path.insert(0, str(RELEASE))
import update_v4 as v4
import release as legacy

spec = importlib.util.spec_from_file_location('bootstrap_package', RELEASE / 'bootstrap_package.py')
bp = None
if (RELEASE / 'bootstrap_package.py').exists():
    bp = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(bp)


def pe(architecture, label=b'fixture'):
    data = bytearray(512)
    data[:2] = b'MZ'
    struct.pack_into('<I', data, 0x3c, 128)
    data[128:132] = b'PE\0\0'
    struct.pack_into('<H', data, 132, {'x86': 0x14c, 'x64': 0x8664}[architecture])
    return bytes(data) + label


class BootstrapPackages(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(bp, 'bootstrap packaging helper must exist')
        base = Path(tempfile.gettempdir()) / 'DFLegacy-bootstrap-packaging-20261108'
        base.mkdir(exist_ok=True)
        self.tmp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.built = self.root / 'built public inputs'
        self.built.mkdir()

    def inputs(self, architecture='x86', flavor='normal'):
        result = []
        data = {
            'DFLegacy.Launcher.exe': pe(architecture, b'native-entry'),
            'DFLegacy.Launcher.App.exe': pe(architecture, b'managed-app'),
            'DFLegacy.Updater.exe': pe(architecture, b'managed-app'),
            'DFLegacy.Client.dll': pe('x86', b'unchanged-game-patch'),
            'DFLegacy.Diagnostics.exe': pe(architecture, b'self-contained-helper'),
            'DFLegacy.CrashCapture.exe': pe('x86', b'local-capture-helper'),
            'UpdateTestAssets/probe.npk': b'public unused resource',
            'UpdateTestAssets/probe.pvf': b'public unused static resource',
        }
        if flavor == 'test':
            data['debug.dll'] = pe('x86', b'public debug patch')
        for name, content in data.items():
            source = self.built / name
            source.parent.mkdir(parents=True, exist_ok=True)
            source.write_bytes(content)
            result.append(dict(path=name, source=str(source), sha256=hashlib.sha256(content).hexdigest(), size=len(content)))
        return result

    def options(self, **changes):
        options = dict(architecture='x86', flavor='normal', version='v0.1.8-giteatest.3',
                       commit='a' * 40, sequence=3, provider='gitea', repository='firekula/DFLegacy',
                       channel='gitea-test', origin='http://firekula.site:3000')
        options.update(changes)
        return options

    def build(self, name='release', inputs=None, **changes):
        options = self.options(**changes)
        return bp.build_release(self.root / name,
                                inputs=inputs if inputs is not None else self.inputs(options['architecture'], options['flavor']),
                                **options)

    def starter(self, result, name='starter.zip', **changes):
        options = {k: v for k, v in self.options().items() if k in
                   ('architecture', 'flavor', 'provider', 'repository', 'channel', 'origin')}
        options.update(changes)
        archive = self.root / name
        bp.create_starter(archive, root=result['sourceRoot'], bundle=result['bundleRoot'], **options)
        return archive

    def test_four_variants_use_real_architecture_and_deduplicate_managed_bytes(self):
        for architecture in ('x86', 'x64'):
            for flavor in ('normal', 'test'):
                with self.subTest(architecture=architecture, flavor=flavor):
                    result = self.build(architecture + flavor, architecture=architecture, flavor=flavor)
                    m = v4.verify_local(result['bundleRoot'])['manifest']
                    self.assertEqual((m['architecture'], m['flavor']), (architecture, flavor))
                    rows = {f['path']: f for f in m['files']}
                    self.assertEqual(rows['DFLegacy.Launcher.App.exe']['parts'], rows['DFLegacy.Updater.exe']['parts'])
                    self.assertEqual(len(result['assets']), len(rows) - 1)
                    descriptor = (Path(result['sourceRoot']) / 'DFLegacy.Bootstrap.ini').read_bytes()
                    self.assertEqual(descriptor, ('[Bootstrap]\nSchemaVersion=1\nArchitecture=' + architecture +
                        '\nLauncherSha256=' + rows['DFLegacy.Launcher.App.exe']['sha256'] +
                        '\nUpdaterSha256=' + rows['DFLegacy.Updater.exe']['sha256'] + '\n').encode('ascii'))
                    self.assertLessEqual(len(descriptor), 4096)
                    self.starter(result, name=architecture + flavor + '.zip', architecture=architecture, flavor=flavor)

    def test_all_fixed_files_are_required_before_output_is_created(self):
        inputs = self.inputs()
        for missing in ('DFLegacy.Launcher.exe', 'DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe', 'DFLegacy.Client.dll', 'DFLegacy.Diagnostics.exe', 'DFLegacy.CrashCapture.exe'):
            with self.subTest(missing=missing), self.assertRaises(v4.ReleaseError):
                self.build(inputs=[row for row in inputs if row['path'] != missing])
            self.assertFalse((self.root / 'release').exists())

    def test_github_stable_four_variants_have_source_bound_repair_receipts(self):
        for architecture in ('x86', 'x64'):
            for flavor in ('normal', 'test'):
                options = dict(architecture=architecture, flavor=flavor, provider='github',
                               repository='firekula/DFLegacy', channel='stable', origin='https://github.com')
                result = self.build('github-' + architecture + flavor, version='v0.1.8', sequence=1, **options)
                archive = self.starter(result, name='github-' + architecture + flavor + '.zip', **options)
                with zipfile.ZipFile(archive) as z:
                    receipt = json.loads(z.read('.dflegacy/v4/installed.json'))
                    self.assertEqual(receipt['release']['source']['provider'], 'github')
                    self.assertEqual(receipt['release']['source']['channel'], 'stable')
                    self.assertNotIn(b'firekula.site:3000', z.read('.dflegacy/v4/installed.json'))

    def test_pin_detects_same_length_mutation_before_copy(self):
        inputs = self.inputs()
        source = Path(inputs[0]['source'])
        source.write_bytes(source.read_bytes()[:-1] + b'!')
        with self.assertRaisesRegex(v4.ReleaseError, 'hash|identity|changed'):
            self.build(inputs=inputs)
        self.assertFalse((self.root / 'release').exists())

    def test_every_executable_has_its_expected_architecture_including_x86_game_patch(self):
        for target in ('DFLegacy.Launcher.exe', 'DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe', 'DFLegacy.Client.dll', 'DFLegacy.Diagnostics.exe', 'DFLegacy.CrashCapture.exe'):
            inputs = self.inputs('x64')
            row = next(row for row in inputs if row['path'] == target)
            bad = pe('x64' if target in ('DFLegacy.Client.dll', 'DFLegacy.CrashCapture.exe') else 'x86')
            Path(row['source']).write_bytes(bad)
            row.update(sha256=hashlib.sha256(bad).hexdigest(), size=len(bad))
            with self.subTest(target=target), self.assertRaisesRegex(v4.ReleaseError, 'architecture'):
                self.build(inputs=inputs, architecture='x64')

    def test_malformed_pe_does_not_pass_on_machine_bytes_alone(self):
        for data in (b'MZ', b'not a PE', b'MZ' + bytes(510)):
            inputs = self.inputs()
            Path(inputs[0]['source']).write_bytes(data)
            inputs[0].update(sha256=hashlib.sha256(data).hexdigest(), size=len(data))
            with self.subTest(data=data[:8]), self.assertRaises(v4.ReleaseError):
                self.build(inputs=inputs)

    def test_exact_selection_rejects_duplicates_case_aliases_and_path_collisions(self):
        inputs = self.inputs()
        for name in ('DFLegacy.Launcher.exe', 'dflegacy.launcher.EXE', 'UpdateTestAssets', '../private.bin', 'bad/*.dll'):
            with self.subTest(name=name), self.assertRaises(v4.ReleaseError):
                self.build(inputs=inputs + [dict(inputs[0], path=name)])

    def test_secrets_configs_and_recovery_inputs_are_rejected_not_silently_packaged(self):
        inputs = self.inputs()
        for name in ('launcher.json', 'launcher.txt', 'nested/launcher.json.backup-1', '.dflegacy/bootstrap/recovery.ini',
                     'cache/a.bin', 'Diagnostics/session.json', 'nested/sessions/session.json', 'captures/crash.dmp',
                     'nested/preference.json', 'queue/body.json', 'nested/closed.json', 'accounts.json', 'nested/private.key', 'DFLegacy.Bootstrap.ini'):
            with self.subTest(name=name), self.assertRaises(v4.ReleaseError):
                self.build(inputs=inputs + [dict(inputs[0], path=name)])
        # Source names themselves cannot launder a private cache into a public destination.
        private = self.root / 'cache' / 'stolen.exe'
        private.parent.mkdir(); private.write_bytes(Path(inputs[0]['source']).read_bytes())
        inputs[0]['source'] = str(private)
        with self.assertRaises(v4.ReleaseError):
            self.build(inputs=inputs)

    def test_unselected_sibling_private_files_never_enter_source_or_receipt(self):
        inputs = self.inputs()
        (self.built / 'launcher.json').write_bytes(b'PRIVATE password')
        result = self.build(inputs=inputs)
        self.assertNotIn('launcher.json', {p.name for p in Path(result['sourceRoot']).rglob('*')})
        archive = self.starter(result)
        with zipfile.ZipFile(archive) as z:
            self.assertFalse(any(b'PRIVATE password' in z.read(n) for n in z.namelist()))

    def test_literal_resource_names_are_not_interpreted_as_include_patterns(self):
        inputs = self.inputs()
        public = self.built / 'resource[1].bin'
        public.write_bytes(b'public literal filename')
        inputs.append(dict(path='Resources/resource[1].bin', source=str(public),
                           sha256=hashlib.sha256(public.read_bytes()).hexdigest(), size=public.stat().st_size))
        result = self.build(inputs=inputs)
        with zipfile.ZipFile(self.starter(result)) as archive:
            self.assertEqual(archive.read('Resources/resource[1].bin'), b'public literal filename')

    def test_source_is_pinned_and_mismatch_is_rejected_before_writes(self):
        for change in (dict(provider='github'), dict(repository='other/repo'), dict(channel='stable'),
                       dict(origin='https://firekula.site:3000'), dict(architecture='arm64')):
            with self.subTest(change=change), self.assertRaises(v4.ReleaseError):
                self.build(inputs=self.inputs(), **change)
            self.assertFalse((self.root / 'release').exists())

    def test_existing_output_is_never_replaced(self):
        result = self.build()
        path = Path(result['sourceRoot']) / 'DFLegacy.Launcher.exe'
        before = path.read_bytes()
        with self.assertRaises(v4.ReleaseError): self.build()
        self.assertEqual(path.read_bytes(), before)
        archive = self.starter(result)
        before = archive.read_bytes()
        with self.assertRaises(v4.ReleaseError): self.starter(result)
        self.assertEqual(archive.read_bytes(), before)

    def test_history_preserves_previously_managed_resources_and_stable_entry(self):
        first = self.build()
        inputs = self.inputs()
        options = dict(version='v0.1.8-giteatest.4', sequence=4, previous=[first['manifest']])
        with self.assertRaisesRegex(v4.ReleaseError, 'omitted|missing'):
            self.build('omitted', inputs=[r for r in inputs if not r['path'].endswith('probe.pvf')], **options)
        second = self.build('second', inputs=inputs, **options)
        bp.assert_stable_entry(first['manifest'], second['manifest'])
        self.assertEqual(next(f for f in second['manifest']['files'] if f['path'] == 'DFLegacy.Launcher.exe')['parts'][0]['release'],
                         'v0.1.8-giteatest.3')
        changed = copy.deepcopy(second['manifest'])
        next(f for f in changed['files'] if f['path'] == 'DFLegacy.Launcher.exe')['sha256'] = '0' * 64
        with self.assertRaisesRegex(v4.ReleaseError, 'entry'):
            bp.assert_stable_entry(first['manifest'], changed)
        row = inputs[0]
        content = pe('x86', b'changed-entry')
        Path(row['source']).write_bytes(content)
        row.update(sha256=hashlib.sha256(content).hexdigest(), size=len(content))
        with self.assertRaisesRegex(v4.ReleaseError, 'entry'):
            self.build('changed', inputs=inputs, **options)

    def test_legacy_v4_history_can_migrate_without_bootstrap_entry_equality(self):
        source = self.root / 'legacy'; source.mkdir()
        for row in self.inputs():
            if row['path'] in ('DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe'): continue
            target = source / row['path']; target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(row['source'], target)
        (source / 'DFLegacy.Launcher.exe').write_bytes(pe('x86', b'legacy-managed-entry'))
        old = v4.build_manifest(root=source, version='v0.1.8-giteatest.2', commit='b' * 40,
                               architecture='x86', flavor='normal', channel='gitea-test', sequence=2, includes=['**'])
        self.build(previous=[old])

    def test_starter_contains_valid_manifest_receipt_and_only_generated_recovery(self):
        result = self.build()
        archive = self.starter(result)
        with zipfile.ZipFile(archive) as z:
            names = set(z.namelist())
            rows = {r['path']: r for r in result['manifest']['files']}
            recovery = '.dflegacy/bootstrap/' + rows['DFLegacy.Updater.exe']['sha256'] + '.exe'
            self.assertEqual(names, set(rows) | {'.dflegacy/v4/installed.json', '.dflegacy/bootstrap/recovery.ini', recovery})
            self.assertEqual(z.read(recovery), z.read('DFLegacy.Updater.exe'))
            self.assertEqual(z.read('.dflegacy/bootstrap/recovery.ini'), z.read('DFLegacy.Bootstrap.ini'))
            receipt = json.loads(z.read('.dflegacy/v4/installed.json'))
            raw = base64.b64decode(receipt['release']['manifestBytes'], validate=True)
            self.assertEqual(json.loads(raw), result['manifest'])
            self.assertEqual(hashlib.sha256(raw).hexdigest(), receipt['release']['manifestSha256'])
            self.assertEqual(receipt['release']['source'], dict(provider='gitea', repository='firekula/DFLegacy',
                             channel='gitea-test', architecture='x86', flavor='normal'))
            self.assertEqual(set(receipt['managedHistory']), set(rows))
            v4.validate_manifest(json.loads(raw))
            client = self.root / 'existing client'; client.mkdir()
            (client / 'launcher.json').write_bytes(b'{broken but preserved account data')
            (client / 'launcher.txt').write_bytes(b'legacy user settings')
            z.extractall(client)
            self.assertEqual((client / 'launcher.json').read_bytes(), b'{broken but preserved account data')
            self.assertEqual((client / 'launcher.txt').read_bytes(), b'legacy user settings')

    def test_starter_rejects_source_architecture_flavor_and_mutated_bundle(self):
        result = self.build()
        for changes in (dict(architecture='x64'), dict(flavor='test'), dict(provider='github'),
                        dict(channel='wrong'), dict(repository='other/repo'), dict(origin='http://evil.example')):
            with self.subTest(changes=changes), self.assertRaises(v4.ReleaseError):
                self.starter(result, **changes)
            self.assertFalse((self.root / 'starter.zip').exists())
        manifest = Path(result['bundleRoot']) / result['manifestAsset']
        manifest.write_bytes(manifest.read_bytes() + b' ')
        with self.assertRaises(v4.ReleaseError): self.starter(result)

    def test_starter_rejects_changed_or_extra_files_including_user_caches(self):
        result = self.build()
        root = Path(result['sourceRoot'])
        for relative in ('launcher.json', '.dflegacy/bootstrap/recovery.ini', 'unselected.dat'):
            extra = root / relative; extra.parent.mkdir(parents=True, exist_ok=True); extra.write_bytes(b'private')
            with self.subTest(relative=relative), self.assertRaises(v4.ReleaseError): self.starter(result)
            extra.unlink()
            if relative.startswith('.dflegacy/'):
                extra.parent.rmdir(); extra.parent.parent.rmdir()
        target = root / 'DFLegacy.Updater.exe'
        target.write_bytes(target.read_bytes()[:-1] + b'?')
        with self.assertRaises(v4.ReleaseError): self.starter(result)

    def test_legacy_schema2_validator_still_rejects_bootstrap_files(self):
        result = self.build()
        root = Path(result['sourceRoot'])
        rows = [r for r in result['manifest']['files'] if r['path'] in ('DFLegacy.Launcher.exe', 'DFLegacy.Client.dll')]
        manifest = dict(schemaVersion=2, version='v0.1.8-giteatest.3', commit='a' * 40, flavor='normal',
                        files=[{k: row[k] for k in ('path', 'sha256', 'size')} for row in rows])
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as z:
            for row in rows: z.write(root / row['path'], row['path'])
            z.writestr('update-manifest.json', json.dumps(manifest))
            z.write(root / 'DFLegacy.Updater.exe', 'DFLegacy.Updater.exe')
        with self.assertRaises(legacy.ReleaseError): legacy.verify_client_package(stream.getvalue())

    @unittest.skipUnless(shutil.which('dotnet'), 'requires .NET SDK for real receipt contract')
    def test_real_update_core_accepts_generated_receipt_and_detects_corrupt_file(self):
        result = self.build()
        client = self.root / 'C# receipt fixture'
        with zipfile.ZipFile(self.starter(result)) as z: z.extractall(client)
        probe = self.root / 'probe'; probe.mkdir()
        core = (REPO / 'src/DFLegacy.UpdateCore').as_posix()
        (probe / 'Probe.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include="''' + core + '''/*.cs" />
</ItemGroup></Project>''', encoding='utf-8')
        (probe / 'Program.cs').write_text('''using DFLegacy.UpdateCore;
var root = args[0];
var installed = new InstallationStore(root).ReadInstalled() ?? throw new Exception("receipt missing");
installed.Release.Validate(new ReleaseSource("gitea", "firekula/DFLegacy", "gitea-test", "x86", "normal"));
if (!(await FileVerifier.VerifyAsync(root, installed.Release.Manifest)).IsValid) throw new Exception("initial invalid");
File.AppendAllText(Path.Combine(root, "DFLegacy.Launcher.App.exe"), "corrupt");
if ((await FileVerifier.VerifyAsync(root, installed.Release.Manifest)).IsValid) throw new Exception("missed corruption");
Console.WriteLine("actual InstallationStore and FileVerifier accepted receipt and detected corruption");
''', encoding='utf-8')
        run = subprocess.run(['dotnet', 'run', '--project', str(probe / 'Probe.csproj'), '--', str(client)],
                             cwd=probe, capture_output=True, text=True, timeout=120)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        self.assertIn('accepted receipt and detected corruption', run.stdout)


if __name__ == '__main__': unittest.main()
