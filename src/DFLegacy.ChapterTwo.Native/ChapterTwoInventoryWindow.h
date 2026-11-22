#pragma once
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>

namespace dfl::chapter_two::inventory_window
{
    enum class Operation : std::uint8_t { Hello, Read };
    enum class Section : std::uint8_t { Equipment, Consumable, Material, Quest, Avatar };
    struct Request
    {
        Operation operation;
        Section section;
        std::uint32_t first, id;
    };
    struct Cell
    {
        std::uint8_t offset;
        std::uint16_t itemId;
        std::uint32_t count, addInfo;
        std::uint16_t durability;
        std::uint8_t attributes, seal;
        std::uint16_t enchantment, appearance;
        std::array<std::uint8_t, 16> instance;
    };
    struct Page
    {
        Request request{};
        std::uint64_t revision{};
        std::uint8_t count{};
        std::array<Cell, 32> cells{};
    };

    // Windows x86 target. The game writes the sequence and transport padding.
    inline std::array<std::uint8_t, 18> EncodeArguments(Request request) noexcept
    {
        std::array<std::uint8_t, 18> bytes{255,255,255,'D','F','I','V',1,
            static_cast<std::uint8_t>(request.operation), static_cast<std::uint8_t>(request.section)};
        std::memcpy(bytes.data() + 10, &request.first, 4);
        std::memcpy(bytes.data() + 14, &request.id, 4);
        return bytes;
    }

    template<typename T>
    inline T ReadField(const std::uint8_t* field) noexcept
    {
        T value;
        std::memcpy(&value, field, sizeof(value));
        return value;
    }

    inline bool MatchesReply(const std::uint8_t* bytes, std::size_t size, Request expected) noexcept
    {
        constexpr std::uint8_t prefix[]{255,'D','F','I','V',1};
        return bytes && size >= 11 && expected.id && !std::memcmp(bytes, prefix, sizeof(prefix))
            && bytes[6] == static_cast<std::uint8_t>(expected.operation)
            && ReadField<std::uint32_t>(bytes + 7) == expected.id;
    }

    inline bool DecodePage(const std::uint8_t* bytes, std::size_t size, Request expected, Page& result) noexcept
    {
        if (expected.operation != Operation::Read || expected.section > Section::Avatar ||
            !MatchesReply(bytes, size, expected) || size < 29 ||
            bytes[11] != static_cast<std::uint8_t>(expected.section) ||
            ReadField<std::uint32_t>(bytes + 12) != expected.first) return false;

        const auto capacity = expected.section == Section::Avatar ? 999999u : 99999u;
        if (ReadField<std::uint32_t>(bytes + 16) != capacity || expected.first >= capacity) return false;
        const auto length = (std::min)(expected.section == Section::Avatar ? 28u : 32u, capacity - expected.first);
        const auto count = bytes[28];
        if (count > length || size != 29u + 35u * count) return false;

        Page page{};
        page.request = expected;
        page.revision = ReadField<std::uint64_t>(bytes + 20);
        if (!page.revision) return false;
        page.count = count;
        for (unsigned i = 0; i < count; ++i)
        {
            const auto* src = bytes + 29 + 35 * i;
            auto& cell = page.cells[i];
            cell.offset = src[0];
            cell.itemId = ReadField<std::uint16_t>(src + 1);
            if (!cell.itemId || cell.offset >= length || (i && cell.offset <= page.cells[i - 1].offset))
                return false;
            cell.count = ReadField<std::uint32_t>(src + 3);
            cell.addInfo = ReadField<std::uint32_t>(src + 7);
            cell.durability = ReadField<std::uint16_t>(src + 11);
            cell.attributes = src[13];
            cell.seal = src[14];
            cell.enchantment = ReadField<std::uint16_t>(src + 15);
            cell.appearance = ReadField<std::uint16_t>(src + 17);
            std::memcpy(cell.instance.data(), src + 19, cell.instance.size());
        }
        result = page;
        return true;
    }
}
