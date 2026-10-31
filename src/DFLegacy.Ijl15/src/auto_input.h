#pragma once

namespace dfl::auto_input
{
    // Installs the GetAsyncKeyState IAT hook, verifies the mouse entry
    // point, and loads auto-input.json. Returns false when the keyboard hook
    // could not be installed; the client keeps running either way.
    bool Install() noexcept;
    // Cancels the timeline, releases held synthetic input while its native
    // bridge is still active, then restores the installed hook entries.
    void Restore() noexcept;
    // Mirrors dfl::ijl15_config::Get().EnableAutoInput.
    bool IsEnabled() noexcept;
    // Frame pump: advances the timeline with the current tick. The call site
    // in performance_overlay.cpp gates this on IsEnabled().
    void Pump() noexcept;

    // Built-in activation mailbox: <exe dir>\auto-input.focus contains this
    // process's decimal PID (optional surrounding ASCII whitespace). A game UI
    // timer checks it even when rendering is paused in the background; Pump
    // also checks it before its foreground gate. Only an owned visible window
    // can be activated. Verified activation renames it to .focus.done.
    // Pure validation is exposed so tests never have to activate a window.
    bool IsFocusRequestForProcess(
        const char* text, unsigned size, unsigned long processId) noexcept;

    // ------------------------------------------------------------------
    // Task 2: pure-logic core -- JSON subset parser, timeline state
    // machine, key-edge table. No hooks, no threads; everything below runs
    // on the caller's (game main) thread.
    // ------------------------------------------------------------------

    enum Op : int
    {
        kKey = 0,
        kClick = 1,
        kMove = 2,
        kText = 3,
        // Native right mouse input. This is separate from the ordinary left
        // click used to open the player interaction menu on this client.
        kRClick = 4,
        // Left drag: source move/down, target move, target up. Stages keep
        // at least16ms spacing even if a pump arrives past the nominal end.
        // Earlier mouse steps finish at least16ms before a drag starts;
        // later mouse steps wait until at least16ms after its target up.
        kDrag = 5,
    };

    struct Step
    {
        int t = 0;
        Op op = kKey;
        unsigned vk = 0;
        int x = 0;
        int y = 0;
        int dur = 80;
        char text[64] = {};
        int x2 = 0;
        int y2 = 0;
    };

    static constexpr int kMaxSteps = 256;

    // Parses the spec section 3.1 JSON subset. Accepts only a root object
    // with "version" (must be the integer 1) and "steps" (an array of flat
    // step objects whose keys are t/op/vk/x/y/dur/chars/x2/y2); strings know only
    // the \\, \", \r and \n escapes; numbers are decimal integers. Any other shape --
    // unknown keys, other escapes, trailing bytes, more steps than the
    // caller takes -- is rejected. On success fills steps/count/version and
    // returns true; on failure returns false and leaves the caller's
    // buffers untouched.
    // Drag requires x/y/x2/y2 and rejects vk/chars; other operations reject
    // the new destination fields and retain their existing field compatibility.
    bool ParsePlan(const char* text, Step* steps, int maxSteps, int* count, int* version);

    // Advances the timeline. nowMs uses the GetTickCount() epoch; the plan's
    // origin was fixed when it was armed, so the elapsed time is
    // nowMs - origin. Fires the edges whose time has come and returns how
    // many keyboard edges (downs and ups, text included) were fired in this
    // call.
    int Advance(unsigned nowMs);

    // Key-edge table read by the GetAsyncKeyState hook: a synthetic press
    // reports 0x8000, everything else forwards the original untouched.
    short QueryKey(unsigned vk, short original);

    // Loads <exe dir>\auto-input.json, parses it and arms the plan with the
    // current tick as the origin. Task 3's Install() calls this. Returns
    // false when missing (state unchanged) or malformed/unreadable (the prior
    // timeline is canceled and held synthetic inputs are released).
    // When a plan armed from the file reaches Finished, Advance renames the
    // file to auto-input.done.json exactly once.
    bool LoadPlanFile();

    // Arms an already-parsed plan without touching the file system (the
    // brief's "ParsePlan + internal ArmPlan equivalent"). t0Ms is the plan's
    // time origin in GetTickCount() coordinates: live callers pass
    // GetTickCount(), tests pass a synthetic origin such as 0. Re-arming
    // releases held synthetic inputs before replacing the timeline. Invalid
    // arguments also cancel/release; the injection log is left observable.
    bool ArmPlan(const Step* steps, int count, int version, unsigned t0Ms = 0);

    // True once every step completed and no up edge is pending.
    bool IsFinished();

    // Injection log: a fixed 64-entry ring of the primitives this module
    // would inject. Task 2 records the click-derived primitives here; Task 3
    // swaps the real NativeMouseInput call in. Tests and the on-device
    // verification read it back.
    enum InjectKind : int
    {
        kKeyDown = 0,
        kKeyUp = 1,
        kMouseMove = 2,
        kMouseDown = 3,
        kMouseUp = 4,
        kMouseRightDown = 5,
        kMouseRightUp = 6,
    };

    struct InjectionRecord
    {
        int op;       // an InjectKind value
        unsigned vk;  // key VK code; 0 for mouse primitives
        int x;
        int y;
        unsigned buttons; // native mouse wParam mask; zero for released buttons
    };

    // Copies the buffered records, oldest first, up to maxRecords of them,
    // and returns how many were copied.
    int GetInjectionLog(InjectionRecord* out, int maxRecords);
    void ClearInjectionLog();

    // Translates a virtual-key code to the DirectInput keyboard-state index
    // (a DIK code: the Set-1 scan code, with bit 7 set for the E0-prefixed
    // extended keys). Exposed for the characterisation tests: MapVirtualKeyA
    // never reports the E0 prefix on this host (measured 2026-10-23), and
    // getting this wrong writes the arrows to 0x48/0x4B/0x4D/0x50 where the
    // game never looks.
    unsigned char MapVkToDik(unsigned vk) noexcept;

    // Fills a 256-byte DirectInput keyboard buffer exactly the way the
    // synthetic keyboard device's GetDeviceState does: the physical keyboard
    // (GetAsyncKeyState) merged with the keys this module is synthesizing.
    // The device calls the same code path; the tests call it directly so the
    // plan -> synth table -> state buffer wiring is covered without a game.
    void BuildKeyboardState(unsigned char* keys) noexcept;
}
