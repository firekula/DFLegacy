#pragma once
#include "../../DFLegacy.Debug/include/debug_api.h"
namespace dfl::debug_module
{
    bool Install() noexcept;
    void Render() noexcept;
    class Pipeline
    {
    public:
        Pipeline() noexcept;
        void Mark() noexcept;
        ~Pipeline() noexcept;
    private:
        debug_api::PipelineSample sample{};
        std::uint64_t last=0;
        unsigned next=0;
        bool active=false,clockValid=true;
    };
}
