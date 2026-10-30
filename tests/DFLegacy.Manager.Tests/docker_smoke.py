"""Exercise a built Linux image, including real game lifecycle and persistent data.

Python 3.12+ standard library. Creates only disposable named containers/volumes.
No private game resources, registry access or remote publication is used.
"""
import argparse
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
                docker("run", "-d", "--name", identifier, "-p", "127.0.0.1::18080", "--mount", f"type=volume,source={volume},target=/app/data",
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
            req = urllib.request.Request(base + "/api/export/client", b"{}", {"Content-Type": "application/json", "X-CSRF-Token": csrf}, method="POST")
            target = fixture / "client.zip"
            with opener.open(req, timeout=180) as response, target.open("wb") as output:
                shutil.copyfileobj(response, output)
            with zipfile.ZipFile(target) as archive:
                profile = json.loads(archive.read("launcher.json"))
                check(profile["Accounts"] == [] and profile["Password"] == "" and not profile["StartServer"], "Linux container exports a complete Windows launcher without credentials")
                check("SHA256SUMS" in archive.namelist() and "DFLegacy.Launcher.exe" in archive.namelist() and "ijl15.dll" in archive.namelist() and "Config.ini" not in archive.namelist() and not any("coreclr" in name or name.startswith("DFLegacyLauncher/") for name in archive.namelist()), "export includes a root single-file launcher and patch without overwriting client INI")
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
