"""Execute the shipped opt-in script; catch lost credentials/settings or unsafe defaults."""
import json
import os
from pathlib import Path
import shutil
import subprocess

import pytest


SCRIPT = Path(__file__).resolve().parents[1] / "Enable-Gitea-Test.ps1"
SHELLS = [name for name in ("powershell.exe", "pwsh") if shutil.which(name)]


def run_script(root, shell):
    return subprocess.run(
        [shell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT),
         "-ClientDirectory", str(root)], capture_output=True, timeout=30,
    )


@pytest.mark.skipif(os.name != "nt", reason="Windows packaging script")
@pytest.mark.parametrize("shell", SHELLS)
@pytest.mark.parametrize("flavor", ["normal", "test"])
def test_enable_preserves_private_and_unknown_settings_with_exact_backup(tmp_path, shell, flavor):
    assert SCRIPT.is_file(), "Shipped opt-in script is missing"
    (tmp_path / "client-package.json").write_text(json.dumps({"flavor": flavor}), encoding="utf-8")
    original = {"Account": "测试 private", "Password": "keep:$`quote\"", "Accounts": [{"Account": "alt", "Password": "secret"}],
                "StartServer": False, "ClientPath": "DNF.exe", "Relay": {"TcpPorts": [2311, 7001], "Enabled": True},
                "PrivateExtension": {"Unknown": [None, False, 987654321098765432109, "保持", {"Nested": "yes"}],
                                     "Date": "2026-11-08T13:14:15+08:00", "odd:key": "kept"},
                "UpdateProvider": "github", "DisableGitHubUpdates": True}
    before = json.dumps(original, ensure_ascii=False, indent=2).encode("utf-8")
    config = tmp_path / "launcher.json"
    config.write_bytes(before)
    result = run_script(tmp_path, shell)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
    updated = json.loads(config.read_text(encoding="utf-8-sig"))
    expected = {**original, "UpdateProvider": "gitea", "UpdateRepository": "firekula/DFLegacy", "UpdateChannel": "gitea-test",
                "UpdateFlavor": flavor, "DisableGitHubUpdates": False}
    assert updated == expected
    backups = list(tmp_path.glob("launcher.json.backup-*"))
    assert len(backups) == 1 and backups[0].read_bytes() == before


@pytest.mark.skipif(os.name != "nt", reason="Windows packaging script")
@pytest.mark.parametrize("shell", SHELLS)
def test_enable_missing_config_creates_blank_accounts_and_no_server(tmp_path, shell):
    assert SCRIPT.is_file(), "Shipped opt-in script is missing"
    (tmp_path / "client-package.json").write_text('{"flavor":"normal"}', encoding="utf-8")
    result = run_script(tmp_path, shell)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
    config = json.loads((tmp_path / "launcher.json").read_text(encoding="utf-8-sig"))
    assert config == {"Account": "", "Password": "", "StartServer": False, "ClientPath": "DNF.exe",
                      "UpdateProvider": "gitea", "UpdateRepository": "firekula/DFLegacy", "UpdateChannel": "gitea-test",
                      "UpdateFlavor": "normal", "DisableGitHubUpdates": False}


@pytest.mark.skipif(os.name != "nt", reason="Windows packaging script")
@pytest.mark.parametrize("shell", SHELLS)
@pytest.mark.parametrize("invalid", [b"{broken", b"[]", b"null"])
def test_enable_invalid_config_leaves_original_untouched(tmp_path, shell, invalid):
    assert SCRIPT.is_file(), "Shipped opt-in script is missing"
    (tmp_path / "client-package.json").write_text('{"flavor":"test"}', encoding="utf-8")
    config = tmp_path / "launcher.json"
    config.write_bytes(invalid)
    result = run_script(tmp_path, shell)
    assert result.returncode != 0
    assert config.read_bytes() == invalid
    assert not list(tmp_path.glob("launcher.json.backup-*"))
