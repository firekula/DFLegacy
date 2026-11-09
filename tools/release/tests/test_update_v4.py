"""V4 release contracts exercised with actual files and an HTTP provider boundary."""
import copy
import hashlib
import http.server
import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import threading
import unittest
import urllib.parse
import email.parser
import email.policy

MODULE = pathlib.Path(__file__).parents[1] / 'update_v4.py'
spec = importlib.util.spec_from_file_location('update_v4', MODULE)
v4 = importlib.util.module_from_spec(spec) if MODULE.exists() else None
if v4 is not None:
    spec.loader.exec_module(v4)


class FileFixture(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(v4, 'V4 publisher engine must exist')
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name)
        self.source = self.root / 'source'
        self.source.mkdir()

    def put(self, name, data=b'payload'):
        p = self.source / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        return p

    def options(self, **kwargs):
        return dict(root=str(self.source), version='v4-test-1', commit='a' * 40,
                    architecture='x86', flavor='test', channel='gitee-test', sequence=1,
                    minimum_sequence=0, includes=['**'], **kwargs)

class Resources(FileFixture):
    def test_nested_arbitrary_resources_and_private_runtime_exclusions(self):
        for name in ['DNF.exe', 'data/deep/map.npk', 'web/index.html', 'empty.bin']:
            self.put(name, b'' if name == 'empty.bin' else b'abc')
        for name in ['launcher.json', 'Config.ini', 'Logs/crash.log', '.git/config',
                     'Save/a.dat', 'data/.env', 'data/private.key', '.dflegacy/cache/a', 'nested/launcher.json',
                     'collections/11111111111111111111111111111111.json',
                     'nested/copied/collections/22222222222222222222222222222222.json']:
            self.put(name, b'PRIVATE')
        preview = v4.plan_resources(**self.options())
        self.assertEqual([x['path'] for x in preview['manifest']['files']],
                         ['data/deep/map.npk', 'DNF.exe', 'empty.bin', 'web/index.html'])
        self.assertEqual(len(preview['excluded']), 11)
        self.assertNotIn('PRIVATE', json.dumps(preview))
        self.assertEqual(preview['manifest']['files'][2]['parts'][0]['size'], 0)

    def test_selection_must_be_explicit_and_exclude_rules_win(self):
        self.put('a.bin'); self.put('nested/b.bin')
        opts = self.options(); opts['includes'] = []
        with self.assertRaises(v4.ReleaseError): v4.plan_resources(**opts)
        opts['includes'] = ['**']; opts['excludes'] = ['nested/**']
        self.assertEqual([x['path'] for x in v4.build_manifest(**opts)['files']], ['a.bin'])

    def test_parts_are_48_mib_and_empty_has_one_real_asset(self):
        large = self.put('nested/large.npk', b'')
        with large.open('wb') as f:
            for _ in range(49): f.write(b'x' * 1024 * 1024)
        self.put('zero.dat', b'')
        receipt = v4.build(output=self.root / 'build', provider='gitee', repository='owner/repo', **self.options())
        manifest = receipt['manifest']
        self.assertEqual([p['size'] for p in manifest['files'][0]['parts']], [50331648, 1048576])
        self.assertEqual(manifest['files'][1]['sha256'], hashlib.sha256(b'').hexdigest())
        self.assertEqual(v4.verify_local(self.root / 'build')['manifest'], manifest)
        blob = self.root / 'build' / 'assets' / manifest['files'][0]['parts'][0]['asset']
        with blob.open('r+b') as f: f.write(b'y')
        with self.assertRaises(v4.ReleaseError): v4.verify_local(self.root / 'build')

    def test_reuse_cumulative_retirements_and_migrations_allow_skipping(self):
        self.put('old.dll'); self.put('keep.npk', b'unchanged')
        a = v4.build_manifest(**self.options())
        (self.source / 'old.dll').unlink()
        options = self.options(previous=[a]); options.update(version='v4-test-2', sequence=2)
        with self.assertRaisesRegex(v4.ReleaseError, 'retire'): v4.build_manifest(**options)
        migration = dict(id='ports-b', file='launcher.json', operation='unionPorts', key='Relay.TcpPorts', value=[7110, 7111])
        options.update(retired=[dict(path='old.dll', action='delete', sequence=2)], migrations=[migration])
        b = v4.build_manifest(**options)
        options.update(previous=[b], version='v4-test-3', sequence=3, retired=[], migrations=[])
        c = v4.plan_resources(**options)
        self.assertEqual(c['manifest']['retired'], [dict(path='old.dll', action='delete', sequence=2)])
        self.assertEqual(c['manifest']['migrations'], [migration])
        self.assertEqual(c['manifest']['files'][0]['parts'][0]['release'], 'v4-test-1')
        self.assertEqual(c['uploadBytes'], 0)
        self.assertEqual(c['reusedBytes'], 9)
        options['migrations'] = [dict(migration, value=[1234])]
        with self.assertRaises(v4.ReleaseError): v4.build_manifest(**options)

    def test_variants_and_metadata_cannot_silently_cross(self):
        self.put('app.exe')
        for arch in ('x86', 'x64'):
            for flavor in ('normal', 'test'):
                opts=self.options(); opts.update(architecture=arch, flavor=flavor)
                m=v4.build_manifest(**opts)
                self.assertEqual((m['architecture'], m['flavor']), (arch, flavor))
                self.assertEqual(v4.manifest_name(m), f'DFLegacy-client-update-v4-gitee-test-{flavor}-win-{arch}.json')
        a=v4.build_manifest(**self.options()); opts=self.options(previous=[a]); opts.update(architecture='x64', sequence=2, version='v4-test-2')
        with self.assertRaises(v4.ReleaseError): v4.build_manifest(**opts)
        for changes in [dict(version='../v1'), dict(commit='bad'), dict(sequence=True), dict(minimum_sequence=2)]:
            opts=self.options(); opts.update(changes)
            with self.assertRaises(v4.ReleaseError): v4.build_manifest(**opts)

    def test_provider_limit_boundaries_count_manifest_and_existing_gitee_storage(self):
        v4.preflight_limits('github', [1]*1000)
        for provider,sizes,existing in [('github',[1]*1001,0), ('github',[2147483648],0), ('gitee',[100000001],0), ('gitee',[1],1000000000)]:
            with self.assertRaises(v4.ReleaseError): v4.preflight_limits(provider,sizes,existing)
        v4.preflight_limits('github',[2147483647])
        v4.preflight_limits('gitee',[100000000],900000000)

    def test_protected_and_windows_aliases_are_rejected_in_manifests(self):
        self.put('good.bin'); original=v4.build_manifest(**self.options())
        for name in ['../bad', '/bad', 'C:/bad', 'a:ads', 'CON.txt', 'COM¹.bin', 'a./b', 'a\\b', 'Logs/a', 'launcher.json']:
            bad=copy.deepcopy(original); bad['files'][0]['path']=name
            with self.assertRaises(v4.ReleaseError, msg=name): v4.validate_manifest(bad)
        bad=copy.deepcopy(original); bad['files'].append(dict(bad['files'][0], path='GOOD.bin'))
        with self.assertRaises(v4.ReleaseError): v4.validate_manifest(bad)
        bad=copy.deepcopy(original); bad['files'].append(dict(bad['files'][0], path='good.bin/child'))
        with self.assertRaises(v4.ReleaseError): v4.validate_manifest(bad)

    def test_windows_short_name_aliases_cannot_bypass_protected_paths(self):
        self.put('good.bin'); original=v4.build_manifest(**self.options())
        for alias in ['LAUNCH~1.JSO','IMAGEP~1/a.npk','config~2.ini','nested/ACCOUN~1/data.bin']:
            manifest=copy.deepcopy(original); manifest['files'][0]['path']=alias
            with self.assertRaises(v4.ReleaseError,msg=alias): v4.validate_manifest(manifest)

    def test_cli_preview_build_verify_share_json_contract(self):
        self.put('nested/test.pvf')
        request = self.options(); request.update(provider='gitee', repository='owner/repo')
        config=self.root/'request.json'; config.write_text(json.dumps(request),encoding='utf-8')
        def run(*args):
            p=subprocess.run([sys.executable,str(MODULE),*args],capture_output=True,text=True)
            self.assertEqual(p.returncode,0,p.stderr)
            return json.loads(p.stdout)
        preview=run('preview','--request',str(config))
        built=run('build','--request',str(config),'--output',str(self.root/'out'))
        verified=run('verify','--bundle',str(self.root/'out'))
        self.assertEqual(preview['manifest'],built['manifest'])
        self.assertEqual(built['manifest'],verified['manifest'])

    def test_unmanage_and_cross_arch_reuse_keep_full_target_and_explicit_history(self):
        self.put('keep.npk'); self.put('optional.bin')
        a=v4.build_manifest(**self.options())
        (self.source/'optional.bin').unlink()
        opts=self.options(previous=[a]); opts.update(version='v4-test-2',sequence=2,retired=[dict(path='optional.bin',action='unmanage',sequence=2)])
        b=v4.build_manifest(**opts)
        self.assertEqual(b['retired'][0]['action'],'unmanage')
        opts=self.options(reuse=[b]); opts.update(version='v4-test-x64',architecture='x64')
        planned=v4.plan_resources(**opts)
        self.assertEqual(planned['uploadBytes'],0)
        self.assertEqual(planned['manifest']['files'][0]['parts'][0]['release'],'v4-test-1')

    def test_symlink_selection_and_nested_private_state_fail_closed(self):
        self.put('good.npk'); self.put('nested/server.json',b'PASSWORD'); self.put('nested/.ssh/id_rsa',b'SECRET')
        planned=v4.plan_resources(**self.options())
        self.assertEqual([x['path'] for x in planned['manifest']['files']],['good.npk'])
        link=self.source/'linked'; target=self.root/'external'; target.mkdir()
        try: link.symlink_to(target,target_is_directory=True)
        except OSError:
            if os.name != 'nt': raise
            result=subprocess.run(['powershell','-NoProfile','-NonInteractive','-Command',
                'New-Item -ItemType Junction -Path $env:V4_TEST_LINK -Target $env:V4_TEST_TARGET | Out-Null'],
                env=os.environ|{'V4_TEST_LINK':str(link),'V4_TEST_TARGET':str(target)},capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr)
        with self.assertRaises(v4.ReleaseError): v4.plan_resources(**self.options())

    def test_invalid_migrations_do_not_generate_client_executable_operations(self):
        self.put('good.bin')
        for migration in [dict(id='bad',file='launcher.json',operation='unionPorts',key='Relay.TcpPorts',value=[0,65536]),
                          dict(id='bad',file='launcher.json',operation='replaceIf',key='Relay.Enabled',value=True),
                          dict(id='bad',file='launcher.json',operation='setDefault',key='Password',value='secret'),
                          dict(id='bad',file='launcher.json',operation='script',key='Relay.Enabled',value=True)]:
            with self.assertRaises(v4.ReleaseError): v4.plan_resources(**self.options(migrations=[migration]))

    def test_port_replace_condition_accepts_legacy_string_and_typed_array(self):
        self.put('good.bin')
        for condition in ('2311; 7001,8080',[2311,7001,8080]):
            row=dict(id='convert',file='launcher.json',operation='replaceIf',key='Relay.TcpPorts',value=[2311,7110],when=condition)
            self.assertEqual(v4.build_manifest(**self.options(migrations=[row]))['migrations'],[row])
        row=dict(id='preference',file='launcher.json',operation='replaceIf',key='Relay.Preference',value='IPv6',when='IPv4')
        self.assertEqual(v4.build_manifest(**self.options(migrations=[row]))['migrations'],[row])

    def test_deletion_guard_lists_every_manifest_still_referencing_release(self):
        self.put('keep.npk'); a=v4.build_manifest(**self.options())
        opts=self.options(previous=[a]); opts.update(version='v4-test-2',sequence=2)
        b=v4.build_manifest(**opts)
        self.assertEqual(v4.release_references('v4-test-1',[a,b]),['v4-test-1','v4-test-2'])
        with self.assertRaisesRegex(v4.ReleaseError,'referenced'): v4.ensure_release_unreferenced('v4-test-1',[a,b])

    def test_build_cannot_scan_its_own_output_or_create_an_unpublishable_manifest_name(self):
        self.put('good.bin')
        with self.assertRaisesRegex(v4.ReleaseError,'outside'): v4.build(output=self.source/'out',provider='gitee',repository='owner/repo',**self.options())
        self.assertFalse((self.source/'out').exists())
        opts=self.options(); opts['channel']='x'*128
        with self.assertRaises(v4.ReleaseError): v4.build(output=self.root/'bad-name',provider='github',repository='owner/repo',**opts)


class ApiHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_): pass

    def reply(self, value, status=200):
        data=json.dumps(value).encode()
        self.send_response(status); self.send_header('Content-Type','application/json')
        self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)

    def do_GET(self):
        state=self.server.state; path=urllib.parse.urlsplit(self.path).path
        state['requests'].append(('GET',path,self.headers.get('Authorization'),self.headers.get('Range')))
        if path.endswith('/releases'):
            return self.reply(list(state['releases'].values()))
        if '/releases/tags/' in path:
            tag=path.rsplit('/',1)[1]; release=state['releases'].get(tag)
            return self.reply(release or {},200 if release else 404)
        if path.endswith(('/assets','/attach_files')):
            release_id=int(path.split('/')[-2]); release=next(x for x in state['releases'].values() if x['id']==release_id)
            return self.reply(release['assets'])
        if path.endswith('/download') or '/download/' in path or '/releases/assets/' in path:
            if path.endswith('/download'): asset_id=int(path.split('/')[-2]); name=state['ids'][asset_id]
            elif '/releases/assets/' in path: name=state['ids'][int(path.rsplit('/',1)[1])]
            else: name=path.rsplit('/',1)[1]
            data=state['data'][name]
            if state.get('html'): data=b'<html>sign in</html>'
            start=0
            if self.headers.get('Range'): start=int(self.headers['Range'].split('=')[1].split('-')[0])
            if start and state.get('corrupt_range'): data=data[:start]+bytes([data[start]^1])+data[start+1:]
            self.send_response(206 if start else 200)
            self.send_header('Content-Type','text/html' if state.get('html') else 'application/octet-stream')
            if start: self.send_header('Content-Range',f'bytes {start}-{len(data)-1}/{len(data)}')
            self.send_header('Content-Length',str(len(data)-start)); self.end_headers(); self.wfile.write(data[start:]); return
        self.reply(dict(private=state.get('private',False),permissions=dict(push=True),default_branch='main'))

    def do_POST(self):
        state=self.server.state; path=urllib.parse.urlsplit(self.path).path
        state['requests'].append(('POST',path,self.headers.get('Authorization'),None))
        raw=self.rfile.read(int(self.headers.get('Content-Length','0')))
        if path.endswith('/releases'):
            row=json.loads(raw); row.update(id=len(state['releases'])+1,assets=[])
            state['releases'][row['tag_name']]=row; return self.reply(row,201)
        release_id=int(path.split('/')[-2]); release=next(x for x in state['releases'].values() if x['id']==release_id)
        if path.endswith('/attach_files'):
            message=email.parser.BytesParser(policy=email.policy.default).parsebytes(b'Content-Type: '+self.headers['Content-Type'].encode()+b'\r\nMIME-Version: 1.0\r\n\r\n'+raw)
            part=next(p for p in message.iter_parts() if p.get_param('name',header='content-disposition')=='file')
            name=part.get_filename(); data=part.get_payload(decode=True)
        else: name=urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)['name'][0]; data=raw
        state['uploads'].append(name)
        if state.get('fail_asset') and name.endswith('.bin'): return self.reply({},503)
        if state.get('fail_manifest') and name.endswith('.json'): return self.reply({},503)
        asset_id=len(state['ids'])+1; state['ids'][asset_id]=name; state['data'][name]=data
        asset=dict(id=asset_id,name=name,size=len(data),digest='sha256:'+hashlib.sha256(data).hexdigest(),
                   browser_download_url=state['base']+f'/download/{release["tag_name"]}/{name}')
        release['assets'].append(asset); return self.reply(asset,201)

    def do_PATCH(self):
        state=self.server.state; raw=self.rfile.read(int(self.headers['Content-Length']))
        state['requests'].append(('PATCH',self.path,self.headers.get('Authorization'),None))
        release=next(x for x in state['releases'].values() if x['id']==int(self.path.rsplit('/',1)[1]))
        release.update(json.loads(raw)); state['visible_uploads']=list(state['uploads']); self.reply(release)


class Publishing(FileFixture):
    def setUp(self):
        super().setUp()
        self.server=http.server.ThreadingHTTPServer(('127.0.0.1',0),ApiHandler)
        self.server.state=dict(releases={},data={},ids={},uploads=[],requests=[],base=f'http://127.0.0.1:{self.server.server_port}')
        thread=threading.Thread(target=self.server.serve_forever,daemon=True); thread.start()
        self.addCleanup(self.server.server_close); self.addCleanup(self.server.shutdown)

    def bundle(self, provider):
        self.put('app.exe', b'client fixture bytes')
        v4.build(output=self.root/'bundle',provider=provider,repository='owner/repo',**self.options())
        return self.root/'bundle'

    def api(self, provider):
        return v4.ReleaseApi(provider,'owner/repo',token='PUBLISH-SECRET',api_root=self.server.state['base'])

    def test_gitee_uploads_manifest_last_and_verifies_without_credentials(self):
        bundle=self.bundle('gitee'); result=v4.publish(bundle,self.api('gitee'),target_commitish='b'*40)
        state=self.server.state
        self.assertEqual(result['status'],'published-verified')
        self.assertTrue(state['uploads'][-1].endswith('.json'))
        downloads=[r for r in state['requests'] if r[1].endswith('/download') or '/download/' in r[1]]
        self.assertTrue(downloads); self.assertTrue(all(r[2] is None for r in downloads))
        self.assertTrue(any(r[3] is not None for r in downloads))
        self.assertNotIn('PUBLISH-SECRET',json.dumps(result))
        self.assertNotIn('PUBLISH-SECRET',(bundle/'receipt.json').read_text())

    def test_github_draft_is_opened_only_after_all_attachments(self):
        result=v4.publish(self.bundle('github'),self.api('github'),target_commitish='b'*40)
        state=self.server.state
        self.assertEqual(result['status'],'published-verified')
        self.assertEqual(state['visible_uploads'],state['uploads'])
        self.assertFalse(state['releases']['v4-test-1']['draft'])
        self.assertTrue(state['visible_uploads'][-1].endswith('.json'))

    def test_failed_part_upload_never_uploads_manifest(self):
        bundle=self.bundle('gitee'); self.server.state['fail_asset']=True
        with self.assertRaises(v4.ReleaseError): v4.publish(bundle,self.api('gitee'),target_commitish='b'*40)
        self.assertFalse(any(x.endswith('.json') for x in self.server.state['uploads']))

    def test_existing_release_is_immutable_even_if_payload_matches(self):
        bundle=self.bundle('gitee'); api=self.api('gitee')
        v4.publish(bundle,api,target_commitish='b'*40)
        mutations=len([r for r in self.server.state['requests'] if r[0]!='GET'])
        with self.assertRaisesRegex(v4.ReleaseError,'immutable'): v4.publish(bundle,api,target_commitish='b'*40)
        self.assertEqual(mutations,len([r for r in self.server.state['requests'] if r[0]!='GET']))

    def test_anonymous_verification_rejects_html_and_foreign_repository(self):
        bundle=self.bundle('gitee'); api=self.api('gitee'); v4.publish(bundle,api,target_commitish='b'*40)
        self.server.state['html']=True
        with self.assertRaises(v4.ReleaseError): v4.verify_remote(bundle,api)
        self.server.state['html']=False
        with self.assertRaises(v4.ReleaseError): v4.verify_remote(bundle,v4.ReleaseApi('gitee','other/repo',api_root=self.server.state['base']))

    def test_gitee_complete_existing_storage_blocks_before_mutation(self):
        bundle=self.bundle('gitee')
        self.server.state['releases']['old']=dict(id=20,tag_name='old',assets=[dict(id=9,name='old.bin',size=999999999)])
        with self.assertRaisesRegex(v4.ReleaseError,'1 GB'): v4.publish(bundle,self.api('gitee'),target_commitish='b'*40)
        self.assertFalse(any(r[0]!='GET' for r in self.server.state['requests']))

    def test_private_repository_rejected_before_creating_release(self):
        bundle=self.bundle('gitee'); self.server.state['private']=True
        with self.assertRaisesRegex(v4.ReleaseError,'public'): v4.publish(bundle,self.api('gitee'),target_commitish='b'*40)
        self.assertFalse(any(r[0]!='GET' for r in self.server.state['requests']))

    def test_range_probe_checks_bytes_against_full_download(self):
        bundle=self.bundle('gitee'); api=self.api('gitee'); v4.publish(bundle,api,target_commitish='b'*40)
        self.server.state['corrupt_range']=True
        with self.assertRaisesRegex(v4.ReleaseError,'Range'): v4.verify_remote(bundle,api)

    def test_real_gitee_redirect_shape_and_untrusted_hosts(self):
        api=v4.ReleaseApi('gitee','owner/repo')
        api.validate_redirect('https://foruda.gitee.com/attach_file/1773140874168858731/dflegacy-v4-abcd.bin')
        api.validate_redirect('https://foruda.gitee.com/attach_file/1773140874168858731/dflegacy-v4-abcd.bin?attname=dflegacy-v4-abcd.bin&token=temporary-signature&ts=1780000000')
        api.validate_redirect('https://gitee.com/owner/repo/attach_files/123/download/dflegacy-v4-abcd.bin')
        for url in ['http://foruda.gitee.com/attach_file/123/a.bin', 'https://foruda.gitee.com.evil.test/attach_file/123/a.bin',
                    'https://foruda.gitee.com/avatar/123/a.bin', 'https://gitee.com/other/repo/attach_files/123/download/a.bin',
                    'https://foruda.gitee.com/attach_file/123/a.bin?redirect=https://evil.test',
                    'https://foruda.gitee.com/attach_file/123/%2e%2e%2fsecret', 'https://foruda.gitee.com@evil.test/attach_file/123/a.bin']:
            with self.assertRaises(v4.ReleaseError,msg=url): api.validate_redirect(url)

    def test_unchanged_second_release_uploads_only_new_manifest(self):
        first=self.bundle('gitee'); api=self.api('gitee'); v4.publish(first,api,target_commitish='b'*40)
        previous=v4.verify_local(first)['manifest']; opts=self.options(previous=[previous]); opts.update(version='v4-test-2',sequence=2)
        v4.build(output=self.root/'second',provider='gitee',repository='owner/repo',**opts)
        count=len(self.server.state['uploads'])
        result=v4.publish(self.root/'second',api,target_commitish='b'*40)
        self.assertEqual(self.server.state['uploads'][count:],[v4.manifest_name(previous)])
        self.assertEqual({x['release'] for x in result['checks']},{'v4-test-1','v4-test-2'})

    def grouped_bundles(self,provider='gitee'):
        self.put('shared.npk',b'shared across every variant')
        bundles=[]
        for arch in ('x86','x64'):
            for flavor in ('normal','test'):
                self.put('app.exe',('launcher-'+arch+'-'+flavor).encode())
                opts=self.options(); opts.update(architecture=arch,flavor=flavor)
                bundle=self.root/(arch+'-'+flavor)
                v4.build(output=bundle,provider=provider,repository='owner/repo',**opts)
                bundles.append(bundle)
        return bundles

    def test_grouped_four_variants_share_tag_upload_resources_once_and_manifests_last(self):
        bundles=self.grouped_bundles()
        result=v4.publish_many(bundles,self.api('gitee'),target_commitish='b'*40)
        uploads=self.server.state['uploads']
        self.assertEqual(len(self.server.state['releases']),1)
        self.assertEqual(len([x for x in uploads if x.endswith('.bin')]),5)
        self.assertEqual(len(uploads),9)
        self.assertTrue(all(x.endswith('.json') for x in uploads[-4:]))
        self.assertTrue(all(x.endswith('.bin') for x in uploads[:-4]))
        self.assertEqual(len(result['bundles']),4)
        self.assertEqual({x['releaseId'] for x in result['bundles']},{1})

    def test_grouped_github_draft_is_visible_after_all_four_manifests(self):
        bundles=self.grouped_bundles('github')
        v4.publish_many(bundles,self.api('github'),target_commitish='b'*40)
        self.assertEqual(self.server.state['visible_uploads'],self.server.state['uploads'])
        self.assertEqual(len([x for x in self.server.state['visible_uploads'] if x.endswith('.json')]),4)

    def test_grouped_duplicate_or_mismatched_identity_rejected_before_mutation(self):
        bundles=self.grouped_bundles()
        for invalid in [[],[bundles[0],bundles[0]]]:
            with self.assertRaises(v4.ReleaseError): v4.publish_many(invalid,self.api('gitee'),target_commitish='b'*40)
        opts=self.options(); opts.update(version='v4-other',sequence=2,architecture='x64')
        wrong=self.root/'wrong'; v4.build(output=wrong,provider='gitee',repository='owner/repo',**opts)
        with self.assertRaises(v4.ReleaseError): v4.publish_many([bundles[0],wrong],self.api('gitee'),target_commitish='b'*40)
        command=subprocess.run([sys.executable,str(MODULE),'publish','--bundle',str(bundles[0]),'--bundle',str(wrong),
                                '--target-commitish','b'*40],capture_output=True,text=True)
        self.assertEqual(command.returncode,2)
        self.assertIn('grouped bundle release identity differs',command.stderr)
        self.assertFalse(any(r[0]!='GET' for r in self.server.state['requests']))

    def test_grouped_reused_attachment_cannot_have_conflicting_hashes(self):
        self.put('app.exe',b'first')
        prior=v4.build_manifest(**self.options()); prior['files'][0]['parts'][0]['asset']='shared.bin'
        opts=self.options(previous=[prior]); opts.update(version='v4-test-2',sequence=2)
        first=self.root/'first'; v4.build(output=first,provider='gitee',repository='owner/repo',**opts)
        self.put('app.exe',b'other')
        prior=v4.build_manifest(**self.options()); prior['architecture']='x64'; prior['files'][0]['parts'][0]['asset']='shared.bin'
        opts=self.options(previous=[prior]); opts.update(version='v4-test-2',sequence=2,architecture='x64')
        second=self.root/'second'; v4.build(output=second,provider='gitee',repository='owner/repo',**opts)
        with self.assertRaisesRegex(v4.ReleaseError,'conflicting grouped resource'):
            v4.publish_many([first,second],self.api('gitee'),target_commitish='b'*40)
        self.assertEqual(self.server.state['requests'],[])

    def test_grouped_limit_is_counted_across_unique_assets_and_all_manifests(self):
        self.put('first.bin',b'a')
        first=self.root/'first'; v4.build(output=first,provider='github',repository='owner/repo',**self.options())
        opts=self.options(); opts.update(architecture='x64')
        # Each variant individually fits, but their combined release exceeds 1000.
        for i in range(500): self.put(f'{i}.bin',str(i).encode())
        second=self.root/'second'; v4.build(output=second,provider='github',repository='owner/repo',**opts)
        for i in range(500): self.put(f'{i}.bin',('other-'+str(i)).encode())
        third=self.root/'third'; opts['flavor']='normal'; v4.build(output=third,provider='github',repository='owner/repo',**opts)
        with self.assertRaisesRegex(v4.ReleaseError,'1000'): v4.publish_many([first,second,third],self.api('github'),target_commitish='b'*40)
        self.assertFalse(any(r[0]!='GET' for r in self.server.state['requests']))

    def test_grouped_failed_manifest_keeps_resources_without_deleting_or_overwriting(self):
        bundles=self.grouped_bundles(); self.server.state['fail_manifest']=True
        with self.assertRaises(v4.ReleaseError): v4.publish_many(bundles,self.api('gitee'),target_commitish='b'*40)
        self.assertEqual(len([x for x in self.server.state['data'] if x.endswith('.bin')]),5)
        self.assertEqual(len(self.server.state['releases']),1)
        self.assertFalse(any(r[0] in ('DELETE','PATCH') for r in self.server.state['requests']))
        self.server.state['fail_manifest']=False
        with self.assertRaisesRegex(v4.ReleaseError,'immutable'): v4.publish_many(bundles,self.api('gitee'),target_commitish='b'*40)


if __name__ == '__main__': unittest.main()
