#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <imm.h>
#include "ChapterTwoImeComposition.h"
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char imeFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t GameBase = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr auto GameWindow = static_cast<std::uintptr_t>(0x1234);
    constexpr auto ImeContext = static_cast<std::uintptr_t>(0x5678);
    constexpr auto ImeObject = static_cast<std::uintptr_t>(0xf2e658);
    unsigned Checks = 0, Failures = 0, Queries = 0, Reads = 0, Releases = 0;
    LONG QueryResult = 0, ReadResult = 0;
    DWORD ExpectedIndex = 0;

    template<class T> T& At(std::uintptr_t address)
    {
        return *reinterpret_cast<T*>(address);
    }

    void Check(bool passed, const char* description)
    {
        ++Checks;
        if (passed) return;
        ++Failures;
        std::fprintf(stderr, "FAIL IME 0x%lx query=%ld read=%ld: %s\n",
            ExpectedIndex, QueryResult, ReadResult, description);
    }

    HIMC WINAPI GetContext(HWND window)
    {
        Check(window == reinterpret_cast<HWND>(GameWindow), "native game window reaches ImmGetContext");
        return reinterpret_cast<HIMC>(ImeContext);
    }

    BOOL WINAPI ReleaseContext(HWND window, HIMC context)
    {
        ++Releases;
        Check(window == reinterpret_cast<HWND>(GameWindow) && context == reinterpret_cast<HIMC>(ImeContext),
            "native window and original context are released together");
        return TRUE;
    }

    LONG WINAPI GetComposition(HIMC context, DWORD index, LPVOID buffer, DWORD bytes)
    {
        Check(context == reinterpret_cast<HIMC>(ImeContext) && index == ExpectedIndex,
            "requested composition kind and context are preserved");
        if (!buffer)
        {
            ++Queries;
            Check(bytes == 0, "size query uses a zero-capacity buffer");
            return QueryResult;
        }
        ++Reads;
        Check(bytes == static_cast<DWORD>(QueryResult), "fetch honors the queried capacity");
        if (ReadResult <= 0 || static_cast<DWORD>(ReadResult) > bytes) return ReadResult;
        if (index == GCS_COMPATTR)
        {
            constexpr unsigned char attributes[]{ATTR_TARGET_CONVERTED, ATTR_CONVERTED, ATTR_CONVERTED};
            std::memcpy(buffer, attributes, static_cast<std::size_t>(ReadResult));
        }
        else
        {
            constexpr wchar_t text[]{L'\u4f60', L'\u597d'};
            std::memcpy(buffer, text, static_cast<std::size_t>(ReadResult));
        }
        return ReadResult;
    }

    void CopyImage(const std::vector<unsigned char>& file, unsigned char* image)
    {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        std::memcpy(image, file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            std::memcpy(image + section.VirtualAddress, file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
    }

    void Exercise(DWORD index, LONG query, LONG read)
    {
        ExpectedIndex = index;
        QueryResult = query;
        ReadResult = read;
        Queries = Reads = Releases = 0;
        std::memset(reinterpret_cast<void*>(ImeObject), 0, 0x98);
        std::memset(reinterpret_cast<void*>(0xf24420), 0, 0x908);
        At<unsigned>(ImeObject + 0x18) = 7;
        At<unsigned>(ImeObject + 0x34) = 7;
        At<WORD>(ImeObject + 0x72) = 936;
        At<unsigned char>(0xf2e640) = 1;
        At<unsigned char>(0xf2e62f) = 0;
        At<unsigned char>(0xf2e631) = 0;
        At<unsigned>(ImeObject + 0x64) = 23;
        At<unsigned>(ImeObject + 0x68) = 23;

        using Compose = bool (__thiscall*)(void*, HWND, WPARAM, LPARAM);
        const auto completed = reinterpret_cast<Compose>(0xa40320)(
            reinterpret_cast<void*>(ImeObject), reinterpret_cast<HWND>(GameWindow), 0, index);
        Check(completed && Queries == 1 && Releases == 1, "native handler completes and releases its context");
        Check(Reads == (query > 0 ? 1U : 0U), "negative or empty size skips the buffer fetch");

        const auto accepted = read > 0 && read <= query && (index == GCS_COMPATTR || read % 2 == 0);
        if (index == GCS_COMPATTR)
        {
            Check(At<unsigned>(ImeObject + 0x64) == (accepted ? 0U : 23U)
                && At<unsigned>(ImeObject + 0x68) == (accepted ? 1U : 23U),
                "only attributes actually returned affect the active range");
            return;
        }
        const auto length = index == GCS_COMPSTR ? ImeObject + 0x30 : ImeObject + 0x14;
        const auto output = index == GCS_COMPSTR ? 0xf24420U : 0xf24928U;
        const auto flag = index == GCS_COMPSTR ? 0xf2e62fU : 0xf2e631U;
        Check(At<unsigned char>(flag) == (accepted ? 1 : 0)
            && At<unsigned>(length) == (accepted ? static_cast<unsigned>(read / 2) : 0),
            "failed and short fetches do not pass unused bytes to the native string");
        if (accepted)
        {
            constexpr unsigned char chinese[]{0xc4, 0xe3, 0xba, 0xc3};
            Check(std::memcmp(reinterpret_cast<void*>(output), chinese, static_cast<std::size_t>(read)) == 0,
                "successful Chinese composition reaches the native text buffer");
        }
    }
}

int wmain(int argc, wchar_t** argv)
{
    const bool unpatched = argc == 3 && std::wcscmp(argv[2], L"--unpatched") == 0;
    if (argc != 2 && !unpatched) return 2;
    std::ifstream stream(std::filesystem::path(argv[1]), std::ios::binary);
    if (!stream) return 2;
    const std::vector<unsigned char> file{std::istreambuf_iterator<char>(stream), {}};
    auto* image = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(GameBase), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = image != nullptr;
    if (!image)
    {
        const auto start = reinterpret_cast<std::uintptr_t>(imeFixtureReserve);
        DWORD previous = 0;
        image = reinterpret_cast<unsigned char*>(GameBase);
        if (start > GameBase || GameBase + ImageSize > start + sizeof(imeFixtureReserve)
            || !VirtualProtect(image, ImageSize, PAGE_EXECUTE_READWRITE, &previous)) return 2;
    }
    CopyImage(file, image);
    At<decltype(&GetContext)>(0xaea0d4) = GetContext;
    At<decltype(&ReleaseContext)>(0xaea0d0) = ReleaseContext;
    At<decltype(&GetComposition)>(0xaea0fc) = GetComposition;
    At<decltype(&WideCharToMultiByte)>(0xaea298) = WideCharToMultiByte;
    if (!FlushInstructionCache(GetCurrentProcess(), image, ImageSize)) return 2;

    if (!unpatched)
    {
        const auto original = At<unsigned char>(0xa40432);
        At<unsigned char>(0xa40432) ^= 1;
        Check(!dfl::chapter_two::InstallImeComposition(image, ImageSize)
            && At<unsigned char>(0xa40387) == 0x74 && At<unsigned char>(0xa4039b) == 0xe8,
            "signature mismatch declines the entire installation");
        At<unsigned char>(0xa40432) = original;
        if (!dfl::chapter_two::InstallImeComposition(image, ImageSize)
            || !dfl::chapter_two::InstallImeComposition(image, ImageSize)) return 2;
        for (const DWORD index : {GCS_COMPSTR, GCS_RESULTSTR, GCS_COMPATTR})
            for (const LONG result : {IMM_ERROR_NODATA, IMM_ERROR_GENERAL, 0})
                Exercise(index, result, 0);
    }
    for (const DWORD index : {GCS_COMPSTR, GCS_RESULTSTR, GCS_COMPATTR})
    {
        const LONG size = index == GCS_COMPATTR ? 3 : 4;
        for (const LONG result : std::array<LONG, 6>{IMM_ERROR_NODATA, IMM_ERROR_GENERAL, 0, 2, size, size + 2})
            Exercise(index, size, result);
        if (index != GCS_COMPATTR) Exercise(index, size, 3);
    }
    if (allocated) VirtualFree(image, 0, MEM_RELEASE);
    std::printf("Chapter Two IME composition: %u checks, %u failures\n", Checks, Failures);
    return Failures ? 1 : 0;
}
