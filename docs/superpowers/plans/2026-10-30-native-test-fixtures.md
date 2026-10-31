# Native Test Fixtures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Independent motion-fixture work is delegated under dispatching-parallel-agents; integration remains with root.

**Goal:** Run the eleven previously skipped tests from verified dedicated fixtures, then push the tested main branch.

**Architecture:** Existing native test producers emit four exact JSON fixtures; a verified default resolver supplies them to Python. A separate bounded original-code fixture preserves the four Unicorn motion tests. Windows CI refreshes native outputs after building; portable tests use checked-in fixtures.

**Tech Stack:** C++17/MSVC/CMake, Python 3.12, pefile/Unicorn/Capstone, pytest.

**Spec:** `docs/superpowers/specs/2026-10-30-native-test-fixtures-design.md`.

**Execution:** Local implementation and verification complete; actual results and the bounded additional portability cases are recorded in [validation](../reports/2026-10-30-native-test-fixtures-validation.md). Main integration/push follows the user's explicit instruction.

## Constraints and review focus

- Keep existing assertions and explicit environment overrides; missing/tampered fixtures fail.
- No full game executable, private PVF/save/credentials or live-client modifications.
- Preserve native export bytes and distinguish deterministic model clocks from game observations.
- Original instruction accesses must remain inside verified fragments; original and repaired counterfactual cases stay intact.
- Output creation never replaces an existing fixture bundle. Source/binary provenance must be explicit.
- Main research changes remain unstaged; no push until the complete suite passes.

### Task 1: Native JSON fixtures and default resolver

- [ ] Add failing tests requiring the four checked fixtures, integrity rejection and explicit overrides.
- [ ] Add `tools/acceptance/native_fixtures.py`, checked `tests/fixtures/native-debug/*` and `tools/release/native_fixtures.py generate/verify` using actual native producers.
- [ ] Replace seven skip branches in the existing Python tests with verified fixture resolution.
- [ ] Run the native generator and the seven existing assertions plus focused resolver tests.
- [ ] Connect generation/validation to Windows CI and document regeneration.

### Task 2: Bounded original motion fixture

- [ ] Prove missing/integrity failures with focused tests.
- [ ] Add the sparse original-code fixture, producer provenance, regeneration CLI and strict access guards.
- [ ] Keep the optional verified original-executable path and all four original-vs-repaired assertions.
- [ ] Run all four tests without external client/patch environment variables; verify tampering is rejected.

### Task 3: Whole-tree verification and integration

- [ ] Run full locked Python tests with zero skips, native CTest and relevant managed/launcher checks.
- [ ] Run portable Linux tests and validate fresh Windows-produced fixtures.
- [ ] Review the branch, fix important findings, record actual boundaries and commit.
- [ ] Fast-forward local main with original working-tree hashes preserved; fetch/reconcile origin/main safely; push and verify the remote SHA.
