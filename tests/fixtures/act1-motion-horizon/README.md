# ACT1 motion horizon arithmetic replay

Contains **1980 original instruction/constant bytes in 53 fragments** and **3 actual native installed repair bytes**. Each fragment has an absolute virtual address, byte count and SHA256 in `manifest.json`. The manifest also records the full original PE SHA256, native test executable hash, native source hashes and successful export command.

Instruction fetches and original-image data reads observed while running all four `MotionHorizonNativeTests`, both original and repaired, were extracted. Five additional bounded original-code ranges support `MotionGridNativeTests` and the control-flow, branch and provider probe tests; each range and its purpose is listed under `capture.audit_ranges`. They contain the grid division/control-flow block, grid routine, v18C getter, remaining motion installer site and provider result marker entry. The probe tests still inspect and execute probes from the actual compiled DLL. No full executable, game resources, mutable player state, heap or stack was captured. The tests create their own synthetic vectors, vtable, controller and stack. The 500ms integration path uses original getter/setter, arithmetic and float-to-integer instructions. No Python motion approximation replaces client instructions. `load_original_image().get_data(rva, length)` supplies a verified bounded adapter for the grid and probe tests; reads outside recorded fragments fail.

This covers decoded-packet reconciliation at `0x4C9919` and per-axis integration at `0x4C8C00`, including existing duration/acceleration, terminal velocity, unaffected axes and full-target publication cases. It does not cover packet decoding, live maps, actor collision, server authority, network delivery or real gameplay. Missing/corrupt/overlapping/oversized fragments and unexpected repair bytes fail validation. In the horizon replay, uncaptured original-image reads/instructions and any original-image write fail rather than using mapped-page zero padding.

Regenerate into a **new directory** from the exact verified original PE and the actual native test binary built from the recorded native source:

```powershell
python -m tools.acceptance.motion_replay_fixture --original-exe <path-to-original-DNF.exe> --native-tests <path-to-ijl15_tests.exe> --output <new-directory>
```

The command reads the original PE without launching or modifying it. It executes only the native unit-test executable to export a fresh installed patch, then executes the bounded instruction replays under Unicorn. The default original-code input needs no private client or environment variables. The probe tests still require `DFLEGACY_NATIVE_LOOP_DLL` pointing to the actual built DLL. For full-image confirmation, supply both `DFLEGACY_ACT1_CLIENT_EXE` and `DFLEGACY_MOTION_HORIZON_PATCH`; the grid and probe tests accept `DFLEGACY_DNF_EXE` for full-image confirmation. Wrong original SHA256 or unexpected patch bytes fail.
