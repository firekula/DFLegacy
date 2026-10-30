from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import shutil
import subprocess

R = Path(__file__).resolve().parents[2]
T = Path('G:/Game/DNF_ACT1/DFLegacy/.tmp/u05-ui-followup-20261030')
E = T / 'evidence-ui-v1'
S = R / '.tmp/u05-ui-followup-20261030/review-static'
A = R / 'knowledge/decisions/assets/2026-10-30-u05-mouse-dialog-followup'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()

review = read(S / 'implementation-review.json')
for row in review['source_files']:
    assert sha(R / row['path']) == row['sha256'].upper(), row['path']
debug_hash = 'B4E03F330153AF6D60BB10B7EB61D3D6D3DCD008DCB722226028BB61BC921E69'
ijl_hash = '0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A'
for name, source, expected in [('debug.dll', 'debug', debug_hash), ('ijl15.dll', 'ijl15', ijl_hash)]:
    assert sha(T / 'native-v1' / name) == expected
    assert sha(R / f'artifacts/native/{source}/Release/{name}') == expected
deployed = read(T / 'deployed-files.json')
for role in 'ABCD':
    for name, expected in [('debug.dll', debug_hash), ('ijl15.dll', ijl_hash)]:
        rows = [x for x in deployed if x['Path'].replace('\\', '/').endswith(f'Loot4_{role}/{name}')]
        assert len(rows) == 1 and rows[0]['Sha256'] == expected

j = read(T / 'deployment.json')
assert j['Status'] == 'stopped' and j['ObservedActiveChannelSessionsBeforeStop'] == 0
for name in ['replacement-backups.json', 'protected-files.json']:
    for row in read(T / 'preflight' / name):
        assert sha(Path(row['Path'])) == row['Sha256']
        if 'Backup' in row:
            assert sha(Path(row['Backup'])) == row['Sha256']
assert subprocess.run(['powershell', '-NoProfile', '-Command',
    'if (@(Get-Process -Name DNF,DFLegacy.Server,DFLegacy.Launcher -ErrorAction SilentlyContinue).Count) {exit 1}; exit 0'],
    capture_output=True).returncode == 0

login = read(E / 'ui-four-login-native-gates.json')['results']
forms = read(E / 'ui-four-new-forms-result.json')['results']
mail = read(E / 'ui-four-mail-button-state.json')['results']
token = lambda value: int(value, 16)
identities = {}
raw_facts = {}
for role in 'ABCD':
    sdk = login[role]['sdk']
    command = sdk['ui']['command_input']
    identities[role] = dict(pid=sdk['pid'], process_started_filetime=sdk['process_started_filetime'],
        uid=sdk['me']['uid'], actor_token=command['actor_token'], checker_token=command['checker_token'])
    raw = read(E / f'ui-{role}-bootstrap-raw.json')
    assert raw['pid'] == sdk['pid'] and raw['process_started_filetime'] == sdk['process_started_filetime']
    events = [x for x in raw['events'] if x.get('command')]
    for event in events:
        c = event['command']
        assert c['uid'] == sdk['me']['uid'] and c['sdk_uid_bound']
        assert token(event['actor_token']) == token(command['actor_token'])
        assert token(c['checker_token']) == token(command['checker_token'])
    first_two = next(x['tick_ms'] for x in events if x['command']['mouse_lock_level'] == 2)
    released = next(x['tick_ms'] for x in events if x['tick_ms'] > first_two and x['command']['mouse_lock_level'] == 0)
    raw_facts[role] = dict(bootstrap_samples=raw['samples'], type1_level2_tick=first_two,
        later_level0_tick=released, same_bound_actor_checker_uid=True)
    for stage, class_id in [('create', 7), ('friend', 9)]:
        opened = forms[role][stage + '_open']; closed = forms[role][stage + '_closed']
        for sample in (opened, closed):
            assert (sample['pid'], sample['process_started_filetime'], sample['me']['uid']) == (
                sdk['pid'], sdk['process_started_filetime'], sdk['me']['uid'])
            assert sample['ui']['input_blocked'] is None
            assert sample['ui']['command_input']['mouse']['physical_excluded'] is False
        assert opened['ui']['command_input']['keyboard_lock_level'] == 1
        assert closed['ui']['command_input']['keyboard_lock_level'] == 0
        item = next(x for x in opened['ui']['dialogs']['items'] if x['class_id'] == class_id)
        assert item['shown_raw'] and item['input_enabled_raw']
        assert item['placement']['attached'] and item['placement']['order'] == 2
        assert item['placement']['layer_shown_raw'] and item['placement']['layer_input_enabled_raw']
        assert not any(x['class_id'] == class_id for x in closed['ui']['dialogs']['items'])
    path = E / (f'ui-{role}-mail-native-gate.json' if role in 'BC' else (
        'ui-A-mail-icon-native-gate.json' if role == 'A' else 'ui-D-mail-native-gate-fine.json'))
    pulse = read(path)
    true_event = next(x for x in pulse['events'] if x['mouse_button_deferred'] is True)
    false_event = next(x for x in pulse['events'] if x['tick_ms'] > true_event['tick_ms'] and x['mouse_button_deferred'] is False)
    for event in (true_event, false_event):
        assert event['command']['uid'] == sdk['me']['uid']
        assert token(event['actor_token']) == token(command['actor_token'])
        assert token(event['command']['checker_token']) == token(command['checker_token'])
    raw_facts[role].update(deferred_true_tick=true_event['tick_ms'], deferred_false_tick=false_event['tick_ms'],
        captured_file=path.name, requested_period_ms=pulse['period_ms'])
assert not any(x['mouse_button_deferred'] is True for x in read(E / 'ui-D-mail-native-gate.json')['events'])

def matches(value):
    if isinstance(value, dict):
        if 'status' in value:
            assert value['status'] == 'matched' and value['ok']
            return 1
        return sum(matches(v) for v in value.values())
    return 0

form_cli = sum(matches(forms[role]['cli']) for role in 'ABCD')
release_cli = sum(matches(mail[role]['cli_released']) for role in 'BCD')
replay = read(T / 'closed-sdk-cli-replay.json')
replay_count = sum(matches(value) for value in replay['results'].values())
assert (form_cli, release_cli, replay_count) == (21, 3, 16)
assert replay['global_unknown_timed_out']
assert all(x['exit_code'] == 0 for x in read(R / '.tmp/u05-ui-followup-20261030/native-final-results.json'))
assert '617 passed, 553 subtests passed' in (R / '.tmp/u05-ui-followup-20261030/python-final.log').read_text(encoding='utf-8-sig')

A.mkdir(parents=True, exist_ok=False)
def put(name, value):
    with (A / name).open('x', encoding='utf-8') as f:
        json.dump(value, f, indent=2, ensure_ascii=False)
def copy(src, name):
    dest = A / name
    assert not dest.exists()
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)

for name in ['ui-four-login-native-gates.json', 'ui-four-new-forms-result.json', 'ui-four-mail-button-state.json',
             'ui-D-mail-fine-state.json']:
    copy(E / name, name)
for role in 'ABCD':
    copy(E / f'ui-{role}-bootstrap-raw.json', f'ui-{role}-bootstrap-raw.json')
for name in ['ui-A-mail-icon-native-gate.json', 'ui-B-mail-native-gate.json', 'ui-C-mail-native-gate.json',
             'ui-D-mail-native-gate.json', 'ui-D-mail-native-gate-fine.json']:
    copy(E / name, name)
for name in ['closed-sdk-cli-replay.json', 'outgoing-header-audit.json', 'private-state-no-transfers.json', 'restoration-final.json']:
    copy(T / name, name)
for src, name in [
    ('ui-A-party-create-open.jpg', 'A-party-create.jpg'), ('ui-B-friend-form.jpg', 'B-friend-add.jpg'),
    ('ui-C-party-create.jpg', 'C-party-create.jpg'), ('ui-D-friend-form.jpg', 'D-friend-add.jpg'),
    ('ui-D-mail-list-open.jpg', 'D-receive-mail.jpg')]:
    copy(E / src, name)
for p in sorted(S.iterdir()):
    assert p.is_file()
    copy(p, 'static/' + p.name)
for name in ['native-final.log', 'native-final-results.json', 'python-final.log']:
    copy(R / '.tmp/u05-ui-followup-20261030' / name, name)
copy(Path(__file__).resolve(), 'provenance/publish.py')
copy(R / '.tmp/u05-ui-followup-20261030/native_sampler.py', 'provenance/native_sampler.py')
copy(R / '.tmp/u05-ui-followup-20261030/closeout_checks.py', 'provenance/closeout_checks.py')
put('identity-ledger.json', dict(phase=j['Phase'], status=j['Status'], started_at_utc=j['StartedAtUtc'],
    stopped_at_utc=j['StoppedAtUtc'], clients=j['Clients'], server=j['Server'], bindings=identities,
    active_channel_sessions_before_stop=0, cleanup_evidence=j['ChannelCleanupEvidence'],
    original_exe_sha256=review['original_exe_sha256'].upper(),
    official_elf_sha256='8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261',
    build_manifest_sha256=sha(T / 'build-manifest-v1.json'),
    journal_sha256=read(E / 'ui-four-new-forms-result.json')['journal_sha256'],
    private_seed_state_sha256=read(T / 'setup-v1.json')['SourceSha256']))
put('verification.json', dict(verified_at_utc=datetime.now(timezone.utc).isoformat(),
    debug_sha256=debug_hash, ijl_sha256=ijl_hash, final_binary_was_loaded_on_all_four_clients=True,
    product_source_unchanged_since_independent_review=True, source_files=review['source_files'],
    python=dict(tests=617, subtests=553, skipped=0, exit_code=0,
        diagnostic='Existing Windows faulthandler stacks in Unicorn.mem_map remain in the raw log; suite continued and exited0.'),
    native=read(R / '.tmp/u05-ui-followup-20261030/native-final-results.json'),
    reused_unchanged_ijl_compatibility='Prior U05 complete compatibility at exact identical IJL SHA; not repeated this turn.',
    live_cli=dict(form_conditions=form_cli, released_conditions=release_cli, total=form_cli + release_cli),
    closed_sdk_replay_conditions=replay_count, closed_global_unknown_timeout_count=4,
    final_extra_live_cli_attempt='Import failure before any condition executed; no passes counted.',
    raw_observations=raw_facts, d_first_mail_pulse_unobserved=True,
    sampling='10/2ms requested sleeps are not guaranteed temporal resolution; these are bounded read-only raw observations, not SDK history.',
    supported_classes_hex=['07','09','23','25','30','56','59','6A'],
    new_four_client_live_classes_hex=['07','09'], password56='static original native contract and fixtures only',
    input_blocked='unknown/null', no_normal_save_clone=True, restoration=read(T / 'restoration-final.json'),
    independent_review=dict(code_findings=[], final_evidence_findings=[],
        limitation='Reviewer cross-checked identities, counts and five screenshots; audited summaries only for network/restoration, without raw-state reads or independent netdump decoding.'),
    held=['U07 position sync / GP42', 'U08 stalls'], daily_package_deployed=False))
manifest = [dict(path=p.relative_to(A).as_posix(), bytes=p.stat().st_size, sha256=sha(p))
    for p in sorted(A.rglob('*')) if p.is_file()]
put('manifest.json', dict(files=manifest, excludes=['normal save', 'private raw state/accounts', 'launcher credentials', 'raw journals', 'raw encrypted netdump']))
print(json.dumps(dict(published_files=len(manifest)+1, assets=str(A), live_cli=24, replay_cli=16,
    restored_files=14, protected_files=8, raw_observations=raw_facts), ensure_ascii=False))
