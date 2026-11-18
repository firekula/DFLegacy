// Debug-client-only control surface. The launcher's F11 window writes two flags
// into a per-process shared block; the host applies them. Release hosts are
// built without DFL_CHAPTER_TWO_DEBUG_CONTROL and do not link this file.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>

#include "ChapterTwoDebugControl.h"
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

        U LocateActor() noexcept
        {
            if (!Read<U>(0xd564ac) || !Read<U>(0xd56cf4)) return 0;
            const auto user = Read<U>(Read<U>(0xd56cf4) + 0x2c);
            if (!user) return 0;
            const auto town = Read<U>(0xd7a8c0);
            if (town && Read<U>(town) == 0xaea99c && Read<U>(user + 0x164) == town) return town;
            const auto dungeon = Read<U>(0xd7a8bc);
            if (dungeon && Read<U>(user + 0x168) == dungeon) return dungeon;
            return 0;
        }

        void __fastcall SetHpGuard(void* actor, void*, int hp, bool force, void* source)
        {
            const auto address = reinterpret_cast<U>(actor);
            if (hp < test_control::CombatOverride::InvincibleValue
                && address == OverrideActor && WantInvincible.load(std::memory_order_relaxed)) return;
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
                Override = test_control::CombatOverride{};
                OverrideActor = 0;
            }
            return;
        }

        const auto actor = LocateActor();
        if (!actor) return;
        if (actor != OverrideActor)
        {
            if (OverrideActor) Override.Disable(OverrideActor, Access);
            Override = test_control::CombatOverride{};
            OverrideActor = 0;
            if (!Override.Enable(actor, Access)) return;
            OverrideActor = actor;
        }
        else
        {
            Override.Maintain(actor, Access);
        }
    }

    bool TryTeleport(U actor, int x, int y) noexcept
    {
        if (!TeleportEnabled() || !actor || !PositionSignatures()) return false;
        using Set = void(__thiscall*)(void*, int, int);
        using Get = int(__thiscall*)(void*, int);
        using Publish = void(__thiscall*)(void*, bool, int);
        using Authority = bool(__thiscall*)(void*);
        auto* self = reinterpret_cast<void*>(actor);
        if (!reinterpret_cast<Authority>(0x489bc0)(self)) return false;

        const auto controller = Read<U>(actor + 0xf4);
        if (!controller) return false;
        if (Read<U>(controller + 0x78) && Read<unsigned char>(controller + 0x7c)) return false;
        const auto vector = Read<U>(controller) ? Read<U>(controller) : controller + 8;
        const auto table = Read<U>(vector);
        if (!table) return false;
        const auto getter = Read<U>(table), setter = Read<U>(table + 4);
        if (!((getter == 0x489be0 && setter == 0x489bf0) || (getter == 0x4f4210 && setter == 0x4f4230))) return false;
        if (!Read<U>(0xd68ef8)) return false;
        const auto map = Read<U>(actor + 0xfc);
        if (!map || !Read<U>(map + 0x104)) return false;

        reinterpret_cast<Set>(0x487050)(self, 0, x);
        reinterpret_cast<Set>(0x487050)(self, 1, y);
        if (reinterpret_cast<Get>(0x487070)(self, 0) != x || reinterpret_cast<Get>(0x487070)(self, 1) != y) return false;
        reinterpret_cast<Publish>(0x4f36d0)(reinterpret_cast<void*>(controller), true, 1);
        return true;
    }
}

