"""GitCode V4 publisher contract with a loopback API and object storage."""
import hashlib
import http.server
import json
import threading
import urllib.parse
from unittest.mock import patch

from test_update_v4 import FileFixture, v4


class GitCodePublishing(FileFixture):
    def setUp(self):
        super().setUp()
        self.events = []
        self.objects = {}
        self.release = None
        self.signed_url = None
        self.download_redirect = None
        self.corrupt_download = False
        self.sign_headers = {
            'content-type': 'application/octet-stream',
            'x-obs-meta-project-id': '77',
            'x-obs-acl': 'private',
            'x-obs-callback': 'fixture-callback',
        }
        fixture = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def send_body(self, status, raw=b'', **headers):
                self.send_response(status)
                self.send_header('Content-Length', str(len(raw)))
                for key, value in headers.items():
                    self.send_header(key, value)
                self.end_headers()
                self.wfile.write(raw)

            def metadata(self, value, status=200):
                self.send_body(status, json.dumps(value).encode(), **{'Content-Type': 'application/json'})

            def do_GET(self):
                url = urllib.parse.urlsplit(self.path)
                query = urllib.parse.parse_qs(url.query)
                token = self.headers.get('Authorization')
                if 'access_token' in query or token not in (None, 'Bearer fixture-token'):
                    return self.send_body(400)
                prefix = '/api/v5/repos/firekula/DFLegacy'
                if url.path == prefix and token:
                    return self.metadata({'private': 0, 'public': 1})
                if url.path == prefix + '/releases' and token:
                    return self.metadata([fixture.release] if fixture.release else [])
                asset = query.get('file_name', [None])[0]
                tag = fixture.release['tag_name'] if fixture.release else 'v4-test-1'
                if url.path in (prefix + '/releases/tags/' + tag, prefix + '/releases/' + tag):
                    if fixture.release is None:
                        return self.send_body(404)
                    return self.metadata(fixture.release | {'assets': [
                        {'name': tag + '.zip', 'type': 'source'}, *[
                            {'name': name, 'type': 'attach', 'id': index, 'size': len(raw),
                             'digest': 'sha256:' + hashlib.sha256(raw).hexdigest(),
                             'browser_download_url': 'https://untrusted.invalid/ignored'}
                            for index, (name, raw) in enumerate(fixture.objects.items(), 1)] ]})
                if url.path == prefix + f'/releases/{tag}/upload_url' and token and asset:
                    return self.metadata({'url': fixture.signed_url or fixture.origin + '/object/' + asset + '?Signature=fixture',
                                          'headers': fixture.sign_headers})
                if url.path.startswith(prefix + f'/releases/{tag}/attach_files/') and not token:
                    asset = url.path.split('/')[-2]
                    if asset in fixture.objects:
                        return self.send_body(302, Location=fixture.download_redirect or '/cdn/' + asset + '?auth_key=fixture')
                if url.path.startswith('/cdn/') and query == {'auth_key': ['fixture']} and not token:
                    asset = url.path.rsplit('/', 1)[-1]
                    raw = fixture.objects[asset]
                    fixture.events.append(('get', asset))
                    if fixture.corrupt_download:
                        raw = bytes([raw[0] ^ 1]) + raw[1:]
                    if self.headers.get('Range') == 'bytes=1-':
                        return self.send_body(206, raw[1:], **{'Content-Range': f'bytes 1-{len(raw)-1}/{len(raw)}'})
                    return self.send_body(200, raw)
                self.send_body(404)

            def do_POST(self):
                if self.path != '/api/v5/repos/firekula/DFLegacy/releases' or self.headers.get('Authorization') != 'Bearer fixture-token':
                    return self.send_body(400)
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                if body.get('release_status') != 'latest' or 'draft' in body or 'prerelease' in body:
                    return self.send_body(422)
                fixture.release = {'tag_name': body['tag_name'], 'target_commitish': body['target_commitish'],
                                   'created_at': '2026-11-15T00:00:00+08:00'}
                self.metadata(fixture.release, 201)

            def do_PUT(self):
                url = urllib.parse.urlsplit(self.path)
                if not url.path.startswith('/object/') or url.query != 'Signature=fixture':
                    return self.send_body(400)
                if self.headers.get('Authorization') or any(self.headers.get(k) != v for k, v in fixture.sign_headers.items()):
                    return self.send_body(400)
                asset = url.path.rsplit('/', 1)[-1]
                fixture.objects[asset] = self.rfile.read(int(self.headers['Content-Length']))
                fixture.events.append(('put', asset))
                self.send_body(200)

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        self.origin = f'http://127.0.0.1:{server.server_port}'

    def build(self):
        self.put('DNF.exe', b'native client bytes')
        options = self.options()
        options.update(channel='chapter-two', flavor='normal')
        return v4.build(self.root / 'bundle', 'gitcode', 'firekula/DFLegacy', **options)

    def publish(self):
        api = v4.ReleaseApi('gitcode', 'firekula/DFLegacy', 'fixture-token', self.origin)
        return v4.publish(self.root / 'bundle', api, 'b' * 40)

    def test_manifest_last_and_anonymous_ranges_across_canonical_redirects(self):
        receipt = self.build()
        result = self.publish()
        part, = receipt['assets']
        manifest = receipt['manifestAsset']
        self.assertEqual(self.events[:4], [('put', part), ('get', part), ('put', manifest), ('get', manifest)])
        self.assertEqual(result['status'], 'published-verified')
        self.assertEqual([check['range'] for check in result['checks']], ['supported', 'supported'])
        self.assertEqual(result['releaseId'], 'v4-test-1')
        with self.assertRaisesRegex(v4.ReleaseError, 'immutable'):
            self.publish()
        self.assertEqual([event for event in self.events if event[0] == 'put'], [('put', part), ('put', manifest)])

    def test_unverified_resource_prevents_manifest(self):
        receipt = self.build()
        self.corrupt_download = True
        with self.assertRaisesRegex(v4.ReleaseError, 'SHA256 differs'):
            self.publish()
        self.assertNotIn(receipt['manifestAsset'], self.objects)

    def test_signed_destination_and_headers_are_constrained(self):
        self.build()
        self.signed_url = 'https://file.gitcode.com.evil.invalid/object?Signature=fixture'
        with self.assertRaisesRegex(v4.ReleaseError, 'signed upload destination'):
            self.publish()
        self.assertEqual(self.events, [])
        self.release = None
        self.signed_url = None
        self.sign_headers['Authorization'] = 'fixture-secret'
        with self.assertRaisesRegex(v4.ReleaseError, 'signed upload headers'):
            self.publish()
        self.assertEqual(self.events, [])

    def test_cross_origin_redirect_fails_before_manifest(self):
        receipt = self.build()
        self.download_redirect = 'https://untrusted.invalid/cdn/object'
        with self.assertRaisesRegex(v4.ReleaseError, 'redirect escaped loopback'):
            self.publish()
        self.assertNotIn(receipt['manifestAsset'], self.objects)

    def test_production_signed_put_accepts_gitcode_file_service(self):
        api = v4.ReleaseApi('gitcode', 'firekula/DFLegacy', 'fixture-token')
        file = self.put('payload.bin')
        url = 'https://file.gitcode.com/77/releases/opaque/payload.bin?Signature=fixture'
        with patch.object(api, 'request', return_value={'url': url, 'headers': self.sign_headers}), patch.object(api, '_open') as send:
            send.return_value.__enter__.return_value.status = 200
            api.upload_gitcode('v4-test-1', file, file.name)
        self.assertEqual(send.call_args.args[:2], (url, 'PUT'))
        self.assertEqual(send.call_args.args[3], self.sign_headers | {'Content-Length': '7'})
        self.assertEqual(send.call_args.kwargs, {})

        for url in ('http://file.gitcode.com/upload?Signature=fixture',
                    'https://file.gitcode.com.evil.invalid/upload?Signature=fixture',
                    'https://file.gitcode.com:444/upload?Signature=fixture',
                    'https://user@file.gitcode.com/upload?Signature=fixture'):
            with self.subTest(url=url), patch.object(api, 'request', return_value={'url': url, 'headers': self.sign_headers}), patch.object(api, '_open') as send:
                with self.assertRaisesRegex(v4.ReleaseError, 'signed upload destination'):
                    api.upload_gitcode('v4-test-1', file, file.name)
                send.assert_not_called()
