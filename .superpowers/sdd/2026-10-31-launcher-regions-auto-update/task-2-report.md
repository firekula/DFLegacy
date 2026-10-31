# Task 2 implementation report — original message notifications

## Status and commits

- Completed Task 2 only on `codex/launcher-regions-auto-update`.
- Implementation: `393d993` (`feat(launcher): restore original message notifications`).
- Starting HEAD: `dd2d0e97685cff4eb44cd0bd3b0dc8eef25834cb` (Task 1 reviewed and approved).
- This report is committed separately after the implementation.

## Delivered behavior and integration interface

- Public `DFLegacy.Launcher.ClassicMessage.Show(Window? owner, string message)` opens one owned modal notification. Visible owners determine centering; a missing/invisible owner uses screen centering. Closing restores the owner.
- Public `DFLegacy.Launcher.Views.ClassicMessageWindow(string message)` constructs and lays out without starting a modal loop, allowing direct render tests.
- Native 286×170 RT_BITMAP314 stone panel, 80×29 original `button_yes00/01/02` confirmation artwork at original HintMsg.xml `(98,137)` position. Panel magenta pixels become alpha; no opaque rectangular background is introduced.
- Uses existing Songti monochrome outlined PixelText and ClassicStyles. The message body scrolls independently of the fixed confirmation button. Enter/Escape from the content close the window; the button exposes the accessible name `确认`, and the window exposes `提示信息`.
- Long diagnostic content is split locally into <=4096 UTF-16-unit PixelText blocks without splitting surrogate pairs. This avoids the shared rasterizer's existing per-block bound while retaining all message text and its final line. Shared PixelText/PixelRasterizer were not changed.
- Replaced every normal `Growl.Info/Warning/Success` call in MainWindow, account management, settings, registration, password modification and reset. Existing validation, persistence, callback ordering and error branches remain in place. OpenUrl/OpenTerms became instance helpers so their failures have the correct owner.
- Startup fallback MessageBox paths in App/Program remain available before UI initialization.
- Task 1 `_confirmedServerSelection`, `ReloadRuntimeOptions`, selection guards and account-service logic were not changed.

## RED → GREEN evidence

1. Added ClassicMessageTests on ClassicUiTests' existing owned STA/Application, before product changes. Focused command failed with seven expected failures: six source guards for remaining generic Growl callsites and the absent message window/Show API. Existing server selection and WPF tests still completed.
2. Implemented the panel and callsite migration. Focused command passed, including real WPF window construction/rendering, owner centering, accessible labels, visual-tree InputHitTest at all button corners/center, routed Enter/Escape from the scroll content, confirmation click close, real ScrollViewer extent/offset and modal owner restoration.
3. Self-review identified the shared 4096-character raster bound. Added a diagnostic exceeding 4096 characters (4095 CJK characters followed by an emoji, then 320 multiline rows and an END tail). The focused command failed at `oversized diagnostics are fully laid out without truncation or split Unicode pairs` before the message-local chunking fix.
4. Implemented message-local chunking. The final full launcher command passed, including the oversized message's preserved text, intact surrogate boundary, full scroll extent and final block inside the viewport after ScrollToEnd.

Focused command:

```powershell
dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release -p:BuildNativeAssets=false -p:Ijl15PrebuiltPath=D:/codex_worktree/deployment-web-release/DFLegacy/artifacts/native/ijl15/Release/ijl15.dll -- --server-selection-tests
```

Final full command (exit 0):

```powershell
dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release -p:BuildNativeAssets=false -p:Ijl15PrebuiltPath=D:/codex_worktree/deployment-web-release/DFLegacy/artifacts/native/ijl15/Release/ijl15.dll
```

Output:

```text
Display configuration behavior checks executed
Classic UI native raster and WPF layout checks executed
Local server selection, preservation and real TCP latency checks executed
Launcher configuration tests passed
```

The existing account/settings integration tests now dismiss only their test-owned ClassicMessageWindow instances with a controlled dispatcher timer. They still execute real modal account/settings round trips and verify Task 1's confirmed server/account preservation. There is only one Application, created and shut down on the existing test-owned STA.

## Resource and visual verification

- Updated `tools/build_classic_launcher_assets.py` and `docs/superpowers/reports/2026-10-31-launcher-classic-assets.json`; all four message PNGs are embedded by the existing WPF resource glob, with no runtime source-audit dependency.
- Verified all **120** original audit PNG file hashes against the audit manifest; originals stayed read-only.
- Verified all **25** embedded manifest asset hashes, dimensions and source hashes.
- Compared the new panel pixels against original image-0056 / RT_BITMAP314: exact except magenta→alpha. Compared all **3** confirmation-state images against original loose button_yes00/01/02: pixel-exact.
- Render evidence: `artifacts/launcher-message-validation-20261031/message-render.png`; inspected against the user-approved `artifacts/launcher-ui-audit-20261031/preview/server-resource-reference-20261031/message-preview.png`. The native panel, centered body, original confirmation and transparent corners match the approved design.
- Hash verification result: `artifacts/launcher-message-validation-20261031/verification.json`.
- `git diff --check` passed.

## Self-review and boundaries

- Reviewed complete owned product diff: notification transport and ownership only; no update engine, updater, publication configuration, native DLL source or release changes.
- No remote/service/runner actions, no player-install changes, no user application actions and no subagents/reviewers were used. WPF tests create and close only their own windows; loopback probes are existing controlled fixtures.
- No remaining known Task 2 defects. Tests use real WPF routed keyboard events and InputHitTest, not physical keyboard/mouse injection. A fresh controller-supplied review remains the next gate.
