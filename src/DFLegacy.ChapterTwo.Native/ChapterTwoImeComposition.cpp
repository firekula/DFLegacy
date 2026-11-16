#include "ChapterTwoImeComposition.h"
#include "memory_patch.h"
#include <array>
#include <cstdint>
#include <cstring>

namespace
{
    constexpr std::uintptr_t GameBase = 0x400000;
    constexpr std::size_t GameSize = 0xe57000;

    // Each original call supplies the four stdcall arguments on the stack.
    // On failure, use the original no-data path, which releases the context.
    __declspec(naked) void FetchComposition()
    {
        __asm {
            mov eax, 00A9D88Eh
            call eax
            test eax, eax
            jle noData
            cmp eax, edi
            ja noData
            test al, 1
            jnz noData
            mov edi, eax
            mov eax, 00A403A0h
            jmp eax
        noData:
            mov ebx, [ebp+8]
            mov esi, [ebp-4]
            mov eax, 00A403FBh
            jmp eax
        }
    }

    __declspec(naked) void FetchResult()
    {
        __asm {
            mov eax, 00A9D88Eh
            call eax
            test eax, eax
            jle noData
            cmp eax, ebx
            ja noData
            test al, 1
            jnz noData
            mov ebx, eax
            mov eax, 00A40478h
            jmp eax
        noData:
            mov esi, [ebp-4]
            mov eax, 00A404B3h
            jmp eax
        }
    }

    __declspec(naked) void FetchAttributes()
    {
        __asm {
            mov eax, 00A9D88Eh
            call eax
            test eax, eax
            jle noData
            cmp eax, edi
            ja noData
            mov edi, eax
            mov eax, 00A4054Ah
            jmp eax
        noData:
            mov esi, [ebp-4]
            mov eax, 00A40586h
            jmp eax
        }
    }

    using Bytes = std::array<unsigned char, 5>;
    struct Patch
    {
        std::uintptr_t address;
        std::size_t size;
        Bytes original;
        Bytes replacement;
    };

    Bytes Jump(std::uintptr_t site, void (*target)()) noexcept
    {
        Bytes bytes{0xe9};
        const auto relative = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(target) - site - bytes.size());
        std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
        return bytes;
    }
}

bool dfl::chapter_two::InstallImeComposition(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4);
    if (reinterpret_cast<std::uintptr_t>(image) != GameBase || bytes != GameSize) return false;
    // The size queries return signed LONGs. The original zero branches skip
    // allocation, copying and attribute scanning when composition is absent.
    constexpr unsigned char sizeChecks[][9]{
        {0xe8, 0x0b, 0xd5, 0x05, 0, 0x8b, 0xf8, 0x85, 0xff},
        {0x81, 0xfb, 0, 4, 0, 0, 0x7c, 7, 0xbb},
        {0xe8, 0x61, 0xd3, 0x05, 0, 0x8b, 0xf8, 0x85, 0xff},
    };
    constexpr std::uintptr_t sizeSites[]{0xa4037e, 0xa40432, 0xa40528};
    constexpr unsigned char resultTest[]{0x85, 0xdb};
    constexpr unsigned char immThunk[]{0xff, 0x25, 0xfc, 0xa0, 0xae, 0};
    for (std::size_t i = 0; i < std::size(sizeSites); ++i)
        if (std::memcmp(image + (sizeSites[i] - GameBase), sizeChecks[i], sizeof(sizeChecks[i])) != 0) return false;
    if (std::memcmp(image + (0xa40441 - GameBase), resultTest, sizeof(resultTest)) != 0 ||
        std::memcmp(image + (0xa9d88e - GameBase), immThunk, sizeof(immThunk)) != 0) return false;

    const std::array patches{
        Patch{0xa40387, 2, {0x74, 0x72}, {0x7e, 0x72}},
        Patch{0xa40443, 2, {0x74, 0x6e}, {0x7e, 0x6e}},
        Patch{0xa40531, 2, {0x74, 0x53}, {0x7e, 0x53}},
        Patch{0xa4039b, 5, {0xe8, 0xee, 0xd4, 0x05, 0}, Jump(0xa4039b, FetchComposition)},
        Patch{0xa40473, 5, {0xe8, 0x16, 0xd4, 0x05, 0}, Jump(0xa40473, FetchResult)},
        Patch{0xa40545, 5, {0xe8, 0x44, 0xd3, 0x05, 0}, Jump(0xa40545, FetchAttributes)},
    };
    const bool installed = image[patches.front().address - GameBase] == patches.front().replacement.front();
    for (const auto& patch : patches)
    {
        const auto& expected = installed ? patch.replacement : patch.original;
        if (std::memcmp(image + (patch.address - GameBase), expected.data(), patch.size) != 0) return false;
    }
    if (installed) return true;

    for (std::size_t i = 0; i < patches.size(); ++i)
    {
        const auto& patch = patches[i];
        if (memory_patch::WriteBytes(patch.address, patch.replacement.data(), patch.size)) continue;
        for (std::size_t count = i + 1; count > 0; --count)
        {
            const auto& prior = patches[count - 1];
            memory_patch::WriteBytes(prior.address, prior.original.data(), prior.size);
        }
        return false;
    }
    return true;
}
