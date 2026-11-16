#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "ChapterTwoNtscRenderer.h"
#include <winternl.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <vector>

namespace
{
    struct LoadedModule
    {
        ULONG Flags;
        const UNICODE_STRING* FullName;
        const UNICODE_STRING* BaseName;
    };

    void CALLBACK ObserveModule(ULONG reason, const LoadedModule* module, void* context) noexcept
    {
        constexpr wchar_t expected[] = L"d3dcompiler_47.dll";
        if (reason != 1 || module->BaseName->Length != sizeof(expected) - sizeof(wchar_t)) return;
        for (std::size_t i = 0; i + 1 < ARRAYSIZE(expected); ++i)
        {
            auto ch = module->BaseName->Buffer[i];
            if (ch >= L'A' && ch <= L'Z') ch += L'a' - L'A';
            if (ch != expected[i]) return;
        }
        *static_cast<bool*>(context) = true;
    }
}

int main()
{
    using Register = NTSTATUS (NTAPI*)(ULONG, decltype(&ObserveModule), void*, void**);
    using Unregister = NTSTATUS (NTAPI*)(void*);
    const auto ntdll = GetModuleHandleW(L"ntdll.dll");
    const auto registerObserver = reinterpret_cast<Register>(GetProcAddress(ntdll, "LdrRegisterDllNotification"));
    const auto unregisterObserver = reinterpret_cast<Unregister>(GetProcAddress(ntdll, "LdrUnregisterDllNotification"));
    bool compilerLoaded = false;
    void* cookie = nullptr;
    if (!registerObserver || !unregisterObserver || registerObserver(0, &ObserveModule, &compilerLoaded, &cookie) != 0)
        return 2;

    constexpr unsigned width = 640, height = 480;
    std::vector<std::uint32_t> input(width * height);
    for (unsigned y = 0; y < height; ++y)
        for (unsigned x = 0; x < width; ++x)
            input[y * width + x] = 0xff000000u | ((x * 17 & 255u) << 16)
                | ((y * 11 & 255u) << 8) | ((x + y) * 7 & 255u);

    bool valid = true;
    std::array<std::vector<std::uint32_t>, 4> outputs;
    {
        dfl::chapter_two::NtscRenderer renderer;
        for (int mode = 0; mode < 4; ++mode)
        {
            auto& output = outputs[mode];
            output.resize(input.size());
            const auto hr = renderer.Render(input.data(), width * 4, mode, output.data(), width * 4);
            std::printf("CRT mode %d HRESULT %08lx\n", mode, static_cast<unsigned long>(hr));
            valid = valid && SUCCEEDED(hr) && output != input && output[width * height / 2] != 0;
            for (int previous = 0; previous < mode; ++previous)
                valid = valid && output != outputs[previous];
        }
    }
    unregisterObserver(cookie);
    if (compilerLoaded) std::puts("FAIL: the CRT renderer loaded D3DCompiler_47.dll at runtime");
    if (!valid) std::puts("FAIL: the four CRT presets must render distinct images");
    return compilerLoaded || !valid ? 1 : 0;
}
