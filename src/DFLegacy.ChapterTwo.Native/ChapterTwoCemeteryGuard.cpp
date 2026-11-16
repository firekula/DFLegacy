#include "ChapterTwoCemeteryGuard.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <atomic>
#include <cstring>

namespace
{
    constexpr std::uintptr_t ImageBase = 0x400000;
    constexpr std::size_t ImageSize = 0xE57000;
    constexpr std::uintptr_t NativeDungeonInfo = 0x9DC9E0;
    constexpr std::uintptr_t NativeGateState = 0x5C8590;
    constexpr std::uintptr_t PacketCursor = 0xE23EE8;
    constexpr std::uintptr_t PacketRemaining = 0xE23EEC;

    enum GateAuthority : unsigned { Native = 0, Locked = 1, Open = 2 };
    std::atomic<unsigned> GateMode{Native};
    // DUNGEON_INFO and the gate notification are dispatched on the same packet stream.
    unsigned CurrentDungeon = 0;
    std::uint32_t ActiveRun = 0;

    void __fastcall CommitDungeonInfo(void* info, void*, unsigned dungeon, unsigned maze, unsigned difficulty)
    {
        GateMode.store(dungeon == 35 ? Locked : Native);
        ActiveRun = 0;
        CurrentDungeon = dungeon;
        using Commit = void(__thiscall*)(void*, unsigned, unsigned, unsigned);
        reinterpret_cast<Commit>(NativeDungeonInfo)(info, dungeon, maze, difficulty);
    }

    void __stdcall ReadGateNotification()
    {
        auto& remaining = *reinterpret_cast<int*>(PacketRemaining);
        auto& cursor = *reinterpret_cast<const unsigned char**>(PacketCursor);
        if (remaining != 9 || !cursor || std::memcmp(cursor, "CEM1", 4)) return;

        std::uint32_t run = 0;
        std::memcpy(&run, cursor + 4, sizeof(run));
        const auto action = cursor[8];
        cursor += 9;
        remaining = 0;
        if (CurrentDungeon != 35 || run == 0 || action > 2) return;
        if (ActiveRun == 0)
        {
            // Live updates can reach a joining member before their entry snapshot.
            if (action == 2) return;
            ActiveRun = run;
        }
        if (ActiveRun != run) return;
        if (action != 0) GateMode.store(Open);
    }

    __declspec(naked) void GateNotificationHook()
    {
        __asm {
            pushfd
            pushad
            call ReadGateNotification
            popad
            popfd
            push 0042F549h
            ret
        }
    }

    unsigned __stdcall ReadGateAuthority()
    {
        return GateMode.load();
    }

    using Apply = void(__thiscall*)(void*, unsigned, void*, unsigned, unsigned);

    void __fastcall ApplyPeerGateState(void* gate, void*, unsigned state, void* data, unsigned a, unsigned b)
    {
        if (state == 0 && GateMode.load() != Native) return;
        reinterpret_cast<Apply>(NativeGateState)(gate, state, data, a, b);
    }

    void __stdcall OpenCemeteryGate(unsigned char* gate)
    {
        // Execute the native state callback locally. The generic state request
        // goes through P2P, while this callback removes the collision on each client.
        if (gate[0x46C] != 0)
            reinterpret_cast<Apply>(NativeGateState)(gate, 0, nullptr, 0, 0);
    }

    __declspec(naked) void GatePredicateHook()
    {
        __asm {
            pushfd
            pushad
            call ReadGateAuthority
            test eax, eax
            jz nativePredicate
            cmp eax, 2
            jne restartTimer
            push esi
            call OpenCemeteryGate
        restartTimer:
            popad
            popfd
            push 005C890Fh
            ret
        nativePredicate:
            popad
            popfd
            mov ecx, [esi+884h]
            push 005C88D0h
            ret
        }
    }

    enum class PatchKind { Call, Jump, Pointer };
    struct Patch
    {
        std::uintptr_t Address;
        std::array<unsigned char, 8> Original;
        std::size_t Size;
        std::uintptr_t Hook;
        PatchKind Kind = PatchKind::Call;
    };

    std::array<unsigned char, 8> EncodePatch(std::uintptr_t source, const Patch& patch)
    {
        std::array<unsigned char, 8> bytes{0xE8, 0, 0, 0, 0, 0x90, 0x90, 0x90};
        if (patch.Kind == PatchKind::Pointer)
        {
            std::memcpy(bytes.data(), &patch.Hook, sizeof(patch.Hook));
            return bytes;
        }
        if (patch.Kind == PatchKind::Jump) bytes[0] = 0xE9;
        const auto relative = static_cast<std::uint32_t>(patch.Hook - source - 5);
        std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
        return bytes;
    }
}

bool dfl::chapter_two::cemetery_guard_detail::InstallImage(
    unsigned char* image, std::size_t size, PatchWriter write) noexcept
{
    static_assert(sizeof(void*) == 4, "The Chapter Two Cemetery hook requires x86.");
    if (!image || size != ImageSize || !write) return false;

    constexpr unsigned char notificationDispatch[]{0xFF, 0x24, 0x85, 0x64, 0xF5, 0x42, 0};
    constexpr unsigned char gateStateStart[]{0x55, 0x8B, 0xEC, 0x53, 0x8B, 0x5D, 0x08, 0x85, 0xDB};
    if (std::memcmp(image + (NativeGateState - ImageBase), gateStateStart, sizeof(gateStateStart))
        || std::memcmp(image + 0x1DC6A, notificationDispatch, sizeof(notificationDispatch))) return false;

    const std::array patches{
        Patch{0x423DD0, {0xE8, 0x0B, 0x8C, 0x5B, 0x00}, 5,
            reinterpret_cast<std::uintptr_t>(&CommitDungeonInfo)},
        Patch{0x42F564 + 186 * 4, {0x49, 0xF5, 0x42, 0}, 4,
            reinterpret_cast<std::uintptr_t>(&GateNotificationHook), PatchKind::Pointer},
        Patch{0xB3BED4 + 0x224, {0x90, 0x85, 0x5C, 0}, 4,
            reinterpret_cast<std::uintptr_t>(&ApplyPeerGateState), PatchKind::Pointer},
        Patch{0x5C88CA, {0x8B, 0x8E, 0x84, 0x08, 0, 0}, 6,
            reinterpret_cast<std::uintptr_t>(&GatePredicateHook), PatchKind::Jump}
    };
    std::array<std::array<unsigned char, 8>, patches.size()> replacements{};
    bool installed = false;
    for (std::size_t i = 0; i < patches.size(); ++i)
    {
        const auto& patch = patches[i];
        auto* site = image + (patch.Address - ImageBase);
        replacements[i] = EncodePatch(reinterpret_cast<std::uintptr_t>(site), patch);
        if (i == 0) installed = std::memcmp(site, replacements[i].data(), patch.Size) == 0;
        const auto& expected = installed ? replacements[i] : patch.Original;
        if (std::memcmp(site, expected.data(), patch.Size)) return false;
    }
    if (installed) return true;

    for (std::size_t i = 0; i < patches.size(); ++i)
    {
        const auto& patch = patches[i];
        auto* site = image + (patch.Address - ImageBase);
        if (write(reinterpret_cast<std::uintptr_t>(site), replacements[i].data(), patch.Size)
            && std::memcmp(site, replacements[i].data(), patch.Size) == 0)
            continue;

        for (std::size_t remaining = i + 1; remaining > 0; --remaining)
        {
            const auto& prior = patches[remaining - 1];
            write(reinterpret_cast<std::uintptr_t>(image + (prior.Address - ImageBase)),
                prior.Original.data(), prior.Size);
        }
        return false;
    }
    return true;
}

bool dfl::chapter_two::InstallCemeteryGuard(unsigned char* image, std::size_t size) noexcept
{
    return reinterpret_cast<std::uintptr_t>(image) == ImageBase
        && cemetery_guard_detail::InstallImage(image, size, memory_patch::WriteBytes);
}
