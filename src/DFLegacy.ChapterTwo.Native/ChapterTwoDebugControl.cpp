// Debug-client-only control surface. The launcher's F11 window writes two flags
// into a per-process shared block; the host applies them. Release hosts are
// built without DFL_CHAPTER_TWO_DEBUG_CONTROL and do not link this file.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <limits>

#include "ChapterTwoDebugControl.h"
#include "ChapterTwoDebugTrace.h"
#include "test_control/CombatOverride.h"

namespace dfl::chapter_two::debug_control
{
    namespace
    {
        using U = std::uint32_t;
        template <class T>
        T Read(U address) noexcept { T value{}; std::memcpy(&value, reinterpret_cast<const void*>(address), sizeof(value)); return value; }

        HANDLE Mapping = nullptr;
        void* View = nullptr;
        std::atomic<bool> Available{false};

        // --- protected combat access (same routines as the private test host) ---
        bool Secure(U address, U& value) noexcept
        {
            const auto manager = Read<U>(0xf35af0), key = Read<U>(0xf35af4);
            const auto encoded = Read<U>(address), index = Read<U>(address + 4);
            if (!manager || index > 0x1000000 || (encoded ^ key) - address != index) return false;
            const auto first = Read<U>(manager + 0x44), second = Read<U>(manager + 0x48);
            if (!first || !second || first > UINT32_MAX - index * 4 || second > UINT32_MAX - index * 4) return false;
            const auto a = first + index * 4, b = second + index * 4;
            const auto va = Read<U>(a) ^ a ^ key, vb = Read<U>(b) ^ b ^ key;
            if (va != vb) return false;
            value = va;
            return true;
        }

        bool CombatRead(void*, U address, U& value) noexcept { return Secure(address, value); }
        bool CombatWrite(void*, U address, U value) noexcept
        {
            U previous = 0;
            if (!Secure(address, previous)) return false;
            using SetSecure = void(__thiscall*)(void*, const U*);
            reinterpret_cast<SetSecure>(0x42fac0)(reinterpret_cast<void*>(address), &value);
            return true;
        }
        constexpr test_control::CombatAccess Access{nullptr, &CombatRead, &CombatWrite};

        constexpr U SetHpAddress = 0x499850;
        constexpr unsigned char OriginalHpEntry[] = {0xe9, 0x93, 0xb0, 0xb5, 0};
        std::array<unsigned char, 5> InstalledHpEntry{};
        using SetHp = void(__thiscall*)(void*, int, bool, void*);
        SetHp OriginalSetHp = nullptr;
        test_control::CombatOverride Override{};
        U OverrideActor = 0;
        std::atomic<U> WantInvincible{0};
        std::atomic<U> WantTeleport{0};

        bool InDungeon() noexcept
        {
            U scene = 0;
            const auto game = Read<U>(0xd564ac);
            return game && Secure(game + 0x10, scene) && (scene == 3 || scene == 10);
        }

        U LocateActor() noexcept
        {
            const auto game = Read<U>(0xd564ac);
            const auto registry = Read<U>(0xd56cf4);
            if (!game || !registry) return 0;
            U scene = 0;
            if (!Secure(game + 0x10, scene)) return 0;
            const auto user = Read<U>(registry + 0x2c);
            if (!user) return 0;
            if (scene == 1)
            {
                const auto town = Read<U>(0xd7a8c0);
                if (town && Read<U>(town) == 0xaea99c && Read<U>(user + 0x164) == town) return town;
                return 0;
            }
            if (scene == 3 || scene == 10)
            {
                const auto dungeon = Read<U>(0xd7a8bc);
                if (dungeon && Read<U>(user + 0x168) == dungeon) return dungeon;
            }
            return 0;
        }

        void __fastcall SetHpGuard(void* actor, void*, int hp, bool force, void* source)
        {
            const auto address = reinterpret_cast<U>(actor);
            if (hp < test_control::CombatOverride::InvincibleValue
                && WantInvincible.load(std::memory_order_relaxed)
                && (address == OverrideActor
                    || (address == Read<U>(0xd7a8bc) && InDungeon()))) return;
            OriginalSetHp(actor, hp, force, source);
        }

        bool InstallHealthGuard() noexcept
        {
            if (OriginalSetHp)
                return std::memcmp(reinterpret_cast<void*>(SetHpAddress), InstalledHpEntry.data(), sizeof(InstalledHpEntry)) == 0;
            if (std::memcmp(reinterpret_cast<void*>(SetHpAddress), OriginalHpEntry, sizeof(OriginalHpEntry))) return false;
            DWORD access = 0;
            if (!VirtualProtect(reinterpret_cast<void*>(SetHpAddress), sizeof(OriginalHpEntry), PAGE_EXECUTE_READWRITE, &access)) return false;
            OriginalSetHp = reinterpret_cast<SetHp>(SetHpAddress + 5 + Read<std::int32_t>(SetHpAddress + 1));
            InstalledHpEntry[0] = 0xe9;
            const auto relative = reinterpret_cast<U>(&SetHpGuard) - (SetHpAddress + 5);
            std::memcpy(InstalledHpEntry.data() + 1, &relative, 4);
            std::memcpy(reinterpret_cast<void*>(SetHpAddress), InstalledHpEntry.data(), sizeof(InstalledHpEntry));
            FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(SetHpAddress), sizeof(InstalledHpEntry));
            DWORD ignored = 0;
            VirtualProtect(reinterpret_cast<void*>(SetHpAddress), sizeof(InstalledHpEntry), access, &ignored);
            return true;
        }

        // --- verified position entry points (same addresses as the private host) ---
        bool PositionSignatures() noexcept
        {
            constexpr unsigned char setter[] = {0x55,0x8b,0xec,0x8b,0x89,0xf4,0,0,0,0x5d,0xe9,0x71,0xbf,0x06,0};
            constexpr unsigned char getter[] = {0x55,0x8b,0xec,0x8b,0x89,0xf4,0,0,0,0x5d,0xe9,0xf1,0xbd,0x06,0};
            constexpr unsigned char publisher[] = {0x55,0x8b,0xec,0x83,0xec,0x50,0x56,0x8b,0xf1,0x8b,0x4e,0x74,0x85,0xc9,0x0f,0x84,0xa2,0x01,0,0,0xe8,0xd7,0x64,0xf9,0xff,0x84,0xc0};
            return !std::memcmp(reinterpret_cast<void*>(0x487050), setter, sizeof(setter))
                && !std::memcmp(reinterpret_cast<void*>(0x487070), getter, sizeof(getter))
                && !std::memcmp(reinterpret_cast<void*>(0x4f36d0), publisher, sizeof(publisher))
                && Read<unsigned char>(0x4f388a) == 0xc2 && Read<unsigned short>(0x4f388b) == 8;
        }
    }

    bool TeleportEnabled() noexcept
    {
        return Available.load(std::memory_order_relaxed) && WantTeleport.load(std::memory_order_relaxed) != 0;
    }

    bool InvincibleEnabled() noexcept
    {
        return Available.load(std::memory_order_relaxed) && WantInvincible.load(std::memory_order_relaxed) != 0;
    }

    void Start() noexcept
    {
        char enabled[4]{};
        if (GetEnvironmentVariableA("DFLEGACY_CH2_DEBUG_ITEMS", enabled, 4) != 1 || enabled[0] != '1') return;
        wchar_t name[96]{};
        std::swprintf(name, 96, L"Local\\DFLegacy.C2.DebugControl");
        Mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 64, name);
        if (!Mapping) return;
        View = MapViewOfFile(Mapping, FILE_MAP_ALL_ACCESS, 0, 0, 64);
        if (!View) return;
        std::memset(View, 0, 64);
        static_cast<unsigned char*>(View)[0] = 1; // schema version
        Available.store(true, std::memory_order_release);
        InstallHealthGuard();
    }

    void Tick() noexcept
    {
        if (!Available.load(std::memory_order_relaxed) || !View) return;
        const auto* bytes = static_cast<const unsigned char*>(View);
        WantTeleport.store((bytes[1] & 1) != 0, std::memory_order_relaxed);
        const bool wantInvincible = (bytes[1] & 2) != 0;
        WantInvincible.store(wantInvincible, std::memory_order_relaxed);
        if (!OriginalSetHp) InstallHealthGuard();

        if (!wantInvincible)
        {
            if (OverrideActor)
            {
                Override.Disable(OverrideActor, Access);
                dfl::chapter_two::debug_trace::Write(
                    "DFL_COMBAT disabled actor=%08x\n", OverrideActor);
                Override = test_control::CombatOverride{};
                OverrideActor = 0;
            }
            return;
        }

        const auto actor = LocateActor();
        if (!actor) return;
        const bool townActor = Read<U>(0xd7a8c0) == actor && Read<U>(actor) == 0xaea99c;
        if (townActor && WantTeleport.load(std::memory_order_relaxed))
        {
            // The game writes the right-click auto-path destination here. In
            // debug mode, replace that walk with an immediate same-town move.
            using Get = int(__thiscall*)(void*, int);
            const auto self = reinterpret_cast<void*>(actor);
            const int currentX = reinterpret_cast<Get>(0x487070)(self, 0);
            const int currentY = reinterpret_cast<Get>(0x487070)(self, 1);
            const int targetX = Read<int>(actor + 0x278);
            const int targetY = Read<int>(actor + 0x27c);
            static U LastFailedX = 0, LastFailedY = 0;
            if ((targetX != currentX || targetY != currentY)
                && (static_cast<U>(targetX) != LastFailedX || static_cast<U>(targetY) != LastFailedY))
            {
                if (TryTeleport(actor, targetX, targetY))
                {
                    LastFailedX = LastFailedY = 0;
                }
                else
                {
                    LastFailedX = static_cast<U>(targetX);
                    LastFailedY = static_cast<U>(targetY);
                }
            }
        }
        if (actor == Read<U>(0xd7a8c0))
        {
            // The effective combat bank only exists on the dungeon actor.
            // Town is intentionally left untouched (dungeon-only debug mode).
            if (OverrideActor)
            {
                Override.Disable(OverrideActor, Access);
                dfl::chapter_two::debug_trace::Write(
                    "DFL_COMBAT dungeon_only leave_town actor=%08x\n", OverrideActor);
                Override = test_control::CombatOverride{};
                OverrideActor = 0;
            }
            return;
        }
        static U LastNoBankActor = 0;
        if (actor != OverrideActor)
        {
            if (OverrideActor) Override.Disable(OverrideActor, Access);
            Override = test_control::CombatOverride{};
            OverrideActor = 0;
            LastNoBankActor = 0;
            if (!Override.Enable(actor, Access))
            {
                if (actor != LastNoBankActor)
                {
                    LastNoBankActor = actor;
                    dfl::chapter_two::debug_trace::Write(
                        "DFL_COMBAT no_bank actor=%08x\n", actor);
                }
                return;
            }
            OverrideActor = actor;
            dfl::chapter_two::debug_trace::Write(
                "DFL_COMBAT enabled actor=%08x\n", actor);
        }
        else
        {
            Override.Maintain(actor, Access);
        }
    }

    bool TryTeleport(U actor, int x, int y) noexcept
    {
        auto fail = [&](const char* reason) noexcept
        {
            dfl::chapter_two::debug_trace::Write(
                "DFL_TELEPORT fail=%s actor=%08x x=%d y=%d\n", reason, actor, x, y);
            return false;
        };
        if (!TeleportEnabled()) return fail("disabled");
        if (!actor) return fail("no_actor");
        if (!PositionSignatures()) return fail("signatures");
        using Set = void(__thiscall*)(void*, int, int);
        using Get = int(__thiscall*)(void*, int);
        using Publish = void(__thiscall*)(void*, bool, int);
        using Authority = bool(__thiscall*)(void*);
        auto* self = reinterpret_cast<void*>(actor);
        const bool townActor = Read<U>(0xd7a8c0) == actor && Read<U>(actor) == 0xaea99c;
        if (!reinterpret_cast<Authority>(0x489bc0)(self) && !townActor) return fail("not_local_authority");

        const auto controller = Read<U>(actor + 0xf4);
        if (!controller) return fail("no_controller");
        if (Read<U>(controller + 0x78) && Read<unsigned char>(controller + 0x7c)) return fail("controller_blocked");
        const auto vector = Read<U>(controller) ? Read<U>(controller) : controller + 8;
        const auto table = Read<U>(vector);
        if (!table) return fail("no_table");
        const auto getter = Read<U>(table), setter = Read<U>(table + 4);
        if (!((getter == 0x489be0 && setter == 0x489bf0) || (getter == 0x4f4210 && setter == 0x4f4230)))
        {
            dfl::chapter_two::debug_trace::Write(
                "DFL_TELEPORT fail=vtable getter=%08x setter=%08x\n", getter, setter);
            return false;
        }
        if (!Read<U>(0xd68ef8)) return fail("no_global");
        const auto map = Read<U>(actor + 0xfc);
        if (!map || !Read<U>(map + 0x104)) return fail("no_map_area");

        reinterpret_cast<Set>(0x487050)(self, 0, x);
        reinterpret_cast<Set>(0x487050)(self, 1, y);
        const auto appliedX = reinterpret_cast<Get>(0x487070)(self, 0);
        const auto appliedY = reinterpret_cast<Get>(0x487070)(self, 1);
        if (appliedX != x || appliedY != y)
        {
            dfl::chapter_two::debug_trace::Write(
                "DFL_TELEPORT fail=readback want=%d,%d got=%d,%d\n", x, y, appliedX, appliedY);
            return false;
        }
        reinterpret_cast<Publish>(0x4f36d0)(reinterpret_cast<void*>(controller), true, 1);
        dfl::chapter_two::debug_trace::Write(
            "DFL_TELEPORT ok actor=%08x x=%d y=%d\n", actor, x, y);
        return true;
    }
}

