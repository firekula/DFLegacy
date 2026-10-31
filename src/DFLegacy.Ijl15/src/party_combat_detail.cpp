#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "memory_patch.h"
#include "party_combat_detail.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t EarlySite = 0x0041A8B4;
    constexpr std::uintptr_t CompleteSite = 0x0041AEE0;
    constexpr std::uintptr_t SceneCompleteSite = 0x0042FC6F;
    constexpr std::uintptr_t RecoveryBranchSite = 0x0041B217;
    constexpr std::uintptr_t GameGlobal = 0x00B5D6F0;
    constexpr std::array<unsigned char, 6> OriginalEarlyCode{
        0xFF, 0x92, 0xA4, 0x04, 0x00, 0x00};
    constexpr std::array<unsigned char, 7> OriginalCompleteCode{
        0x0F, 0xB6, 0x85, 0x53, 0xFE, 0xFF, 0xFF};
    constexpr std::array<unsigned char, 7> OriginalSceneCompleteCode{
        0x5F, 0x5E, 0x5B, 0x5D, 0xC2, 0x04, 0x00};
    constexpr std::array<unsigned char, 5> OriginalRecoveryBranchCode{
        0x83, 0xF8, 0x01, 0x75, 0x15};

    thread_local dfl::party_combat_detail::detail::PendingInitialization Pending;
    thread_local dfl::party_combat_detail::detail::TransitionQueue TownPending;

    void WriteDiagnostic(const char* action, void* actor = nullptr,
        unsigned int mode = 0, int sceneState = -1) noexcept
    {
        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return;
        DWORD filename = length;
        while (filename != 0 && path[filename - 1] != L'\\') --filename;
        if (_snwprintf_s(path + filename, MAX_PATH - filename, _TRUNCATE,
                L"party-combat-detail-%lu.log", GetCurrentProcessId()) < 0) return;
        const HANDLE file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME time{};
        GetLocalTime(&time);
        char line[192]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%04u-%02u-%02u %02u:%02u:%02u.%03u T=%lu %s actor=%p mode=%u scene=%d\r\n",
            time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute,
            time.wSecond, time.wMilliseconds, GetCurrentThreadId(), action, actor, mode, sceneState);
        DWORD written = 0;
        if (size > 0) WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        CloseHandle(file);
    }

    int SceneState() noexcept
    {
        const auto game = *reinterpret_cast<const std::uintptr_t*>(GameGlobal);
        if (game == 0) return -1;
        using Read = int (__thiscall*)(const void*);
        return reinterpret_cast<Read>(0x00402030)(
            reinterpret_cast<const void*>(game + 16));
    }

    bool SupportedActor(void* actor) noexcept
    {
        // This hook is specific to the shared CNUser combat-detail parser.
        // Do not defer a different actor implementation's initialization.
        __try
        {
            if (actor == nullptr) return false;
            const auto table = *reinterpret_cast<const std::uintptr_t*>(actor);
            return *reinterpret_cast<const std::uintptr_t*>(table + 0x49C) == 0x00495D40
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x4A4) == 0x00496BC0
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x330) == 0x00652A20
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x334) == 0x00652A30;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    bool CurrentUser(const dfl::party_combat_detail::detail::TransitionInitialization& entry) noexcept
    {
        __try
        {
            const auto manager = *reinterpret_cast<void* const*>(0x00B5DCB8);
            if (manager == nullptr || entry.User == nullptr || !SupportedActor(entry.Actor))
                return false;
            using Lookup = void* (__thiscall*)(void*, unsigned int);
            const auto user = reinterpret_cast<Lookup>(0x004FDDB0)(manager, entry.UserId);
            if (user == nullptr || user != entry.User) return false;
            const auto bytes = static_cast<const unsigned char*>(user);
            return *reinterpret_cast<const std::uint16_t*>(bytes + 4) == entry.UserId
                && *reinterpret_cast<void* const*>(bytes + 0x110) == entry.Actor;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    int MainModuleState() noexcept
    {
        __try
        {
            const auto game = *reinterpret_cast<const std::uintptr_t*>(GameGlobal);
            if (game == 0) return -1;
            const auto module = *reinterpret_cast<const std::uintptr_t*>(game + 0x24);
            return module == 0 ? -1 : *reinterpret_cast<const int*>(module + 0x14);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return -1;
        }
    }

    bool PureSelection() noexcept
    {
        __try
        {
            const auto game = *reinterpret_cast<const std::uintptr_t*>(GameGlobal);
            if (game == 0 || SceneState() != 2) return false;
            const auto module = *reinterpret_cast<const std::uintptr_t*>(game + 0x20);
            if (module == 0) return false;
            const auto table = *reinterpret_cast<const std::uintptr_t*>(module);
            // NOTI30 calls 8A1290, sets +100=1 and attaches the dungeon stage
            // while scene2 remains visible. No recovery reset is safe then.
            return *reinterpret_cast<const std::uintptr_t*>(table + 4) == 0x008A2A20
                && *reinterpret_cast<const std::uintptr_t*>(table + 8) == 0x008A0EB0
                && *reinterpret_cast<const unsigned char*>(module + 0x100) == 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    void __cdecl Recalculate(void* actor, bool initializeHp)
    {
        const auto table = *reinterpret_cast<const std::uintptr_t*>(actor);
        using Function = void (__thiscall*)(void*, unsigned char);
        reinterpret_cast<Function>(
            *reinterpret_cast<const std::uintptr_t*>(table + 0x4A4))(
                actor, initializeHp ? 1 : 0);
    }

    const dfl::party_combat_detail::detail::ClientFunctions Functions{&Recalculate};

    void __cdecl RecalculateAfterTown(void* actor, bool initializeHp)
    {
        Recalculate(actor, initializeHp);
        WriteDiagnostic("initialized-after-town-transition", actor);
    }

    const dfl::party_combat_detail::detail::ClientFunctions TownFunctions{&RecalculateAfterTown};

    unsigned int __stdcall RecoveryContinuation(int sceneState) noexcept
    {
        // Town keeps the exact original virtual call. Only the new, validated
        // pure-selection path invokes it here and then skips that old call.
        if (sceneState == 1) return 0x0041B21C;
        if (sceneState == 2)
        {
            void* const actor = *reinterpret_cast<void* const*>(0x00B75A14);
            const bool applied = dfl::party_combat_detail::detail::ApplySelectionRecovery(
                actor, sceneState, SupportedActor(actor) && PureSelection(), Functions);
            WriteDiagnostic(applied ? "selection-recovery-initialized"
                : "selection-recovery-preserved", actor, 0, sceneState);
        }
        return 0x0041B231;
    }

    void __stdcall BeginNativeDetail(void* actor, void* parserFrame,
        unsigned int nativeInitializeHp) noexcept
    {
        const auto* frame = static_cast<const unsigned char*>(parserFrame);
        const auto mode = frame[-0x1AD];
        void* const frameActor = *reinterpret_cast<void* const*>(frame - 0x4C8);
        const int sceneState = SceneState();
        dfl::party_combat_detail::detail::BeginDetail(Pending, actor, parserFrame,
            frameActor == actor && SupportedActor(actor) ? sceneState : -1,
            mode, (nativeInitializeHp & 0xFF) != 0, Functions, PureSelection());
    }

    void __stdcall CompleteNativeDetail(void* parserFrame) noexcept
    {
        const auto* frame = static_cast<const unsigned char*>(parserFrame);
        void* const actor = *reinterpret_cast<void* const*>(frame - 0x4C8);
        const auto mode = frame[-0x1AD];
        const int sceneState = SupportedActor(actor) ? SceneState() : -1;
        if (dfl::party_combat_detail::detail::CompleteDetail(Pending, actor,
                parserFrame, sceneState, mode, Functions, PureSelection()))
        {
            WriteDiagnostic("initialized-after-equipment", actor, mode, sceneState);
        }
        else
        {
            dfl::party_combat_detail::detail::TransitionInitialization entry;
            entry.Actor = actor;
            entry.User = *reinterpret_cast<void* const*>(frame - 0x1C0);
            entry.UserId = *reinterpret_cast<const std::uint16_t*>(frame - 0x1BC);
            entry.Mode = mode;
            entry.QueuedAtMs = GetTickCount();
            if (CurrentUser(entry)
                && dfl::party_combat_detail::detail::QueueTownTransition(
                    TownPending, entry, SceneState(), MainModuleState()))
            {
                WriteDiagnostic("queued-town-transition", actor, mode);
            }
        }
    }

    void __stdcall CompleteNativeScene(void* game, int oldScene, int newScene) noexcept
    {
        if (game != *reinterpret_cast<void* const*>(GameGlobal)) return;
        dfl::party_combat_detail::detail::CompleteTownTransition(TownPending,
            oldScene, newScene, SceneState(), GetTickCount(), &CurrentUser, TownFunctions);
    }

    // Replaces a six-byte virtual call. Its one stack argument and ECX match
    // the original thiscall; the helper forwards that call exactly once.
    __declspec(naked) void EarlyHook()
    {
        __asm
        {
            push dword ptr [esp + 4]
            push ebp
            push ecx
            call BeginNativeDetail
            ret 4
        }
    }

    __declspec(naked) void CompleteHook()
    {
        __asm
        {
            pushfd
            pushad
            push ebp
            call CompleteNativeDetail
            popad
            popfd
            // Complete displaced movzx, then resume the original mode-3 test.
            movzx eax, byte ptr [ebp - 1ADh]
            push 0041AEE7h
            ret
        }
    }

    // 42FC10 has already stored the secure scene and finished 42F110's Town
    // setup. EDI remains GameState, EBX the old scene, [EBP+8] the new scene.
    __declspec(naked) void SceneCompleteHook()
    {
        __asm
        {
            pushfd
            pushad
            push dword ptr [ebp + 8]
            push ebx
            push edi
            call CompleteNativeScene
            popad
            popfd
            pop edi
            pop esi
            pop ebx
            pop ebp
            ret 4
        }
    }

    // The displaced cmp/jne only chooses these two continuations. Keep the
    // original scene1 call, add one pure-selection reset, preserve all state,
    // and replay cmp so flags at either continuation match the native code.
    __declspec(naked) void RecoveryBranchHook()
    {
        __asm
        {
            pushfd
            pushad
            push eax
            call RecoveryContinuation
            cmp eax, 0041B21Ch
            je OriginalInitialize
            popad
            popfd
            cmp eax, 1
            push 0041B231h
            ret
        OriginalInitialize:
            popad
            popfd
            cmp eax, 1
            push 0041B21Ch
            ret
        }
    }

    template<std::size_t Size>
    bool Matches(std::uintptr_t address,
        const std::array<unsigned char, Size>& expected) noexcept
    {
        __try
        {
            return std::memcmp(reinterpret_cast<const void*>(address),
                expected.data(), expected.size()) == 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    template<std::size_t Size>
    std::array<unsigned char, Size> Branch(
        unsigned char opcode, std::uintptr_t site, const void* target) noexcept
    {
        std::array<unsigned char, Size> patch{};
        patch.fill(0x90);
        patch[0] = opcode;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(target) - site - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        return patch;
    }
}

namespace dfl::party_combat_detail
{
    bool detail::ApplySelectionRecovery(void* actor, int sceneState,
        bool pureSelection, const ClientFunctions& functions) noexcept
    {
        if (sceneState != 2 || !pureSelection || actor == nullptr
            || functions.Recalculate == nullptr) return false;
        functions.Recalculate(actor, true);
        return true;
    }

    bool detail::QueueTownTransition(TransitionQueue& queue,
        const TransitionInitialization& entry, int sceneState,
        int mainModuleState) noexcept
    {
        if (sceneState != 3 || mainModuleState != 4 || entry.Actor == nullptr
            || entry.User == nullptr || (entry.Mode != 1 && entry.Mode != 3))
            return false;
        for (std::size_t index = 0; index < queue.Count; ++index)
        {
            if (queue.Entries[index].UserId == entry.UserId)
            {
                queue.Entries[index] = entry;
                return true;
            }
        }
        if (queue.Count >= queue.Entries.size()) return false;
        queue.Entries[queue.Count++] = entry;
        return true;
    }

    unsigned int detail::CompleteTownTransition(TransitionQueue& queue,
        int oldScene, int newScene, int actualScene, std::uint32_t nowMs,
        ValidateTransition validate, const ClientFunctions& functions) noexcept
    {
        const TransitionQueue before = queue;
        queue = {};
        if (oldScene != 3 || newScene != 1 || actualScene != 1
            || validate == nullptr || functions.Recalculate == nullptr)
            return 0;
        unsigned int initialized = 0;
        for (std::size_t index = 0; index < before.Count; ++index)
        {
            const auto& entry = before.Entries[index];
            // The native fade is 500 ms. Drop an abandoned transition rather
            // than carrying a reset into a much later return or user lifetime.
            if (nowMs - entry.QueuedAtMs > 10000 || !validate(entry)) continue;
            functions.Recalculate(entry.Actor, true);
            ++initialized;
        }
        return initialized;
    }

    void detail::BeginDetail(PendingInitialization& pending, void* actor,
        void* parserFrame, int sceneState, std::uint8_t mode,
        bool nativeInitializeHp, const ClientFunctions& functions,
        bool pureSelection) noexcept
    {
        pending = {};
        if (functions.Recalculate == nullptr || actor == nullptr) return;
        const bool detailRecord = parserFrame != nullptr && (mode == 1 || mode == 3);
        const bool initialize = nativeInitializeHp
            && !(detailRecord && sceneState == 2 && !pureSelection);
        const bool defer = initialize && (sceneState == 1 || (sceneState == 2 && pureSelection))
            && detailRecord;
        if (defer) pending = {actor, parserFrame, mode, sceneState};
        functions.Recalculate(actor, initialize && !defer);
    }

    bool detail::CompleteDetail(PendingInitialization& pending, void* actor,
        void* parserFrame, int sceneState, std::uint8_t mode,
        const ClientFunctions& functions, bool pureSelection) noexcept
    {
        const PendingInitialization before = pending;
        pending = {};
        if ((sceneState != 1 && !(sceneState == 2 && pureSelection))
            || actor == nullptr || parserFrame == nullptr
            || (mode != 1 && mode != 3) || functions.Recalculate == nullptr
            || before.Actor != actor || before.ParserFrame != parserFrame
            || before.Mode != mode || before.SceneState != sceneState)
        {
            return false;
        }
        functions.Recalculate(actor, true);
        return true;
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != ImageBase)
            return false;
        const auto earlyPatch = Branch<6>(0xE8, EarlySite,
            reinterpret_cast<const void*>(&EarlyHook));
        const auto completePatch = Branch<7>(0xE9, CompleteSite,
            reinterpret_cast<const void*>(&CompleteHook));
        const auto scenePatch = Branch<7>(0xE9, SceneCompleteSite,
            reinterpret_cast<const void*>(&SceneCompleteHook));
        const auto recoveryPatch = Branch<5>(0xE9, RecoveryBranchSite,
            reinterpret_cast<const void*>(&RecoveryBranchHook));
        if (Matches(EarlySite, earlyPatch) && Matches(CompleteSite, completePatch)
            && Matches(SceneCompleteSite, scenePatch) && Matches(RecoveryBranchSite, recoveryPatch))
            return true;
        constexpr std::array<unsigned char, 6> StatSetter{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x10};
        // Recalculation reloads the base stat blocks and folds the current
        // equipment into them; it does not accumulate item bonuses each call.
        constexpr std::array<unsigned char, 6> Recalculation{
            0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68};
        constexpr std::array<unsigned char, 11> SceneReader{
            0x53, 0x8B, 0x1D, 0x5C, 0xD4, 0xCF, 0x00,
            0x56, 0x57, 0x8B, 0xF9};
        constexpr std::array<unsigned char, 3> FollowingModeTest{0x83, 0xF8, 0x03};
        constexpr std::array<unsigned char, 5> PreviousSceneCallback{0xE8, 0xA1, 0xF4, 0xFF, 0xFF};
        constexpr std::array<unsigned char, 8> UserLookup{
            0x55, 0x8B, 0xEC, 0x51, 0x0F, 0xB7, 0x45, 0x08};
        constexpr std::array<unsigned char, 5> SelectionUpdateCode{
            0x55, 0x8B, 0xEC, 0x83, 0xEC};
        constexpr std::array<unsigned char, 7> SelectionHandoffCode{
            0xC6, 0x86, 0x00, 0x01, 0x00, 0x00, 0x01};
        if (!Matches(EarlySite, OriginalEarlyCode)
            || !Matches(CompleteSite, OriginalCompleteCode)
            || !Matches(SceneCompleteSite, OriginalSceneCompleteCode)
            || !Matches(RecoveryBranchSite, OriginalRecoveryBranchCode)
            || !Matches(0x00495D40, StatSetter)
            || !Matches(0x00496BC0, Recalculation)
            || !Matches(0x00402030, SceneReader)
            || !Matches(0x0041AEE7, FollowingModeTest)
            || !Matches(0x0042FC6A, PreviousSceneCallback)
            || !Matches(0x004FDDB0, UserLookup)
            || !Matches(0x008A2A20, SelectionUpdateCode)
            || !Matches(0x008A1299, SelectionHandoffCode))
        {
            // Recover a partial installation owned by this exact module.
            if (Matches(EarlySite, earlyPatch)) memory_patch::WriteBytes(
                EarlySite, OriginalEarlyCode.data(), OriginalEarlyCode.size());
            if (Matches(CompleteSite, completePatch)) memory_patch::WriteBytes(
                CompleteSite, OriginalCompleteCode.data(), OriginalCompleteCode.size());
            if (Matches(SceneCompleteSite, scenePatch)) memory_patch::WriteBytes(
                SceneCompleteSite, OriginalSceneCompleteCode.data(), OriginalSceneCompleteCode.size());
            if (Matches(RecoveryBranchSite, recoveryPatch)) memory_patch::WriteBytes(
                RecoveryBranchSite, OriginalRecoveryBranchCode.data(), OriginalRecoveryBranchCode.size());
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        // Install the inert completion hook first. A partial install never
        // leaves the early reset suppressed without its matching completion.
        if (!memory_patch::WriteBytes(CompleteSite, completePatch.data(), completePatch.size())
            || !memory_patch::WriteBytes(SceneCompleteSite, scenePatch.data(), scenePatch.size())
            || !memory_patch::WriteBytes(RecoveryBranchSite, recoveryPatch.data(), recoveryPatch.size())
            || !memory_patch::WriteBytes(EarlySite, earlyPatch.data(), earlyPatch.size()))
        {
            memory_patch::WriteBytes(EarlySite,
                OriginalEarlyCode.data(), OriginalEarlyCode.size());
            memory_patch::WriteBytes(CompleteSite,
                OriginalCompleteCode.data(), OriginalCompleteCode.size());
            memory_patch::WriteBytes(SceneCompleteSite,
                OriginalSceneCompleteCode.data(), OriginalSceneCompleteCode.size());
            memory_patch::WriteBytes(RecoveryBranchSite,
                OriginalRecoveryBranchCode.data(), OriginalRecoveryBranchCode.size());
            WriteDiagnostic("install-failed");
            return false;
        }
        WriteDiagnostic("installed-41A8B4-41AEE0-42FC6F-41B217");
        return true;
    }
}
