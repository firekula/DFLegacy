#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoCountdownCoin.h"
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char countdownFixtureReserve[0x1300000]{1};

namespace {
constexpr std::uintptr_t GameBase = 0x400000;
constexpr std::size_t ImageSize = 0xe57000;
constexpr unsigned CountdownProduct = 100004;
constexpr unsigned VisibleProduct = 100000;
alignas(4) std::array<unsigned char, 0x3654> Shop{};
alignas(4) std::array<unsigned char, 0x58> VisibleRow{};
unsigned Popups = 0;
unsigned VisibleReceipts = 0;
unsigned Failures = 0;

template<class T> void Set(unsigned char* object, std::size_t offset, T value)
{
    std::memcpy(object + offset, &value, sizeof(value));
}

void Check(bool passed, const char* message)
{
    if (!passed) {
        ++Failures;
        std::fprintf(stderr, "FAIL countdown coin: %s\n", message);
    }
}

bool Write(std::uintptr_t address, const void* bytes, std::size_t size)
{
    DWORD old = 0;
    auto* destination = reinterpret_cast<void*>(address);
    if (!VirtualProtect(destination, size, PAGE_EXECUTE_READWRITE, &old)) return false;
    std::memcpy(destination, bytes, size);
    DWORD ignored = 0;
    const bool restored = VirtualProtect(destination, size, old, &ignored) != FALSE;
    return FlushInstructionCache(GetCurrentProcess(), destination, size) != FALSE && restored;
}

bool Jump(std::uintptr_t from, std::uintptr_t to)
{
    unsigned char code[]{0xe9, 0, 0, 0, 0};
    Set(code, 1, static_cast<std::uint32_t>(to - from - sizeof(code)));
    return Write(from, code, sizeof(code));
}

int __fastcall CountPopup(void*, void*, int, int)
{
    ++Popups;
    return 0;
}

__declspec(naked) void __cdecl ReturnFromFixture()
{
    __asm {
        pop ebp
        ret
    }
}

__declspec(naked) void __cdecl CountVisibleReceipt()
{
    __asm {
        inc VisibleReceipts
        push 943faeh
        ret
    }
}

// Enter an actual CMD67 CALL instruction with its native registers and stack.
__declspec(naked) void __cdecl Present(std::uintptr_t, unsigned char*, int, unsigned, int, int, int, int)
{
    __asm {
        push ebp
        mov ebp, esp
        push [ebp+24h]
        push [ebp+20h]
        push [ebp+1ch]
        push [ebp+18h]
        push [ebp+14h]
        push [ebp+10h]
        mov ecx, [ebp+0ch]
        mov eax, [ebp+8]
        jmp eax
    }
}

void ExpectReceipt(unsigned commodity, unsigned expectedPopups, unsigned expectedVisible, const char* description)
{
    Shop[0x263c] = 1;
    Popups = VisibleReceipts = 0;
    Present(0x418345, Shop.data(), 1, 1, commodity, -1, 65535, 0);
    Check(Popups == expectedPopups && VisibleReceipts == expectedVisible && Shop[0x263c] == 0, description);
}
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto module = LoadLibraryExW(argv[1], nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (!module) return 2;
    const auto* mapped = reinterpret_cast<const unsigned char*>(module);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(mapped);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(mapped + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.SizeOfImage != ImageSize
        || nt->OptionalHeader.ImageBase != GameBase) return 2;
    auto* image = reinterpret_cast<unsigned char*>(GameBase);
    const bool needsCopy = mapped != image;
    bool allocated = false;
    if (needsCopy) {
        allocated = VirtualAlloc(image, ImageSize, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE) != nullptr;
        if (!allocated) {
            const auto start = reinterpret_cast<std::uintptr_t>(countdownFixtureReserve);
            DWORD old = 0;
            if (start > GameBase || GameBase + ImageSize > start + sizeof(countdownFixtureReserve)
                || !VirtualProtect(image, ImageSize, PAGE_EXECUTE_READWRITE, &old)) return 2;
        }
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& section = sections[i];
            if (section.VirtualAddress > ImageSize || section.SizeOfRawData > ImageSize - section.VirtualAddress) return 2;
            std::memcpy(image + section.VirtualAddress, mapped + section.VirtualAddress, section.SizeOfRawData);
        }
        if (!FlushInstructionCache(GetCurrentProcess(), image, ImageSize)) return 2;
    }

    using dfl::chapter_two::InstallCountdownCoin;
    const auto originalOpcode = image[0x18345];
    const auto originalSender = image[0x53e407];
    const unsigned char invalid = 0xcc;
    if (!Write(0x93e407, &invalid, 1)) return 2;
    Check(!InstallCountdownCoin(image, ImageSize) && image[0x18345] == originalOpcode,
        "a different countdown sender prevents installation");
    if (!Write(0x93e407, &originalSender, 1)) return 2;
    Check(InstallCountdownCoin(image, ImageSize) && InstallCountdownCoin(image, ImageSize),
        "install at the verified success call and allow reinstalling");

    constexpr unsigned char returnOneArgument[]{0xc2, 0x04, 0x00};
    constexpr unsigned char returnTwoArguments[]{0xc2, 0x08, 0x00};
    if (!Jump(0x41834a, reinterpret_cast<std::uintptr_t>(&ReturnFromFixture))
        || !Jump(0x418496, reinterpret_cast<std::uintptr_t>(&ReturnFromFixture))
        || !Write(0x865700, returnOneArgument, sizeof(returnOneArgument))
        || !Write(0x8657d0, returnTwoArguments, sizeof(returnTwoArguments))
        || !Jump(0x93dfc0, reinterpret_cast<std::uintptr_t>(&CountPopup))
        || !Jump(0x943ceb, reinterpret_cast<std::uintptr_t>(&CountVisibleReceipt))) return 2;

    Set(Shop.data(), 0x198, VisibleRow.data());
    Set(Shop.data(), 0x19c, VisibleRow.data() + VisibleRow.size());
    Set(VisibleRow.data(), 0, VisibleProduct);
    Set(Shop.data(), 0x35f8, CountdownProduct);
    Set(Shop.data(), 0x35fc, 1u);
    Set(Shop.data(), 0x3604, 1u);
    Set(Shop.data(), 0x3608, static_cast<unsigned char>(0xff));
    Set(Shop.data(), 0x360c, 1u);
    Set(Shop.data(), 0x3620, 100u);
    ExpectReceipt(CountdownProduct, 0, 0, "the hidden successful countdown purchase clears pending without an error popup");
    ExpectReceipt(VisibleProduct, 0, 1, "an ordinary visible coin receipt reaches the original success path");
    ExpectReceipt(123456, 1, 0, "an unrelated missing receipt retains the original error path");

    Shop[0x263c] = 1;
    Popups = VisibleReceipts = 0;
    Present(0x418491, Shop.data(), 0, 1, CountdownProduct, 4, 65535, 0);
    Check(Popups == 1 && VisibleReceipts == 0 && Shop[0x263c] == 0,
        "an actual countdown purchase rejection retains the original error path");
    Set(VisibleRow.data(), 0, CountdownProduct);
    ExpectReceipt(CountdownProduct, 0, 1, "a visible catalog match takes precedence over the reserved entry");
    std::printf("countdown coin: %u failures\n", Failures);
    if (allocated) VirtualFree(image, 0, MEM_RELEASE);
    FreeLibrary(module);
    return Failures ? 1 : 0;
}
