# ACT1 Town Navigation Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans task by task. Each task must retain its RED/GREEN evidence.

**Goal:** 原生 城镇地图同区点击移动及 debug 目的地接口，以结构化状态完成操作和验收。

**Architecture:** Read-only state v2; separate navigation command processor; single main-thread native destination path shared by 城镇地图 and file API.

**Tech Stack:** Existing x86 C++17 Win32 debug.dll, Python standard-library consumer and tests.

**Spec:** docs/superpowers/specs/2026-10-29-town-navigation-design.md

## Global constraints

Original EXE/PVF/saves unchanged; no external memory write/input/packet injection. Native harness for UI input. Existing frozen b777 artifacts unchanged. Unknown fields null, no invented A*/cross-area capability. Worker-only IO, bounded main-thread reads, fail closed.

## Review focus

Stale Town/actor binding, native missing-data success, icon/child click interception, repeated/expired requests, transition during dispatch must cause safe rejection or interruption.

## Tasks

- [x] State v2: client_state.h/.cpp/_json.cpp and native tests. RED Town me/UID/binding/context change and UI countdown readiness; implement bounded Town capture and UI visibility; build focused and full tests.
- [x] Navigation: new client_navigation.h/.cpp and tests. RED identity/freshness/replay/point/context checks, projection round trip and marker rejection; implement pure policy and separate runtime native dispatch. Publish command wire contract before consumer work.
- [x] Integration: Debug project/tests project/debug_overlay.cpp invoke worker pump and frame processing, config default disabled. Ensure original hook bytes and supported image gate; preserve shutdown rollback.
- [x] Consumer: tools/debug/client_state.py v1/v2, new client_navigation.py CLI and tests, README examples. Reject malformed/null/truncated/stale snapshots, wait for observed result rather than input ACK.
- [x] Native verification: pin next build and deployment preimages; two clients use data after bootstrap, observe destinations/motion/arrival plus 城镇地图 click UI regressions and expired identities; retain original pictures. Archive failed cases and restore temporary config/mailbox state; independent review and local commit.

验收限定：同区双端目的地与N地图点击、控件保护和非法请求拒绝已通过；筛选项效果全集／跨区／A*／同组副本流程仍未列为完成。最终原生5449，代码02de7053，详见 knowledge/decisions/2026-10-29-debug-town-navigation-verification.md。
