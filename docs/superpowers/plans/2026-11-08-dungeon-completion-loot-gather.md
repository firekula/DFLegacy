# Dungeon completion and solo loot plan

Spec: `docs/superpowers/specs/2026-11-08-dungeon-completion-loot-gather-design.md`.

1. Server worker: inspect completion/room state; RED tests then shared guard; exact solo chat authorization and marker; focused smoke tests. Own Server/Protocol/smoke test files, coordinate shared Program registration.
2. Native worker: verify actual chat ABI and ground coordinate setter; RED policy/runtime tests then production marker hook/one-shot frame-safe gather; no debug mailbox dependency; native tests and build on C:. Own Ijl15 source/tests/CMake only, no managed/bootstrap entry changes.
3. Root: local isolated server/saves/PVF fixtures, reproduction and any evidence-driven idle-crash fix; integrate server/native work; independent review, regression and actual local UI acceptance. Do not silently equate room gate fix with a proven crash fix.
4. Root: build Gitea test .4 with new native DLL and updated managed build, real .3 -> .4 interrupted transaction recovery in x86/x64, then verify changed game features from updated package against local server. Preserve original client, production and GitHub. Report images, hashes, tests and limits.

Parallel workers follow fixed marker contract and disjoint ownership; root controls process lifetimes, publishing and all UI automation.
