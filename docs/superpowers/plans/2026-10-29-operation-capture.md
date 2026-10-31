# Operation capture default Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to execute this bounded fix.

**Goal:** Ordinary agent operations do not implicitly trigger native screenshots; key acceptance remains explicit.

**Architecture:** Reusable Python owned-run wrapper around each run's existing IJL file driver, preserving identity and evidence guards. Only capture defaults change. Frame observations remain separate diagnostic tools.

**Tech Stack:** Python argparse/subprocess/ctypes, existing Win32 IJL file input, pytest.

**Spec:** docs/superpowers/specs/2026-10-29-operation-capture.md

## Constraints and review focus
- Never weaken PID/path/start or journal identity, permit ambiguous clients, overwrite evidence, or add OS input/business packets.
- Screenshot opt-in must work while the default and legacy --no-shot generate no implicit capture.
- No DLL, original EXE/PVF or original save edits.
- Compare timing only with continuously successful reads; distinguish observer gaps, timer resets and GPU presentation.

### Task1: Root cause and source fix
- [x] Preserve original data and screenshot phases; locate implicit capture default.
- [x] Extract tools/acceptance/native_control.py; add tests/test_native_control.py. RED3fails/5passes under legacy default.
- [x] Change defaults to opt-in; GREEN176tests/173subtests. Invalid PYTHONPATH collection attempt retained separately.

### Task2: Native validation
- [x] Current private run uses canonical wrapper;40s continuous/short default operations produce no new JPG; maximum qualified timer pauses7.42/7.56ms.
- [x] Explicit capture produces byte-identical native JPG. DLL remains C3ED; no Native rebuild required.
- [x] Full Python471passed/221subtests with6known baseline failures; source and evidence review requested.

### Task3: Evidence and closeout
- [x] Publish timing definitions/corrections, original key image and review; preserve scope of unresolved stutter.
- [x] Stop exact owned run, restore configs/interfaces and protected hashes; audit and commit.

Source e6dac3f. Qualified observed pause reduction is limited to implicit captures; all other stutter remains unclosed. See knowledge/decisions/2026-10-29-operation-stutter-verification.md for original evidence, corrections and baseline test failures.
