#!/usr/bin/env python3
"""DFLegacy release pipeline. Python 3.12+, standard library only.

Every build targets a new directory. Verification is an integrity check, not a
signature or a claim that a cross-compiled platform was run on real hardware.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from email.utils import format_datetime
import configparser
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parent))
from bundle import _architecture, read_bundle

REPO = Path(__file__).resolve().parents[2]
RIDS = ("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
OWNER_MARKER = "<!-- dflegacy-release-pipeline:v1 -->"
DEV = re.compile(r"dev-\d{8}T\d{6}Z-[a-f0-9]{12}\Z")
OFFICIAL = re.compile(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\Z")
MAX_ARCHIVE = 8 * 1024**3
UPDATE_ASSET = "DFLegacy-client-update-v2-win-x64.zip"
UPDATE_TEST_ASSET = "DFLegacy-client-update-v2-test-win-x64.zip"
TOOLS_ASSET = "DFLegacy-client-tools-win-x64.zip"
TOOLS_TEST_ASSET = "DFLegacy-client-tools-test-win-x64.zip"
X86_UPDATE_ASSETS = tuple(name.replace("win-x64", "win-x86") for name in (UPDATE_ASSET, UPDATE_TEST_ASSET))
V3_UPDATE_ASSETS = tuple(f"DFLegacy-client-update-v3{flavor}-win-{architecture}.zip"
                         for architecture in ("x64", "x86") for flavor in ("", "-test"))
PUBLIC_ASSETS = (UPDATE_ASSET, UPDATE_TEST_ASSET, TOOLS_ASSET, TOOLS_TEST_ASSET)
UPDATE_FILES = ("DFLegacy.Launcher.exe", "DFLegacy.Client.dll")
SCRIPT_PVF = "Script.pvf"
MAX_UPDATE_ZIP = 256 * 1024**2
MAX_UPDATE_EXPANDED = 512 * 1024**2


class ReleaseError(Exception):
    pass


def validate_identity(version, commit):
    if not isinstance(version, str) or not (OFFICIAL.fullmatch(version) or DEV.fullmatch(version)):
        raise ReleaseError("version must be vX.Y.Z or dev-YYYYMMDDTHHMMSSZ-<12 lowercase commit digits>")
    if not isinstance(commit, str) or not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise ReleaseError("commit must be a full lowercase 40-digit SHA")
    if DEV.fullmatch(version) and not version.endswith(commit[:12]):
        raise ReleaseError("development version does not match commit")
    if DEV.fullmatch(version):
        try:
            datetime.strptime(version[4:20], "%Y%m%dT%H%M%SZ")
        except ValueError as error:
            raise ReleaseError("invalid development version timestamp") from error


def safe_path(value):
    if not isinstance(value, str) or not value or len(value) > 400 or "\\" in value or ":" in value or "\x00" in value:
        raise ReleaseError("unsafe manifest/archive path")
    path = PurePosixPath(value)
    if path.is_absolute() or value != path.as_posix() or any(p in (".", "..", "") for p in value.split("/")):
        raise ReleaseError("unsafe manifest/archive path")
    for part in path.parts:
        if part.endswith((".", " ")) or re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", part):
            raise ReleaseError("nonportable manifest/archive path")
        if any(ord(c) < 32 or c in '<>"|?*' for c in part):
            raise ReleaseError("nonportable manifest/archive path")
    return path


def files(root):
    root = Path(root)
    if not root.is_dir() or root.is_symlink() or root.is_junction():
        raise ReleaseError(f"not a regular directory: {root}")
    found = []
    folded = set()
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or path.is_junction() or not (path.is_file() or path.is_dir()):
            raise ReleaseError(f"links and special files are forbidden: {path.name}")
        relative = path.relative_to(root).as_posix()
        safe_path(relative)
        if relative.casefold() in folded:
            raise ReleaseError("case-colliding filenames")
        folded.add(relative.casefold())
        if path.is_file():
            found.append(path)
    return found


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, data):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_keys)
    except (OSError, ValueError) as error:
        raise ReleaseError(f"invalid JSON: {Path(path).name}") from error


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key")
        result[key] = value
    return result


def sensitive_name(key):
    return key.casefold() != "publickeytoken" and bool(re.fullmatch(
        r"(?i)[A-Za-z_-]*(?:password(?:hash|salt)?|secrets?|credentials?|connectionstring|token(?:hash|salt)?)", key))


def scan(path):
    relative = path.name.casefold()
    if relative in ("script.pvf", "state.json", ".env", "id_rsa", "id_ed25519") or path.suffix.casefold() in (".pvf", ".pfx", ".pem", ".key"):
        raise ReleaseError(f"private/game resource forbidden: {path.name}")
    if path.suffix.lower() not in (".json", ".ini", ".config", ".txt", ".md", ".yaml", ".yml"):
        return
    try:
        text = path.read_text(encoding="utf-8-sig")
    except UnicodeError as error:
        raise ReleaseError("configuration must be UTF-8") from error
    if re.search(r"[A-Za-z]:\\|/(?:home|Users|root)/|-----BEGIN .*PRIVATE KEY-----", text):
        raise ReleaseError(f"machine path/private key forbidden: {path.name}")
    # Never print values. Reject a matching secret from the CI environment.
    for name, value in os.environ.items():
        if re.search(r"(?:TOKEN|PASSWORD|SECRET)(?:$|_)", name.upper()) and len(value) >= 8 and value in text:
            raise ReleaseError(f"environment secret found in {path.name}")
    if path.suffix.lower() == ".json":
        def walk(value):
            if isinstance(value, dict):
                for key, item in value.items():
                    if sensitive_name(key) and item not in ("", None, False, [], {}):
                        raise ReleaseError(f"nonempty credential field in {path.name}")
                    walk(item)
            elif isinstance(value, list):
                for item in value:
                    walk(item)
        walk(read_json(path))
    elif path.suffix.lower() == ".ini":
        ini = configparser.ConfigParser(interpolation=None)
        try:
            ini.read_string(text)
        except configparser.Error as error:
            raise ReleaseError("invalid INI configuration") from error
        for section in ini.sections():
            for key, value in ini[section].items():
                if sensitive_name(key) and value.strip():
                    raise ReleaseError(f"nonempty credential field in {path.name}")


def runtime_files(rid):
    if rid not in RIDS:
        raise ReleaseError("unsupported RID")
    native = ("coreclr.dll", "hostfxr.dll") if rid.startswith("win") else (
        ("libcoreclr.so", "libhostfxr.so") if rid.startswith("linux") else ("libcoreclr.dylib", "libhostfxr.dylib"))
    return (*native, "System.Private.CoreLib.dll")


def check_app(root, assembly, rid, web=False, wpf=False):
    executable = assembly + (".exe" if rid.startswith("win") else "")
    if (root / (assembly + ".dll")).exists():
        raise ReleaseError(f"{assembly} requires single-file output; loose application DLL found")
    try:
        bundle = read_bundle(root / executable, rid)
        for name in (assembly + ".dll", "System.Private.CoreLib.dll", *(("PresentationFramework.dll", "PresentationCore.dll", "WindowsBase.dll") if wpf else ())):
            if name not in bundle.entries or bundle.entries[name][1] <= 0:
                raise ValueError("missing embedded dependency: " + name)
        configuration = json.loads(bundle.read(assembly + ".runtimeconfig.json"), object_pairs_hook=unique_keys)["runtimeOptions"]
        frameworks = {item["name"] for item in configuration.get("includedFrameworks", [])}
        required_frameworks = {"Microsoft.NETCore.App"} | ({"Microsoft.AspNetCore.App"} if web else set()) | ({"Microsoft.WindowsDesktop.App"} if wpf else set())
        if not required_frameworks <= frameworks or configuration.get("tfm") != "net10.0" or configuration.get("framework") or configuration.get("frameworks"):
            raise ValueError("bundle is not a self-contained .NET 10 application")
        dependencies = json.loads(bundle.read(assembly + ".deps.json"), object_pairs_hook=unique_keys)
        target = dependencies["runtimeTarget"]["name"]
        if not target.endswith("/" + rid):
            raise ValueError("embedded runtime target differs from RID")
        for library in dependencies["targets"][target].values():
            for category in ("runtime", "native", "resources"):
                for dependency, metadata in library.get(category, {}).items():
                    safe_path(dependency); name = PurePosixPath(dependency).name
                    if name == "_._":
                        continue
                    relative = (PurePosixPath(metadata["locale"]) / name).as_posix() if category == "resources" and metadata.get("locale") else name
                    safe_path(relative)
                    if relative not in bundle.entries:
                        raise ValueError("missing embedded dependency: " + relative)
        if web and not any(p.is_file() for p in (root / "wwwroot").rglob("*")):
            raise ValueError("missing external web assets")
        return
    except (ValueError, KeyError, TypeError, OSError) as error:
        raise ReleaseError(f"incomplete single-file {assembly}: {error}") from error


def manifest(root, version, commit, **extra):
    return {"version": version, "commit": commit, **extra, "files": [
        {"path": path.relative_to(root).as_posix(), "sha256": digest(path)} for path in files(root)]}


def check_manifest(root, name, version=None, commit=None):
    data = read_json(root / name)
    if not isinstance(data, dict):
        raise ReleaseError("manifest must be an object")
    validate_identity(data.get("version"), data.get("commit"))
    if (version is not None and data["version"] != version) or (commit is not None and data["commit"] != commit):
        raise ReleaseError("mixed release version or commit")
    allowed = {"version", "commit", "files"} | ({"rid", "verification"} if name == "release-manifest.json" else set())
    if set(data) != allowed or not isinstance(data.get("files"), list):
        raise ReleaseError("invalid manifest schema")
    listed = set()
    for row in data["files"]:
        if not isinstance(row, dict) or set(row) != {"path", "sha256"}:
            raise ReleaseError("invalid manifest file entry")
        path = safe_path(row["path"])
        folded = str(path).casefold()
        if folded in listed or str(path) == name or not isinstance(row["sha256"], str) or not re.fullmatch(r"[a-f0-9]{64}", row["sha256"]):
            raise ReleaseError("invalid/duplicate manifest file")
        listed.add(folded)
        target = root.joinpath(*path.parts)
        if not target.is_file() or digest(target) != row["sha256"]:
            raise ReleaseError(f"missing or modified release file: {path}")
    actual = files(root)
    if {p.relative_to(root).as_posix().casefold() for p in actual if p.name != name or p.parent != root} != listed:
        raise ReleaseError("unlisted files in bundle")
    for path in actual:
        scan(path)
    return data


def verify_assets(root, version=None, commit=None):
    root = Path(root)
    data = check_manifest(root, "manifest.json", version, commit)
    paths = [row["path"] for row in data["files"]]
    allowed = {"launcher/DFLegacy.Launcher.exe", "launcher-x86/DFLegacy.Launcher.exe", "launcher/DFLegacy.Client.dll", "launcher/Config.ini", "launcher/launcher.json",
               "launcher/Assets/Skin/fwtk.html", "patch/DFLegacy.Client.dll", "patch/debug.dll", "patch/Config.ini"}
    if any(p not in allowed for p in paths):
        raise ReleaseError("client asset outside whitelist")
    for required in ("patch/DFLegacy.Client.dll", "patch/Config.ini", "launcher/launcher.json"):
        if required not in paths:
            raise ReleaseError("missing client asset: " + required)
    check_app(root / "launcher", "DFLegacy.Launcher", "win-x64", wpf=True)
    if "launcher-x86/DFLegacy.Launcher.exe" in paths:
        check_app(root / "launcher-x86", "DFLegacy.Launcher", "win-x86", wpf=True)
    launcher = read_json(root / "launcher/launcher.json")
    client_paths = {"DNF.exe"}
    if launcher.get("Accounts") != [] or launcher.get("Account") != "" or launcher.get("Password") != "" or launcher.get("StartServer") is not False or launcher.get("ClientPath") not in client_paths:
        raise ReleaseError("unsafe launcher profile")
    if (root / "launcher/DFLegacy.Client.dll").exists() and digest(root / "launcher/DFLegacy.Client.dll") != digest(root / "patch/DFLegacy.Client.dll"):
        raise ReleaseError("mixed native patch binaries")
    return data


def new_output(output):
    output = Path(output).absolute()
    if output.exists() or output.is_symlink():
        raise ReleaseError("output already exists; use a new version directory")
    output.parent.mkdir(parents=True, exist_ok=True)
    return output


def copy_tree(source, output):
    for path in files(source):
        destination = output / path.relative_to(source)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)


def build_client_assets(launcher, client_patch, config, output, version, commit, debug=None, launcher_x86=None):
    validate_identity(version, commit)
    output = new_output(output)
    launcher, client_patch, config = Path(launcher), Path(client_patch), Path(config)
    check_app(launcher, "DFLegacy.Launcher", "win-x64", wpf=True)
    if launcher_x86 is not None:
        launcher_x86 = Path(launcher_x86)
        if launcher_x86 / "DFLegacy.Launcher.exe" not in files(launcher_x86):
            raise ReleaseError("missing regular x86 launcher executable")
        check_app(launcher_x86, "DFLegacy.Launcher", "win-x86", wpf=True)
    for native in (client_patch, config, *((Path(debug),) if debug else ())):
        if not native.is_file() or native.is_symlink():
            raise ReleaseError("missing regular native asset")
    with tempfile.TemporaryDirectory(prefix="dflegacy-assets-", dir=output.parent) as temp:
        stage = Path(temp) / "bundle"
        stage.mkdir()
        copy_tree(launcher, stage / "launcher")
        if launcher_x86 is not None:
            (stage / "launcher-x86").mkdir()
            shutil.copy2(launcher_x86 / "DFLegacy.Launcher.exe", stage / "launcher-x86/DFLegacy.Launcher.exe")
        (stage / "patch").mkdir()
        shutil.copy2(client_patch, stage / "patch/DFLegacy.Client.dll")
        shutil.copy2(client_patch, stage / "launcher/DFLegacy.Client.dll")
        ini = configparser.ConfigParser(interpolation=None, strict=True)
        ini.optionxform = str
        try:
            ini.read_string(config.read_text(encoding="utf-8-sig"))
        except (ValueError, configparser.Error) as error:
            raise ReleaseError("invalid patch Config.ini") from error
        for section in ini.sections():
            for key in list(ini[section]):
                if "debug" in section.casefold() or "debug" in key.casefold():
                    if key.casefold() in ("enabled", "debugoverlay", "debugenabled", "enabledebug") or "debug" in key.casefold():
                        ini[section][key] = "0"
        if not ini.has_section("Network"):
            ini.add_section("Network")
        ini["Network"]["ClientServiceUrl"] = "http://127.0.0.1:8081"
        text = io.StringIO()
        ini.write(text, space_around_delimiters=False)
        for name in ("patch/Config.ini", "launcher/Config.ini"):
            (stage / name).write_text(text.getvalue(), encoding="utf-8")
        # A source publish can contain developer credentials. They are replaced,
        # never copied into the immutable release asset manifest.
        write_json(stage / "launcher/launcher.json", {"ClientPath": "DNF.exe", "EntranceHost": "127.0.0.1", "EntrancePort": 2311,
                   "AccountServiceUrl": "http://127.0.0.1:8081", "Account": "", "Password": "", "Accounts": [], "AccountIndex": 0,
                   "StartServer": False, "Relay": {"Enabled": False}})
        (stage / "launcher/debug.dll").unlink(missing_ok=True)
        if debug:
            shutil.copy2(debug, stage / "patch/debug.dll")
        write_json(stage / "manifest.json", manifest(stage, version, commit))
        verify_assets(stage, version, commit)
        stage.rename(output)
    return output


def read_client_update(archive):
    """Read one bounded immutable snapshot; later path changes cannot alter it."""
    archive = Path(archive)
    if archive.is_symlink() or archive.is_junction():
        raise ReleaseError("invalid update archive size or file")
    with archive.open("rb") as source:
        content = source.read(MAX_UPDATE_ZIP + 1)
    if not 0 < len(content) <= MAX_UPDATE_ZIP:
        raise ReleaseError("invalid update archive size or file")
    return content


def verify_client_package(archive, version=None, commit=None, asset_name=None):
    """Validate a bounded immutable public update/tools ZIP and its exact flavor."""
    if asset_name is None and not isinstance(archive, bytes):
        name = Path(archive).name
        if name in (UPDATE_ASSET, UPDATE_TEST_ASSET, *X86_UPDATE_ASSETS, *V3_UPDATE_ASSETS):
            asset_name = name
    content = archive if isinstance(archive, bytes) else read_client_update(archive)
    if not 0 < len(content) <= MAX_UPDATE_ZIP:
        raise ReleaseError("invalid client archive size")
    if asset_name is not None and asset_name not in (*PUBLIC_ASSETS, *X86_UPDATE_ASSETS, *V3_UPDATE_ASSETS):
        raise ReleaseError("unknown public asset name")
    tools = asset_name in (TOOLS_ASSET, TOOLS_TEST_ASSET)
    manifest_name = "client-package.json" if tools else "update-manifest.json"
    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            if key.casefold() in {k.casefold() for k in result}:
                raise ReleaseError("duplicate package JSON property")
            result[key] = value
        return result
    try:
        with zipfile.ZipFile(io.BytesIO(content)) as z:
            entries = z.infolist()
            if sum(e.file_size for e in entries) > MAX_UPDATE_EXPANDED:
                raise ReleaseError("expanded client package exceeds limit")
            for entry in entries:
                mode = entry.external_attr >> 16
                if entry.file_size <= 0 or stat.S_IFMT(mode) not in (0, stat.S_IFREG) or entry.external_attr & 0x410:
                    raise ReleaseError("package contains link, directory or empty entry")
            if z.getinfo(manifest_name).file_size > 16384:
                raise ReleaseError("package manifest exceeds limit")
            data = json.loads(z.read(manifest_name).decode("utf-8"), object_pairs_hook=unique_object)
            if not isinstance(data, dict) or set(data) != {"schemaVersion", "version", "commit", "flavor", "files"} or type(data["schemaVersion"]) is not int or data["schemaVersion"] != 2 or data["flavor"] not in ("normal", "test"):
                raise ReleaseError("invalid client manifest schema")
            validate_identity(data["version"], data["commit"])
            if (version is not None and version != data["version"]) or (commit is not None and commit != data["commit"]):
                raise ReleaseError("mixed client identity")
            test = data["flavor"] == "test"
            if asset_name is not None and test != ("-test-" in asset_name):
                raise ReleaseError("asset name differs from manifest flavor")
            binaries = {*UPDATE_FILES, *(("debug.dll",) if test else ())}
            if asset_name in V3_UPDATE_ASSETS or (asset_name is None and SCRIPT_PVF in z.namelist()):
                binaries.add(SCRIPT_PVF)
            expected = binaries | {manifest_name} | ({"SHA256SUMS", "README.txt"} if tools else set())
            if len(entries) != len(expected) or {e.filename for e in entries} != expected:
                raise ReleaseError("client archive inventory differs")
            rows = data["files"]
            if not isinstance(rows, list) or len(rows) != len(binaries):
                raise ReleaseError("invalid client file list")
            names = set()
            for row in rows:
                if not isinstance(row, dict) or set(row) != {"path", "sha256", "size"} or row["path"] not in binaries or row["path"] in names:
                    raise ReleaseError("invalid client file entry")
                names.add(row["path"])
                if type(row["size"]) is not int or row["size"] <= 0 or row["size"] != z.getinfo(row["path"]).file_size or not isinstance(row["sha256"], str) or not re.fullmatch(r"[a-f0-9]{64}", row["sha256"]):
                    raise ReleaseError("invalid client size or digest")
                with z.open(row["path"]) as stream:
                    if hashlib.file_digest(stream, "sha256").hexdigest() != row["sha256"]:
                        raise ReleaseError("client file digest differs")
            if asset_name in (*X86_UPDATE_ASSETS, *V3_UPDATE_ASSETS):
                architecture = "x86" if asset_name.endswith("win-x86.zip") else "x64"
                launcher = z.read("DFLegacy.Launcher.exe")
                if len(launcher) < 64 or not _architecture(launcher, "win-" + architecture):
                    raise ReleaseError(f"{architecture} package launcher architecture differs")
            if tools:
                if z.read("README.txt") != migration_instructions(data["version"], data["commit"], data["flavor"]):
                    raise ReleaseError("unexpected migration instructions")
                expected_sums = "".join(hashlib.sha256(z.read(name)).hexdigest() + "  " + name + "\n" for name in sorted(expected - {"SHA256SUMS"}))
                if z.read("SHA256SUMS") != expected_sums.encode():
                    raise ReleaseError("tools checksums differ")
            return data
    except (zipfile.BadZipFile, ValueError, KeyError, TypeError, RuntimeError) as error:
        raise ReleaseError("invalid client package archive") from error


def verify_client_update(archive, version=None, commit=None):
    return verify_client_package(archive, version, commit)


def migration_instructions(version, commit, flavor):
    return (f"DFLegacy {version}\nSource commit: {commit}\nFlavor: {flavor}\n\n"
            "The patch is the independent DFLegacy.Client.dll. Exit the game and back up your client before installation.\n"
            "Extract these tools into the DNF.exe directory. Old patched clients require one visible, hash-verified original IJL restoration in the launcher.\n"
            "Never infer an original DLL from a backup filename. Keep original EXE/DLL/PVF/NPK files intact.\n"
            "Only verified compatible unpacked clients are supported; the original packed shell is not repaired.\n"
            "No game binaries or player configuration are included. Preserve Config.ini and launcher.json.\n"
            "Normal includes all gameplay optimizations without diagnostics; test defaults to local diagnostics on the first in-world frame. Automation controls require explicit opt-in.\n"
            "Only server overlay data requires a shared local directory; remote data is not synchronized.\n"
            "Full Detours license and version are embedded in patch/helper/launcher. Read: DFLegacy.Launcher.exe --third-party-notices | more\n"
            "After migration the launcher updates the installed normal/test flavor through schema 2 packages.\n").encode("utf-8")


def build_client_update(client_assets, output, version, commit, test_build=False, tools=False, architecture="x64", *, pvf_snapshot=None):
    """Package verified same-release binaries without any player configuration."""
    root = Path(client_assets)
    verified = verify_assets(root, version, commit)
    names = (*UPDATE_FILES, *(("debug.dll",) if test_build else ()))
    launcher_dir = "launcher-x86" if architecture == "x86" else "launcher"
    sources = {name: root / (launcher_dir if name == "DFLegacy.Launcher.exe" else "patch/") / name for name in names}
    anchored = {row["path"]: row["sha256"] for row in verified["files"]}
    if any(path.relative_to(root).as_posix() not in anchored for path in sources.values()):
        raise ReleaseError("missing same-release client binary")
    data = {"schemaVersion": 2, "version": version, "commit": commit, "flavor": "test" if test_build else "normal",
            "files": [{"path": name, "sha256": anchored[path.relative_to(root).as_posix()], "size": path.stat().st_size} for name, path in sources.items()]}
    if pvf_snapshot is not None:
        if tools:
            raise ReleaseError("Script.pvf is only allowed in V3 update packages")
        path, sha256, size = pvf_snapshot
        sources[SCRIPT_PVF] = path
        data["files"].append({"path": SCRIPT_PVF, "sha256": sha256, "size": size})
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    asset = (TOOLS_TEST_ASSET if test_build else TOOLS_ASSET) if tools else (UPDATE_TEST_ASSET if test_build else UPDATE_ASSET)
    if pvf_snapshot is not None:
        asset = asset.replace("update-v2", "update-v3")
    if architecture == "x86":
        asset = asset.replace("win-x64", "win-x86")
    destination = output / asset
    if destination.exists() or destination.is_symlink():
        raise ReleaseError("client archive already exists")
    with tempfile.TemporaryDirectory(prefix="dflegacy-client-", dir=output) as temporary:
        archive = Path(temporary) / asset
        manifest_name = "client-package.json" if tools else "update-manifest.json"
        metadata = {manifest_name: (json.dumps(data, sort_keys=True, indent=2) + "\n").encode()}
        if tools:
            metadata["README.txt"] = migration_instructions(version, commit, data["flavor"])
            hashes = {row["path"]: row["sha256"] for row in data["files"]} | {name: hashlib.sha256(value).hexdigest() for name, value in metadata.items()}
            metadata["SHA256SUMS"] = "".join(hashes[name] + "  " + name + "\n" for name in sorted(hashes)).encode()
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for name in (*sources, *metadata):
                info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with z.open(info, "w") as target:
                    if name in sources:
                        with sources[name].open("rb") as source:
                            shutil.copyfileobj(source, target, 1024 * 1024)
                    else:
                        target.write(metadata[name])
        verify_client_package(archive, version, commit, asset)
        archive.rename(destination)
    return destination


def build_client_packages(client_assets, output, version, commit, updates_only=False, script_pvf=None):
    if script_pvf is not None:
        if not updates_only:
            raise ReleaseError("--script-pvf requires --updates-only")
        if not (Path(client_assets) / "launcher-x86").is_dir():
            raise ReleaseError("V3 update packages require an x86 launcher")
        script_pvf = Path(script_pvf)
        if script_pvf.is_symlink() or not script_pvf.is_file() or not 0 < script_pvf.stat().st_size < MAX_UPDATE_EXPANDED:
            raise ReleaseError("--script-pvf must be a regular nonempty file within the package limit")
        output = Path(output)
        output.mkdir(parents=True, exist_ok=True)
        for name in V3_UPDATE_ASSETS:
            if (output / name).exists() or (output / name).is_symlink():
                raise ReleaseError("client archive already exists")
        with tempfile.TemporaryDirectory(prefix="dflegacy-client-v3-", dir=output) as temporary:
            stage = Path(temporary)
            snapshot = stage / SCRIPT_PVF
            shutil.copyfile(script_pvf, snapshot)
            pvf_snapshot = snapshot, digest(snapshot), snapshot.stat().st_size
            packages = [build_client_update(client_assets, stage, version, commit, test,
                                            architecture=architecture, pvf_snapshot=pvf_snapshot)
                        for architecture in ("x64", "x86") for test in (False, True)]
            return [package.rename(output / package.name) for package in packages]
    packages = [build_client_update(client_assets, output, version, commit, test, tools)
                for tools in ((False,) if updates_only else (False, True)) for test in (False, True)]
    if (Path(client_assets) / "launcher-x86").is_dir():
        packages.extend(build_client_update(client_assets, output, version, commit, test, architecture="x86")
                        for test in (False, True))
    return packages


def dotnet_environment():
    # MSBuild imports environment property names case-insensitively. A release
    # tag such as VERSION=v1.2.3 becomes an invalid NuGet Version during restore.
    return {key: value for key, value in os.environ.items() if key.casefold() != "version"}


def publish_dotnet(project, rid, output):
    command = ["dotnet", "publish", str(REPO / "src" / project / (project + ".csproj")), "-c", "Release", "-r", rid,
               "--self-contained", "true", "-p:UseAppHost=true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
               "-p:DebugType=embedded", "-p:PublishTrimmed=false", "-p:ContinuousIntegrationBuild=true", "-p:BuildRandomNativeReference=false", "-o", str(output)]
    subprocess.run(command, cwd=REPO, check=True, env=dotnet_environment())


def executable(path):
    return path.name in ("start.sh", "Start.command", "DFLegacy.Manager", "DFLegacy.Server", "DFLegacy.Channel", "createdump")


def source_epoch():
    raw = os.environ.get("SOURCE_DATE_EPOCH", "0")
    if not raw.isascii() or not raw.isdigit() or not 0 <= int(raw) <= 253402300799:
        raise ReleaseError("invalid SOURCE_DATE_EPOCH")
    return int(raw)


def normalize_web_metadata(root, epoch):
    stamp = format_datetime(datetime.fromtimestamp(epoch, timezone.utc), usegmt=True)
    for path in root.glob("*.staticwebassets.endpoints.json"):
        data = read_json(path)
        def walk(value):
            if isinstance(value, dict):
                if isinstance(value.get("Name"), str) and value["Name"].casefold() == "last-modified" and "Value" in value:
                    value["Value"] = stamp
                for child in value.values():
                    walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)
        walk(data)
        write_json(path, data)


def make_archive(root, destination, rid):
    if destination.exists():
        raise ReleaseError("archive already exists")
    if rid.startswith("win"):
        with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in files(root):
                info = zipfile.ZipInfo(root.name + "/" + path.relative_to(root).as_posix(), (2020, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | (0o755 if executable(path) else 0o644)) << 16
                archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED)
    else:
        import gzip
        with destination.open("xb") as target, gzip.GzipFile(filename="", mode="wb", fileobj=target, mtime=0) as compressed, tarfile.open(fileobj=compressed, mode="w") as archive:
            for path in files(root):
                info = tarfile.TarInfo(root.name + "/" + path.relative_to(root).as_posix())
                info.size, info.mtime, info.mode = path.stat().st_size, 0, 0o755 if executable(path) else 0o644
                with path.open("rb") as source:
                    archive.addfile(info, source)


def publish(rid, version, commit, client_assets, output, manager_output=None, server_output=None, channel_output=None, verification="cross-built; native execution not performed", directory_only=False):
    validate_identity(version, commit)
    runtime_files(rid)
    epoch = source_epoch()
    output = new_output(output)
    archive = Path(str(output) + (".zip" if rid.startswith("win") else ".tar.gz"))
    if archive.exists():
        raise ReleaseError("archive output already exists")
    if not isinstance(verification, str) or not verification.strip() or len(verification) > 2000:
        raise ReleaseError("explicit verification description required")
    verify_assets(client_assets, version, commit)
    with tempfile.TemporaryDirectory(prefix="dflegacy-publish-", dir=output.parent) as temp:
        temp = Path(temp)
        stage = temp / output.name
        stage.mkdir()
        for source, assembly, relative in ((manager_output, "DFLegacy.Manager", ""), (server_output, "DFLegacy.Server", "runtime/server"), (channel_output, "DFLegacy.Channel", "runtime/channel")):
            if source is None:
                source = temp / (assembly + "-published")
                publish_dotnet(assembly, rid, source)
            source = Path(source)
            check_app(source, assembly, rid, web=assembly != "DFLegacy.Channel")
            destination = stage / relative
            copy_tree(source, destination)
            # ASP.NET's generated HTTP metadata otherwise contains checkout and
            # compression wall-clock timestamps even under deterministic MSBuild.
            normalize_web_metadata(destination, epoch)
            # Repository development config is intentionally replaced by templates.
            for name in ("server.json", "launcher.json", "appsettings.Development.json"):
                (destination / name).unlink(missing_ok=True)
            if not rid.startswith("win"):
                (destination / "DFLegacy.RandomNative.dll").unlink(missing_ok=True)
        copy_tree(Path(client_assets), stage / "client-assets")
        copy_tree(REPO / "deploy/templates", stage / "templates")
        copy_tree(REPO / "deploy/start", stage)
        (stage / "resources").mkdir()
        (stage / "resources/README.txt").write_text("Upload your own Script.pvf in Manager's service configuration; it is saved under the persistent data/resources directory. Existing external PVF paths remain supported. Game resources are not distributed.\n", encoding="utf-8")
        for path in files(stage):
            if executable(path):
                path.chmod(0o755)
        write_json(stage / "release-manifest.json", manifest(stage, version, commit, rid=rid, verification=verification))
        verify(stage, version, commit)
        staged_archive = temp / archive.name
        if not directory_only:
            make_archive(stage, staged_archive, rid)
            verify(staged_archive, version, commit)
        stage.rename(output)
        if not directory_only:
            staged_archive.rename(archive)
    return output, None if directory_only else archive


def unpack(archive, destination):
    total, count, seen = 0, 0, set()
    def target(name, size, regular):
        nonlocal total, count
        path = safe_path(name.rstrip("/"))
        folded = str(path).casefold()
        if not regular or folded in seen:
            raise ReleaseError("archive contains links, special or duplicate files")
        seen.add(folded)
        total += size
        count += 1
        if total > MAX_ARCHIVE or count > 50000:
            raise ReleaseError("archive exceeds limits")
        return destination.joinpath(*path.parts)
    if zipfile.is_zipfile(archive):
        with zipfile.ZipFile(archive) as source:
            for row in source.infolist():
                mode = row.external_attr >> 16
                path = target(row.filename, row.file_size, not stat.S_ISLNK(mode) and (not stat.S_IFMT(mode) or stat.S_ISREG(mode) or row.is_dir()))
                if row.is_dir():
                    path.mkdir(parents=True, exist_ok=True)
                else:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    with source.open(row) as stream, path.open("xb") as output:
                        shutil.copyfileobj(stream, output)
                    path.chmod(mode & 0o777 or 0o644)
    else:
        try:
            with tarfile.open(archive, "r:gz") as source:
                for row in source:
                    path = target(row.name, row.size, row.isfile() or row.isdir())
                    if row.isdir():
                        path.mkdir(parents=True, exist_ok=True)
                    else:
                        path.parent.mkdir(parents=True, exist_ok=True)
                        with source.extractfile(row) as stream, path.open("xb") as output:
                            shutil.copyfileobj(stream, output)
                        path.chmod(row.mode & 0o777)
        except tarfile.TarError as error:
            raise ReleaseError("invalid release archive") from error
    children = list(destination.iterdir())
    if len(children) != 1 or not children[0].is_dir():
        raise ReleaseError("archive must contain exactly one release root")
    return children[0]


def verify(path, version=None, commit=None):
    path = Path(path)
    if path.is_file():
        with tempfile.TemporaryDirectory(prefix="dflegacy-verify-") as temp:
            return verify(unpack(path, Path(temp)), version, commit)
    data = check_manifest(path, "release-manifest.json", version, commit)
    runtime_files(data.get("rid"))
    if not data["rid"].startswith("win") and any(
        (root / "DFLegacy.RandomNative.dll").exists() for root in (path, path / "runtime/server", path / "runtime/channel")):
        raise ReleaseError("Windows RandomNative binary is forbidden in Unix runtime directories")
    if not isinstance(data.get("verification"), str) or not data["verification"].strip():
        raise ReleaseError("missing truthful verification description")
    verify_assets(path / "client-assets", data["version"], data["commit"])
    for root, assembly in ((path, "DFLegacy.Manager"), (path / "runtime/server", "DFLegacy.Server"), (path / "runtime/channel", "DFLegacy.Channel")):
        check_app(root, assembly, data["rid"], web=assembly != "DFLegacy.Channel")
    required = ("templates/server.json", "templates/manager.json", "resources/README.txt", "Start.cmd", "start.sh", "Start.command")
    if any(not (path / name).is_file() for name in required):
        raise ReleaseError("incomplete deployment layout")
    template = read_json(path / "templates/server.json").get("DFLegacy", {})
    if template.get("SeedTestAccount") is not False or template.get("Admin", {}).get("Users") != []:
        raise ReleaseError("unsafe default account seeding")
    if (path / "data").exists():
        raise ReleaseError("mutable data must not ship in release")
    if os.name != "nt" and not data["rid"].startswith("win"):
        for item in (path / "start.sh", path / "Start.command", path / "DFLegacy.Manager", path / "runtime/server/DFLegacy.Server", path / "runtime/channel/DFLegacy.Channel"):
            if item.stat().st_mode & 0o111 != 0o111:
                raise ReleaseError("Unix executable mode missing")
    return data


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ReleaseError("API redirect refused to protect credentials")


class Gitea:
    def __init__(self, url, repository, token, allow_http=False):
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment or (parsed.scheme == "http" and not allow_http):
            raise ReleaseError("use HTTPS Gitea URL; explicit --allow-http required for trusted HTTP instances")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or any(p in (".", "..") for p in repository.split("/")) or not token or "\n" in token or "\r" in token:
            raise ReleaseError("invalid repository/token")
        self.base = url.rstrip("/") + "/api/v1/repos/" + repository
        self.origin = (parsed.scheme, parsed.hostname.lower(), parsed.port or (443 if parsed.scheme == "https" else 80))
        self.path_prefix = parsed.path.rstrip("/") + "/"
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())

    def request(self, method, path, data=None, content_type="application/json", missing_ok=False):
        payload = json.dumps(data).encode() if data is not None and not isinstance(data, bytes) else data
        request = urllib.request.Request(self.base + path, data=payload, method=method,
                                         headers={"Authorization": "token " + self.token, "Content-Type": content_type, "Accept": "application/json"})
        try:
            with self.opener.open(request, timeout=120) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if error.code == 404 and missing_ok:
                return None
            raise ReleaseError(f"Gitea {method} failed (HTTP {error.code}); inspect the release before retrying") from None
        except (urllib.error.URLError, ValueError) as error:
            raise ReleaseError("Gitea request failed; inspect draft before retrying") from error

    def upload(self, release_id, name, content):
        boundary = "dflegacy" + uuid.uuid4().hex
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="attachment"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode() + content + f"\r\n--{boundary}--\r\n".encode()
        return self.request("POST", f"/releases/{release_id}/assets?name=" + urllib.parse.quote(name, safe=""), body, "multipart/form-data; boundary=" + boundary)

    def attachment_digest(self, attachment, size):
        """Hash immutable attachment bytes; never forward credentials elsewhere."""
        if attachment.get("size") != size or not isinstance(attachment.get("browser_download_url"), str):
            raise ReleaseError("existing attachment has wrong size or no download URL")
        url = attachment["browser_download_url"]
        parsed = urllib.parse.urlsplit(url)
        origin = (parsed.scheme, (parsed.hostname or "").lower(), parsed.port or (443 if parsed.scheme == "https" else 80))
        if origin != self.origin or parsed.username or parsed.password or parsed.fragment or not parsed.path.startswith(self.path_prefix):
            raise ReleaseError("attachment download must remain on the configured Gitea origin")
        request = urllib.request.Request(url, headers={"Authorization": "token " + self.token, "Accept": "application/octet-stream"})
        checksum, actual = hashlib.sha256(), 0
        try:
            with self.opener.open(request, timeout=120) as response:
                while block := response.read(1024 * 1024):
                    actual += len(block)
                    if actual > size:
                        raise ReleaseError("existing attachment exceeds its expected size")
                    checksum.update(block)
        except (urllib.error.URLError, ValueError) as error:
            raise ReleaseError("cannot verify existing attachment bytes") from error
        if actual != size:
            raise ReleaseError("existing attachment was truncated")
        return checksum.hexdigest()


def release(api, version, commit, archives, image_digest=None, check_only=False, reserve=False):
    validate_identity(version, commit)
    checked, platforms = [], set()
    names = set()
    for path in map(Path, archives):
        safe_path(path.name)
        if path.name in names:
            raise ReleaseError("duplicate archive name")
        names.add(path.name)
        data = verify(path, version, commit)
        if data["rid"] in platforms:
            raise ReleaseError("duplicate platform")
        platforms.add(data["rid"])
        checked.append((path.name, digest(path), path))
    if platforms != set(RIDS):
        raise ReleaseError("release requires all five verified platform archives")
    for name, source in (("compose.yaml", REPO / "compose.yaml"), ("compose.host-ipv6.yaml", REPO / "deploy/compose.host-ipv6.yaml"), ("compose.pvf.yaml", REPO / "deploy/compose.pvf.yaml"), (".env.example", REPO / ".env.example")):
        if not source.is_file() or source.is_symlink() or name in names:
            raise ReleaseError("missing or conflicting standalone deployment example")
        scan(source)
        checked.append((name, digest(source), source))
    checked.sort(key=lambda row: row[0])
    if image_digest is not None and not re.fullmatch(r"[^\s@]+@sha256:[a-f0-9]{64}", image_digest):
        raise ReleaseError("invalid immutable image digest reference")
    inventory = {"version": version, "commit": commit, "files": [{"path": name, "sha256": sha} for name, sha, _ in checked], "image": image_digest}
    inventory_bytes = (json.dumps(inventory, sort_keys=True, indent=2) + "\n").encode()
    marker = OWNER_MARKER + "\ncommit=" + commit + "\ninventory-sha256=" + hashlib.sha256(inventory_bytes).hexdigest()
    body = marker + "\n\nAll five packages were verified before this draft was created. Platform execution limits are in each release-manifest.json."
    if image_digest:
        body += "\nContainer: `" + image_digest + "`"
    tag_path = "/releases/tags/" + urllib.parse.quote(version, safe="")
    def fetch():
        current = api.request("GET", tag_path, missing_ok=True)
        if current is not None and (not isinstance(current, dict) or not current.get("body", "").startswith(marker + "\n")
                                    or current.get("target_commitish") != commit or current.get("tag_name") != version
                                    or type(current.get("id")) is not int or type(current.get("draft")) is not bool):
            raise ReleaseError("release identity differs; refusing any mutation")
        return current
    existing = fetch()
    tag = api.request("GET", "/tags/" + urllib.parse.quote(version, safe=""), missing_ok=True)
    if tag is None and OFFICIAL.fullmatch(version):
        raise ReleaseError("official version requires an existing matching Git tag")
    if tag is not None and tag.get("commit", {}).get("sha") != commit:
        raise ReleaseError("Git tag points to a different commit")
    expected = {name: (sha, path.stat().st_size, path) for name, sha, path in checked}
    expected["release-inventory.json"] = (hashlib.sha256(inventory_bytes).hexdigest(), len(inventory_bytes), None)
    verified = set()
    def inspect():
        current = fetch()
        if current is None:
            raise ReleaseError("reserved release disappeared; refusing to recreate it")
        assets = api.request("GET", f'/releases/{current["id"]}/assets')
        if not isinstance(assets, list):
            raise ReleaseError("invalid Gitea attachment listing")
        present = set()
        for asset in assets:
            if not isinstance(asset, dict) or asset.get("name") not in expected or type(asset.get("id")) is not int:
                raise ReleaseError("unexpected attachment in reserved release")
            name = asset["name"]
            sha, size, _ = expected[name]
            key = (asset["id"], name, size, asset.get("browser_download_url"))
            if key not in verified:
                if api.attachment_digest(asset, size) != sha:
                    raise ReleaseError("existing attachment digest differs; refusing to replace it")
                verified.add(key)
            present.add(name)
        # The listing may have raced another run publishing this exact identity.
        latest = fetch()
        if latest is None or latest["id"] != current["id"]:
            raise ReleaseError("release changed during attachment verification")
        if not latest["draft"] and present != set(expected):
            raise ReleaseError("published release is incomplete; refusing attachment mutations")
        return latest, present
    def outcome(status, current):
        return {"status": status, "version": version, "commit": commit, "release_id": current["id"] if current else None}
    if check_only and existing is None:
        return outcome("absent", None)
    if existing is None:
        try:
            api.request("POST", "/releases", {"tag_name": version, "target_commitish": commit, "name": version, "body": body,
                                               "draft": True, "prerelease": bool(DEV.fullmatch(version))})
        except ReleaseError:
            # CreateRelease's unique tag is the cross-run reservation. A losing
            # run may continue only if the winner bound the identical inventory.
            if fetch() is None:
                raise
    current, present = inspect()
    if not current["draft"]:
        return outcome("already-published", current)
    if check_only or reserve:
        return outcome("reserved" if reserve else "draft", current)
    for name, (sha, size, path) in expected.items():
        if name in present:
            continue
        current, present = inspect()
        if not current["draft"]:
            return outcome("already-published", current)
        if name in present:
            continue
        content = path.read_bytes() if path is not None else inventory_bytes
        if len(content) != size or hashlib.sha256(content).hexdigest() != sha:
            raise ReleaseError("local artifact changed after its release identity was reserved")
        try:
            api.upload(current["id"], name, content)
        except ReleaseError:
            # A matching concurrent uploader may have supplied this file. Never
            # delete/rewrite an attachment, including when a draft became public.
            current, present = inspect()
            if name not in present:
                raise
    current, present = inspect()
    if present != set(expected):
        raise ReleaseError("draft attachments are incomplete")
    if not current["draft"]:
        return outcome("already-published", current)
    api.request("PATCH", f'/releases/{current["id"]}', {"draft": False})
    return outcome("published", current)


def prune(api, keep=20, apply=False):
    if not isinstance(keep, int) or keep < 1:
        raise ReleaseError("keep must be a positive integer")
    releases, page = [], 1
    while True:
        batch = api.request("GET", f"/releases?limit=50&page={page}")
        if not isinstance(batch, list):
            raise ReleaseError("invalid Gitea release listing")
        releases.extend(batch)
        if len(batch) < 50:
            break
        page += 1
    owned = [row for row in releases if DEV.fullmatch(row.get("tag_name", "")) and row.get("prerelease") is True
             and row.get("body", "").startswith(OWNER_MARKER) and not row.get("draft")]
    owned.sort(key=lambda row: row["tag_name"], reverse=True)
    selected = owned[keep:]
    if apply:
        for row in selected:
            api.request("DELETE", f'/releases/{int(row["id"])}')
            api.request("DELETE", "/tags/" + urllib.parse.quote(row["tag_name"], safe=""))
    return [row["tag_name"] for row in selected]


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    sub = result.add_subparsers(dest="command", required=True)
    build = sub.add_parser("build-client-assets")
    for name in ("launcher-output", "client-patch", "config", "output", "version", "commit"):
        build.add_argument("--" + name, required=True)
    build.add_argument("--debug")
    build.add_argument("--launcher-x86-output")
    update = sub.add_parser("build-client-update")
    for name in ("client-assets", "output", "version", "commit"):
        update.add_argument("--" + name, required=True)
    update.add_argument("--test-build", action="store_true")
    update.add_argument("--tools", action="store_true")
    all_packages = sub.add_parser("build-client-packages")
    for name in ("client-assets", "output", "version", "commit"):
        all_packages.add_argument("--" + name, required=True)
    all_packages.add_argument("--updates-only", action="store_true")
    all_packages.add_argument("--script-pvf", help="opt in to the four V3 updates containing this PVF export; requires --updates-only and both launcher architectures")
    update_check = sub.add_parser("verify-client-update")
    update_check.add_argument("path")
    update_check.add_argument("--version")
    update_check.add_argument("--commit")
    package = sub.add_parser("publish")
    for name in ("rid", "version", "commit", "client-assets", "output"):
        package.add_argument("--" + name, required=True, choices=RIDS if name == "rid" else None)
    for name in ("manager-output", "server-output", "channel-output"):
        package.add_argument("--" + name)
    package.add_argument("--directory-only", action="store_true")
    package.add_argument("--verification", default="cross-built; native execution not performed")
    check = sub.add_parser("verify")
    check.add_argument("path")
    check.add_argument("--version")
    check.add_argument("--commit")
    check.add_argument("--client-assets", action="store_true")
    sub.add_parser("test")
    for command in ("release", "prune"):
        remote = sub.add_parser(command)
        remote.add_argument("--gitea-url", required=True)
        remote.add_argument("--repository", required=True)
        remote.add_argument("--token-env", default="GITEA_TOKEN")
        remote.add_argument("--allow-http", action="store_true")
        if command == "release":
            remote.add_argument("--version", required=True)
            remote.add_argument("--commit", required=True)
            remote.add_argument("--archive", action="append", required=True)
            remote.add_argument("--image-digest")
            remote.add_argument("--check-only", action="store_true", help="verify all archives and remote identity without any mutation")
            remote.add_argument("--reserve", action="store_true", help="atomically reserve matching draft identity before a registry write; upload nothing")
        else:
            remote.add_argument("--keep", type=int, default=20)
            remote.add_argument("--apply", action="store_true", help="delete only owned dev releases/tags; default is dry-run")
    return result


def main(argv=None):
    args = parser().parse_args(argv)
    try:
        if args.command == "test":
            return subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", str(Path(__file__).parent / "tests"), "-v"], cwd=REPO).returncode
        if args.command == "build-client-assets":
            print(build_client_assets(args.launcher_output, args.client_patch, args.config, args.output, args.version, args.commit,
                                      args.debug, launcher_x86=args.launcher_x86_output))
        elif args.command == "build-client-update":
            print(build_client_update(args.client_assets, args.output, args.version, args.commit, args.test_build, args.tools))
        elif args.command == "build-client-packages":
            print(json.dumps([str(p) for p in build_client_packages(args.client_assets, args.output, args.version, args.commit,
                                                                   updates_only=args.updates_only, script_pvf=args.script_pvf)]))
        elif args.command == "verify-client-update":
            print(json.dumps(verify_client_update(args.path, args.version, args.commit)))
        elif args.command == "publish":
            _, archive = publish(args.rid, args.version, args.commit, args.client_assets, args.output, args.manager_output, args.server_output, args.channel_output, args.verification, args.directory_only)
            print(archive or args.output)
        elif args.command == "verify":
            data = (verify_assets if args.client_assets else verify)(Path(args.path), args.version, args.commit)
            print(json.dumps({key: value for key, value in data.items() if key != "files"}, ensure_ascii=False))
        else:
            api = Gitea(args.gitea_url, args.repository, os.environ.get(args.token_env, ""), args.allow_http)
            if args.command == "release":
                if args.check_only and args.reserve:
                    raise ReleaseError("--check-only and --reserve are mutually exclusive")
                print(json.dumps(release(api, args.version, args.commit, args.archive, args.image_digest, args.check_only, args.reserve)))
            else:
                print(json.dumps({"dry_run": not args.apply, "delete": prune(api, args.keep, args.apply)}))
        return 0
    except (ReleaseError, OSError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print("release: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    if sys.version_info < (3, 12):
        sys.exit("Python 3.12+ is required")
    sys.exit(main())
