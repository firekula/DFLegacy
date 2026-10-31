"""Behavioral release tests: disposable files, actual archives and a local Gitea API."""
import http.server
import importlib.util
import json
import pathlib
import tempfile
import threading
import unittest
import zipfile
import tarfile
import urllib.parse
import email.parser
import email.policy
import copy
import os
import platform
import subprocess
from unittest.mock import patch

MODULE = pathlib.Path(__file__).parents[1] / "release.py"
spec = importlib.util.spec_from_file_location("release_tool", MODULE)
release = importlib.util.module_from_spec(spec) if spec else None
if MODULE.exists():
    spec.loader.exec_module(release)
COMMIT = "a" * 40
VERSION = "v1.2.3"


def write(root, name, content=b"fixture"):
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content.encode() if isinstance(content, str) else content)
    return path


def app(root, name, rid):
    from test_bundle import fixture
    root.mkdir(parents=True, exist_ok=True)
    ext = ".exe" if rid.startswith("win") else ""
    managed = [name + ".dll", "System.Private.CoreLib.dll", "Fixture.Dependency.dll"]
    frameworks = ["Microsoft.NETCore.App"]
    if name == "DFLegacy.Launcher":
        managed += ["PresentationFramework.dll", "PresentationCore.dll", "WindowsBase.dll"]
        frameworks.append("Microsoft.WindowsDesktop.App")
    elif name != "DFLegacy.Channel":
        frameworks.append("Microsoft.AspNetCore.App")
        write(root, "wwwroot/index.html", "<!doctype html><title>fixture</title>")
    target = "net10.0/" + rid
    deps = {"runtimeTarget": {"name": target}, "targets": {target: {name + "/1.0": {"runtime": {item: {} for item in managed}}}}}
    entries = {item: b"fixture managed assembly" for item in managed}
    entries[name + ".deps.json"] = json.dumps(deps).encode()
    entries[name + ".runtimeconfig.json"] = json.dumps({"runtimeOptions": {"tfm": "net10.0", "includedFrameworks": [{"name": item} for item in frameworks]}}).encode()
    fixture(root / (name + ext), entries, rid=rid)
    return root


def remove_embedded(root, name, rid, entry):
    from test_bundle import fixture
    executable = root / (name + (".exe" if rid.startswith("win") else ""))
    parsed = release.read_bundle(executable, rid)
    contents = {key: parsed.read(key) for key in parsed.entries if key != entry}
    fixture(executable, contents, rid=rid)


def refresh_manifest(root, name):
    path = root / name; old = release.read_json(path); path.unlink()
    extra = {key: value for key, value in old.items() if key not in ("version", "commit", "files")}
    release.write_json(path, release.manifest(root, old["version"], old["commit"], **extra))


class Bundles(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name)

    def assets(self):
        source = app(self.root / "launcher-input", "DFLegacy.Launcher", "win-x64")
        write(source, "launcher.json", '{"Password":"PRIVATE", "ClientPath":"C:\\\\private\\\\DNF.exe"}')
        native = write(self.root, "native/DFLegacy.Client.dll")
        config = write(self.root, "native/Config.ini", "[Window]\nWidth=640\n[Debug]\nEnabled=1\n")
        output = self.root / "assets"
        debug = write(self.root, "native/debug.dll", b"paired debug fixture")
        release.build_client_assets(source, native, config, output, VERSION, COMMIT, debug)
        return output

    def test_production_rejects_complete_loose_launcher_before_player_export(self):
        legacy = self.root / "legacy-launcher"
        for name in ("DFLegacy.Launcher.exe", "DFLegacy.Launcher.dll", "PresentationFramework.dll", "PresentationCore.dll", "WindowsBase.dll", *release.runtime_files("win-x64")):
            write(legacy, name)
        write(legacy, "DFLegacy.Launcher.deps.json", '{"targets":{}}')
        write(legacy, "DFLegacy.Launcher.runtimeconfig.json", '{}')
        native = write(self.root, "legacy-native/DFLegacy.Client.dll")
        config = write(self.root, "legacy-native/Config.ini", '[Features]\nEnableDebugOverlay=0\n')
        with self.assertRaisesRegex(release.ReleaseError, "single-file"):
            release.build_client_assets(legacy, native, config, self.root / "bad-legacy-assets", VERSION, COMMIT)

    def test_real_dotnet_publish_ignores_legacy_version_environment(self):
        write(self.root, "src/Fixture/Fixture.csproj", '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
        write(self.root, "src/Fixture/Program.cs", 'System.Console.WriteLine("isolated build fixture");')
        architecture = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
        rid = ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + "-" + architecture
        output = self.root / "real-publish"
        with patch.object(release, "REPO", self.root), patch.dict(os.environ, {"VERSION": "v1.2.3", "Version": "v1.2.3"}):
            release.publish_dotnet("Fixture", rid, output)
        executable = output / ("Fixture.exe" if os.name == "nt" else "Fixture")
        result = subprocess.run([str(executable)], check=True, capture_output=True, text=True)
        self.assertEqual(result.stdout.strip(), "isolated build fixture")
        self.assertFalse((output / "Fixture.dll").exists(), "real release publish must bundle managed dependencies")
        release.check_app(output, "Fixture", rid)

    def test_single_file_app_checks_embedded_dependencies_and_runtime_identity(self):
        from test_bundle import fixture
        root = self.root / "single-file"; root.mkdir()
        target = "net10.0/win-x64"
        deps = {"runtimeTarget": {"name": target}, "targets": {target: {"Fixture/1.0": {"runtime": {"Fixture.dll": {}, "Needed.dll": {}}}}}}
        entries = {"Fixture.dll": b"fixture", "Needed.dll": b"dependency", "System.Private.CoreLib.dll": b"corelib",
                   "Fixture.deps.json": json.dumps(deps).encode(), "Fixture.runtimeconfig.json": json.dumps({"runtimeOptions": {"tfm": "net10.0", "includedFrameworks": [{"name": "Microsoft.NETCore.App"}]}}).encode()}
        fixture(root / "Fixture.exe", entries)
        release.check_app(root, "Fixture", "win-x64")
        missing = dict(entries); del missing["Needed.dll"]
        fixture(root / "Fixture.exe", missing)
        with self.assertRaisesRegex(release.ReleaseError, "Needed.dll"):
            release.check_app(root, "Fixture", "win-x64")
        wrong = copy.deepcopy(deps); wrong["runtimeTarget"]["name"] = "net10.0/linux-x64"
        fixture(root / "Fixture.exe", entries | {"Fixture.deps.json": json.dumps(wrong).encode()})
        with self.assertRaisesRegex(release.ReleaseError, "runtime target"):
            release.check_app(root, "Fixture", "win-x64")
        fixture(root / "Fixture.exe", entries | {"Fixture.runtimeconfig.json": b'{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App"}}}'})
        with self.assertRaisesRegex(release.ReleaseError, "self-contained"):
            release.check_app(root, "Fixture", "win-x64")

    def package(self, rid="linux-x64"):
        assets = self.assets()
        outputs = [app(self.root / name, assembly, rid) for name, assembly in
                   (("manager", "DFLegacy.Manager"), ("server", "DFLegacy.Server"), ("channel", "DFLegacy.Channel"))]
        return release.publish(rid, VERSION, COMMIT, assets, self.root / ("DFLegacy-" + rid),
                               *outputs, verification="cross-built; native execution not performed")

    def test_assets_clean_credentials_and_keep_complete_runtime(self):
        assets = self.assets()
        manifest = release.verify_assets(assets, VERSION, COMMIT)
        self.assertEqual({row["path"] for row in manifest["files"]}, {
            "launcher/DFLegacy.Launcher.exe", "launcher/DFLegacy.Client.dll", "launcher/launcher.json", "launcher/Config.ini",
            "patch/DFLegacy.Client.dll", "patch/debug.dll", "patch/Config.ini"})
        config = json.loads((assets / "launcher/launcher.json").read_text())
        self.assertEqual(config["Accounts"], [])
        self.assertEqual(config["Password"], "")
        self.assertFalse(config["StartServer"])
        self.assertEqual(config["ClientPath"], "DNF.exe")
        self.assertIn("PresentationFramework.dll", release.read_bundle(assets / "launcher/DFLegacy.Launcher.exe", "win-x64").entries)

    def test_assets_reject_missing_wpf_dependency(self):
        assets = self.assets()
        remove_embedded(assets / "launcher", "DFLegacy.Launcher", "win-x64", "PresentationFramework.dll")
        refresh_manifest(assets, "manifest.json")
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)

    def test_dependency_omission_rejected_even_when_manifest_is_recomputed(self):
        assets = self.assets()
        remove_embedded(assets / "launcher", "DFLegacy.Launcher", "win-x64", "Fixture.Dependency.dll")
        (assets / "manifest.json").unlink()
        (assets / "manifest.json").write_text(json.dumps(release.manifest(assets, VERSION, COMMIT)))
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)

    def test_debug_overlay_is_disabled_even_with_optional_debug_binary(self):
        assets = self.assets()
        config = assets / "patch/Config.ini"
        self.assertIn("Enabled=0", config.read_text())

    def test_manifest_schema_and_nonempty_credentials_are_rejected(self):
        assets = self.assets()
        profile = assets / "launcher/launcher.json"
        data = json.loads(profile.read_text())
        data["Password"] = "fixture-private-password"
        profile.write_text(json.dumps(data))
        m = release.manifest(assets, VERSION, COMMIT)
        m["files"] = [row for row in m["files"] if row["path"] != "manifest.json"]
        (assets / "manifest.json").write_text(json.dumps(m))
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)

    def test_persisted_password_hash_and_ini_secret_are_not_distributable(self):
        path = write(self.root, "identity.json", '{"PasswordHash":"private-management-hash"}')
        with self.assertRaises(release.ReleaseError):
            release.scan(path)
        path = write(self.root, "Config.ini", "[Credentials]\nApiToken=private-value\n")
        with self.assertRaises(release.ReleaseError):
            release.scan(path)

    def test_dependency_names_containing_usersecrets_are_not_credentials(self):
        assets = self.assets()
        from test_bundle import fixture
        executable = assets / "launcher/DFLegacy.Launcher.exe"
        parsed = release.read_bundle(executable, "win-x64")
        contents = {name: parsed.read(name) for name in parsed.entries}
        data = json.loads(contents["DFLegacy.Launcher.deps.json"])
        data["libraries"] = {"Microsoft.Extensions.Configuration.UserSecrets/10.0.13": {"type": "package", "sha512": ""}}
        contents["DFLegacy.Launcher.deps.json"] = json.dumps(data).encode()
        fixture(executable, contents)
        (assets / "manifest.json").unlink()
        (assets / "manifest.json").write_text(json.dumps(release.manifest(assets, VERSION, COMMIT)))
        release.verify_assets(assets, VERSION, COMMIT)

    def test_symlink_in_archive_is_rejected_without_writing_target(self):
        malicious = self.root / "symlink.tar.gz"
        with tarfile.open(malicious, "w:gz") as archive:
            link = tarfile.TarInfo("DFLegacy/link")
            link.type = tarfile.SYMTYPE
            link.linkname = "../../outside"
            archive.addfile(link)
        with self.assertRaises(release.ReleaseError):
            release.verify(malicious)
        self.assertFalse((self.root / "outside").exists())

    def test_assets_reject_tampering_extra_files_and_mixed_commit(self):
        assets = self.assets()
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, "b" * 40)
        write(assets, "patch/DFLegacy.Client.dll", "tampered")
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)
        write(assets, "secret.txt", "private")
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)

    def test_reject_manifest_traversal_and_case_collision(self):
        assets = self.assets()
        path = assets / "manifest.json"
        data = json.loads(path.read_text())
        for unsafe in ("../outside", "C:/private", "launcher\\secret", "/absolute", "launcher/../secret", "launcher/CON", "launcher/a:stream"):
            with self.subTest(unsafe=unsafe):
                changed = dict(data, files=[{"path": unsafe, "sha256": "a" * 64}])
                path.write_text(json.dumps(changed))
                with self.assertRaises(release.ReleaseError):
                    release.verify_assets(assets, VERSION, COMMIT)
        changed = dict(data, files=data["files"] + [dict(data["files"][0], path=data["files"][0]["path"].upper())])
        path.write_text(json.dumps(changed))
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)

    def test_package_contains_safe_layout_and_archive_modes(self):
        output, archive = self.package()
        manifest = release.verify(output, VERSION, COMMIT)
        self.assertEqual(manifest["rid"], "linux-x64")
        self.assertNotIn("release-manifest.json", [f["path"] for f in manifest["files"]])
        self.assertFalse((output / "data").exists())
        self.assertFalse((output / "resources/Script.pvf").exists())
        template = json.loads((output / "templates/server.json").read_text())
        self.assertFalse(template["DFLegacy"]["SeedTestAccount"])
        with tarfile.open(archive) as tar:
            for name in ("start.sh", "Start.command", "DFLegacy.Manager", "runtime/server/DFLegacy.Server"):
                self.assertEqual(tar.getmember(output.name + "/" + name).mode & 0o111, 0o111)
        release.verify(archive, VERSION, COMMIT)

    def test_repeated_publish_normalizes_generated_web_last_modified_timestamps(self):
        assets = self.assets()
        outputs = [app(self.root / name, assembly, "linux-x64") for name, assembly in
                   (("manager", "DFLegacy.Manager"), ("server", "DFLegacy.Server"), ("channel", "DFLegacy.Channel"))]
        metadata = outputs[0] / "DFLegacy.Manager.staticwebassets.endpoints.json"
        bundles = []
        with patch.dict(os.environ, {"SOURCE_DATE_EPOCH": "1793344496"}):
            for index, clock in enumerate(("Fri, 30 Oct 2026 08:18:35 GMT", "Sat, 31 Oct 2026 10:19:42 GMT")):
                metadata.write_text(json.dumps({"Endpoints": [{"ResponseHeaders": [{"Name": "Last-Modified", "Value": clock}]}]}))
                output, archive = release.publish("linux-x64", VERSION, COMMIT, assets, self.root / str(index) / "DFLegacy", *outputs)
                bundles.append((output, archive))
        self.assertEqual((bundles[0][0] / "release-manifest.json").read_bytes(), (bundles[1][0] / "release-manifest.json").read_bytes())
        self.assertEqual(bundles[0][1].read_bytes(), bundles[1][1].read_bytes())

    def test_windows_package_zip_and_complete_runtime(self):
        output, archive = self.package("win-x64")
        with zipfile.ZipFile(archive) as archive_file:
            self.assertIn(output.name + "/Start.cmd", archive_file.namelist())
            self.assertIn(output.name + "/runtime/server/DFLegacy.Server.exe", archive_file.namelist())
            self.assertNotIn(output.name + "/runtime/server/coreclr.dll", archive_file.namelist())
        release.verify(archive, VERSION, COMMIT)

    def test_missing_runtime_is_rejected(self):
        output, _ = self.package()
        remove_embedded(output, "DFLegacy.Manager", "linux-x64", "System.Private.CoreLib.dll")
        refresh_manifest(output, "release-manifest.json")
        with self.assertRaises(release.ReleaseError):
            release.verify(output, VERSION, COMMIT)

    def test_unix_release_rejects_windows_random_native_even_with_valid_hashes(self):
        output, _ = self.package()
        write(output, "runtime/server/DFLegacy.RandomNative.dll", "Windows PE fixture")
        (output / "release-manifest.json").unlink()
        (output / "release-manifest.json").write_text(json.dumps(release.manifest(output, VERSION, COMMIT, rid="linux-x64", verification="fixture cross build")))
        with self.assertRaises(release.ReleaseError):
            release.verify(output, VERSION, COMMIT)

    def test_publish_never_overwrites_existing_directory(self):
        output, _ = self.package()
        marker = write(output, "my-save.txt", "do not replace")
        with self.assertRaises(release.ReleaseError):
            release.publish("linux-x64", VERSION, COMMIT, self.root / "assets", output)
        self.assertEqual(marker.read_text(), "do not replace")

    def test_archive_traversal_and_symlinks_rejected(self):
        malicious = self.root / "evil.zip"
        with zipfile.ZipFile(malicious, "w") as archive:
            archive.writestr("../outside", "evil")
        with self.assertRaises(release.ReleaseError):
            release.verify(malicious)
        self.assertFalse((self.root / "outside").exists())

    def test_invalid_versions_commits_and_rids_fail_closed(self):
        for version in ("../v1", "latest", "v1.2", "v1.2.3;curl", "dev-main", "dev-99999999T999999Z-aaaaaaaaaaaa"):
            with self.assertRaises(release.ReleaseError):
                release.validate_identity(version, COMMIT)
        for commit in ("main", "a" * 7, "A" * 40, "a" * 39 + "/"):
            with self.assertRaises(release.ReleaseError):
                release.validate_identity(VERSION, commit)
        with self.assertRaises(release.ReleaseError):
            release.publish("linux-unknown", VERSION, COMMIT, self.root, self.root / "out")

    def test_sensitive_files_rejected_before_packaging(self):
        assets = self.assets()
        write(assets, "launcher/secret.pem", "-----BEGIN PRIVATE KEY-----")
        with self.assertRaises(release.ReleaseError):
            release.verify_assets(assets, VERSION, COMMIT)


class FakeGitea(http.server.BaseHTTPRequestHandler):
    releases = []
    attachments = {}
    requests = []
    fail_upload = False
    fail_after = None
    tag_commit = COMMIT
    before_assets_list = None
    before_create = None

    def log_message(self, *args):
        pass

    def handle_request(self):
        cls = type(self)
        size = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(size)
        cls.requests.append((self.command, self.path))
        path = self.path.split("?")[0]
        status, result = 200, {}
        if self.command == "GET" and "/releases/tags/" in path:
            tag = path.rsplit("/", 1)[1]
            found = next((r for r in cls.releases if r["tag_name"] == tag), None)
            status, result = (200, found) if found else (404, {})
        elif self.command == "GET" and "/tags/" in path:
            result = {"name": path.rsplit("/", 1)[1], "commit": {"sha": cls.tag_commit}}
        elif self.command == "GET" and path.endswith("/assets"):
            if cls.before_assets_list:
                callback, cls.before_assets_list = cls.before_assets_list, None
                callback()
            result = [{"id": key, "name": value["name"], "size": len(value["content"]),
                       "browser_download_url": f"http://127.0.0.1:{self.server.server_port}/download/{key}"} for key, value in cls.attachments.items()]
        elif self.command == "GET" and path.startswith("/download/"):
            content = cls.attachments[int(path.rsplit("/", 1)[1])]["content"]
            self.send_response(200)
            self.send_header("Content-Length", str(len(content)))
            self.end_headers()
            self.wfile.write(content)
            return
        elif self.command == "GET" and path.endswith("/releases"):
            result = cls.releases
        elif self.command == "POST" and path.endswith("/releases"):
            if cls.before_create:
                callback, cls.before_create = cls.before_create, None
                callback()
            data = json.loads(body)
            if any(row["tag_name"] == data["tag_name"] for row in cls.releases):
                status, result = 409, {}
            else:
                result = dict(data, id=len(cls.releases) + 1)
                cls.releases.append(result)
        elif self.command == "POST" and path.endswith("/assets"):
            if cls.fail_upload or (cls.fail_after is not None and len(cls.attachments) >= cls.fail_after):
                status = 503
            else:
                result = {"id": max(cls.attachments, default=0) + 1}
                message = email.parser.BytesParser(policy=email.policy.default).parsebytes(
                    ("Content-Type: " + self.headers["Content-Type"] + "\r\nMIME-Version: 1.0\r\n\r\n").encode() + body)
                content = next(message.iter_parts()).get_payload(decode=True)
                cls.attachments[result["id"]] = {"content": content, "name": urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)["name"][0]}
        elif self.command == "PATCH":
            rid = int(path.rsplit("/", 1)[1])
            result = next(r for r in cls.releases if r["id"] == rid)
            result.update(json.loads(body))
        elif self.command == "DELETE":
            if "/assets/" in path:
                cls.attachments.pop(int(path.rsplit("/", 1)[1]), None)
            result = {}
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(result).encode())

    do_GET = do_POST = do_PATCH = do_DELETE = handle_request


class Remote(unittest.TestCase):
    assets = Bundles.assets
    package = Bundles.package

    def setUp(self):
        Bundles.setUp(self)
        FakeGitea.releases, FakeGitea.attachments, FakeGitea.requests = [], {}, []
        FakeGitea.fail_upload = False
        FakeGitea.fail_after = None
        FakeGitea.tag_commit = COMMIT
        FakeGitea.before_assets_list = FakeGitea.before_create = None
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), FakeGitea)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        self.api = release.Gitea(f"http://127.0.0.1:{server.server_port}", "owner/repo", "fixture-token", allow_http=True)

    def archives(self):
        archives = []
        assets = self.assets()
        for rid in release.RIDS:
            outputs = [app(self.root / (rid + name), assembly, rid) for name, assembly in
                       (("manager", "DFLegacy.Manager"), ("server", "DFLegacy.Server"), ("channel", "DFLegacy.Channel"))]
            _, archive = release.publish(rid, VERSION, COMMIT, assets, self.root / ("DFLegacy-" + rid), *outputs)
            archives.append(archive)
        return archives

    def test_draft_is_published_only_after_every_upload(self):
        archives = self.archives()
        release.release(self.api, VERSION, COMMIT, archives)
        self.assertFalse(FakeGitea.releases[0]["draft"])
        self.assertEqual(len(FakeGitea.attachments), 10)
        self.assertTrue({"compose.yaml", "compose.host-ipv6.yaml", "compose.pvf.yaml", ".env.example"}.issubset({value["name"] for value in FakeGitea.attachments.values()}))
        self.assertEqual(FakeGitea.requests[-1][0], "PATCH")

    def test_check_only_validates_complete_set_without_remote_mutation(self):
        archives = self.archives()
        release.release(self.api, VERSION, COMMIT, archives, check_only=True)
        self.assertEqual(FakeGitea.releases, [])
        self.assertEqual(FakeGitea.attachments, {})
        self.assertTrue(all(method == "GET" for method, _ in FakeGitea.requests))

    def test_upload_failure_leaves_draft_and_retry_is_identity_bound(self):
        archives = self.archives()
        FakeGitea.fail_upload = True
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertTrue(FakeGitea.releases[0]["draft"])
        FakeGitea.fail_upload = False
        release.release(self.api, VERSION, COMMIT, archives)
        self.assertFalse(FakeGitea.releases[0]["draft"])
        before = len(FakeGitea.requests)
        result = release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(result["status"], "already-published")
        self.assertTrue(all(method == "GET" for method, _ in FakeGitea.requests[before:]))

    def test_partial_upload_retry_verifies_and_preserves_matching_draft_assets(self):
        archives = self.archives()
        FakeGitea.fail_after = 2
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(len(FakeGitea.attachments), 2)
        originals = copy.deepcopy(FakeGitea.attachments)
        self.assertTrue(FakeGitea.releases[0]["draft"])
        FakeGitea.fail_after = None
        release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(len(FakeGitea.attachments), 10)
        self.assertEqual(sum(method == "DELETE" for method, _ in FakeGitea.requests), 0)
        self.assertTrue(all(FakeGitea.attachments[key] == value for key, value in originals.items()))

    def test_retry_paused_before_assets_listing_cannot_delete_concurrently_published_release(self):
        archives = self.archives()
        FakeGitea.fail_after = 2
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        FakeGitea.fail_after = None
        def competing_run():
            release.release(self.api, VERSION, COMMIT, archives)
            FakeGitea.fail_upload = True
        FakeGitea.before_assets_list = competing_run
        result = release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(result["status"], "already-published")
        self.assertFalse(FakeGitea.releases[0]["draft"])
        self.assertEqual(len(FakeGitea.attachments), 10)
        self.assertFalse(any(method == "DELETE" for method, _ in FakeGitea.requests))

    def test_same_published_identity_check_only_is_read_only_for_latest_retry(self):
        archives = self.archives()
        release.release(self.api, VERSION, COMMIT, archives)
        before = len(FakeGitea.requests)
        result = release.release(self.api, VERSION, COMMIT, archives, check_only=True)
        self.assertEqual(result["status"], "already-published")
        self.assertTrue(all(method == "GET" for method, _ in FakeGitea.requests[before:]))

    def test_wrong_existing_attachment_fails_closed_without_mutation(self):
        archives = self.archives()
        FakeGitea.fail_after = 2
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        FakeGitea.fail_after = None
        FakeGitea.attachments[1]["content"] = b"wrong content"
        before = len(FakeGitea.requests)
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertTrue(FakeGitea.releases[0]["draft"])
        self.assertTrue(all(method == "GET" for method, _ in FakeGitea.requests[before:]))

    def test_reserve_is_atomic_and_conflicting_image_digest_never_reaches_registry_write(self):
        archives = self.archives()
        image_a = "registry.invalid/owner/image@sha256:" + "a" * 64
        image_b = "registry.invalid/owner/image@sha256:" + "b" * 64
        registry_writes = []
        def winner():
            result = release.release(self.api, VERSION, COMMIT, archives, image_b, reserve=True)
            self.assertEqual(result["status"], "reserved")
            registry_writes.append(image_b)
        FakeGitea.before_create = winner
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives, image_a, reserve=True)
            registry_writes.append(image_a)
        self.assertEqual(registry_writes, [image_b])
        self.assertEqual(len(FakeGitea.releases), 1)
        self.assertEqual(FakeGitea.attachments, {})
        self.assertTrue(FakeGitea.releases[0]["draft"])
        release.release(self.api, VERSION, COMMIT, archives, image_b)
        self.assertFalse(FakeGitea.releases[0]["draft"])

    def test_same_identity_concurrent_reservation_uses_one_draft(self):
        archives = self.archives()
        image = "registry.invalid/owner/image@sha256:" + "a" * 64
        FakeGitea.before_create = lambda: release.release(self.api, VERSION, COMMIT, archives, image, reserve=True)
        result = release.release(self.api, VERSION, COMMIT, archives, image, reserve=True)
        self.assertEqual(result["status"], "reserved")
        self.assertEqual(len(FakeGitea.releases), 1)
        self.assertEqual(FakeGitea.attachments, {})

    def test_artifact_changed_after_reservation_is_never_uploaded(self):
        archives = self.archives()
        FakeGitea.before_assets_list = lambda: archives[0].write_bytes(b"changed after identity was reserved")
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertTrue(FakeGitea.releases[0]["draft"])
        self.assertFalse(any(asset["name"] == archives[0].name for asset in FakeGitea.attachments.values()))

    def test_attachment_download_refuses_other_origin_before_sending_credentials(self):
        with self.assertRaises(release.ReleaseError):
            self.api.attachment_digest({"size": 3, "browser_download_url": "https://other.invalid/file"}, 3)
        self.assertEqual(FakeGitea.requests, [])

    def test_partial_platform_set_fails_before_network(self):
        _, archive = self.package()
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, [archive])
        self.assertEqual(FakeGitea.requests, [])

    def test_mismatched_draft_refuses_retry_without_upload(self):
        archives = self.archives()
        FakeGitea.releases.append({"id": 1, "tag_name": VERSION, "draft": True, "body": release.OWNER_MARKER, "target_commitish": "b" * 40})
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(FakeGitea.attachments, {})

    def test_tag_commit_mismatch_refuses_release_before_mutation(self):
        archives = self.archives()
        FakeGitea.tag_commit = "c" * 40
        with self.assertRaises(release.ReleaseError):
            release.release(self.api, VERSION, COMMIT, archives)
        self.assertEqual(FakeGitea.releases, [])
        self.assertTrue(all(method == "GET" for method, _ in FakeGitea.requests))

    def test_remote_configuration_rejects_unsafe_urls_and_repository(self):
        for url in ("http://example.com", "https://user:pass@example.com", "https://example.com?token=bad", "file:///tmp/a"):
            with self.assertRaises(release.ReleaseError):
                release.Gitea(url, "owner/repo", "fixture")
        with self.assertRaises(release.ReleaseError):
            release.Gitea("https://example.com", "../repo", "fixture")

    def test_prune_only_owned_dev_default_dry_run_keep_20(self):
        for i in range(25):
            FakeGitea.releases.append({"id": i + 1, "tag_name": f"dev-20261030T1200{i:02}Z-aaaaaaaaaaaa", "prerelease": True,
                                       "body": release.OWNER_MARKER, "draft": False, "created_at": f"2026-10-30T12:00:{i:02}Z"})
        FakeGitea.releases += [{"id": 99, "tag_name": "v1.0.0", "body": release.OWNER_MARKER},
                               {"id": 100, "tag_name": "dev-someone-else", "prerelease": True, "body": "human"}]
        selected = release.prune(self.api)
        self.assertEqual(len(selected), 5)
        self.assertFalse(any(method == "DELETE" for method, _ in FakeGitea.requests))
        release.prune(self.api, apply=True)
        deleted = [path for method, path in FakeGitea.requests if method == "DELETE"]
        self.assertEqual(len(deleted), 10)
        self.assertFalse(any("99" in path or "100" in path or "v1.0.0" in path for path in deleted))


if __name__ == "__main__":
    unittest.main()
