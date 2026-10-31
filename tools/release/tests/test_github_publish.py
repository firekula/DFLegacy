"""Dedicated client update packaging and release-only GitHub publication contract."""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).parents[1]))
import release
import github_publish as github
import test_release as fixtures
COMMIT, VERSION = fixtures.COMMIT, fixtures.VERSION


class UpdatePackageTests(unittest.TestCase):
    # Reuse only the fixture setup, not the generic release test cases.
    setUp = fixtures.Bundles.setUp
    assets = fixtures.Bundles.assets
    def test_exact_root_whitelist_digests_and_reproducible_archive(self):
        assets = self.assets()
        archive = release.build_client_update(assets, self.root / 'out', VERSION, COMMIT)
        with zipfile.ZipFile(archive) as z:
            self.assertEqual(z.namelist(), ['DFLegacy.Launcher.exe', 'DFLegacy.Client.dll', 'update-manifest.json'])
            manifest = json.loads(z.read('update-manifest.json'))
            self.assertEqual(set(manifest), {'schemaVersion', 'version', 'commit', 'flavor', 'files'})
            self.assertEqual((manifest['schemaVersion'], manifest['version'], manifest['commit']), (2, VERSION, COMMIT))
            for row in manifest['files']:
                data = z.read(row['path'])
                self.assertEqual(set(row), {'path', 'sha256', 'size'})
                self.assertEqual(row['sha256'], hashlib.sha256(data).hexdigest())
                self.assertEqual(row['size'], len(data))
            self.assertNotIn(b'PRIVATE', b''.join(z.read(n) for n in z.namelist()))
        second = release.build_client_update(assets, self.root / 'out2', VERSION, COMMIT)
        self.assertEqual(archive.name, github.ASSET_NAME)
        self.assertEqual(archive.read_bytes(), second.read_bytes())
        release.verify_client_update(archive, VERSION, COMMIT)

    def test_four_public_flavors_have_exact_inventory_and_paired_debug(self):
        assets = self.assets()
        packages = release.build_client_packages(assets, self.root / 'public', VERSION, COMMIT)
        self.assertEqual({p.name for p in packages}, {
            'DFLegacy-client-update-v2-win-x64.zip', 'DFLegacy-client-update-v2-test-win-x64.zip',
            'DFLegacy-client-tools-win-x64.zip', 'DFLegacy-client-tools-test-win-x64.zip'})
        for package in packages:
            data = release.verify_client_package(package, VERSION, COMMIT, package.name)
            test = '-test-' in package.name
            self.assertEqual(data['flavor'], 'test' if test else 'normal')
            with zipfile.ZipFile(package) as z:
                binaries = {'DFLegacy.Launcher.exe', 'DFLegacy.Client.dll'} | ({'debug.dll'} if test else set())
                metadata = {'client-package.json', 'SHA256SUMS', 'README.txt'} if '-tools-' in package.name else {'update-manifest.json'}
                self.assertEqual(set(z.namelist()), binaries | metadata)
                for info in z.infolist(): self.assertEqual(info.external_attr >> 16, 0o100644)
                self.assertEqual({r['path'] for r in data['files']}, binaries)
                if test: self.assertEqual(z.read('debug.dll'), (assets / 'patch/debug.dll').read_bytes())
        self.assertFalse((self.root / 'public/DFLegacy-client-update-win-x64.zip').exists())

    def test_tools_reject_private_configuration_changed_instructions_and_checksums(self):
        assets = self.assets()
        package = release.build_client_update(assets, self.root / 'out', VERSION, COMMIT, tools=True)
        original = package.read_bytes()
        for name in ('Config.ini', 'launcher.json', 'README.txt', 'SHA256SUMS'):
            with zipfile.ZipFile(io.BytesIO(original)) as z:
                contents = {entry: z.read(entry) for entry in z.namelist()}
            contents[name] = b'private or altered content'
            with zipfile.ZipFile(package, 'w') as z:
                for entry, content in contents.items(): z.writestr(entry, content)
            with self.subTest(name=name), self.assertRaises(release.ReleaseError):
                release.verify_client_package(package, VERSION, COMMIT, package.name)

    def test_manifest_types_links_and_limits_are_enforced(self):
        assets = self.assets()
        original = release.build_client_update(assets, self.root / 'out', VERSION, COMMIT)
        with zipfile.ZipFile(original) as source:
            contents = {name: source.read(name) for name in source.namelist()}
        mutations = ('schema', 'missing-size', 'bool-size', 'hash', 'link', 'case-duplicate-json')
        for index, mutation in enumerate(mutations):
            archive = self.root / f'bad-{index}.zip'
            manifest = json.loads(contents['update-manifest.json'])
            if mutation == 'schema': manifest['schemaVersion'] = True
            if mutation == 'missing-size': del manifest['files'][0]['size']
            if mutation == 'bool-size': manifest['files'][0]['size'] = True
            if mutation == 'hash': manifest['files'][0]['sha256'] = 'f' * 64
            with zipfile.ZipFile(archive, 'w') as target:
                for name, data in contents.items():
                    if name == 'update-manifest.json':
                        data = json.dumps(manifest).encode()
                        if mutation == 'case-duplicate-json': data = data.replace(b'{', b'{"SchemaVersion":1,', 1)
                    info = zipfile.ZipInfo(name)
                    if mutation == 'link' and name == 'DFLegacy.Client.dll': info.external_attr = 0o120777 << 16
                    target.writestr(info, data)
            with self.subTest(mutation=mutation), self.assertRaises(release.ReleaseError):
                release.verify_client_update(archive)
        for limit in ('MAX_UPDATE_ZIP', 'MAX_UPDATE_EXPANDED'):
            with patch.object(release, limit, 1), self.assertRaises(release.ReleaseError):
                release.verify_client_update(original)

    def test_modified_source_or_mixed_identity_never_packages(self):
        assets = self.assets()
        with self.assertRaises(release.ReleaseError):
            release.build_client_update(assets, self.root / 'bad', VERSION, 'b' * 40)
        (assets / 'patch/DFLegacy.Client.dll').write_bytes(b'changed')
        with self.assertRaises(release.ReleaseError):
            release.build_client_update(assets, self.root / 'bad2', VERSION, COMMIT)

    def test_source_changed_after_verification_cannot_reanchor_new_bytes(self):
        assets = self.assets()
        original = release.verify_assets
        def changed(*args):
            verified = original(*args)
            (assets / 'patch/DFLegacy.Client.dll').write_bytes(b'changed after verification')
            return verified
        with patch.object(release, 'verify_assets', side_effect=changed):
            with self.assertRaises(release.ReleaseError):
                release.build_client_update(assets, self.root / 'out', VERSION, COMMIT)

    def test_extra_duplicate_or_modified_entries_are_rejected(self):
        assets = self.assets()
        archive = release.build_client_update(assets, self.root / 'out', VERSION, COMMIT)
        for name, data in [('launcher.json', b'{"Password":"secret"}'), ('DFLegacy.Client.dll', b'changed'), ('../Config.ini', b'x')]:
            altered = self.root / (str(len(name)) + '.zip')
            altered.write_bytes(archive.read_bytes())
            if name == 'DFLegacy.Client.dll':
                with self.assertWarnsRegex(UserWarning, "Duplicate name"):
                    with zipfile.ZipFile(altered, 'a') as z:
                        z.writestr(name, data)
            else:
                with zipfile.ZipFile(altered, 'a') as z:
                    z.writestr(name, data)
            with self.assertRaises(release.ReleaseError):
                release.verify_client_update(altered, VERSION, COMMIT)

    def test_publisher_uploads_only_validated_snapshot_after_path_mutation(self):
        assets = self.assets()
        packages = release.build_client_packages(assets, self.root / 'out', VERSION, COMMIT)
        original_bytes = {p.name: p.read_bytes() for p in packages}
        original_verifier = release.verify_client_package
        for mutation in ('append', 'replace'):
            with self.subTest(mutation=mutation):
                for package in packages: package.write_bytes(original_bytes[package.name])
                def verify_then_mutate(content, version, commit, name):
                    result = original_verifier(content, version, commit, name)
                    archive = next(p for p in packages if p.name == name)
                    if mutation == 'replace':
                        replacement = self.root / 'replacement.zip'
                        with zipfile.ZipFile(replacement, 'w') as z:
                            z.writestr('launcher.json', b'synthetic-private-marker')
                        replacement.replace(archive)
                    else:
                        with zipfile.ZipFile(archive, 'a') as z:
                            z.writestr('launcher.json', b'synthetic-private-marker')
                    return result
                api = FakeGitHub()
                with patch.object(github, 'verify_client_package', side_effect=verify_then_mutate):
                    outcome = github.publish(api, VERSION, COMMIT, packages)
                self.assertEqual(outcome['status'], 'published')
                self.assertEqual(api.contents, original_bytes)
                for package in packages:
                    self.assertEqual(outcome['assets'][package.name]['sha256'], hashlib.sha256(original_bytes[package.name]).hexdigest())
                    with self.assertRaises(release.ReleaseError):
                        original_verifier(package, VERSION, COMMIT, package.name)

    def test_public_set_rejects_mixed_shared_binaries_before_remote_calls(self):
        assets = self.assets()
        packages = release.build_client_packages(assets, self.root / 'out', VERSION, COMMIT)
        package = packages[0]
        with zipfile.ZipFile(package) as z:
            contents = {name: z.read(name) for name in z.namelist()}
        contents['DFLegacy.Client.dll'] = b'other independently valid patch'
        manifest = json.loads(contents['update-manifest.json'])
        row = next(row for row in manifest['files'] if row['path'] == 'DFLegacy.Client.dll')
        row['size'] = len(contents[row['path']]); row['sha256'] = hashlib.sha256(contents[row['path']]).hexdigest()
        contents['update-manifest.json'] = json.dumps(manifest).encode()
        with zipfile.ZipFile(package, 'w') as z:
            for name, content in contents.items(): z.writestr(name, content)
        release.verify_client_package(package, VERSION, COMMIT, package.name)
        api = FakeGitHub()
        with self.assertRaises(release.ReleaseError): github.publish(api, VERSION, COMMIT, packages)
        self.assertEqual(api.calls, [])

    def test_real_client_parser_accepts_all_four_produced_archives(self):
        assets = self.assets()
        packages = release.build_client_packages(assets, self.root / 'out', VERSION, COMMIT)
        project = Path(__file__).parent / 'package-probe/PackageProbe.csproj'
        result = subprocess.run(['dotnet', 'run', '--project', str(project), '-c', 'Release',
                                 '--artifacts-path', str(self.root / 'probe-build'), '--', VERSION, COMMIT, *map(str, packages)],
                                capture_output=True, text=True, env=release.dotnet_environment())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(result.stdout.count('actual UpdatePackage parser accepted'), 2)
        self.assertEqual(result.stdout.count('actual ClientPackageManifest parser accepted'), 2)


class FakeGitHub:
    def __init__(self, empty=False):
        self.current = None
        self.assets = []
        self.contents = {}
        self.calls = []
        self.empty = empty
        self.corrupt = False
        self.upload_timeout = False
        self.hide_drafts_by_tag = False
        self.target = 'b' * 40  # Public history is deliberately unrelated to Gitea.

    def request(self, method, path, data=None, **kwargs):
        self.calls.append((method, path, copy.deepcopy(data)))
        if path == '': return {'private': False, 'default_branch': 'main', 'permissions': {'push': True}}
        if path == '/commits?per_page=1': return None if self.empty else [{'sha': self.target}]
        if path == '/contents/README.md' and method == 'PUT':
            self.empty = False
            return {'commit': {'sha': self.target}}
        if path.startswith('/git/ref/heads/'):
            return None if self.empty else {'object': {'sha': self.target}}
        if path.startswith('/releases/tags/'):
            return None if self.hide_drafts_by_tag and self.current and self.current['draft'] else copy.deepcopy(self.current)
        if path.startswith('/releases?'): return [copy.deepcopy(self.current)] if self.current else []
        if '/assets?' in path and method == 'GET': return copy.deepcopy(self.assets)
        if path == '/releases' and method == 'POST':
            self.current = dict(data, id=12, html_url='https://github.com/firekula/DFLegacy/releases/tag/' + data['tag_name'])
            return copy.deepcopy(self.current)
        if path == '/releases/12' and method == 'PATCH':
            self.current.update(data)
            return copy.deepcopy(self.current)
        raise AssertionError((method, path))

    def upload(self, release_id, name, content):
        self.calls.append(('UPLOAD', name, None))
        self.assets.append({'id': 7 + len(self.assets), 'name': name, 'size': len(content), 'digest': 'sha256:' + hashlib.sha256(content).hexdigest(),
                            'browser_download_url': 'https://github.com/firekula/DFLegacy/releases/download/' + self.current['tag_name'] + '/' + name})
        self.content = content
        self.contents[name] = content
        if self.upload_timeout: raise release.ReleaseError('lost upload response')

    def attachment_digest(self, asset, size, draft=False):
        self.calls.append(('VERIFY-DRAFT' if draft else 'VERIFY-PUBLIC', str(asset['id']), None))
        return '0' * 64 if self.corrupt else hashlib.sha256(self.contents[asset['name']]).hexdigest()


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name) / github.ASSET_NAME
        self.path.write_bytes(b'archive-fixture')
        self.paths = [Path(self.tmp.name) / name for name in (
            'DFLegacy-client-update-v2-win-x64.zip', 'DFLegacy-client-update-v2-test-win-x64.zip',
            'DFLegacy-client-tools-win-x64.zip', 'DFLegacy-client-tools-test-win-x64.zip')]
        for path in self.paths: path.write_bytes(b'archive-fixture')
        self.verifier = patch.object(github, 'verify_client_package', return_value={'files': []})
        self.verifier.start()
        self.addCleanup(self.verifier.stop)

    def publish(self, api, version=VERSION, **kwargs):
        return github.publish(api, version, COMMIT, self.paths, **kwargs)

    def test_partial_published_release_is_never_complete(self):
        api = FakeGitHub(); self.publish(api)
        api.assets.pop()
        api.calls.clear()
        with self.assertRaises(release.ReleaseError): self.publish(api)
        self.assertFalse(any(c[0] in ('UPLOAD', 'PATCH', 'POST', 'PUT') for c in api.calls))

    def test_partial_draft_resumes_only_missing_assets(self):
        api = FakeGitHub(); self.publish(api)
        api.current['draft'] = True
        missing = api.assets.pop()['name']
        api.calls.clear()
        self.assertEqual(self.publish(api)['status'], 'published')
        self.assertEqual([c[1] for c in api.calls if c[0] == 'UPLOAD'], [missing])

    def test_partial_local_inventory_rejected_before_remote_access(self):
        api = FakeGitHub()
        with self.assertRaises(release.ReleaseError): github.publish(api, VERSION, COMMIT, self.paths[:-1])
        self.assertEqual(api.calls, [])

    def test_draft_verify_publish_public_verify_and_idempotent_retry(self):
        api = FakeGitHub()
        self.assertEqual(self.publish(api)['status'], 'published')
        names = [c[0] for c in api.calls]
        self.assertLess(names.index('VERIFY-DRAFT'), names.index('PATCH'))
        self.assertGreater(names.index('VERIFY-PUBLIC'), names.index('PATCH'))
        self.assertFalse(api.current['prerelease'])
        self.assertEqual(api.current['target_commitish'], api.target)
        self.assertIn(COMMIT, api.current['body'])
        api.calls.clear()
        self.assertEqual(self.publish(api)['status'], 'already-published')
        self.assertTrue(all(c[0] in ('GET', 'VERIFY-PUBLIC') for c in api.calls))

    def test_dev_is_prerelease_and_empty_repo_only_bootstraps_readme(self):
        api = FakeGitHub(empty=True)
        self.publish(api, 'dev-20261031T010203Z-' + COMMIT[:12])
        self.assertTrue(api.current['prerelease'])
        writes = [c for c in api.calls if c[0] == 'PUT']
        self.assertEqual(len(writes), 1)
        self.assertEqual(writes[0][1], '/contents/README.md')
        self.assertNotIn('sha', writes[0][2])

    def test_readonly_absent_and_missing_credentials_are_explicit(self):
        api = FakeGitHub(empty=True)
        self.assertEqual(self.publish(api, check_only=True)['status'], 'absent')
        self.assertTrue(all(c[0] == 'GET' for c in api.calls))
        with patch.dict(os.environ, {'GITHUB_TOKEN': 'gitea-token'}, clear=True), patch.object(github.os, 'name', 'posix'):
            with self.assertRaisesRegex(release.ReleaseError, 'DFLEGACY_GITHUB_RELEASE_TOKEN'):
                github.credentials()

    def test_resume_after_lost_upload_response(self):
        api = FakeGitHub(); api.upload_timeout = True
        self.assertEqual(self.publish(api)['status'], 'published')
        self.assertEqual(len(api.assets), 4)

    def test_draft_lookup_uses_authenticated_listing_when_tag_endpoint_is_published_only(self):
        api = FakeGitHub(); api.hide_drafts_by_tag = True
        self.assertEqual(self.publish(api)['status'], 'published')
        self.assertTrue(any(call[1].startswith('/releases?') for call in api.calls))

    def test_mismatched_bytes_identity_prerelease_and_extra_asset_refused(self):
        for mutation in ('bytes', 'commit', 'prerelease', 'extra', 'digest'):
            with self.subTest(mutation=mutation):
                api = FakeGitHub(); self.publish(api)
                if mutation == 'bytes': self.path.write_bytes(b'other')
                if mutation == 'commit': api.current['body'] = api.current['body'].replace(COMMIT, 'c' * 40)
                if mutation == 'prerelease': api.current['prerelease'] = True
                if mutation == 'extra': api.assets.append(dict(api.assets[0], id=8, name='private.zip'))
                if mutation == 'digest': api.assets[0]['digest'] = None
                api.calls.clear()
                with self.assertRaises(release.ReleaseError): self.publish(api)
                self.assertFalse(any(c[0] in ('UPLOAD', 'PATCH', 'POST', 'PUT') for c in api.calls))
                self.path.write_bytes(b'archive-fixture')

    def test_corrupt_download_leaves_draft(self):
        api = FakeGitHub(); api.corrupt = True
        with self.assertRaises(release.ReleaseError): self.publish(api)
        self.assertTrue(api.current['draft'])
        self.assertFalse(any(c[0] == 'PATCH' for c in api.calls))

    def test_concurrent_publication_still_checks_anonymous_bytes(self):
        api = FakeGitHub()
        self.publish(api)
        api.current['draft'] = True
        api.calls.clear()
        original = api.attachment_digest
        def concurrent(asset, size, draft=False):
            result = original(asset, size, draft)
            if draft: api.current['draft'] = False
            return result
        api.attachment_digest = concurrent
        self.publish(api)
        self.assertIn('VERIFY-PUBLIC', [call[0] for call in api.calls])

    def test_failed_upload_resumes_draft_and_published_retry_is_read_only(self):
        api = FakeGitHub()
        original = api.upload
        with patch.object(api, 'upload', side_effect=release.ReleaseError('network failure')):
            with self.assertRaises(release.ReleaseError): self.publish(api)
        self.assertTrue(api.current['draft'])
        api.upload = original
        self.assertEqual(self.publish(api)['status'], 'published')
        api.calls.clear()
        self.assertEqual(self.publish(api, check_only=True)['status'], 'already-published')
        self.assertTrue(all(call[0] in ('GET', 'VERIFY-PUBLIC') for call in api.calls))

    def test_lost_create_and_publish_responses_are_reconciled(self):
        api = FakeGitHub()
        original = api.request
        def lost_response(method, path, *args, **kwargs):
            result = original(method, path, *args, **kwargs)
            if method in ('POST', 'PATCH'): raise release.ReleaseError('response lost')
            return result
        api.request = lost_response
        self.assertEqual(self.publish(api)['status'], 'published')
        self.assertEqual(len(api.assets), 4)

    def test_missing_permission_and_private_repo_cannot_mutate(self):
        for repository in ({'private': True, 'permissions': {'push': True}}, {'private': False, 'permissions': {'push': False}}):
            api = FakeGitHub()
            original = api.request
            api.request = lambda method, path, *args, **kwargs: repository if path == '' else original(method, path, *args, **kwargs)
            with self.assertRaises(release.ReleaseError): self.publish(api)
            self.assertEqual(api.calls, [])


class Response(io.BytesIO):
    def __init__(self, content, status=200, location=None):
        super().__init__(content); self.status = status; self.headers = {'Location': location}


class TransportTests(unittest.TestCase):
    def test_credentials_are_in_memory_noninteractive_and_gitea_token_ignored(self):
        completed = subprocess.CompletedProcess([], 0, 'protocol=https\nhost=github.com\nusername=user\npassword=secret\n', '')
        with patch.dict(os.environ, {'GITHUB_TOKEN': 'wrong'}, clear=True), patch.object(github.os, 'name', 'nt'), patch.object(github.subprocess, 'run', return_value=completed) as run:
            self.assertEqual(github.credentials(), ('secret', 'git-credential-manager'))
            args, kwargs = run.call_args
            self.assertNotIn('secret', str(args))
            self.assertEqual(kwargs['env']['GIT_TERMINAL_PROMPT'], '0')
            self.assertEqual(kwargs['env']['GCM_INTERACTIVE'], 'Never')
            self.assertIn('credential.interactive=never', args[0])

    def test_transport_whitelists_exactly_the_four_public_names(self):
        api = github.GitHub('secret')
        for name in release.PUBLIC_ASSETS:
            api._validate_api('https://uploads.github.com/repos/firekula/DFLegacy/releases/1/assets?name=' + name)
            api._validate_download('https://github.com/firekula/DFLegacy/releases/download/v1.2.3/' + name)
        for name in ('DFLegacy-client-update-win-x64.zip', 'private.zip', '../' + release.UPDATE_ASSET):
            with self.assertRaises(release.ReleaseError): api._validate_api('https://uploads.github.com/repos/firekula/DFLegacy/releases/1/assets?name=' + name)
            with self.assertRaises(release.ReleaseError): api._validate_download('https://github.com/firekula/DFLegacy/releases/download/v1.2.3/' + name)

    def test_api_and_upload_host_path_allowlist_before_auth(self):
        api = github.GitHub('secret')
        for url in ['https://evil.test/repos/firekula/DFLegacy/releases', 'https://api.github.com/repos/other/repo/releases',
                    'https://uploads.github.com/repos/firekula/DFLegacy/contents/a', 'http://api.github.com/repos/firekula/DFLegacy/releases',
                    'https://api.github.com:444/repos/firekula/DFLegacy/releases', 'https://api.github.com/repos/firekula/DFLegacy/../other']:
            with self.assertRaises(release.ReleaseError): api._open(url, 'GET')

    def test_public_redirects_never_receive_auth_and_untrusted_redirect_refused(self):
        api = github.GitHub('secret')
        requests = []
        def opening(req, **kwargs):
            requests.append(req)
            return Response(b'', 302, 'https://release-assets.githubusercontent.com/path?sig=signed') if len(requests) == 1 else Response(b'abc')
        asset = {'id': 7, 'size': 3, 'browser_download_url': 'https://github.com/firekula/DFLegacy/releases/download/v1.2.3/' + github.ASSET_NAME}
        with patch.object(api.opener, 'open', side_effect=opening):
            self.assertEqual(api.attachment_digest(asset, 3), hashlib.sha256(b'abc').hexdigest())
        self.assertTrue(all(not r.has_header('Authorization') for r in requests))
        with patch.object(api.opener, 'open', return_value=Response(b'', 302, 'https://evil.test/steal')):
            with self.assertRaises(release.ReleaseError): api.attachment_digest(asset, 3)

    def test_draft_auth_only_api_first_hop_and_errors_redacted(self):
        api = github.GitHub('secret'); requests = []
        def opening(req, **kwargs):
            requests.append(req)
            return Response(b'', 302, 'https://release-assets.githubusercontent.com/draft') if len(requests) == 1 else Response(b'abc')
        with patch.object(api.opener, 'open', side_effect=opening):
            api.attachment_digest({'id': 7, 'size': 3}, 3, draft=True)
        self.assertEqual(requests[0].get_header('Authorization'), 'Bearer secret')
        self.assertFalse(requests[1].has_header('Authorization'))
        with patch.object(api.opener, 'open', side_effect=urllib.error.URLError('secret')):
            with self.assertRaises(release.ReleaseError) as error: api.request('GET', '')
        self.assertNotIn('secret', str(error.exception))
        self.assertTrue(error.exception.__suppress_context__)

    def test_authenticated_api_refuses_redirect_and_draft_refuses_wrong_hosts(self):
        api = github.GitHub('secret')
        with patch.object(api.opener, 'open', return_value=Response(b'', 302, 'https://evil.test/')) as opening:
            with self.assertRaises(release.ReleaseError): api.request('GET', '')
            self.assertEqual(opening.call_count, 1)
        for destination in ('https://api.github.com/repos/other/repo/releases/assets/7',
                            'https://uploads.github.com/repos/firekula/DFLegacy/releases/7/assets',
                            'https://github.com/other/repo/releases/download/v1.2.3/' + github.ASSET_NAME,
                            'https://release-assets.githubusercontent.com.evil.test/path'):
            with patch.object(api.opener, 'open', return_value=Response(b'', 302, destination)) as opening:
                with self.assertRaises(release.ReleaseError): api.attachment_digest({'id': 7, 'size': 3}, 3, draft=True)
                self.assertEqual(opening.call_count, 1)

    def test_draft_direct_200_hashes_bytes_at_exact_api_path(self):
        api = github.GitHub('secret')
        with patch.object(api.opener, 'open', return_value=Response(b'abc')) as opening:
            self.assertEqual(api.attachment_digest({'id': 7, 'size': 3}, 3, draft=True), hashlib.sha256(b'abc').hexdigest())
        request = opening.call_args.args[0]
        self.assertEqual(request.full_url, 'https://api.github.com/repos/firekula/DFLegacy/releases/assets/7')
        self.assertEqual(request.get_header('Accept'), 'application/octet-stream')

    def test_dedicated_token_does_not_run_gcm_and_failed_gcm_never_exposes_output(self):
        with patch.dict(os.environ, {github.TOKEN_ENV: 'dedicated', 'GITHUB_TOKEN': 'gitea'}, clear=True), patch.object(github.subprocess, 'run') as run:
            self.assertEqual(github.credentials(), ('dedicated', github.TOKEN_ENV))
            run.assert_not_called()
        with patch.dict(os.environ, {}, clear=True), patch.object(github.os, 'name', 'nt'), patch.object(github.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1, 'password=secret', 'secret')):
            with self.assertRaises(release.ReleaseError) as error: github.credentials()
            self.assertNotIn('secret', str(error.exception))

    def test_truncated_or_oversize_download_is_refused(self):
        api = github.GitHub('secret')
        for content in (b'a', b'abcd'):
            with patch.object(api.opener, 'open', return_value=Response(content)):
                with self.assertRaises(release.ReleaseError): api.attachment_digest({'id': 7, 'size': 3}, 3, draft=True)


class WorkflowTests(unittest.TestCase):
    def test_github_windows_job_after_every_existing_gate(self):
        text = (release.REPO / '.gitea/workflows/release.yaml').read_text()
        job = text.split('\n  publish-github:\n')[1]
        self.assertIn('needs: [windows, test-linux, linux-packages-and-image, publish]', job)
        self.assertIn('runs-on: dflegacy-windows-x64', job)
        self.assertIn('shell: pwsh', job)
        self.assertIn('persist-credentials: false', job)
        self.assertIn('github_publish.py', job)
        self.assertNotIn('secrets.GITHUB_TOKEN', job)
        self.assertNotIn('git push', job)
        self.assertNotIn('DFLegacy-client-update-win-x64.zip', text)
        for name in release.PUBLIC_ASSETS: self.assertIn(name, text)
        self.assertIn('build_client_packages', Path(release.REPO / 'tools/release/ci.py').read_text())
        self.assertNotIn('incoming/windows/DFLegacy-*.zip', text,
                         'the update ZIP must not be mistaken for a sixth Gitea platform package')


if __name__ == '__main__': unittest.main()
