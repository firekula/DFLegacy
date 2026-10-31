"""Opt-in real Windows artifact regression (no game/server or credentials).

Run after native build + single-file publish. An omitted RC resource, mismatched
embedded helper, unbundled managed notice, or CLI that reads player configuration
must fail. ZIP checks exercise actual release parsers and exact flavor pairing.
"""
import argparse
import ctypes
from ctypes import wintypes
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import bundle
import release

REPO = Path(__file__).resolve().parents[3]


def resource(path, name):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.LoadLibraryExW.argtypes = [wintypes.LPCWSTR, wintypes.HANDLE, wintypes.DWORD]
    kernel.LoadLibraryExW.restype = wintypes.HMODULE
    kernel.FindResourceW.argtypes = [wintypes.HMODULE, wintypes.LPCWSTR, ctypes.c_void_p]
    kernel.FindResourceW.restype = wintypes.HANDLE
    kernel.SizeofResource.argtypes = [wintypes.HMODULE, wintypes.HANDLE]
    kernel.SizeofResource.restype = wintypes.DWORD
    kernel.LoadResource.argtypes = [wintypes.HMODULE, wintypes.HANDLE]
    kernel.LoadResource.restype = wintypes.HANDLE
    kernel.LockResource.argtypes = [wintypes.HANDLE]
    kernel.LockResource.restype = ctypes.c_void_p
    kernel.FreeLibrary.argtypes = [wintypes.HMODULE]
    # LOAD_LIBRARY_AS_DATAFILE: inspect x86 resources without executing DLL code.
    module = kernel.LoadLibraryExW(str(path.resolve()), None, 2)
    assert module, f"cannot map {path}"
    try:
        found = kernel.FindResourceW(module, name, 10)  # RT_RCDATA
        assert found, f"{path.name}: missing {name}"
        size = kernel.SizeofResource(module, found)
        pointer = kernel.LockResource(kernel.LoadResource(module, found))
        assert size and pointer, f"{path.name}: empty {name}"
        return ctypes.string_at(pointer, size)
    finally:
        kernel.FreeLibrary(module)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native", required=True, type=Path)
    parser.add_argument("--launcher", type=Path)
    parser.add_argument("--packages", type=Path)
    args = parser.parse_args()
    notices = {"DETOURS_LICENSE": (REPO/"third_party/detours/LICENSE").read_bytes(),
               "DETOURS_PROVENANCE": (REPO/"third_party/detours/PROVENANCE.md").read_bytes()}
    failures = []

    def check(label, action):
        try:
            action()
            print("PASS", label)
        except (AssertionError, ValueError, OSError, subprocess.SubprocessError) as error:
            failures.append(label)
            print("FAIL", label, str(error))

    def equal(actual, expected):
        assert actual == expected, "artifact bytes differ from vendored source"

    for leaf in ("DFLegacy.Client.dll", "DFLegacy.Bootstrap32.exe"):
        for name, expected in notices.items():
            check(leaf + ":" + name, lambda leaf=leaf, name=name, expected=expected:
                  equal(resource(args.native/leaf, name), expected))
    if args.launcher:
        def managed():
            assembly = bundle.read_bundle(args.launcher, "win-x64").read("DFLegacy.Launcher.dll")
            for content in notices.values():
                assert content in assembly, "managed assembly omits full source notice/provenance"
            assert (args.native/"DFLegacy.Bootstrap32.exe").read_bytes() in assembly, "embedded helper differs from matching native build"
        check("actual singlefile managed notices and exact helper", managed)

        def cli():
            with tempfile.TemporaryDirectory(prefix="dfl-notice-") as temporary:
                root = Path(temporary)
                exe = root/"DFLegacy.Launcher.exe"
                shutil.copyfile(args.launcher, exe)
                (root/"launcher.json").write_text("deliberately invalid player config")
                before = {p.name: p.read_bytes() for p in root.iterdir()}
                result = subprocess.run([str(exe), "--third-party-notices"], cwd=root,
                                        capture_output=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
                assert result.returncode == 0, f"notice exit={result.returncode}, stderr={result.stderr!r}"
                expected = notices["DETOURS_PROVENANCE"].decode() + "\n" + notices["DETOURS_LICENSE"].decode()
                assert result.stdout.decode("utf-8").replace("\r\n", "\n") == expected.replace("\r\n", "\n"), "CLI must print full exact notice/provenance"
                assert not result.stderr, result.stderr
                assert {p.name: p.read_bytes() for p in root.iterdir()} == before, "notice CLI wrote player files/logs"
        check("singlefile read-only notice CLI with invalid configuration", cli)
    if args.packages:
        def packages():
            names = {release.UPDATE_ASSET, release.UPDATE_TEST_ASSET, release.TOOLS_ASSET, release.TOOLS_TEST_ASSET}
            assert {p.name for p in args.packages.glob("DFLegacy-client-*.zip")} == names
            shared = {}
            for name in sorted(names):
                path = args.packages/name
                manifest = release.verify_client_package(path, asset_name=name)
                with zipfile.ZipFile(path) as archive:
                    for leaf in ("DFLegacy.Client.dll", "DFLegacy.Launcher.exe", "debug.dll"):
                        if leaf not in archive.namelist():
                            continue
                        body = archive.read(leaf)
                        assert body == shared.setdefault(leaf, body), f"flavor pairing differs: {leaf}"
                        expected = args.launcher if leaf == "DFLegacy.Launcher.exe" else args.native/leaf
                        assert body == expected.read_bytes(), f"archive uses stale {leaf}"
                    assert manifest["flavor"] == ("test" if "-test-" in name else "normal")
            print("verified four exact inventories, shared binaries, flavor-only debug.dll")
        check("four real schema 2 public packages", packages)
    print(f"artifact notice checks: {len(failures)} failures")
    return bool(failures)


if __name__ == "__main__":
    sys.exit(main())
