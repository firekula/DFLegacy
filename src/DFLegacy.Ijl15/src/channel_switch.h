#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::channel_switch
{
    bool Install() noexcept;
    namespace detail
    {
        struct Selection
        {
            void* Game{};
            void* Channel{};
            void* Network{};
            std::uintptr_t Record{};
            std::uint32_t Socket{};
            int Group{}, Category{}, Row{};
            int Armed{}, Connecting{};
            std::array<unsigned char, 104> Metadata{};
        };
        using SecureReader = int (*)(void*);
        bool ReadSelection(void* game, void* channel, void* network,
            SecureReader secureRead, Selection& result) noexcept;
        struct Operations
        {
            bool (*Capture)(void*, Selection&);
            void (*Close)(void*, void*);
            void (*Arm)(void*, void*);
        };
        void CloseAndArm(void* context, void* network, const Operations& operations) noexcept;
        bool ArmPending(void* context, void* channel, const Operations& operations) noexcept;
        using PatchWriter = bool (*)(std::uintptr_t, const void*, std::size_t);
        // Order: existing FD_CLOSE armer, then success-ACK close.
        bool InstallImage(std::uintptr_t image, const std::array<std::uintptr_t, 2>& targets, PatchWriter write) noexcept;
        bool InstallCalls(const std::array<std::uintptr_t, 2>& sites,
            const std::array<std::uintptr_t, 2>& targets, PatchWriter write) noexcept;
    }
}
