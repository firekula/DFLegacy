#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <mutex>

namespace dfl::channel_reentry
{
    bool Install() noexcept;

    namespace detail
    {
        struct ProbeContext
        {
            std::uintptr_t Manager{};
            std::uint32_t LocalIpv4{};
            std::uint32_t PrimaryPort{};
            std::uint32_t SecondaryPort{};
            std::uint32_t HostCount{};
            std::size_t HostLength{};
            std::array<char, 260> Host{};
        };

        class MtuCache
        {
        public:
            bool TryGet(const ProbeContext& context, int& mtu) noexcept;
            void Record(const ProbeContext& context, int mtu) noexcept;
            void Clear() noexcept;
        private:
            std::mutex gate_;
            ProbeContext context_{};
            int mtu_{};
            bool valid_{};
        };

        struct ProbeOperations
        {
            void (*SelectHost)(void*);
            void (*Probe)(void*);
            int (*ReadMtu)(void*);
            void (*WriteMtu)(void*, int);
        };

        // Refresh native host ownership even on a cache hit. The ICMP probe
        // runs outside the cache mutex and remains native for unknown routes.
        bool ProbeWithCache(MtuCache& cache, const ProbeContext& context,
            void* manager, const ProbeOperations& operations) noexcept;
        bool ReadNativeProbeContext(void* manager, ProbeContext& context) noexcept;
    }
}
