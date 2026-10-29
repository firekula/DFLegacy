#pragma once
#include "client_state.h"
#include <algorithm>

namespace dfl::client_state::ui_detail
{
    struct Vector
    {
        std::uint32_t begin = 0, end = 0, capacity = 0;
        bool operator==(const Vector& other) const noexcept
        { return begin == other.begin && end == other.end && capacity == other.capacity; }
    };
    template<class R> bool Header(R& r, std::uint32_t base, std::uint32_t offset,
                                  std::uint32_t stride, Vector& v) noexcept
    {
        return r.ReadAt(base, offset, v.begin) && r.ReadAt(base, offset + 4, v.end)
            && r.ReadAt(base, offset + 8, v.capacity) && v.end >= v.begin && v.capacity >= v.end
            && v.begin % 4 == 0 && (v.end - v.begin) % stride == 0
            && (v.capacity - v.begin) % stride == 0 && (v.begin >= 0x10000 || v.capacity == 0);
    }
    template<class R> bool SameHeader(R& r, std::uint32_t base, std::uint32_t offset,
                                      std::uint32_t stride, const Vector& initial) noexcept
    { Vector final; return Header(r, base, offset, stride, final) && final == initial; }

    template<class R> void Selection(R& r, Ui& ui, std::uint32_t game) noexcept
    {
        Selector s; std::uint32_t object = 0, table = 0, phase = 0, repeated = 0;
        unsigned char pending = 0; std::int32_t selected = -1, suppression = 0;
        Vector rows;
        if (!r.ReadAt(game, 0x20, object) || !object || !r.Read(object, table) || table != 0x99A5B8
            || !r.Getter(object, 4, 0x8A2A20) || !r.Getter(object, 8, 0x8A0EB0)
            || !r.ReadAt(object, 0x100, pending) || pending > 1
            || !r.ReadAt(object, 0x1A0, phase) || phase > 4
            || !r.ReadAt(object, 0x68, s.stageToken) || !r.ReadAt(object, 0x200, selected)
            || !Header(r, object, 0x1F4, 16, rows) || (rows.end - rows.begin) / 16 > 64) return;
        s.instanceToken = object; s.transitionPending = {true, pending != 0}; s.phase = {true, phase};
        s.selectedIndex = selected;
        s.recordCount = {true, (rows.end - rows.begin) / 16};
        if (r.SignedAt(object, 0x144, suppression)) s.inputSuppression = {true, suppression};
        std::int32_t id = 0, difficulty = 0, maximum = 0;
        const bool projected = selected >= 0 && static_cast<std::uint32_t>(selected) < s.recordCount.value;
        if (projected)
        {
            const auto row = rows.begin + static_cast<std::uint32_t>(selected) * 16;
            if (!r.ReadAt(row, 0, id) || !r.ReadAt(row, 4, difficulty) || !r.ReadAt(row, 8, maximum)) return;
            s.dungeonId = {true, id}; s.difficulty = {true, difficulty}; s.maximumDifficulty = {true, maximum};
        }
        unsigned char finalPending = 0; std::int32_t finalSelected = 0, finalSuppression = 0;
        if (!r.ReadAt(game, 0x20, repeated) || repeated != object || !r.Read(object, repeated) || repeated != table
            || !SameHeader(r, object, 0x1F4, 16, rows)
            || !r.ReadAt(object, 0x200, finalSelected) || finalSelected != selected
            || !r.ReadAt(object, 0x100, finalPending) || finalPending != pending
            || !r.ReadAt(object, 0x1A0, repeated) || repeated != phase
            || !r.ReadAt(object, 0x68, repeated) || repeated != s.stageToken
            || (s.inputSuppression.known && (!r.SignedAt(object, 0x144, finalSuppression) || finalSuppression != suppression))) return;
        if (projected)
        {
            const auto row = rows.begin + static_cast<std::uint32_t>(selected) * 16;
            std::int32_t final = 0;
            if (!r.ReadAt(row, 0, final) || final != id || !r.ReadAt(row, 4, final) || final != difficulty
                || !r.ReadAt(row, 8, final) || final != maximum) return;
        }
        ui.selector = s;
    }
    template<class T> bool Same(const Value<T>& a, const Value<T>& b) noexcept
    { return a.known == b.known && (!a.known || a.value == b.value); }
    template<class R> bool StableSelection(R& r, const Ui& ui, std::uint32_t game) noexcept
    {
        Ui final; Selection(r, final, game);
        const auto& a = ui.selector; const auto& b = final.selector;
        return a.instanceToken && a.instanceToken == b.instanceToken && a.stageToken == b.stageToken
            && a.selectedIndex == b.selectedIndex && ui.selectorSelected.known
            && ui.selectorSelected.value == a.selectedIndex
            && Same(a.phase, b.phase) && Same(a.transitionPending, b.transitionPending)
            && Same(a.recordCount, b.recordCount) && Same(a.inputSuppression, b.inputSuppression)
            && Same(a.dungeonId, b.dungeonId) && Same(a.difficulty, b.difficulty)
            && Same(a.maximumDifficulty, b.maximumDifficulty);
    }

    constexpr std::uint32_t Classes[] = {0x23, 0x25, 0x30, 0x59, 0x6A};
    constexpr std::uint32_t Tables[] = {0xA7E254, 0xA7E2F4, 0xA7E434, 0xA7EA5C, 0xA7F4C4};
    template<class R> bool Window(R& r, std::uint32_t manager, std::uint32_t token,
                                  std::size_t kind, Dialog& d) noexcept
    {
        std::uint32_t table = 0, owner = 0, type = 0, root = 0, backlink = 0, rootTable = 0;
        unsigned char shown = 0, enabled = 0;
        if (!r.Read(token, table) || table != Tables[kind]
            || !r.Getter(token, 0x2C, 0x7BF640) || !r.Getter(token, 0x30, 0x7BF650)
            || !r.ReadAt(token, 4, type) || type != Classes[kind]
            || !r.ReadAt(token, 0x10, owner) || owner != manager || !r.ReadAt(token, 8, root) || !root
            || !r.Read(root, rootTable) || rootTable != 0xA7E09C
            || !r.ReadAt(root, 0x44, backlink) || backlink != token
            || !r.ReadAt(root, 0x10, shown) || shown > 1 || !r.ReadAt(root, 0x11, enabled) || enabled > 1) return false;
        d = {type, token, root, shown != 0, enabled != 0}; return true;
    }
    template<class R> Dialogs Inventory(R& r, std::uint32_t manager) noexcept
    {
        Dialogs out; Vector closing; std::array<Vector, 5> banks{};
        std::array<std::uint32_t, 64> closes{}, tokens{};
        std::array<std::size_t, 64> kinds{}; std::size_t count = 0;
        if (!Header(r, manager, 0xEF8, 4, closing)) return out;
        const auto closeCount = (closing.end - closing.begin) / 4;
        if (closeCount > closes.size()) { out.truncated = true; return out; }
        for (std::uint32_t i = 0; i < closeCount; ++i)
            if (!r.ReadAt(closing.begin, i * 4, closes[i]) || !closes[i]) return out;
        for (std::size_t k = 0; k < banks.size(); ++k)
        {
            if (!Header(r, manager, 0xD8 + Classes[k] * 16, 4, banks[k])) return out;
            const auto size = (banks[k].end - banks[k].begin) / 4;
            if (size > tokens.size() - count) { out.truncated = true; return out; }
            for (std::uint32_t i = 0; i < size; ++i)
            {
                std::uint32_t token = 0;
                if (!r.ReadAt(banks[k].begin, i * 4, token) || !token
                    || std::find(tokens.begin(), tokens.begin() + count, token) != tokens.begin() + count) return out;
                tokens[count] = token; kinds[count++] = k;
            }
        }
        for (std::size_t i = 0; i < count; ++i)
        {
            if (std::find(closes.begin(), closes.begin() + closeCount, tokens[i]) != closes.begin() + closeCount) continue;
            if (!Window(r, manager, tokens[i], kinds[i], out.items[out.count])) { out.count = 0; return out; }
            ++out.count;
        }
        // Repeat registration, queue content and every root identity. No native getters are called.
        if (!SameHeader(r, manager, 0xEF8, 4, closing)) { out.count = 0; return out; }
        for (std::uint32_t i = 0; i < closeCount; ++i)
        { std::uint32_t token = 0; if (!r.ReadAt(closing.begin, i * 4, token) || token != closes[i]) { out.count = 0; return out; } }
        std::size_t index = 0, item = 0;
        for (std::size_t k = 0; k < banks.size(); ++k)
        {
            if (!SameHeader(r, manager, 0xD8 + Classes[k] * 16, 4, banks[k])) { out.count = 0; return out; }
            for (std::uint32_t i = 0; i < (banks[k].end - banks[k].begin) / 4; ++i, ++index)
            {
                std::uint32_t token = 0; Dialog final;
                if (!r.ReadAt(banks[k].begin, i * 4, token) || token != tokens[index]) { out.count = 0; return out; }
                if (std::find(closes.begin(), closes.begin() + closeCount, token) != closes.begin() + closeCount) continue;
                if (!Window(r, manager, token, k, final) || final.rootToken != out.items[item].rootToken
                    || final.shownRaw != out.items[item].shownRaw || final.inputEnabledRaw != out.items[item].inputEnabledRaw)
                { out.count = 0; return out; }
                ++item;
            }
        }
        std::uint32_t finalManager = 0;
        if (!r.Read(0xB5DCE8, finalManager) || finalManager != manager) { out.count = 0; return out; }
        out.valid = true; return out;
    }
}
