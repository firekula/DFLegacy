#include "channel_reentry.h"

#include <array>
#include <cstdio>
#include <cstring>

namespace
{
    using namespace dfl::channel_reentry::detail;
    struct NativeState
    {
        int Mtu{-1};
        int ProbeCount{};
        int SelectCount{};
        int ProbeResult{1024};
        bool HostRefreshed{};
    };
    void Select(void* value)
    {
        auto& state = *static_cast<NativeState*>(value);
        ++state.SelectCount;
        state.HostRefreshed = true;
    }
    void Probe(void* value)
    {
        auto& state = *static_cast<NativeState*>(value);
        ++state.ProbeCount;
        state.Mtu = state.HostRefreshed ? state.ProbeResult : -1;
    }
    int ReadMtu(void* value) { return static_cast<NativeState*>(value)->Mtu; }
    void WriteMtu(void* value, int mtu) { static_cast<NativeState*>(value)->Mtu = mtu; }
    const ProbeOperations Operations{Select, Probe, ReadMtu, WriteMtu};
    ProbeContext Context()
    {
        ProbeContext context{};
        context.Manager = 0x12340000;
        context.LocalIpv4 = 0xB103A8C0;
        context.PrimaryPort = 7002;
        context.SecondaryPort = 7003;
        context.HostCount = 1;
        const char host[] = "192.168.3.177";
        context.HostLength = sizeof(host) - 1;
        std::memcpy(context.Host.data(), host, sizeof(host));
        return context;
    }
    bool Check(bool condition, const char* message)
    {
        if (!condition) std::fprintf(stderr, "FAIL: channel reentry: %s\n", message);
        return condition;
    }
}

bool TestChannelReentryMtu()
{
    bool ok = true;
    MtuCache cache;
    NativeState state;
    auto context = Context();
    const auto initial = ProbeWithCache(cache, context, &state, Operations);
    state.Mtu = -1; // NOTI1 rebuilds the host list and invalidates native MTU.
    state.HostRefreshed = false;
    const auto rejoined = ProbeWithCache(cache, context, &state, Operations);
    ok &= Check(!initial && rejoined && state.Mtu == 1024 && state.ProbeCount == 1,
        "same endpoint restores the actual MTU without another blocking native probe");
    ok &= Check(state.SelectCount == 2 && state.HostRefreshed,
        "cached return still refreshes the native selected-host binding");

    for (int field = 0; field < 5; ++field)
    {
        MtuCache changedCache;
        NativeState changed;
        auto route = Context();
        ProbeWithCache(changedCache, route, &changed, Operations);
        switch (field)
        {
        case 0: ++route.Manager; break;
        case 1: ++route.LocalIpv4; break;
        case 2: ++route.PrimaryPort; break;
        case 3: ++route.SecondaryPort; break;
        default: route.Host[12] = '8'; break;
        }
        changed.ProbeResult = 768;
        ok &= Check(!ProbeWithCache(changedCache, route, &changed, Operations)
            && changed.Mtu == 768 && changed.ProbeCount == 2,
            "manager, local IP, either port or host changes require a fresh native result");
    }
    for (int result : {-1, 0, 1473})
    {
        MtuCache invalidCache;
        NativeState invalid;
        invalid.ProbeResult = result;
        ProbeWithCache(invalidCache, context, &invalid, Operations);
        invalid.ProbeResult = 750;
        ok &= Check(!ProbeWithCache(invalidCache, context, &invalid, Operations)
            && invalid.Mtu == 750 && invalid.ProbeCount == 2,
            "missing or invalid results never manufacture a reusable MTU");
    }
    MtuCache multipleCache;
    NativeState multiple;
    auto multipleContext = Context();
    multipleContext.HostCount = 2;
    ProbeWithCache(multipleCache, multipleContext, &multiple, Operations);
    ok &= Check(!ProbeWithCache(multipleCache, multipleContext, &multiple, Operations)
        && multiple.ProbeCount == 2 && multiple.SelectCount == 2,
        "multiple native hosts retain native selection and probing");

    // A real legacy-layout fixture: the obsolete selected pointer is poisoned,
    // while the current owned vector contains an inline short string.
    std::array<unsigned char, 0x200> manager{};
    std::array<unsigned char, 0x1C> record{};
    unsigned char available = 1;
    auto put = [](unsigned char* base, std::size_t offset, std::uintptr_t value)
    {
        std::memcpy(base + offset, &value, sizeof(value));
    };
    std::memcpy(record.data() + 4, "192.168.3.177", 14);
    put(record.data(), 0x14, 13);
    put(record.data(), 0x18, 15);
    put(manager.data(), 8, 0xB103A8C0);
    put(manager.data(), 0x1CC, reinterpret_cast<std::uintptr_t>(record.data()));
    put(manager.data(), 0x1D0, reinterpret_cast<std::uintptr_t>(record.data() + record.size()));
    put(manager.data(), 0x1D4, reinterpret_cast<std::uintptr_t>(record.data() + record.size()));
    put(manager.data(), 0x1E0, reinterpret_cast<std::uintptr_t>(&available));
    put(manager.data(), 0x1EC, 0xCDCDCDCD);
    put(manager.data(), 0x1F0, 7002);
    put(manager.data(), 0x1F4, 7003);
    ProbeContext read{};
    ok &= Check(ReadNativeProbeContext(manager.data(), read)
        && std::strcmp(read.Host.data(), "192.168.3.177") == 0
        && read.PrimaryPort == 7002 && read.SecondaryPort == 7003
        && read.LocalIpv4 == 0xB103A8C0 && read.HostCount == 1,
        "context reads fresh owned strings without dereferencing the stale selected pointer");
    return ok;
}
