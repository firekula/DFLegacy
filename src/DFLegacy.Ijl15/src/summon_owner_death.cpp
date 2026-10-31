#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../../DFLegacy.Client/client_paths.h"

#include <array>
#include <cstdio>
#include <cstring>

#include "memory_patch.h"
#include "summon_owner_death.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t LifeFalseSite = 0x0042082D;
    constexpr std::uintptr_t MapRemovalSite = 0x004C6700;
    constexpr std::uintptr_t KindGetter = 0x004073D0;
    constexpr std::uintptr_t AuthorityGetter = 0x0047F480;
    constexpr std::uintptr_t OwnerSetter = 0x004AF280;
    constexpr std::uintptr_t StateSetter = 0x0042C980;
    constexpr std::uintptr_t QueueState = 0x004AB480;
    constexpr std::uintptr_t SendState = 0x004ABC60;
    constexpr std::array<unsigned char, 7> OriginalLifeFalseCode{
        0x83, 0xBD, 0x34, 0xE8, 0xFF, 0xFF, 0x01};
    constexpr std::array<unsigned char, 6> OriginalMapRemovalCode{
        0x55, 0x8B, 0xEC, 0x51, 0x53, 0x57};

    void ApplyMapOwnerLoss(void* map, void* owner, const char* action,
        const dfl::summon_owner_death::detail::RemovedOwnerSnapshot* removedOwner) noexcept;

    void WriteDiagnostic(const char* action, void* map = nullptr,
        void* owner = nullptr, std::size_t applied = 0,
        std::size_t flushed = 0) noexcept
    {
        wchar_t path[MAX_PATH]{},leaf[100]{};
        if(swprintf_s(leaf,L"summon-owner-death-%lu.log",GetCurrentProcessId())<0
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
            "%04u-%02u-%02u %02u:%02u:%02u.%03u T=%lu %s map=%p owner=%p applied=%u flushed=%u\r\n",
            time.wYear, time.wMonth, time.wDay,
            time.wHour, time.wMinute, time.wSecond, time.wMilliseconds,
            GetCurrentThreadId(), action, map, owner,
            static_cast<unsigned>(applied), static_cast<unsigned>(flushed));
        DWORD written = 0;
        if (size > 0)
        {
            WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        }
        CloseHandle(file);
    }

    bool ReadContext(void* map, void*& localActor, void*& game) noexcept
    {
        __try
        {
            game = *reinterpret_cast<void* const*>(0x00B5D6F0);
            localActor = *reinterpret_cast<void* const*>(0x00B75A14);
            const auto ui = *reinterpret_cast<const std::uintptr_t*>(0x00B5DCE0);
            const auto stage = ui == 0 ? 0
                : *reinterpret_cast<const std::uintptr_t*>(ui + 0x200010);
            return game != nullptr && localActor != nullptr && stage != 0
                && *reinterpret_cast<void* const*>(stage + 0xB0) == map;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    bool ReadMapBounds(void* map, std::uintptr_t& begin,
        std::uintptr_t& end) noexcept
    {
        __try
        {
            const auto bytes = reinterpret_cast<std::uintptr_t>(map);
            begin = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x10);
            end = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x14);
            return end >= begin && (end - begin) % sizeof(void*) == 0
                && (end - begin) / sizeof(void*) <= 4096;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    bool SupportedPlayer(void* actor) noexcept
    {
        __try
        {
            if (actor == nullptr)
            {
                return false;
            }
            const auto bytes = reinterpret_cast<std::uintptr_t>(actor);
            const auto table = *reinterpret_cast<const std::uintptr_t*>(bytes);
            return *reinterpret_cast<const std::uintptr_t*>(table + 0x34) == KindGetter
                && *reinterpret_cast<const std::uint32_t*>(bytes + 0xD0) == 273
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x49C) == 0x00495D40
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x4A4) == 0x00496BC0
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x330) == 0x00652A20
                && *reinterpret_cast<const std::uintptr_t*>(table + 0x334) == 0x00652A30;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    bool ReadSnapshot(std::uintptr_t slot, void* owner,
        dfl::summon_owner_death::detail::ActorSnapshot& result) noexcept
    {
        __try
        {
            const auto actor = *reinterpret_cast<const std::uintptr_t*>(slot);
            if (actor == 0 || actor == reinterpret_cast<std::uintptr_t>(owner))
            {
                return false;
            }
            const auto table = *reinterpret_cast<const std::uintptr_t*>(actor);
            // Method identity establishes the exact Monster field layout;
            // kind 529 on its own is insufficient for these offsets.
            if (*reinterpret_cast<const std::uintptr_t*>(table + 0x34) != KindGetter
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x138) != AuthorityGetter
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x50C) != OwnerSetter
                || *reinterpret_cast<const std::uint32_t*>(actor + 0xD0) != 529
                || *reinterpret_cast<void* const*>(actor + 0x1DC8) != owner)
            {
                return false;
            }
            const auto policy = *reinterpret_cast<const std::uintptr_t*>(actor + 0x1DD0);
            if (policy == 0)
            {
                return false;
            }
            const auto kind = *reinterpret_cast<const unsigned char*>(policy + 1);
            if ((kind != 1 && kind != 2)
                || (kind == 1 && *reinterpret_cast<const std::uintptr_t*>(table + 0x188) != StateSetter)
                || (kind == 2 && (*reinterpret_cast<const std::uintptr_t*>(table + 0x240) != QueueState
                    || *reinterpret_cast<const std::uintptr_t*>(table + 0x244) != SendState)))
            {
                return false;
            }
            result = {reinterpret_cast<void*>(actor), owner,
                *reinterpret_cast<void* const*>(actor + 0x1864), 529, kind};
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    void __cdecl LoseOwner(void* actor)
    {
        using Function = void (__thiscall*)(void*, void*);
        reinterpret_cast<Function>(OwnerSetter)(actor, nullptr);
    }

    bool __cdecl HasQueuedOwnerLoss(void* actor)
    {
        const auto bytes = reinterpret_cast<std::uintptr_t>(actor);
        return *reinterpret_cast<const std::uint32_t*>(bytes + 0x42C) == 13
            && *reinterpret_cast<const unsigned char*>(bytes + 0x429) == 0;
    }

    void __cdecl FlushQueuedOwnerLoss(void* actor)
    {
        // 4ABC60 has no stack parameters. Its EBX/EDI pushes preserve those
        // registers; UI virtual+8 consumes only protocol/type and returns 8.
        using Function = void (__thiscall*)(void*);
        reinterpret_cast<Function>(SendState)(actor);
    }

    void __stdcall BeforeMapRemoval(void* map, void* removedOwner) noexcept
    {
        // Run before the removed player's own callback and the later
        // broadcast, while the map still contains every dependent actor.
        if (!SupportedPlayer(removedOwner))
        {
            return;
        }
        void* localActor = nullptr;
        void* game = nullptr;
        if (map == nullptr || !ReadContext(map, localActor, game))
        {
            return;
        }
        using ReadSceneState = int (__thiscall*)(const void*);
        const int scene = reinterpret_cast<ReadSceneState>(0x00402030)(
            static_cast<const unsigned char*>(game) + 16);
        if (scene != 3)
        {
            return;
        }
        using ReadHp = int (__thiscall*)(void*);
        const int hp = reinterpret_cast<ReadHp>(0x00652A20)(removedOwner);
        if (hp > 0)
        {
            return;
        }
        const dfl::summon_owner_death::detail::RemovedOwnerSnapshot owner{
            removedOwner, true, hp};
        char action[64]{};
        _snprintf_s(action, sizeof(action), _TRUNCATE,
            "map-owner-loss hp=%d", hp);
        ApplyMapOwnerLoss(map, removedOwner, action, &owner);
    }

    __declspec(naked) void MapRemovalHook()
    {
        __asm
        {
            pushfd
            pushad
            // Original entry: ECX=map, [ESP+4]=removedOwner. The saved
            // flags/registers add 36 bytes, so the argument is now +28h.
            mov eax, dword ptr [esp + 028h]
            push eax
            push ecx
            call BeforeMapRemoval
            popad
            popfd

            push ebp
            mov ebp, esp
            push ecx
            push ebx
            push edi
            push 004C6706h
            ret
        }
    }

    void ApplyMapOwnerLoss(void* map, void* owner, const char* action,
        const dfl::summon_owner_death::detail::RemovedOwnerSnapshot* removedOwner) noexcept
    {
        if (map == nullptr || owner == nullptr)
        {
            return;
        }
        void* localActor = nullptr;
        void* game = nullptr;
        if (!ReadContext(map, localActor, game))
        {
            return;
        }
        using ReadSceneState = int (__thiscall*)(const void*);
        const int scene = reinterpret_cast<ReadSceneState>(0x00402030)(
            static_cast<const unsigned char*>(game) + 16);
        if (scene != 3)
        {
            return;
        }
        std::uintptr_t begin = 0;
        std::uintptr_t end = 0;
        if (!ReadMapBounds(map, begin, end))
        {
            return;
        }
        std::array<dfl::summon_owner_death::detail::ActorSnapshot, 1024> actors{};
        std::size_t count = 0;
        for (auto slot = begin; slot != end; slot += sizeof(void*))
        {
            dfl::summon_owner_death::detail::ActorSnapshot actor{};
            if (ReadSnapshot(slot, owner, actor))
            {
                if (count == actors.size())
                {
                    WriteDiagnostic("skipped-capacity", map, owner);
                    return;
                }
                actors[count++] = actor;
            }
        }
        const dfl::summon_owner_death::detail::ClientFunctions functions{
            &LoseOwner, &HasQueuedOwnerLoss, &FlushQueuedOwnerLoss};
        const auto result = removedOwner == nullptr
            ? dfl::summon_owner_death::detail::ApplyOwnerLossBeforeRemoval(
                actors.data(), count, owner, localActor, scene, functions)
            : dfl::summon_owner_death::detail::ApplyOwnerLossBeforeMapRemoval(
                actors.data(), count, *removedOwner, localActor, scene, functions);
        WriteDiagnostic(action, map, owner, result.Applied, result.Flushed);
    }

    void __stdcall BeforeLifeFalse(void* map, void* owner) noexcept
    {
        ApplyMapOwnerLoss(map, owner, "owner-loss", nullptr);
    }

    __declspec(naked) void LifeFalseHook()
    {
        __asm
        {
            pushfd
            pushad
            push dword ptr [ebp - 017D8h]
            push dword ptr [ebp - 017BCh]
            call BeforeLifeFalse
            popad
            popfd

            // Preserve the original living-count branch and all following
            // native removal, death-flag and Stage follow processing.
            cmp dword ptr [ebp - 017CCh], 1
            push 00420834h
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
}

namespace dfl::summon_owner_death
{
    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != ImageBase)
        {
            return false;
        }
        constexpr std::array<unsigned char, 8> BranchAndOwner{
            0x7E, 0x12, 0x8B, 0x8D, 0x28, 0xE8, 0xFF, 0xFF};
        constexpr std::array<unsigned char, 7> MapRemovalBody{
            0x8B, 0x7D, 0x08, 0x85, 0xFF, 0x8B, 0xD9};
        constexpr std::array<unsigned char, 12> OwnerSetterCode{
            0x55, 0x8B, 0xEC, 0x56, 0x8B, 0xF1,
            0x8B, 0x86, 0xC8, 0x1D, 0x00, 0x00};
        constexpr std::array<unsigned char, 10> SendStateCode{
            0x55, 0x8B, 0xEC, 0x6A, 0xFF,
            0x68, 0xBC, 0x16, 0x96, 0x00};
        constexpr std::array<unsigned char, 7> AuthorityGetterCode{
            0x8B, 0x81, 0x64, 0x18, 0x00, 0x00, 0xC3};
        constexpr std::array<unsigned char, 11> SceneStateReader{
            0x53, 0x8B, 0x1D, 0x5C, 0xD4, 0xCF, 0x00,
            0x56, 0x57, 0x8B, 0xF9};
        constexpr std::array<unsigned char, 11> PlayerHpGetter{
            0x81, 0xC1, 0x68, 0x11, 0x00, 0x00,
            0xE9, 0x05, 0xF6, 0xDA, 0xFF};
        std::array<unsigned char, OriginalLifeFalseCode.size()> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&LifeFalseHook) - LifeFalseSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        std::array<unsigned char, OriginalMapRemovalCode.size()> mapPatch{};
        mapPatch.fill(0x90);
        mapPatch[0] = 0xE9;
        const auto mapDisplacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&MapRemovalHook) - MapRemovalSite - 5);
        std::memcpy(mapPatch.data() + 1, &mapDisplacement,
            sizeof(mapDisplacement));
        if (Matches(LifeFalseSite, patch) && Matches(MapRemovalSite, mapPatch))
        {
            return true;
        }
        if (!Matches(LifeFalseSite, OriginalLifeFalseCode)
            || !Matches(MapRemovalSite, OriginalMapRemovalCode)
            || !Matches(0x004C6706, MapRemovalBody)
            || !Matches(0x00420834, BranchAndOwner)
            || !Matches(OwnerSetter, OwnerSetterCode)
            || !Matches(SendState, SendStateCode)
            || !Matches(AuthorityGetter, AuthorityGetterCode)
            || !Matches(0x00652A20, PlayerHpGetter)
            || !Matches(0x00402030, SceneStateReader))
        {
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        if (!memory_patch::WriteBytes(MapRemovalSite,
                mapPatch.data(), mapPatch.size())
            || !memory_patch::WriteBytes(LifeFalseSite, patch.data(), patch.size()))
        {
            memory_patch::WriteBytes(MapRemovalSite,
                OriginalMapRemovalCode.data(), OriginalMapRemovalCode.size());
            memory_patch::WriteBytes(LifeFalseSite,
                OriginalLifeFalseCode.data(), OriginalLifeFalseCode.size());
            WriteDiagnostic("install-failed");
            return false;
        }
        WriteDiagnostic("installed-42082D");
        WriteDiagnostic("installed-4C6700");
        return true;
    }
}

namespace dfl::summon_owner_death::detail
{
    Result ApplyOwnerLossBeforeMapRemoval(
        const ActorSnapshot* actors,
        std::size_t count,
        const RemovedOwnerSnapshot& removedOwner,
        void* localActor,
        int sceneState,
        const ClientFunctions& functions) noexcept
    {
        if (!removedOwner.SupportedPlayer || removedOwner.CurrentHp > 0)
        {
            return {};
        }
        return ApplyOwnerLossBeforeRemoval(actors, count, removedOwner.Actor,
            localActor, sceneState, functions);
    }

    Result ApplyOwnerLossBeforeRemoval(
        const ActorSnapshot* actors,
        std::size_t count,
        void* dyingOwner,
        void* localActor,
        int sceneState,
        const ClientFunctions& functions) noexcept
    {
        Result result;
        if (sceneState != 3 || actors == nullptr || dyingOwner == nullptr
            || localActor == nullptr || functions.LoseOwner == nullptr)
        {
            return result;
        }

        for (std::size_t index = 0; index < count; ++index)
        {
            const ActorSnapshot& actor = actors[index];
            if (actor.Actor == nullptr || actor.Actor == dyingOwner
                || actor.Kind != 529 || actor.Owner != dyingOwner
                || (actor.OwnerLossPolicy != 1 && actor.OwnerLossPolicy != 2))
            {
                continue;
            }

            // The original setter owns policy-specific state changes. In the
            // native callback, clearing authority before this setter makes
            // policy 2 fail its local-authority check.
            functions.LoseOwner(actor.Actor);
            ++result.Applied;

            // Flush only an actual locally queued owner-loss state; never
            // broadcast another owner's pending animation/action state.
            if (actor.OwnerLossPolicy == 2 && actor.Authority == localActor
                && functions.HasQueuedOwnerLoss != nullptr
                && functions.FlushQueuedOwnerLoss != nullptr
                && functions.HasQueuedOwnerLoss(actor.Actor))
            {
                functions.FlushQueuedOwnerLoss(actor.Actor);
                ++result.Flushed;
            }
        }
        return result;
    }
}
