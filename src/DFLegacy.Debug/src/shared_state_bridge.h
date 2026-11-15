#pragma once
#include "../../DFLegacy.Client/player_runtime.h"
namespace dfl::client_state { bool BindShared(const player_runtime::SharedState*) noexcept; player_runtime::FrameTiming SharedTiming() noexcept; }
