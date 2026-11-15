"""Build and serve the isolated win-x64 normal chapter bridge."""
import argparse
import datetime
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import re
import shutil
import stat
import struct
import zipfile


HOST = '127.0.0.2'
MAX_ARCHIVE = 256 * 1024 * 1024
MAX_EXPANDED = 512 * 1024 * 1024
MAX_MANIFEST = 16 * 1024
MANDATORY = {'DFLegacy.Launcher.exe', 'DFLegacy.Client.dll'}
HELPERS = {'DFLegacy.Diagnostics.exe', 'DFLegacy.CrashCapture.exe'}
PVF = 'Script.pvf'
NAME = r'[A-Za-z0-9][A-Za-z0-9._-]{0,127}'
SLOT = 'v1/x64/normal'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def ordinary(path):
    for item in (path, *path.parents):
        try:
            info = item.lstat()
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & 0x400,
                'bridge paths must not cross links or reparse points')
    return path


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def identity(path, bound=MAX_EXPANDED):
    ordinary(path)
    require(path.is_file() and 0 < path.stat().st_size <= bound, f'bridge file missing or too large: {path.name}')
    return {'path': path.name, 'size': path.stat().st_size, 'sha256': digest(path)}


def encoded(record):
    return (json.dumps(record, ensure_ascii=False, indent=2, allow_nan=False) + '\n').encode('utf-8')


def manifest(version, commit, rows):
    require(isinstance(version, str) and re.fullmatch(NAME, version), 'invalid legacy version')
    require(re.fullmatch(r'[a-f0-9]{40}', commit), 'invalid legacy commit')
    names = {row['path'] for row in rows}
    require(len(names) == len(rows) and MANDATORY <= names <= MANDATORY | HELPERS | {PVF}
            and (not names & HELPERS or HELPERS <= names), 'invalid legacy manifest file set')
    require(sum(row['size'] for row in rows) <= MAX_EXPANDED, 'legacy payload exceeds expanded size limit')
    result = encoded(dict(schemaVersion=2, version=version, commit=commit, flavor='normal', files=rows))
    require(len(result) <= MAX_MANIFEST, 'legacy manifest is too large')
    return result


def check_launcher(path):
    with path.open('rb') as file:
        header = file.read(64)
        require(len(header) == 64 and header[:2] == b'MZ', 'invalid launcher PE header')
        offset = struct.unpack_from('<I', header, 60)[0]
        require(64 <= offset <= 1024 * 1024, 'invalid launcher PE offset')
        file.seek(offset)
        header = file.read(26)
        require(len(header) == 26 and header[:6] == b'PE\0\0\x64\x86' and header[24:26] == b'\x0b\x02',
                'hosted bridge requires a win-x64 launcher')


def package_rows(directory, include_pvf):
    require(directory.is_dir(), 'bridge input directory is missing')
    names = MANDATORY | {name for name in HELPERS if ordinary(directory / name).exists()}
    if include_pvf and ordinary(directory / PVF).exists():
        names.add(PVF)
    rows = [identity(directory / name) for name in sorted(names)]
    check_launcher(directory / 'DFLegacy.Launcher.exe')
    return rows


def build(old, new, output, old_version, version, commit, release_id, old_commit=None):
    old, new, output = (ordinary(Path(path).absolute()) for path in (old, new, output))
    require(old != new and old not in output.parents and new not in output.parents and output not in (old, new),
            'bridge output must be outside both input directories')
    require(type(release_id) is int and 0 < release_id <= 2**63 - 1 and old_version != version,
            'bridge requires a newer release ID and a distinct version')
    old_rows = package_rows(old, include_pvf=True)
    new_rows = package_rows(new, include_pvf=False)
    old_manifest = manifest(old_version, old_commit or commit, old_rows)
    update_manifest = manifest(version, commit, new_rows)
    # V2/V3 compatibility requires the original install to have an original
    # version of every file in the hosted ZIP, so it can recover by byte identity.
    require({row['path'] for row in new_rows} <= {row['path'] for row in old_rows},
            'hosted ZIP cannot introduce files outside the old standalone manifest')
    # The old installer backs up the new payload set, then validates every
    # original runtime file. Only its existing PVF is borrowed from the root.
    require({row['path'] for row in old_rows} - {PVF} <= {row['path'] for row in new_rows},
            'hosted ZIP must preserve every original runtime helper for legacy backup validation')
    require(not output.exists() and output.parent.is_dir(), 'bridge output must be a new directory')
    output.mkdir()
    slot = output / SLOT
    slot.mkdir(parents=True)
    archive = slot / 'building.zip'
    try:
        with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as package:
            for row in new_rows:
                with (new / row['path']).open('rb') as source, package.open(row['path'], 'w') as target:
                    shutil.copyfileobj(source, target, 128 * 1024)
            package.writestr('update-manifest.json', update_manifest)
        with zipfile.ZipFile(archive) as package:
            for row in new_rows:
                with package.open(row['path']) as content:
                    require(package.getinfo(row['path']).file_size == row['size']
                            and hashlib.file_digest(content, 'sha256').hexdigest() == row['sha256'],
                            'bridge input changed during ZIP creation')
        result = identity(archive, MAX_ARCHIVE)
        final = slot / (result['sha256'] + '.zip')
        archive.rename(final)
        (output / 'client-package.json').write_bytes(old_manifest)
        (output / 'update-manifest.json').write_bytes(update_manifest)
        metadata = dict(id=release_id, version=version,
                        publishedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                        size=result['size'], sha256=result['sha256'])
        (slot / 'latest').write_bytes(encoded(metadata))
        return metadata
    finally:
        archive.unlink(missing_ok=True)


def bridge_migration(port, channel, flavor='normal'):
    require(type(port) is int and 0 < port <= 65535 and re.fullmatch(NAME, channel) and flavor == 'normal',
            'invalid bridge migration source')
    origin = f'http://{HOST}:{port}'
    return dict(id='bind-local-chapter-one', file='launcher.json', operation='bindLocalBridge', key='UpdateSource',
                value=dict(provider='local', repository=origin + '/c1', channel=channel,
                           architecture='x64', flavor=flavor), when=origin)


def create_server(root, port):
    root = ordinary(Path(root).absolute())
    require(root.is_dir() and type(port) is int and 0 <= port <= 65535, 'invalid local channel root or port')
    v4_manifest = re.compile(rf'/(?:c1|c2)/DFLegacy-client-update-v4-{NAME}-(?:normal|test)-win-x64\.json')
    v4_part = re.compile(rf'/(?:c1|c2)/releases/{NAME}/{NAME}\.bin')
    v3_archive = re.compile(rf'/{SLOT}/[a-f0-9]{{64}}\.zip')

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_HEAD(self):
            self.respond(False)

        def do_GET(self):
            self.respond(True)

        def respond(self, send_body):
            request = self.path
            if self.client_address[0] not in ('127.0.0.1', HOST) or not (
                    request == '/' + SLOT + '/latest' or v3_archive.fullmatch(request)
                    or v4_manifest.fullmatch(request) or v4_part.fullmatch(request)):
                self.send_error(404)
                return
            try:
                path = ordinary(root.joinpath(*request[1:].split('/')))
                stream = path.open('rb')
            except (OSError, ValueError):
                self.send_error(404)
                return
            with stream:
                size = stream.seek(0, 2)
                offset = 0
                partial = False
                version = str(path.stat().st_mtime_ns)
                etag = f'"{version}-{size}"'
                requested = self.headers.get('Range')
                if requested and self.headers.get('If-Range', etag) == etag:
                    match = re.fullmatch(r'bytes=([0-9]{1,19})-', requested)
                    if not match or int(match[1]) >= size:
                        self.send_response(416)
                        self.send_header('Content-Range', f'bytes */{size}')
                        self.send_header('Content-Length', '0')
                        self.end_headers()
                        return
                    offset = int(match[1])
                    partial = True
                self.send_response(206 if partial else 200)
                self.send_header('Content-Type', 'application/json' if path.suffix == '.json' or path.name == 'latest'
                                 else 'application/zip' if path.suffix == '.zip' else 'application/octet-stream')
                self.send_header('Content-Length', str(size - offset))
                self.send_header('Cache-Control', 'no-store')
                self.send_header('X-Content-Type-Options', 'nosniff')
                self.send_header('Accept-Ranges', 'bytes')
                self.send_header('ETag', etag)
                if partial:
                    self.send_header('Content-Range', f'bytes {offset}-{size - 1}/{size}')
                self.end_headers()
                if not send_body:
                    return
                stream.seek(offset)
                try:
                    remaining = size - offset
                    while remaining:
                        chunk = stream.read(min(remaining, 128 * 1024))
                        if not chunk:
                            break
                        self.wfile.write(chunk)
                        remaining -= len(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    pass

    return ThreadingHTTPServer((HOST, port), Handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    package = commands.add_parser('build')
    for option in ('old', 'new', 'output', 'old-version', 'version', 'commit'):
        package.add_argument('--' + option, required=True)
    package.add_argument('--old-commit')
    package.add_argument('--id', required=True, type=int)
    serve = commands.add_parser('serve')
    serve.add_argument('--root', required=True)
    serve.add_argument('--port', required=True, type=int)
    migration = commands.add_parser('migration')
    migration.add_argument('--port', required=True, type=int)
    migration.add_argument('--channel', default='c1-bridge')
    args = parser.parse_args()
    if args.command == 'build':
        print(encoded(build(args.old, args.new, args.output, args.old_version, args.version, args.commit,
                            args.id, args.old_commit)).decode('utf-8'), end='')
    elif args.command == 'migration':
        print(encoded([bridge_migration(args.port, args.channel)]).decode('utf-8'), end='')
    else:
        with create_server(args.root, args.port) as server:
            print(f'http://{HOST}:{server.server_address[1]}', flush=True)
            server.serve_forever()


if __name__ == '__main__':
    main()
