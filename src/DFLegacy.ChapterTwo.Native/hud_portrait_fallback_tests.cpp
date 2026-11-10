// Real C2 user fallback and texture instructions, without a game entry/window.
// Input PE SHA256: 9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb.
#include "ChapterTwoHudPortraitCache.cpp"
#include <cstdio>
#include <fstream>
#include <iterator>
#include <thread>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char fallbackFixtureReserve[0x1300000]{1};

namespace
{
    unsigned Checks = 0, Failures = 0, MainDraws = 0, Invalidations = 0;
    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL C2 fallback: %s\n", message); }
    }
    bool WriteFixture(std::uintptr_t address, const void* bytes, std::size_t size)
    {
        std::memcpy(reinterpret_cast<void*>(address), bytes, size);
        return true;
    }
    void CheckFallbackGuards(const std::vector<unsigned char>& original)
    {
        const std::array<std::pair<std::size_t, std::size_t>, 3> ranges{
            std::pair<std::size_t, std::size_t>{0x1347d3, 97}, {0x613a66, 24}, {0x8308e0, 4}};
        for (const auto& range : ranges)
            for (std::size_t i = 0; i < range.second; ++i)
            {
                auto image = original; image[range.first + i] ^= 1;
                const auto before = image;
                Check(!dfl::chapter_two::hud_portrait_detail::InstallImage(image.data(), image.size(), &WriteFixture) &&
                    image == before, "every private fallback and native simple-blit signature byte is guarded before writes");
            }
    }
    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    template<class T> std::uintptr_t Address(T& value) { return reinterpret_cast<std::uintptr_t>(&value); }
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
            nt->OptionalHeader.ImageBase != ImageBase || nt->OptionalHeader.SizeOfImage != ImageSize ||
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
    struct Texture
    {
        std::array<unsigned char, 0xa0> object{};
        std::array<unsigned short, 36 * 36> pixels{};
        std::array<unsigned short*, 36> rows{};
    };
    Texture Source, Target, Other;
    std::array<unsigned char, 0x1100> Ui{};
    std::array<unsigned char, 0x200> User{};
    std::array<unsigned char, 0xa0> Pack{}, Main{};
    void* SourceEntries[20]{};
    void* TextureMethods[64]{};
    void* UserMethods[2]{};
    bool UseMain = false;
    bool DirectOutput = false;
    unsigned Alpha = 255;
    unsigned ScaleX = 0x3f800000, ScaleY = 0x3f800000, Blend = 0xffffffff, Flags = 0;
    unsigned PivotX = 0x4f800000, PivotY = 0x4f800000;
    void __fastcall Invalidate(void*, void*) { ++Invalidations; }
    void __cdecl Clip(int, int, int, int) {}
    void __cdecl Unclip() {}
    void __cdecl BeginRaster(int) {}
    void __cdecl EndRaster(int, int) {}
    void __fastcall MainOutput(void*, void*, int, int, void* target, unsigned, unsigned)
    {
        Check(target == Main.data(), "main-screen draw still reaches its original D3D boundary");
        ++MainDraws;
    }
    void Jump(std::uintptr_t from, const void* to)
    {
        At<unsigned char>(from) = 0xe9;
        At<std::uint32_t>(from + 1) = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(to) - from - 5);
    }
    void PrepareTexture(Texture& texture, unsigned width, unsigned height)
    {
        texture = {};
        const auto address = Address(texture.object);
        At<void**>(address) = TextureMethods;
        At<unsigned char>(address + 4) = 1;
        At<unsigned>(address + 8) = 14;
        At<void*>(address + 0xc) = texture.pixels.data();
        At<void*>(address + 0x10) = texture.rows.data();
        At<unsigned>(address + 0x14) = width;
        At<unsigned>(address + 0x18) = height;
        At<RECT>(address + 0x30) = {0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
        At<unsigned>(address + 0x84) = 1; // Already-loaded sprite, no D3D upload.
        for (unsigned y = 0; y < height; ++y) texture.rows[y] = texture.pixels.data() + y * width;
    }
    unsigned Filled(const Texture& texture)
    {
        unsigned result = 0;
        for (auto color : texture.pixels) if (color != 0) ++result;
        return result;
    }
    using UserDraw = void(__thiscall*)(void*, int, int, int, int, int, void*, unsigned);
    void DrawUser(void* target)
    {
        reinterpret_cast<UserDraw>(0x5346f0)(User.data(), 0, 0, 24, 24, 0, target, Alpha);
    }
    void __fastcall DrawFixtureGroup(void*, void*)
    {
        auto* target = UseMain ? Main.data() : Target.object.data();
        if (DirectOutput)
        {
            const auto call = 0x53482f;
            const auto function = call + 5 + At<std::int32_t>(call + 1);
            using Output = void (__thiscall*)(void*, int, int, void*, unsigned, unsigned, unsigned, unsigned, unsigned, unsigned);
            reinterpret_cast<Output>(function)(target, 0, 0, Source.object.data(), ScaleX, ScaleY,
                Flags, Blend, PivotX, PivotY);
        }
        else DrawUser(target);
    }
    void Reset()
    {
        std::memcpy(TextureMethods, reinterpret_cast<void*>(0xc30824), sizeof(TextureMethods));
        TextureMethods[0x8c / 4] = reinterpret_cast<void*>(&Invalidate);
        PrepareTexture(Source, 24, 26); PrepareTexture(Target, 36, 36); PrepareTexture(Other, 36, 36);
        // A nonempty, partly transparent source makes a no-op distinguishable
        // from real original 1555 alpha-copy, with no fabricated pixel writer.
        for (unsigned i = 0; i < 24 * 26; ++i) Source.pixels[i] = i % 3 == 0 ? 0 : 0x801f;
        Ui = {}; User = {}; Pack = {}; Main = {};
        At<unsigned>(Address(User) + 8) = 42;
        At<unsigned>(Address(User) + 0x18) = 0;
        UserMethods[1] = reinterpret_cast<void*>(0x5346f0);
        At<void**>(Address(User)) = UserMethods;
        for (unsigned job = 0; job < 5; ++job) SourceEntries[job * 4] = Source.object.data();
        At<void*>(Address(Pack) + 0x54) = SourceEntries;
        At<void*>(Address(Pack) + 0x58) = SourceEntries + 20;
        At<void*>(0xd7a8b8) = Pack.data();
        At<unsigned>(Address(Main) + 8) = 7;
        At<void*>(0xe01fd4) = Main.data();
        At<void*>(Address(Ui) + 0x850) = Target.object.data();
        MainDraws = Invalidations = 0;
        UseMain = DirectOutput = false; Alpha = 255;
        ScaleX = ScaleY = 0x3f800000; Blend = 0xffffffff; Flags = 0;
        PivotX = PivotY = 0x4f800000;
    }
    void RunCases()
    {
        Reset(); DrawUser(Target.object.data());
        Check(Filled(Target) == 0, "the real original scaled fallback rejects an offscreen type14 target");
        Reset(); DrawUser(Main.data());
        Check(MainDraws == 1 && Filled(Target) == 0, "the same original fallback works on main-screen type7");
        Reset(); RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 416 && Invalidations == 1,
            "HUD-scoped no-actor fallback copies the original nontransparent 1555 pixels");
        Check(At<void*>(Address(User) + 0x164) == nullptr && At<void*>(Address(User) + 0x168) == nullptr,
            "drawing a private-room fallback does not create town or combat actors");
        Reset(); Target.pixels.fill(0x83e0); RenderTown(Ui.data(), nullptr);
        Check(Target.pixels[0] == 0x83e0 && Target.pixels[1] == 0x801f && Target.pixels[24] == 0x83e0 &&
            Target.pixels[26 * 36] == 0x83e0, "native simple blit preserves transparent pixels and the area outside the source");
        for (unsigned job = 0; job < 5; ++job)
        {
            Reset(); At<unsigned>(Address(User) + 0x18) = job; RenderTown(Ui.data(), nullptr);
            Check(Filled(Target) == 416, "every native job icon can populate the private-room HUD target");
        }
        Reset(); UseMain = true; RenderTown(Ui.data(), nullptr);
        Check(MainDraws == 1 && Filled(Target) == 0, "HUD scope does not redirect main-screen fallback drawings");
        Reset(); Alpha = 100; RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 0, "a tinted fallback preserves the original scaled-draw behavior");
        Reset(); At<void*>(Address(Ui) + 0x850) = Other.object.data(); RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 0, "HUD scope cannot redirect a target outside its eight cache surfaces");
        for (const auto scale : {0u, 0x3f000000u, 0x40000000u, 0x7fc00000u})
            for (const auto horizontal : {false, true})
            {
                Reset(); DirectOutput = true;
                (horizontal ? ScaleX : ScaleY) = scale;
                RenderTown(Ui.data(), nullptr);
                Check(Filled(Target) == 0, "non-unit or NaN scale retains the native scaled-draw path");
            }
        Reset(); DirectOutput = true; Blend = 0x80ffffff; RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 0, "the direct fallback bridge cannot discard a non-opaque tint");
        Reset(); DirectOutput = true; Flags = 1; RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 0, "a nondefault transform flag retains the original path");
        Reset(); DirectOutput = true; PivotX = 0; RenderTown(Ui.data(), nullptr);
        Check(Filled(Target) == 0, "a nondefault pivot retains the original path");
        Reset();
        std::thread other([] { RenderTown(Ui.data(), nullptr); });
        other.join();
        Check(Filled(Target) == 0, "another thread cannot read or borrow the bound rendering thread's scope");
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto image = ReadImage(argv[1]);
    if (image.empty()) return 2;
    CheckFallbackGuards(image);
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(ImageBase), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        DWORD previous = 0;
        const auto reserve = Address(fallbackFixtureReserve);
        if (reserve <= ImageBase && ImageBase + ImageSize <= reserve + sizeof(fallbackFixtureReserve) &&
            VirtualProtect(reinterpret_cast<void*>(ImageBase), ImageSize, PAGE_EXECUTE_READWRITE, &previous))
            mapped = reinterpret_cast<unsigned char*>(ImageBase);
    }
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
    Check(dfl::chapter_two::InstallHudPortraitCache(mapped, ImageSize), "production HUD hooks install on original C2 code");
    // Only OS imports and unrelated D3D boundaries are provided. Real user
    // fallback, image-pack lookup, scaled rejection and simple CPU blit execute.
    At<void*>(0xaea4a4) = reinterpret_cast<void*>(&IntersectRect);
    At<void*>(0xaea470) = reinterpret_cast<void*>(&OffsetRect);
    Jump(0x910770, reinterpret_cast<void*>(&DrawFixtureGroup));
    Jump(0xa13320, reinterpret_cast<void*>(&MainOutput));
    Jump(0x4013c0, reinterpret_cast<void*>(&Clip)); Jump(0x401410, reinterpret_cast<void*>(&Unclip));
    Jump(0xa3b030, reinterpret_cast<void*>(&BeginRaster)); Jump(0xa3b070, reinterpret_cast<void*>(&EndRaster));
    FlushInstructionCache(GetCurrentProcess(), mapped, ImageSize);
    RunCases();
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two private-room HUD fallback: %u checks, %u failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
