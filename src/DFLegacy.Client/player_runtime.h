#pragma once
#include "../DFLegacy.Debug/src/client_state.h"
namespace dfl::player_runtime
{
    struct FrameTiming { std::uint64_t captureUs=0,navigationUs=0; };
    struct SharedState
    {
        std::uint32_t size,version,snapshotSize,readerSize;
        client_state::Reader reader;
        bool (__stdcall* copyLatest)(client_state::Snapshot*) noexcept;
        void (__stdcall* frameTiming)(FrameTiming*) noexcept;
    };
    constexpr std::uint32_t SharedStateVersion=1;
    void Initialize() noexcept;
    void OnFrame() noexcept;
    void Shutdown() noexcept;
    bool DiagnosticsAllowed() noexcept;
    const SharedState* GetSharedState() noexcept;
}
