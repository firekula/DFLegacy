#pragma once

#include "../DFLegacy.Ijl15/src/presentation_scaling.h"
#include <cstddef>

namespace dfl::chapter_two
{
    // Call only on the writable, import-resolved game image before entering its CRT.
    bool InstallPresentation(unsigned char* image, std::size_t bytes,
        presentation::Mode mode, unsigned width, unsigned height,
        presentation::PostProcess effect = presentation::PostProcess::None) noexcept;

    // The host calls this with the original settings unless a launcher explicitly opts in.
    void ConfigurePresentation(unsigned char* image, std::size_t bytes) noexcept;
}
