"""Read and operate the GM client-diagnostic reports from a terminal.

Auth never goes into the repository: pass a token or credentials through the
environment.

  DFLEGACY_GM_URL       default http://127.0.0.1:48093
  DFLEGACY_GM_TOKEN     an already minted Admin bearer token (preferred)
  DFLEGACY_GM_USER      admin name; paired with DFLEGACY_GM_PASSWORD
  DFLEGACY_GM_PASSWORD  admin password

Examples:
  python -B tools/admin/diagnostics.py groups --status new
  python -B tools/admin/diagnostics.py export --out diag.zip --kind crash
  python -B tools/admin/diagnostics.py status <group-id> resolved
  python -B tools/admin/diagnostics.py delete <receipt-id>

Only the standard library is used. The export command works against the
current GM API (no server upgrade needed) by fetching every page and every
report body itself and writing the ZIP locally.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

DEFAULT_URL = "http://127.0.0.1:48093"
FILTERS = ("kind", "status", "version", "system", "gpu", "from", "to")


class GmError(Exception):
    pass


class Gm:
    def __init__(self, base, token=None):
        self.base = base.rstrip("/")
        self.token = token

    def login(self, user, password):
        body = json.dumps({"userName": user, "password": password}).encode()
        request = urllib.request.Request(
            self.base + "/api/auth/login", data=body, method="POST",
            headers={"Content-Type": "application/json"})
        payload = self._open(request)
        self.token = payload["token"]
        return self.token

    def _open(self, request, raw=False):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                data = response.read()
                return data if raw else json.loads(data or b"{}")
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:400]
            raise GmError(f"HTTP {error.code} {request.full_url}: {detail}") from None
        except urllib.error.URLError as error:
            raise GmError(f"cannot reach {self.base}: {error.reason}") from None

    def call(self, method, path, data=None, raw=False):
        headers = {}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        body = None
        if data is not None:
            body = json.dumps(data).encode()
            headers["Content-Type"] = "application/json"
        return self._open(urllib.request.Request(
            self.base + path, data=body, method=method, headers=headers), raw=raw)

    def groups(self, filters, page=1, page_size=100):
        query = {k: v for k, v in filters.items() if v}
        query.update(page=str(page), pageSize=str(page_size))
        return self.call("GET", "/api/admin/diagnostics/groups?" + urllib.parse.urlencode(query))

    def all_groups(self, filters):
        page = self.groups(filters, 1)
        items = list(page["items"])
        for number in range(2, (page["total"] + page["pageSize"] - 1) // page["pageSize"] + 1):
            items.extend(self.groups(filters, number)["items"])
        return items

    def download(self, receipt):
        return self.call("GET", "/api/admin/diagnostics/reports/" + receipt + "/download", raw=True)

    def set_status(self, group, status):
        return self.call("PATCH", "/api/admin/diagnostics/groups/" + group, {"status": status})

    def delete(self, receipt):
        return self.call("DELETE", "/api/admin/diagnostics/reports/" + receipt)


def connect(args):
    base = args.url or os.environ.get("DFLEGACY_GM_URL") or DEFAULT_URL
    gm = Gm(base, os.environ.get("DFLEGACY_GM_TOKEN"))
    if gm.token:
        return gm
    user = os.environ.get("DFLEGACY_GM_USER")
    password = os.environ.get("DFLEGACY_GM_PASSWORD")
    if not user or not password:
        raise GmError("set DFLEGACY_GM_TOKEN, or DFLEGACY_GM_USER and DFLEGACY_GM_PASSWORD")
    gm.login(user, password)
    return gm


def filters(args):
    return {name: getattr(args, name) for name in FILTERS}


def print_groups(groups, as_json):
    if as_json:
        print(json.dumps(groups, ensure_ascii=False, indent=2))
        return
    for group in groups:
        print("\t".join(str(value) for value in (
            group["kind"], group["module"], group["clientVersion"], group["status"],
            group["occurrences"], group["reportCount"], group["lastSeen"],
            ",".join(group["reportIds"]), group["id"])))


def command_export(gm, args):
    groups = gm.all_groups(filters(args))
    output = Path(args.out)
    index = {"format": "DFLegacy-client-diagnostics-export-1", "filters": filters(args), "groups": []}
    written = 0
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, strict_timestamps=False) as archive:
        for group in groups:
            entry = {key: group[key] for key in (
                "id", "kind", "clientVersion", "module", "status", "occurrences",
                "reportCount", "lastSeen", "evidence")}
            entry["reports"] = []
            for receipt in group["reportIds"]:
                body = gm.download(receipt)
                name = f"reports/{group['id'][:12]}/{receipt}.json"
                archive.writestr(name, body)
                entry["reports"].append({"receiptId": receipt, "file": name})
                written += 1
            index["groups"].append(entry)
        archive.writestr("index.json", json.dumps(index, ensure_ascii=False, indent=2).encode())
    print(json.dumps({"zip": str(output), "groups": len(groups), "reports": written},
                     ensure_ascii=False))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--url")
    sub = parser.add_subparsers(dest="command", required=True)

    groups = sub.add_parser("groups")
    for name in FILTERS:
        groups.add_argument("--" + name)
    groups.add_argument("--page", type=int, default=1)
    groups.add_argument("--page-size", type=int, default=100)
    groups.add_argument("--json", action="store_true")
    groups.set_defaults(handler=lambda gm, args: print_groups(
        gm.all_groups(filters(args)), args.json))

    report = sub.add_parser("report")
    report.add_argument("receipt")
    report.add_argument("--out")
    report.set_defaults(handler=lambda gm, args: report_body(gm, args))

    export = sub.add_parser("export")
    export.add_argument("--out", required=True)
    for name in FILTERS:
        export.add_argument("--" + name)
    export.set_defaults(handler=lambda gm, args: command_export(gm, args))

    status = sub.add_parser("status")
    status.add_argument("group")
    status.add_argument("value", choices=("new", "investigating", "resolved"))
    status.set_defaults(handler=lambda gm, args: (
        gm.set_status(args.group, args.value), print("status updated"))[1])

    delete = sub.add_parser("delete")
    delete.add_argument("receipt")
    delete.set_defaults(handler=lambda gm, args: (gm.delete(args.receipt), print("deleted"))[1])

    args = parser.parse_args(argv)
    try:
        gm = connect(args)
        args.handler(gm, args)
        return 0
    except GmError as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


def report_body(gm, args):
    body = gm.download(args.receipt)
    if args.out:
        Path(args.out).write_bytes(body)
        print("wrote " + args.out)
    else:
        sys.stdout.write(body.decode("utf-8", "replace") + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
