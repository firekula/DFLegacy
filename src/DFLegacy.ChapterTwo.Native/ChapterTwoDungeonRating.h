#pragma once

#include "ChapterTwoLootGather.h"

namespace dfl::chapter_two
{
    bool InstallDungeonRating(unsigned char* image, std::size_t bytes) noexcept;
}

namespace dfl::chapter_two::dungeon_rating_detail
{
    bool TryActivate(const loot_gather_detail::Runtime&, const char* body, unsigned int uid) noexcept;
    bool InstallImage(unsigned char*, std::size_t, loot_gather_detail::PatchWriter) noexcept;
}
