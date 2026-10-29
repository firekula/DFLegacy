#pragma once
#include "client_state.h"
#include "client_navigation.h"

namespace dfl::client_map_feedback
{
    // Feedback appends to the original NMap owner draw callback (88187E -> 8814A0).
    void Initialize(client_state::Reader,const wchar_t* directory) noexcept;
    void AcceptGoal(const client_state::Snapshot&,const client_navigation::CanvasGoal&) noexcept;
    void ObserveFrame(const client_state::Snapshot&,bool navigationHooksReady) noexcept;
    void PumpWorker() noexcept;
    // False keeps game-thread observations enabled for cleanup. True reports the current
    // hook/resource/callback drain only. Before unloading the DLL, the host must stop new
    // DebugRender entry and establish a quiescent boundary; arbitrary concurrent FreeLibrary
    // is not made safe by this module's counters or hook lock.
    bool Shutdown() noexcept;
}
