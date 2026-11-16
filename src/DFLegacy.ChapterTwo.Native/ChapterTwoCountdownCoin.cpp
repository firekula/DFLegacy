#include "ChapterTwoCountdownCoin.h"
#include "memory_patch.h"
#include <cstdint>
#include <cstring>

namespace {
constexpr std::uintptr_t GameBase = 0x400000;
constexpr std::size_t GameSize = 0xe57000;
constexpr std::size_t SuccessfulReceiptCall = 0x18345;
constexpr std::uintptr_t FindVisibleProduct = 0x9410f0;
constexpr std::uintptr_t PresentPurchaseResult = 0x943cb0;
constexpr std::size_t PurchasePending = 0x263c;
constexpr std::size_t CountdownProduct = 0x35f8;
constexpr std::size_t ProductCashPrice = 0x28;
constexpr int CoinCategory = 1;

using FindProduct = const void* (__thiscall*)(void*, int, std::uint32_t);
using PresentResult = void (__thiscall*)(void*, int, int, std::uint32_t, int, int, int);

template<class T> T Read(const unsigned char* data, std::size_t offset) noexcept
{
    T value;
    std::memcpy(&value, data + offset, sizeof(value));
    return value;
}

void __fastcall PresentReceipt(unsigned char* shop, void*, int success, int category,
    std::uint32_t commodity, int error, int slot, int gift) noexcept
{
    // The last coin product is reserved for the countdown. The client copies
    // it out of the shop vector, so the usual receipt cannot find its icon.
    if (success == 1 && category == CoinCategory && commodity != 0 && error == -1
        && slot == 65535 && gift == 0 && shop && shop[PurchasePending] == 1
        && Read<std::uint32_t>(shop, CountdownProduct) == commodity
        && Read<std::int32_t>(shop, CountdownProduct + ProductCashPrice) > 0
        && !reinterpret_cast<FindProduct>(FindVisibleProduct)(shop, category, commodity)) {
        // This is the same pending flag reset as the native result routine.
        // CMD67's remaining processing and the inventory update run as usual.
        shop[PurchasePending] = 0;
        return;
    }
    reinterpret_cast<PresentResult>(PresentPurchaseResult)(shop, success, category, commodity, error, slot, gift);
}

bool Matches(const unsigned char* image, std::size_t offset, const unsigned char* signature,
    std::size_t size) noexcept
{
    return std::memcmp(image + offset, signature, size) == 0;
}
}

bool dfl::chapter_two::InstallCountdownCoin(unsigned char* image, std::size_t imageSize) noexcept
{
    constexpr unsigned char originalCall[]{0xe8, 0x66, 0xb9, 0x52, 0x00};
    constexpr unsigned char arguments[]{0x8b, 0x89, 0x88, 0x12, 0, 0, 0x6a, 0xff, 0x52, 0x50, 0x6a, 1};
    constexpr unsigned char resultLookup[]{0xe8, 0x12, 0xd4, 0xff, 0xff, 0x8b, 0xf8, 0x85, 0xff,
        0x89, 0x7d, 0x10, 0x0f, 0x84, 0x9b, 0x02, 0, 0};
    constexpr unsigned char clearPending[]{0xc6, 0x86, 0x3c, 0x26, 0, 0, 0};
    constexpr unsigned char extractProduct[]{0x8d, 0xbb, 0xf8, 0x35, 0, 0, 0x50, 0x8b, 0xcf, 0xe8};
    constexpr unsigned char sendProduct[]{0x8b, 0x96, 0xf8, 0x35, 0, 0, 0x8b, 0x0d, 0x98, 0xcc, 0xdf, 0};
    if (reinterpret_cast<std::uintptr_t>(image) != GameBase || imageSize != GameSize
        || !Matches(image, 0x18339, arguments, sizeof(arguments))
        || !Matches(image, 0x543cd9, resultLookup, sizeof(resultLookup))
        || !Matches(image, 0x543fae, clearPending, sizeof(clearPending))
        || !Matches(image, 0x542624, extractProduct, sizeof(extractProduct))
        || !Matches(image, 0x53e407, sendProduct, sizeof(sendProduct))) return false;

    const auto site = GameBase + SuccessfulReceiptCall;
    unsigned char patchedCall[]{0xe8, 0, 0, 0, 0};
    const auto distance = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&PresentReceipt)
        - site - sizeof(patchedCall));
    std::memcpy(patchedCall + 1, &distance, sizeof(distance));
    if (Matches(image, SuccessfulReceiptCall, patchedCall, sizeof(patchedCall))) return true;
    if (!Matches(image, SuccessfulReceiptCall, originalCall, sizeof(originalCall))) return false;
    if (memory_patch::WriteBytes(site, patchedCall, sizeof(patchedCall))) return true;
    memory_patch::WriteBytes(site, originalCall, sizeof(originalCall));
    return false;
}
