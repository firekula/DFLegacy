#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../../DFLegacy.Client/client_paths.h"

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "memory_patch.h"
#include "stage_follow.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t RemovalSite = 0x004C6932;
    constexpr std::uintptr_t UiGlobal = 0x00B5DCE0;
    constexpr std::uintptr_t GameGlobal = 0x00B5D6F0;
    constexpr std::uintptr_t LocalActorGlobal = 0x00B75A14;
    constexpr std::size_t UiStageOffset = 0x200010;
    // In sub_4C6700, EBX is the map and EDI is the removed actor. All map
    // vectors have been updated here; the next call detaches the actor.
    constexpr std::array<unsigned char, 9> OriginalRemovalCode{
        0x8B, 0x07, 0x6A, 0x00, 0x8B, 0xCF, 0xFF, 0x50, 0x50};
    constexpr std::size_t HookSize = 6;

    void WriteDiagnostic(
        const char* action,
        void* stage = nullptr,
        void* actor = nullptr,
        void* target = nullptr) noexcept
    {
        wchar_t path[MAX_PATH]{},leaf[100]{};
        if(swprintf_s(leaf,L"stage-follow-%lu.log",GetCurrentProcessId())<0
            || !dfl::client_paths::BuildLogPath(leaf,path,MAX_PATH))return;
        const HANDLE file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return;
        }
        SYSTEMTIME time{};
        GetLocalTime(&time);
        char line[256]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%04u-%02u-%02u %02u:%02u:%02u.%03u T=%lu %s stage=%p removed=%p target=%p\r\n",
            time.wYear, time.wMonth, time.wDay,
            time.wHour, time.wMinute, time.wSecond, time.wMilliseconds,
            GetCurrentThreadId(), action, stage, actor, target);
        DWORD written = 0;
        if (size > 0)
        {
            WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        }
        CloseHandle(file);
    }

    void* __cdecl SelectTarget(void* stage, void* localActor)
    {
        using Function = void* (__thiscall*)(void*, void*);
        return reinterpret_cast<Function>(0x004E3DA0)(stage, localActor);
    }

    void __cdecl SetTarget(void* stage, void* actor)
    {
        using Function = void (__thiscall*)(void*, void*);
        reinterpret_cast<Function>(0x004E3900)(stage, actor);
    }

    bool __cdecl MapContains(void* map, void* actor)
    {
        using Function = unsigned char (__thiscall*)(void*, void*);
        return reinterpret_cast<Function>(0x004C5310)(map, actor) != 0;
    }

    void* CurrentStage(void* map, void* actor) noexcept
    {
        // Actor removal also occurs during startup/scene teardown. Only read
        // the optional UI context here; never suppress an actor-method fault.
        __try
        {
            const auto ui = *reinterpret_cast<const std::uintptr_t*>(UiGlobal);
            if (ui == 0)
            {
                return nullptr;
            }
            const auto stage = *reinterpret_cast<const std::uintptr_t*>(
                ui + UiStageOffset);
            return stage != 0
                && *reinterpret_cast<void* const*>(stage
                    + dfl::stage_follow::detail::StageMapOffset) == map
                && *reinterpret_cast<void* const*>(stage
                    + dfl::stage_follow::detail::FollowTargetOffset) == actor
                ? reinterpret_cast<void*>(stage) : nullptr;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    void __stdcall RepairRemovedActor(void* map, void* actor) noexcept
    {
        // The town actor uses a different global (B75A18), and is reused
        // during map transitions. Match the native selector's dungeon gate
        // before even looking at the Stage or its target.
        const auto game = *reinterpret_cast<const std::uintptr_t*>(GameGlobal);
        if (game == 0)
        {
            return;
        }
        using ReadSceneState = int (__thiscall*)(const void*);
        const int sceneState = reinterpret_cast<ReadSceneState>(0x00402030)(
            reinterpret_cast<const void*>(game + 16));
        if (sceneState != 3)
        {
            return;
        }
        void* const stage = CurrentStage(map, actor);
        const dfl::stage_follow::detail::ClientFunctions functions{
            &SelectTarget, &SetTarget, &MapContains};
        if (dfl::stage_follow::detail::RepairFollowTarget(stage, map, actor,
                *reinterpret_cast<void* const*>(LocalActorGlobal),
                sceneState, functions))
        {
            void* target = nullptr;
            std::memcpy(&target,
                static_cast<const unsigned char*>(stage)
                    + dfl::stage_follow::detail::FollowTargetOffset,
                sizeof(target));
            WriteDiagnostic("repaired", stage, actor, target);
        }
    }

    __declspec(naked) void RemovalHook()
    {
        __asm
        {
            pushfd
            pushad
            push edi
            push ebx
            call RepairRemovedActor
            popad
            popfd

            // Replay all six displaced bytes without changing EFLAGS.
            mov eax, dword ptr [edi]
            push 0
            mov ecx, edi
            push 004C6938h
            ret
        }
    }

    template<std::size_t Size>
    bool Matches(
        std::uintptr_t address,
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
}

namespace dfl::stage_follow
{
    bool detail::RepairFollowTarget(
        void* stage,
        void* map,
        void* removedActor,
        void* localActor,
        int sceneState,
        const ClientFunctions& functions) noexcept
    {
        if (sceneState != 3)
        {
            return false;
        }
        // Local actors are temporarily removed and reused across town maps.
        // Their native transition/life handlers own the synchronous rebind;
        // spectator repair must only detach a departing remote target.
        if (stage == nullptr || map == nullptr || removedActor == nullptr
            || removedActor == localActor)
        {
            return false;
        }
        void* stageMap = nullptr;
        void* target = nullptr;
        const auto* bytes = static_cast<const unsigned char*>(stage);
        std::memcpy(&stageMap, bytes + StageMapOffset, sizeof(stageMap));
        std::memcpy(&target, bytes + FollowTargetOffset, sizeof(target));
        if (stageMap != map || target != removedActor)
        {
            return false;
        }

        // Clear first: selection can return no actor, and no code between
        // these calls may retain the removed follow target.
        functions.Set(stage, nullptr);
        void* const selected = localActor == nullptr ? nullptr
            : functions.Select(stage, localActor);
        if (selected != nullptr && selected != removedActor
            && functions.Contains(map, selected))
        {
            functions.Set(stage, selected);
        }
        return true;
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))
            != ImageBase)
        {
            return false;
        }
        // Validate the context getter, native selector, setter, membership
        // helper and complete overwritten instruction boundary for 2008DF.
        constexpr std::array<unsigned char, 7> StageGetter{
            0x8B, 0x81, 0x10, 0x00, 0x20, 0x00, 0xC3};
        constexpr std::array<unsigned char, 6> Selector{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08};
        constexpr std::array<unsigned char, 6> Setter{
            0x55, 0x8B, 0xEC, 0x8B, 0x45, 0x08};
        constexpr std::array<unsigned char, 6> Membership{
            0x55, 0x8B, 0xEC, 0x8B, 0x51, 0x10};
        constexpr std::array<unsigned char, 11> SceneStateReader{
            0x53, 0x8B, 0x1D, 0x5C, 0xD4, 0xCF, 0x00,
            0x56, 0x57, 0x8B, 0xF9};
        std::array<unsigned char, HookSize> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&RemovalHook) - RemovalSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        if (Matches(RemovalSite, patch))
        {
            return true;
        }
        if (!Matches(RemovalSite, OriginalRemovalCode)
            || !Matches(0x00438850, StageGetter)
            || !Matches(0x004E3DA0, Selector)
            || !Matches(0x004E3900, Setter)
            || !Matches(0x004C5310, Membership)
            || !Matches(0x00402030, SceneStateReader))
        {
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        if (!memory_patch::WriteBytes(RemovalSite, patch.data(), patch.size()))
        {
            memory_patch::WriteBytes(
                RemovalSite, OriginalRemovalCode.data(), HookSize);
            WriteDiagnostic("install-failed");
            return false;
        }
        WriteDiagnostic("installed-4C6932");
        return true;
    }
}
