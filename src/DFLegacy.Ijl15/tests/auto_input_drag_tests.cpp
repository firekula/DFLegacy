#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>

#include "auto_input.h"

namespace
{
    using namespace dfl::auto_input;
    int failures = 0;

    bool Check(bool ok, const char* message)
    {
        if (!ok) { ++failures; std::printf("FAIL: %s\n", message); }
        return ok;
    }

    bool ParseDrag(Step& step, int t = 0, int dur = 80)
    {
        char json[256] = {};
        _snprintf_s(json, sizeof(json), _TRUNCATE,
            "{\"version\":1,\"steps\":[{\"t\":%d,\"op\":\"drag\",\"x\":10,\"y\":20,\"x2\":300,\"y2\":400,\"dur\":%d}]}", t, dur);
        int count = 0, version = 0;
        return ParsePlan(json, &step, 1, &count, &version) && count == 1 && version == 1;
    }

    void Schema()
    {
        Step step{};
        Check(ParseDrag(step) && step.op == kDrag && step.x == 10 && step.y == 20
            && step.x2 == 300 && step.y2 == 400 && step.dur == 80,
            "drag parses distinct source/destination coordinates");
        const char* bad[] = {
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"y2\":4}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3}",
            "{\"t\":0,\"op\":\"drag\",\"y\":2,\"x2\":3,\"y2\":4}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"x2\":3,\"y2\":4}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3,\"y2\":4,\"x2\":5}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3.5,\"y2\":4}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3,\"y2\":4,\"dur\":-1}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3,\"y2\":4,\"vk\":13}",
            "{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2,\"x2\":3,\"y2\":4,\"chars\":\"x\"}",
            "{\"t\":0,\"op\":\"click\",\"x2\":1}",
            "{\"t\":0,\"op\":\"rclick\",\"y2\":1}",
            "{\"t\":0,\"op\":\"move\",\"x2\":1}",
            "{\"t\":0,\"op\":\"key\",\"x2\":1}",
            "{\"t\":0,\"op\":\"text\",\"y2\":1}"
        };
        for (const auto* body : bad)
        {
            const std::string json = std::string("{\"version\":1,\"steps\":[") + body + "]}";
            Step sentinel{}; sentinel.x = 12345;
            int count = 17, version = 23;
            Check(!ParsePlan(json.c_str(), &sentinel, 1, &count, &version)
                && sentinel.x == 12345 && count == 17 && version == 23,
                "malformed drag/destination fields are rejected transactionally");
        }
    }

    void Timeline()
    {
        Restore(); ClearInjectionLog();
        Step step{};
        if (!Check(ParseDrag(step, 100), "nominal drag plan parses")) return;
        Check(ArmPlan(&step, 1, 1), "nominal drag arms");
        InjectionRecord log[8] = {};
        Advance(99); Check(GetInjectionLog(log, 8) == 0, "drag does not fire early");
        Advance(100);
        Check(GetInjectionLog(log, 8) == 2 && log[0].op == kMouseMove && log[0].x == 10
            && log[1].op == kMouseDown && log[1].x == 10 && log[1].y == 20,
            "drag starts with source move then left-down");
        Advance(139); Check(GetInjectionLog(log, 8) == 2, "drag waits for its nominal midpoint");
        Advance(140);
        Check(GetInjectionLog(log, 8) == 3 && log[2].op == kMouseMove
            && log[2].x == 300 && log[2].y == 400 && log[2].buttons == 1,
            "drag moves to destination with native MK_LBUTTON still held");
        Advance(179); Check(GetInjectionLog(log, 8) == 3 && !IsFinished(), "drag holds until nominal end");
        Advance(180);
        Check(GetInjectionLog(log, 8) == 4 && log[3].op == kMouseUp
            && log[3].x == 300 && log[3].y == 400 && log[3].buttons == 0 && IsFinished(),
            "drag releases at destination exactly once");
        Advance(1000); Check(GetInjectionLog(log, 8) == 4, "finished drag never repeats");
    }

    void CatchUpAndWrap()
    {
        for (int duration : {0, 1, 80})
        {
            Restore(); ClearInjectionLog();
            Step step{};
            if (!Check(ParseDrag(step, 0, duration), "late/short drag parses")) continue;
            ArmPlan(&step, 1, 1);
            InjectionRecord log[8] = {};
            Advance(1000); Advance(1000); Advance(1015);
            Check(GetInjectionLog(log, 8) == 2 && !IsFinished(),
                "catch-up and repeated same-sample pumps cannot collapse drag down/move/up");
            Advance(1016); Advance(1016); Advance(1031);
            Check(GetInjectionLog(log, 8) == 3 && log[2].op == kMouseMove && !IsFinished(),
                "late destination move has a distinct16ms sampling gap");
            Advance(1032);
            Check(GetInjectionLog(log, 8) == 4 && log[3].op == kMouseUp && IsFinished(),
                "late release follows destination move after another16ms gap");
        }
        Restore(); ClearInjectionLog();
        Step step{};
        if (!Check(ParseDrag(step, 0, 32), "wrap drag parses")) return;
        constexpr unsigned origin = 0xFFFFFFF0u;
        ArmPlan(&step, 1, 1, origin);
        Advance(origin); Advance(origin + 16u); Advance(origin + 32u);
        InjectionRecord log[8] = {};
        Check(GetInjectionLog(log, 8) == 4 && log[3].x == 300 && IsFinished(),
            "drag stage spacing remains valid across GetTickCount wrap");
    }

    void CatchUpMouseSequence()
    {
        // Nominally separate gestures must remain separate when the first
        // pump arrives after both scheduled end times. Keyboard/text stay
        // independent of the drag's ownership of the one native pointer.
        for (Op next : {kClick, kRClick, kDrag, kMove})
        {
            Restore(); ClearInjectionLog();
            Step plan[4]{};
            if (!Check(ParseDrag(plan[0]), "catch-up sequence drag parses")) continue;
            plan[1].op = next; plan[1].t = 200; plan[1].dur = 80;
            plan[1].x = 500; plan[1].y = 600; plan[1].x2 = 700; plan[1].y2 = 800;
            plan[2].op = kKey; plan[2].t = 200; plan[2].vk = 'A';
            plan[3].op = kText; plan[3].t = 200; strcpy_s(plan[3].text, "b");
            Check(ArmPlan(plan, 4, 1), "catch-up sequence arms");
            InjectionRecord all[32] = {}, mouse[16] = {};
            const auto mouseLog = [&]()
            {
                const int count = GetInjectionLog(all, 32);
                int found = 0;
                for (int i = 0; i < count; ++i)
                    if (all[i].op >= kMouseMove) mouse[found++] = all[i];
                return found;
            };
            Advance(1000);
            Check(mouseLog() == 2 && mouse[0].x == 10 && mouse[1].op == kMouseDown,
                "late later click/drag/move cannot overwrite the first drag down");
            Check((QueryKey('A', 0) & 0x8000) != 0 && (QueryKey('B', 0) & 0x8000) != 0,
                "keyboard and text start in parallel while later mouse gestures wait");
            Advance(1016);
            Check(mouseLog() == 3 && mouse[2].op == kMouseMove
                && mouse[2].x == 300 && mouse[2].buttons == 1,
                "later mouse actions cannot move or release an active drag");
            Advance(1032); Advance(1032); Advance(1047);
            Check(mouseLog() == 4 && mouse[3].op == kMouseUp
                && mouse[3].x == 300 && mouse[3].y == 400 && mouse[3].buttons == 0,
                "drag target release has a full16ms sample before the next mouse action");
            Advance(1048);
            const int started = next == kMove ? 5 : 6;
            Check(mouseLog() == started && mouse[4].op == kMouseMove
                && mouse[4].x == 500 && mouse[4].y == 600,
                "the deferred mouse action starts after the drag release sampling gap");
            if (next == kDrag)
            {
                Advance(1063); Check(mouseLog() == 6, "second late drag retains its own down sample");
                Advance(1064); Advance(1080);
                Check(mouseLog() == 8 && mouse[6].op == kMouseMove && mouse[6].x == 700
                    && mouse[6].buttons == 1 && mouse[7].op == kMouseUp
                    && mouse[7].x == 700 && mouse[7].y == 800 && IsFinished(),
                    "second late drag completes at its own target after distinct stages");
            }
            else if (next != kMove)
            {
                // Preserve ordinary click's existing late-up behavior: its
                // up is eligible on the next Advance, without a new16ms rule.
                Advance(1048);
                Check(mouseLog() == 7 && mouse[6].op == (next == kRClick ? kMouseRightUp : kMouseUp)
                    && mouse[6].x == 500 && mouse[6].y == 600 && IsFinished(),
                    "deferred ordinary click retains its existing next-pump release semantics");
            }
            else Check(IsFinished(), "deferred move completes after the first drag");
        }
    }

    void CatchUpMousePrefix()
    {
        for (Op prefix : {kClick, kRClick, kMove})
        {
            Restore(); ClearInjectionLog();
            Step plan[2]{};
            plan[0].op = prefix; plan[0].dur = 80; plan[0].x = 500; plan[0].y = 600;
            if (!Check(ParseDrag(plan[1], 200), "catch-up drag prefix parses")) continue;
            Check(ArmPlan(plan, 2, 1), "catch-up drag prefix arms");
            InjectionRecord log[16] = {};
            Advance(1000);
            const int initial = prefix == kMove ? 1 : 2;
            Check(GetInjectionLog(log, 16) == initial && log[0].op == kMouseMove && log[0].x == 500,
                "late drag waits for the earlier click/rclick/move instead of double-down");
            const unsigned releasedAt = prefix == kMove ? 1000u : 1016u;
            Advance(releasedAt); Advance(releasedAt); Advance(releasedAt + 15u);
            const int released = prefix == kMove ? 1 : 3;
            Check(GetInjectionLog(log, 16) == released && (prefix == kMove
                || (log[2].op == (prefix == kRClick ? kMouseRightUp : kMouseUp)
                    && log[2].x == 500 && log[2].buttons == 0)),
                "drag cannot overwrite its prefix release in the same or next15ms sample");
            Advance(releasedAt + 16u);
            Check(GetInjectionLog(log, 16) == released + 2 && log[released].op == kMouseMove
                && log[released].x == 10 && log[released + 1].op == kMouseDown,
                "drag begins after the earlier mouse action completed and a16ms sample elapsed");
            Advance(releasedAt + 32u); Advance(releasedAt + 48u);
            Check(GetInjectionLog(log, 16) == released + 4 && log[released + 2].op == kMouseMove
                && log[released + 2].x == 300 && log[released + 2].buttons == 1
                && log[released + 3].op == kMouseUp && log[released + 3].x == 300
                && log[released + 3].y == 400 && IsFinished(),
                "drag after a delayed mouse prefix retains held target move and target release");
        }
    }

    void Lifecycle()
    {
        for (Op op : {kClick, kRClick})
        {
            for (int action : {0, 1, 2})
            {
                Restore(); ClearInjectionLog();
                Step pressed{}; pressed.op = op; pressed.x = 20; pressed.y = 30; pressed.dur = 1000;
                ArmPlan(&pressed, 1, 1); Advance(0); ClearInjectionLog();
                Step replacement{}; replacement.op = kMove; replacement.t = 10000;
                if (action == 0) Restore();
                else if (action == 1) Check(ArmPlan(&replacement, 1, 1), "replacement plan arms");
                else Check(!ArmPlan(nullptr, 1, 1), "malformed arm rejects");
                InjectionRecord log[8] = {};
                Check(GetInjectionLog(log, 8) == 1 && log[0].op == (op == kClick ? kMouseUp : kMouseRightUp)
                    && log[0].x == 20 && log[0].y == 30 && log[0].buttons == 0,
                    "Restore/rearm/malformed arm releases a held synthetic mouse button");
                Advance(2000);
                Check(GetInjectionLog(log, 8) == 1, "canceled timeline cannot emit delayed mouse edges");
            }
        }
        for (bool afterMove : {false, true})
        {
            Restore(); ClearInjectionLog();
            Step step{};
            if (!Check(ParseDrag(step), "cancel drag parses")) continue;
            ArmPlan(&step, 1, 1); Advance(0);
            if (afterMove) Advance(40);
            ClearInjectionLog(); Restore();
            InjectionRecord log[8] = {};
            Check(GetInjectionLog(log, 8) == 1 && log[0].op == kMouseUp
                && log[0].x == (afterMove ? 300 : 10) && log[0].y == (afterMove ? 400 : 20),
                "canceling drag releases at its current native pointer position");
        }
    }

    void MalformedFile()
    {
        Restore(); ClearInjectionLog();
        Step step{};
        if (!Check(ParseDrag(step), "malformed-file fixture drag parses")) return;
        char path[MAX_PATH] = {};
        const DWORD size = GetModuleFileNameA(nullptr, path, MAX_PATH);
        if (!Check(size != 0 && size < MAX_PATH, "test host path is bounded")) return;
        char* slash = std::strrchr(path, '\\');
        if (!Check(slash != nullptr, "test host directory exists")) return;
        strcpy_s(slash + 1, MAX_PATH - static_cast<size_t>(slash + 1 - path), "auto-input.json");
        const bool existed = GetFileAttributesA(path) != INVALID_FILE_ATTRIBUTES;
        std::ifstream prior(path, std::ios::binary);
        const std::string old((std::istreambuf_iterator<char>(prior)), std::istreambuf_iterator<char>());
        prior.close();
        ArmPlan(&step, 1, 1); Advance(0); ClearInjectionLog();
        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << "{\"version\":1,\"steps\":[{\"t\":0,\"op\":\"drag\",\"x\":1,\"y\":2}]}";
        }
        Check(!LoadPlanFile(), "malformed on-disk drag cannot arm");
        InjectionRecord log[8] = {};
        Check(GetInjectionLog(log, 8) == 1 && log[0].op == kMouseUp,
            "malformed replacement file cancels and releases the old held drag");
        Advance(2000);
        Check(GetInjectionLog(log, 8) == 1, "malformed file leaves no delayed drag edge");
        ArmPlan(&step, 1, 1); Advance(0); ClearInjectionLog();
        const HANDLE exclusive = CreateFileA(path, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (Check(exclusive != INVALID_HANDLE_VALUE, "test owns an exclusive replacement-plan file handle"))
        {
            Check(!LoadPlanFile(), "an exclusively locked replacement file cannot arm");
            Check(GetInjectionLog(log, 8) == 1 && log[0].op == kMouseUp,
                "an unreadable replacement plan releases the old held mouse button");
            Advance(2000);
            Check(GetInjectionLog(log, 8) == 1 && log[0].op == kMouseUp,
                "open failure cancellation leaves no later drag edge");
            CloseHandle(exclusive);
        }
        Check(DeleteFileA(path) != FALSE, "test removes only its own replacement file");
        ArmPlan(&step, 1, 1); Advance(0); ClearInjectionLog();
        Check(!LoadPlanFile() && GetInjectionLog(log, 8) == 0,
            "a genuinely missing replacement file preserves the old valid plan");
        Advance(40); Advance(80);
        Check(GetInjectionLog(log, 8) == 2 && log[0].op == kMouseMove && log[1].op == kMouseUp && IsFinished(),
            "the old plan still completes normally when no replacement file exists");
        if (existed)
        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << old;
        }
        else DeleteFileA(path);
    }

    void MalformedTypedAndPureParse()
    {
        for (int kind : {0, 1, 2, 3})
        {
            Restore(); ClearInjectionLog();
            Step drag{};
            if (!Check(ParseDrag(drag), "typed validation fixture parses")) continue;
            ArmPlan(&drag, 1, 1); Advance(0); Advance(40); ClearInjectionLog();
            Step bad{};
            if (kind == 0) bad.op = static_cast<Op>(99);
            else if (kind == 1) bad.t = -1;
            else if (kind == 2) bad.dur = -1;
            else bad.vk = 0x10000;
            Check(!ArmPlan(&bad, 1, 1), "malformed typed step cannot arm");
            InjectionRecord log[8] = {};
            Check(GetInjectionLog(log, 8) == 1 && log[0].op == kMouseUp
                && log[0].x == 300 && log[0].y == 400,
                "malformed typed step releases the current drag destination");
            Advance(2000);
            Check(GetInjectionLog(log, 8) == 1, "malformed typed step leaves no delayed input");
        }
        Restore(); ClearInjectionLog();
        Step drag{};
        if (!Check(ParseDrag(drag), "pure parse fixture drag parses")) return;
        ArmPlan(&drag, 1, 1); Advance(0); ClearInjectionLog();
        Step out{}; out.x = 12345;
        int count = 17, version = 23;
        Check(!ParsePlan("{", &out, 1, &count, &version) && out.x == 12345 && count == 17 && version == 23,
            "pure malformed parsing preserves its caller buffers");
        InjectionRecord log[8] = {};
        Check(GetInjectionLog(log, 8) == 0, "pure parsing does not cancel the currently running plan");
        Advance(40); Advance(80);
        Check(GetInjectionLog(log, 8) == 2 && log[0].op == kMouseMove && log[0].buttons == 1
            && log[1].op == kMouseUp && IsFinished(),
            "the prior valid drag still moves/releases after a pure parse rejection");
    }
}

bool TestAutoInputDrag()
{
    failures = 0;
    Schema(); Timeline(); CatchUpAndWrap(); CatchUpMouseSequence(); CatchUpMousePrefix();
    Lifecycle(); MalformedFile(); MalformedTypedAndPureParse();
    dfl::auto_input::Restore();
    std::printf("Auto-input drag tests: %d failures\n", failures);
    return failures == 0;
}
