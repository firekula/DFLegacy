# V4 Gitea test migration plan

> Execute in the existing isolated codex/client-update-v4-diagnostics worktree. Follow executing-plans and test-driven-development; independent provider/client/publisher work may use dispatching-parallel-agents.

**Goal:** publish and verify V4 test updates through the existing Gitea and remove the local Gitee remote association.
**Spec:** docs/superpowers/specs/2026-11-08-v4-gitea-test-design.md
**Architecture:** retain V4 schema and five-field ReleaseSource; add fixed Gitea origin http://firekula.site:3000 only for provider gitea, channel gitea-test. No general HTTP source option and no schema migration.

## Tasks

- [x] Inspect exact repository and remove matching Gitee remote from isolated public clone. Preserve hosted Gitee repository and existing origin.
- [x] Core: failing Gitea source/API/URL-boundary tests, adapter, full core suite. Files src/DFLegacy.UpdateCore/ReleaseSource.cs and ReleaseProvider.cs; tests/DFLegacy.UpdateCore.Tests/ProviderTests.cs.
- [x] Publisher/GUI: failing Gitea API contract and grouped publication tests; fixed-origin draft publication, manifests last, exact-source anonymous validation; add Gitea choice and preserving opt-in script. Files tools/release/update_v4.py, related tests, src/DFLegacy.ClientPackager. Do not edit Core/Launcher.
- [x] Launcher: explicit Gitea selection overriding legacy hosted source without GitHub fallback; repair/source regression tests. Files src/DFLegacy.Launcher/Updates and tests. Do not edit Core/Publisher.
- [x] Integrate, test, independent review, build self-contained x86/x64 with new source commit. Generate Gitea .1/.2 test-only bundles and starter/repair packages on C: (D: space low).
- [x] Obtain existing Gitea authentication through its credential helper in memory, publish isolated .1 draft and validate anonymously, test actual engine A; publish .2 and test differential update B. Never echo credentials, delete or overwrite existing releases, or change GitHub/online servers.
- [x] Update documentation and local report with actual tests and links; preserve ACT1_LOCAL_ACCEPTANCE.md and other worktrees.

## Shared contract

`ReleaseSource.GiteaOrigin` constant http://firekula.site:3000; provider gitea, repository firekula/DFLegacy, channel gitea-test. Constructor signature stays unchanged. Python GITEA_ORIGIN matches. Source API origin is fixed, not a field in manifests. CLI uses dedicated DFLEGACY_GITEA_RELEASE_TOKEN only; existing stored Git credentials may be consumed by an external orchestrator without logging. No Gitee token reuse for Gitea.
