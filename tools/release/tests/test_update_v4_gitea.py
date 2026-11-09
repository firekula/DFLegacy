"""Gitea 1.26 wire contracts and immutable, source-bound draft publication."""
import copy
import email.parser
import email.policy
import hashlib
import http.server
import json
import os
import threading
import unittest
import urllib.parse
from unittest.mock import patch

from test_update_v4 import FileFixture, v4


ORIGIN = 'http://firekula.site:3000'
REPOSITORY = 'firekula/DFLegacy'
UUID = '01234567-89ab-4cde-8fab-0123456789ab'


class GiteaHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_): pass

    def reply(self, value, status=200):
        data=json.dumps(value).encode()
        self.send_response(status); self.send_header('Content-Type','application/json')
        self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)

    def record(self):
        self.server.state['requests'].append((self.command,self.path,self.headers.get('Authorization')))

    def do_GET(self):
        self.record(); state=self.server.state; parsed=urllib.parse.urlsplit(self.path); path=parsed.path
        prefix='/api/v1/repos/firekula/DFLegacy'
        if path==prefix: return self.reply(dict(private=False,permissions=dict(push=True)))
        if path==prefix+'/releases' or path.endswith('/assets'):
            query=urllib.parse.parse_qs(parsed.query)
            if query.get('limit')!=['50'] or 'per_page' in query: return self.reply({},400)
            page=int(query['page'][0])
            if path.endswith('/releases'): rows=list(state['releases'].values())
            else:
                release=next(r for r in state['releases'].values() if r['id']==int(path.split('/')[-2]))
                if release['draft'] and not self.headers.get('Authorization'): return self.reply({},404)
                rows=release['assets']
            return self.reply(rows[(page-1)*50:page*50])
        if path.startswith(prefix+'/releases/tags/'):
            row=state['releases'].get(path.rsplit('/',1)[1])
            if row is None or row['draft'] and not self.headers.get('Authorization'): return self.reply({},404)
            return self.reply(row)
        matches=[(r,a) for r in state['releases'].values() for a in r['assets']
                 if path in ('/firekula/DFLegacy/releases/download/'+r['tag_name']+'/'+a['name'], '/attachments/'+a['uuid'])]
        if not matches: return self.reply({},404)
        release,asset=matches[0]
        if release['draft'] and not self.headers.get('Authorization'): return self.reply({},404)
        if state.get('redirect') and not path.startswith('/attachments/'):
            self.send_response(302); self.send_header('Location',state['redirect'].replace('{uuid}',asset['uuid'])); self.end_headers(); return
        data=state['data'][asset['name']]
        start=1 if self.headers.get('Range')=='bytes=1-' else 0
        self.send_response(206 if start else 200); self.send_header('Content-Type','application/octet-stream')
        if start: self.send_header('Content-Range',f'bytes 1-{len(data)-1}/{len(data)}')
        self.send_header('Content-Length',str(len(data)-start)); self.end_headers(); self.wfile.write(data[start:])

    def do_POST(self):
        self.record(); state=self.server.state; parsed=urllib.parse.urlsplit(self.path)
        raw=self.rfile.read(int(self.headers['Content-Length']))
        if parsed.path=='/api/v1/repos/firekula/DFLegacy/releases':
            row=json.loads(raw); row.update(id=len(state['releases'])+1,assets=[])
            if state.get('ignore_draft'): row['draft']=False
            state['releases'][row['tag_name']]=row; return self.reply(row,201)
        if not parsed.path.endswith('/assets'): return self.reply({},404)
        message=email.parser.BytesParser(policy=email.policy.default).parsebytes(
            b'Content-Type: '+self.headers['Content-Type'].encode()+b'\r\nMIME-Version: 1.0\r\n\r\n'+raw)
        parts=list(message.iter_parts()) if message.is_multipart() else []
        if len(parts)!=1 or parts[0].get_param('name',header='content-disposition')!='attachment': return self.reply({},400)
        part=parts[0]; name=part.get_filename(); data=part.get_payload(decode=True)
        if urllib.parse.parse_qs(parsed.query).get('name')!=[name]: return self.reply({},400)
        release=next(r for r in state['releases'].values() if r['id']==int(parsed.path.split('/')[-2]))
        state['uploads'].append(name)
        if state.get('fail_manifest') and name.endswith('.json'): return self.reply({},503)
        asset_id=len(state['data'])+1; identity=f'01234567-89ab-4cde-8fab-{asset_id:012x}'
        state['data'][name]=data
        asset=dict(id=asset_id,name=name,size=len(data),uuid=identity,
                   browser_download_url=state['base']+'/firekula/DFLegacy/releases/download/'+release['tag_name']+'/'+name)
        release['assets'].append(asset); self.reply(asset,201)

    def do_PATCH(self):
        self.record(); state=self.server.state
        row=next(r for r in state['releases'].values() if r['id']==int(self.path.rsplit('/',1)[1]))
        row.update(json.loads(self.rfile.read(int(self.headers['Content-Length']))))
        state['visible_uploads']=list(state['uploads']); self.reply(row)


class GiteaContracts(FileFixture):
    def test_fixed_origin_and_source_cannot_select_another_repository_or_channel(self):
        self.put('app.exe'); opts=self.options(); opts['channel']='gitea-test'
        api=v4.ReleaseApi('gitea',REPOSITORY)
        self.assertEqual(api.root,ORIGIN+'/api/v1')
        receipt=v4.build(output=self.root/'good',provider='gitea',repository=REPOSITORY,**opts)
        self.assertEqual(v4.verify_local(self.root/'good')['provider'],'gitea')
        self.assertNotIn('apiRoot',receipt)
        for repo in ('owner/repo','firekula/DFLegacy-extra'):
            with self.assertRaises(v4.ReleaseError): v4.ReleaseApi('gitea',repo)
        for channel in ('stable','gitee-test'):
            wrong=copy.deepcopy(receipt['manifest']); wrong['channel']=channel
            with self.assertRaises(v4.ReleaseError): v4.source_identity('gitea',REPOSITORY,wrong)
        with self.assertRaises(v4.ReleaseError): v4.ReleaseApi('gitea',REPOSITORY,api_root='http://evil.test')

    def test_attachment_cap_is_bounded_without_gitee_repository_quota(self):
        v4.preflight_limits('gitea',[50331648]*25,existing_bytes=2_000_000_000)
        v4.preflight_limits('gitea',[100_000_000])
        with self.assertRaises(v4.ReleaseError): v4.preflight_limits('gitea',[100_000_001])

    def test_download_and_redirect_are_bound_to_release_or_structured_uuid(self):
        api=v4.ReleaseApi('gitea',REPOSITORY)
        release=dict(id=1,tag_name='v0.1.8-giteatest.1')
        url=ORIGIN+'/firekula/DFLegacy/releases/download/v0.1.8-giteatest.1/a.bin'
        asset=dict(id=2,name='a.bin',size=5,uuid=UUID,browser_download_url=url)
        self.assertEqual(api.download_url(release,asset),url)
        self.assertEqual(api.download_url(release,asset|{'browser_download_url':ORIGIN+'/attachments/'+UUID}),ORIGIN+'/attachments/'+UUID)
        api.validate_redirect(ORIGIN+'/attachments/'+UUID,release,asset)
        bad=[url.replace('firekula.site','evil.test'),url.replace('/firekula/','/other/'),url.replace('giteatest.1','giteatest.2'),
             url.replace('a.bin','b.bin'),url+'?token=secret',url+'#fragment',url.replace('http://','http://user:secret@'),
             url.replace(':3000',':3001'),url.replace('/a.bin','/%61.bin'),ORIGIN+'/attachments/ffffffff-89ab-4cde-8fab-0123456789ab',
             ORIGIN+'/attachments/'+UUID+'/../secret',ORIGIN+'/api/v1/repos/firekula/DFLegacy']
        for value in bad:
            with self.subTest(url=value):
                with self.assertRaises(v4.ReleaseError): api.download_url(release,asset|{'browser_download_url':value})
                with self.assertRaises(v4.ReleaseError): api.validate_redirect(value,release,asset)
        with self.assertRaises(v4.ReleaseError): api.download_url(release,asset|{'uuid':'bad','browser_download_url':ORIGIN+'/attachments/bad'})

    def test_authentication_cannot_be_sent_to_other_provider_upload_hosts(self):
        api=v4.ReleaseApi('gitea',REPOSITORY,token='SECRET')
        with self.assertRaises(v4.ReleaseError):
            api._open('https://uploads.github.com/repos/firekula/DFLegacy/releases/1/assets',authenticated=True)

    def test_gitea_publish_does_not_reuse_gitee_credentials(self):
        self.put('app.exe'); opts=self.options(); opts['channel']='gitea-test'
        bundle=self.root/'bundle'; v4.build(output=bundle,provider='gitea',repository=REPOSITORY,**opts)
        with patch.dict(os.environ,{'DFLEGACY_GITEE_RELEASE_TOKEN':'WRONG'},clear=True):
            with self.assertRaisesRegex(v4.ReleaseError,'dedicated'): v4.publish(bundle,target_commitish='b'*40)


class GiteaPublishing(FileFixture):
    def setUp(self):
        super().setUp()
        self.server=http.server.ThreadingHTTPServer(('127.0.0.1',0),GiteaHandler)
        self.server.state=dict(releases={},data={},uploads=[],requests=[],base=f'http://127.0.0.1:{self.server.server_port}')
        threading.Thread(target=self.server.serve_forever,daemon=True).start()
        self.addCleanup(self.server.server_close); self.addCleanup(self.server.shutdown)
        self.api=v4.ReleaseApi('gitea',REPOSITORY,token='PUBLISH-SECRET',api_root=self.server.state['base'])

    def bundles(self):
        self.put('shared.npk',b'common bytes'); bundles=[]
        for architecture in ('x86','x64'):
            for flavor in ('normal','test'):
                self.put('app.exe',(architecture+flavor).encode())
                opts=self.options(); opts.update(architecture=architecture,flavor=flavor,channel='gitea-test')
                bundle=self.root/(architecture+flavor)
                v4.build(output=bundle,provider='gitea',repository=REPOSITORY,**opts); bundles.append(bundle)
        return bundles

    def test_four_variants_remain_draft_until_resources_and_manifests_verify_then_check_anonymously(self):
        result=v4.publish_many(self.bundles(),self.api,target_commitish='b'*40); state=self.server.state
        self.assertEqual(result['status'],'published-verified'); self.assertEqual(len(result['bundles']),4)
        self.assertEqual(len(state['uploads']),9); self.assertTrue(all(n.endswith('.json') for n in state['uploads'][-4:]))
        self.assertEqual(state['visible_uploads'],state['uploads'])
        self.assertTrue(state['releases']['v4-test-1']['prerelease']); self.assertFalse(state['releases']['v4-test-1']['draft'])
        patch_index=next(i for i,r in enumerate(state['requests']) if r[0]=='PATCH')
        before=[r for r in state['requests'][:patch_index] if '/releases/download/' in r[1]]
        after=[r for r in state['requests'][patch_index+1:] if '/releases/download/' in r[1]]
        self.assertEqual(len(before),9); self.assertTrue(all(r[2]=='token PUBLISH-SECRET' for r in before))
        self.assertTrue(after); self.assertTrue(all(r[2] is None for r in after))
        self.assertNotIn('PUBLISH-SECRET',json.dumps(result))

    def test_failed_manifest_stays_draft_and_second_attempt_cannot_overwrite(self):
        bundles=self.bundles(); self.server.state['fail_manifest']=True
        with self.assertRaises(v4.ReleaseError): v4.publish_many(bundles,self.api,target_commitish='b'*40)
        self.assertTrue(self.server.state['releases']['v4-test-1']['draft'])
        self.assertFalse(any(r[0] in ('DELETE','PATCH') for r in self.server.state['requests']))
        with self.assertRaisesRegex(v4.ReleaseError,'immutable'): v4.publish_many(bundles,self.api,target_commitish='b'*40)

    def test_server_ignoring_draft_does_not_receive_attachments(self):
        bundles=self.bundles(); self.server.state['ignore_draft']=True
        with self.assertRaisesRegex(v4.ReleaseError,'draft'): v4.publish_many(bundles,self.api,target_commitish='b'*40)
        self.assertEqual(self.server.state['uploads'],[])

    def test_limit_pagination_includes_second_page(self):
        for index in range(51): self.server.state['releases'][str(index)]=dict(id=index+1,tag_name=str(index),draft=False,assets=[])
        rows=self.api.listing('/releases',authenticated=True)
        self.assertEqual(len(rows),51)
        self.assertIn(('GET','/api/v1/repos/firekula/DFLegacy/releases?limit=50&page=2','token PUBLISH-SECRET'),self.server.state['requests'])

    def test_public_uuid_redirect_is_verified_and_external_redirect_never_requested(self):
        bundles=self.bundles(); v4.publish_many(bundles,self.api,target_commitish='b'*40)
        self.server.state['redirect']='/attachments/{uuid}'
        v4.verify_remote(bundles[0],self.api)
        redirected=[r for r in self.server.state['requests'] if r[1].startswith('/attachments/')]
        self.assertTrue(redirected); self.assertTrue(all(r[2] is None for r in redirected))
        self.server.state['redirect']='http://127.0.0.1:1/attachments/{uuid}'
        with self.assertRaises(v4.ReleaseError): v4.verify_remote(bundles[0],self.api)


if __name__=='__main__': unittest.main()
