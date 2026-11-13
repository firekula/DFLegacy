#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>

#ifndef CHAPTER_TWO_CASH_USE_BASELINE
#include "ChapterTwoCashItemUse.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char cashUseFixtureReserve[0x1300000]{1};

namespace
{
constexpr std::uintptr_t ImageBase = 0x400000;
constexpr std::size_t ImageSize = 0xe57000;
constexpr std::size_t HookOffset = 0x67d11;
unsigned failures = 0;
unsigned checks = 0;

void Check(bool ok, const char* label, unsigned id = 0, unsigned kind = 0)
{
    ++checks;
    if (!ok)
    {
        ++failures;
        std::fprintf(stderr, "FAIL cash item use: %s (item=%u, kind=%u)\n", label, id, kind);
    }
}

std::vector<unsigned char> Load(const wchar_t* path)
{
    std::ifstream file(path, std::ios::binary);
    const std::vector<unsigned char> raw{std::istreambuf_iterator<char>(file), {}};
    if (raw.size() < sizeof(IMAGE_DOS_HEADER)) return {};
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
        static_cast<std::size_t>(dos->e_lfanew) > raw.size() ||
        sizeof(IMAGE_NT_HEADERS32) > raw.size() - dos->e_lfanew) return {};
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data() + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->FileHeader.SizeOfOptionalHeader != sizeof(IMAGE_OPTIONAL_HEADER32) ||
        nt->OptionalHeader.ImageBase != ImageBase || nt->OptionalHeader.SizeOfImage != ImageSize ||
        nt->OptionalHeader.SizeOfHeaders > raw.size() || nt->OptionalHeader.SizeOfHeaders > ImageSize) return {};
    const auto sectionOffset = static_cast<std::size_t>(dos->e_lfanew) + sizeof(IMAGE_NT_HEADERS32);
    if (nt->FileHeader.NumberOfSections > (raw.size() - sectionOffset) / sizeof(IMAGE_SECTION_HEADER)) return {};
    const auto* sections = reinterpret_cast<const IMAGE_SECTION_HEADER*>(raw.data() + sectionOffset);
    std::vector<unsigned char> image(ImageSize);
    std::memcpy(image.data(), raw.data(), nt->OptionalHeader.SizeOfHeaders);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
    {
        const auto& s = sections[i];
        if (s.VirtualAddress > ImageSize || s.SizeOfRawData > ImageSize - s.VirtualAddress ||
            s.PointerToRawData > raw.size() || s.SizeOfRawData > raw.size() - s.PointerToRawData) return {};
        std::memcpy(image.data() + s.VirtualAddress, raw.data() + s.PointerToRawData, s.SizeOfRawData);
    }
    return image;
}

template<class T>
void Put(std::uintptr_t address, T value)
{
    std::memcpy(reinterpret_cast<void*>(address), &value, sizeof(value));
}

void Jump(std::uintptr_t address, std::uintptr_t target)
{
    Put<unsigned char>(address, 0xe9);
    Put<std::uint32_t>(address + 1, static_cast<std::uint32_t>(target - address - 5));
}

alignas(4) std::array<unsigned char, 0x1c0> item{};
unsigned sceneReads = 0;
void* lastSceneAddress = nullptr;
unsigned __fastcall ReadScene(void* scene, void*)
{
    ++sceneReads;
    lastSceneAddress = scene;
    return 1;
}

unsigned beforeEsp = 0, afterEsp = 0;
unsigned afterEbx = 0, afterEsi = 0, afterEdi = 0, afterEbp = 0;
unsigned result = 0;
// Exercise the real stock vtable +B0 entry with native thiscall ABI. Only
// the protected scene reader is replaced; the getter and its jump tables run.
__declspec(naked) void CallGetter()
{
    __asm {
        pushad
        mov ebx, 11223344h
        mov esi, 22334455h
        mov edi, 33445566h
        mov ebp, 44556677h
        lea ecx, item
        mov eax, [ecx]
        mov beforeEsp, esp
        call dword ptr [eax + 0b0h]
        mov result, eax
        mov afterEsp, esp
        mov afterEbx, ebx
        mov afterEsi, esi
        mov afterEdi, edi
        mov afterEbp, ebp
        popad
        ret
    }
}

struct Example
{
    unsigned id, kind, original, restored;
};
constexpr Example examples[]{
    {38, 13, 0, 1}, {39, 13, 0, 1}, {40, 13, 0, 1}, {41, 13, 0, 1},
    {49, 12, 0, 1},
    {52, 13, 0, 1}, {53, 13, 0, 1}, {54, 13, 0, 1}, {55, 13, 0, 1}, {56, 13, 0, 1},
    {27, 12, 0, 0}, {49, 13, 0, 0}, {38, 12, 0, 0}, {56, 10, 2, 2},
    {63, 13, 0, 0}, {67, 13, 0, 0}, {69, 13, 0, 0}, {73, 13, 0, 0},
    {86, 13, 0, 0}, {89, 13, 0, 0}, {92, 13, 0, 0}, {95, 13, 0, 0}, {98, 13, 0, 0},
    {37, 13, 0, 0}, {42, 13, 0, 0}, {48, 12, 0, 0}, {50, 12, 0, 0},
    {51, 13, 0, 0}, {57, 13, 0, 0}, {0, 13, 0, 0}, {0xffffffff, 13, 0, 0},
    {16, 10, 2, 2}, {1121, 10, 2, 2}, {49, 8, 1, 1}, {38, 9, 1, 1},
    {38, 0x10d, 0, 0}, {38, 0xffffffff, 0, 0},
};

void Exercise(bool installed)
{
    Put(reinterpret_cast<std::uintptr_t>(item.data()), std::uint32_t{0xaef6a4});
    for (const auto& example : examples)
    {
        const auto address = reinterpret_cast<std::uintptr_t>(item.data());
        // Contradictory decoys detect an accidental copy of C1 offsets.
        Put(address + 0x0c, std::uint32_t{63});
        Put(address + 0xac, std::uint32_t{10});
        Put(address + 0x10, example.id);
        Put(address + 0xc0, example.kind);
        const auto savedItem = item;
        const auto beforeReads = sceneReads;
        CallGetter();
        Check(result == (installed ? example.restored : example.original),
            installed ? "restored getter mode" : "original getter mode", example.id, example.kind);
        Check(beforeEsp == afterEsp && afterEbx == 0x11223344 && afterEsi == 0x22334455 &&
            afterEdi == 0x33445566 && afterEbp == 0x44556677,
            "thiscall preserves stack and nonvolatile registers", example.id, example.kind);
        Check(sceneReads == beforeReads + 1 && lastSceneAddress == reinterpret_cast<void*>(0xd60010),
            "native scene read is retained", example.id, example.kind);
        Check(item == savedItem, "getter preserves item layout and contents", example.id, example.kind);
    }
}

// Exercise the actual shop driver and CMD67 writer against one native record.
// The seams stand in for RTTI, UI display, heap-backed record strings and
// network primitives. The original job, level and package predicates run.
alignas(4) std::array<unsigned char, 0x3700> shop{};
alignas(4) std::array<unsigned char, 0xb00> checkout{};
alignas(4) std::array<unsigned char, 0x6e0> records{};
alignas(4) std::array<unsigned char, 0xc0> recordCopy{};
alignas(4) std::array<unsigned char, 0x100> secureState{};
unsigned previewCalls = 0, checkoutCalls = 0, warnings = 0, selectionCalls = 0;
unsigned command = 0, sends = 0;
std::array<unsigned char, 64> payload{};
std::size_t payloadSize = 0;

template<class T>
void Set(unsigned char* object, std::size_t offset, T value)
{
    Put(reinterpret_cast<std::uintptr_t>(object + offset), value);
}

void* __cdecl Cast(void* object, int delta, void* source, void* target, int reference)
{
    Check(delta == 0 && source == reinterpret_cast<void*>(0xcb2844) &&
        target == reinterpret_cast<void*>(0xcb3b80) && reference == 0, "stackable RTTI boundary");
    return object == item.data() ? object : nullptr;
}

void __fastcall ConstructRecord(void* destination, void*)
{
    std::memset(destination, 0, 0x58);
}

void __fastcall CopyRecord(void* destination, void*, const void* source)
{
    std::memcpy(destination, source, 0x58);
}

void __fastcall EmptyDestructor(void*, void*) {}
bool __fastcall Preview(void* window, void*, const void* record)
{
    ++previewCalls;
    Check(window == shop.data() && record != nullptr, "original package preview boundary");
    return false;
}

void __fastcall Checkout(void* window, void*, const unsigned char* args)
{
    ++checkoutCalls;
    Check(window == checkout.data() && args == shop.data() + 0x3684, "original checkout boundary");
}

bool __fastcall ShowWarning(void*, void*, unsigned key, unsigned mode)
{
    ++warnings;
    Check((key == 0x15bb && mode == 1) || (key == 0x15b9 && mode == 0), "original job and level notices");
    return true;
}

void __fastcall StartPacket(void*, void*, unsigned cmd)
{
    command = cmd;
    payloadSize = 0;
}

void __fastcall AppendPacket(void*, void*, const unsigned char* bytes, unsigned length)
{
    if (length <= payload.size() - payloadSize)
    {
        std::memcpy(payload.data() + payloadSize, bytes, length);
        payloadSize += length;
    }
}

void __cdecl SendPacket() { ++sends; }
void __fastcall ClearCart(void*, void*, unsigned) {}

void* __fastcall GetSelections(void*, void*, unsigned char* vector)
{
    // Preexisting selections from a historical avatar package must never
    // affect this contract. Expose one deterministic stale record.
    ++selectionCalls;
    static unsigned char* stale[]{recordCopy.data()};
    Set(vector, 4, stale);
    Set(vector, 8, stale + 1);
    Set(vector, 12, stale + 1);
    return vector;
}

void __cdecl FreeFixture(void*) {}

void PreparePurchase(unsigned commodity, unsigned id, unsigned kind, bool gift, bool cart)
{
    shop.fill(0);
    checkout.fill(0);
    records.fill(0);
    item.fill(0);
    recordCopy.fill(0);
    Set(item.data(), 0, std::uint32_t{0xaef6a4});
    Set(item.data(), 4, std::uint32_t{1});
    Set(item.data(), 8, std::uint32_t{1});
    Set(item.data(), 0x10, id);
    std::memset(item.data() + 0x4c, 1, 5);
    Set(item.data(), 0xc0, kind);
    Set(records.data(), 0, commodity);
    Set(records.data(), 4, std::uint32_t{4});
    Set(records.data(), 0x0c, id);
    Set(records.data(), 0x10, static_cast<unsigned char>(0xff));
    Set(records.data(), 0x50, item.data());
    Set(recordCopy.data(), 0x50, item.data());
    Set(shop.data(), 0x18, checkout.data());
    Set(shop.data(), 0x3684, static_cast<unsigned char>(cart));
    Set(shop.data(), 0x3685, static_cast<unsigned char>(gift));
    Set(shop.data(), 0x3688, static_cast<unsigned char>(gift));
    Set(shop.data(), 0x368c, records.data());
    Set(shop.data(), 0x3694, std::uint32_t{60});
    Set(shop.data(), 0x3698, std::uint32_t{3});
    Set(checkout.data(), 8, shop.data());
    Set(checkout.data(), 0xc, secureState.data());
    Set(checkout.data(), 0x7a4, std::uint32_t{1});
    Set(checkout.data(), 0x958, static_cast<std::uint32_t>(gift));
    std::memcpy(checkout.data() + 0xb4, records.data(), 0x58);
    previewCalls = checkoutCalls = warnings = selectionCalls = command = sends = 0;
    payloadSize = 0;
}

void ExercisePurchase()
{
    using NativeFunction = void(__thiscall*)(void*);
    const auto begin = reinterpret_cast<NativeFunction>(0x9437b0);
    const auto buy = reinterpret_cast<NativeFunction>(0x94b770);
    for (const bool cart : {false, true})
    {
        PreparePurchase(109049, 49, 12, false, cart);
        begin(shop.data());
        Check(checkoutCalls == 1 && !previewCalls && !warnings,
            cart ? "contract cart reaches ordinary checkout" : "contract single purchase reaches ordinary checkout");
    }
    for (const auto& other : std::array<std::array<unsigned, 4>, 5>{{
             {100060, 49, 12, 0}, {109049, 27, 12, 0}, {109049, 49, 12, 1},
             {109049, 49, 13, 0}, {100054, 201, 10, 0}}})
    {
        PreparePurchase(other[0], other[1], other[2], other[3] != 0, false);
        begin(shop.data());
        Check(previewCalls == (other[2] == 12 ? 1u : 0u) && checkoutCalls == (other[2] == 12 ? 0u : 1u),
            "other products and gifts retain their original checkout routes", other[1], other[2]);
    }
    PreparePurchase(109049, 49, 12, false, false);
    item[0x4f] = 0;
    begin(shop.data());
    Check(warnings == 1 && !checkoutCalls && !previewCalls, "original job gate still runs");
    PreparePurchase(109049, 49, 12, false, false);
    Set(shop.data(), 0x3694, std::uint32_t{0});
    begin(shop.data());
    Check(warnings == 1 && !checkoutCalls && !previewCalls, "original level gate still runs");
    PreparePurchase(109049, 49, 12, true, false);
    shop[0x3688] = 0;
    begin(shop.data());
    Check(!checkoutCalls && !previewCalls && !warnings, "original gift confirmation gate still runs");

    PreparePurchase(109049, 49, 12, false, false);
    buy(checkout.data());
    constexpr std::array<unsigned char, 10> contractBody{0, 1, 0xff, 0, 0, 0xf9, 0xa9, 1, 0, 0};
    Check(command == 67 && sends == 1 && selectionCalls == 0,
        "contract uses the original CMD67 writer with the ordinary selection branch");
    Check(payloadSize == contractBody.size() &&
        std::memcmp(payload.data(), contractBody.data(), contractBody.size()) == 0,
        "CMD67 preserves commodity and writes zero package selections");
    PreparePurchase(100060, 49, 12, false, false);
    buy(checkout.data());
    Check(command == 67 && sends == 1 && selectionCalls == 1 && payloadSize > contractBody.size(),
        "original legacy package serializer retains its selections");
}

void BindPurchaseFixture()
{
    Set(secureState.data(), 0x10, static_cast<unsigned char>(1));
    Jump(0xa602ad, reinterpret_cast<std::uintptr_t>(&Cast));
    Jump(0x7f13e0, reinterpret_cast<std::uintptr_t>(&ConstructRecord));
    Jump(0x7ef140, reinterpret_cast<std::uintptr_t>(&CopyRecord));
    Jump(0x86a6e0, reinterpret_cast<std::uintptr_t>(&EmptyDestructor));
    Jump(0x942fb0, reinterpret_cast<std::uintptr_t>(&Preview));
    Jump(0x94c700, reinterpret_cast<std::uintptr_t>(&Checkout));
    Jump(0x93e6d0, reinterpret_cast<std::uintptr_t>(&ShowWarning));
    Jump(0x942520, reinterpret_cast<std::uintptr_t>(&EmptyDestructor));
    Jump(0x9cbc20, reinterpret_cast<std::uintptr_t>(&StartPacket));
    Jump(0x9cbae0, reinterpret_cast<std::uintptr_t>(&AppendPacket));
    Jump(0x9cbcc0, reinterpret_cast<std::uintptr_t>(&SendPacket));
    Jump(0x944a40, reinterpret_cast<std::uintptr_t>(&ClearCart));
    Jump(0x93ff90, reinterpret_cast<std::uintptr_t>(&EmptyDestructor));
    Jump(0x94c7e0, reinterpret_cast<std::uintptr_t>(&GetSelections));
    Jump(0xa5f27d, reinterpret_cast<std::uintptr_t>(&FreeFixture));
    Put(0xcb26c0, static_cast<unsigned char>(1));
}

#ifndef CHAPTER_TWO_CASH_USE_BASELINE
unsigned writes = 0;
unsigned failAt = 0;
bool failAfter = false;
bool Write(std::uintptr_t address, const void* bytes, std::size_t size)
{
    ++writes;
    if (writes == failAt && !failAfter) return false;
    std::memcpy(reinterpret_cast<void*>(address), bytes, size);
    return writes != failAt;
}

void TestInstallation(const std::vector<unsigned char>& original)
{
    using dfl::chapter_two::cash_item_use_detail::InstallImage;
    auto copy = original;
    Check(!InstallImage(nullptr, ImageSize, Write) &&
          !InstallImage(copy.data(), ImageSize - 1, Write) &&
          !InstallImage(copy.data(), ImageSize, nullptr) && writes == 0,
          "invalid installation rejected without writes");
    for (const auto offset : {0x67d00u, 0x67d11u, 0x67d27u, 0x67ddau,
                              0x67e50u, 0x67e64u, 0x6ef754u, 0x400ab1u, 0x400ae0u})
    {
        copy = original;
        copy[offset] ^= 1;
        writes = 0;
        Check(!InstallImage(copy.data(), ImageSize, Write) && writes == 0,
            "changed original signature rejected without writes", offset);
    }
    for (const bool after : {false, true})
    {
        copy = original;
        writes = 0;
        failAt = 1;
        failAfter = after;
        Check(!InstallImage(copy.data(), ImageSize, Write) && copy == original,
            "failed write restores the original bytes");
    }
    copy = original;
    writes = failAt = 0;
    Check(InstallImage(copy.data(), ImageSize, Write) && writes == 1,
        "recognized image installs one detour");
    Check(std::memcmp(copy.data(), original.data(), HookOffset) == 0 &&
          std::memcmp(copy.data() + HookOffset + 6, original.data() + HookOffset + 6,
              ImageSize - HookOffset - 6) == 0, "only the six-byte getter instruction changes");
    writes = 0;
    Check(InstallImage(copy.data(), ImageSize, Write) && writes == 0,
        "repeat installation is idempotent");
    copy[HookOffset + 1] ^= 1;
    Check(!InstallImage(copy.data(), ImageSize, Write) && writes == 0,
        "a foreign detour is rejected without writes");
}

void TestCheckoutInstallation(const std::vector<unsigned char>& original)
{
    using dfl::chapter_two::cash_item_use_detail::InstallCheckoutImage;
    constexpr std::array<std::size_t, 3> sites{0x5438d5, 0x543930, 0x54b879};
    auto copy = original;
    writes = failAt = 0;
    Check(!InstallCheckoutImage(nullptr, ImageSize, Write) &&
        !InstallCheckoutImage(copy.data(), ImageSize - 1, Write) &&
        !InstallCheckoutImage(copy.data(), ImageSize, nullptr) && writes == 0,
        "invalid checkout image rejected without writing");
    for (const std::size_t offset : {0x53ea10u, 0x543878u, 0x5438cbu, 0x5438d5u,
             0x5438e9u, 0x543930u, 0x54b7a7u, 0x54b879u, 0x54b880u, 0x54b94bu})
    {
        copy = original;
        copy[offset] ^= 1;
        writes = 0;
        Check(!InstallCheckoutImage(copy.data(), ImageSize, Write) && writes == 0,
            "changed checkout signature rejected without writing", static_cast<unsigned>(offset));
    }
    for (const bool after : {false, true})
    {
        for (unsigned site = 1; site <= sites.size(); ++site)
        {
            copy = original;
            writes = 0;
            failAt = site;
            failAfter = after;
            Check(!InstallCheckoutImage(copy.data(), ImageSize, Write) && copy == original,
                "failed checkout install restores all original calls", site);
        }
    }
    copy = original;
    writes = failAt = 0;
    Check(InstallCheckoutImage(copy.data(), ImageSize, Write) && writes == sites.size(),
        "checkout install rewrites all three original calls");
    auto untouched = copy;
    for (const auto site : sites)
        std::memcpy(untouched.data() + site, original.data() + site, 5);
    Check(untouched == original, "checkout install modifies only the three verified calls");
    writes = 0;
    Check(InstallCheckoutImage(copy.data(), ImageSize, Write) && writes == 0, "checkout install is idempotent");
    std::memcpy(copy.data() + sites[1], original.data() + sites[1], 5);
    Check(!InstallCheckoutImage(copy.data(), ImageSize, Write) && writes == 0,
        "partial checkout install rejected without writing");
}
#endif
}

int wmain(int argc, wchar_t** argv)
{
    const bool purchaseOnly = argc == 3 && std::wcscmp(argv[2], L"--purchase-only") == 0;
    if (argc != 2 && !purchaseOnly) return 2;
    const auto original = Load(argv[1]);
    if (original.empty()) return 2;
#ifndef CHAPTER_TWO_CASH_USE_BASELINE
    TestInstallation(original);
    TestCheckoutInstallation(original);
#endif
    auto* image = reinterpret_cast<unsigned char*>(ImageBase);
    const auto reserve = reinterpret_cast<std::uintptr_t>(cashUseFixtureReserve);
    DWORD previousProtection = 0;
    if (reserve > ImageBase || reserve + sizeof(cashUseFixtureReserve) < ImageBase + ImageSize ||
        !VirtualProtect(image, ImageSize, PAGE_EXECUTE_READWRITE, &previousProtection)) return 2;
    std::memcpy(image, original.data(), ImageSize);
    Jump(0xa46ab0, reinterpret_cast<std::uintptr_t>(&ReadScene));
    Put(0xd564ac, std::uint32_t{0xd60000});
    if (!FlushInstructionCache(GetCurrentProcess(), image, ImageSize)) return 2;
    if (!purchaseOnly) Exercise(false);
#ifndef CHAPTER_TWO_CASH_USE_BASELINE
    Check(dfl::chapter_two::InstallCashItemUse(image, ImageSize), "production installer accepts the mapped C2 image");
    Check(dfl::chapter_two::InstallCashPackageCheckout(image, ImageSize),
        "production installer accepts the mapped C2 shop checkout");
#endif
    if (!purchaseOnly) Exercise(true);
    BindPurchaseFixture();
    if (!FlushInstructionCache(GetCurrentProcess(), image, ImageSize)) return 2;
    ExercisePurchase();
    std::printf("cash item use: %u checks, %u failures\n", checks, failures);
    return failures ? 1 : 0;
}
