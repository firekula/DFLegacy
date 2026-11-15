#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>

namespace
{
    int Checks;
    int Failures;
    int GuildRequests;
    int CloseCalls;
    void* ExpectedPanel;
    DWORD ClickedId = 42;

    void Check(bool pass, const char* label)
    {
        ++Checks;
        if (pass) return;
        ++Failures;
        std::fprintf(stderr, "FAIL guild navigation: %s\n", label);
    }

    void WINAPI CaptureGuildRequest(const char* message)
    {
        ++GuildRequests;
        Check(std::strcmp(message, "DFL_S1C2_GUILD_OPEN") == 0, "the native click sends a fixed event without credentials or URL");
        Check(CloseCalls == 0, "the request precedes the original panel close");
    }

    void __fastcall CaptureClose(void* panel, void*)
    {
        ++CloseCalls;
        Check(panel == ExpectedPanel, "original panel is passed to its close method");
    }

    // Supply the original guild tab proc's frame and saved registers to its
    // button branch. The branch returns through the original epilogue.
    void __declspec(naked) RunClick(const unsigned char*, void*)
    {
        __asm {
            mov eax, dword ptr [esp + 4]
            mov edx, dword ptr [esp + 8]
            push ebp
            mov ebp, esp
            sub esp, 268h
            push ebx
            push esi
            push edi
            mov esi, edx
            jmp eax
        }
    }
}

#define OutputDebugStringA CaptureGuildRequest
#include "ChapterTwoGuildNavigation.cpp"
#undef OutputDebugStringA

namespace
{
    constexpr std::size_t ButtonCheck = 0x4c989d;
    constexpr std::size_t OriginalMouseId = 0x4c98b0;
    constexpr std::size_t Navigate = 0x4c99fc;
    constexpr std::size_t LegacyUrl = 0x815634;

    bool LoadStockBranches(const wchar_t* path, unsigned char* image)
    {
        const auto file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        const auto mapping = CreateFileMappingW(file, nullptr, PAGE_READONLY | SEC_IMAGE_NO_EXECUTE, 0, 0, nullptr);
        CloseHandle(file);
        if (!mapping) return false;
        const auto stock = static_cast<const unsigned char*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, 0));
        CloseHandle(mapping);
        if (!stock) return false;
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(stock);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(stock + dos->e_lfanew);
        const bool valid = dos->e_magic == IMAGE_DOS_SIGNATURE && nt->Signature == IMAGE_NT_SIGNATURE &&
            nt->OptionalHeader.ImageBase == 0x400000 && nt->OptionalHeader.SizeOfImage == ImageSize;
        if (valid)
        {
            std::memcpy(image + ButtonCheck, stock + ButtonCheck, 0x2b);
            std::memcpy(image + Navigate, stock + Navigate, 6);
            std::memcpy(image + CloseGuildPanel, stock + CloseGuildPanel, 14);
            std::memcpy(image + LegacyUrl, stock + LegacyUrl, 0x3c);
        }
        UnmapViewOfFile(stock);
        return valid;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    auto* image = static_cast<unsigned char*>(VirtualAlloc(nullptr, ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    if (!image || !LoadStockBranches(argv[1], image)) return 2;

    const auto original = image[ClickAction];
    for (const auto rva : {ButtonCheck, ClickAction, Navigate, CloseGuildPanel, LegacyUrl})
    {
        image[rva] ^= 1;
        Check(!dfl::chapter_two::InstallGuildNavigation(image, ImageSize),
            "a corrupt click, COM navigation, close or URL signature is rejected");
        image[rva] ^= 1;
        Check(image[ClickAction] == original, "rejected signature leaves the action alone");
    }

    using Word = std::uintptr_t;
    Word vtable[10]{};
    vtable[9] = reinterpret_cast<Word>(&CaptureClose);
    Word panel[38]{};
    panel[0] = reinterpret_cast<Word>(vtable);
    ExpectedPanel = panel;
    alignas(Word) unsigned char button[18]{};
    const auto setButtonId = [&](DWORD id) { std::memcpy(button + 0xc, &id, sizeof(id)); };
    panel[0x94 / sizeof(Word)] = reinterpret_cast<Word>(button);
    setButtonId(ClickedId);

    if (!dfl::chapter_two::InstallGuildNavigation(image, ImageSize)) return 2;
    const auto idSlot = reinterpret_cast<Word>(&ClickedId);
    std::memcpy(image + OriginalMouseId, &idSlot, sizeof(idSlot));
    FlushInstructionCache(GetCurrentProcess(), image + ButtonCheck, CloseGuildPanel - ButtonCheck + 14);
    RunClick(image + ButtonCheck, panel);
    Check(!GuildRequests && !CloseCalls, "hidden button does not request a page or close");
    button[0x11] = 1;
    setButtonId(ClickedId + 1);
    RunClick(image + ButtonCheck, panel);
    Check(!GuildRequests && !CloseCalls, "unrelated click does not request a page or close");
    setButtonId(ClickedId);
    RunClick(image + ButtonCheck, panel);
    Check(GuildRequests == 1 && CloseCalls == 1, "the matched original button requests a page and closes its panel");
    VirtualFree(image, 0, MEM_RELEASE);
    std::printf("guild navigation: %d checks, %d failures\n", Checks, Failures);
    return Failures ? 1 : 0;
}
