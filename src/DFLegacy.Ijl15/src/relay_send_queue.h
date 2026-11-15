#pragma once
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <array>
#include <vector>

namespace dfl::relay_send_queue
{
    bool Install() noexcept;
    namespace detail
    {
        class Gate
        {
        public:
            std::recursive_mutex Mutex;
        };
        template<class F> auto Guarded(Gate& gate, F&& body) -> decltype(body())
        { std::lock_guard<std::recursive_mutex> lock(gate.Mutex);return body(); }
        template<class F> auto Conditional(Gate& gate, bool enabled, F&& body) -> decltype(body())
        { if(enabled)return Guarded(gate,body);return body(); }
        struct Operations
        {
            void* (*ReadCurrent)(void* core);
            int (*Send)(void* relay, const void*, unsigned);
            int (*Tick)(void* relay);
        };
        int Dispatch(Gate&, void* core, const void*, unsigned, const Operations&, bool enabled=true);
        int Tick(Gate&, void* core, const Operations&, bool enabled=true);
        using PatchWriter = bool (*)(std::uintptr_t, const void*, std::size_t) noexcept;
        constexpr std::size_t SiteCount=9;
        using Targets=std::array<std::uintptr_t,SiteCount>;
        bool InstallImage(std::uintptr_t image, const Targets&, PatchWriter write) noexcept;
        std::vector<unsigned char> BuildStub(std::size_t site, std::uintptr_t address,
            std::uintptr_t image, std::uintptr_t send, std::uintptr_t tick);
    }
}
