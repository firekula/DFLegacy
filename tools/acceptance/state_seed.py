"""Clone one explicit/configured save snapshot and retain its provenance."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
from typing import Mapping


def is_data_path_key(key: str) -> bool:
    return key.replace('__', ':').casefold() == 'dflegacy:datapath'


def seed_state(server_json: Path, dest_dir: Path, *, seed_state: Path | None = None,
               environ: Mapping[str, str] | None = None) -> dict:
    config = Path(server_json).resolve(strict=True)
    environment = os.environ if environ is None else environ
    if seed_state is not None:
        source = Path(seed_state).resolve(strict=True)
        selection = 'explicit'
    else:
        overrides = {value for key, value in environment.items()
                     if is_data_path_key(key)}
        if len(overrides) > 1:
            raise ValueError('Conflicting DFLegacy__DataPath overrides; select --seed-state explicitly')
        raw = json.loads(config.read_text(encoding='utf-8-sig'))
        sections = [value for key, value in raw.items() if key.casefold() == 'dflegacy']
        if len(sections) != 1 or not isinstance(sections[0], dict):
            raise ValueError('Expected one DFLegacy config section')
        configured = [value for key, value in sections[0].items() if key.casefold() == 'datapath']
        if len(configured) > 1:
            raise ValueError('Conflicting configured DataPath values')
        value = next(iter(overrides)) if overrides else (configured[0] if configured else 'data/state.json')
        if not isinstance(value, str) or not value.strip():
            raise ValueError('DataPath must be a nonempty path')
        path = Path(value)
        source = (path if path.is_absolute() else config.parent / path).resolve(strict=True)
        selection = 'environment' if overrides else 'configured'
    snapshot = source.read_bytes()
    # The server replaces complete JSON snapshots atomically. Capture one opened
    # file's contents; do not hash one version and then copy another version.
    if not isinstance(json.loads(snapshot), dict):
        raise ValueError('Save snapshot must be a JSON object')
    dest = Path(dest_dir).resolve() / 'state.json'
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open('xb') as output:
        output.write(snapshot)
    digest = hashlib.sha256(snapshot).hexdigest().upper()
    return dict(schema='dfl-acceptance-state-seed/v1', selection=selection,
                source=str(source), clone=str(dest), bytes=len(snapshot),
                source_sha256=digest, clone_sha256=digest, write_back=False)
