#!/usr/bin/env python3
"""Small, locally reusable CI driver; it does not push images or publish releases."""
from datetime import datetime, timezone
import argparse
import json
import hashlib
import uuid
import urllib.parse
import os
import re
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release import REPO, ReleaseError, build_client_assets, build_client_packages, dotnet_environment, make_archive, new_output, publish, read_json, unpack, validate_identity, verify, verify_assets, write_json


def build_version(ref, commit, timestamp):
    if ref.startswith("refs/tags/"):
        version = ref.removeprefix("refs/tags/")
    else:
        stamp = datetime.fromisoformat(timestamp).astimezone(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        version = "dev-" + stamp + "-" + commit[:12]
    try:
        validate_identity(version, commit)
    except ReleaseError as error:
        raise ValueError(str(error)) from error
    return version


def run(*command):
    subprocess.run([str(part) for part in command], cwd=REPO, check=True, env=dotnet_environment())


def normalize_oci_metadata(path, version, commit, epoch):
    validate_identity(version, commit)
    if not isinstance(epoch, str) or not epoch.isascii() or not epoch.isdigit() or not 0 <= int(epoch) <= 253402300799:
        raise ReleaseError("invalid SOURCE_DATE_EPOCH")
    digest = read_json(path).get("containerimage.digest")
    if not isinstance(digest, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
        raise ReleaseError("Buildx metadata lacks a valid image index digest")
    # buildx.build.ref and provenance contain invocation-specific identifiers.
    # The published metadata records only the immutable release identity.
    write_json(path, {"containerimage.digest": digest, "version": version, "commit": commit,
                      "source_date_epoch": int(epoch), "platforms": ["linux/amd64", "linux/arm64"],
                      "provenance": False, "sbom": False})


def smoke_command(command, cwd):
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix="DFLegacy smoke data ") as data, tempfile.TemporaryFile() as output:
        process = subprocess.Popen(command(port, data), cwd=cwd, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 30
            while True:
                if process.poll() is not None:
                    raise ReleaseError("packaged Manager exited before health became ready")
                try:
                    with urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=1) as response:
                        if response.status == 200:
                            break
                except (OSError, urllib.error.HTTPError):
                    pass
                if time.monotonic() > deadline:
                    raise ReleaseError("packaged Manager health timed out")
                time.sleep(0.1)
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{port}/ready", timeout=2).close()
            except urllib.error.HTTPError as error:
                if error.code != 503:
                    raise ReleaseError("stopped game readiness must be 503") from error
            else:
                raise ReleaseError("unconfigured game incorrectly reports ready")
        finally:
            process.terminate()
            try:
                process.wait(timeout=35)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
                raise ReleaseError("packaged Manager failed to stop in 35 seconds")


def smoke_archive(archive):
    data = verify(archive)
    with tempfile.TemporaryDirectory(prefix="DFLegacy release with spaces ") as temporary:
        root = unpack(archive, Path(temporary))
        executable = root / ("DFLegacy.Manager.exe" if data["rid"].startswith("win") else "start.sh")
        smoke_command(lambda port, data: [str(executable), "--root", str(root), "--data-dir", data, "--listen", "127.0.0.1", "--port", str(port), "--no-browser"], Path(temporary).parent)


def metadata():
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    if commit != os.environ["BUILD_COMMIT"]:
        raise ReleaseError("checkout SHA differs from triggering commit")
    timestamp = subprocess.check_output(["git", "show", "-s", "--format=%cI", commit], cwd=REPO, text=True).strip()
    epoch = subprocess.check_output(["git", "show", "-s", "--format=%ct", commit], cwd=REPO, text=True).strip()
    if not epoch.isascii() or not epoch.isdigit():
        raise ReleaseError("Git commit has no valid source timestamp")
    version = build_version(os.environ["BUILD_REF"], commit, timestamp)
    for filename, rows in ((os.environ.get("GITHUB_OUTPUT"), ("version=" + version, "commit=" + commit, "source_date_epoch=" + epoch)),
                           (os.environ.get("GITHUB_ENV"), ("DFLEGACY_RELEASE_VERSION=" + version, "DFLEGACY_RELEASE_COMMIT=" + commit, "SOURCE_DATE_EPOCH=" + epoch))):
        if filename:
            with open(filename, "a", encoding="utf-8") as stream:
                stream.write("\n".join(rows) + "\n")
    print(json.dumps({"version": version, "commit": commit, "source_date_epoch": int(epoch)}))


LEGACY_FIXTURE_NAME = "DFLegacy-client-update-win-x64.zip"
LEGACY_FIXTURE_SHA256 = "4851bfc41b1c39799ba7ed735954c1d6fd1cbc91fba0d8b2a201eb2c7977e0f5"
LEGACY_FIXTURE_SIZE = 63332728
LEGACY_FIXTURE_URL = ("https://github.com/firekula/DFLegacy/releases/download/"
                      "dev-20261031T114502Z-4a0e12555c17/" + LEGACY_FIXTURE_NAME)


def open_legacy_fixture():
    class Redirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, request, response, code, message, headers, url):
            parsed = urllib.parse.urlsplit(url)
            if parsed.scheme != "https" or parsed.hostname not in ("github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com") or parsed.username or parsed.password or parsed.port not in (None, 443):
                raise ReleaseError("untrusted legacy fixture redirect")
            return super().redirect_request(request, response, code, message, headers, url)
    return urllib.request.build_opener(Redirect()).open(LEGACY_FIXTURE_URL, timeout=120)


def legacy_patch_fixture(cache):
    """Fetch only the pinned project-owned prior patch; never game/original IJL files."""
    cache = Path(cache)
    cache.mkdir(parents=True, exist_ok=True)
    if cache.is_symlink() or cache.is_junction():
        raise ReleaseError("legacy fixture cache cannot be a link")
    destination = cache / LEGACY_FIXTURE_NAME
    def verified(path):
        if path.is_symlink() or path.is_junction():
            raise ReleaseError("legacy fixture cannot be a link")
        if not path.is_file() or path.stat().st_size != LEGACY_FIXTURE_SIZE:
            return False
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, 'sha256').hexdigest() == LEGACY_FIXTURE_SHA256
    if verified(destination):
        return destination
    temporary = cache / (uuid.uuid4().hex + '.part')
    try:
        with open_legacy_fixture() as source, temporary.open('xb') as target:
            size = 0
            while block := source.read(min(1024 * 1024, LEGACY_FIXTURE_SIZE - size + 1)):
                size += len(block)
                if size > LEGACY_FIXTURE_SIZE:
                    raise ReleaseError("legacy fixture exceeds pinned size")
                target.write(block)
        if not verified(temporary):
            raise ReleaseError("legacy fixture size or SHA256 differs")
        temporary.replace(destination)
        return destination
    finally:
        temporary.unlink(missing_ok=True)


def windows(version, commit, client_only=False):
    if os.name != "nt":
        raise ReleaseError("Windows asset build requires Windows")
    validate_identity(version, commit)
    # Clean checkout in CI; source scripts fail on any native build/test failure.
    run("pwsh", "-NoProfile", "-File", "src/DFLegacy.Ijl15/build-native.ps1", "-Configuration", "Release", "-RunTests")
    run("pwsh", "-NoProfile", "-File", "src/DFLegacy.RandomNative/build-native.ps1", "-Configuration", "Release", "-RunTests")
    native_build = REPO / "artifacts/native/ijl15"
    native = native_build / "Release/DFLegacy.Client.dll"
    bootstrap = native_build / "Release/DFLegacy.Bootstrap32.exe"
    debug = native_build / "Release/debug.dll"
    fixture = legacy_patch_fixture(REPO / "artifacts/test-fixture-cache")
    os.environ["DFL_NATIVE_FIXTURES"] = str(native_build)
    os.environ["DFL_LEGACY_PATCH_ARCHIVE"] = str(fixture)
    native_properties = ("-p:BuildNativeAssets=false", "-p:ClientPatchPrebuiltPath=" + str(native),
                         "-p:ClientBootstrapPrebuiltPath=" + str(bootstrap),
                         "-p:Ijl15PrebuiltPath=" + str(native_build / "Release/ijl15.dll"))
    run("dotnet", "run", "--project", "src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj", "-c", "Release",
        *native_properties)
    launcher = new_output(REPO / "artifacts" / ("launcher-" + version))
    launcher_x86 = new_output(REPO / "artifacts" / ("launcher-x86-" + version))
    for rid, destination in (("win-x64", launcher), ("win-x86", launcher_x86)):
        run("dotnet", "publish", "src/DFLegacy.Launcher/DFLegacy.Launcher.csproj", "-c", "Release", "-r", rid, "--self-contained", "true",
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=embedded", "-p:PublishTrimmed=false",
            *native_properties, "-p:ContinuousIntegrationBuild=true", "-o", destination)
    # Separate self-contained helper preserves single-file repair and needs no installed runtime.
    for rid, destination in (("win-x64", launcher), ("win-x86", launcher_x86)):
        helper = new_output(REPO / "artifacts" / ("diagnostics-" + rid + "-" + version))
        run("dotnet", "publish", "src/DFLegacy.Diagnostics/DFLegacy.Diagnostics.csproj", "-c", "Release", "-r", rid,
            "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=embedded", "-p:PublishTrimmed=false", "-p:ContinuousIntegrationBuild=true", "-o", helper)
        shutil.copy2(helper / "DFLegacy.Diagnostics.exe", destination / "DFLegacy.Diagnostics.exe")
    assets = build_client_assets(launcher, native, REPO / "src/DFLegacy.Ijl15/Config.ini", REPO / "client-assets", version, commit,
                                 debug=debug, launcher_x86=launcher_x86, capture_helper=native_build / "crash_capture/Release/DFLegacy.CrashCapture.exe")
    # Tar transport keeps the immutable assets together. All receivers verify it.
    make_archive(assets, REPO / "artifacts/client-assets.tar.gz", "linux-x64")
    if not client_only:
        _, archive = publish("win-x64", version, commit, assets, REPO / "artifacts" / ("DFLegacy-" + version + "-win-x64"),
                         verification="Windows x64 CI: managed/native tests and packaged Manager health/readiness; GPU and graphical game acceptance are not executed in headless CI")
        smoke_archive(archive)
    build_client_packages(assets, REPO / "artifacts", version, commit)
    run(sys.executable, "tools/release/tests/verify_client_notices.py", "--native", native.parent,
        "--launcher", launcher / "DFLegacy.Launcher.exe", "--launcher-x86", launcher_x86 / "DFLegacy.Launcher.exe",
        "--packages", REPO / "artifacts")
    run(sys.executable, "tools/release/tests/verify_client_notices.py", "--native", native.parent,
        "--launcher", launcher_x86 / "DFLegacy.Launcher.exe", "--launcher-rid", "win-x86")


def linux(version, commit, archive):
    if sys.platform != "linux":
        raise ReleaseError("Linux packaging gate requires Linux")
    validate_identity(version, commit)
    if (REPO / "client-assets").exists():
        raise ReleaseError("client-assets already exists; use a clean checkout")
    with tempfile.TemporaryDirectory(prefix="dflegacy-transfer-") as temporary:
        assets = unpack(Path(archive), Path(temporary))
        verify_assets(assets, version, commit)
        shutil.copytree(assets, REPO / "client-assets")
    for rid in ("linux-x64", "linux-arm64", "osx-x64", "osx-arm64"):
        status = "Linux x64 CI managed tests and packaged Manager health/readiness; no graphical game acceptance" if rid == "linux-x64" else "cross-built and archive verified; native execution not performed"
        _, package = publish(rid, version, commit, REPO / "client-assets", REPO / "artifacts" / ("DFLegacy-" + version + "-" + rid), verification=status)
        if rid == "linux-x64":
            smoke_archive(package)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("metadata")
    for command in ("windows", "linux"):
        child = sub.add_parser(command)
        child.add_argument("--version", required=True)
        child.add_argument("--commit", required=True)
        if command == "windows":
            child.add_argument("--client-only", action="store_true")
        if command == "linux":
            child.add_argument("--assets-archive", required=True)
    child = sub.add_parser("smoke")
    child.add_argument("archive")
    child = sub.add_parser("image-metadata")
    for name in ("path", "version", "commit", "epoch"):
        child.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        if args.command == "metadata":
            metadata()
        elif args.command == "windows":
            windows(args.version, args.commit, args.client_only)
        elif args.command == "linux":
            linux(args.version, args.commit, args.assets_archive)
        elif args.command == "image-metadata":
            normalize_oci_metadata(args.path, args.version, args.commit, args.epoch)
        else:
            smoke_archive(args.archive)
    except (ReleaseError, ValueError, OSError, subprocess.CalledProcessError) as error:
        print("CI gate failed: " + str(error), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
