# Independent panel ports implementation plan

User approved ports: Manager18080, GM18081, Player18082, account API8081.
Use two additional listeners in the existing server, sharing store/auth. Root requests serve the corresponding page. Keep old API routes for compatibility, restrict player listener from GM and manager-control access. Preserve persistent data and existing configuration fields.

- [ ] Server options, listener validation, root mapping and access policy; RED/GREEN focused and actual HTTP tests.
- [x] Manager typed configuration, collision checks, startup probes and separate root links.
- [x] Compose mappings/environment sample and deployment instructions.
- [ ] Run browser, managed HTTP, native and release gates; review.
- [ ] Commit and publish Gitea, then GitHub client update assets and public GHCR image; deliver exact Compose commands.
