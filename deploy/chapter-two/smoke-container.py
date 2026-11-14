"""Smoke-test an isolated Linux Chapter Two container with a temporary save."""

import argparse
import json
import secrets
import subprocess
import time
from pathlib import Path


def docker(*args, payload=None):
    return subprocess.run(["docker", *args], input=payload, capture_output=True,
                          check=True, timeout=90).stdout


def smoke(image, pvf):
    name = "dflegacy-chapter-two-smoke-" + secrets.token_hex(5)
    manager = "http://127.0.0.2:48092"
    server = "http://127.0.0.2:48091"
    cookie = "/tmp/chapter-two-smoke.cookies"

    def request(path, *, base=manager, data=None, csrf=None, bearer=None):
        flags = ["-fsS", "--max-time", "60"]
        if base == manager:
            flags += ["-b", cookie, "-c", cookie]
        if data is not None:
            flags += ["-H", "Content-Type: application/json", "--data-binary", "@-"]
        if csrf:
            flags += ["-H", "X-CSRF-Token: " + csrf]
        if bearer:
            flags += ["-H", "Authorization: Bearer " + bearer]
        return docker("exec", "-i", name, "curl", *flags, base + path,
                      payload=json.dumps(data).encode() if data is not None else None)

    def expect(condition, message):
        if not condition:
            raise RuntimeError(message)
        print(message, flush=True)

    try:
        docker("run", "-d", "--name", name, "--network", "none",
               "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
               "--mount", f"type=bind,src={pvf},dst=/app/resources/Script.pvf,readonly",
               image)
        for attempt in range(60):
            try:
                expect(json.loads(request("/health"))["status"] == "ok", "Manager healthy")
                break
            except subprocess.CalledProcessError:
                if attempt == 59:
                    raise
                time.sleep(1)
        token = docker("exec", name, "cat", "/app/data/management/setup-token.txt").decode().strip()
        password = secrets.token_urlsafe(24)
        expect(json.loads(request("/api/setup", data={
            "setupToken": token, "userName": "smoke-admin", "password": password
        }))["configured"], "Initialized an isolated management account")
        csrf = json.loads(request("/api/session"))["csrf"]
        status = json.loads(request("/api/status"))
        expect(status["chapterTwoLocal"] and not status["assetsAvailable"],
               "Chapter Two loopback profile active; client export assets absent")
        expect(json.loads(request("/api/resources/pvf"))["available"],
               "Matched external PVF visible to the manager")
        def service(command, expected):
            status = json.loads(request("/api/server/" + command, data={}, csrf=csrf))
            expect(status["state"] == expected, f"Manager {command}: {expected}")

        service("start", "running")
        expect(json.loads(request("/ready"))["ready"], "Game service ready")
        expect(json.loads(request("/health", base=server))["status"] == "ok",
               "Game account service healthy")
        expect(b"testCeraEnabled:false" in request("/client/client-pay-settings.js", base=server),
               "Test payments disabled by default")
        account = "smoke" + secrets.token_hex(3)
        game_password = secrets.token_hex(8)
        request("/api/accounts", base=server, data={
            "userName": account, "password": game_password, "oicqCode": "90519742"
        })
        service("stop", "stopped")
        service("start", "running")
        login = json.loads(request("/api/auth/login", base=server,
                                   data={"userName": account, "password": game_password}))
        expect(bool(login.get("token")), "Game account authenticates after restart on the same isolated save")
        request(f"/api/accounts/{account}/characters", base=server, bearer=login["token"])
        for port in (42312, 48090, 47001, *range(7100, 7106)):
            docker("exec", name, "bash", "-c", f"exec 3<>/dev/tcp/127.0.0.2/{port}")
        expect(True, "Entrance, channel and regional TCP listeners accept connections")
        udp = docker("exec", name, "cat", "/proc/net/udp").decode().upper()
        for port in (42312, 47012, 47013, 47005):
            expect(f"0200007F:{port:04X}" in udp, f"Loopback UDP listener {port}")
        expect(not docker("exec", name, "find", "/app", "-iname", "*.npk").strip(),
               "Container has no client image packs")
        service("stop", "stopped")
    except subprocess.CalledProcessError as error:
        print(error.stderr.decode(errors="replace").strip(), flush=True)
        raise RuntimeError("Docker or container HTTP smoke request failed") from None
    finally:
        subprocess.run(["docker", "logs", name], check=False)
        subprocess.run(["docker", "rm", "-f", "-v", name], capture_output=True, check=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image")
    parser.add_argument("pvf", type=Path)
    args = parser.parse_args()
    smoke(args.image, args.pvf.resolve(strict=True))
