import os
from pathlib import Path
import subprocess

SCRIPT = Path(__file__).resolve().parents[1] / 'sign_launcher.ps1'
CERT_ENV = ('DFLEGACY_SIGN_PFX', 'DFLEGACY_SIGN_PFX_PASSWORD', 'DFLEGACY_SIGN_THUMBPRINT')


def clean_env():
    env = os.environ.copy()
    for name in CERT_ENV:
        env.pop(name, None)
    return env


def run(args, env):
    return subprocess.run(['pwsh', '-NoProfile', '-File', str(SCRIPT), *args],
                          capture_output=True, text=True, encoding='utf-8', errors='replace',
                          timeout=120, env=env)


def test_missing_certificate_fails(tmp_path):
    executable = tmp_path / 'DFOLauncher.exe'
    executable.write_bytes(b'MZ')
    result = run(['-Files', str(executable)], clean_env())
    assert result.returncode != 0
    assert '证书' in result.stdout + result.stderr


def test_missing_pfx_fails(tmp_path):
    executable = tmp_path / 'DFOLauncher.exe'
    executable.write_bytes(b'MZ')
    env = clean_env()
    env['DFLEGACY_SIGN_PFX'] = str(tmp_path / 'missing.pfx')
    env['DFLEGACY_SIGN_PFX_PASSWORD'] = 'test-password'
    result = run(['-Files', str(executable)], env)
    assert result.returncode != 0
    assert 'PFX' in result.stdout + result.stderr


def test_missing_password_fails(tmp_path):
    executable = tmp_path / 'DFOLauncher.exe'
    executable.write_bytes(b'MZ')
    pfx = tmp_path / 'test.pfx'
    pfx.write_bytes(b'pfx')
    env = clean_env()
    env['DFLEGACY_SIGN_PFX'] = str(pfx)
    result = run(['-Files', str(executable)], env)
    assert result.returncode != 0
    assert 'DFLEGACY_SIGN_PFX_PASSWORD' in result.stdout + result.stderr
