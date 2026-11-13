#pragma once

#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
bool InstallCashItemUse(unsigned char* image, std::size_t bytes) noexcept;
bool InstallCashPackageCheckout(unsigned char* image, std::size_t bytes) noexcept;

namespace cash_item_use_detail
{
using PatchWriter = bool(*)(std::uintptr_t, const void*, std::size_t);
bool InstallImage(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
bool InstallCheckoutImage(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
}
}
