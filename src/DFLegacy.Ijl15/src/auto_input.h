#pragma once

namespace dfl::auto_input
{
    // Task 3 wires the real hooks; this task only ships the install gate.
    bool Install() noexcept;
    void Restore() noexcept;
    // Mirrors dfl::ijl15_config::Get().EnableAutoInput.
    bool IsEnabled() noexcept;
    // Task 2/3 fill this in; it stays empty in this task.
    void Pump() noexcept;

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
    };

    static constexpr int kMaxSteps = 256;

    // Parses the spec section 3.1 JSON subset. Accepts only a root object
    // with "version" (must be the integer 1) and "steps" (an array of flat
    // step objects whose keys are t/op/vk/x/y/dur/chars); strings know only
    // the \\ and \" escapes; numbers are decimal integers. Any other shape --
    // unknown keys, other escapes, trailing bytes, more steps than the
    // caller takes -- is rejected. On success fills steps/count/version and
    // returns true; on failure returns false and leaves the caller's
    // buffers untouched.
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
    // false (state unchanged) when the file is missing or does not parse.
    // When a plan armed from the file reaches Finished, Advance renames the
    // file to auto-input.done.json exactly once.
    bool LoadPlanFile();

    // Arms an already-parsed plan without touching the file system (the
    // brief's "ParsePlan + internal ArmPlan equivalent"). t0Ms is the plan's
    // time origin in GetTickCount() coordinates: live callers pass
    // GetTickCount(), tests pass a synthetic origin such as 0. Re-arming
    // clears the key-edge table; the injection log is left alone.
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
    };

    struct InjectionRecord
    {
        int op;       // an InjectKind value
        unsigned vk;  // key VK code; 0 for mouse primitives
        int x;
        int y;
    };

    // Copies the buffered records, oldest first, up to maxRecords of them,
    // and returns how many were copied.
    int GetInjectionLog(InjectionRecord* out, int maxRecords);
    void ClearInjectionLog();
}
