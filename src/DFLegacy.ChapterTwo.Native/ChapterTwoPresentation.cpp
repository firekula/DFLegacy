#include "ChapterTwoPresentation.h"
#include "ChapterTwoFrameStages.h"
#include "../DFLegacy.Ijl15/src/d3d8_hooks.h"
#include "../DFLegacy.Ijl15/src/pe_import.h"
#include "../DFLegacy.Ijl15/src/window_input_scaling.h"
#include <array>
#include <cwchar>
#include <intrin.h>

namespace
{
    namespace presentation = dfl::presentation;
    constexpr unsigned ImageSize = 0xe57000;
    constexpr std::uintptr_t ImageBase = 0x400000;
    using Create = void* (WINAPI*)(UINT);
    using Dispatch = LRESULT (WINAPI*)(const MSG*);
    using Screen = BOOL (WINAPI*)(HWND, LPPOINT);
    Create OriginalCreate;
    Dispatch OriginalDispatch;
    Screen OriginalClientToScreen;
    presentation::Mode Filter;

    template <std::size_t Size>
    bool Matches(const unsigned char* image, unsigned offset,
        const std::array<unsigned char, Size>& expected) noexcept
    {
        return std::memcmp(image + offset, expected.data(), expected.size()) == 0;
    }

    template <typename Function>
    bool CheckImport(unsigned char* image, unsigned offset, const char* module,
        const char* symbol, Function& original) noexcept
    {
        const auto slot = dfl::pe_import::FindNamed(image, ImageSize, module, symbol);
        if (reinterpret_cast<unsigned char*>(slot) != image + offset) return false;
        const auto library = GetModuleHandleA(module);
        const auto expected = library ? GetProcAddress(library, symbol) : nullptr;
        static_assert(sizeof(Function) == sizeof(expected));
        if (!expected || std::memcmp(slot, &expected, sizeof(expected)) != 0) return false;
        std::memcpy(&original, slot, sizeof(original));
        return true;
    }

    template <typename Value>
    void Write(unsigned char* image, unsigned offset, Value value) noexcept
    {
        std::memcpy(image + offset, &value, sizeof(value));
    }

    bool SupportedSize(unsigned width, unsigned height) noexcept
    {
        // Same validated 4:3 presets as the first chapter's display settings.
        constexpr unsigned widths[]{640, 800, 960, 1024, 1280, 1600};
        for (const auto preset : widths)
            if (width == preset && height == preset * 3 / 4) return true;
        return false;
    }

    void* WINAPI CreateProxy(UINT sdk)
    {
        void* const factory = OriginalCreate(sdk);
        if (factory && !dfl::d3d8_hooks::AttachFactory(factory, {Filter}))
            OutputDebugStringA("DFLegacy Chapter Two: original D3D8 factory retained.\n");
        return factory;
    }

    LRESULT WINAPI DispatchProxy(const MSG* message)
    {
        using namespace dfl::chapter_two::performance;
        FrameStageScope scope(FrameStage::WindowDispatch);
        MSG logical{};
        if (message && dfl::window_input_scaling::Translate(*message, logical))
            return OriginalDispatch(&logical);
        return OriginalDispatch(message);
    }

    BOOL WINAPI CursorTargetToScreen(HWND window, LPPOINT point)
    {
        const auto caller = reinterpret_cast<std::uintptr_t>(_ReturnAddress());
        if (point && (caller == ImageBase + 0x642285 || caller == ImageBase + 0x642ae0))
            dfl::window_input_scaling::LogicalToClient(window, *point);
        return OriginalClientToScreen(window, point);
    }

    unsigned ReadDimension(const wchar_t* name) noexcept
    {
        wchar_t value[8]{};
        const auto length = GetEnvironmentVariableW(name, value, _countof(value));
        if (!length || length >= _countof(value)) return 0;
        unsigned parsed = 0;
        for (unsigned index = 0; index < length; ++index)
        {
            if (value[index] < L'0' || value[index] > L'9') return 0;
            parsed = parsed * 10 + (value[index] - L'0');
        }
        return parsed;
    }
}

bool dfl::chapter_two::InstallPresentation(unsigned char* image, std::size_t bytes,
    presentation::Mode mode, unsigned width, unsigned height) noexcept
{
    static_assert(sizeof(void*) == 4, "Chapter Two host must be x86.");
    if (!image || bytes != ImageSize || mode == presentation::Mode::Legacy ||
        !SupportedSize(width, height)) return false;

    // The host already verifies the image header and extent. Check every
    // instruction and every import used here before editing the mapped image.
    constexpr std::array<unsigned char, 15> outerWindow{
        0x68, 0xe0, 0x01, 0, 0, 0x68, 0x80, 0x02, 0, 0, 0xe8, 0x75, 0x17, 0x63, 0};
    constexpr std::array<unsigned char, 15> logicalD3DFrame{
        0x68, 0xe0, 0x01, 0, 0, 0x68, 0x80, 0x02, 0, 0, 0xe8, 0xac, 0x27, 0x63, 0};
    constexpr std::array<unsigned char, 7> defaultWidth{0xc7, 0x46, 0x2c, 0x20, 0x03, 0, 0};
    constexpr std::array<unsigned char, 7> defaultHeight{0xc7, 0x46, 0x30, 0x58, 0x02, 0, 0};
    constexpr std::array<unsigned char, 6> d3dCreate{0xff, 0x25, 0xc8, 0xa5, 0xae, 0};
    constexpr std::array<unsigned char, 6> dispatch{0xff, 0x15, 0xd8, 0xa3, 0xae, 0};
    constexpr std::array<unsigned char, 20> automaticCursor{
        0xff, 0x15, 0x6c, 0xa4, 0xae, 0, 0x8b, 0x55, 0xfc, 0x8b, 0x45, 0xf8, 0x52, 0x50,
        0xff, 0x15, 0x68, 0xa4, 0xae, 0};
    constexpr std::array<unsigned char, 20> selectionCursor{
        0xff, 0x15, 0x6c, 0xa4, 0xae, 0, 0x8b, 0x55, 0xf8, 0x8b, 0x45, 0xf4, 0x52, 0x50,
        0xff, 0x15, 0x68, 0xa4, 0xae, 0};
    if (!Matches(image, 0xb62c, outerWindow) || !Matches(image, 0xb655, logicalD3DFrame) ||
        !Matches(image, 0x63d7a0, defaultWidth) || !Matches(image, 0x63d7ac, defaultHeight) ||
        !Matches(image, 0x681b9e, d3dCreate) || !Matches(image, 0x63ced0, dispatch) ||
        !Matches(image, 0x63d050, dispatch) || !Matches(image, 0x64227f, automaticCursor) ||
        !Matches(image, 0x642ada, selectionCursor)) return false;

    constexpr unsigned createSlot = 0x6ea5c8, dispatchSlot = 0x6ea3d8, screenSlot = 0x6ea46c;
    Create create{};
    Dispatch dispatchMessage{};
    Screen clientToScreen{};
    if (!CheckImport(image, createSlot, "d3d8.dll", "Direct3DCreate8", create) ||
        !CheckImport(image, dispatchSlot, "user32.dll", "DispatchMessageA", dispatchMessage) ||
        !CheckImport(image, screenSlot, "user32.dll", "ClientToScreen", clientToScreen)) return false;

    OriginalCreate = create;
    OriginalDispatch = dispatchMessage;
    OriginalClientToScreen = clientToScreen;
    Filter = mode;
    Write(image, createSlot, &CreateProxy);
    Write(image, dispatchSlot, &DispatchProxy);
    Write(image, screenSlot, &CursorTargetToScreen);
    Write(image, 0xb62d, height);
    Write(image, 0xb632, width);
    Write(image, 0x63d7a3, width);
    Write(image, 0x63d7af, height);
    return true;
}

void dfl::chapter_two::ConfigurePresentation(unsigned char* image, std::size_t bytes) noexcept
{
    char filter[16]{};
    const auto length = GetEnvironmentVariableA("DFL_S1C2_PRESENTATION_FILTER", filter, _countof(filter));
    if (!length) return;
    const auto mode = length < _countof(filter) ? presentation::ParseMode(filter) : presentation::Mode::Legacy;
    const auto width = ReadDimension(L"DFL_S1C2_WINDOW_WIDTH");
    const auto height = ReadDimension(L"DFL_S1C2_WINDOW_HEIGHT");
    if (InstallPresentation(image, bytes, mode, width, height))
        OutputDebugStringA("DFLegacy Chapter Two: enhanced D3D8 display adapter installed.\n");
    else
        OutputDebugStringA("DFLegacy Chapter Two: using original display settings.\n");
}
