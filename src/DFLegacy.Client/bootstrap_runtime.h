#pragma once
namespace dfl::client_bootstrap
{
    enum class Mode { Normal, Test };
    Mode GetMode() noexcept;
    bool IsStandalone() noexcept;
}
