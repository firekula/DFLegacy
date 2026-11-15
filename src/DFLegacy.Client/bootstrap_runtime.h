#pragma once
namespace dfl::client_bootstrap
{
    enum class Mode { Normal, Test };
    Mode GetMode() noexcept;
    bool IsStandalone() noexcept;
    inline bool AllowTestInput(bool optedIn) noexcept
    {return optedIn && (!IsStandalone() || GetMode()==Mode::Test);}
}
