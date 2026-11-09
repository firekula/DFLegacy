#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../src/town_route_catalog.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <limits>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace
{
    using namespace dfl::town_route;
    using Bytes = std::vector<unsigned char>;
    int Failures = 0, Checks = 0;
    void Check(bool okay, const char* label)
    { ++Checks; if (!okay) { ++Failures; std::fprintf(stderr, "FAIL town catalog: %s\n", label); } }
    void U32(Bytes& bytes, unsigned value)
    { for (int i = 0; i < 4; ++i) bytes.push_back(static_cast<unsigned char>(value >> (i * 8))); }
    Bytes Encrypt(Bytes bytes, unsigned key)
    {
        while (bytes.size() % 4) bytes.push_back(0);
        for (std::size_t i = 0; i < bytes.size(); i += 4)
        {
            unsigned word = 0;
            for (unsigned n = 0; n < 4; ++n) word |= static_cast<unsigned>(bytes[i + n]) << (n * 8);
            word = ((word << 6) | (word >> 26)) ^ key;
            for (unsigned n = 0; n < 4; ++n) bytes[i + n] = static_cast<unsigned char>(word >> (n * 8));
        }
        return bytes;
    }
    Bytes Archive(const std::vector<std::pair<std::string, std::string>>& files,
                  std::size_t treeAlignment = 4, std::size_t extraPadding = 0, unsigned char paddingByte = 0)
    {
        Bytes tree, body;
        for (std::size_t i = 0; i < files.size(); ++i)
        {
            U32(tree, static_cast<unsigned>(i)); U32(tree, static_cast<unsigned>(body.size()));
            U32(tree, static_cast<unsigned>(files[i].second.size())); U32(tree, 0x12340000U + static_cast<unsigned>(i));
            U32(tree, static_cast<unsigned>(files[i].first.size()));
            tree.insert(tree.end(), files[i].first.begin(), files[i].first.end());
            auto encrypted = Encrypt(Bytes(files[i].second.begin(), files[i].second.end()), 0x12340000U + static_cast<unsigned>(i));
            body.insert(body.end(), encrypted.begin(), encrypted.end());
        }
        tree.resize(tree.size() + (treeAlignment - tree.size() % treeAlignment) % treeAlignment + extraPadding, paddingByte);
        tree = Encrypt(std::move(tree), 0x87654321U);
        const std::string magic = "DNF_SCRIPT_PACK";
        Bytes out(magic.begin(), magic.end()); U32(out, static_cast<unsigned>(tree.size()));
        U32(out, 0x87654321U); U32(out, static_cast<unsigned>(files.size()));
        out.insert(out.end(), tree.begin(), tree.end()); out.insert(out.end(), body.begin(), body.end()); return out;
    }
    Bytes RawTree(Bytes tree, unsigned count, Bytes body = {})
    {
        tree = Encrypt(std::move(tree), 7); const std::string magic = "DNF_SCRIPT_PACK"; Bytes out(magic.begin(), magic.end());
        U32(out, static_cast<unsigned>(tree.size())); U32(out, 7); U32(out, count);
        out.insert(out.end(), tree.begin(), tree.end()); out.insert(out.end(), body.begin(), body.end()); return out;
    }
    bool LoadFixture(const Bytes& bytes, Catalog& catalog, std::string& error)
    {
        wchar_t path[MAX_PATH]{}; const DWORD n = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (!n || n >= MAX_PATH) return false;
        std::wstring file(path); file.resize(file.find_last_of(L"\\/") + 1);
        static unsigned sequence = 0;
        file += L"town-catalog-fixture-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(++sequence) + L".pvf";
        std::ofstream out(file, std::ios::binary); out.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size())); out.close();
        const bool okay = LoadCatalog(file.c_str(), catalog, error); DeleteFileW(file.c_str()); return okay;
    }
    std::string Map(const char* rects, const char* portals = "")
    { return std::string("[virtual movable area]\n") + rects + "\n[/virtual movable area]\n[town movable area]\n" + portals + "\n[/town movable area]\n"; }
    std::vector<std::pair<std::string, std::string>> Files(const std::string& map)
    { return {{"town/town.lst", "1 `test.twn` // comment\n"}, {"town/test.twn", "[area]\n0 `test/a.map` `[normal]`\n[/area]"}, {"map/test/a.map", map}}; }
    Catalog Geometry(std::vector<Rect> rectangles)
    { Catalog c; Area a; a.key = {1, 0}; a.kind = "normal"; a.walkable = std::move(rectangles); c.areas.push_back(std::move(a)); return c; }
    bool Plan(const Catalog& c, Point start, Waypoint goal, std::vector<Waypoint>& points, std::string& error, const std::vector<Portal>& blocked = {})
    { return c.Waypoints({1, 0}, start, goal, blocked, points, error); }
    bool IdleModel(const Catalog& c, Point& current, Waypoint goal, const std::vector<Portal>& blocked = {})
    {
        const Area* area = c.Find({1, 0}); if (!area) return false;
        auto rounded = [](double value) { return static_cast<int>(value + (value >= 0 ? .5 : -.5)); };
        for (int tick = 0; tick < 200000; ++tick)
        {
            if (rounded(current.x) == goal.x && rounded(current.y) == goal.y) return true;
            bool moved = false;
            for (int axis = 0; axis < 2; ++axis)
            {
                Point candidate = current; double& value = axis == 0 ? candidate.x : candidate.y;
                const int target = axis == 0 ? goal.x : goal.y;
                if (rounded(value) == target) continue;
                value += (axis == 0 ? .186 : .122) * (target > rounded(value) ? 1 : -1);
                bool walkable = false; for (const auto& r : area->walkable) walkable |= r.Contains(candidate.x, candidate.y);
                for (const auto& p : blocked) if (p.rect.Contains(candidate.x, candidate.y)) walkable = false;
                if (walkable) { current = candidate; moved = true; }
            }
            if (!moved) return false;
        }
        return false;
    }
    void Corpus(const Catalog& actual)
    {
        wchar_t path[MAX_PATH]{};
        const DWORD size = GetEnvironmentVariableW(L"DFL_TOWN_ROUTE_CORPUS", path, MAX_PATH);
        if (!size) return;
        Check(size < MAX_PATH, "Python corpus path is bounded"); if (size >= MAX_PATH) return;
        std::ifstream input(path); std::string line; unsigned cases = 0;
        while (std::getline(input, line))
        {
            std::istringstream row(line); char type = 0; row >> type; std::string error, expectedError; int success = 0, count = 0;
            bool okay = false; bool same = true;
            if (type == 'R')
            {
                Key a, b; row >> a.town >> a.area >> b.town >> b.area >> success >> expectedError >> count;
                std::vector<Portal> result; okay = actual.Route(a, b, result, error); same = result.size() == static_cast<std::size_t>(count);
                for (int i = 0; i < count; ++i)
                { Key target; Waypoint p; row >> target.town >> target.area >> p.x >> p.y; if (static_cast<std::size_t>(i) < result.size()) same &= result[i].target == target && result[i].point == p; }
            }
            else if (type == 'W')
            {
                Key key; Point from; Waypoint goal; int blockedCount = 0;
                row >> key.town >> key.area >> from.x >> from.y >> goal.x >> goal.y >> blockedCount;
                std::vector<Portal> blocked; const auto* area = actual.Find(key);
                for (int i = 0; i < blockedCount; ++i) { int index = 0; row >> index; if (area && index >= 0 && static_cast<std::size_t>(index) < area->portals.size()) blocked.push_back(area->portals[static_cast<std::size_t>(index)]); else same = false; }
                row >> success >> expectedError >> count; std::vector<Waypoint> result;
                okay = actual.Waypoints(key, from, goal, blocked, result, error); same &= result.size() == static_cast<std::size_t>(count);
                for (int i = 0; i < count; ++i) { Waypoint point; row >> point.x >> point.y; if (static_cast<std::size_t>(i) < result.size()) same &= point == result[static_cast<std::size_t>(i)]; }
            }
            else same = false;
            Check(same && okay == (success != 0) && error == (expectedError == "-" ? "" : expectedError) && !row.fail(), "Python real-PVF corpus exact result/code"); ++cases;
        }
        Check(input.eof() && cases > 0, "Python corpus was read"); std::printf("Town catalog Python corpus: %u cases\n", cases);
    }
}

int RunTownRouteCatalogTests()
{
    using namespace dfl::town_route;
    Failures = Checks = 0; Catalog c; std::string error; std::vector<Waypoint> points;
    auto files = Files(Map("0 0 100 100"));
    Check(LoadFixture(Archive(files), c, error) && error.empty() && c.areas.size() == 1 && c.pvfSha256.size() == 64, "encrypted PVF parses and hashes");
    for (unsigned suffix = 0; suffix < 8; ++suffix)
    {
        auto padded = files; padded.push_back({"unused/" + std::string(suffix + 1, 'x'), ""});
        Check(LoadFixture(Archive(padded, 8), c, error) && c.Find({1, 0}), "eight-byte directory alignment supports every zero-padding length");
        Check(!LoadFixture(Archive(padded, 8, 8), c, error) && error == "invalid_pvf", "excess directory padding still rejected");
    }
    Check(!LoadFixture(Archive(files, 8, 0, 1), c, error) && error == "invalid_pvf", "nonzero directory padding still rejected");
    files[0] = {"TOWN\\town.lst", "1 `\xB8\xB6\xC0\xBB.twn`\n"}; files[1].first = "town/\xB8\xB6\xC0\xBB.twn";
    Check(LoadFixture(Archive(files), c, error) && c.Find({1, 0}) != nullptr, "strict CP949 normalized directory paths");
    files[1].second = "[area]\n0 `test/\xB8\xB6\xC0\xBB.map` `[normal]`\n[/area]"; files[2].first = "map/test/\xB8\xB6\xC0\xBB.map";
    Check(LoadFixture(Archive(files), c, error) && c.Find({1, 0}) && c.Find({1, 0})->mapPath == u8"map/test/마을.map", "CP949 map references become UTF8 catalog paths");
    // CP949 includes case pairs outside ASCII: Greek, Cyrillic and fullwidth.
    // Directory normalization must agree with Python/C# invariant lowercase.
    for (const auto& pair : std::vector<std::pair<std::string, std::string>>{
            {"\xA5\xC1", "\xA5\xE1"}, {"\xAC\xA1", "\xAC\xD1"}, {"\xA3\xC1", "\xA3\xE1"}})
    {
        for (bool reverse : {false, true})
        {
            files = Files(Map("0 0 100 100"));
            files[0].second = "1 `" + (reverse ? pair.first : pair.second) + ".twn`\n";
            files[1].first = "town/" + (reverse ? pair.second : pair.first) + ".twn";
            Check(LoadFixture(Archive(files), c, error) && error.empty() && c.Find({1, 0}), "CP949 non-ASCII directory/reference case pairs agree");
        }
        files.push_back({"town/" + pair.first + ".twn", files[1].second});
        Check(!LoadFixture(Archive(files), c, error) && error == "invalid_pvf", "Unicode-normalized duplicate directory rejected");
    }
    for (const Bytes& bytes : std::vector<Bytes>{Bytes{}, Bytes(26, 0), Bytes(27, 0)})
        Check(!LoadFixture(bytes, c, error) && error == "invalid_pvf" && c.areas.size() == 1, "invalid archive preserves old catalog");
    auto bytes = Archive(Files(Map("0 0 100 100"))); bytes[15] = 3;
    Check(!LoadFixture(bytes, c, error) && error == "invalid_pvf", "unaligned tree rejected");
    for (unsigned field : {15U, 23U})
    { bytes = Archive(Files(Map("0 0 100 100"))); for (unsigned n = 0; n < 4; ++n) bytes[field + n] = 0xFF; Check(!LoadFixture(bytes, c, error) && error == "invalid_pvf", "oversized tree/count rejected before allocation"); }
    for (const auto& name : std::vector<std::string>{"a", std::string(1, static_cast<char>(0xFF)), std::string("a\0b", 3), "../town/town.lst"})
    {
        Bytes tree; U32(tree, 1); U32(tree, 0); U32(tree, 4); U32(tree, 1); U32(tree, static_cast<unsigned>(name.size())); tree.insert(tree.end(), name.begin(), name.end());
        const auto malformed = RawTree(tree, 1, {}); Check(!LoadFixture(malformed, c, error) && error == "invalid_pvf", "truncated body rejected for every path");
        if (name != "a") Check(!LoadFixture(RawTree(tree, 1, Bytes(4, 0)), c, error) && error == "invalid_pvf", "invalid CP949/NUL/traversal directory rejected");
    }
    for (unsigned size : {0xFFFFFFFFU, 8U * 1024U * 1024U + 1})
    { Bytes tree; U32(tree, 1); U32(tree, 0); U32(tree, size); U32(tree, 1); U32(tree, 1); tree.push_back('a'); Check(!LoadFixture(RawTree(tree, 1), c, error) && error == "invalid_pvf", "signed/oversized entry rejected"); }
    for (unsigned offset : {1U, 0xFFFFFFFCU})
    { Bytes tree; U32(tree, 1); U32(tree, offset); U32(tree, 4); U32(tree, 1); U32(tree, 1); tree.push_back('a'); Check(!LoadFixture(RawTree(tree, 1, Bytes(4, 0)), c, error) && error == "invalid_pvf", "unaligned/out-of-range body offset rejected without overflow"); }
    for (unsigned length : {0U, 4097U, 0xFFFFFFFFU})
    { Bytes tree; U32(tree, 1); U32(tree, 0); U32(tree, 4); U32(tree, 1); U32(tree, length); tree.push_back('a'); Check(!LoadFixture(RawTree(tree, 1, Bytes(4, 0)), c, error) && error == "invalid_pvf", "empty/oversized path length rejected"); }
    {
        Bytes tree;
        for (char name : {'a', 'b'}) { U32(tree, 7); U32(tree, 0); U32(tree, 4); U32(tree, 1); U32(tree, 1); tree.push_back(static_cast<unsigned char>(name)); }
        Check(!LoadFixture(RawTree(tree, 2, Bytes(4, 0)), c, error) && error == "invalid_pvf", "duplicate file identifier rejected");
    }
    files = Files(Map("0 0 100 100")); files.push_back({"TOWN\\TOWN.LST", ""});
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_pvf", "duplicate normalized directory rejected");
    files = Files(Map("0 0 -1 100"));
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "negative rectangle rejected");
    files = Files(Map("0 0 100 100", "1 2 3"));
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "exact six-column portal rows required");
    for (const auto& invalidList : {std::string("-0 `test.twn`"), std::string("1`test.twn`"), std::string(5000, '9') + " `test.twn`"})
    { files = Files(Map("0 0 100 100")); files[0].second = invalidList; Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "town ID syntax and numeric length bounded"); }
    files = Files(Map("0 0 100 100")); files[1].second = "[area]\n-0 `test/a.map` `[normal]`\n[/area]";
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "negative-zero area ID rejected");
    files = Files(Map("0 0 100 100")); files[1].second += "\n[/area]";
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "unmatched area closer rejected");
    files = Files(Map("0 0 100 100")); files[1].second += files[1].second;
    Check(!LoadFixture(Archive(files), c, error) && error == "invalid_catalog", "duplicate area rejected");
    c = Geometry({{0, 0, 100, 100}}); c.areas[0].portals.push_back({{1, 0}, {1, 1}, {80, 20, 20, 20}, {90, 30}});
    Area a; a.key = {1, 1}; a.kind = "dungeon_gate"; a.walkable = {{0, 0, 100, 100}};
    a.portals.push_back({{1, 1}, {1, 2}, {80, 20, 20, 20}, {90, 30}}); c.areas.push_back(a); a.key = {1, 2}; a.kind = "normal"; a.portals.clear(); c.areas.push_back(a);
    std::vector<Portal> route;
    Check(c.Route({1, 0}, {1, 1}, route, error) && route.size() == 1, "dungeon gate can be route endpoint");
    Check(!c.Route({1, 0}, {1, 2}, route, error) && error == "no_route" && route.empty(), "dungeon gate cannot be intermediary");
    Check(!c.Route({1, 0}, {99, 99}, route, error) && error == "unknown_area", "unknown target excluded");
    Check(!c.Route({std::numeric_limits<int>::max(), 0}, {1, 0}, route, error) && error == "unknown_area", "unbounded external key rejected");
    files = Files(Map("0 0 100 100", "80 20 30 30 1 0"));
    const bool portalOkay = LoadFixture(Archive(files), c, error);
    Check(portalOkay && c.Find({1, 0}) && c.Find({1, 0})->portals.size() == 1 && c.Find({1, 0})->portals[0].point == Waypoint{90, 35}, "portal point is interior of walkable intersection");
    c = Geometry({{0, 0, 20, 20}});
    Check(c.areas[0].walkable[0].Contains(0, 0) && !c.areas[0].walkable[0].Contains(20, 10), "native right/bottom half-open bounds");
    Check(!Plan(c, {20, 2}, {20, 18}, points, error) && error == "outside_walkable", "illegal degenerate boundary segment rejected");
    c = Geometry({{0, 0, 20, 100}, {0, 80, 100, 20}, {80, 0, 20, 100}});
    Check(Plan(c, {10.5, 10.5}, {90, 10}, points, error) && points.size() >= 3 && points.front().y > 70 && points.back() == Waypoint{90, 10}, "U-shaped detour initially moves away");
    c = Geometry({{0, 0, 50, 50}, {49, 0, 51, 50}});
    Check(Plan(c, {1.5, 20.5}, {99, 20}, points, error) && points == std::vector<Waypoint>{{99, 20}}, "covered straight union simplifies");
    c = Geometry({{0, 0, 20, 20}, {20, 20, 20, 20}});
    Check(!Plan(c, {10, 10}, {30, 30}, points, error) && error == "no_path", "corner-only connection rejected");
    c = Geometry({{0, 0, 100, 100}, {90, 20, 110, 20}, {180, 20, 220, 80}});
    Check(Plan(c, {10, 10}, {310, 60}, points, error) && points == std::vector<Waypoint>{{95, 30}, {190, 30}, {310, 60}}, "186:122 native steering requires safe turns");
    c = Geometry({{9, 196, 1548, 142}, {729, 157, 109, 40}, {1048, 322, 89, 33}});
    for (Point start : {Point{785, 174}, Point{782.58197, 195.941757}})
    {
        Check(Plan(c, start, {16, 264}, points, error) && points == std::vector<Waypoint>{{783, 200}, {16, 264}}, "thin overlap target clears rounded-idle old side");
        if (points.size() == 2) { Check(IdleModel(c, start, points[0]), "fractional native model reaches bridge"); Check(Plan(c, start, {16, 264}, points, error) && points == std::vector<Waypoint>{{16, 264}}, "fresh float stop does not repeat old bridge"); }
    }
    Check(Plan(c, {16, 264}, {785, 174}, points, error) && points.size() >= 2, "thin overlap reverse path supported");
    c = Geometry({{0, 0, 100, 100}}); Portal blocked{{1, 0}, {9, 9}, {40, 40, 20, 20}, {50, 50}};
    Check(Plan(c, {10, 50}, {90, 50}, points, error, {blocked}) && points.size() > 1, "foreign door blocks whole leg and forces detour");
    blocked.rect = {40, 80, 20, 5};
    Check(Plan(c, {50, 90}, {25, 90}, points, error, {blocked}) && points == std::vector<Waypoint>{{25, 90}}, "birth escape safely leaves foreign door behind");
    Check(!Plan(c, {50, 90}, {50, 76}, points, error, {blocked}) && error == "outside_walkable", "four-pixel expanded portal top remains blocked");
    Check(!Plan(c, {std::numeric_limits<double>::quiet_NaN(), 1}, {2, 2}, points, error) && error == "outside_walkable", "nonfinite endpoint rejected");
    blocked.source = {2, 0}; Check(!Plan(c, {1, 1}, {2, 2}, points, error, {blocked}) && error == "invalid_catalog", "foreign-area blocked portal rejected");
    c = Geometry({{std::numeric_limits<int>::max(), 0, std::numeric_limits<int>::max(), 10}});
    Check(!Plan(c, {1, 1}, {2, 2}, points, error) && error == "invalid_catalog", "public malformed rectangle cannot overflow geometry");
    c = Geometry(std::vector<Rect>(257, Rect{0, 0, 100, 100}));
    Check(!Plan(c, {1, 1}, {2, 2}, points, error) && error == "invalid_catalog", "public rectangle count bounded");
    c = Geometry({{0, 0, 100, 100}}); blocked.source = {1, 0};
    Check(!Plan(c, {1, 1}, {2, 2}, points, error, std::vector<Portal>(257, blocked)) && error == "invalid_catalog", "blocked portal count bounded");
    {
        std::vector<Rect> many; std::vector<Portal> cuts;
        for (int i = 0; i < 256; ++i) many.push_back({i * 100, 0, 60, 1000});
        for (int i = 0; i < 17; ++i) cuts.push_back({{1, 0}, {-1, -1}, {0, 20 + 20 * i, 25600, 1}, {0, 0}});
        c = Geometry(std::move(many));
        Check(!Plan(c, {1, 1}, {50, 999}, points, error, cuts) && error == "no_path" && points.empty(), "fragmentation work is bounded and fails without a partial route");
    }
    Catalog actual; const wchar_t* actualPath = L"G:\\Game\\DNF_ACT1\\DF\\Script.pvf";
    if (GetFileAttributesW(actualPath) != INVALID_FILE_ATTRIBUTES)
    {
        const bool loaded = LoadCatalog(actualPath, actual, error); std::size_t portals = 0; for (const auto& area : actual.areas) portals += area.portals.size();
        Check(loaded && actual.areas.size() == 29 && portals == 68 && actual.pvfSha256 == "288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f", "actual PVF29areas68portals and protected SHA");
        const std::vector<Key> expected{{1, 0}, {2, 1}, {2, 0}, {2, 2}, {2, 8}, {5, 0}, {5, 1}};
        const bool routed = actual.Route({1, 1}, {5, 1}, route, error); bool same = routed && route.size() == expected.size();
        for (std::size_t i = 0; i < route.size() && i < expected.size(); ++i) same &= route[i].target == expected[i];
        Check(same, "actual route matches Python target sequence");
        if (loaded) { const auto* area = actual.Find({1, 0}); std::vector<Portal> other; if (area) for (const auto& p : area->portals) if (p.target != Key{2, 1}) other.push_back(p);
            Check(actual.Waypoints({1, 0}, {782.58197, 195.941757}, {16, 264}, other, points, error) && points == std::vector<Waypoint>{{783, 200}, {16, 264}}, "actual foreign-door-aware float route matches Python"); Corpus(actual); }
    }
    else std::printf("Town catalog actual PVF: skipped (not installed)\n");
    std::printf("Town route catalog: %d checks, %d failures\n", Checks, Failures); return Failures;
}
