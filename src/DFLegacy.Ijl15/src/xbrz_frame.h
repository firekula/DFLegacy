#pragma once
#include "xbrz.h"
#include <cstddef>
#include <cstdint>
#include <memory>
#include <vector>

namespace dfl::presentation
{
    struct XbrzWork
    {
        unsigned ChangedRows = 0;
        unsigned ScaledRows = 0;
        unsigned Workers = 0;
    };

    class XbrzFrame
    {
    public:
        using ScaleRoutine = void (*)(std::size_t, const std::uint32_t*, std::uint32_t*, int, int,
            xbrz::ColorFormat, const xbrz::ScalerCfg&, int, int);
        XbrzFrame(unsigned width, unsigned height, unsigned workers = 0, ScaleRoutine scale = &xbrz::scale);
        ~XbrzFrame();
        XbrzFrame(const XbrzFrame&) = delete;
        XbrzFrame& operator=(const XbrzFrame&) = delete;
        bool Prepare(const void* pixels, std::size_t pitch, unsigned factor, xbrz::ColorFormat format, XbrzWork& work) noexcept;
        void Commit() noexcept;
        void Discard() noexcept;
        const std::vector<std::uint32_t>& Output() const noexcept;
    private:
        struct Implementation;
        std::unique_ptr<Implementation> implementation_;
    };
}
