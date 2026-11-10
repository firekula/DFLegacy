// Execute C2's real BeginText/EndText against the production overlay. The fixture
// never enters the game or creates a D3D device/window. Input SHA256:
// 9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb.
#include "ChapterTwoPerformance.cpp"
#include <fstream>
#include <iterator>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char textFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    unsigned Checks = 0, Failures = 0, Draws = 0, OriginalCalls = 0;
    bool ChangeRenderer = false, OriginalChangesRenderer = false;
    struct Renderer { void** methods; unsigned char storage[0x80 - sizeof(void*)]; };
    static_assert(sizeof(Renderer) == 0x80);
    Renderer First{}, Second{};
    void* Methods[24]{};
    TextState NativeState{0, 1, -1, 1, 0, 0, 0x12345678};
    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL C2 text: %s\n", message); }
    }
    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    unsigned char& Active(Renderer& value) { return reinterpret_cast<unsigned char*>(&value)[0x2d]; }
    const TextState*& Saved(Renderer& value)
    {
        return *reinterpret_cast<const TextState**>(reinterpret_cast<unsigned char*>(&value) + 0x40);
    }
    void Begin(Renderer& renderer, const TextState& state)
    {
        reinterpret_cast<void(__thiscall*)(void*, const TextState*)>(Base + 0x614f20)(&renderer, &state);
    }
    void End(Renderer& renderer)
    {
        reinterpret_cast<void(__thiscall*)(void*)>(Base + 0x614f40)(&renderer);
    }
    int __fastcall Draw(void* object, void*, int x, int y, int color, char* text)
    {
        auto& renderer = *static_cast<Renderer*>(object);
        ++Draws;
        Check(Active(renderer) == 1 && Saved(renderer) != nullptr,
            "both overlay lines have a live native text state during Draw");
        if (Saved(renderer))
            Check(Saved(renderer)->mode == 1 && Saved(renderer)->font == 0x12345678,
                "the native consumer sees the correct mode and font through its retained pointer");
        Check(x == 440 && (y == 35 || y == 48) && color == static_cast<int>(0xff99c6e3u),
            "overlay positions and color remain unchanged");
        Check(text && (y == 35 ? std::strncmp(text, "FPS : ", 6) == 0 :
            std::strncmp(text, "Committed : ", 12) == 0), "both existing performance labels remain intact");
        if (ChangeRenderer && Draws == 1) At<void*>(Base + 0xa01fd4) = &Second;
        return y == 48 ? 19 : 17;
    }
    // Only the unrelated full native diagnostic drawing is stubbed: its real
    // entry needs a D3D device. Native Begin and End still execute C2 instructions.
    int __fastcall Original(void*, void*)
    {
        ++OriginalCalls;
        Begin(First, NativeState);
        End(First);
        if (OriginalChangesRenderer) At<void*>(Base + 0xa01fd4) = &Second;
        return 23;
    }
    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t a, std::size_t n, std::size_t limit) { return a <= limit && n <= limit - a; };
        if (!fits(0, sizeof(IMAGE_DOS_HEADER), file.size())) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                nt->FileHeader.NumberOfSections * sizeof(*sections), file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, ImageSize)) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, image.size()) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress, file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
        return image;
    }
    void CheckInstaller(const std::vector<unsigned char>& original)
    {
        Check(!dfl::chapter_two::InstallPerformanceOverlay(nullptr, ImageSize), "null image is rejected");
        auto image = original;
        Check(!dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size() - 1) && image == original,
            "wrong image extent is rejected without writes");
        // Existing frame/FPS guards plus the newly relied-on C2 text contract.
        const std::array<std::pair<std::size_t, std::size_t>, 6> guards{
            std::pair<std::size_t, std::size_t>{0x51e2c0, 16}, {0x3c080, 7}, {0x512e00, 9},
            {0x614f20, 24}, {0x614f40, 16}, {0x82ff60, 12}};
        for (const auto& range : guards)
            for (std::size_t i = 0; i < range.second; ++i)
            {
                image = original; image[range.first + i] ^= 1;
                const auto before = image;
                Check(!dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size()) && image == before,
                    "every frame/FPS/text signature byte is checked before any image write");
            }
        image = original;
        Check(dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size()), "the actual C2 image installs");
        unsigned changed = 0;
        for (std::size_t i = 0; i < image.size(); ++i)
            if (image[i] != original[i])
            {
                ++changed;
                Check(i == 0x51e2c7 || i == 0x51e2c8 || (i >= 0x51e2cc && i < 0x51e2d0),
                    "only the existing frame gate and relative call operand change");
            }
        Check(changed > 2 && changed <= 6, "the existing six-byte maximum patch extent is preserved");
    }
    void Reset()
    {
        First = {}; Second = {};
        Methods[0x4c / 4] = reinterpret_cast<void*>(Base + 0x614f20);
        Methods[0x50 / 4] = reinterpret_cast<void*>(Base + 0x614f40);
        Methods[0x54 / 4] = reinterpret_cast<void*>(&Draw);
        First.methods = Second.methods = Methods;
        At<void*>(Base + 0xa01fd4) = &First;
        At<std::uint32_t>(Base + 0x954144) = 0x12345678;
        At<float>(0xd56d84) = 60;
        Draws = OriginalCalls = 0;
        ChangeRenderer = OriginalChangesRenderer = false;
    }
    void CheckOwnedAndFollowup()
    {
        Reset();
        Check(DrawPerformance(nullptr, nullptr) == 19 && Draws == 2,
            "overlay draws both lines and preserves the final draw return value");
        Check(!Active(First) && !Saved(First), "owned overlay batch ends before its stack TextState expires");
        TextState next = NativeState;
        Begin(First, next);
        Check(Active(First) && Saved(First) == &next, "the following native message selects its own state");
        End(First);
        for (unsigned i = 0; i < 256; ++i)
        {
            DrawPerformance(nullptr, nullptr);
            Check(!Active(First) && !Saved(First), "repeated frames do not retain an expired stack TextState");
            Begin(First, next);
            Check(Saved(First) == &next, "native messages immediately after every frame use their own state");
            End(First);
        }
    }
    void CheckBorrowedAndRendererChange()
    {
        Reset(); Begin(First, NativeState);
        DrawPerformance(nullptr, nullptr);
        Check(Active(First) && Saved(First) == &NativeState,
            "an already active native batch retains its caller's state and ownership");
        End(First);
        Reset(); ChangeRenderer = true;
        Begin(Second, NativeState);
        DrawPerformance(nullptr, nullptr);
        Check(!Active(First) && !Saved(First), "cleanup follows the captured renderer after the global root changes");
        Check(Active(Second) && Saved(Second) == &NativeState,
            "a changed global root cannot end another renderer's batch");
        End(Second);
    }
    void CheckMissingMethods()
    {
        for (const auto index : {0x4c / 4, 0x50 / 4, 0x54 / 4})
        {
            Reset(); Methods[index] = nullptr;
            Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0 && !Active(First) && !Saved(First),
                "a missing Begin, End or Draw cannot create an incomplete batch");
        }
        Reset(); First.methods = nullptr;
        Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0, "missing renderer vtable is safe");
        Reset(); At<void*>(Base + 0xa01fd4) = nullptr;
        Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0, "missing renderer root is safe");
    }
    void CheckOriginalFlag()
    {
        std::array<unsigned char, 0x300> state{};
        Reset(); state[0x2f0] = 1;
        Check(DrawPerformance(state.data(), nullptr) == 19 && OriginalCalls == 1 && Draws == 2 && !Active(First),
            "the original diagnostic flag still invokes the native entry before overlay drawing");
        Reset(); state[0x2f0] = 0;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 0, "a disabled original diagnostic flag does not invoke the native entry");
        Reset(); state[0x2f0] = 2;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 0, "the original diagnostic predicate remains exactly flag one");
        Reset(); state[0x2f0] = 1; OriginalChangesRenderer = true;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 1 && Draws == 2 && !Active(First) && !Active(Second),
            "overlay uses the renderer selected after the original diagnostic callback");
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto image = ReadImage(argv[1]);
    Check(image.size() == ImageSize, "fixture reads the actual original C2 PE");
    if (image.empty()) return 2;
    CheckInstaller(image);
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        DWORD previous = 0;
        const auto reserve = reinterpret_cast<std::uintptr_t>(textFixtureReserve);
        if (reserve <= Base && Base + ImageSize <= reserve + sizeof(textFixtureReserve) &&
            VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &previous))
            mapped = reinterpret_cast<unsigned char*>(Base);
    }
    Check(mapped == reinterpret_cast<void*>(Base), "fixture maps real C2 instructions at their original base");
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
    Check(dfl::chapter_two::InstallPerformanceOverlay(mapped, ImageSize), "production overlay installs on executable fixture");
    // Preserve the real C2 Begin/End and FPS reader. Only the original full D3D
    // diagnostic entry is replaced; its flag dispatch remains production code.
    mapped[0x512e00] = 0xe9;
    At<std::uint32_t>(Base + 0x512e01) = static_cast<std::uint32_t>(
        reinterpret_cast<std::uintptr_t>(&Original) - (Base + 0x512e00) - 5);
    FlushInstructionCache(GetCurrentProcess(), mapped, ImageSize);
    CheckOwnedAndFollowup();
    CheckBorrowedAndRendererChange();
    CheckMissingMethods();
    CheckOriginalFlag();
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two performance text lifetime: %u checks, %u failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
