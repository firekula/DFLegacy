#!/usr/bin/env python3
"""Release-only public GitHub publisher. Never pushes private source or history."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

from release import DEV, MAX_UPDATE_ZIP, ReleaseError, UPDATE_ASSET, read_client_update, validate_identity, verify_client_update

REPOSITORY = 'firekula/DFLegacy'
ASSET_NAME = UPDATE_ASSET
TOKEN_ENV = 'DFLEGACY_GITHUB_RELEASE_TOKEN'
MARKER = '<!-- dflegacy-client-update:v1 -->'
README = b'# DFLegacy client updates\n\nThis repository distributes public client releases only. Source development and build history are maintained separately.\n'


def credentials():
    """Dedicated token or existing Windows GCM; no interactive prompt or token output."""
    token = os.environ.get(TOKEN_ENV, '')
    source = TOKEN_ENV
    if not token and os.name == 'nt':
        try:
            result = subprocess.run(['git', '-c', 'credential.interactive=never', 'credential', 'fill'],
                                    input='protocol=https\nhost=github.com\n\n', capture_output=True, text=True,
                                    timeout=30, env=os.environ | {'GIT_TERMINAL_PROMPT': '0', 'GCM_INTERACTIVE': 'Never'})
            values = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
            if result.returncode == 0 and values.get('protocol') == 'https' and values.get('host') == 'github.com':
                token = values.get('password', '')
                source = 'git-credential-manager'
        except (OSError, subprocess.SubprocessError, ValueError):
            pass
    if not token or any(c.isspace() for c in token):
        raise ReleaseError('GitHub publication unavailable: set ' + TOKEN_ENV + ' or provision noninteractive Windows Git Credential Manager')
    return token, source


class NoFollow(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class GitHub:
    def __init__(self, token):
        if not token or any(c.isspace() for c in token):
            raise ReleaseError('invalid GitHub credential')
        self.token = token
        self.prefix = '/repos/' + REPOSITORY
        self.opener = urllib.request.build_opener(NoFollow())

    def _validate_api(self, url):
        p = urllib.parse.urlsplit(url)
        if p.scheme != 'https' or p.hostname not in ('api.github.com', 'uploads.github.com') or p.port not in (None, 443) or p.username or p.password or p.fragment:
            raise ReleaseError('untrusted GitHub API origin')
        path = p.path.removeprefix(self.prefix)
        decoded = urllib.parse.unquote(path)
        if not p.path.startswith(self.prefix + '/') and p.path != self.prefix:
            raise ReleaseError('untrusted GitHub repository path')
        if any(part in ('.', '..') for part in decoded.split('/')) or '\\' in decoded:
            raise ReleaseError('untrusted GitHub API path')
        if p.hostname == 'uploads.github.com':
            allowed = re.fullmatch(r'/releases/[1-9]\d*/assets', path) and p.query == 'name=' + ASSET_NAME
        else:
            allowed = path in ('', '/releases', '/commits', '/contents/README.md') or re.fullmatch(r'/releases/(?:[1-9]\d*(?:/assets)?|assets/[1-9]\d*|tags/[A-Za-z0-9.%-]+)', path) or re.fullmatch(r'/git/ref/heads/[A-Za-z0-9._%/-]+', path)
            allowed = allowed and (p.query in ('', 'per_page=1') or re.fullmatch(r'per_page=100&page=([1-9]\d?|100)', p.query))
        if not allowed:
            raise ReleaseError('untrusted GitHub API endpoint')

    def _open(self, url, method='GET', data=None, content_type='application/json', authenticated=True, accept='application/vnd.github+json'):
        if authenticated:
            self._validate_api(url)
        headers = {'Accept': accept, 'User-Agent': 'DFLegacy-release/1.0'}
        if authenticated:
            headers.update({'Authorization': 'Bearer ' + self.token, 'X-GitHub-Api-Version': '2022-11-28'})
        if data is not None:
            headers['Content-Type'] = content_type
        request = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            return self.opener.open(request, timeout=120)
        except urllib.error.HTTPError as error:
            # Caller handles status without printing remote bodies, URLs or credentials.
            return error
        except (OSError, ValueError, urllib.error.URLError):
            raise ReleaseError('GitHub connection failed; inspect draft before retrying') from None

    def request(self, method, path, data=None, missing_ok=False, empty_ok=False):
        payload = json.dumps(data).encode() if data is not None else None
        with self._open('https://api.github.com' + self.prefix + path, method, payload) as response:
            code = response.status
            if code == 404 and missing_ok or code == 409 and empty_ok and path == '/commits?per_page=1':
                return None
            if not 200 <= code < 300:
                raise ReleaseError(f'GitHub {method} failed (HTTP {code}); inspect draft before retrying')
            try:
                raw = response.read(4 * 1024 * 1024 + 1)
                if len(raw) > 4 * 1024 * 1024:
                    raise ValueError()
                return json.loads(raw) if raw else None
            except (OSError, ValueError):
                raise ReleaseError('invalid GitHub API response') from None

    def upload(self, release_id, name, content):
        if type(release_id) is not int or release_id <= 0 or name != ASSET_NAME:
            raise ReleaseError('invalid GitHub upload identity')
        url = f'https://uploads.github.com{self.prefix}/releases/{release_id}/assets?name={ASSET_NAME}'
        with self._open(url, 'POST', content, 'application/zip') as response:
            if response.status != 201:
                raise ReleaseError(f'GitHub upload failed (HTTP {response.status}); inspect draft before retrying')

    def _validate_download(self, url, redirect=False):
        p = urllib.parse.urlsplit(url)
        if p.scheme != 'https' or p.port not in (None, 443) or p.username or p.password or p.fragment:
            raise ReleaseError('untrusted GitHub asset origin')
        if p.hostname == 'github.com' and re.fullmatch(re.escape('/' + REPOSITORY + '/releases/download/') + r'[A-Za-z0-9._%-]+/' + re.escape(ASSET_NAME), p.path) and not p.query:
            return
        if redirect and p.hostname in ('release-assets.githubusercontent.com', 'objects.githubusercontent.com'):
            return
        raise ReleaseError('untrusted GitHub asset download')

    def attachment_digest(self, asset, size, draft=False):
        if type(asset.get('id')) is not int or asset['id'] <= 0 or asset.get('size') != size or not 0 < size <= MAX_UPDATE_ZIP:
            raise ReleaseError('invalid GitHub asset identity or size')
        if draft:
            # Draft browser URLs are not public. Authenticate only this API hop.
            url = f'https://api.github.com{self.prefix}/releases/assets/{asset["id"]}'
        else:
            url = asset.get('browser_download_url', '')
            self._validate_download(url)
        for hop in range(5):
            authenticated = draft and hop == 0
            if not authenticated:
                self._validate_download(url, hop > 0)
            with self._open(url, authenticated=authenticated, accept='application/octet-stream') as response:
                if response.status in (301, 302, 303, 307, 308):
                    location = response.headers.get('Location')
                    if not location:
                        raise ReleaseError('GitHub asset redirect has no destination')
                    url = urllib.parse.urljoin(url, location)
                    continue
                if response.status != 200:
                    raise ReleaseError(f'GitHub asset download failed (HTTP {response.status})')
                checksum, actual = hashlib.sha256(), 0
                try:
                    while block := response.read(min(1024 * 1024, size - actual + 1)):
                        actual += len(block)
                        if actual > size:
                            raise ReleaseError('GitHub asset exceeds expected size')
                        checksum.update(block)
                except OSError:
                    raise ReleaseError('GitHub asset download interrupted') from None
                if actual != size:
                    raise ReleaseError('GitHub asset download truncated')
                return checksum.hexdigest()
        raise ReleaseError('too many GitHub asset redirects')


def preflight(api):
    repository = api.request('GET', '')
    if not isinstance(repository, dict) or repository.get('private') is not False or repository.get('permissions', {}).get('push') is not True:
        raise ReleaseError('GitHub publication requires a public repository and push permission')
    branch = repository.get('default_branch')
    if not isinstance(branch, str) or not re.fullmatch(r'[A-Za-z0-9._/-]+', branch):
        raise ReleaseError('invalid GitHub default branch')
    return branch


def target_commit(api, branch):
    path = '/git/ref/heads/' + urllib.parse.quote(branch, safe='')
    ref = api.request('GET', path, missing_ok=True)
    if ref is None:
        commits = api.request('GET', '/commits?per_page=1', empty_ok=True)
        if commits:
            raise ReleaseError('default branch missing in nonempty GitHub repository')
        try:
            api.request('PUT', '/contents/README.md', {'message': 'Initialize public client release repository',
                                                      'content': base64.b64encode(README).decode('ascii')})
        except ReleaseError:
            # An identical parallel run may have initialized the repository.
            if api.request('GET', path, missing_ok=True) is None:
                raise
        ref = api.request('GET', path)
    sha = ref.get('object', {}).get('sha') if isinstance(ref, dict) else None
    if not isinstance(sha, str) or not re.fullmatch(r'[a-f0-9]{40}', sha):
        raise ReleaseError('GitHub default branch has no valid commit')
    return sha


def publish(api, version, commit, archive, check_only=False):
    validate_identity(version, commit)
    archive = Path(archive)
    if archive.name != ASSET_NAME:
        raise ReleaseError('client update asset name must be ' + ASSET_NAME)
    # Validate, reserve and upload exactly this immutable, bounded snapshot.
    # Reopening the path after validation could publish unchecked private data.
    content = read_client_update(archive)
    verify_client_update(content, version, commit)
    sha, size = hashlib.sha256(content).hexdigest(), len(content)
    branch = preflight(api)
    prerelease = bool(DEV.fullmatch(version))
    body = f'{MARKER}\nversion={version}\nsource-commit={commit}\nasset-sha256={sha}\nasset-size={size}\n\nPublic client binaries only. Built from the pinned Gitea source commit above.\n'
    tag = '/releases/tags/' + urllib.parse.quote(version, safe='')

    def fetch():
        current = api.request('GET', tag, missing_ok=True)
        if current is None:
            # The tag endpoint only guarantees published releases. Authorized
            # release listings include drafts and let interrupted runs resume.
            for page in range(1, 101):
                batch = api.request('GET', f'/releases?per_page=100&page={page}')
                if not isinstance(batch, list) or len(batch) > 100 or any(not isinstance(row, dict) for row in batch):
                    raise ReleaseError('invalid GitHub release listing')
                matching = [row for row in batch if row.get('tag_name') == version]
                if len(matching) > 1:
                    raise ReleaseError('duplicate GitHub release identity')
                if matching:
                    current = matching[0]
                    break
                if len(batch) < 100:
                    break
            else:
                raise ReleaseError('GitHub release lookup limit reached; refusing mutation')
        if current is not None and (not isinstance(current, dict) or current.get('body') != body or current.get('tag_name') != version or current.get('prerelease') is not prerelease or type(current.get('draft')) is not bool or type(current.get('id')) is not int or current['id'] <= 0):
            raise ReleaseError('GitHub release identity differs; refusing mutation')
        return current

    def outcome(status, current):
        return {'status': status, 'repository': REPOSITORY, 'version': version, 'commit': commit,
                'sha256': sha, 'size': size, 'release_id': current['id'] if current else None}

    def inspect():
        current = fetch()
        if current is None:
            raise ReleaseError('GitHub reserved release disappeared')
        assets = api.request('GET', f'/releases/{current["id"]}/assets?per_page=100&page=1')
        if not isinstance(assets, list) or len(assets) > 1:
            raise ReleaseError('unexpected GitHub release assets')
        if assets:
            asset = assets[0]
            if not isinstance(asset, dict) or asset.get('name') != ASSET_NAME or asset.get('digest') != 'sha256:' + sha:
                raise ReleaseError('GitHub asset identity or published digest differs')
            if api.attachment_digest(asset, size, draft=current['draft']) != sha:
                raise ReleaseError('GitHub downloaded asset digest differs')
        if not current['draft'] and not assets:
            raise ReleaseError('published GitHub release is incomplete')
        latest = fetch()
        if latest is None or latest['id'] != current['id']:
            raise ReleaseError('GitHub release changed during verification')
        if not latest['draft'] and not assets:
            raise ReleaseError('published GitHub release is incomplete')
        if current['draft'] and not latest['draft']:
            # A competing run published while we were checking private draft
            # bytes. Repeat through the anonymous public download boundary.
            return inspect()
        return latest, bool(assets)

    current = fetch()
    if current is None:
        if check_only:
            return outcome('absent', None)
        target = target_commit(api, branch)
        try:
            api.request('POST', '/releases', {'tag_name': version, 'target_commitish': target, 'name': version,
                                            'body': body, 'draft': True, 'prerelease': prerelease})
        except ReleaseError:
            if fetch() is None:
                raise
    current, present = inspect()
    if check_only or not current['draft']:
        return outcome('draft' if current['draft'] else 'already-published', current)
    if not present:
        try:
            api.upload(current['id'], ASSET_NAME, content)
        except ReleaseError:
            current, present = inspect()
            if not present:
                raise
        current, present = inspect()
    if not present:
        raise ReleaseError('GitHub draft is incomplete')
    if current['draft']:
        try:
            api.request('PATCH', f'/releases/{current["id"]}', {'draft': False})
        except ReleaseError:
            if fetch()['draft']:
                raise
    current, present = inspect()  # Anonymous public bytes must also match.
    if current['draft'] or not present:
        raise ReleaseError('GitHub publication not confirmed')
    return outcome('published', current)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preflight', action='store_true', help='read-only credential and repository capability check')
    parser.add_argument('--check-only', action='store_true', help='verify identity and any existing bytes without mutation')
    for name in ('version', 'commit', 'archive'):
        parser.add_argument('--' + name)
    args = parser.parse_args(argv)
    try:
        token, source = credentials()
        api = GitHub(token)
        if args.preflight:
            preflight(api)
            print(json.dumps({'status': 'available', 'repository': REPOSITORY, 'credential_source': source}))
        else:
            if not all((args.version, args.commit, args.archive)):
                raise ReleaseError('--version, --commit and --archive are required')
            print(json.dumps(publish(api, args.version, args.commit, args.archive, args.check_only)))
        return 0
    except (ReleaseError, OSError):
        # Fixed errors only: no exception chain, raw server body or credential helper output.
        print('GitHub publication unavailable or verification failed. Check dedicated credential capability and release identity before retrying.', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
