# Launcher Classic UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Implement the original-asset login, server selection and settings interface explicitly approved by the user.

**Architecture:** Keep WPF, existing account/launch flows and display authority. Shared pixel text and bitmap button/frame controls render the approved assets; MainWindow navigates login/server pages; ConfigWindow retains all saved-target and byte-preservation handlers.

**Tech Stack:** .NET10 WPF, Win32 GDI for monochrome text only, embedded PNG; no new runtime package.

**Spec:** docs/superpowers/specs/2026-10-31-launcher-classic-ui-design.md. User approved revision-2 login/settings images and prior server-selection preview.

## Global Constraints

- Approved original-asset geometry and fixed DNF skin; login has no server list.
- Existing independent saves, four-target authority, three-key byte preservation, restart semantics and historical diagnostics remain intact.
- One configured local-server choice reads saved host/port; no connection/credential writes from selection.
- Real Button keyboard/automation support; no stored raster of password contents.
- Main original3 dirty tracked files and163 status records; original426 resources and account config preserved.

## Review Focus

- Global HandyControl styles and popup visual trees must not change the fixed skin or produce unreadable controls.
- Long paths/errors, high DPI and smaller work areas must wrap/scroll while keeping saves/close reachable.
- Server-page keyboard actions must never start the hidden login page; focused buttons must receive their own Enter action.
- GDI failures, Unicode, bounded raster sizes/cache and resource lifetime must not hang or leak the UI.
- External saved config changes and malformed/unreadable stores must never cause server selection/display save to overwrite another installation or credentials.

### Task 1: Native skin resources and shared controls

**Files:** Add Controls/PixelRasterizer.cs, PixelText.cs, SkinButton.cs, SkinFrame.cs, embedded classic PNG assets and provenance. Add tests/ClassicUiTests.cs and hook tests/Program.cs; replace the rejected SettingsStyles.xaml with scoped classic styles.

**Interfaces:** PixelRasterizer.Render(text,size,colour,outline) returns frozen raster and advance; PixelText preserves semantic Text, wrapping, alignment and AutomationPeer; SkinButton supplies normal/hover/pressed sources and focused Enter; SkinFrame draws original borders with fixed cap sizes.

- [x] Observe RED for missing raster/control APIs using real GDI/WPF, Unicode, outline pixels, 17px placeholder centering and wrapped long text; ensure GDI/caches remain bounded.
- [x] Embed approved single-image panel and derived assets with source hashes, preserving original files and avoiding build-time F-drive dependency.
- [x] Implement shared controls and scoped templates without global theme leakage; run complete Launcher suite and commit.

### Task 2: Approved screens and server selection

**Files:** Rewrite MainWindow.xaml presentation, add Views/ConfigWindow classic layout, LocalServerSelection.cs and relevant handlers; retain existing account/launch/backend configuration logic.

**Interfaces:** LocalServerSelection reads actual LauncherAccountStore and confirms current host/port without writes; MainWindow routes login/server key actions and actual existing account handlers; ConfigWindow retains named authority-protected controls.

- [x] Observe RED for real-store server selection: custom/IPv6 endpoint, account retention, no writes or mtime changes, externally changed endpoint and malformed/locked config.
- [x] Implement six unique top entries, official-style banner/guides, centered placeholders, one server choice, safe page keys, classic settings and folded details.
- [x] Publish a fresh single-file test build; inspect actual login, server page, settings, dropdowns, focus, long errors and smaller bounds through Sky. Keep user authentication/security handoffs.
- [x] Run complete Launcher/managed regression gates and commit; one fresh UI-scope review, then one material-finding fix pass with RED/GREEN proof and green suites.

### Task 3: Final publication acceptance

**Files:** Update original acceptance report and both plans; protected-main and existing Gitea/private verification tooling.

**Interfaces:** Corrected single-file launcher plus previously verified native scaling; original Task4 publication/download/install/pause gates.

- [x] Preserve exact main dirty bytes/status, fast-forward final UI commit, verify merged Launcher gate and push a complete CI run.
- [x] Verify all final Release assets/manifests and dual-arch private OCI, actual packaged game and matching binaries; install only validated client files with backups and preserve original426 resources/configuration.
- [x] Restore Runner pause and owned process baseline; complete original Task4, preserve ledgers/rulings, record final acceptance and push docs.
