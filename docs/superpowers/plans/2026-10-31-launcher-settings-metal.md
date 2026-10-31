# Launcher Settings Metal Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the rejected initial settings page using the user's explicit DNF dark-metal and gold visual choice.

**Architecture:** Keep the existing WPF settings controls, names, handlers and configuration authority unchanged. Scope local resource styles and custom window chrome to ConfigWindow; reorganize content into clear navigation, primary fields, folded details and one primary save action per page.

**Tech Stack:** Existing .NET10 WPF and embedded vector drawing; no dependency or rendering change.

**Spec:** docs/superpowers/specs/2026-10-31-launcher-resolution-sharp-scaling-design.md, user visual-feedback addendum.

## Global Constraints

- Dark metal and warm gold, readable Microsoft YaHei UI Chinese text, unified window and controls.
- Existing independent saves, stale-target guards, three-key byte preservation, restart semantics and historical diagnostics remain intact.
- No tests mirroring XAML colors; compile and actual window inspection verify the reversible visual change. Existing complete Launcher behavior tests remain the regression gate.
- Continue the original publication/install task; final delivery must contain the corrected interface.

## Review Focus

- Global HandyControl light/dark resources must not turn popup text or title chrome unreadable.
- Long client paths/errors and smaller desktop work areas must wrap/scroll without hiding saves.
- Keyboard focus, Escape, drop-down selection and folded advanced controls must remain operable.
- Folding history must not conceal errors, warnings, or saved-target refusal.
- Renaming/localizing displayed options must preserve actual stored values.

### Task 1: Settings styling and actual-window acceptance

**Files:** Modify src/DFLegacy.Launcher/Views/ConfigWindow.xaml and .xaml.cs only for chrome, viewport bounds and presentation. Preserve all control names and configuration handlers.

**Interfaces:** Existing ResolutionBox/PresentationBox selections and named diagnostics; existing Save_Click/DisplaySave_Click/DisplayReload_Click/Cancel_Click. WindowChrome handles dragging/resizing; the existing close handler handles the title-bar close action.

- [ ] Replace mixed native/HandyControl appearance with scoped dark-metal styles, left navigation, gold primary buttons and readable fonts.
- [ ] Place display selections together, keep save/warning/error visible, fold path/history and advanced connection fields; retain all controls and stored option values.
- [ ] Run the complete Launcher suite with the matching native DLL, publish a fresh single-file portable build, and inspect both pages plus popups, folded content, focus and bounded-size layout through Sky.
- [ ] Commit the visual deliverable, request one fresh review scoped to this new user-directed UI change, and resolve material findings with evidence.

### Task 2: Final publication acceptance

**Files:** Update original acceptance report and plan; use existing protected-main integration, Gitea and private evidence tooling.

**Interfaces:** Corrected single-file launcher plus unchanged verified native scaling code; original Task4 remote/download/install/pause gates.

- [ ] Preserve main's original dirty bytes/status, fast-forward the UI commit, verify Launcher tests and push a complete CI run.
- [ ] Verify the final published assets and OCI identity, run the actual packaged launcher/client, install only validated files and preserve all original resources/configuration.
- [ ] Restore Runner pause and owned process baseline, complete original Task4, archive both ledgers and record final acceptance.
