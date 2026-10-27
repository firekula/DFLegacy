#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace dfl::debug_model
{
    constexpr std::uint32_t Magic = 0x31474244;
    constexpr std::size_t MaximumHidden = 255;
#pragma pack(push, 1)
    struct Header
    {
        std::uint32_t magic, version;
        std::uint64_t fileTime, runId;
        std::uint16_t userId, dungeonId, currentMapId, bossMapId;
        std::uint8_t roomX, roomY, bossX, bossY, clearEnabled, atBoss;
        std::uint16_t count;
    };
    struct Monster
    {
        std::uint16_t uid, monster;
        std::uint8_t level, box, state, reserved;
        std::uint16_t x, y;
    };
    struct DropState
    {
        std::uint8_t enabled, allowed, result, reserved;
        std::int64_t requestId;
    };
    struct DropRequest
    {
        std::uint32_t magic = 0x31504444, version = 1;
        std::int64_t requestId = 0, runId = 0;
        std::uint16_t userId = 0;
        std::uint32_t reserved = 0;
        std::uint8_t enabled = 0, reserved2 = 0;
    };
#pragma pack(pop)
    static_assert(sizeof(Header) == 40 && sizeof(Monster) == 12);
    static_assert(sizeof(DropState) == 12 && sizeof(DropRequest) == 32);
    struct Snapshot
    {
        Header header{};
        std::array<Monster, MaximumHidden> monsters{};
        DropState drop{};
    };
    constexpr std::size_t MaximumBytes = sizeof(Header) + MaximumHidden * sizeof(Monster) + sizeof(DropState);
    inline bool Decode(const void* bytes, std::size_t size, Snapshot& output) noexcept
    {
        if (bytes == nullptr || size < sizeof(Header)) return false;
        Snapshot candidate{};
        std::memcpy(&candidate.header, bytes, sizeof(Header));
        const auto& header = candidate.header;
        if (header.magic != Magic || (header.version != 1 && header.version != 2) || header.userId == 0
            || header.runId == 0 || header.count > MaximumHidden
            || header.atBoss > 1 || header.clearEnabled > 1
            || size != sizeof(Header) + header.count * sizeof(Monster)
                + (header.version == 2 ? sizeof(DropState) : 0)) return false;
        if (header.count != 0) std::memcpy(candidate.monsters.data(),
            static_cast<const unsigned char*>(bytes) + sizeof(Header), header.count * sizeof(Monster));
        for (std::size_t i = 0; i < header.count; ++i)
        {
            if (candidate.monsters[i].state > 3 || candidate.monsters[i].reserved != 0) return false;
            for (std::size_t j = 0; j < i; ++j)
                if (candidate.monsters[i].uid == candidate.monsters[j].uid) return false;
        }
        if (header.version == 2)
        {
            std::memcpy(&candidate.drop, static_cast<const unsigned char*>(bytes)
                + sizeof(Header) + header.count * sizeof(Monster), sizeof(DropState));
            if (candidate.drop.enabled > 1 || candidate.drop.allowed > 1 || candidate.drop.result > 3
                || candidate.drop.reserved != 0 || candidate.drop.requestId < 0) return false;
        }
        output = candidate;
        return true;
    }
    inline bool IsCurrent(const Snapshot& value, std::uint32_t userId,
        std::uint64_t now, std::uint64_t entered) noexcept
    {
        return userId != 0 && value.header.userId == userId
            && value.header.fileTime >= entered && value.header.fileTime <= now
            && now - value.header.fileTime <= 15000000ULL; // 1.5 seconds
    }
}
