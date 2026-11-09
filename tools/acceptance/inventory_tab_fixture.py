"""Run bounded native backpack-tab fixtures; never start or inspect a game.

The native executable owns its synthetic image, model and window state. It
executes compatible x86 NOTI13/NOTI14 insertion calls, amount-increase calls,
and the original automatic selector with deterministic slot lookup. It applies
the production installer with the real in-process memory writer. This is
fixture evidence, not controller-owned live Sort/reopen acceptance.
"""

import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-fixture", type=Path, required=True)
    args = parser.parse_args()
    executable = args.native_fixture.resolve(strict=True)
    results = []
    for scenario, arguments in (
        ("original_control", ["--original-control"]),
        ("preservation_and_signature_safety", []),
        ("partial_updates", ["--partial-updates-only"]),
    ):
        result = subprocess.run(
            [str(executable), *arguments], capture_output=True, text=True,
            timeout=30, check=False,
        )
        results.append({"scenario": scenario, "exitCode": result.returncode,
                        "stdout": result.stdout.strip(), "stderr": result.stderr.strip()})
    print(json.dumps({"fixtureOnly": True, "liveGameplayAcceptance": False,
                      "results": results}, indent=2))
    return int(any(result["exitCode"] != 0 for result in results))


if __name__ == "__main__":
    raise SystemExit(main())
