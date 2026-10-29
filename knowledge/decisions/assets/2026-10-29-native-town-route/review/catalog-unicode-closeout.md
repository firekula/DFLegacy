# Native Catalog Unicode P2 closeout

Reviewed followup `3b7e9b973197888487a90626e47043bf7cac0a2e` in `D:/codex_worktree/debug-town-navigation/DFLegacy`. This closes the single P2 in `../independent-review.md` against441c152. The original report, raw fixtures, failing probe and initial build/test evidence remain unchanged.

The path-key normalization now performs strict UTF8→UTF16, invariant LCMapStringEx lowercase, then strict UTF16→UTF8, before path validation and namespace lookup/duplicate checks. ASCII paths retain the fast path; structural tag matching retains its original ASCII helper. Explicit input lengths and checked API returns avoid terminator ambiguity. Directory strict CP949 decoding, script replacement policy, parser bounds, geometry and header interfaces are unchanged. No new P1/P2 findings in this delta.

Independent verification:

- Worktree HEAD equals the reviewed SHA. All3source hashes match `frozen-catalog-unicode.json`; `input-provenance.json` records the checks.
- Rebuilt the unchanged private project into this review/unicode/out and obj namespace with Release Win32 `/W4 /WX`. Success, no compiler warnings/errors; `independent-build.log`.
- Independent executable:2963 original-PVF corpus cases /3037 checks /0 failures; `independent-tests.log`.
- Reused the ORIGINAL `../cp949-case-fixture.pvf` without regeneration: output `okay=1 error= areas=1`, rc0.
- Reused the ORIGINAL `../cp949-duplicate-fixture.pvf` without regeneration: output `okay=0 error=invalid_pvf areas=0`, expected rc1. This negative exit is success of the rejection check, not a failed suite.
- `original-repro-retests.json` binds those unchanged fixture hashes, new executable hash and exact outputs.

P2 CLOSED. Source is ready for root integration within the reviewed Catalog scope. This is not live Native route/controller acceptance or a claim of every Town map being exercised. No product sources, Git state, configuration or live process were modified by this review.
