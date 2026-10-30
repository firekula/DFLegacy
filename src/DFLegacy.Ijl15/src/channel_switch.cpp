#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "channel_switch.h"
#include "memory_patch.h"
#include <cstring>

namespace
{
    using namespace dfl::channel_switch::detail;
    static_assert(sizeof(void*) == 4, "ACT1 layout requires Win32");
    template<class T> T Read(const unsigned char* at) noexcept
    {
        T value{}; std::memcpy(&value, at, sizeof(value)); return value;
    }
    int NativeSecure(void* value)
    {
        return reinterpret_cast<int(__thiscall*)(void*)>(0x00402030)(value);
    }
    bool Capture(void*, Selection& selection)
    {
        __try
        {
            auto* game = *reinterpret_cast<unsigned char**>(0x00B5D6F0);
            auto* network = *reinterpret_cast<void**>(0x00BF0A9C);
            if (game == nullptr) return false;
            return ReadSelection(game, Read<void*>(game + 44), network, NativeSecure, selection);
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    void Close(void*, void* network)
    {
        reinterpret_cast<void(__thiscall*)(void*)>(0x008B2810)(network);
    }
    void Arm(void*, void* channel)
    {
        // Original owner of the secure armed flag and the 500ms timer.
        reinterpret_cast<void(__thiscall*)(void*)>(0x00893480)(channel);
    }
    const Operations NativeOperations{Capture, Close, Arm};
    void __fastcall CloseHook(void* network, void*) noexcept
    {
        CloseAndArm(nullptr, network, NativeOperations);
    }
    void __fastcall ArmHook(void* channel, void*) noexcept
    {
        ArmPending(nullptr, channel, NativeOperations);
    }
    bool SameSelection(const Selection& a, const Selection& b) noexcept
    {
        return a.Game == b.Game && a.Channel == b.Channel && a.Network == b.Network
            && a.Record == b.Record && a.Group == b.Group && a.Category == b.Category
            && a.Row == b.Row && a.Metadata == b.Metadata;
    }
    bool CanArm(const Selection& s) noexcept
    {
        return s.Socket == 0 && s.Armed == 0 && s.Connecting == 0;
    }
    template<std::size_t N> bool Matches(std::uintptr_t a, const std::array<unsigned char,N>& bytes) noexcept
    {
        __try { return a != 0 && std::memcmp(reinterpret_cast<const void*>(a), bytes.data(), N) == 0; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::channel_switch::detail
{
    bool ReadSelection(void* game, void* channel, void* network,
        SecureReader secureRead, Selection& result) noexcept
    {
        result = {};
        if (game == nullptr || channel == nullptr || network == nullptr || secureRead == nullptr) return false;
        __try
        {
            auto* g = static_cast<unsigned char*>(game);
            auto* c = static_cast<unsigned char*>(channel);
            auto* n = static_cast<unsigned char*>(network);
            if (Read<void*>(g + 44) != channel || secureRead(g + 16) != 5) return false;
            const int group = Read<int>(c + 0x288);
            const int category = Read<int>(c + 0x290);
            const int row = Read<int>(c + 0x298);
            if (group < 0 || group >= 109 || category < 0 || category >= 8 || row < 0) return false;
            const auto* table = c + 0x203C + group * 0xBC + category * 0x10;
            const auto begin = Read<std::uintptr_t>(table);
            const auto end = Read<std::uintptr_t>(table + 4);
            const auto capacity = Read<std::uintptr_t>(table + 8);
            if (begin == 0 || end <= begin || capacity < end || (end - begin) % 104 != 0
                || (capacity - begin) % 104 != 0) return false;
            const auto count = (end - begin) / 104;
            // Bounds pointer arithmetic for corrupt metadata, not a gameplay quota.
            if (count > 65535 || static_cast<std::uintptr_t>(row) >= count) return false;
            const auto record = begin + static_cast<std::uintptr_t>(row) * 104;
            const auto* data = reinterpret_cast<const unsigned char*>(record);
            const int port = Read<int>(data + 84);
            if (data[64] == 0 || std::memchr(data + 64, 0, 16) == nullptr
                || port < 1 || port > 65535 || data[88] == 1) return false;
            const int armed = secureRead(c + 0x2AC);
            const int connecting = secureRead(c + 0x2EC);
            if ((armed != 0 && armed != 1) || (connecting != 0 && connecting != 1)) return false;
            result.Game = game; result.Channel = channel; result.Network = network;
            result.Socket = Read<std::uint32_t>(n + 0x1B4); result.Record = record;
            result.Group = group; result.Category = category; result.Row = row;
            result.Armed = armed; result.Connecting = connecting;
            std::memcpy(result.Metadata.data(), data, result.Metadata.size());
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { result = {}; return false; }
    }

    void CloseAndArm(void* context, void* network, const Operations& operations) noexcept
    {
        Selection before{};
        const bool eligible = operations.Capture(context, before) && before.Network == network;
        // Never suppress the original native successful EXIT close, even if a
        // snapshot is invalid or there is no selected automatic destination.
        operations.Close(context, network);
        if (!eligible) return;
        Selection after{};
        if (operations.Capture(context, after) && SameSelection(before, after) && CanArm(after))
            operations.Arm(context, after.Channel);
    }

    bool ArmPending(void* context, void* channel, const Operations& operations) noexcept
    {
        Selection current{};
        if (!operations.Capture(context, current) || current.Channel != channel || !CanArm(current)) return false;
        operations.Arm(context, channel);
        return true;
    }

    bool InstallImage(std::uintptr_t image, const std::array<std::uintptr_t, 2>& targets, PatchWriter write) noexcept
    {
        if (image == 0 || image > UINT32_MAX - 0x004F3E88) return false;
        constexpr std::array<unsigned char,8> CloseEntry{0x56,0x8B,0xF1,0x8B,0x0D,0xF0,0xD6,0xB5};
        constexpr std::array<unsigned char,9> ArmEntry{0x83,0xB9,0x98,0x02,0x00,0x00,0xFF,0x74,0x5F};
        constexpr std::array<unsigned char,11> ReaderEntry{0x53,0x8B,0x1D,0x5C,0xD4,0xCF,0x00,0x56,0x57,0x8B,0xF9};
        constexpr std::array<unsigned char,8> TimerReset{0x8B,0x41,0x1C,0x8B,0x15,0x58,0xD4,0xCF};
        constexpr std::array<unsigned char,8> TimerStart{0x55,0x8B,0xEC,0x51,0x56,0x8B,0xF1,0x8D};
        constexpr std::array<unsigned char,5> SceneGuard{0x83,0xF8,0x05,0x75,0x10};
        constexpr std::array<unsigned char,8> SocketGuard{0x39,0xB8,0xB4,0x01,0x00,0x00,0x75,0x0E};
        if (!Matches(image + 0x004B2810,CloseEntry) || !Matches(image + 0x00493480,ArmEntry)
            || !Matches(image + 0x00002030,ReaderEntry) || !Matches(image + 0x00005310,TimerReset)
            || !Matches(image + 0x004F3E80,TimerStart) || !Matches(image + 0x0000FB16,SceneGuard)
            || !Matches(image + 0x004B2CFF,SocketGuard)) return false;
        return InstallCalls({image + 0x004B2D10, image + 0x0000FB21}, targets, write);
    }

    bool InstallCalls(const std::array<std::uintptr_t, 2>& sites,
        const std::array<std::uintptr_t, 2>& targets, PatchWriter write) noexcept
    {
        const std::array<std::array<unsigned char,5>,2> original{{
            {0xE8,0x6B,0x07,0xFE,0xFF}, {0xE8,0xEA,0x2C,0x4A,0x00}}};
        std::array<std::array<unsigned char,5>,2> patches{};
        if (write == nullptr || sites[0] == 0 || sites[1] == 0 || sites[0] == sites[1]
            || targets[0] == 0 || targets[1] == 0) return false;
        for (std::size_t i=0;i<2;++i)
        {
            patches[i][0]=0xE8;
            // x86 rel32 arithmetic wraps modulo32 bits, as CALL does.
            const auto delta=static_cast<std::uint32_t>(targets[i]-sites[i]-5);
            std::memcpy(patches[i].data()+1,&delta,sizeof(delta));
        }
        if (Matches(sites[0],patches[0]) && Matches(sites[1],patches[1])) return true;
        // A mixed/foreign pair is never overwritten as though it were original.
        if (!Matches(sites[0],original[0]) || !Matches(sites[1],original[1])) return false;
        for (std::size_t i=0;i<2;++i)
        {
            if (!write(sites[i],patches[i].data(),5))
            {
                // A writer may return false after changing bytes (e.g. failed
                // protection restoration), so restore the attempted site too.
                for (std::size_t j=0;j<=i;++j) write(sites[j],original[j].data(),5);
                return false;
            }
        }
        return true;
    }
}

namespace dfl::channel_switch
{
    bool Install() noexcept
    {
        const auto image = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if (image != 0x00400000) return false;
        // The native event loop owns these callbacks; install before gameplay
        // starts. Guard existing FD_CLOSE first, then expose the ACK helper.
        return detail::InstallImage(image,
            {reinterpret_cast<std::uintptr_t>(&ArmHook),reinterpret_cast<std::uintptr_t>(&CloseHook)},
            memory_patch::WriteBytes);
    }
}
