#pragma once
#include <array>

namespace dfl::chapter_two
{
    struct RelayPortMap
    {
        struct Entry { unsigned short service; unsigned short local; };
        std::array<Entry, 32> entries{};
        unsigned count{};

        static RelayPortMap Read(const char* name) noexcept
        {
            char text[512]{};
            const auto length = GetEnvironmentVariableA(name, text, sizeof(text));
            if (!length || length >= sizeof(text)) return {};
            const char* cursor = text;
            auto port = [&cursor]() noexcept {
                unsigned result = 0;
                while (*cursor >= '0' && *cursor <= '9') {
                    result = result * 10 + static_cast<unsigned>(*cursor++ - '0');
                    if (result > 65535) return 0u;
                }
                return result;
            };
            RelayPortMap map;
            while (*cursor) {
                const auto service = port();
                if (!service || *cursor++ != ':' || map.count == map.entries.size()) return {};
                const auto local = port();
                if (!local || (*cursor && *cursor != ',')) return {};
                for (unsigned i = 0; i < map.count; ++i)
                    if (map.entries[i].service == service || map.entries[i].local == local) return {};
                map.entries[map.count++] = {
                    static_cast<unsigned short>(service), static_cast<unsigned short>(local)};
                if (*cursor == ',' && !*++cursor) return {};
            }
            return map;
        }

        unsigned short ToLocal(unsigned short service) const noexcept
        {
            for (unsigned i = 0; i < count; ++i)
                if (entries[i].service == service) return entries[i].local;
            return count ? 0 : service;
        }

        unsigned short ToService(unsigned short local) const noexcept
        {
            for (unsigned i = 0; i < count; ++i)
                if (entries[i].local == local) return entries[i].service;
            return count ? 0 : local;
        }
    };
}
