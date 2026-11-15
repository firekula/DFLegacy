#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::countdown_coin
{
    bool Install() noexcept;
    namespace detail
    {
        struct Receipt
        {
            unsigned Success;
            int Category;
            unsigned Commodity;
            int Error;
            int Slot;
            int Gift;
        };
        struct PriceDraw { int X; int Y; void* Sprite; };
        struct Drawing
        {
            void* (__thiscall* Sprite)(void*, int);
            void (__thiscall* SelectFont)(void*, void*);
            void (__cdecl* Text)(void*, int, int, int, const char*, ...);
            void (__thiscall* RestoreFont)(void*);
        };
        bool HandleReceipt(void* ui, void* shop, const Receipt* receipt) noexcept;
        bool DrawPrice(void* ui, void* renderer, const PriceDraw* draw,
            int effectivePrice, const Drawing& operations) noexcept;
        using ReplyStub = std::array<unsigned char, 47>;
        using PriceStub = std::array<unsigned char, 48>;
        ReplyStub BuildReplyStub(std::uintptr_t address, std::uintptr_t helper,
            std::uintptr_t uiAddress, std::uintptr_t resume) noexcept;
        PriceStub BuildPriceStub(std::uintptr_t address, std::uintptr_t helper,
            std::uintptr_t uiAddress, std::uintptr_t priceAddress,
            std::uintptr_t originalDraw) noexcept;
        using PatchWriter = bool (*)(std::uintptr_t, const void*, std::size_t);
        bool InstallImage(std::uintptr_t image, std::uintptr_t replyStub,
            std::uintptr_t priceStub, PatchWriter write) noexcept;
    }
}
