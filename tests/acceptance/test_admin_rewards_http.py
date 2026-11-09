"""Real isolated ACT1 HTTP acceptance; never contacts the live server."""
import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", required=True, type=Path)
    parser.add_argument("--pvf", required=True, type=Path)
    parser.add_argument("--workdir", required=True, type=Path)
    parser.add_argument("--ui", action="store_true")
    args = parser.parse_args()
    root = args.workdir.resolve() / ("http-" + uuid.uuid4().hex)
    root.mkdir(parents=True)
    state_path = root / "state.json"
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    credentials = {"userName": "rewardsgm", "password": secrets.token_urlsafe(24)}
    cfg = root / "server.json"
    cfg.write_text(json.dumps({"DFLegacy": {
        "DataPath": str(state_path), "SeedTestAccount": False,
        "ScriptPvfPath": str(args.pvf.resolve()), "SkillScriptPath": str(root),
        "Admin": {"Host": "127.0.0.1", "Port": port},
        "Entrance": {"Enabled": False}, "Pvp": {"Enabled": False},
        "WebPanels": {"Player": {"Enabled": False}, "Gm": {"Enabled": False}},
        "FallbackRelay": {"Enabled": False}, "CharacterDatagram": {"Enabled": False},
        "GameplayDatagram": {"Enabled": False},
        "Processes": {"Channel": {"Enabled": False}, "Game": {"EntranceBinding": False}},
    }}), encoding="utf-8")
    command = ["dotnet", str(args.server.resolve()), "--config", str(cfg)]
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    env = os.environ.copy()
    env["ASPNETCORE_WEBROOT"] = str(Path(__file__).resolve().parents[2] / "src/DFLegacy.Server/wwwroot")

    def request(path, method="GET", body=None, token=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        raw = None if body is None else json.dumps(body).encode()
        try:
            response = urlopen(Request(base + path, data=raw, method=method, headers=headers), timeout=12)
        except HTTPError as error:
            response = error
        with response:
            payload = response.read()
            return response.status, json.loads(payload) if payload else None

    @contextmanager
    def host():
        with (root / "server.log").open("ab") as log:
            process = subprocess.Popen(command, cwd=root, env=env, stdin=subprocess.DEVNULL,
                                       stdout=log, stderr=subprocess.STDOUT, creationflags=flags)
            try:
                for _ in range(200):
                    if process.poll() is not None:
                        raise AssertionError(f"Isolated host exited: {process.returncode}; log={root / 'server.log'}")
                    try:
                        if request("/health")[0] == 200:
                            break
                    except (URLError, TimeoutError):
                        pass
                    time.sleep(0.05)
                else:
                    raise AssertionError("Isolated host did not start")
                yield
            finally:
                if process.poll() is None:
                    process.terminate()
                process.wait(timeout=10)

    def check(condition, description):
        assert condition, description
        print("PASS " + description, flush=True)

    def login():
        status, data = request("/api/auth/login", "POST", credentials)
        check(status == 200, "authenticate scratch administrator")
        return data["token"]

    initialized = subprocess.run(command + ["--initialize-admin"], cwd=root, env=env,
                                 input=json.dumps(credentials | {"oicqCode": "123456"}),
                                 capture_output=True, text=True, timeout=30, creationflags=flags)
    check(initialized.returncode == 0, "initialize scratch administrator")
    with host():
        token = login()
        status, first = request("/api/accounts/rewardsgm/characters", "POST", {"name": "RewardOne"}, token)
        check(status == 201, "create first offline character")
        status, second = request("/api/accounts/rewardsgm/characters", "POST", {"name": "RewardTwo"}, token)
        check(status == 201, "create second offline character on same account")
        first_id, second_id = first["id"], second["id"]
        player = {"userName": "rewardplayer", "password": secrets.token_urlsafe(18), "oicqCode": "123456"}
        check(request("/api/accounts", "POST", player)[0] == 201, "create scratch normal player")
        status, player_login = request("/api/auth/login", "POST", player)
        check(status == 200, "authenticate scratch normal player")
        player_token = player_login["token"]
        operation = str(uuid.uuid4())
        vp_path = f"/api/admin/characters/{first_id}/victory-points"
        status, grant = request(vp_path, "POST", {"operationId": operation, "amount": 123}, token)
        check(status == 200 and grant["balance"] == 123,
              f"GM grants123 VP through actual HTTP endpoint, HTTP {status}")
        status, repeated = request(vp_path, "POST", {"operationId": operation, "amount": 123}, token)
        check(status == 200 and repeated["balance"] == 123 and repeated["replayed"], "same VP operation cannot double grant")
        check(request(vp_path, "POST", {"operationId": operation, "amount": 124}, token)[0] == 409,
              "reused VP operation with different amount is rejected")
        check(request(vp_path, "POST", {"operationId": str(uuid.uuid4()), "amount": 0}, token)[0] == 400,
              "zero VP grant is rejected")
        check(request(vp_path, "POST", {"operationId": str(uuid.uuid4()), "amount": 1})[0] == 401,
              "anonymous VP grant is rejected")
        check(request(vp_path, "POST", {"operationId": str(uuid.uuid4()), "amount": 1}, player_token)[0] == 403,
              "normal player cannot grant VP")
        check(request(vp_path, "POST", {"operationId": "bad-guid", "amount": 1}, token)[0] == 400,
              "malformed operation ID is rejected")
        batch_id = str(uuid.uuid4())
        batch_payload = {"operationId": batch_id, "mail": {
            "sender": "GM", "text": "Broadcast acceptance", "gold": 17,
            "attachmentKind": 1, "itemId": 1000, "quantity": 2501}}
        status, preview = request("/api/admin/mail/broadcast/preview", "POST", batch_payload, token)
        check(status == 200 and preview["targetCount"] == 2 and preview["sentCount"] == 0,
              "broadcast preview freezes both existing offline characters without sending")
        check(request("/api/admin/mail/broadcast/preview", "POST", batch_payload, player_token)[0] == 403,
              "normal player cannot prepare broadcast")
        invalid = {"operationId": str(uuid.uuid4()), "mail": {"sender": "GM", "text": "unsupported \U0001f600"}}
        check(request("/api/admin/mail/broadcast/preview", "POST", invalid, token)[0] == 400,
              "client-incompatible mail text is rejected")
        check(request(f"/api/admin/mail/broadcast/{uuid.uuid4()}/send", "POST", {}, token)[0] == 404,
              "unknown batch cannot be sent")
        status, late = request("/api/accounts/rewardsgm/characters", "POST", {"name": "RewardLate"}, token)
        check(status == 201, "create character after frozen preview")
        send_path = f"/api/admin/mail/broadcast/{batch_id}/send"
        status, delivered = request(send_path, "POST", {}, token)
        check(status == 200 and delivered["sentCount"] == 2 and delivered["failedCount"] == 0,
              "confirmed broadcast delivers once to both frozen recipients")
        status, duplicate = request(send_path, "POST", {}, token)
        check(status == 200 and duplicate["replayed"], "broadcast confirmation retry replays receipt")
        check(request(send_path, "POST", {})[0] == 401, "anonymous broadcast is rejected")
        check(request(send_path, "POST", {}, player_token)[0] == 403, "normal player cannot confirm broadcast")
    with host():
        token = login()
        status, grant = request(vp_path, "POST", {"operationId": operation, "amount": 123}, token)
        check(status == 200 and grant["balance"] == 123 and grant["replayed"], "VP receipt survives server restart")
        status, batch = request(f"/api/admin/mail/broadcast/{batch_id}", token=token)
        check(status == 200 and batch["sentCount"] == 2, "broadcast receipt survives server restart")
    saved = json.loads(state_path.read_text(encoding="utf-8-sig"))
    characters = {c["Id"]: c for a in saved["Accounts"] for c in a["Characters"]}
    check(characters[first_id]["VictoryPoints"] == 123 and characters[first_id]["PvpPoints"] == 0,
          "grant persists spendable VP without changing rating")
    for recipient in (first_id, second_id):
        matching = [m for m in characters[recipient].get("Mailbox", []) if m["Text"] == "Broadcast acceptance"]
        check(len(matching) == 1 and matching[0]["Attachment"]["CountOrValue"] == 2501,
              "each recipient has exactly one complete attachment")
    check(not characters[late["id"]].get("Mailbox"), "new character does not receive frozen broadcast")
    if args.ui:
        from copy import deepcopy
        template = characters[first_id]["Mailbox"][0]
        characters[late["id"]]["Mailbox"] = [deepcopy(template) | {"MailId": n + 1} for n in range(255)]
        state_path.write_text(json.dumps(saved), encoding="utf-8")
        with host():
            from playwright.sync_api import sync_playwright, expect
            with sync_playwright() as pw:
                browser = pw.chromium.launch(headless=True)
                page = browser.new_page(viewport={"width": 1366, "height": 1000})
                errors = []
                page.on("pageerror", lambda error: errors.append(str(error)))
                page.goto(base + "/admin/index.html")
                page.locator("#loginUserName").fill(credentials["userName"])
                page.locator("#loginPassword").fill(credentials["password"])
                page.locator("#loginSubmit").click()
                expect(page.locator("#loginOverlay")).to_be_hidden()
                page.locator('[data-view="victory"]').click()
                page.locator('#victoryForm [name="characterId"]').select_option(first_id)
                page.locator('#victoryForm [name="amount"]').fill("11")
                page.locator('#victoryForm button[type="submit"]').click()
                expect(page.locator("#victoryBalance")).to_contain_text("134")
                check(True, "GM victory form grants and refreshes canonical balance")
                page.screenshot(path=str(root / "gm-victory.png"), full_page=True)
                page.locator('[data-view="mail"]').click()
                page.locator("#mailScope").select_option("all")
                page.locator('#mailForm [name="text"]').fill("UI broadcast acceptance")
                page.locator("#mailSubmit").click()
                expect(page.locator("#mailBatchPreviewText")).to_contain_text("3 个角色")
                page.locator("#mailBatchCancel").click()
                expect(page.locator("#mailBatchPreview")).to_be_hidden()
                check(True, "broadcast preview can be cancelled without sending")
                page.locator("#mailSubmit").click()
                expect(page.locator("#mailBatchPreview")).to_be_visible()
                page.locator('#mailForm [name="text"]').fill("UI broadcast edited")
                expect(page.locator("#mailBatchPreview")).to_be_hidden()
                page.locator("#mailSubmit").click()
                expect(page.locator("#mailBatchPreviewText")).to_contain_text("edited")
                page.locator("#mailBatchConfirm").click()
                expect(page.locator("#mailBatchSummary")).to_contain_text("发送成功 2，失败 1")
                expect(page.locator("#mailBatchFailures")).to_contain_text("RewardLate：邮箱已满")
                page.screenshot(path=str(root / "gm-broadcast-partial.png"), full_page=True)
                page.locator("#mailBatchRetry").click()
                expect(page.locator("#mailBatchPreviewText")).to_contain_text("1 个角色")
                page.locator("#mailBatchConfirm").click()
                expect(page.locator("#mailBatchSummary")).to_contain_text("目标 1 个角色，发送成功 0，失败 1")
                check(True, "GM broadcast displays partial failures and retries only the failed recipient")
                page.locator("#mailScope").select_option("single")
                page.locator('#mailForm [name="characterId"]').select_option(first_id)
                page.locator('#mailForm [name="text"]').fill("UI single mail")
                page.locator("#mailSubmit").click()
                expect(page.locator("#toast")).to_contain_text("邮件")
                check(not errors, "single-recipient form remains usable without browser script errors")
                lost = []
                def lose_committed_response(route):
                    response = route.fetch()
                    assert response.status == 200, "fault injection must occur after successful server commit"
                    lost.append(json.loads(route.request.post_data or "{}"))
                    route.abort("failed")
                recovery_failures = []
                page.locator('[data-view="victory"]').click()
                page.locator('#victoryForm [name="characterId"]').select_option(first_id)
                page.locator('#victoryForm [name="amount"]').fill("7")
                page.route("**/api/admin/characters/*/victory-points", lose_committed_response, times=1)
                page.locator('#victoryForm button[type="submit"]').click()
                expect(page.locator('#victoryForm button[type="submit"]')).to_be_enabled()
                page.reload()
                expect(page.locator("#loginOverlay")).to_be_hidden()
                page.locator('[data-view="victory"]').click()
                try:
                    expect(page.locator('#victoryForm [name="amount"]')).to_have_value("7", timeout=3000)
                    expect(page.locator('#victoryOperation')).to_contain_text(lost[0]["operationId"])
                    page.locator('#victoryForm button[type="submit"]').click()
                    expect(page.locator("#victoryBalance")).to_contain_text("141")
                    check(True, "lost committed VP response survives reload and original-ID retry grants once")
                except AssertionError as error:
                    recovery_failures.append("VP recovery: " + str(error))
                page.locator('[data-view="mail"]').click()
                if page.locator('#mailBatchNew').is_visible():
                    page.locator('#mailBatchNew').click()
                page.locator('#mailScope').select_option('all')
                page.locator('#mailForm [name="text"]').fill('UI lost response')
                page.locator('#mailSubmit').click()
                expect(page.locator('#mailBatchPreview')).to_be_visible()
                sent_ids = []
                def lose_mail_response(route):
                    sent_ids.append(route.request.url.split('/')[-2])
                    lose_committed_response(route)
                page.route('**/api/admin/mail/broadcast/*/send', lose_mail_response, times=1)
                page.locator('#mailBatchConfirm').click()
                expect(page.locator('#mailBatchConfirm')).to_be_enabled()
                page.reload()
                expect(page.locator('#loginOverlay')).to_be_hidden()
                page.locator('[data-view="mail"]').click()
                try:
                    expect(page.locator('#mailBatchRecover')).to_be_visible(timeout=3000)
                    page.locator('#mailBatchRecover').click()
                    expect(page.locator('#mailBatchId')).to_have_text(sent_ids[0])
                    expect(page.locator('#mailBatchSummary')).to_contain_text('发送成功 2，失败 1')
                    check(True, 'lost committed broadcast response survives reload and retrieves original receipt')
                except AssertionError as error:
                    recovery_failures.append('Mail recovery: ' + str(error))
                current = json.loads(state_path.read_text(encoding='utf-8-sig'))
                chars = {c['Id']: c for a in current['Accounts'] for c in a['Characters']}
                check(chars[first_id]['VictoryPoints'] == 141, 'uncertain VP retry does not double grant')
                check(all(sum(m['Text']=='UI lost response' for m in chars[i]['Mailbox'])==1 for i in (first_id,second_id)),
                      'uncertain mail retry does not duplicate committed deliveries')
                assert not recovery_failures, '\n'.join(recovery_failures)
                page.locator('[data-view="victory"]').click()
                page.locator('#victoryForm [name="characterId"]').select_option(first_id)
                page.locator('#victoryForm [name="amount"]').fill('4294967295')
                page.locator('#victoryForm button[type="submit"]').click()
                expect(page.locator('#toast')).to_contain_text('超过上限')
                expect(page.locator('#victoryOperation')).to_have_text('')
                page.locator('#victoryForm [name="amount"]').fill('1')
                page.locator('#victoryForm button[type="submit"]').click()
                expect(page.locator('#victoryBalance')).to_contain_text('142')
                check(True, 'explicit rejected overflow permits a corrected new grant')
                browser.close()
    print("ARTIFACTS " + str(root), flush=True)


if __name__ == "__main__":
    main()
