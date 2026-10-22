#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "auto_input.h"
#include "ijl15_config.h"

namespace dfl::auto_input
{
    bool IsEnabled() noexcept
    {
        return dfl::ijl15_config::Get().EnableAutoInput;
    }

    void Pump() noexcept
    {
    }

    bool Install() noexcept
    {
        // Task 3 replaces this with the address checks and real hooks.
        return true;
    }

    void Restore() noexcept
    {
    }
}
