# Public source snapshot

This repository starts from a sanitized snapshot of DFLegacy source commit `4a0e12555c1786176c4a24fec3096a9db2a992cd` maintained on Gitea. Earlier development history is intentionally not imported.

The application, tests, build tools, deployment files, and protocol data are unchanged. A local VM database password in ten historical investigation files has been replaced with `REDACTED_VM_DB_PASSWORD`. Those investigation scripts require your own VM configuration before use.

The Gitea source commit recorded in release manifests identifies the build source; GitHub snapshot commit IDs identify this public source copy. Future source synchronization must export sanitized file trees as new public commits; do not merge or push private development history into this repository.
