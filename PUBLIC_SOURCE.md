# Public source snapshot

The current public tree mirrors Gitea source commit `209a1daec7d08ac781bc2f3c8a7af60b47c2ac44`. This repository began with a sanitized snapshot; private development history is intentionally not imported.

Application, tests, build tools, deployment files and protocol data retain their source bytes and modes. A local VM database password in ten historical investigation files is replaced with `REDACTED_VM_DB_PASSWORD`; those scripts require your own VM configuration.

Release manifests identify the actual Gitea build commit. GitHub commit IDs identify the corresponding sanitized source copy. Synchronization appends sanitized trees only to this public history; never merge or push private development history here.

The v0.1.8 binaries remain pinned to source `5bea1d8bb55d7bc00ec9ece2e45ef5a6fbc5ef5e` (public release snapshot `279f9dc7c152346e1563d0adfe11bfa91d505d93`). The subsequent source commit only updates the container export smoke test to check both diagnostic helpers.

The v0.1.11 binaries, manifests, fixed-entry repair packages and release assets remain pinned to runtime Gitea source commit `0e72cff78871686eb80a9d67d115d074e71fef8f` (sanitized runtime snapshot `75a8cfcb44a2f49518549bdaa80ff61f933b66dd`). Source commit `209a1daec7d08ac781bc2f3c8a7af60b47c2ac44` records final verification documentation only; it does not change runtime identity.
