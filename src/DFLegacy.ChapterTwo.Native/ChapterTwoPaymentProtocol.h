#pragma once

#include <string>
#include <string_view>
#include <vector>

namespace dfl::chapter_two
{
    inline constexpr std::size_t MaximumPaymentForm = 1024;
    inline constexpr std::size_t MaximumPaymentReply = 2 * 1024 * 1024;

    struct PaymentReply
    {
        std::string mime;
        std::vector<unsigned char> body;
    };

    using PaymentFetch = bool (*)(std::string_view route, std::string_view form, PaymentReply& reply);

    // Registers a process-local handler. All URLs outside this exact payment
    // origin/prefix fall back to the normal, sandboxed browser protocol.
    bool RegisterPaymentProtocol(std::wstring_view prefix, PaymentFetch fetch) noexcept;

    // Before the game CRT runs, redirect the verified native browser call.
    // URLMon registration waits for the game's actual COM browser thread.
    bool InstallPaymentProtocol(unsigned char* image, std::size_t bytes) noexcept;
}
