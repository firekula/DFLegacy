"""Read-only full installed-resource audit; never include account values in output."""
import argparse
import base64
import datetime
import hashlib
import json
from pathlib import Path


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def audit(root, baseline_config, config, output):
    root = Path(root).resolve(strict=True)
    receipt = json.loads((root / '.dflegacy/v4/installed.json').read_bytes())
    raw = base64.b64decode(receipt['release']['manifestBytes'], validate=True)
    if hashlib.sha256(raw).hexdigest() != receipt['release']['manifestSha256']:
        raise ValueError('Installed manifest identity differs')
    manifest = json.loads(raw)
    mismatches = []
    for row in manifest['files']:
        path = root.joinpath(*row['path'].split('/'))
        if not path.resolve().is_relative_to(root):
            raise ValueError('Managed path escapes client root')
        if not path.is_file():
            mismatches.append(dict(path=row['path'], reason='missing'))
        elif path.stat().st_size != row['size'] or sha(path) != row['sha256']:
            mismatches.append(dict(path=row['path'], reason='content differs'))
    before = json.loads(Path(baseline_config).read_bytes())
    after = json.loads((root / config).read_bytes())
    accounts = {name: before.get(name) == after.get(name)
                for name in ('Accounts', 'AccountIndex', 'Account', 'Password')}
    retired_present = [row['path'] for row in manifest['retired']
                       if row['action'] == 'delete' and (root / row['path']).exists()]
    checkpoint_path = root / '.dflegacy/chapter-transition/checkpoint.json'
    checkpoint = json.loads(checkpoint_path.read_bytes()) if checkpoint_path.exists() else None
    result = dict(time=datetime.datetime.now(datetime.timezone.utc).isoformat(), root=str(root),
                  version=manifest['version'], sequence=manifest['sequence'],
                  chapter=json.loads((root / 'DFLegacy.Chapter.json').read_bytes())['chapter'],
                  manifestSha256=hashlib.sha256(raw).hexdigest(), files=len(manifest['files']),
                  bytes=sum(row['size'] for row in manifest['files']), mismatches=mismatches,
                  retiredPresent=retired_present, accountFieldsUnchanged=accounts,
                  checkpointPresent=checkpoint_path.exists(),
                  checkpointSha256=sha(checkpoint_path) if checkpoint else None,
                  baselineManifestSha256=checkpoint['baseline']['release']['manifestSha256'] if checkpoint else None,
                  rollbackPending=(root / '.dflegacy/chapter-transition/rollback.json').exists(),
                  rollbackHold=(root / '.dflegacy/chapter-transition/hold').exists(),
                  pendingPresent=(root / '.dflegacy/v4/pending.json').exists(),
                  display=after.get('ChapterTwoDisplay'))
    Path(output).write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    return (not mismatches and not retired_present and all(accounts.values())
            and not result['pendingPresent'] and not result['rollbackPending'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('root', 'baseline-config', 'output'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--config', default='launcher.json')
    raise SystemExit(0 if audit(**vars(parser.parse_args())) else 1)
