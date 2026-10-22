#pragma once

namespace dfl::auto_input
{
    // Task 3 wires the real hooks; this task only ships the install gate.
    bool Install() noexcept;
    void Restore() noexcept;
    // Mirrors dfl::ijl15_config::Get().EnableAutoInput.
    bool IsEnabled() noexcept;
    // Task 2/3 fill this in; it stays empty in this task.
    void Pump() noexcept;
}
