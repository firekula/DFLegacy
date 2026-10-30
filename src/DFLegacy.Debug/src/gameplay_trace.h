#pragma once
#include "client_state.h"
#include "../include/debug_api.h"
namespace dfl::gameplay_trace
{
    void Initialize(const wchar_t*,const wchar_t*,client_state::Reader) noexcept;
    bool Enabled() noexcept;
    void Observe(std::uint32_t context,const void* bytes,std::uint32_t length,bool provider,std::uint32_t frame=0) noexcept;
    void ObserveGate(const debug_api::MovementGate&) noexcept;
    void Export() noexcept;
    void Shutdown() noexcept;
}
