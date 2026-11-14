#pragma once
#include <windows.h>
#include <cstddef>
#include <memory>

namespace dfl::chapter_two
{
    // Owns an independent offscreen device. The caller serializes Render and Clear.
    class NtscRenderer
    {
    public:
        NtscRenderer() noexcept;
        ~NtscRenderer();
        NtscRenderer(const NtscRenderer&) = delete;
        NtscRenderer& operator=(const NtscRenderer&) = delete;

        // Source and destination each contain 480 rows of 640 BGRA8 pixels.
        // A failed call leaves the destination untouched.
        HRESULT Render(const void* bgra, std::size_t sourcePitch, int mode,
            void* outputBgra, std::size_t outputPitch) noexcept;
        void Clear() noexcept;

    private:
        struct Implementation;
        std::unique_ptr<Implementation> implementation_;
    };
}
