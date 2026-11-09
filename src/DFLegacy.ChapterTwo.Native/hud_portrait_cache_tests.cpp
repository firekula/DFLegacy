#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <thread>
#include <vector>

#ifndef CHAPTER_TWO_HUD_BASELINE
#include "ChapterTwoHudPortraitCache.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char hudFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::array<std::uintptr_t, 3> Calls{0x91dbf6, 0x91dbff, 0x90e4fa};
    unsigned Checks = 0;
    int Failures = 0;
    void Check(bool condition, const char* message)
    {
        ++Checks;
        if (!condition) { ++Failures; std::fprintf(stderr, "FAIL C2 HUD: %s\n", message); }
    }
    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    template<class T> std::uintptr_t Address(T& value) { return reinterpret_cast<std::uintptr_t>(&value); }
    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t a, std::size_t n, std::size_t size) { return a <= size && n <= size - a; };
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                nt->FileHeader.NumberOfSections * sizeof(*sections), file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size())) return {};
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

#ifndef CHAPTER_TWO_HUD_BASELINE
    unsigned Writes = 0, FailAt = 0;
    bool FailAfter = false;
    bool Write(std::uintptr_t address, const void* data, std::size_t size)
    {
        ++Writes;
        if (Writes == FailAt && !FailAfter) return false;
        std::memcpy(reinterpret_cast<void*>(address), data, size);
        return Writes != FailAt;
    }
    void CheckInstaller(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::hud_portrait_detail::InstallImage;
        auto image = original;
        Writes = FailAt = 0;
        Check(!dfl::chapter_two::InstallHudPortraitCache(image.data(), image.size()) && image == original,
            "the runtime installer rejects an image outside its required400000 execution base");
        image = original;
        Check(!InstallImage(nullptr, ImageSize, &Write) &&
            !InstallImage(image.data(), ImageSize - 1, &Write) &&
            !InstallImage(image.data(), ImageSize, nullptr) && Writes == 0,
            "invalid image, extent or writer makes no writes");
        const std::array<std::pair<std::size_t, std::size_t>, 3> guards{
            std::pair<std::size_t, std::size_t>{0x50e440, 0x11e}, {0x51dbe7, 0x1d}, {0x5c0e00, 0x70}};
        for (const auto& range : guards)
            for (std::size_t i = 0; i < range.second; ++i)
            {
                image = original;
                image[range.first + i] ^= 1;
                const auto before = image;
                Writes = 0;
                Check(!InstallImage(image.data(), image.size(), &Write) && Writes == 0 && image == before,
                    "every byte in all three calls and guarded contexts is checked before any write");
            }
        for (unsigned fail = 1; fail <= 3; ++fail)
            for (const auto after : {false, true})
            {
                image = original;
                Writes = 0; FailAt = fail; FailAfter = after;
                Check(!InstallImage(image.data(), image.size(), &Write) && image == original,
                    "failure before or after each mutation restores every original call");
            }
        image = original;
        Writes = FailAt = 0;
        Check(InstallImage(image.data(), image.size(), &Write) && Writes == 3,
            "the production installer patches all three verified calls");
        unsigned differences = 0;
        for (std::size_t i = 0; i < image.size(); ++i)
            if (image[i] != original[i])
            {
                ++differences;
                bool inside = false;
                for (auto call : Calls) inside = inside || (i >= call - Base && i < call - Base + 5);
                Check(inside, "no coordinate, timer period, UID/dirty logic or slot code is changed");
            }
        Check(differences > 0 && differences <= 12, "only relative call operands differ");
        Writes = 0;
        Check(InstallImage(image.data(), image.size(), &Write) && Writes == 0,
            "a complete matching reinstall writes nothing");
        for (auto call : Calls)
        {
            auto mixed = image;
            std::memcpy(mixed.data() + call - Base, original.data() + call - Base, 5);
            const auto before = mixed;
            Writes = 0;
            Check(!InstallImage(mixed.data(), mixed.size(), &Write) && Writes == 0 && mixed == before,
                "mixed original/patched calls are rejected without writes");
        }
        image[0x50e4e2] ^= 1;
        const auto before = image;
        Writes = 0;
        Check(!InstallImage(image.data(), image.size(), &Write) && Writes == 0 && image == before,
            "patched calls do not bypass the surrounding signature guard");
    }
#endif

    struct Surface
    {
        void** methods = nullptr;
        unsigned draws = 0, clears = 0;
        bool content = false;
    };
    struct User
    {
        void** methods = nullptr;
        unsigned unused = 0;
        unsigned uid = 0;
    };
    static_assert(offsetof(User, uid) == 8);
    struct Fixture
    {
        std::array<unsigned char, 0x1100> ui{};
        std::array<Surface, 8> surfaces{};
        std::array<User, 8> users{};
    };
    Fixture First, Second;
    DWORD Now = 0, ReadyAt = 0;
    unsigned SlotCount = 4, Outputs = 0, TickCalls = 0;
    bool Reverse = false, Nested = false, InNested = false;
    void* ActiveBase = nullptr;
    void* Entry = reinterpret_cast<void*>(Calls[1]);
    void* SurfaceMethods[8]{};
    void* UserMethods[2]{};
    unsigned BeforeEsp = 0, AfterEsp = 0, AfterEbp = 0, AfterEbx = 0, AfterEsi = 0, AfterEdi = 0;

    DWORD __stdcall Tick() { ++TickCalls; return Now; }
    void __fastcall Clear(Surface* target, void*, unsigned color)
    {
        Check(color == 0, "native surface clearing retains color zero");
        ++target->clears; target->content = false;
    }
    void __fastcall Draw(User*, void*, int x, int y, int width, int height, int force,
        Surface* target, unsigned alpha)
    {
        Check(x == 0 && y == 0 && width == 24 && height == 24 && force == 0 && alpha == 255,
            "the actual cache branch forwards its native crop and alpha unchanged");
        ++target->draws; target->content = Now >= ReadyAt;
    }
    void __cdecl Clip(int x, int y, int right, int bottom)
    {
        Check(right - x == 24 && bottom - y == 24, "native output clip remains 24 by 24");
    }
    void __cdecl Unclip() {}
    void __fastcall Output(void*, void*, unsigned x, unsigned y, Surface*, unsigned sx,
        unsigned sy, unsigned zero, unsigned color, unsigned px, unsigned py)
    {
        Check(x >= 100 && x <= 317 && y >= 10 && y <= 31 && sx == 0x3f800000 && sy == 0x3f800000 &&
            zero == 0 && color == 0xffffffff && px == 0x4f800000 && py == 0x4f800000,
            "the original unmirrored output retains coordinates, scales and color");
        ++Outputs;
    }
    using Portrait = void (__thiscall*)(void*, int, int, unsigned, User*, unsigned, int, int, unsigned);
    void DrawSlot(Fixture& f, unsigned slot)
    {
        reinterpret_cast<Portrait>(0x90e440)(f.ui.data(), 100 + static_cast<int>(slot) * 31,
            10 + static_cast<int>(slot) * 3, slot, &f.users[slot], 0, 24, 24, 0);
    }
    using Group = void (__thiscall*)(void*);
    void __fastcall DrawGroup(void* base, void*)
    {
        auto& fixture = base == First.ui.data() ? First : Second;
        for (unsigned i = 0; i < SlotCount; ++i)
        {
            const auto slot = Reverse ? SlotCount - i - 1 : i;
            DrawSlot(fixture, slot);
            if (Nested && !InNested && i == 0)
            {
                InNested = true;
                reinterpret_cast<Group>(Calls[0])(Second.ui.data());
                InNested = false;
            }
        }
    }
    __declspec(naked) void RunGroup()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp, esp
            mov ecx, ActiveBase
            mov ebp, 11223344h
            mov ebx, 22334455h
            mov esi, 33445566h
            mov edi, 44556677h
            call Entry
            mov AfterEsp, esp
            mov AfterEbp, ebp
            mov AfterEbx, ebx
            mov AfterEsi, esi
            mov AfterEdi, edi
            popad
            popfd
            ret
        }
    }
    void Jump(std::uintptr_t from, const void* to)
    {
        At<unsigned char>(from) = 0xe9;
        At<std::uint32_t>(from + 1) = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(to) - from - 5);
    }
    void PrepareDependencies()
    {
        SurfaceMethods[7] = reinterpret_cast<void*>(&Clear);
        UserMethods[1] = reinterpret_cast<void*>(&Draw);
        At<void*>(0xaea50c) = reinterpret_cast<void*>(&Tick);
        At<void*>(0xe01fd4) = reinterpret_cast<void*>(1);
        Jump(0x910770, reinterpret_cast<void*>(&DrawGroup));
        Jump(0x913770, reinterpret_cast<void*>(&DrawGroup));
        Jump(0xa3eb30, reinterpret_cast<void*>(&Output));
        Jump(0x4013c0, reinterpret_cast<void*>(&Clip));
        Jump(0x401410, reinterpret_cast<void*>(&Unclip));
        // Execute each real five-byte party draw call. Its unrelated following
        // renderer work is outside the fixture, so end at their shared return.
        At<unsigned char>(0x91dc04) = 0xc3;
        FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(Base), ImageSize);
    }
    void Reset(Fixture& fixture, bool uncached = false)
    {
        fixture = {};
        const auto base = Address(fixture.ui);
        for (unsigned slot = 0; slot < 8; ++slot)
        {
            fixture.surfaces[slot].methods = SurfaceMethods;
            fixture.users[slot].methods = UserMethods;
            fixture.users[slot].uid = slot + 1;
            At<Surface*>(base + 0x850 + slot * 4) = &fixture.surfaces[slot];
            At<unsigned>(base + 0x870 + slot * 4) = uncached ? 0 : slot + 1;
        }
        At<unsigned>(base + 0x890) = 500;
        At<unsigned>(base + 0x894) = 0xffffffff;
        At<unsigned char>(base + 0x89a) = 1;
        ActiveBase = First.ui.data();
        Now = ReadyAt = Outputs = TickCalls = 0;
        Nested = InNested = Reverse = false;
    }
    unsigned Count(Fixture& f) { return At<unsigned>(Address(f.ui) + 0x8a0); }
    void CheckPeriodic(unsigned slots, bool reverse, std::uintptr_t call, bool delayed)
    {
        Reset(First, delayed); SlotCount = slots; Reverse = reverse; ReadyAt = delayed ? 1000 : 0;
        Entry = reinterpret_cast<void*>(call);
        for (Now = 0; Now <= 20000; Now += 16) RunGroup();
        std::printf("C2 HUD actual %s %u slots %s delayed=%u: timer=%u draws=", call == Calls[0] ? "battle" : "town",
            slots, reverse ? "reverse" : "forward", delayed ? 1u : 0u, Count(First));
        for (unsigned slot = 0; slot < slots; ++slot)
        {
            std::printf("%u%s", First.surfaces[slot].draws, slot + 1 == slots ? "\n" : ",");
            Check(First.surfaces[slot].draws == (delayed ? 6u : 5u) && First.surfaces[slot].content,
                "every available slot periodically refreshes through actual original instructions and installed calls");
        }
        Check(Count(First) == 40, "20 seconds advances the original 500ms timer exactly 40 times");
        Check(Outputs == 1251 * slots, "all original per-frame portrait output calls still execute");
        Check(BeforeEsp == AfterEsp && AfterEbp == 0x11223344 && AfterEbx == 0x22334455 &&
            AfterEsi == 0x33445566 && AfterEdi == 0x44556677,
            "actual call wrappers retain stack balance and all nonvolatile registers");
    }
#ifndef CHAPTER_TWO_HUD_BASELINE
    void CheckImmediateAndBoundary()
    {
        Reset(First); SlotCount = 4; Entry = reinterpret_cast<void*>(Calls[1]);
        Now = 499; RunGroup();
        Check(Count(First) == 0, "499ms does not advance the original timer");
        Now = 500; RunGroup(); RunGroup();
        Check(Count(First) == 1 && First.surfaces[1].draws == 1 && First.surfaces[0].draws == 0,
            "500ms selects slot1 once and a second same-tick pass does not accelerate rotation");
        Now = 1000; RunGroup();
        Check(Count(First) == 2 && First.surfaces[2].draws == 1, "1000ms retains the count-and-seven slot2 selection");
        Reset(First); SlotCount = 4; Now = 500;
        for (unsigned slot = 0; slot < 4; ++slot) First.ui[0x103c + slot] = 1;
        RunGroup();
        Check(Count(First) == 0 && TickCalls == 0, "all-dirty immediate redraw remains lazy and never samples the timer");
        for (unsigned slot = 0; slot < 4; ++slot)
            Check(First.surfaces[slot].draws == 1 && First.ui[0x103c + slot] == 0,
                "the original dirty path redraws immediately and clears its own flag");
        Reset(First); Now = 1; First.users[2].uid = 42; RunGroup();
        Check(First.surfaces[2].draws == 1 && At<unsigned>(Address(First.ui) + 0x878) == 42 && Count(First) == 0,
            "UID replacement redraws immediately and retains the new UID without advancing rotation");
    }
    void CheckScopeBoundaries()
    {
        Reset(First); SlotCount = 4;
        for (Now = 0; Now <= 20000; Now += 16)
            for (unsigned slot = 0; slot < 4; ++slot) DrawSlot(First, slot);
        Check(First.surfaces[0].draws == 5 && First.surfaces[1].draws == 0 && Count(First) == 40,
            "unwrapped callers retain the original timer-consumption behavior");
        Reset(First); Reset(Second); SlotCount = 4; Nested = true; Now = 500;
        RunGroup();
        Check(Count(First) == 1 && Count(Second) == 1 && First.surfaces[1].draws == 1 && Second.surfaces[1].draws == 1,
            "nested rendering uses each owner's timer and restores the outer shared result");
        Nested = false; Now = 1000; DrawSlot(First, 0);
        Check(Count(First) == 2, "the outer scope restores the unwrapped original timer after returning");
        Reset(First); SlotCount = 4;
        std::thread other([] { for (Now = 0; Now <= 20000; Now += 16) RunGroup(); });
        other.join();
        Check(First.surfaces[0].draws == 5 && First.surfaces[1].draws == 0 && Count(First) == 40,
            "calls on another thread retain their original behavior and cannot borrow the rendering scope");
    }
#endif
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto image = ReadImage(argv[1]);
    Check(image.size() == ImageSize, "the fixture uses the actual original C2 PE at its preferred base");
    if (image.empty()) return 2;
#ifndef CHAPTER_TWO_HUD_BASELINE
    CheckInstaller(image);
#endif
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        DWORD old = 0;
        const auto begin = reinterpret_cast<std::uintptr_t>(hudFixtureReserve);
        if (begin <= Base && Base + ImageSize <= begin + sizeof(hudFixtureReserve) &&
            VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &old))
            mapped = reinterpret_cast<unsigned char*>(Base);
    }
    Check(mapped == reinterpret_cast<void*>(Base), "the fixture can execute the real x86 native image at400000");
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
#ifndef CHAPTER_TWO_HUD_BASELINE
    Check(dfl::chapter_two::InstallHudPortraitCache(mapped, ImageSize), "the production patch installs on the mapped original image");
#endif
    PrepareDependencies();
    CheckPeriodic(4, false, Calls[1], false);
#ifndef CHAPTER_TWO_HUD_BASELINE
    CheckPeriodic(4, false, Calls[0], false);
    CheckPeriodic(1, false, Calls[1], false);
    CheckPeriodic(8, false, Calls[1], false);
    CheckPeriodic(4, true, Calls[1], false);
    CheckPeriodic(8, true, Calls[0], false);
    CheckPeriodic(4, false, Calls[1], true);
    CheckImmediateAndBoundary();
    CheckScopeBoundaries();
#endif
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two HUD portrait cache: %u checks, %d failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
