#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../../DFLegacy.Client/client_paths.h"

#include <cstdio>
#include <cstring>
#include <cwchar>

#include "channel_reentry.h"
#include "memory_patch.h"

namespace
{
    using namespace dfl::channel_reentry::detail;
    MtuCache Cache;

    bool Eligible(const ProbeContext& context) noexcept
    {
        return context.Manager != 0 && context.LocalIpv4 != 0
            && context.PrimaryPort > 0 && context.PrimaryPort <= 65535
            && context.SecondaryPort > 0 && context.SecondaryPort <= 65535
            && context.HostCount == 1 && context.HostLength > 0
            && context.HostLength < context.Host.size()
            && context.Host[context.HostLength] == '\0';
    }
    bool SameRoute(const ProbeContext& a, const ProbeContext& b) noexcept
    {
        return a.Manager == b.Manager && a.LocalIpv4 == b.LocalIpv4
            && a.PrimaryPort == b.PrimaryPort && a.SecondaryPort == b.SecondaryPort
            && a.HostLength == b.HostLength
            && std::memcmp(a.Host.data(), b.Host.data(), a.HostLength) == 0;
    }
    void SelectNativeHost(void* manager)
    {
        reinterpret_cast<void(__thiscall*)(void*)>(0x0078C730)(manager);
    }
    void ProbeNativeMtu(void* manager)
    {
        reinterpret_cast<void(__thiscall*)(void*)>(0x0078C1B0)(manager);
    }
    int ReadNativeMtu(void* manager)
    {
        return *reinterpret_cast<const int*>(static_cast<const unsigned char*>(manager) + 0x1B4);
    }
    void WriteNativeMtu(void* manager, int mtu)
    {
        *reinterpret_cast<int*>(static_cast<unsigned char*>(manager) + 0x1B4) = mtu;
    }
    void LogProbe(bool reused, int mtu, ULONGLONG elapsed) noexcept
    {
        wchar_t path[MAX_PATH]{},leaf[100]{};
        if(swprintf_s(leaf,L"channel-reentry-%lu.log",GetCurrentProcessId())<0
            ||!dfl::client_paths::BuildLogPath(leaf,path,MAX_PATH))return;
        const auto file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        char line[128]{};
        const auto length = sprintf_s(line, "tick=%llu reused=%d native_mtu=%d elapsed_ms=%llu\r\n",
            GetTickCount64(), reused ? 1 : 0, mtu, elapsed);
        if (length > 0)
        {
            DWORD written{};
            WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
        }
        CloseHandle(file);
    }
    void __fastcall ProbeHook(void* manager, void*) noexcept
    {
        const auto started = GetTickCount64();
        ProbeContext context{};
        if (!ReadNativeProbeContext(manager, context))
        {
            // No invented MTU for metadata outside the verified ACT1 layout.
            // Leave that call's preexisting native behavior intact.
            Cache.Clear();
            ProbeNativeMtu(manager);
            return;
        }
        const ProbeOperations operations{SelectNativeHost, ProbeNativeMtu, ReadNativeMtu, WriteNativeMtu};
        const auto reused = ProbeWithCache(Cache, context, manager, operations);
        LogProbe(reused, ReadNativeMtu(manager), GetTickCount64() - started);
    }
    template<std::size_t Size>
    bool Matches(std::uintptr_t address, const std::array<unsigned char, Size>& expected) noexcept
    {
        __try
        {
            return std::memcmp(reinterpret_cast<const void*>(address), expected.data(), Size) == 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::channel_reentry::detail
{
    bool MtuCache::TryGet(const ProbeContext& context, int& mtu) noexcept
    {
        std::lock_guard<std::mutex> lock(gate_);
        if (!Eligible(context) || !valid_ || !SameRoute(context, context_)) return false;
        mtu = mtu_;
        return true;
    }
    void MtuCache::Record(const ProbeContext& context, int mtu) noexcept
    {
        std::lock_guard<std::mutex> lock(gate_);
        valid_ = Eligible(context) && mtu > 0 && mtu <= 1472;
        if (valid_) { context_ = context; mtu_ = mtu; }
    }
    void MtuCache::Clear() noexcept
    {
        std::lock_guard<std::mutex> lock(gate_);
        valid_ = false;
    }
    bool ProbeWithCache(MtuCache& cache, const ProbeContext& context, void* manager,
        const ProbeOperations& operations) noexcept
    {
        operations.SelectHost(manager);
        int mtu{};
        if (cache.TryGet(context, mtu))
        {
            operations.WriteMtu(manager, mtu);
            return true;
        }
        cache.Clear();
        operations.Probe(manager);
        cache.Record(context, operations.ReadMtu(manager));
        return false;
    }
    bool ReadNativeProbeContext(void* manager, ProbeContext& context) noexcept
    {
        context = {};
        __try
        {
            const auto* bytes = static_cast<const unsigned char*>(manager);
            const auto begin = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x1CC);
            const auto end = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x1D0);
            const auto capacityEnd = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x1D4);
            if (begin == 0 || end <= begin || capacityEnd < end
                || (end - begin) % 0x1C != 0
                || *reinterpret_cast<const std::uintptr_t*>(bytes + 0x1E0) == 0) return false;
            context.Manager = reinterpret_cast<std::uintptr_t>(manager);
            context.HostCount = static_cast<std::uint32_t>((end - begin) / 0x1C);
            context.LocalIpv4 = *reinterpret_cast<const std::uint32_t*>(bytes + 8);
            context.PrimaryPort = *reinterpret_cast<const std::uint32_t*>(bytes + 0x1F0);
            context.SecondaryPort = *reinterpret_cast<const std::uint32_t*>(bytes + 0x1F4);
            if (context.HostCount != 1) return true;
            const auto* record = reinterpret_cast<const unsigned char*>(begin);
            const auto length = *reinterpret_cast<const std::uint32_t*>(record + 0x14);
            const auto capacity = *reinterpret_cast<const std::uint32_t*>(record + 0x18);
            if (length == 0 || length > capacity || length >= context.Host.size()) return false;
            const auto* data = capacity < 16 ? reinterpret_cast<const char*>(record + 4)
                : *reinterpret_cast<const char* const*>(record + 4);
            if (data == nullptr || data[length] != '\0') return false;
            context.HostLength = length;
            std::memcpy(context.Host.data(), data, length + 1);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { context = {}; return false; }
    }
}

namespace dfl::channel_reentry
{
    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != 0x00400000) return false;
        constexpr std::array<unsigned char, 11> ProbeEntry{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x28, 0x53, 0x56, 0x57, 0x8B, 0xF1};
        constexpr std::array<unsigned char, 10> SelectorEntry{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08, 0x53, 0x56, 0x8B, 0xF1};
        constexpr std::array<std::uintptr_t, 2> sites{0x0078CA43, 0x0078CCAB};
        constexpr std::array<std::array<unsigned char, 5>, 2> original{{
            {0xE8, 0x68, 0xF7, 0xFF, 0xFF}, {0xE8, 0x00, 0xF5, 0xFF, 0xFF}}};
        if (!Matches(0x0078C1B0, ProbeEntry) || !Matches(0x0078C730, SelectorEntry)) return false;
        std::array<std::array<unsigned char, 5>, 2> patches{};
        for (std::size_t i = 0; i < sites.size(); ++i)
        {
            patches[i][0] = 0xE8;
            const auto delta = static_cast<std::uint32_t>(
                reinterpret_cast<std::uintptr_t>(&ProbeHook) - sites[i] - 5);
            std::memcpy(patches[i].data() + 1, &delta, sizeof(delta));
            if (!Matches(sites[i], original[i]) && !Matches(sites[i], patches[i])) return false;
        }
        for (std::size_t i = 0; i < sites.size(); ++i)
        {
            if (!memory_patch::WriteBytes(sites[i], patches[i].data(), patches[i].size()))
            {
                for (std::size_t j = 0; j < sites.size(); ++j)
                    memory_patch::WriteBytes(sites[j], original[j].data(), original[j].size());
                return false;
            }
        }
        return true;
    }
}
