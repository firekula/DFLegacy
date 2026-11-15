#pragma once
#include "d3d8_abi.h"
#include "presentation_scaling.h"
#include <memory>

namespace dfl::d3d8_presenter
{
    struct ProcessingTimes
    {
        double ReadbackUs = 0, ScaleUs = 0, UploadUs = 0;
        unsigned ScaledRows = 0, Workers = 0;
    };
    struct RenderResult
    {
        HRESULT Status;
        HRESULT RestoreStatus;
        const char* Stage;
        ProcessingTimes Times{};
    };
    // The caller owns input/output surfaces and presentation. This class owns only GPU scaling resources.
    class Presenter
    {
    public:
        Presenter(void* device, void** originalTable) noexcept;
        ~Presenter();
        Presenter(const Presenter&) = delete;
        Presenter& operator=(const Presenter&) = delete;
        RenderResult Render(void* source, void* target, const presentation::Plan& plan,
            presentation::PostProcess effect = presentation::PostProcess::None) noexcept;
        void Clear() noexcept;
    private:
        struct Implementation;
        std::unique_ptr<Implementation> implementation_;
    };
}
