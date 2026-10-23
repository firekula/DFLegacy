#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <vector>

#define IJL15_EXPORTS
#include "auto_input.h"
#include "dnf_patches.h"
#include "external_string_table.h"
#include "ijl15.h"
#include "ijl15_config.h"
#include "memory_patch.h"
#include "output_paths.h"

namespace
{
struct IjlApi
{
    HMODULE Module;
    const IJLibVersion* (__stdcall* GetLibVersion)();
    IJLERR (__stdcall* Init)(JPEG_CORE_PROPERTIES*);
    IJLERR (__stdcall* Free)(JPEG_CORE_PROPERTIES*);
    IJLERR (__stdcall* Read)(JPEG_CORE_PROPERTIES*, IJLIOTYPE);
    IJLERR (__stdcall* Write)(JPEG_CORE_PROPERTIES*, IJLIOTYPE);
    const char* (__stdcall* ErrorStr)(IJLERR);
};

int Fail(const char* message)
{
    std::fprintf(stderr, "FAIL: %s\n", message);
    return 1;
}

bool IsClose(unsigned char actual, unsigned char expected, int tolerance)
{
    return std::abs(
        static_cast<int>(actual) - static_cast<int>(expected)) <= tolerance;
}

template<typename T>
T Resolve(HMODULE module, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(module, name));
}

bool LoadApi(IjlApi* api)
{
    api->Module = LoadLibraryW(L"ijl15.dll");
    if (api->Module == nullptr)
    {
        return false;
    }

    api->GetLibVersion = Resolve<decltype(api->GetLibVersion)>(
        api->Module,
        "ijlGetLibVersion");
    api->Init = Resolve<decltype(api->Init)>(api->Module, "ijlInit");
    api->Free = Resolve<decltype(api->Free)>(api->Module, "ijlFree");
    api->Read = Resolve<decltype(api->Read)>(api->Module, "ijlRead");
    api->Write = Resolve<decltype(api->Write)>(api->Module, "ijlWrite");
    api->ErrorStr = Resolve<decltype(api->ErrorStr)>(
        api->Module,
        "ijlErrorStr");
    return api->GetLibVersion != nullptr
        && api->Init != nullptr
        && api->Free != nullptr
        && api->Read != nullptr
        && api->Write != nullptr
        && api->ErrorStr != nullptr;
}

bool TestMemoryPatchHelpers()
{
    constexpr std::size_t RegionSize = 64;
    void* const region = VirtualAlloc(
        nullptr,
        RegionSize,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE);
    if (region == nullptr)
    {
        return false;
    }

    std::memset(region, 0x5A, RegionSize);
    DWORD oldProtection = 0;
    if (!VirtualProtect(
            region,
            RegionSize,
            PAGE_EXECUTE_READ,
            &oldProtection))
    {
        VirtualFree(region, 0, MEM_RELEASE);
        return false;
    }

    const std::uintptr_t base =
        reinterpret_cast<std::uintptr_t>(region);
    const std::array<unsigned char, 4> bytes = {
        0x10,
        0x20,
        0x30,
        0x40
    };
    bool passed = dfl::memory_patch::WriteBytes(
        base,
        bytes.data(),
        bytes.size());
    passed = passed
        && std::memcmp(region, bytes.data(), bytes.size()) == 0;

    passed = passed && dfl::memory_patch::WriteNops(base + 4, 4);
    const auto* const memory =
        static_cast<const unsigned char*>(region);
    passed = passed
        && std::all_of(memory + 4, memory + 8, [](unsigned char value)
        {
            return value == 0x90;
        });

    passed = passed && dfl::memory_patch::Fill(base + 8, 0, 4);
    passed = passed
        && std::all_of(memory + 8, memory + 12, [](unsigned char value)
        {
            return value == 0;
        });

    constexpr std::size_t JumpOffset = 16;
    constexpr std::size_t CallOffset = 24;
    constexpr std::size_t JumpDestinationOffset = 48;
    constexpr std::size_t CallDestinationOffset = 52;
    passed = passed && dfl::memory_patch::WriteRelativeJump(
        base + JumpOffset,
        base + JumpDestinationOffset);
    passed = passed && memory[JumpOffset] == 0xE9;
    std::int32_t displacement = 0;
    std::memcpy(
        &displacement,
        memory + JumpOffset + 1,
        sizeof(displacement));
    passed = passed
        && base + JumpOffset + 5 + displacement
            == base + JumpDestinationOffset;

    passed = passed && dfl::memory_patch::WriteRelativeCall(
        base + CallOffset,
        base + CallDestinationOffset);
    passed = passed && memory[CallOffset] == 0xE8;
    std::memcpy(
        &displacement,
        memory + CallOffset + 1,
        sizeof(displacement));
    passed = passed
        && base + CallOffset + 5 + displacement
            == base + CallDestinationOffset;

    passed = passed
        && dfl::memory_patch::WriteBreakpoints(base + 32, 4);
    passed = passed
        && std::all_of(memory + 32, memory + 36, [](unsigned char value)
        {
            return value == 0xCC;
        });
    passed = passed
        && !dfl::memory_patch::WriteRelativeJump(0, base);
    passed = passed
        && !dfl::memory_patch::WriteRelativeCall(
            0x00010000,
            0xF0000000);

    MEMORY_BASIC_INFORMATION information{};
    passed = passed
        && VirtualQuery(
            region,
            &information,
            sizeof(information)) == sizeof(information)
        && information.Protect == PAGE_EXECUTE_READ;

    VirtualFree(region, 0, MEM_RELEASE);
    return passed;
}

bool TestCustomStackableUseModePolicy()
{
    using dfl::dnf_patches::ShouldUseStackableMode1;

    return dfl::dnf_patches::EnableCustomStackableUseMode1
        && ShouldUseStackableMode1(7'518u, 10u)
        && ShouldUseStackableMode1(7'519u, 10u)
        && ShouldUseStackableMode1(1'121u, 10u)
        && ShouldUseStackableMode1(1'122u, 10u)
        && ShouldUseStackableMode1(1'123u, 10u)
        && !ShouldUseStackableMode1(7'517u, 10u)
        && ShouldUseStackableMode1(71u, 13u)
        && ShouldUseStackableMode1(0u, 13u)
        && !ShouldUseStackableMode1(71u, 0u);
}

bool TestCustomPopupPatchConfiguration()
{
    return dfl::dnf_patches::RemoveCustomPopupAnnouncementTitle;
}

bool TestMailboxArchiveRefreshPatchConfiguration()
{
    return dfl::dnf_patches::FixMailboxArchiveRefresh;
}

bool TestCeraPurchaseGatePolicy()
{
    using Gate = dfl::dnf_patches::CeraPurchaseGate;
    using dfl::dnf_patches::ShouldBypassCeraPurchaseGate;

    return ShouldBypassCeraPurchaseGate(Gate::LegacySaleWindow)
        && ShouldBypassCeraPurchaseGate(Gate::PerCharacterLimit)
        && ShouldBypassCeraPurchaseGate(Gate::CharacterLevel)
        && !ShouldBypassCeraPurchaseGate(Gate::ClientInventoryCapacity)
        && !ShouldBypassCeraPurchaseGate(Gate::JobCompatibility)
        && !ShouldBypassCeraPurchaseGate(Gate::CurrencyBalance)
        && !ShouldBypassCeraPurchaseGate(Gate::RequiredOption)
        && !ShouldBypassCeraPurchaseGate(Gate::GiftEligibility);
}

bool TestAutoInputConfigDefaults()
{
    // Default off: must be false when Config.ini does not provide the key.
    return dfl::ijl15_config::Settings{}.EnableAutoInput == false;
}

bool TestExternalStringTableConfiguration()
{
    using Validation = dfl::external_string_table::EncodingValidation;

    constexpr unsigned char Cp936Text[] = {
        '7', '2', '3', '>',
        0xCD, 0xE6,
        0xBC, 0xD2,
        '\r', '\n'};
    constexpr unsigned char Utf8Text[] = {
        '7', '2', '3', '>',
        0xE7, 0x8E, 0xA9,
        0xE5, 0xAE, 0xB6,
        '\r', '\n'};
    constexpr unsigned char Utf8Bom[] = {
        0xEF, 0xBB, 0xBF, '7', '2', '3', '>'};
    constexpr unsigned char InvalidCp936[] = {
        '7', '2', '3', '>', 0x81, 0x7F};
    constexpr unsigned char EmbeddedNull[] = {
        '7', '2', '3', '>', 0, 'x'};

    wchar_t path[MAX_PATH]{};
    return dfl::dnf_patches::UseExternalDnfStringTable
        && std::wcscmp(
            dfl::external_string_table::RelativePath,
            L"Plugin\\dnf.str") == 0
        && dfl::external_string_table::BuildPluginPath(
            L"D:\\DFLegacy\\DF2008\\DNF.exe",
            path,
            MAX_PATH)
        && std::wcscmp(
            path,
            L"D:\\DFLegacy\\DF2008\\Plugin\\dnf.str") == 0
        && dfl::external_string_table::ValidateCp936(
            Cp936Text,
            sizeof(Cp936Text)) == Validation::ValidCp936
        && dfl::external_string_table::ValidateCp936(
            Utf8Text,
            sizeof(Utf8Text)) == Validation::Utf8
        && dfl::external_string_table::ValidateCp936(
            Utf8Bom,
            sizeof(Utf8Bom)) == Validation::UnicodeBom
        && dfl::external_string_table::ValidateCp936(
            InvalidCp936,
            sizeof(InvalidCp936)) == Validation::InvalidCp936
        && dfl::external_string_table::ValidateCp936(
            EmbeddedNull,
            sizeof(EmbeddedNull)) == Validation::EmbeddedNull;
}

bool TestRelativeOutputPathPolicy()
{
    char output[128]{};
    if (!dfl::output_paths::BuildRelativePath(
            "DNF.trc",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\DNF.trc") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "Low\\DNF\\NeopleEngine.LOG",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\NeopleEngine.LOG") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "SomeOtherClient.LOG",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\SomeOtherClient.LOG") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "ScreenShot",
            output,
            sizeof(output))
        || std::strcmp(output, "ScreenShot\\") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "ScreenShot\\ScreenShot00004.JPG",
            output,
            sizeof(output))
        || std::strcmp(output, "ScreenShot\\ScreenShot00004.JPG") != 0)
    {
        return false;
    }

    return !dfl::output_paths::BuildRelativePath(
        "Interface\\premiumService.img",
        output,
        sizeof(output));
}

// ---------------------------------------------------------------------------
// Synthetic input core (src/auto_input.cpp).
//
// What this proves: the auto-input.json subset parser accepts exactly the
// spec 3.1 shape and nothing else, and the timeline state machine fires key
// edges, click primitives and text expansion at the pinned times. ArmPlan's
// explicit origin pins every timeline to synthetic ticks, so none of these
// tests touch the file system.
// ---------------------------------------------------------------------------

bool TestParsePlanHappyPath()
{
    using namespace dfl::auto_input;
    Step steps[kMaxSteps];
    int count = 0;
    int version = 0;
    const char* json =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"click\",\"x\":320,\"y\":240},"
        "{\"t\":300,\"op\":\"key\",\"vk\":13,\"dur\":60},"
        "{\"t\":500,\"op\":\"text\",\"chars\":\"test\"}]}";
    if (!ParsePlan(json, steps, kMaxSteps, &count, &version))
    {
        return false;
    }
    return version == 1
        && count == 3
        && steps[0].t == 100
        && steps[0].op == kClick
        && steps[0].x == 320
        && steps[0].y == 240
        && steps[0].dur == 80  // omitted in the JSON: the struct default survives
        && steps[1].op == kKey
        && steps[1].vk == 13
        && steps[1].dur == 60
        && steps[2].op == kText
        && steps[2].t == 500
        && steps[2].text[0] == 't'
        && steps[2].text[1] == 'e'
        && steps[2].text[2] == 's'
        && steps[2].text[3] == 't'
        && steps[2].text[4] == '\0';
}

bool TestParsePlanRejectsGarbage()
{
    using namespace dfl::auto_input;
    Step steps[8];
    int count = 0;
    int version = 0;
    const char* bad[] = {
        "",
        "{",
        "{\"version\":2,\"steps\":[]}",                        // wrong plan version
        "{\"version\":1}",                                     // steps missing
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"fly\"}]}",
        "{\"version\":1,\"steps\":[]} tail",                   // trailing garbage
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"vk\":13},\"x\"]}",
        "{\"version\":1,\"steps\":[{\"t\":-1,\"op\":\"key\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"dur\":-5}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"text\",\"chars\":\"a\\nb\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"vk\":13,\"vk\":14}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"who\":3}]}",
        "{\"version\":1,\"steps\":["
        "{\"t\":1,\"op\":\"move\"},{\"t\":2,\"op\":\"move\"},{\"t\":3,\"op\":\"move\"},"
        "{\"t\":4,\"op\":\"move\"},{\"t\":5,\"op\":\"move\"},{\"t\":6,\"op\":\"move\"},"
        "{\"t\":7,\"op\":\"move\"},{\"t\":8,\"op\":\"move\"},{\"t\":9,\"op\":\"move\"}]}"};
    for (const char* sample : bad)
    {
        if (ParsePlan(sample, steps, 8, &count, &version))
        {
            return false;
        }
        if (count != 0)
        {
            return false;  // a rejected plan must not touch the caller's buffer
        }
    }
    return true;
}

bool TestAdvanceKeyEdgeAndFinish()
{
    using namespace dfl::auto_input;
    ClearInjectionLog();
    Step steps[kMaxSteps];
    int count = 0;
    int version = 0;

    // The brief's key-edge assertions and the injection-log assertions on one
    // timeline: a key press at t=100 held for 60 ms, then a click at t=200.
    const char* json =
        "{\"version\":1,\"steps\":["
        "{\"t\":100,\"op\":\"key\",\"vk\":13,\"dur\":60},"
        "{\"t\":200,\"op\":\"click\",\"x\":320,\"y\":240,\"dur\":80}]}";
    if (!ParsePlan(json, steps, kMaxSteps, &count, &version) || count != 2)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0) || IsFinished())
    {
        return false;
    }

    // t<100: nothing fires and the key forwards untouched.
    if (Advance(50) != 0 || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=100: the down edge; the synth table reports the press.
    if (Advance(100) != 1 || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // 100<t<160: still held, no further edges.
    if (Advance(130) != 0 || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // t=160: the up edge; the key forwards again.
    if (Advance(160) != 1 || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=200: the click's move and down land in the injection log; the up is
    // due only at t+dur. Mouse primitives are not keyboard edges.
    if (Advance(200) != 0)
    {
        return false;
    }
    InjectionRecord records[64] = {};
    if (GetInjectionLog(records, 64) != 2)
    {
        return false;
    }
    if (records[0].op != kMouseMove || records[0].x != 320 || records[0].y != 240
        || records[1].op != kMouseDown || records[1].x != 320 || records[1].y != 240)
    {
        return false;
    }
    // t=280: the click's up completes the plan -> Finished.
    if (Advance(280) != 0)
    {
        return false;
    }
    if (GetInjectionLog(records, 64) != 3)
    {
        return false;
    }
    if (records[2].op != kMouseUp || records[2].x != 320 || records[2].y != 240)
    {
        return false;
    }
    if (!IsFinished() || QueryKey(13, 0x00AB) != 0x00AB || Advance(400) != 0)
    {
        return false;
    }

    // The brief's key-only timeline: t<100 no key, 100<=t<160 down, t>=160
    // released and Finished.
    const char* keyOnly =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"key\",\"vk\":13,\"dur\":60}]}";
    if (!ParsePlan(keyOnly, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(99) != 0 || Advance(100) != 1 || Advance(159) != 0
        || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    if (Advance(160) != 1 || !IsFinished() || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }

    // Text expands serially: downs 40 ms apart, each char held until the
    // next char's down ('a' -> VK 'A', 'b' -> VK 'B').
    const char* textPlan =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"text\",\"chars\":\"ab\"}]}";
    if (!ParsePlan(textPlan, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(99) != 0 || Advance(100) != 1
        || QueryKey('A', 0x00AB) != static_cast<short>(0x8000)
        || QueryKey('B', 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=140: 'A' releases and 'B' presses inside the same pump (ups first).
    if (Advance(140) != 2
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    if (Advance(180) != 1 || !IsFinished())
    {
        return false;
    }

    // Catch-up regression: a pump gap that skips whole 40 ms slots (loading
    // screens stall the frame hook) must never release a char before its own
    // down fired, and must never end with a key stuck down.
    const char* catchUpPlan =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"text\",\"chars\":\"ab\"}]}";
    if (!ParsePlan(catchUpPlan, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(139) != 1 || QueryKey('A', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // One pump 80 ms after the previous one skips 'B's down slot entirely.
    if (Advance(220) != 2
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // The next pump releases what was already pressed ('B') and finishes
    // with an all-clear table.
    if (Advance(260) != 1
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != 0x00AB
        || !IsFinished())
    {
        return false;
    }
    return true;
}

bool TestQueryKeyForwardsOriginal()
{
    // With an empty synth table every query must forward the original value
    // untouched: the hook may never swallow the real keyboard.
    using namespace dfl::auto_input;
    return QueryKey(0x41, 0x0001) == 0x0001
        && QueryKey(0x41, static_cast<short>(0x8000)) == static_cast<short>(0x8000);
}

bool TestKeyboardDikTranslation()
{
    // DirectInput hands the keyboard state back as 256 bytes indexed by DIK
    // code: the Set-1 scan code, with bit 7 set for the E0-prefixed keys.
    // MapVirtualKeyA reports only the raw scan code on this host (measured
    // 2026-10-23: MAPVK_VK_TO_VSC_EX answers 0x004B for VK_LEFT), so the
    // extended set is enumerated in auto_input.cpp. A regression here writes
    // the arrows to 0x48/0x4B/0x4D/0x50 -- slots the client never reads --
    // which is exactly the bug that kept the character standing still.
    using namespace dfl::auto_input;
    struct Case
    {
        unsigned vk;
        unsigned char dik;
    };
    const Case cases[] = {
        {0x25, 0xCB},  // VK_LEFT     -> DIK_LEFT (extended)
        {0x26, 0xC8},  // VK_UP
        {0x27, 0xCD},  // VK_RIGHT
        {0x28, 0xD0},  // VK_DOWN
        {0x2D, 0xD2},  // VK_INSERT
        {0x2E, 0xD3},  // VK_DELETE
        {0x24, 0xC7},  // VK_HOME
        {0x23, 0xCF},  // VK_END
        {0x21, 0xC9},  // VK_PRIOR (PageUp)
        {0x22, 0xD1},  // VK_NEXT  (PageDown)
        {0xA3, 0x9D},  // VK_RCONTROL
        {0xA5, 0xB8},  // VK_RMENU
        {0x41, 0x1E},  // 'A' -- plain scan code, bit 7 clear
        {0x20, 0x39},  // VK_SPACE
        {0x1B, 0x01},  // VK_ESCAPE
        {0x0D, 0x1C},  // VK_RETURN
    };
    for (const Case& entry : cases)
    {
        if (MapVkToDik(entry.vk) != entry.dik)
        {
            return false;
        }
    }
    return true;
}

bool TestSyntheticKeyboardStateMerge()
{
    // The synthetic device's GetDeviceState (auto_input.cpp) is what the game
    // reads every frame; its wiring -- plan edge -> synth table -> DIK slot in
    // the 256-byte buffer -- is what actually moved the character on
    // 2026-10-23, and it had no automated coverage until here.
    //
    // VK_F13 is used as the probe key because no physical keyboard held during
    // a test run carries it: the merge also ORs in the real keyboard state, so
    // a common key would make the "released" assertion depend on the machine.
    using namespace dfl::auto_input;

    constexpr unsigned kProbeVk = 0x7C;  // VK_F13
    Step steps[1] = {};
    steps[0].t = 0;
    steps[0].op = kKey;
    steps[0].vk = kProbeVk;
    steps[0].dur = 100;
    if (!ArmPlan(steps, 1, 1, 0))
    {
        return false;
    }

    const unsigned char dik = MapVkToDik(kProbeVk);
    unsigned char keys[256] = {};

    // The down edge is due at t=0: the buffer must carry that DIK slot.
    Advance(10);
    BuildKeyboardState(keys);
    if ((keys[dik] & 0x80) == 0)
    {
        return false;
    }

    // Past t+dur the up edge fires and the buffer must be clean again.
    Advance(200);
    for (int i = 0; i < 256; ++i)
    {
        keys[i] = 0;
    }
    BuildKeyboardState(keys);
    return (keys[dik] & 0x80) == 0;
}
}

int main()
{
    if (!TestMemoryPatchHelpers())
    {
        return Fail("memory patch helpers failed");
    }
    if (!TestCustomStackableUseModePolicy())
    {
        return Fail("[etc] stackable mode-1 list failed");
    }
    if (!TestCustomPopupPatchConfiguration())
    {
        return Fail("custom popup announcement-title patch is disabled");
    }

    if (!TestMailboxArchiveRefreshPatchConfiguration())
    {
        return Fail("mailbox archive refresh patch is disabled");
    }
    if (!TestCeraPurchaseGatePolicy())
    {
        return Fail("Cera purchase-gate policy is invalid");
    }
    if (!TestAutoInputConfigDefaults())
    {
        return Fail("auto-input config default is not disabled");
    }
    if (!TestExternalStringTableConfiguration())
    {
        return Fail("external CP936 dnf.str configuration failed");
    }
    if (!TestRelativeOutputPathPolicy())
    {
        return Fail("relative Logs/ScreenShot output path policy failed");
    }

    if (!TestParsePlanHappyPath())
    {
        return Fail("auto-input JSON subset happy path failed");
    }
    if (!TestParsePlanRejectsGarbage())
    {
        return Fail("auto-input JSON subset parser accepted invalid input");
    }
    if (!TestAdvanceKeyEdgeAndFinish())
    {
        return Fail("auto-input timeline advance or key-edge query failed");
    }
    // Plan Task 3 step 1: this guard is expected green already (Task 2
    // forwards untouched); its value is catching a regression once the real
    // hook lands.
    if (!TestQueryKeyForwardsOriginal())
    {
        return Fail("auto-input QueryKey did not forward the original value");
    }
    if (!TestKeyboardDikTranslation())
    {
        return Fail("keyboard DIK translation lost the extended-key bit");
    }
    if (!TestSyntheticKeyboardStateMerge())
    {
        return Fail("synthetic keyboard state did not follow the plan edges");
    }

    IjlApi api{};
    if (!LoadApi(&api))
    {
        return Fail("could not load the six IJL exports");
    }

    JPEG_CORE_PROPERTIES properties{};
    if (api.Init(&properties) != IJL_OK)
    {
        return Fail("ijlInit failed");
    }
    if (properties.DIBChannels != 3
        || properties.DIBColor != IJL_BGR
        || properties.JPGChannels != 3
        || properties.JPGColor != IJL_YCBCR
        || properties.jquality != 75)
    {
        return Fail("ijlInit defaults do not match IJL 1.5");
    }

    // A top-down RGB image with four colored quadrants. The DNF_v1.0 screenshot
    // path supplies this row order with a positive DIBHeight.
    constexpr int width = 16;
    constexpr int height = 16;
    std::vector<unsigned char> rgb(width * height * 3);
    for (int y = 0; y < height; ++y)
    {
        for (int x = 0; x < width; ++x)
        {
            unsigned char* pixel =
                rgb.data() + static_cast<std::size_t>(y * width + x) * 3;
            pixel[0] = x < width / 2 ? 230 : 30;
            pixel[1] = y < height / 2 ? 220 : 20;
            pixel[2] = x < width / 2 ? 20 : 225;
        }
    }

    std::vector<unsigned char> jpeg(width * height * 6);
    properties.DIBBytes = rgb.data();
    properties.DIBWidth = width;
    properties.DIBHeight = height;
    properties.DIBPadBytes = 0;
    properties.DIBChannels = 3;
    properties.DIBColor = IJL_RGB;
    properties.JPGBytes = jpeg.data();
    properties.JPGSizeBytes = static_cast<int>(jpeg.size());
    properties.JPGWidth = width;
    properties.JPGHeight = height;
    properties.JPGChannels = 3;
    properties.JPGColor = IJL_YCBCR;
    properties.JPGSubsampling = IJL_411;
    properties.jquality = 95;

    if (api.Write(&properties, IJL_JBUFF_WRITEWHOLEIMAGE) != IJL_OK)
    {
        return Fail("buffer JPEG encoding failed");
    }
    const int jpegSize = properties.JPGSizeBytes;
    if (jpegSize < 4
        || jpeg[0] != 0xFF
        || jpeg[1] != 0xD8
        || jpeg[jpegSize - 2] != 0xFF
        || jpeg[jpegSize - 1] != 0xD9)
    {
        return Fail("encoded output is not a complete JPEG");
    }

    JPEG_CORE_PROPERTIES decoded{};
    if (api.Init(&decoded) != IJL_OK)
    {
        return Fail("decode ijlInit failed");
    }
    decoded.JPGBytes = jpeg.data();
    decoded.JPGSizeBytes = jpegSize;
    if (api.Read(&decoded, IJL_JBUFF_READPARAMS) != IJL_OK
        || decoded.JPGWidth != width
        || decoded.JPGHeight != height
        || decoded.JPGChannels != 3)
    {
        return Fail("buffer JPEG parameter read failed");
    }

    constexpr int padding = 4;
    const int decodedStride = width * 3 + padding;
    std::vector<unsigned char> bgr(
        static_cast<std::size_t>(decodedStride * height),
        0xCD);
    decoded.DIBBytes = bgr.data();
    decoded.DIBWidth = width;
    decoded.DIBHeight = height;
    decoded.DIBPadBytes = padding;
    decoded.DIBChannels = 3;
    decoded.DIBColor = IJL_BGR;
    if (api.Read(&decoded, IJL_JBUFF_READWHOLEIMAGE) != IJL_OK)
    {
        return Fail("buffer JPEG decode failed");
    }

    // Positive decode height is bottom-up, so the first row is the image bottom.
    const unsigned char* bottomLeft = bgr.data();
    if (!IsClose(bottomLeft[0], 20, 45)
        || !IsClose(bottomLeft[1], 20, 45)
        || !IsClose(bottomLeft[2], 230, 45))
    {
        return Fail("positive-height encoding or bottom-up decoding is wrong");
    }
    if (!std::all_of(
            bottomLeft + width * 3,
            bottomLeft + decodedStride,
            [](unsigned char value) { return value == 0xCD; }))
    {
        return Fail("DIB row padding was modified");
    }

    std::array<char, MAX_PATH> temporaryDirectory{};
    std::array<char, MAX_PATH> temporaryFile{};
    if (GetTempPathA(
            static_cast<DWORD>(temporaryDirectory.size()),
            temporaryDirectory.data()) == 0
        || GetTempFileNameA(
            temporaryDirectory.data(),
            "ijl",
            0,
            temporaryFile.data()) == 0)
    {
        return Fail("could not create a temporary JPEG path");
    }

    properties.JPGFile = temporaryFile.data();
    if (api.Write(&properties, IJL_JFILE_WRITEWHOLEIMAGE) != IJL_OK)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG encoding failed");
    }

    JPEG_CORE_PROPERTIES fileDecoded{};
    api.Init(&fileDecoded);
    fileDecoded.JPGFile = temporaryFile.data();
    if (api.Read(&fileDecoded, IJL_JFILE_READPARAMS) != IJL_OK
        || fileDecoded.JPGWidth != width
        || fileDecoded.JPGHeight != height)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG parameter read failed");
    }

    std::vector<unsigned char> filePixels(width * height * 3);
    fileDecoded.DIBBytes = filePixels.data();
    fileDecoded.DIBWidth = width;
    fileDecoded.DIBHeight = -height;
    fileDecoded.DIBChannels = 3;
    fileDecoded.DIBColor = IJL_BGR;
    if (api.Read(&fileDecoded, IJL_JFILE_READWHOLEIMAGE) != IJL_OK)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG decode failed");
    }
    DeleteFileA(temporaryFile.data());

    const IJLibVersion* version = api.GetLibVersion();
    if (version == nullptr
        || version->major != 1
        || version->minor != 5
        || version->build != 4)
    {
        return Fail("version export is invalid");
    }
    if (std::strcmp(api.ErrorStr(IJL_BUFFER_TOO_SMALL),
            "Output buffer too small") != 0)
    {
        return Fail("error string export is invalid");
    }
    if (api.Free(&fileDecoded) != IJL_OK
        || api.Free(&decoded) != IJL_OK
        || api.Free(&properties) != IJL_OK)
    {
        return Fail("ijlFree failed");
    }

    FreeLibrary(api.Module);
    std::puts("ijl15 compatibility tests passed");
    return 0;
}
