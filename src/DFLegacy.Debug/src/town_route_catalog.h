#pragma once
#include <string>
#include <vector>

namespace dfl::town_route
{
    constexpr int PortalBlockMargin = 4;
    struct Key
    {
        int town = 0, area = 0;
        bool operator==(const Key& other) const noexcept { return town == other.town && area == other.area; }
        bool operator!=(const Key& other) const noexcept { return !(*this == other); }
    };
    struct Point { double x = 0, y = 0; };
    struct Waypoint
    {
        int x = 0, y = 0;
        bool operator==(const Waypoint& other) const noexcept { return x == other.x && y == other.y; }
    };
    struct Rect
    {
        int x = 0, y = 0, width = 0, height = 0;
        bool Contains(double px, double py) const noexcept;
    };
    struct Portal { Key source, target; Rect rect; Waypoint point; };
    struct Area
    {
        Key key;
        std::string mapPath, kind;
        std::vector<Rect> walkable;
        std::vector<Portal> portals;
    };
    // Publish this value as const after loading. Planning does no file IO or
    // waiting; input sizes and total geometry work are bounded.
    struct Catalog
    {
        std::vector<Area> areas;
        std::string pvfSha256;
        const Area* Find(Key key) const noexcept;
        // Success clears error; failure clears output and sets a stable code.
        bool Route(Key start, Key goal, std::vector<Portal>& output, std::string& error) const;
        // Doors are expanded PortalBlockMargin on all four sides. Internal
        // goals are4px inside, with3px stop-region coverage. Final goal stays.
        bool Waypoints(Key key, Point start, Waypoint goal, const std::vector<Portal>& blocked,
                       std::vector<Waypoint>& output, std::string& error) const;
    };
    // Worker-only, read-only bounded PVF load. Failure preserves output.
    // Only decoded areas/portals and SHA256 remain after archive bytes free.
    bool LoadCatalog(const wchar_t* path, Catalog& output, std::string& error) noexcept;
}
