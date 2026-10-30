#pragma once
#include "client_state.h"
#include <algorithm>

namespace dfl::client_state::ui_detail
{
    template<class R> void Commands(R& r, Ui& ui, const Player& me, bool haveMe,std::uint32_t scene) noexcept
    {
        Ui::CommandInput out;std::uint32_t actor=0,raw=0,kind=0,category=0,uid=0,table=0,repeated=0;
        if (!r.Read(0xB75A14,actor) || !actor || !r.Getter(actor,0x5D4,0x493770)
            || !r.ReadAt(actor,0xCC,category) || category!=0x111
            || !r.ReadAt(actor,0xD0,kind) || (kind!=0x111 && (scene==3 || kind!=0))
            || !r.SecureAt(actor,0xD4,uid) || !uid || uid>65535
            || (haveMe && (!me.uid.known || me.uid.value!=uid || !r.ReadAt(me.userToken,0x110,repeated) || repeated!=actor))
            || !r.ReadAt(actor,0x2384,raw)) return;
        const auto checker=raw?raw:0xB5DE68; // 493770 returns the literal static object address.
        const auto supported=[&]() noexcept {
            return r.Read(checker,table) && table==0x99A88C && r.Getter(checker,0x28,0x8BA0C0)
                && r.Getter(checker,0x30,0x8BA7F0) && r.Getter(checker,0x40,0x8BA030) && r.Getter(checker,0x44,0x43BC30);
        };
        if (!supported() || !r.ReadAt(checker,0x64,out.keyboardLockLevel)) return;
        std::int32_t mouseLevel=0;unsigned char mouseFlag=0;
        const bool mouseKnown=r.ReadAt(checker,0x68,mouseLevel) && r.ReadAt(checker,0x90,mouseFlag) && mouseFlag<=1;
        std::int32_t finalLevel=0;std::uint16_t userUid=0;
        if (!r.Read(0xB75A14,repeated) || repeated!=actor || !r.Getter(actor,0x5D4,0x493770)
            || !r.SecureAt(actor,0xD4,repeated) || repeated!=uid
            || !r.ReadAt(actor,0xD0,repeated) || repeated!=kind
            || !r.ReadAt(actor,0xCC,repeated) || repeated!=category
            || !r.ReadAt(actor,0x2384,repeated) || repeated!=raw
            || (haveMe && (!r.ReadAt(me.userToken,0x110,repeated) || repeated!=actor
                          || !r.ReadAt(me.userToken,4,userUid) || userUid!=uid))
            || !supported() || !r.ReadAt(checker,0x64,finalLevel) || finalLevel!=out.keyboardLockLevel) return;
        std::int32_t finalMouse=0;unsigned char finalFlag=0;
        if(mouseKnown && r.ReadAt(checker,0x68,finalMouse) && finalMouse==mouseLevel
            && r.ReadAt(checker,0x90,finalFlag) && finalFlag==mouseFlag)
        {out.mouseLockLevel={true,mouseLevel};out.mouseFlag90={true,mouseFlag!=0};}
        out.present=true;out.actorToken=actor;out.checkerToken=checker;out.uid=uid;ui.commandInput=out;
    }
    template<class R> Value<bool> MouseButtons(R& r) noexcept
    {
        constexpr std::array<unsigned char,5> Read={0xA0,0x08,0xD2,0xCF,0x00};
        constexpr std::array<unsigned char,7> Acquire={0xC6,0x05,0x08,0xD2,0xCF,0x00,0x01};
        constexpr std::array<unsigned char,7> Release={0xC6,0x05,0x08,0xD2,0xCF,0x00,0x00};
        std::array<unsigned char,5> read{};std::array<unsigned char,7> acquire{},release{};
        unsigned char first=0,last=0;
        if(r.Read(0x8FCAE8,read) && read==Read && r.Read(0x7993C4,acquire) && acquire==Acquire
            && r.Read(0x8FCC9D,release) && release==Release && r.Read(0xCFD208,first) && first<=1
            && r.Read(0xCFD208,last) && last==first)return {true,first!=0};
        return {};
    }
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

    constexpr auto& Classes=DialogClassIds;
    constexpr std::array<std::uint32_t,8> Tables={0xA7E614,0xA7E6B4,0xA7E254,0xA7E2F4,0xA7E434,0xA7F154,0xA7EA5C,0xA7F4C4};
    constexpr unsigned LayerIndex(std::uint32_t type) noexcept {return type<=0x0F?0:type<=0x17?1:2;}
    struct Layer
    {
        std::uint32_t token=0,head=0,first=0,last=0,count=0;
        unsigned char shown=0,enabled=0;
        std::array<std::uint32_t,128> nodes{},next{},previous{},roots{};
        bool operator==(const Layer& b) const noexcept
        {
            return token==b.token && head==b.head && first==b.first && last==b.last && count==b.count
                && shown==b.shown && enabled==b.enabled && nodes==b.nodes && next==b.next
                && previous==b.previous && roots==b.roots;
        }
    };
    // Order is native forward rendering order within the class's layer.
    template<class R> bool ReadLayer(R& r,std::uint32_t manager,unsigned index,Layer& layer) noexcept
    {
        std::uint32_t table=0;
        if (index>2 || !r.ReadAt(manager,0xC8+index*4,layer.token) || !layer.token || !r.Read(layer.token,table) || table!=0x9A1608
            || !r.Getter(layer.token,0x10,0x8F6880)
            || !r.ReadAt(layer.token,0x10,layer.shown) || layer.shown>1
            || !r.ReadAt(layer.token,0x11,layer.enabled) || layer.enabled>1
            || !r.ReadAt(layer.token,0x64,layer.head) || !layer.head
            || !r.ReadAt(layer.token,0x68,layer.count) || layer.count>layer.nodes.size()
            || !r.Read(layer.head,layer.first) || !r.ReadAt(layer.head,4,layer.last)) return false;
        auto node=layer.first,previous=layer.head;
        for(std::uint32_t i=0;i<layer.count;++i)
        {
            if (!node || node==layer.head || std::find(layer.nodes.begin(),layer.nodes.begin()+i,node)!=layer.nodes.begin()+i
                || !r.Read(node,layer.next[i]) || !r.ReadAt(node,4,layer.previous[i]) || layer.previous[i]!=previous
                || !r.ReadAt(node,8,layer.roots[i]) || !layer.roots[i]
                || std::find(layer.roots.begin(),layer.roots.begin()+i,layer.roots[i])!=layer.roots.begin()+i) return false;
            layer.nodes[i]=node;previous=node;node=layer.next[i];
        }
        return node==layer.head && previous==layer.last;
    }
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
        Dialogs out; Vector closing; std::array<Vector,DialogClassIds.size()> banks{};
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
        std::array<Layer,3> layers{};std::array<bool,3> used{},known{};
        for(std::size_t i=0;i<out.count;++i)used[LayerIndex(out.items[i].classId)]=true;
        for(unsigned i=0;i<3;++i)if(used[i])known[i]=ReadLayer(r,manager,i,layers[i]);
        for(unsigned i=0;i<3;++i)for(unsigned j=i+1;j<3;++j)
            if(known[i]&&known[j]&&layers[i].token==layers[j].token){known[i]=known[j]=false;}
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
        for(unsigned layerIndex=0;layerIndex<3;++layerIndex)
        {
            Layer finalLayer;
            if(!known[layerIndex] || !ReadLayer(r,manager,layerIndex,finalLayer) || !(layers[layerIndex]==finalLayer))continue;
            const auto& layer=layers[layerIndex];
            for(std::size_t i=0;i<out.count;++i)
            {
                auto& d=out.items[i];auto& p=d.placement;
                if(LayerIndex(d.classId)!=layerIndex)continue;
                const auto found=std::find(layer.roots.begin(),layer.roots.begin()+layer.count,d.rootToken);
                p.present=true;p.attached=found!=layer.roots.begin()+layer.count;p.layerToken=layer.token;
                p.layerShownRaw=layer.shown!=0;p.layerInputEnabledRaw=layer.enabled!=0;
                if(p.attached)p.order=static_cast<std::uint32_t>(found-layer.roots.begin());
            }
        }
        out.valid = true; return out;
    }
}
