"""Exercise CI metadata and launch probes with temporary process/HTTP fixtures."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import sys
import json
import hashlib
import io
import os
import subprocess
from unittest.mock import patch
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location("release_ci", Path(__file__).parents[1] / "ci.py")
ci = importlib.util.module_from_spec(spec)
if Path(spec.origin).exists():
    spec.loader.exec_module(ci)


class Metadata(unittest.TestCase):
    def test_legacy_fixture_download_is_bounded_verified_and_cached(self):
        content = b'public-project-owned-fixture'
        with tempfile.TemporaryDirectory() as temporary:
            with patch.object(ci, 'LEGACY_FIXTURE_SIZE', len(content)), patch.object(ci, 'LEGACY_FIXTURE_SHA256', hashlib.sha256(content).hexdigest()):
                with patch.object(ci, 'open_legacy_fixture', return_value=io.BytesIO(content)) as opening:
                    path = ci.legacy_patch_fixture(Path(temporary))
                    self.assertEqual(path.read_bytes(), content)
                    self.assertEqual(ci.legacy_patch_fixture(Path(temporary)), path)
                    self.assertEqual(opening.call_count, 1)
                for bad in (b'short', b'x' * len(content), content + b'extra'):
                    path.unlink()
                    with patch.object(ci, 'open_legacy_fixture', return_value=io.BytesIO(bad)):
                        with self.assertRaises(ci.ReleaseError): ci.legacy_patch_fixture(Path(temporary))
                    self.assertFalse(path.exists())
                    self.assertEqual(list(Path(temporary).glob('*.part')), [])
                    path.write_bytes(b'corrupt cached fixture')

    def test_windows_build_passes_matching_native_paths_and_both_flavors(self):
        calls = []
        with tempfile.TemporaryDirectory() as temporary, patch.object(ci, 'REPO', Path(temporary)), patch.dict(os.environ), patch.object(ci, 'os', SimpleNamespace(name='nt', environ=os.environ)):
            with patch.object(ci, 'run', side_effect=lambda *args: calls.append(args)), patch.object(ci, 'legacy_patch_fixture', return_value=Path(temporary) / 'prior.zip'), patch.object(ci, 'build_client_assets', return_value=Path(temporary) / 'assets') as assets, patch.object(ci, 'make_archive'), patch.object(ci, 'publish', return_value=(None, Path(temporary) / 'win.zip')), patch.object(ci, 'smoke_archive'), patch.object(ci, 'build_client_packages') as packages:
                ci.windows('v1.2.3', 'a' * 40)
                launcher_run = next(call for call in calls if 'src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj' in call)
                self.assertTrue(any(str(arg).endswith('DFLegacy.Client.dll') and str(arg).startswith('-p:ClientPatchPrebuiltPath=') for arg in launcher_run))
                self.assertTrue(any(str(arg).endswith('DFLegacy.Bootstrap32.exe') and str(arg).startswith('-p:ClientBootstrapPrebuiltPath=') for arg in launcher_run))
                self.assertEqual(assets.call_args.kwargs['debug'].name, 'debug.dll')
                packages.assert_called_once()

    def test_metadata_exports_qualified_version_variables_that_allow_real_dotnet_restore(self):
        with tempfile.TemporaryDirectory(prefix="metadata SDK fixture ") as temporary:
            root = Path(temporary)
            environment_file, output_file = root / "environment", root / "outputs"
            commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ci.REPO, text=True).strip()
            with patch.dict(os.environ, {"BUILD_REF": "refs/tags/v1.2.3", "BUILD_COMMIT": commit,
                                        "GITHUB_ENV": str(environment_file), "GITHUB_OUTPUT": str(output_file)}):
                ci.metadata()
            exported = dict(line.split("=", 1) for line in environment_file.read_text().splitlines())
            self.assertEqual(set(exported), {"DFLEGACY_RELEASE_VERSION", "DFLEGACY_RELEASE_COMMIT", "SOURCE_DATE_EPOCH"})
            self.assertEqual(exported["DFLEGACY_RELEASE_VERSION"], "v1.2.3")
            project = root / "Fixture.csproj"
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
            result = subprocess.run(["dotnet", "restore", str(project)], cwd=root, env=ci.dotnet_environment() | exported,
                                    capture_output=True)
            self.assertEqual(result.returncode, 0, (result.stdout + result.stderr).decode("utf-8", errors="replace"))

    def test_oci_metadata_is_canonical_across_build_refs_and_clock_fields(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "metadata.json"
            digest = "sha256:" + "a" * 64
            outputs = []
            for nonce in ("run-one", "run-two"):
                path.write_text(json.dumps({"containerimage.digest": digest, "buildx.build.ref": nonce, "buildx.build.provenance": {"created": nonce}}))
                ci.normalize_oci_metadata(path, "v1.2.3", "b" * 40, "1793344496")
                outputs.append(path.read_bytes())
            self.assertEqual(outputs[0], outputs[1])
            self.assertEqual(json.loads(outputs[0])["source_date_epoch"], 1793344496)
            path.write_text('{"containerimage.digest":"invalid"}')
            with self.assertRaises(ci.ReleaseError):
                ci.normalize_oci_metadata(path, "v1.2.3", "b" * 40, "1793344496")

    def test_push_metadata_is_reproducible_and_tag_is_validated(self):
        commit = "b" * 40
        self.assertEqual(ci.build_version("refs/heads/main", commit, "2026-10-30T12:34:56+08:00"), "dev-20261030T043456Z-bbbbbbbbbbbb")
        self.assertEqual(ci.build_version("refs/tags/v1.2.3", commit, "2026-10-30T12:34:56Z"), "v1.2.3")
        with self.assertRaises(ValueError):
            ci.build_version("refs/tags/v1.bad", commit, "2026-10-30T12:34:56Z")

    def test_smoke_runs_process_and_rejects_healthy_game_before_setup(self):
        # Windows venv uses a redirector executable. This stdlib-only fixture
        # must be the actual leaf process that smoke_command terminates/waits.
        interpreter = getattr(sys, "_base_executable", sys.executable) if sys.platform == "win32" else sys.executable
        with tempfile.TemporaryDirectory(prefix="DFLegacy probe ") as temporary:
            root = Path(temporary)
            source = root / "fake_manager.py"
            source.write_text('''import http.server, sys
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200 if self.path == '/health' else 503)
  self.end_headers()
 def log_message(self, *args): pass
http.server.HTTPServer(('127.0.0.1', int(sys.argv[1])), Handler).serve_forever()
''')
            ci.smoke_command(lambda port, data: [interpreter, str(source), str(port)], root.parent)
            source.write_text(source.read_text().replace("else 503", "else 200"))
            with self.assertRaises(ci.ReleaseError):
                ci.smoke_command(lambda port, data: [interpreter, str(source), str(port)], root.parent)


if __name__ == "__main__":
    unittest.main()
