#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include "town_route_catalog.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <deque>
#include <sstream>
#include <unordered_map>
#include <unordered_set>
#include <utility>

namespace dfl::town_route
{
    namespace
    {
        constexpr std::size_t MaxArchive = 256U * 1024U * 1024U, MaxTree = 16U * 1024U * 1024U;
        constexpr std::size_t MaxEntry = 8U * 1024U * 1024U, MaxFiles = 200000, MaxAreas = 4096;
        constexpr std::size_t MaxRects = 256, MaxPortals = 256, MaxPlanRects = 4096;
        constexpr int CoordinateLimit = 1000000, Inset = 4, StopMargin = 3;
        struct Failure { const char* code; };
        [[noreturn]] void Bad(const char* code) { throw Failure{code}; }
        bool Fail(std::string& error, const char* code) noexcept
        { try { error = code; } catch (...) { error.clear(); } return false; }
        struct Budget
        {
            // Rectangle membership visits and graph edges share a hard cap.
            // Malformed/dense geometry fails safely instead of stalling a
            // game-thread call through an adversarial bounded-size catalog.
            std::size_t remaining = 2000000;
            void Spend(std::size_t count = 1) { if (count > remaining) Bad("no_path"); remaining -= count; }
        };
        std::int64_t Right(const Rect& r) noexcept { return static_cast<std::int64_t>(r.x) + r.width; }
        std::int64_t Bottom(const Rect& r) noexcept { return static_cast<std::int64_t>(r.y) + r.height; }
        Point AsPoint(Waypoint p) noexcept { return {static_cast<double>(p.x), static_cast<double>(p.y)}; }
        bool ValidKey(Key key) noexcept { return key.town >= 0 && key.town <= 255 && key.area >= 0 && key.area <= 255; }
        int KeyNumber(Key key) noexcept { return key.town * 256 + key.area; }
        bool ValidRect(const Rect& r) noexcept
        {
            return r.x >= -CoordinateLimit && r.x <= CoordinateLimit && r.y >= -CoordinateLimit && r.y <= CoordinateLimit
                && r.width > 0 && r.width <= CoordinateLimit && r.height > 0 && r.height <= CoordinateLimit
                && Right(r) >= -CoordinateLimit && Right(r) <= CoordinateLimit && Bottom(r) >= -CoordinateLimit && Bottom(r) <= CoordinateLimit;
        }
        bool Intersect(const Rect& a, const Rect& b, Rect& result) noexcept
        {
            const auto x = (std::max)(static_cast<std::int64_t>(a.x), static_cast<std::int64_t>(b.x));
            const auto y = (std::max)(static_cast<std::int64_t>(a.y), static_cast<std::int64_t>(b.y));
            const auto right = (std::min)(Right(a), Right(b)), bottom = (std::min)(Bottom(a), Bottom(b));
            if (right < x || bottom < y || (right == x && bottom == y)) return false;
            result = {static_cast<int>(x), static_cast<int>(y), static_cast<int>(right - x), static_cast<int>(bottom - y)}; return true;
        }
        Waypoint Middle(const Rect& r) noexcept { return {r.x + r.width / 2, r.y + r.height / 2}; }
        bool BoxCovered(const std::vector<Rect>& rectangles, Point from, Point to, Budget& budget)
        {
            const double left = (std::min)(from.x, to.x), right = (std::max)(from.x, to.x);
            const double low = (std::min)(from.y, to.y), high = (std::max)(from.y, to.y);
            std::vector<double> columns{left, right}; columns.reserve(rectangles.size() * 4 + 4);
            for (const auto& r : rectangles)
            { budget.Spend(); for (double x : {static_cast<double>(r.x), static_cast<double>(Right(r))}) if (left <= x && x <= right) columns.push_back(x); }
            std::sort(columns.begin(), columns.end()); columns.erase(std::unique(columns.begin(), columns.end()), columns.end());
            const auto count = columns.size(); for (std::size_t i = 1; i < count; ++i) columns.push_back((columns[i - 1] + columns[i]) / 2);
            std::vector<std::pair<double, double>> intervals; intervals.reserve(rectangles.size());
            for (double x : columns)
            {
                intervals.clear();
                for (const auto& r : rectangles) { budget.Spend(); if (r.x <= x && x < Right(r)) intervals.emplace_back(r.y, static_cast<double>(Bottom(r))); }
                std::sort(intervals.begin(), intervals.end()); double covered = low; bool okay = false;
                for (const auto& interval : intervals)
                {
                    if (interval.second <= covered) continue;
                    if (interval.first > covered) return false;
                    covered = interval.second;
                    if (covered > high) { okay = true; break; }
                }
                if (!okay) return false;
            }
            return true;
        }
        bool LegCovered(const std::vector<Rect>& rectangles, Point from, Point to, int fromMargin, int toMargin, Budget& budget)
        {
            return BoxCovered(rectangles,
                {(std::min)(from.x - fromMargin, to.x - toMargin), (std::min)(from.y - fromMargin, to.y - toMargin)},
                {(std::max)(from.x + fromMargin, to.x + toMargin), (std::max)(from.y + fromMargin, to.y + toMargin)}, budget);
        }
        bool Bridge(const Rect& a, const Rect& b, std::vector<Waypoint>& bridge, Budget& budget)
        {
            Rect shared; if (!Intersect(a, b, shared)) return false;
            const int ax = a.x + Inset, ay = a.y + Inset, ar = static_cast<int>(Right(a)) - Inset, ab = static_cast<int>(Bottom(a)) - Inset;
            const int bx = b.x + Inset, by = b.y + Inset, br = static_cast<int>(Right(b)) - Inset, bb = static_cast<int>(Bottom(b)) - Inset;
            if (ax > ar || ay > ab || bx > br || by > bb) return false;
            const auto mid = Middle(shared); std::vector<std::pair<Waypoint, Waypoint>> candidates;
            if ((std::max)(ax, bx) <= (std::min)(ar, br))
            { const int x = std::clamp(mid.x, (std::max)(ax, bx), (std::min)(ar, br)); candidates.push_back({{x, std::clamp(mid.y, ay, ab)}, {x, std::clamp(mid.y, by, bb)}}); }
            if ((std::max)(ay, by) <= (std::min)(ab, bb))
            { const int y = std::clamp(mid.y, (std::max)(ay, by), (std::min)(ab, bb)); candidates.push_back({{std::clamp(mid.x, ax, ar), y}, {std::clamp(mid.x, bx, br), y}}); }
            for (const auto& pair : candidates)
            {
                if (LegCovered({a, b}, AsPoint(pair.first), AsPoint(pair.second), StopMargin, StopMargin, budget))
                { bridge = {pair.first}; if (!(pair.first == pair.second)) bridge.push_back(pair.second); return true; }
            }
            return false;
        }
        struct RectHash
        {
            std::size_t operator()(const Rect& r) const noexcept
            { std::size_t h = 0; for (int v : {r.x, r.y, r.width, r.height}) h = h * 16777619U ^ static_cast<unsigned>(v); return h; }
        };
        struct RectEqual
        { bool operator()(const Rect& a, const Rect& b) const noexcept { return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height; } };
        std::vector<Rect> WithoutPortals(const Area& area, const std::vector<Portal>& blocked, Budget& budget)
        {
            if (area.walkable.empty() || area.walkable.size() > MaxRects || blocked.size() > MaxPortals) Bad("invalid_catalog");
            for (const auto& r : area.walkable) if (!ValidRect(r)) Bad("invalid_catalog");
            auto rectangles = area.walkable;
            for (const auto& p : blocked)
            {
                if (p.source != area.key || !ValidRect(p.rect)) Bad("invalid_catalog");
                const Rect obstacle{p.rect.x - PortalBlockMargin, p.rect.y - PortalBlockMargin,
                    p.rect.width + 2 * PortalBlockMargin, p.rect.height + 2 * PortalBlockMargin};
                std::vector<Rect> remaining; std::unordered_set<Rect, RectHash, RectEqual> seen;
                auto add = [&](const Rect& r) { if (r.width > 0 && r.height > 0 && seen.insert(r).second) { if (remaining.size() == MaxPlanRects) Bad("no_path"); remaining.push_back(r); } };
                for (const auto& ground : rectangles)
                {
                    budget.Spend(); Rect shared;
                    if (!Intersect(ground, obstacle, shared) || shared.width == 0 || shared.height == 0) { add(ground); continue; }
                    add({ground.x, ground.y, ground.width, shared.y - ground.y});
                    add({ground.x, static_cast<int>(Bottom(shared)), ground.width, static_cast<int>(Bottom(ground) - Bottom(shared))});
                    add({ground.x, shared.y, shared.x - ground.x, shared.height});
                    add({static_cast<int>(Right(shared)), shared.y, static_cast<int>(Right(ground) - Right(shared)), shared.height});
                }
                rectangles = std::move(remaining);
            }
            return rectangles;
        }
        std::unordered_map<int, std::size_t> IndexAreas(const Catalog& c)
        {
            if (c.areas.empty() || c.areas.size() > MaxAreas) Bad("invalid_catalog");
            std::unordered_map<int, std::size_t> index; index.reserve(c.areas.size());
            for (std::size_t i = 0; i < c.areas.size(); ++i)
                if (!ValidKey(c.areas[i].key) || c.areas[i].portals.size() > MaxPortals || !index.emplace(KeyNumber(c.areas[i].key), i).second) Bad("invalid_catalog");
            return index;
        }
        using Bytes = std::vector<unsigned char>;
        unsigned Read32(const Bytes& bytes, std::size_t offset)
        { if (offset > bytes.size() || bytes.size() - offset < 4) Bad("invalid_pvf"); unsigned value = 0; for (unsigned n = 0; n < 4; ++n) value |= static_cast<unsigned>(bytes[offset + n]) << (n * 8); return value; }
        Bytes Decode(const unsigned char* bytes, std::size_t size, unsigned key)
        {
            if (size % 4) Bad("invalid_pvf"); Bytes out(size);
            for (std::size_t i = 0; i < size; i += 4)
            { unsigned word = 0; for (unsigned n = 0; n < 4; ++n) word |= static_cast<unsigned>(bytes[i + n]) << (n * 8); word ^= key; word = (word >> 6) | (word << 26); for (unsigned n = 0; n < 4; ++n) out[i + n] = static_cast<unsigned char>(word >> (n * 8)); }
            return out;
        }
        std::string Lower(std::string text)
        { for (char& c : text) if (c >= 'A' && c <= 'Z') c = static_cast<char>(c + ('a' - 'A')); return text; }
        std::string LowerPath(std::string text, const char* code)
        {
            // ASCII tags keep the cheap helper above; CP949 paths also carry
            // Greek/Cyrillic/fullwidth case pairs. Match the original reader's
            // invariant Unicode lowercase before comparing UTF8 path keys.
            if (std::all_of(text.begin(), text.end(), [](unsigned char c) { return c < 128; })) return Lower(std::move(text));
            if (text.size() > MaxEntry) Bad(code);
            const int size = static_cast<int>(text.size());
            const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), size, nullptr, 0);
            if (count <= 0) Bad(code); std::wstring wide(static_cast<std::size_t>(count), L'\0');
            if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), size, wide.data(), count) != count) Bad(code);
            const int mappedSize = LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_LOWERCASE, wide.data(), count, nullptr, 0, nullptr, nullptr, 0);
            if (mappedSize <= 0) Bad(code); std::wstring mapped(static_cast<std::size_t>(mappedSize), L'\0');
            if (LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_LOWERCASE, wide.data(), count, mapped.data(), mappedSize, nullptr, nullptr, 0) != mappedSize) Bad(code);
            const int utf8Size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, mapped.data(), mappedSize, nullptr, 0, nullptr, nullptr);
            if (utf8Size <= 0) Bad(code); std::string result(static_cast<std::size_t>(utf8Size), '\0');
            if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, mapped.data(), mappedSize, result.data(), utf8Size, nullptr, nullptr) != utf8Size) Bad(code);
            return result;
        }
        std::string Normalize(std::string path, const char* code)
        {
            for (char& c : path) if (c == '\\') c = '/'; path = LowerPath(std::move(path), code);
            if (path.empty() || path.front() == '/' || path.find('\0') != std::string::npos || path.find(':') != std::string::npos) Bad(code);
            std::size_t cursor = 0;
            for (;;)
            { const auto end = path.find('/', cursor); const auto part = path.substr(cursor, end == std::string::npos ? end : end - cursor); if (part.empty() || part == "." || part == "..") Bad(code); if (end == std::string::npos) break; cursor = end + 1; }
            return path;
        }
        std::string Cp949(const unsigned char* bytes, std::size_t size, bool strict)
        {
            if (!size) return {};
            const int length = static_cast<int>(size); const DWORD flags = strict ? MB_ERR_INVALID_CHARS : 0;
            const int count = MultiByteToWideChar(949, flags, reinterpret_cast<const char*>(bytes), length, nullptr, 0);
            if (count <= 0) Bad("invalid_pvf"); std::wstring wide(static_cast<std::size_t>(count), L'\0');
            if (MultiByteToWideChar(949, flags, reinterpret_cast<const char*>(bytes), length, wide.data(), count) != count) Bad("invalid_pvf");
            const int outSize = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, wide.data(), count, nullptr, 0, nullptr, nullptr);
            if (outSize <= 0) Bad("invalid_pvf"); std::string out(static_cast<std::size_t>(outSize), '\0');
            if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, wide.data(), count, out.data(), outSize, nullptr, nullptr) != outSize) Bad("invalid_pvf"); return out;
        }
        struct FileHandle { HANDLE value = INVALID_HANDLE_VALUE; ~FileHandle() { if (value != INVALID_HANDLE_VALUE) CloseHandle(value); } };
        Bytes ReadArchive(const wchar_t* path)
        {
            if (!path || !*path) Bad("invalid_pvf"); FileHandle file;
            file.value = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            LARGE_INTEGER size{}; if (file.value == INVALID_HANDLE_VALUE || !GetFileSizeEx(file.value, &size) || size.QuadPart < 27 || size.QuadPart > MaxArchive) Bad("invalid_pvf");
            Bytes bytes(static_cast<std::size_t>(size.QuadPart)); std::size_t cursor = 0;
            while (cursor < bytes.size())
            { DWORD read = 0; const DWORD count = static_cast<DWORD>((std::min)(bytes.size() - cursor, static_cast<std::size_t>(1024 * 1024))); if (!ReadFile(file.value, bytes.data() + cursor, count, &read, nullptr) || read != count) Bad("invalid_pvf"); cursor += read; }
            return bytes;
        }
        std::string Sha256(const Bytes& bytes)
        {
            struct Handles
            // storage is a member so the hash is destroyed before its caller-
            // supplied BCrypt object buffer is released, including failures.
            { Bytes storage; BCRYPT_ALG_HANDLE algorithm = nullptr; BCRYPT_HASH_HANDLE hash = nullptr; ~Handles() { if (hash) BCryptDestroyHash(hash); if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0); } } handles;
            if (BCryptOpenAlgorithmProvider(&handles.algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) Bad("invalid_pvf");
            DWORD length = 0, copied = 0;
            if (BCryptGetProperty(handles.algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&length), sizeof(length), &copied, 0) < 0 || copied != sizeof(length) || length > 1024 * 1024) Bad("invalid_pvf");
            handles.storage.resize(length); unsigned char digest[32]{};
            if (BCryptCreateHash(handles.algorithm, &handles.hash, handles.storage.data(), length, nullptr, 0, 0) < 0
                || BCryptHashData(handles.hash, const_cast<PUCHAR>(bytes.data()), static_cast<ULONG>(bytes.size()), 0) < 0
                || BCryptFinishHash(handles.hash, digest, sizeof(digest), 0) < 0) Bad("invalid_pvf");
            const char hex[] = "0123456789abcdef"; std::string out; out.reserve(64); for (unsigned char c : digest) { out.push_back(hex[c >> 4]); out.push_back(hex[c & 15]); } return out;
        }
        struct Entry { unsigned offset = 0, size = 0, key = 0; };
        struct Pvf
        {
            Bytes raw;
            std::size_t body = 0;
            std::unordered_map<std::string, Entry> entries;
            explicit Pvf(const wchar_t* path) : raw(ReadArchive(path))
            {
                const std::string magic = "DNF_SCRIPT_PACK";
                if (!std::equal(magic.begin(), magic.end(), raw.begin())) Bad("invalid_pvf");
                const unsigned length = Read32(raw, 15), key = Read32(raw, 19), count = Read32(raw, 23);
                if (!length || length % 4 || length > MaxTree || length > raw.size() - 27 || count > MaxFiles || count > length / 21) Bad("invalid_pvf");
                body = 27 + length; auto tree = Decode(raw.data() + 27, length, key); std::size_t cursor = 0;
                entries.reserve(count); std::unordered_set<unsigned> identifiers; identifiers.reserve(count);
                for (unsigned i = 0; i < count; ++i)
                {
                    if (cursor > tree.size() || tree.size() - cursor < 20) Bad("invalid_pvf");
                    const auto identifier = Read32(tree, cursor), offset = Read32(tree, cursor + 4), size = Read32(tree, cursor + 8);
                    const auto crc = Read32(tree, cursor + 12), pathSize = Read32(tree, cursor + 16); cursor += 20;
                    const std::uint64_t padded = (static_cast<std::uint64_t>(size) + 3) & ~std::uint64_t{3};
                    if (size > MaxEntry || !pathSize || pathSize > 4096 || pathSize > tree.size() - cursor || offset % 4
                        || static_cast<std::uint64_t>(body) + offset + padded > raw.size()) Bad("invalid_pvf");
                    auto name = Cp949(tree.data() + cursor, pathSize, true); cursor += pathSize;
                    while (!name.empty() && name.back() == '\0') name.pop_back(); name = Normalize(std::move(name), "invalid_pvf");
                    if (!identifiers.insert(identifier).second || !entries.emplace(std::move(name), Entry{offset, size, crc}).second) Bad("invalid_pvf");
                }
                if (tree.size() - cursor > 3 || std::any_of(tree.begin() + static_cast<std::ptrdiff_t>(cursor), tree.end(), [](unsigned char b) { return b != 0; })) Bad("invalid_pvf");
            }
            std::string Text(const std::string& path) const
            {
                const auto it = entries.find(Normalize(path, "invalid_catalog")); if (it == entries.end()) Bad("invalid_catalog");
                const auto& e = it->second; auto bytes = Decode(raw.data() + body + e.offset, (static_cast<std::size_t>(e.size) + 3) & ~std::size_t{3}, e.key);
                auto text = Cp949(bytes.data(), e.size, false); std::string out; out.reserve(text.size());
                for (std::size_t i = 0; i < text.size();)
                { if (i + 1 < text.size() && text[i] == '/' && text[i + 1] == '/') { while (i < text.size() && text[i] != '\r' && text[i] != '\n') ++i; } else out.push_back(text[i++]); }
                return out;
            }
        };
        bool White(char c) noexcept { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
        void Skip(const std::string& text, std::size_t& pos) noexcept { while (pos < text.size() && White(text[pos])) ++pos; }
        int Number(const std::string& text, std::size_t& pos, std::size_t digits = 7)
        {
            Skip(text, pos); bool negative = false; if (pos < text.size() && text[pos] == '-') { if (digits == 3) Bad("invalid_catalog"); negative = true; ++pos; }
            int value = 0; std::size_t count = 0;
            while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9')
            { if (++count > digits) Bad("invalid_catalog"); value = value * 10 + (text[pos++] - '0'); }
            if (!count || (digits == 3 && pos < text.size() && !White(text[pos]))) Bad("invalid_catalog"); if (negative) value = -value;
            if (value < -CoordinateLimit || value > CoordinateLimit) Bad("invalid_catalog"); return value;
        }
        std::string Quoted(const std::string& text, std::size_t& pos)
        {
            Skip(text, pos); if (pos == text.size() || text[pos++] != '`') Bad("invalid_catalog");
            const auto end = text.find('`', pos); if (end == std::string::npos || end == pos || end - pos > 12288) Bad("invalid_catalog");
            auto result = text.substr(pos, end - pos); pos = end + 1; return result;
        }
        std::vector<std::string> Blocks(const std::string& text, const std::string& tag)
        {
            const auto lower = Lower(text); const auto open = "[" + tag + "]", close = "[/" + tag + "]";
            std::size_t pos = 0; std::vector<std::string> blocks;
            for (;;)
            {
                const auto start = lower.find(open, pos), stray = lower.find(close, pos);
                if (start == std::string::npos) { if (stray != std::string::npos) Bad("invalid_catalog"); break; }
                if (stray != std::string::npos && stray < start) Bad("invalid_catalog");
                const auto end = lower.find(close, start + open.size()), nested = lower.find(open, start + open.size());
                if (end == std::string::npos || (nested != std::string::npos && nested < end) || blocks.size() == MaxAreas) Bad("invalid_catalog");
                blocks.push_back(text.substr(start + open.size(), end - start - open.size())); pos = end + close.size();
            }
            return blocks;
        }
        std::string Block(const std::string& text, const std::string& tag, bool required)
        { auto blocks = Blocks(text, tag); if (blocks.size() > 1 || (required && blocks.size() != 1)) Bad("invalid_catalog"); return blocks.empty() ? "" : std::move(blocks[0]); }
        std::vector<std::vector<int>> Rows(const std::string& text, std::size_t width, std::size_t maximum)
        {
            std::istringstream input(text); std::string line; std::vector<std::vector<int>> rows;
            while (std::getline(input, line))
            {
                std::size_t pos = 0; Skip(line, pos); if (pos == line.size()) continue;
                std::vector<int> row; row.reserve(width);
                for (std::size_t i = 0; i < width; ++i)
                { row.push_back(Number(line, pos)); if (pos < line.size() && !White(line[pos])) Bad("invalid_catalog"); }
                Skip(line, pos); if (pos != line.size() || rows.size() == maximum) Bad("invalid_catalog"); rows.push_back(std::move(row));
            }
            return rows;
        }
        Rect ReadRect(const std::vector<int>& row)
        { Rect r{row[0], row[1], row[2], row[3]}; if (!ValidRect(r)) Bad("invalid_catalog"); return r; }
        Waypoint PortalPoint(const Rect& portal, const std::vector<Rect>& ground)
        {
            bool found = false; Rect best{};
            for (const auto& r : ground)
            {
                Rect shared; if (!Intersect(portal, r, shared) || shared.width < 2 || shared.height < 2) continue;
                const int score = (std::min)(shared.width, shared.height), bestScore = (std::min)(best.width, best.height);
                if (!found || score > bestScore || (score == bestScore && static_cast<std::int64_t>(shared.width) * shared.height > static_cast<std::int64_t>(best.width) * best.height)) { found = true; best = shared; }
            }
            if (!found) Bad("invalid_catalog"); return Middle(best);
        }
        Catalog ParseCatalog(const Pvf& pvf)
        {
            Catalog result; std::istringstream list(pvf.Text("town/town.lst")); std::string line; std::unordered_set<int> towns, keys;
            while (std::getline(list, line))
            {
                std::size_t pos = 0; Skip(line, pos); if (pos == line.size()) continue;
                const int town = Number(line, pos, 3); const auto path = Quoted(line, pos); Skip(line, pos);
                if (town < 0 || town > 255 || pos != line.size() || !towns.insert(town).second) Bad("invalid_catalog");
                const auto blocks = Blocks(pvf.Text("town/" + path), "area"); if (blocks.empty()) Bad("invalid_catalog");
                for (const auto& block : blocks)
                {
                    pos = 0; const int id = Number(block, pos, 3); const auto map = Quoted(block, pos);
                    const Key key{town, id}; if (!ValidKey(key) || result.areas.size() == MaxAreas || !keys.insert(KeyNumber(key)).second) Bad("invalid_catalog");
                    Area area; area.key = key; area.mapPath = Normalize("map/" + map, "invalid_catalog");
                    for (std::size_t at = block.find('`', pos); at != std::string::npos;)
                    {
                        const auto end = block.find('`', at + 1); if (end == std::string::npos) Bad("invalid_catalog"); auto kind = Lower(block.substr(at + 1, end - at - 1));
                        if (kind == "[normal]" || kind == "[gate]" || kind == "[dungeon gate]" || kind == "[pub]" || kind == "[fishing place]")
                        { if (!area.kind.empty()) Bad("invalid_catalog"); kind = kind.substr(1, kind.size() - 2); std::replace(kind.begin(), kind.end(), ' ', '_'); area.kind = std::move(kind); }
                        at = block.find('`', end + 1);
                    }
                    if (area.kind.empty()) Bad("invalid_catalog"); const auto text = pvf.Text(area.mapPath);
                    for (const auto& row : Rows(Block(text, "virtual movable area", true), 4, MaxRects)) area.walkable.push_back(ReadRect(row));
                    if (area.walkable.empty()) Bad("invalid_catalog");
                    for (const auto& row : Rows(Block(text, "town movable area", false), 6, MaxPortals))
                    { const Rect rect = ReadRect(row); const Key target{row[4], row[5]}; if (!ValidKey(target) && target != Key{-1, -1}) Bad("invalid_catalog"); area.portals.push_back({key, target, rect, PortalPoint(rect, area.walkable)}); }
                    result.areas.push_back(std::move(area));
                }
            }
            if (result.areas.empty()) Bad("invalid_catalog"); return result;
        }
    }

    bool Rect::Contains(double px, double py) const noexcept
    { return x <= px && px < Right(*this) && y <= py && py < Bottom(*this); }
    const Area* Catalog::Find(Key key) const noexcept
    { if (!ValidKey(key) || areas.size() > MaxAreas) return nullptr; for (const auto& area : areas) if (area.key == key) return &area; return nullptr; }
    bool Catalog::Route(Key start, Key goal, std::vector<Portal>& output, std::string& error) const
    {
        output.clear();
        try
        {
            if (!ValidKey(start) || !ValidKey(goal)) Bad("unknown_area");
            const auto index = IndexAreas(*this); const auto first = index.find(KeyNumber(start)), last = index.find(KeyNumber(goal));
            if (first == index.end() || last == index.end()) Bad("unknown_area");
            Budget budget; std::deque<std::size_t> queue{first->second}; std::vector<int> previous(areas.size(), -2); std::vector<const Portal*> links(areas.size(), nullptr); previous[first->second] = -1;
            while (!queue.empty())
            {
                const auto current = queue.front(); queue.pop_front();
                if (current == last->second)
                { for (std::size_t at = current; previous[at] >= 0; at = static_cast<std::size_t>(previous[at])) output.push_back(*links[at]); std::reverse(output.begin(), output.end()); error.clear(); return true; }
                for (const auto& portal : areas[current].portals)
                {
                    budget.Spend(); if (!ValidKey(portal.target)) continue; const auto it = index.find(KeyNumber(portal.target)); if (it == index.end()) continue; const auto target = it->second;
                    if (previous[target] != -2 || (areas[target].kind == "dungeon_gate" && target != last->second)) continue;
                    previous[target] = static_cast<int>(current); links[target] = &portal; queue.push_back(target);
                }
            }
            Bad("no_route");
        }
        catch (const Failure& failure) { output.clear(); return Fail(error, failure.code); }
        catch (...) { output.clear(); return Fail(error, "invalid_catalog"); }
    }
    bool Catalog::Waypoints(Key key, Point start, Waypoint goal, const std::vector<Portal>& blocked,
                            std::vector<Waypoint>& output, std::string& error) const
    {
        output.clear();
        try
        {
            const Area* area = Find(key); if (!area) Bad("unknown_area"); Budget budget; const auto ground = WithoutPortals(*area, blocked, budget);
            if (!std::isfinite(start.x) || !std::isfinite(start.y) || std::abs(start.x) > CoordinateLimit || std::abs(start.y) > CoordinateLimit || goal.x < -CoordinateLimit || goal.x > CoordinateLimit || goal.y < -CoordinateLimit || goal.y > CoordinateLimit) Bad("outside_walkable");
            std::deque<std::size_t> queue; std::vector<int> previous(ground.size(), -2); std::vector<bool> goals(ground.size(), false); std::vector<std::vector<Waypoint>> links(ground.size()); bool haveGoal = false;
            for (std::size_t i = 0; i < ground.size(); ++i)
            { if (ground[i].Contains(start.x, start.y)) { queue.push_back(i); previous[i] = -1; } goals[i] = ground[i].Contains(goal.x, goal.y); haveGoal |= goals[i]; }
            if (queue.empty() || !haveGoal) Bad("outside_walkable");
            if (BoxCovered(ground, start, AsPoint(goal), budget)) { output.push_back(goal); error.clear(); return true; }
            std::size_t final = ground.size();
            while (!queue.empty())
            {
                const auto current = queue.front(); queue.pop_front(); if (goals[current]) { final = current; break; }
                for (std::size_t next = 0; next < ground.size(); ++next)
                { budget.Spend(); if (previous[next] != -2) continue; if (Bridge(ground[current], ground[next], links[next], budget)) { previous[next] = static_cast<int>(current); queue.push_back(next); } }
            }
            if (final == ground.size()) Bad("no_path"); std::vector<Waypoint> turns{goal};
            for (std::size_t at = final; previous[at] >= 0; at = static_cast<std::size_t>(previous[at])) for (auto it = links[at].rbegin(); it != links[at].rend(); ++it) turns.push_back(*it);
            std::reverse(turns.begin(), turns.end()); std::vector<Point> path{start}; path.reserve(turns.size() + 1); for (auto point : turns) path.push_back(AsPoint(point));
            std::size_t at = 0;
            while (at + 1 < path.size())
            {
                bool found = false;
                for (std::size_t next = path.size() - 1; next > at; --next)
                { if (LegCovered(ground, path[at], path[next], at > 0 ? StopMargin : 0, next + 1 < path.size() ? StopMargin : 0, budget)) { output.push_back(turns[next - 1]); at = next; found = true; break; } }
                if (!found) Bad("no_path");
            }
            error.clear(); return true;
        }
        catch (const Failure& failure) { output.clear(); return Fail(error, failure.code); }
        catch (...) { output.clear(); return Fail(error, "invalid_catalog"); }
    }
    bool LoadCatalog(const wchar_t* path, Catalog& output, std::string& error) noexcept
    {
        try { Pvf pvf(path); auto candidate = ParseCatalog(pvf); candidate.pvfSha256 = Sha256(pvf.raw); output = std::move(candidate); error.clear(); return true; }
        catch (const Failure& failure) { return Fail(error, failure.code); }
        catch (...) { return Fail(error, "invalid_pvf"); }
    }
}
