# Payment browser and frame scaling verification

Status: isolated native build verified and installed with user authorization. The user screenshot confirms frame/page scaling; live test top-up awaits server deployment and user acceptance. No live game account balances changed during implementation.

## Reproduction and cause

The active 1600x1200 client used a separately owned `Browser Example` window,
460x316, centered at the main window center. The renderer retained a 640x480
logical backbuffer. The native opening method copied physical browser offsets
into background-frame fields, which were then scaled a second time.

Separately, requests to `games.firekula.site:8081/client/client-auth.js` over the
IPv4 endpoint returned HTTP302 to a provider blocking page. The IPv4 literal
Host and IPv6 endpoint returned HTTP200 with the expected PayAuth definition.
Source changes do not remove provider filtering; deployment must use a reachable
account-service URL. Missing PayAuth now produces an inline error instead of
calling an undefined object. The diamond callback's incorrect setPayMessage call
was also reproduced and corrected.

## Bounded native seam

Original DNF.exe SHA256:
`6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`.

- Payment opening method: 0x820280; original create call: 0x8203E6 -> 0x8AB260.
- The installer checks original call bytes, callee prologue and 44-byte native
  coordinate consumer before patching this callsite only.
- Both callee exits (0x8AB280, 0x8AB300) use C21000 / ret0x10: four stdcall args.
- RECT pointer is loaded at 0x8AB2B6; fields are copied into scalar arguments for
  CreateWindowExA at 0x8AB2E2. The pointer is never retained or passed onward;
  lpParam is zero. A stack-local physical RECT is therefore valid.
- The original payment call passes modal=false at 0x8203B7. Failure is checked by
  the original caller; the wrapper leaves the caller RECT unchanged on failure.
- Successful creation uses scaled physical dimensions, then returns the logical
  RECT for the original caller's frame-coordinate calculation.
- At 1600x1200 the viewport becomes1150x790 and frame offsets remain90,82.
  The patch supports verified borderless4:3 geometry; unsupported geometry keeps
  the original behavior. It does not change the internal D3D dimensions.

## Validation

- Arithmetic regression observed failing before implementation, then passing.
- Isolated native Release build and all44 CTests passed.
- Nine webpage tests cover missing script, Cera/diamond callbacks, viewport zoom,
  legacy resize events and page integration. Existing Manager browser tests stay
  included in CI.
- Local browser at1150x790 reports body zoom2.5. Authentication overlay fits
  within the viewport. This is browser QA, not a claim of live MSHTML acceptance.
- With user authorization, the isolated DLL was installed with backups and the game restarted. The subsequent user screenshot confirms the enlarged page and native frame align. The request showed HTTP 0; no successful live top-up is claimed.
- Added an explicit opt-in test Cera form flow for character-name top-ups. Backend persistence and notification tests pass; live Chinese-name form acceptance remains for the user after deployment.

Commands:

```powershell
./src/DFLegacy.Ijl15/build-native.ps1 -RunTests -BuildDirectory D:/DFLegacyCI/payment-layout-build
node --test tests/DFLegacy.Manager.Tests/manager-browser.test.cjs tests/DFLegacy.Manager.Tests/client-pay-browser.test.cjs
```
