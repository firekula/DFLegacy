#include "bootstrap_runtime.h"
namespace dfl::client_bootstrap
{
    namespace { Mode mode = Mode::Normal; bool standalone = false; }
    Mode GetMode() noexcept { return mode; }
    bool IsStandalone() noexcept { return standalone; }
    void SetStandaloneMode(Mode value) noexcept { mode = value; standalone = true; }
}
