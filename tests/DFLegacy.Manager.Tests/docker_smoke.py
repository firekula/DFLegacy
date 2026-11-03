"""Exercise a built Linux image, including real game lifecycle and persistent data.

Python 3.12+ standard library. Creates only disposable named containers/volumes.
No private game resources, registry access or remote publication is used.
"""
import argparse
import hashlib
import http.cookiejar
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid
import zipfile


def docker(*arguments):
    return subprocess.check_output(["docker", *map(str, arguments)], text=True, stderr=subprocess.STDOUT).strip()


def fixture_pvf(path):
    name, payload = b"creature/exptable.tbl", b"10\n100\n"
    tree = struct.pack("<IIiIi", 1, 0, len(payload), 0, len(name)) + name
    def encrypt(data):
        data += b"\0" * (-len(data) % 4)
        words = struct.unpack("<" + "I" * (len(data) // 4), data)
        return b"".join(struct.pack("<I", ((word << 6) | (word >> 26)) & 0xFFFFFFFF) for word in words)
    encrypted_tree = encrypt(tree)
    path.write_bytes(b"DNF_SCRIPT_PACK" + struct.pack("<iII", len(encrypted_tree), 0, 1) + encrypted_tree + encrypt(payload))
    # Only this disposable synthetic file is shared with the app's non-root UID.
    path.chmod(0o644)


def exercise(image):
    identifier = "dflegacy-manager-smoke-" + uuid.uuid4().hex[:12]
    volume = identifier + "-data"
    active = False
    checks = 0
    def check(condition, name):
        nonlocal checks
        if not condition:
            raise AssertionError(name)
        checks += 1
        print("PASS: " + name, flush=True)
    with tempfile.TemporaryDirectory(prefix="DFLegacy Docker fixture ") as directory:
        fixture = Path(directory)
        fixture_pvf(fixture / "Script.pvf")
        docker("volume", "create", volume)
        try:
            def start(controlled=False):
                nonlocal active
                docker("run", "-d", "--name", identifier, "-p", "127.0.0.1::18080", "-p", "127.0.0.1::18083",
                       "-e", "DFLEGACY_UPDATE_PUBLIC_URL=https://games.example.test:18443", "--mount", f"type=volume,source={volume},target=/app/data",
                       "--mount", f"type=bind,source={fixture / 'Script.pvf'},target=/pvf/Script.pvf,readonly",
                       *(["-e", "DFLegacy__ScriptPvfPath=/pvf/Script.pvf"] if controlled else []), image)
                active = True
                mapping = docker("port", identifier, "18080/tcp").splitlines()[0]
                base = "http://" + mapping
                opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), urllib.request.ProxyHandler({}))
                for _ in range(150):
                    try:
                        with opener.open(base + "/health", timeout=2) as response:
                            if response.status == 200:
                                return base, opener
                    except (OSError, urllib.error.URLError):
                        time.sleep(0.2)
                raise AssertionError("container Manager health did not become ready")

            base, opener = start()
            image_info = json.loads(docker("image", "inspect", image))[0]
            labels = image_info["Config"]["Labels"]
            release = json.loads(docker("exec", identifier, "cat", "/app/release-manifest.json"))
            assets = json.loads(docker("exec", identifier, "cat", "/app/client-assets/manifest.json"))
            release_files = {row["path"]: row["sha256"] for row in release["files"]}
            asset_files = {row["path"]: row["sha256"] for row in assets["files"]}
            check(docker("inspect", "--format", "{{.Image}}", identifier) == image_info["Id"]
                  and release["version"] == assets["version"] == labels["org.opencontainers.image.version"]
                  and release["commit"] == assets["commit"] == labels["org.opencontainers.image.revision"],
                  "installed client assets and container release identity match actual image OCI labels")
            check(docker("exec", identifier, "sha256sum", "/app/client-assets/manifest.json").split()[0]
                  == release_files["client-assets/manifest.json"], "installed client asset manifest is anchored to release SHA256")
            csrf = ""
            def request(path, data=None, method=None):
                headers = {"Content-Type": "application/octet-stream" if isinstance(data, bytes) else "application/json"}
                if csrf:
                    headers["X-CSRF-Token"] = csrf
                body = data if isinstance(data, bytes) else json.dumps(data).encode() if data is not None else None
                with opener.open(urllib.request.Request(base + path, body, headers, method=method), timeout=60) as response:
                    raw = response.read()
                    return json.loads(raw) if raw else None
            check(docker("exec", identifier, "id", "-u") != "0", "container runs as non-root")
            token = docker("exec", identifier, "cat", "/app/data/management/setup-token.txt")
            password = "Disposable-test-password-123!"
            request("/api/setup", {"setupToken": token, "userName": "smokeoperator", "password": password})
            csrf = request("/api/session")["csrf"]
            check(not request("/api/setup/status")["required"], "setup claims a one-use management identity")
            uploaded = request("/api/resources/pvf", (fixture / "Script.pvf").read_bytes())
            check(uploaded["available"] and uploaded["path"].startswith("/app/data/resources/"), "non-root administrator uploads PVF to persistent data without a host path")
            saved = request("/api/config")["saved"]
            request("/api/config", saved, "PUT")
            request("/api/game-admin", {"userName": "firstadmin", "password": "Disposable-game-password-123!", "oicqCode": "12345678"})
            check("firstadmin" in docker("exec", identifier, "cat", "/app/data/game/state.json"), "offline game admin writes to persistent game storage")
            started = request("/api/server/start", {})
            check(started["state"] == "running", "real game starts inside the container using synthetic PVF")
            check(request("/ready")["ready"], "game readiness becomes healthy after private owned probe")
            stopped = request("/api/server/stop", {})
            check(stopped["state"] == "stopped" and request("/health")["status"] == "ok", "stopping game leaves Manager healthy and reachable")
            try:
                request("/ready")
            except urllib.error.HTTPError as error:
                check(error.code == 503, "stopped game is not ready while Manager remains online")
            else:
                raise AssertionError("stopped game readiness returned success")
            normal_files = None
            for test_build in (False, True):
                flavor = "test" if test_build else "normal"
                req = urllib.request.Request(base + "/api/export/client", json.dumps({"testBuild": test_build}).encode(),
                                             {"Content-Type": "application/json", "X-CSRF-Token": csrf}, method="POST")
                target = fixture / (flavor + "-client.zip")
                with opener.open(req, timeout=180) as response, target.open("wb") as output:
                    shutil.copyfileobj(response, output)
                with zipfile.ZipFile(target) as archive:
                    binary_sources = {"DFLegacy.Launcher.exe": "launcher/DFLegacy.Launcher.exe",
                                      "DFLegacy.Client.dll": "patch/DFLegacy.Client.dll"}
                    if test_build:
                        binary_sources["debug.dll"] = "patch/debug.dll"
                    sources = dict(binary_sources)
                    if "launcher/Assets/Skin/fwtk.html" in asset_files:
                        sources["Assets/Skin/fwtk.html"] = "launcher/Assets/Skin/fwtk.html"
                    names = archive.namelist()
                    expected = set(sources) | {"client-package.json", "SHA256SUMS", "launcher.json", "README.txt"}
                    check(len(names) == len(expected) and set(names) == expected,
                          flavor + " export has exact root player bundle inventory, no IJL, loose bootstrap, client INI, originals or runtime tree")
                    profile = json.loads(archive.read("launcher.json"))
                    check(profile["Accounts"] == [] and profile["Account"] == "" and profile["Password"] == "" and not profile["StartServer"],
                          flavor + " Linux container export contains no player credentials or server autostart")
                    package = json.loads(archive.read("client-package.json"))
                    check(set(package) == {"schemaVersion", "version", "commit", "flavor", "files"}
                          and type(package["schemaVersion"]) is int and package["schemaVersion"] == 2
                          and package["flavor"] == flavor and package["version"] == release["version"]
                          and package["commit"] == release["commit"], flavor + " installed schema2 manifest matches image release identity and selected flavor")
                    rows = package["files"]
                    check(isinstance(rows, list) and len(rows) == len(binary_sources)
                          and all(isinstance(row, dict) and set(row) == {"path", "size", "sha256"} for row in rows)
                          and {row["path"] for row in rows} == set(binary_sources), flavor + " manifest declares exactly the selected binaries")
                    hashes = {}
                    for name in expected - {"SHA256SUMS"}:
                        with archive.open(name) as stream:
                            hashes[name] = hashlib.file_digest(stream, "sha256").hexdigest()
                    checksums = [line.split("  ") for line in archive.read("SHA256SUMS").decode("utf-8").splitlines()]
                    check(len(checksums) == len(hashes) and all(len(row) == 2 for row in checksums)
                          and {row[1]: row[0] for row in checksums} == hashes,
                          flavor + " SHA256SUMS covers every exported payload exactly once with correct hashes")
                    for row in rows:
                        name = row["path"]
                        check(type(row["size"]) is int and row["size"] > 0 and row["size"] == archive.getinfo(name).file_size
                              and row["size"] == int(docker("exec", identifier, "stat", "-c", "%s", "/app/client-assets/" + binary_sources[name]))
                              and row["sha256"] == hashes[name], flavor + " manifest binary size and SHA256 match exported and installed " + name)
                    check(all(hashes[name] == asset_files[source] == release_files["client-assets/" + source]
                              for name, source in sources.items()), flavor + " exported binaries and optional skin match release-anchored installed assets")
                    instructions = archive.read("README.txt").decode("utf-8")
                    check(release["version"] in instructions and release["commit"] in instructions
                          and "DFLegacy.Launcher.exe" in instructions and "DFLegacy.Client.dll" in instructions,
                          flavor + " generated installation instructions identify the release and independent client")
                    identities = {row["path"]: row for row in rows}
                    if test_build:
                        check(all(identities[name] == row for name, row in normal_files.items()),
                              "test export preserves normal Launcher and Client binary identities while adding same-release debug.dll")
                    else:
                        normal_files = identities
            if "launcher-x86/DFLegacy.Launcher.exe" in asset_files:
                update_base = "http://" + docker("port", identifier, "18083/tcp").splitlines()[0]
                for private in ("/", "/health", "/api/setup/status"):
                    try:
                        opener.open(update_base + private, timeout=10)
                    except urllib.error.HTTPError as error:
                        check(error.code == 404, "update port isolates " + private)
                    else:
                        raise AssertionError("private route exposed on update port")
                job = request("/api/exports", {"testBuild": False})
                for _ in range(450):
                    job = request("/api/exports/" + job["id"])
                    if job["state"] == "ready":
                        break
                    if job["state"] == "failed":
                        raise AssertionError(job["error"])
                    time.sleep(.2)
                else:
                    raise AssertionError("dual-architecture export timed out")
                for arch in ("x86", "x64"):
                    route = f"{update_base}/v1/{arch}/normal"
                    with opener.open(route + "/latest", timeout=10) as response:
                        update = json.load(response)
                    with opener.open(f"{route}/{update['sha256']}.zip", timeout=60) as response:
                        digest = hashlib.file_digest(response, "sha256").hexdigest()
                    check(update["version"] == release["version"] and digest == update["sha256"],
                          f"{arch} hosted update matches installed image release and archive")
            request("/api/server/start", {})
            docker("stop", "--time", "40", identifier)
            check(docker("inspect", "--format", "{{.State.ExitCode}}", identifier) == "0", "container SIGTERM gracefully stops Manager and owned game")
            docker("rm", identifier); active = False
            base, opener = start()
            csrf = ""
            check(not request("/api/setup/status")["required"], "recreated container retains management account")
            request("/api/login", {"userName": "smokeoperator", "password": password})
            csrf = request("/api/session")["csrf"]
            check(request("/api/config")["configured"] and "firstadmin" in docker("exec", identifier, "cat", "/app/data/game/state.json"), "recreated container retains configuration and game data")
            check(request("/api/resources/pvf")["path"] == uploaded["path"] and request("/api/resources/pvf")["available"], "recreated container retains uploaded PVF")
            docker("stop", "--time", "40", identifier); docker("rm", identifier); active = False
            base, opener = start(controlled=True); csrf = ""
            request("/api/login", {"userName": "smokeoperator", "password": password}); csrf = request("/api/session")["csrf"]
            check(request("/api/resources/pvf")["externallyControlled"], "explicit read-only PVF mounts retain compatibility")
            try:
                request("/api/resources/pvf", (fixture / "Script.pvf").read_bytes())
            except urllib.error.HTTPError as error:
                check(error.code == 400, "environment controlled PVF cannot be replaced by upload")
            else:
                raise AssertionError("mounted PVF upload was accepted")
            check(request("/api/server/start", {})["state"] == "running", "real game still starts with an explicit read-only mounted PVF")
            print(f"Docker smoke: {checks} checks passed", flush=True)
        except Exception:
            if active:
                print(docker("logs", "--tail", "50", identifier), flush=True)
            raise
        finally:
            if active:
                docker("rm", "-f", identifier)
            docker("volume", "rm", volume)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    exercise(parser.parse_args().image)
