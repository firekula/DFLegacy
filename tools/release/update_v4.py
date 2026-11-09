#!/usr/bin/env python3
"""V4 client resource engine. Explicit selection; immutable releases; no source pushes.

CLI/GUI exchange a JSON request (build_manifest keyword arguments plus provider and
repository). Credentials are read only by publish from a dedicated environment
variable, never from the request, bundle, command line or client configuration.
"""
import argparse
import copy
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import uuid

PART_LIMIT = 48 * 1024 * 1024
# This isolated test provider is pinned; this is not a general HTTP source option.
GITEA_ORIGIN = 'http://firekula.site:3000'
# /api/v1/settings/attachment reported max_size=100 on 2026-11-08. Release
# uploads can differ from issue uploads; use a conservative decimal MB bound.
GITEA_ATTACHMENT_LIMIT = 100_000_000
MAX_METADATA = 16 * 1024 * 1024
MAX_LONG = 2**63 - 1
PROTECTED = {'launcher.json', 'launcher.txt', 'config.ini', 'client-package.json',
             'client-update-v4.json', '.dflegacy', 'logs', 'screenshot', 'screenshots',
             'save', 'saves', 'accounts', 'crashreports'}
PRIVATE = {'.git', '.svn', '.hg', '.vs', '.vscode', '__pycache__', 'node_modules',
           '.ssh', '.aws', '.codex', '.superpowers', '.tmp', 'server.json', 'manager.json',
           'diagnostics', 'sessions', 'collections', 'captures', 'queue', 'events', 'installations', 'preference.json', 'settings.json', 'active.json', 'preferences.json', 'session.json', 'closed.json', 'drops.json',
           'account.json', 'accounts.json', 'credentials.json', 'cookies.json', 'cache', 'backup', 'backups'}
# Backups and interrupted atomic saves can contain the same credentials as the
# original configuration. Match derivatives before include rules at every depth.
PRIVATE_PREFIXES = ('.env', '.launcher-save-') + tuple(
    filename + suffix for filename in sorted(PROTECTED | PRIVATE)
    if filename.endswith(('.json', '.ini', '.txt')) for suffix in ('.', '~'))
MANIFEST_KEYS = {'schemaVersion', 'version', 'commit', 'architecture', 'flavor', 'channel',
                 'sequence', 'minimumSequence', 'files', 'retired', 'migrations'}


class ReleaseError(ValueError):
    """A safe local diagnostic; remote bodies and credentials never reach this type."""


def require(condition, message):
    if not condition: raise ReleaseError(message)


def name(value):
    return isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,127}', value) is not None


def checksum(value, length=64):
    return isinstance(value, str) and re.fullmatch('[a-f0-9]{%d}' % length, value) is not None


def integer(value, lower=0, upper=MAX_LONG):
    return type(value) is int and lower <= value <= upper


def validate_path(value):
    require(isinstance(value, str) and 0 < len(value) <= 1024 and unicodedata.normalize('NFC', value) == value,
            'invalid resource path')
    require(not any(ord(c) < 32 or ord(c) == 127 or c in '\\:<>"|?*' for c in value), 'invalid resource path')
    for part in value.split('/'):
        require(0 < len(part) <= 255 and part not in ('.', '..') and not part.endswith(('.', ' '))
                and not re.search(r'~[0-9]',part)
                and not re.match(r'^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)', part, re.I), 'unsafe Windows resource path')
    require(value.split('/')[0].casefold() not in PROTECTED, 'protected resource path')


def private_path(value):
    # Protect nested copies too: a broad include must never collect player secrets.
    return any(p in PROTECTED | PRIVATE or p.startswith(PRIVATE_PREFIXES)
               or p.endswith(('.key', '.pem', '.pfx', '.p12', '.log', '.dmp'))
               for p in (part.casefold() for part in value.split('/')))


def no_links(path):
    for item in (Path(path), *Path(path).parents):
        if item.exists() or item.is_symlink():
            info = item.lstat()
            require(not stat.S_ISLNK(info.st_mode) and not (getattr(info, 'st_file_attributes', 0) & 0x400),
                    'resource paths must not traverse links or reparse points')


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n').encode('utf-8')


def load_json(path):
    def pairs(rows):
        result = {}
        for key, value in rows:
            require(key not in result, 'duplicate JSON field')
            result[key] = value
        return result
    raw = Path(path).read_bytes()
    require(0 < len(raw) <= MAX_METADATA, 'metadata size limit exceeded')
    try: return json.loads(raw.decode('utf-8-sig'), object_pairs_hook=pairs,
                           parse_constant=lambda _: (_ for _ in ()).throw(ReleaseError('invalid JSON number')))
    except (UnicodeError, json.JSONDecodeError, RecursionError): raise ReleaseError('invalid JSON metadata') from None


def migration_valid(m):
    require(isinstance(m, dict) and {'id', 'file', 'operation', 'key', 'value'} <= m.keys()
            and m.keys() <= {'id', 'file', 'operation', 'key', 'value', 'when'} and name(m['id'])
            and m['file'] == 'launcher.json' and m['operation'] in ('unionPorts', 'setDefault', 'replaceIf')
            and m['key'] in ('Relay.TcpPorts', 'Relay.UdpPorts', 'Relay.Enabled', 'Relay.Preference'), 'invalid configuration migration')
    def value_ok(value):
        if m['key'].endswith('Ports'): return isinstance(value, list) and len(value) <= 65535 and all(integer(p, 1, 65535) for p in value)
        if m['key'] == 'Relay.Enabled': return type(value) is bool
        return isinstance(value,str) and value.casefold() in ('ipv4', 'ipv6')
    require(value_ok(m['value']), 'invalid migration value')
    require(m['operation'] != 'unionPorts' or m['key'].endswith('Ports'), 'unionPorts requires a port field')
    condition_ok='when' in m and value_ok(m['when'])
    if m['key'].endswith('Ports') and isinstance(m.get('when'),str):
        fields=re.split(r'[,;\s]+',m['when'].strip()) if m['when'].strip() else []
        condition_ok=all(re.fullmatch(r'[0-9]{1,5}',p) and 1<=int(p)<=65535 for p in fields)
    require(m['operation'] != 'replaceIf' or condition_ok, 'replaceIf requires a typed old value')
    require(m['operation'] == 'replaceIf' or m.get('when') is None, 'unexpected migration condition')


def validate_manifest(m):
    require(isinstance(m, dict) and set(m) == MANIFEST_KEYS, 'invalid manifest fields')
    require(m['schemaVersion'] == 4 and type(m['schemaVersion']) is int and name(m['version'])
            and checksum(m['commit'], 40) and m['architecture'] in ('x86', 'x64')
            and m['flavor'] in ('normal', 'test') and name(m['channel'])
            and integer(m['sequence'], 1) and integer(m['minimumSequence'], 0, m['sequence']), 'invalid manifest identity')
    require(isinstance(m['files'], list) and 0 < len(m['files']) <= 100000
            and isinstance(m['retired'], list) and isinstance(m['migrations'], list), 'invalid manifest collections')
    paths, resources, total = set(), {}, 0
    for f in m['files']:
        require(isinstance(f, dict) and set(f) == {'path','size','sha256','parts'}, 'invalid file record')
        validate_path(f['path']); path = f['path'].casefold()
        require(path not in paths and integer(f['size']) and checksum(f['sha256'])
                and isinstance(f['parts'], list) and 0 < len(f['parts']) <= 100000, 'duplicate or invalid file')
        paths.add(path); size = 0
        for p in f['parts']:
            require(isinstance(p, dict) and set(p) == {'sha256','size','release','asset'} and checksum(p['sha256'])
                    and integer(p['size'], 0, PART_LIMIT) and name(p['release']) and name(p['asset'])
                    and (p['size'] != 0 or f['size'] == 0 and len(f['parts']) == 1), 'invalid resource part')
            key=(p['release'],p['asset']); identity=(p['sha256'],p['size'])
            require(key not in resources or resources[key] == identity, 'conflicting attachment identity')
            resources[key]=identity; size += p['size']
        require(size == f['size'], 'part sizes do not match file')
        total += size
    require(total <= MAX_LONG, 'file sizes overflow')
    for path in paths:
        parts=path.split('/')
        require(not any('/'.join(parts[:i]) in paths for i in range(1,len(parts))), 'file/directory path collision')
    retired=set()
    for r in m['retired']:
        require(isinstance(r,dict) and set(r)=={'path','action','sequence'}, 'invalid retirement')
        validate_path(r['path']); p=r['path'].casefold()
        require(p not in paths and p not in retired and r['action'] in ('delete','unmanage')
                and integer(r['sequence'],1,m['sequence']), 'invalid retirement')
        retired.add(p)
    ids=set()
    for migration in m['migrations']:
        migration_valid(migration)
        require(migration['id'] not in ids, 'duplicate migration ID'); ids.add(migration['id'])
    require(len(json_bytes(m)) <= MAX_METADATA, 'manifest size limit exceeded')
    return m


def manifest_name(m):
    return f"DFLegacy-client-update-v4-{m['channel']}-{m['flavor']}-win-{m['architecture']}.json"


def release_references(tag, manifests):
    require(name(tag),'invalid release tag')
    result=[]
    for value in manifests:
        manifest=validate_manifest(load_json(value) if isinstance(value,(str,Path)) else value)
        if any(part['release']==tag for f in manifest['files'] for part in f['parts']): result.append(manifest['version'])
    return sorted(set(result))


def ensure_release_unreferenced(tag, manifests):
    require(not release_references(tag,manifests),'release is still referenced by supported manifests; deletion refused')


def preflight_limits(provider, sizes, existing_bytes=0):
    sizes=list(sizes)
    require(provider in ('github','gitee','gitea') and integer(existing_bytes) and all(integer(s) for s in sizes), 'invalid provider inventory')
    if provider == 'github':
        require(len(sizes) <= 1000, 'GitHub release exceeds 1000 attachments')
        require(all(s < 2**31 for s in sizes), 'GitHub attachment must be smaller than 2 GiB')
    elif provider == 'gitee':
        require(all(s <= 100000000 for s in sizes), 'Gitee attachment exceeds 100 MB')
        require(sum(sizes) + existing_bytes <= 1000000000, 'Gitee repository attachments exceed 1 GB')
    else:
        require(all(s <= GITEA_ATTACHMENT_LIMIT for s in sizes), 'Gitea attachment exceeds conservative 100 MB limit')


def plan_resources(root, version, commit, architecture, flavor, channel, sequence,
                   minimum_sequence=0, includes=None, excludes=None, previous=None,
                   retired=None, migrations=None, reuse=None, _asset_output=None):
    require(includes and all(isinstance(p,str) and p for p in includes), 'explicit include rules are required')
    require(excludes is None or isinstance(excludes,list) and all(isinstance(p,str) for p in excludes), 'invalid exclude rules')
    root=Path(root).absolute(); no_links(root)
    require(root.is_dir(), 'resource root is not a directory')
    m=dict(schemaVersion=4, version=version, commit=commit, architecture=architecture, flavor=flavor, channel=channel,
           sequence=sequence, minimumSequence=minimum_sequence, files=[], retired=[], migrations=[])
    histories=[validate_manifest(load_json(p) if isinstance(p,(str,Path)) else copy.deepcopy(p)) for p in previous or []]
    reusable=histories + [validate_manifest(load_json(p) if isinstance(p,(str,Path)) else copy.deepcopy(p)) for p in reuse or []]
    for old in histories:
        require(all(old[k] == m[k] for k in ('architecture','flavor','channel'))
                and integer(sequence,old['sequence']+1) and old['version'] != version, 'previous manifest identity differs or sequence is not increasing')
        require(integer(minimum_sequence,old['minimumSequence']), 'minimum baseline cannot move backwards')
    for old in reusable:
        require(old['channel']==channel and old['flavor']==flavor and old['version']!=version, 'reuse manifest must share channel and flavor with a different version')
    reuse_parts={(p['sha256'],p['size']): p for old in reusable for f in old['files'] for p in f['parts']}
    excluded=[]; assets={}; seen=set(); upload_bytes=0; reused_bytes=0
    for directory, dirs, files in os.walk(root, followlinks=False):
        for d in list(dirs):
            child=Path(directory)/d; relative=child.relative_to(root).as_posix()
            if private_path(relative):
                excluded.append(dict(path=relative+'/',reason='protected/private')); dirs.remove(d)
            else: no_links(child)
        for filename in sorted(files,key=str.casefold):
            path=Path(directory)/filename; relative=path.relative_to(root).as_posix()
            if private_path(relative): excluded.append(dict(path=relative,reason='protected/private')); continue
            if not any(fnmatch.fnmatchcase(relative.casefold(), p.casefold()) for p in includes) or any(fnmatch.fnmatchcase(relative.casefold(),p.casefold()) for p in excludes or []):
                excluded.append(dict(path=relative,reason='selection')); continue
            validate_path(relative); no_links(path)
            require(path.is_file(), 'resource is not a regular file')
            require(relative.casefold() not in seen, 'case-colliding selected files'); seen.add(relative.casefold())
            total_hash=hashlib.sha256(); parts=[]; size=0
            with path.open('rb') as stream:
                while True:
                    data=stream.read(PART_LIMIT)
                    if not data and parts: break
                    digest=hashlib.sha256(data).hexdigest(); total_hash.update(data); size+=len(data)
                    identity=(digest,len(data))
                    if identity in reuse_parts:
                        part=copy.deepcopy(reuse_parts[identity]); reused_bytes+=len(data)
                    else:
                        part=dict(sha256=digest,size=len(data),release=version,asset='dflegacy-v4-'+digest+'.bin')
                        if part['asset'] not in assets:
                            assets[part['asset']]=dict(size=len(data),sha256=digest); upload_bytes+=len(data)
                            if _asset_output is not None: (Path(_asset_output)/part['asset']).write_bytes(data)
                    parts.append(part)
                    if not data: break
            m['files'].append(dict(path=relative,size=size,sha256=total_hash.hexdigest(),parts=parts))
    m['files'].sort(key=lambda f:f['path'].casefold())
    cumulative_retired={}; cumulative_migrations={}
    for old in sorted(histories,key=lambda x:x['sequence']):
        for row in old['retired']:
            key=row['path'].casefold()
            require(key not in cumulative_retired or cumulative_retired[key]==row, 'conflicting historical retirement')
            cumulative_retired[key]=row
        for row in old['migrations']:
            require(row['id'] not in cumulative_migrations or cumulative_migrations[row['id']]==row, 'conflicting historical migration')
            cumulative_migrations[row['id']]=row
    for row in retired or []:
        key=row['path'].casefold()
        require(key not in cumulative_retired or cumulative_retired[key]==row, 'retirement identity cannot change')
        cumulative_retired[key]=copy.deepcopy(row)
    for row in migrations or []:
        migration_valid(row)
        require(row['id'] not in cumulative_migrations or cumulative_migrations[row['id']]==row, 'migration identity cannot change')
        cumulative_migrations[row['id']]=copy.deepcopy(row)
    old_files={f['path'].casefold():f for old in histories for f in old['files']}
    for path in old_files.keys()-seen:
        require(path in cumulative_retired, 'omitted managed file requires explicit retirement: '+old_files[path]['path'])
    m['retired']=list(cumulative_retired.values()); m['migrations']=list(cumulative_migrations.values())
    validate_manifest(m)
    latest={f['path'].casefold():f for f in max(histories,key=lambda x:x['sequence'])['files']} if histories else {}
    diff=[dict(path=f['path'],status=('added' if f['path'].casefold() not in latest else
                'unchanged' if (f['sha256'],f['size'])==(latest[f['path'].casefold()]['sha256'],latest[f['path'].casefold()]['size']) else 'changed'),size=f['size']) for f in m['files']]
    diff.extend(dict(path=r['path'],status=r['action'],size=0) for r in m['retired'])
    return dict(manifest=m, manifestAsset=manifest_name(m), assets=assets, excluded=excluded, diff=diff,
                totalBytes=sum(f['size'] for f in m['files']),uploadBytes=upload_bytes,reusedBytes=reused_bytes,
                migrations=m['migrations'],retired=m['retired'])


def build_manifest(**kwargs):
    return plan_resources(**kwargs)['manifest']


def validate_source(provider, repository):
    require(provider in ('github','gitee','gitea') and isinstance(repository,str)
            and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*',repository), 'invalid provider/repository')
    require(provider != 'gitea' or repository == 'firekula/DFLegacy', 'Gitea test repository must be firekula/DFLegacy')


def source_identity(provider, repository, manifest):
    validate_source(provider,repository)
    require(provider != 'gitee' or manifest['channel']=='gitee-test', 'Gitee requires explicit gitee-test channel')
    require(provider != 'gitea' or manifest['channel']=='gitea-test', 'Gitea requires explicit gitea-test channel')
    require(name(manifest_name(manifest)), 'manifest attachment name is too long')


def build(output, provider, repository, **kwargs):
    output=Path(output).absolute(); no_links(output)
    source=Path(kwargs['root']).absolute()
    require(output!=source and source not in output.parents, 'build output must be outside the resource root')
    require(not output.exists(), 'build output already exists; choose a new directory')
    output.mkdir(parents=True); (output/'assets').mkdir()
    result=plan_resources(_asset_output=output/'assets',**kwargs)
    source_identity(provider,repository,result['manifest'])
    raw=json_bytes(result['manifest']); asset=result['manifestAsset']
    preflight_limits(provider,[x['size'] for x in result['assets'].values()]+[len(raw)])
    (output/asset).write_bytes(raw)
    result.update(provider=provider,repository=repository,manifestSha256=hashlib.sha256(raw).hexdigest(),manifestSize=len(raw))
    (output/'receipt.json').write_bytes(json_bytes(result))
    return result


def file_hash(path):
    digest=hashlib.sha256(); size=0; no_links(path)
    with Path(path).open('rb') as stream:
        while chunk:=stream.read(1024*1024): size+=len(chunk); digest.update(chunk)
    return digest.hexdigest(),size


def verify_local(bundle):
    bundle=Path(bundle); no_links(bundle)
    receipt=load_json(bundle/'receipt.json'); m=validate_manifest(receipt['manifest'])
    source_identity(receipt['provider'],receipt['repository'],m)
    require(receipt['manifestAsset']==manifest_name(m), 'manifest attachment name differs')
    raw=(bundle/receipt['manifestAsset']).read_bytes()
    require(raw==json_bytes(m) and file_hash(bundle/receipt['manifestAsset'])==(receipt['manifestSha256'],receipt['manifestSize']), 'manifest bytes differ from receipt')
    expected={p['asset']:(p['sha256'],p['size']) for f in m['files'] for p in f['parts'] if p['release']==m['version']}
    require(set(receipt['assets'])==set(expected), 'local attachment inventory differs')
    for asset,identity in expected.items():
        require(file_hash(bundle/'assets'/asset)==identity, 'local resource hash or length differs')
    preflight_limits(receipt['provider'],[size for _,size in expected.values()]+[len(raw)])
    return receipt


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs): return None


class ReleaseApi:
    """Bound repository API. An explicit loopback root is only for HTTP contract tests.

    Gitea 1.26 uses /api/v1, limit=50 pagination, assets and multipart attachment.
    Gitee API 5.4.93: /api/v5/swagger_doc; release attachments use attach_files,
    multipart field file, and the canonical per-attachment download endpoint.
    No credential-bearing URL, ambient cookies, proxy auth, or redirect forwarding.
    """
    def __init__(self, provider, repository, token=None, api_root=None):
        validate_source(provider,repository)
        require(token is None or isinstance(token,str) and token and not any(c.isspace() for c in token), 'invalid publishing credential')
        self.provider=provider; self.repository=repository; self.token=token
        self.root=api_root or {'github':'https://api.github.com','gitee':'https://gitee.com/api/v5','gitea':GITEA_ORIGIN+'/api/v1'}[provider]
        self.test_root=api_root
        if api_root:
            p=urllib.parse.urlsplit(api_root)
            require(p.scheme=='http' and p.hostname=='127.0.0.1' and not p.username and not p.password and not p.query and not p.fragment and p.path=='', 'custom API root must be loopback')
            if provider=='gitea': self.root+='/api/v1'
        self.prefix='/repos/'+repository
        self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect())

    def anonymous(self):
        return ReleaseApi(self.provider,self.repository,api_root=self.test_root)

    def _open(self,url,method='GET',data=None,headers=None,authenticated=False,attachment=None):
        values={'User-Agent':'DFLegacy-V4-publisher/1.0','Accept':'application/json'}
        values.update(headers or {})
        if authenticated:
            require(self.token is not None, 'publishing credential is unavailable')
            allowed=[self.root+self.prefix+'/']
            if self.provider=='github': allowed.append('https://uploads.github.com'+self.prefix+'/')
            if self.provider=='gitea' and attachment is not None:
                self.validate_gitea_url(url,*attachment)
            else:
                require(url==self.root+self.prefix or any(url.startswith(p) for p in allowed), 'untrusted authenticated API destination')
            values['Authorization']=('token ' if self.provider=='gitea' else 'Bearer ')+self.token
        try:
            return self.opener.open(urllib.request.Request(url,data=data,method=method,headers=values),timeout=120)
        except urllib.error.HTTPError as error: return error
        except (OSError,ValueError,urllib.error.URLError): raise ReleaseError('provider connection failed; inspect the incomplete release before retrying') from None

    def request(self,method,path,data=None,missing_ok=False,authenticated=False):
        require((path=='' or path.startswith('/')) and '\\' not in path and '..' not in path, 'invalid repository API path')
        body=json_bytes(data) if data is not None else None
        with self._open(self.root+self.prefix+path,method,body,{'Content-Type':'application/json'},authenticated) as response:
            if response.status==404 and missing_ok: return None
            require(200<=response.status<300, f'provider API failed (HTTP {response.status})')
            raw=response.read(MAX_METADATA+1)
            require(len(raw)<=MAX_METADATA, 'provider metadata too large')
            try: return json.loads(raw)
            except (ValueError,UnicodeError): raise ReleaseError('invalid provider metadata') from None

    def listing(self,path,authenticated=False):
        result=[]
        # This instance caps responses at 50 even when a higher limit is asked.
        page_size=50 if self.provider=='gitea' else 100
        parameter='limit' if self.provider=='gitea' else 'per_page'
        for page in range(1,101):
            batch=self.request('GET',path+f'?{parameter}={page_size}&page={page}',authenticated=authenticated)
            require(isinstance(batch,list) and len(batch)<=page_size and all(isinstance(x,dict) for x in batch), 'invalid provider listing')
            result.extend(batch)
            if len(batch)<page_size: return result
        raise ReleaseError('provider listing limit reached; refusing partial inventory')

    def release(self,tag,authenticated=False):
        require(name(tag), 'invalid release tag')
        row=self.request('GET','/releases/tags/'+tag,missing_ok=True,authenticated=authenticated)
        if row is not None:
            require(isinstance(row,dict) and row.get('tag_name')==tag and integer(row.get('id'),1), 'invalid release identity')
        return row

    def attachments(self,release,authenticated=False):
        require(integer(release.get('id'),1), 'invalid release ID')
        suffix='attach_files' if self.provider=='gitee' else 'assets'
        rows=self.listing(f'/releases/{release["id"]}/{suffix}',authenticated)
        names=set(); ids=set()
        for asset in rows:
            require(integer(asset.get('id'),1) and asset['id'] not in ids and isinstance(asset.get('name'),str)
                    and asset['name'] not in names and integer(asset.get('size')), 'invalid or duplicate attachment metadata')
            names.add(asset['name']); ids.add(asset['id'])
        return rows

    def existing_storage(self):
        total=0
        for release in self.listing('/releases',authenticated=True):
            total+=sum(a['size'] for a in self.attachments(release,authenticated=True))
        return total

    def create(self,m,target_commitish):
        require(checksum(target_commitish,40), 'target_commitish must be the existing public repository commit SHA')
        body=dict(tag_name=m['version'],name=m['version'],body='DFLegacy V4 client resources. Manifest is uploaded only after resources are verified.',
                  target_commitish=target_commitish,prerelease=self.provider in ('gitee','gitea') or m['flavor']=='test')
        if self.provider in ('github','gitea'): body['draft']=True
        release=self.request('POST','/releases',body,authenticated=True)
        require(isinstance(release,dict) and integer(release.get('id'),1) and release.get('tag_name')==m['version'], 'created release identity differs')
        if self.provider=='gitea': require(release.get('draft') is True, 'Gitea did not create the required draft; attachments were not uploaded')
        return release

    def upload(self,release,path,asset_name):
        require(integer(release.get('id'),1) and name(asset_name), 'invalid upload identity')
        path=Path(path); no_links(path)
        if self.provider=='github':
            root=self.test_root or 'https://uploads.github.com'
            url=root+self.prefix+f'/releases/{release["id"]}/assets?name='+asset_name
            prefix=b''; suffix=b''; content_type='application/octet-stream'
        else:
            endpoint='assets?name='+asset_name if self.provider=='gitea' else 'attach_files'
            url=self.root+self.prefix+f'/releases/{release["id"]}/'+endpoint
            boundary='dflegacy-'+uuid.uuid4().hex
            field='attachment' if self.provider=='gitea' else 'file'
            prefix=(f'--{boundary}\r\nContent-Disposition: form-data; name="{field}"; filename="{asset_name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()
            suffix=f'\r\n--{boundary}--\r\n'.encode(); content_type='multipart/form-data; boundary='+boundary
        def stream():
            yield prefix
            with path.open('rb') as file:
                while chunk:=file.read(1024*1024): yield chunk
            yield suffix
        headers={'Content-Type':content_type,'Content-Length':str(len(prefix)+path.stat().st_size+len(suffix))}
        with self._open(url,'POST',stream(),headers,authenticated=True) as response:
            require(response.status==201,f'provider upload failed (HTTP {response.status}); manifest may remain unpublished')
            # Never trust or print a server-supplied upload URL: re-read structured attachments.
            response.read(MAX_METADATA+1)

    def download_url(self,release,asset,draft=False):
        if self.provider=='gitee':
            return self.root+self.prefix+f'/releases/{release["id"]}/attach_files/{asset["id"]}/download'
        if self.provider=='gitea':
            value=asset.get('browser_download_url')
            self.validate_gitea_url(value,release,asset)
            return value
        if draft:
            return self.root+self.prefix+f'/releases/assets/{asset["id"]}'
        expected='https://github.com/'+self.repository+'/releases/download/'+release['tag_name']+'/'+asset['name']
        value=asset.get('browser_download_url')
        require(value==expected or self.test_root and value==self.test_root+'/download/'+release['tag_name']+'/'+asset['name'], 'attachment URL escapes release/repository')
        return value

    def validate_gitea_url(self,url,release,asset):
        require(isinstance(url,str) and name(release.get('tag_name')) and name(asset.get('name')), 'invalid Gitea attachment identity')
        origin=self.test_root or GITEA_ORIGIN
        canonical=origin+'/'+self.repository+'/releases/download/'+release['tag_name']+'/'+asset['name']
        identity=asset.get('uuid')
        uuid_ok=isinstance(identity,str) and re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}',identity)
        # Exact strings exclude credentials, query/fragment, percent escapes,
        # alternate ports, traversal and another repository, tag, asset or UUID.
        require(url==canonical or uuid_ok and url==origin+'/attachments/'+identity,
                'Gitea attachment URL escapes its source-bound identity')

    def validate_redirect(self,url,release=None,asset=None):
        if self.provider=='gitea':
            require(release is not None and asset is not None, 'Gitea redirect requires structured attachment identity')
            self.validate_gitea_url(url,release,asset); return
        p=urllib.parse.urlsplit(url)
        require(not p.username and not p.password and not p.fragment, 'untrusted attachment redirect')
        if self.test_root:
            require(url.startswith(self.test_root+'/'), 'attachment redirect escaped loopback'); return
        require(p.scheme=='https' and p.port in (None,443), 'untrusted attachment redirect')
        if self.provider=='github':
            require(p.hostname in ('release-assets.githubusercontent.com','objects.githubusercontent.com'), 'untrusted GitHub attachment redirect')
        else:
            # Observed anonymously from the structured attachment API (2026-11-08).
            # The CDN path is valid only as a redirect from that source-bound endpoint.
            decoded=urllib.parse.unquote(p.path)
            canonical=p.hostname=='gitee.com' and re.fullmatch(re.escape('/'+self.repository)+r'/attach_files/[1-9][0-9]*/download/[A-Za-z0-9._-]+',decoded)
            cdn=p.hostname=='foruda.gitee.com' and re.fullmatch(r'/attach_file/[1-9][0-9]*/[A-Za-z0-9._-]+',decoded)
            signed=urllib.parse.parse_qs(p.query,keep_blank_values=True)
            signed_ok=not p.query or (len(p.query)<=4096 and set(signed)=={'attname','token','ts'}
                                     and all(len(values)==1 and values[0] for values in signed.values())
                                     and re.fullmatch(r'[0-9]+',signed['ts'][0]))
            require(canonical and not p.query or cdn and signed_ok,
                    'unverified Gitee attachment redirect; record its origin before adding support')

    def verify_asset(self,release,asset,expected_hash,expected_size,draft=False,probe_range=False):
        require(asset['size']==expected_size, 'attachment size differs')
        if asset.get('digest') is not None:
            require(asset['digest']=='sha256:'+expected_hash, 'provider attachment digest differs')
        url=self.download_url(release,asset,draft)
        start=1 if probe_range and expected_size>1 else 0
        headers={'Accept':'application/octet-stream'}
        if start: headers['Range']='bytes=1-'
        for hop in range(6):
            options={'headers':headers,'authenticated':draft and hop==0}
            if self.provider=='gitea' and draft and hop==0: options['attachment']=(release,asset)
            with self._open(url,**options) as response:
                if response.status in (301,302,303,307,308):
                    url=urllib.parse.urljoin(url,response.headers.get('Location',''))
                    if self.provider=='gitea': self.validate_redirect(url,release,asset)
                    else: self.validate_redirect(url)
                    continue
                require(response.status in (200,206), f'anonymous attachment download failed (HTTP {response.status})')
                require('text/html' not in response.headers.get('Content-Type','').lower(), 'attachment returned an HTML login/challenge page')
                actual_start=start if response.status==206 else 0
                if response.status==206:
                    require(start>0 and response.headers.get('Content-Range')==f'bytes {start}-{expected_size-1}/{expected_size}', 'invalid attachment Content-Range')
                length=expected_size-actual_start
                if response.headers.get('Content-Length') is not None:
                    require(response.headers['Content-Length']==str(length), 'attachment Content-Length differs')
                digest=hashlib.sha256(); tail_digest=hashlib.sha256(); size=0
                while chunk:=response.read(min(1024*1024,length-size+1)):
                    tail_digest.update(chunk[1:] if size==0 and actual_start==0 else chunk)
                    size+=len(chunk); require(size<=length,'attachment exceeds expected size'); digest.update(chunk)
                require(size==length,'attachment truncated')
                if actual_start==0: require(digest.hexdigest()==expected_hash,'attachment SHA256 differs')
                return {'range':'supported' if response.status==206 else 'full','size':size,'sha256':digest.hexdigest(),'tailSha256':tail_digest.hexdigest()}
        raise ReleaseError('attachment redirect limit exceeded')


def bind_api(receipt,api):
    require(api.provider==receipt['provider'] and api.repository==receipt['repository'], 'provider/repository differs from bundle')


def verify_remote(bundle,api=None):
    receipt=verify_local(bundle)
    api=api or ReleaseApi(receipt['provider'],receipt['repository'])
    bind_api(receipt,api); public=api.anonymous(); m=receipt['manifest']
    target=public.release(m['version'])
    require(target is not None and not target.get('draft',False), 'published target is unavailable')
    resources={}
    for f in m['files']:
        for p in f['parts']: resources[(p['release'],p['asset'])]=(p['sha256'],p['size'])
    resources[(m['version'],receipt['manifestAsset'])]=(receipt['manifestSha256'],receipt['manifestSize'])
    releases={}; checks=[]
    for (tag,asset_name),(digest,size) in resources.items():
        if tag not in releases:
            release=public.release(tag)
            require(release is not None and not release.get('draft',False),'referenced immutable release is unavailable')
            releases[tag]=(release,{x['name']:x for x in public.attachments(release)})
        release,assets=releases[tag]; require(asset_name in assets,'required attachment missing from structured release list')
        full=public.verify_asset(release,assets[asset_name],digest,size)
        range_result=public.verify_asset(release,assets[asset_name],digest,size,probe_range=True) if size>1 else {'range':'empty-or-single-byte'}
        if range_result['range']=='supported': require(range_result['sha256']==full['tailSha256'],'Range bytes differ from verified full attachment')
        checks.append(dict(release=tag,asset=asset_name,sha256=digest,size=size,range=range_result['range']))
    fresh=public.release(m['version'])
    require(fresh is not None and fresh['id']==target['id'],'release identity changed during verification')
    return dict(status='published-verified',provider=receipt['provider'],repository=receipt['repository'],version=m['version'],
                releaseId=target['id'],manifestSha256=receipt['manifestSha256'],checks=checks)


def publish(bundle,api=None,target_commitish=None):
    """Compatibility entry for one variant; grouped releases use publish_many."""
    return publish_many([bundle],api,target_commitish)['bundles'][0]


def publish_many(bundles,api=None,target_commitish=None):
    require(isinstance(bundles,(list,tuple)) and 0<len(bundles)<=4, 'one to four unique variant bundles are required')
    prepared=[(Path(bundle),verify_local(bundle)) for bundle in bundles]
    receipt=prepared[0][1]; m=receipt['manifest']; variants=set(); resources={}; manifests=[]; references={}
    identity_keys=('version','commit','sequence','minimumSequence','channel')
    for bundle,item in prepared:
        variant=item['manifest']
        require((item['provider'],item['repository'])==(receipt['provider'],receipt['repository'])
                and all(variant[key]==m[key] for key in identity_keys), 'grouped bundle release identity differs')
        name_key=(variant['architecture'],variant['flavor'])
        require(name_key not in variants, 'duplicate architecture/flavor in grouped release'); variants.add(name_key)
        for file in variant['files']:
            for part in file['parts']:
                key=(part['release'],part['asset']); identity=(part['sha256'],part['size'])
                require(key not in references or references[key]==identity,'conflicting grouped resource reference')
                references[key]=identity
        for asset,metadata in item['assets'].items():
            identity=(metadata['sha256'],metadata['size'])
            require(asset not in resources or resources[asset][1:]==identity, 'conflicting grouped attachment identity')
            resources[asset]=(bundle/'assets'/asset,*identity)
        manifests.append((item['manifestAsset'],bundle/item['manifestAsset'],item['manifestSha256'],item['manifestSize']))
    # This preflight is local and counts the final deduplicated attachment set.
    preflight_limits(receipt['provider'],[value[2] for value in resources.values()]+[value[3] for value in manifests])
    if api is None:
        token=os.environ.get('DFLEGACY_'+receipt['provider'].upper()+'_RELEASE_TOKEN')
        require(token is not None,'dedicated provider publishing credential is unavailable')
        api=ReleaseApi(receipt['provider'],receipt['repository'],token)
    bind_api(receipt,api)
    require(checksum(target_commitish,40),'target_commitish must be the existing public repository commit SHA')
    repository=api.request('GET','',authenticated=True)
    require(isinstance(repository,dict) and repository.get('private') is False, 'publishing requires a public repository')
    require(api.release(m['version'],authenticated=True) is None,'release tag is immutable; use a new version')
    # The GitHub tag endpoint omits drafts. Never accidentally mutate an interrupted draft.
    existing=api.listing('/releases',authenticated=True)
    require(not any(row.get('tag_name')==m['version'] for row in existing),'release tag is immutable; use a new version')
    current_bytes=api.existing_storage() if api.provider=='gitee' else 0
    preflight_limits(api.provider,[value[2] for value in resources.values()]+[value[3] for value in manifests],current_bytes)
    public=api.anonymous(); checked=set()
    for _,item in prepared:
        for f in item['manifest']['files']:
            for p in f['parts']:
                if p['release']==m['version'] or (p['release'],p['asset']) in checked: continue
                old=public.release(p['release']); require(old is not None and not old.get('draft',False),'reused release unavailable')
                old_assets={a['name']:a for a in public.attachments(old)}
                require(p['asset'] in old_assets,'reused attachment unavailable')
                public.verify_asset(old,old_assets[p['asset']],p['sha256'],p['size']); checked.add((p['release'],p['asset']))
    release=api.create(m,target_commitish)
    # No manifest is visible until every resource needed by every variant verifies.
    inventory=[(asset,*metadata) for asset,metadata in resources.items()]+manifests
    for asset,path,digest,size in inventory:
        require(file_hash(path)==(digest,size),'local attachment changed before upload')
        api.upload(release,path,asset)
        assets={a['name']:a for a in api.attachments(release,authenticated=True)}
        require(asset in assets,'uploaded attachment absent from structured release list')
        draft=api.provider in ('github','gitea')
        verifier=api if draft else public
        verifier.verify_asset(release,assets[asset],digest,size,draft=draft)
    if api.provider in ('github','gitea'): api.request('PATCH',f'/releases/{release["id"]}',{'draft':False},authenticated=True)
    verified=[verify_remote(bundle,api)|{'manifestAsset':item['manifestAsset'],
               'architecture':item['manifest']['architecture'],'flavor':item['manifest']['flavor']} for bundle,item in prepared]
    require(all(result['releaseId']==release['id'] for result in verified),'grouped release identity changed during verification')
    return dict(status='published-verified',provider=api.provider,repository=api.repository,version=m['version'],
                releaseId=release['id'],bundles=verified)


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    commands=parser.add_subparsers(dest='command',required=True)
    for action in ('preview','build'):
        command=commands.add_parser(action); command.add_argument('--request',required=True)
        if action=='build': command.add_argument('--output',required=True)
    command=commands.add_parser('verify'); command.add_argument('--bundle',required=True); command.add_argument('--remote',action='store_true')
    command=commands.add_parser('publish'); command.add_argument('--bundle',required=True,action='append',help='repeat for each architecture/flavor in the same immutable release'); command.add_argument('--target-commitish',required=True)
    args=parser.parse_args(argv)
    try:
        if args.command=='verify': result=verify_remote(args.bundle) if args.remote else verify_local(args.bundle)
        elif args.command=='publish': result=publish_many(args.bundle,target_commitish=args.target_commitish)
        else:
            request=load_json(args.request)
            if args.command=='build': result=build(args.output,**request)
            else:
                provider=request.pop('provider'); repository=request.pop('repository')
                result=plan_resources(**request); source_identity(provider,repository,result['manifest'])
                preflight_limits(provider,[x['size'] for x in result['assets'].values()]+[len(json_bytes(result['manifest']))])
                result.update(provider=provider,repository=repository)
        print(json.dumps(result,ensure_ascii=True,allow_nan=False)); return 0
    except ReleaseError as error:
        print(str(error),file=sys.stderr); return 2
    except (OSError,ValueError,TypeError,KeyError,RecursionError):
        print('V4 operation failed: invalid input, inaccessible file or transport failure.',file=sys.stderr); return 2


if __name__=='__main__': sys.exit(main())
