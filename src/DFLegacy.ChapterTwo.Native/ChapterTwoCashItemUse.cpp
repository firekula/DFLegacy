#include "ChapterTwoCashItemUse.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"

#include <array>
#include <cstring>
#include <limits>

namespace
{
constexpr std::uintptr_t ImageBase = 0x400000;
constexpr std::size_t ImageSize = 0xe57000;
constexpr std::uintptr_t TypeLoad = 0x467d11;
constexpr unsigned char OriginalLoad[]{0x8b,0x86,0xc0,0x00,0x00,0x00};

// The stock getter has already saved ESI and read the scene. ESI points to
// the item (+10h ID, +C0h kind). EAX feeds only the native dispatch below.
// For supported containers, dispatch through the existing mode-1 return
// shared by kinds 8 and 9. The item's actual kind never changes.
__declspec(naked) void SelectUseModeBranch()
{
    __asm {
        mov eax, [esi + 0c0h]
        cmp eax, 12
        je package
        cmp eax, 13
        jne original
        cmp dword ptr [esi + 10h], 38
        jb original
        cmp dword ptr [esi + 10h], 41
        jbe directUse
        cmp dword ptr [esi + 10h], 52
        jb original
        cmp dword ptr [esi + 10h], 56
        ja original
        jmp directUse
    package:
        cmp dword ptr [esi + 10h], 49
        jne original
    directUse:
        mov eax, 8
    original:
        ret
    }
}

template<std::size_t Size>
bool Matches(const unsigned char* image, std::uintptr_t address, const unsigned char (&signature)[Size]) noexcept
{
    return std::memcmp(image + address - ImageBase, signature, Size) == 0;
}

template<class T>
T Field(const unsigned char* object, std::size_t offset) noexcept
{
    T result{};
    std::memcpy(&result, object + offset, sizeof(result));
    return result;
}

bool IsContractProduct(const unsigned char* record) noexcept
{
    // Only called after the native RTTI/type-12 classifier succeeded.
    return Field<std::uint32_t>(record, 0) == 109049 &&
        Field<std::uint32_t>(record, 0xc) == 49 &&
        Field<std::uint32_t>(Field<const unsigned char*>(record, 0x50), 0x10) == 49;
}

bool NativePackage(void* shop, const unsigned char* record)
{
    return reinterpret_cast<bool(__thiscall*)(void*, const unsigned char*)>(0x93ea10)(shop, record);
}

// The stock driver has already passed job, level and recipient checks.
// The old package preview only offers avatar selections and rejects item 49.
// For its self purchase, use the existing checkout and its original record.
bool __fastcall NeedsPackagePreview(unsigned char* shop, void*, const unsigned char* record)
{
    return NativePackage(shop, record) &&
        (shop[0x3685] != 0 || !IsContractProduct(record));
}

bool __fastcall NeedsPackageSelections(
    void* shop, const unsigned char* checkout, const unsigned char* record)
{
    return NativePackage(shop, record) &&
        (Field<std::uint32_t>(checkout, 0x958) != 0 || !IsContractProduct(record));
}

// The writer holds the checkout window in EDI. Its stock thiscall already
// supplies shop in ECX and the record on the stack, with a callee pop of 4.
__declspec(naked) void CheckoutSelections()
{
    __asm {
        mov edx, edi
        jmp NeedsPackageSelections
    }
}
}

bool dfl::chapter_two::cash_item_use_detail::InstallImage(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    static_assert(sizeof(void*) == 4, "The native cash item hook requires x86.");
    if (!image || bytes != ImageSize || !write ||
        reinterpret_cast<std::uintptr_t>(image) > std::numeric_limits<std::uintptr_t>::max() - ImageSize)
        return false;

    constexpr unsigned char getterPrefix[]{
        0x56,0x8b,0xf1,0x8b,0x0d,0xac,0x64,0xd5,0x00,0x83,0xc1,0x10,0xe8,0x9f,0xed,0x5d,0x00
    };
    constexpr unsigned char dispatch[]{
        0x83,0xf8,0x11,0x0f,0x87,0x16,0x01,0x00,0x00,0x0f,0xb6,0x80,0x58,0x7e,0x46,0x00,
        0xff,0x24,0x85,0x3c,0x7e,0x46,0x00
    };
    constexpr unsigned char modeOne[]{0xb8,0x01,0x00,0x00,0x00,0x5e,0xc3};
    constexpr unsigned char modeZeroAndTable[]{
        0x33,0xc0,0x5e,0xc3,0x8b,0xff,0x2e,0x7d,0x46,0x00,0xe1,0x7d,0x46,0x00,0x0e,0x7e,
        0x46,0x00,0x15,0x7e,0x46,0x00,0xf0,0x7d,0x46,0x00,0xda,0x7d,0x46,0x00,0x36,0x7e,
        0x46,0x00,0x00,0x01,0x02,0x03,0x03,0x02,0x06,0x04,0x05,0x05,0x02,0x02,0x06,0x06,
        0x02,0x00,0x02,0x02
    };
    constexpr unsigned char getterVtableEntry[]{0x00,0x7d,0x46,0x00};
    constexpr unsigned char packageParser[]{
        0x68,0x18,0x2a,0xc0,0x00,0x8d,0x4d,0xe4,0xe8,0xd4,0xd3,0xc8,0xff,0x84,0xc0,0x74,0x1e,0x53,
        0xc7,0x86,0x88,0x00,0x00,0x00,0x0c,0x00,0x00,0x00
    };
    constexpr unsigned char boosterParser[]{
        0x68,0x08,0x2a,0xc0,0x00,0x8d,0x4d,0xe4,0xe8,0xa5,0xd3,0xc8,0xff,0x84,0xc0,0x74,0x1e,0x53,
        0xc7,0x86,0x88,0x00,0x00,0x00,0x0d,0x00,0x00,0x00
    };
    constexpr unsigned char copyKind[]{0x8b,0x87,0x88,0x00,0x00,0x00,0x89,0x83,0xc0,0x00,0x00,0x00};
    constexpr char packageTag[] = "[cera package]";
    constexpr char boosterTag[] = "[cera booster]";
    if (!Matches(image, 0x467d00, getterPrefix) || !Matches(image, 0x467d17, dispatch) ||
        !Matches(image, 0x467dda, modeOne) || !Matches(image, 0x467e36, modeZeroAndTable) ||
        !Matches(image, 0xaef754, getterVtableEntry) || !Matches(image, 0x46a333, copyKind) ||
        !Matches(image, 0x800a9f, packageParser) || !Matches(image, 0x800ace, boosterParser) ||
        std::memcmp(image + (0xc02a18 - ImageBase), packageTag, sizeof(packageTag)) != 0 ||
        std::memcmp(image + (0xc02a08 - ImageBase), boosterTag, sizeof(boosterTag)) != 0)
        return false;

    auto* site = image + (TypeLoad - ImageBase);
    const auto address = reinterpret_cast<std::uintptr_t>(site);
    // x86 rel32 displacement is computed modulo the 32-bit address space.
    const auto relative = static_cast<std::uint32_t>(
        reinterpret_cast<std::uintptr_t>(&SelectUseModeBranch) - address - 5);
    unsigned char call[sizeof(OriginalLoad)]{0xe8, 0, 0, 0, 0, 0x90};
    std::memcpy(call + 1, &relative, sizeof(relative));
    if (std::memcmp(site, call, sizeof(call)) == 0) return true;
    if (std::memcmp(site, OriginalLoad, sizeof(OriginalLoad)) != 0) return false;
    if (write(address, call, sizeof(call)) && std::memcmp(site, call, sizeof(call)) == 0)
        return true;
    // A protection or cache flush failure can be reported after copying.
    write(address, OriginalLoad, sizeof(OriginalLoad));
    return false;
}

bool dfl::chapter_two::InstallCashItemUse(unsigned char* image, std::size_t bytes) noexcept
{
    return reinterpret_cast<std::uintptr_t>(image) == ImageBase &&
        cash_item_use_detail::InstallImage(image, bytes, memory_patch::WriteBytes);
}

bool dfl::chapter_two::cash_item_use_detail::InstallCheckoutImage(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    if (!image || bytes != ImageSize || !write ||
        reinterpret_cast<std::uintptr_t>(image) > std::numeric_limits<std::uintptr_t>::max() - ImageSize)
        return false;

    constexpr unsigned char classify[]{
        0x55,0x8b,0xec,0x56,0x8b,0x75,0x08,0x8b,0x4e,0x50,0x8b,0x01,0xff,0x10,0x83,0xf8,
        0x01,0x75,0x34,0x8b,0x46,0x50,0x6a,0x00,0x68,0x80,0x3b,0xcb,0x00,0x68,0x44,0x28,
        0xcb,0x00,0x6a,0x00,0x50,0xe8,0x73,0x18,0x12,0x00,0x83,0xc4,0x14,0x85,0xc0,0x74,
        0x16,0x8b,0x10,0x8b,0xc8,0xff,0x92,0xa8,0x00,0x00,0x00,0x83,0x38,0x0c,0x75,0x07,
        0xb0,0x01,0x5e,0x5d,0xc2,0x04,0x00,0x32,0xc0,0x5e,0x5d,0xc2,0x04,0x00
    };
    constexpr unsigned char eligibility[]{
        0x8a,0x86,0x86,0x36,0x00,0x00,0x84,0xc0,0x8d,0x9e,0x84,0x36,0x00,0x00,0x74,0x7a,
        0x8a,0x43,0x03,0x84,0xc0,0x74,0x73,0x8a,0x43,0x04,0x84,0xc0,0x75,0x07,0x8a,0x43,
        0x01,0x84,0xc0,0x75,0x65
    };
    constexpr unsigned char cartPrefix[]{0x8d,0x45,0x9c,0x50,0x8b,0xce,0xc6,0x45,0xf0,0x00};
    constexpr unsigned char cartSuffix[]{0x84,0xc0,0x75,0x5b,0x83,0xc7,0x58,0x81,0xff,0xe0,0x06,0x00,0x00,0x7c,0xc8};
    constexpr unsigned char singlePrefix[]{0x8d,0x4d,0x9c,0x51,0x8b,0xce,0xc6,0x45,0xf0,0x00};
    constexpr unsigned char singleSuffix[]{0x84,0xc0,0x74,0xb0,0x8d,0x55,0x9c,0x52,0x8b,0xce,0xe8,0x6c,0xf6,0xff,0xff};
    constexpr unsigned char checkoutEntry[]{0x8b,0x4e,0x18,0x53,0xe8,0x0e,0x8e,0x00,0x00};
    constexpr unsigned char giftMode[]{0x8a,0x87,0x58,0x09,0x00,0x00,0x6a,0x01,0x8d,0x4d,0xf0,0x51};
    constexpr unsigned char serializerPrefix[]{0x8b,0x4f,0x08,0x56};
    constexpr unsigned char serializerSuffix[]{0x84,0xc0,0x0f,0x84,0xc5,0x00,0x00,0x00,0x8d,0x4d,0xcc,0x51};
    constexpr unsigned char emptySelections[]{0x8b,0x0d,0x98,0xcc,0xdf,0x00,0x6a,0x01,0x8d,0x45,0xec,0x50,0xc6,0x45,0xec,0x00};
    if (!Matches(image, 0x93ea10, classify) || !Matches(image, 0x943878, eligibility) ||
        !Matches(image, 0x9438cb, cartPrefix) || !Matches(image, 0x9438da, cartSuffix) ||
        !Matches(image, 0x943926, singlePrefix) || !Matches(image, 0x943935, singleSuffix) ||
        !Matches(image, 0x9438e9, checkoutEntry) || !Matches(image, 0x94b7a7, giftMode) ||
        !Matches(image, 0x94b875, serializerPrefix) || !Matches(image, 0x94b87e, serializerSuffix) ||
        !Matches(image, 0x94b94b, emptySelections))
        return false;

    struct CallSite
    {
        std::uintptr_t location;
        std::uintptr_t replacement;
        std::array<unsigned char, 5> original;
    };
    const CallSite calls[]{
        {0x9438d5, reinterpret_cast<std::uintptr_t>(&NeedsPackagePreview), {0xe8,0x36,0xb1,0xff,0xff}},
        {0x943930, reinterpret_cast<std::uintptr_t>(&NeedsPackagePreview), {0xe8,0xdb,0xb0,0xff,0xff}},
        {0x94b879, reinterpret_cast<std::uintptr_t>(&CheckoutSelections), {0xe8,0x92,0x31,0xff,0xff}},
    };
    constexpr auto count = sizeof(calls) / sizeof(calls[0]);
    std::array<std::array<unsigned char, 5>, count> replacements{};
    std::array<unsigned char*, count> addresses{};
    std::size_t installed = 0;
    for (std::size_t index = 0; index < count; ++index)
    {
        const auto& call = calls[index];
        auto* site = image + (call.location - ImageBase);
        addresses[index] = site;
        auto& replacement = replacements[index];
        replacement[0] = 0xe8;
        const auto relative = static_cast<std::uint32_t>(
            call.replacement - reinterpret_cast<std::uintptr_t>(site) - 5);
        std::memcpy(replacement.data() + 1, &relative, sizeof(relative));
        if (std::memcmp(site, replacement.data(), replacement.size()) == 0)
            ++installed;
        else if (std::memcmp(site, call.original.data(), call.original.size()) != 0)
            return false;
    }
    if (installed != 0)
        return installed == count;

    for (std::size_t index = 0; index < count; ++index)
    {
        const auto& replacement = replacements[index];
        const auto address = reinterpret_cast<std::uintptr_t>(addresses[index]);
        if (write(address, replacement.data(), replacement.size()) &&
            std::memcmp(addresses[index], replacement.data(), replacement.size()) == 0)
            continue;
        for (std::size_t rollback = 0; rollback <= index; ++rollback)
        {
            const auto& original = calls[rollback].original;
            write(reinterpret_cast<std::uintptr_t>(addresses[rollback]), original.data(), original.size());
        }
        return false;
    }
    return true;
}

bool dfl::chapter_two::InstallCashPackageCheckout(unsigned char* image, std::size_t bytes) noexcept
{
    return reinterpret_cast<std::uintptr_t>(image) == ImageBase &&
        cash_item_use_detail::InstallCheckoutImage(image, bytes, memory_patch::WriteBytes);
}
