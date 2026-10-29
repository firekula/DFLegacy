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
    Schema(); Timeline(); CatchUpAndWrap(); Lifecycle(); MalformedFile(); MalformedTypedAndPureParse();
    dfl::auto_input::Restore();
    std::printf("Auto-input drag tests: %d failures\n", failures);
    return failures == 0;
}
