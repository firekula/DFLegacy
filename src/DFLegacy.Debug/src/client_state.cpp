#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <limits>
#include "client_state.h"
#include "client_skill_state.h"
#include "client_ui_state.h"

namespace dfl::client_state
{
    namespace
    {
        void Error(Snapshot& s, const char* code) noexcept
        {
            for (std::size_t i = 0; i < s.errorCount; ++i)
                if (std::strcmp(s.errors[i].data(), code) == 0) return;
            if (s.errorCount < s.errors.size())
                strncpy_s(s.errors[s.errorCount++].data(), 64, code, _TRUNCATE);
        }
        struct Reads
        {
            const Reader& reader;
            Snapshot& snapshot;
            std::uint32_t manager = 0, key = 0;
            template<class T> bool Read(std::uint32_t address, T& out) const noexcept
            {
                return address >= 0x10000 && address <= 0xFFFFFFF0U - sizeof(T)
                    && reader.read(reader.context, address, &out, sizeof(T));
            }
            static bool Address(std::uint32_t base, std::uint32_t offset, std::uint32_t& result) noexcept
            {
                if (base < 0x10000 || offset > UINT32_MAX - base) return false;
                result = base + offset; return true;
            }
            template<class T> bool ReadAt(std::uint32_t base, std::uint32_t offset, T& out) const noexcept
            {
                std::uint32_t address = 0;
                return Address(base, offset, address) && Read(address, out);
            }
            bool SecureAt(std::uint32_t base, std::uint32_t offset, std::uint32_t& out) const noexcept
            {
                std::uint32_t address = 0;
                return Address(base, offset, address) && Secure(address, out);
            }
            bool SignedAt(std::uint32_t base, std::uint32_t offset, std::int32_t& out) const noexcept
            {
                std::uint32_t address = 0;
                return Address(base, offset, address) && Signed(address, out);
            }
            bool FloatAt(std::uint32_t base, std::uint32_t offset, float& out) const noexcept
            {
                std::uint32_t address = 0;
                return Address(base, offset, address) && Float(address, out);
            }
            bool Secure(std::uint32_t address, std::uint32_t& out) const noexcept
            {
                std::uint32_t encoded = 0, index = 0, count = 0, first = 0, second = 0;
                if (!Read(address, encoded) || !ReadAt(address, 4, index)
                    || ((encoded ^ key) - address) != index || !ReadAt(manager, 4, count)
                    || index >= count || index > 0x1000000
                    || !ReadAt(manager, 0x44, first) || !ReadAt(manager, 0x48, second)
                    || first < 0x10000 || second < 0x10000
                    || first > UINT32_MAX - index * 4 || second > UINT32_MAX - index * 4) return false;
                first += index * 4; second += index * 4;
                std::uint32_t a = 0, b = 0;
                if (!Read(first, a) || !Read(second, b)) return false;
                a ^= first ^ key; b ^= second ^ key;
                if (a != b) return false;
                out = a; return true;
            }
            bool Signed(std::uint32_t address, std::int32_t& out) const noexcept
            {
                std::uint32_t bits = 0;
                if (!Secure(address, bits)) return false;
                std::memcpy(&out, &bits, sizeof(out)); return true;
            }
            bool Float(std::uint32_t address, float& out) const noexcept
            {
                std::uint32_t bits = 0;
                if (!Secure(address, bits)) return false;
                std::memcpy(&out, &bits, sizeof(out)); return std::isfinite(out);
            }
            bool Getter(std::uint32_t object, std::uint32_t offset, std::uint32_t expected) const noexcept
            {
                std::uint32_t table = 0, function = 0;
                return Read(object, table) && table && ReadAt(table, offset, function) && function == expected;
            }
            Position Coordinates(std::uint32_t actor) const noexcept
            {
                Position p; std::uint32_t controller = 0, provider = 0, vector = 0, table = 0, getter = 0;
                unsigned char active = 0;
                if (!ReadAt(actor, 0xE0, controller) || !controller
                    || !ReadAt(controller, 0x78, provider) || !ReadAt(controller, 0x7C, active)
                    || (provider && active) || !Read(controller, vector)) return p;
                if (!vector && !Address(controller, 8, vector)) return p;
                if (!Read(vector, table) || !table || !Read(table, getter)) return p;
                if (getter == 0x4CAC70)
                    p.known = FloatAt(vector, 0x35, p.x) && FloatAt(vector, 0x3D, p.y) && FloatAt(vector, 0x45, p.z);
                else if (getter == 0x4736A0)
                    p.known = ReadAt(vector, 4, p.x) && ReadAt(vector, 8, p.y) && ReadAt(vector, 12, p.z);
                p.known = p.known && std::isfinite(p.x) && std::isfinite(p.y) && std::isfinite(p.z)
                    && std::fabs(p.x) < 1000000 && std::fabs(p.y) < 1000000 && std::fabs(p.z) < 1000000;
                return p;
            }
            void Mp(Player& p, bool local) noexcept
            {
                std::uint32_t actor=0, own=0, uid=0, kind=0,category=0;
                const bool town=snapshot.scene.known && snapshot.scene.value==1;
                if (!p.uid.known || !ReadAt(p.userToken,0x110,actor) || !actor
                    || (!town && actor!=p.actorToken)
                    || !Read(0xB75A14,own) || !own || ((actor==own)!=local)
                    || !ReadAt(actor,0xCC,category) || category!=0x111
                    || !ReadAt(actor,0xD0,kind) || (kind!=0x111 && (!town || kind!=0))
                    || !SecureAt(actor,0xD4,uid) || uid!=p.uid.value) return;
                if (!Getter(actor,0x340,0x652A70) || !Getter(actor,0x344,0x652A80) || !Getter(actor,0x2E0,0x652670))
                { Error(snapshot,"unsupported_mp_layout"); return; }
                Value<std::int32_t> mp{}, maximumMp{};
                std::int32_t current=0, base=0;float factor=0;
                if (SignedAt(actor,0x1170,current) && current>=0) mp={true,current};
                else Error(snapshot,"invalid_mp");
                if (SignedAt(actor,0xE48,base) && base>=0 && FloatAt(actor,0xE50,factor) && factor>=0)
                {
                    // 652A80 stores the integer base as float32 before multiplying.
                    const volatile float nativeBase=static_cast<float>(base);
                    const double maximum=static_cast<double>(nativeBase)*factor;
                    if (std::isfinite(maximum) && maximum>=0 && maximum<=INT32_MAX)
                        maximumMp={true,static_cast<std::int32_t>(maximum)};
                }
                if (!maximumMp.known) Error(snapshot,"invalid_max_mp");
                std::uint32_t repeated=0;std::uint16_t userUid=0;
                if (!ReadAt(p.userToken,0x110,repeated) || repeated!=actor
                    || !ReadAt(p.userToken,4,userUid) || userUid!=uid
                    || !SecureAt(actor,0xD4,repeated) || repeated!=uid
                    || !ReadAt(actor,0xD0,repeated) || repeated!=kind
                    || !ReadAt(actor,0xCC,repeated) || repeated!=category
                    || !Read(0xB75A14,repeated) || repeated!=own
                    || !Getter(actor,0x340,0x652A70) || !Getter(actor,0x344,0x652A80) || !Getter(actor,0x2E0,0x652670))
                { Error(snapshot,"mp_identity_changed"); return; }
                p.mp=mp;p.maxMp=maximumMp;p.mpActorToken=actor;p.mpLocal=local;
            }
            Player Actor(std::uint32_t user, std::uint32_t actor, std::uint32_t map, bool localMp = false) noexcept
            {
                Player p; std::uint16_t uid = 0; std::uint32_t kind = 0, secureUid = 0, binding = 0;
                if (!user || !actor || !ReadAt(user, 4, uid) || !uid
                    || !ReadAt(user, 0x110, binding) || binding != actor
                    || !ReadAt(actor, 0xD0, kind) || kind != 0x111
                    || !SecureAt(actor, 0xD4, secureUid) || secureUid != uid)
                { Error(snapshot, "invalid_player_identity"); return p; }
                p.uid = {true, uid}; p.userToken = user; p.actorToken = actor;
                if (ReadAt(actor, 0xE4, p.mapToken)) p.sameMap = {true, p.mapToken != 0 && p.mapToken == map};
                if (Getter(actor, 0x330, 0x652A20) && Getter(actor, 0x334, 0x652A30))
                {
                    std::int32_t hp = 0, base = 0; float factor = 0;
                    if (SignedAt(actor, 0x1168, hp) && hp >= 0) p.hp = {true, hp};
                    else Error(snapshot, "invalid_hp");
                    if (SignedAt(actor, 0xE38, base) && FloatAt(actor, 0xE40, factor))
                    {
                        const double maximum = static_cast<double>(base) * factor;
                        if (maximum >= 0 && maximum <= INT32_MAX && std::isfinite(maximum))
                            p.maxHp = {true, static_cast<std::int32_t>(maximum)};
                    }
                }
                else Error(snapshot, "unsupported_hp_layout");
                Mp(p,localMp);
                std::int32_t dead = 0;
                if (Getter(actor, 0x318, 0x652A00) && SignedAt(actor, 0x142C, dead)) p.dead = {true, dead != 0};
                if (p.sameMap.known && p.mapToken != 0) p.detached = {true, false};
                else if (p.sameMap.known && p.dead.known && p.dead.value) p.detached = {true, true};
                if (p.sameMap.known && p.sameMap.value && p.dead.known && !p.dead.value)
                {
                    if (Getter(actor, 0x80, 0x471610)) p.position = Coordinates(actor);
                    if (!p.position.known) Error(snapshot, "position_unknown");
                }
                return p;
            }
            Player TownActor(std::uint32_t user, std::uint32_t actor, std::uint32_t map, bool localMp=false) noexcept
            {
                Player p; std::uint16_t uid = 0; std::uint32_t table = 0, secureUid = 0, binding = 0, actorMap = 0;
                if (!user || !actor || !ReadAt(user, 4, uid) || !uid
                    || !ReadAt(user, 0x10C, binding) || binding != actor
                    || !Read(actor, table) || table != 0x9988DC
                    || !SecureAt(actor, 0xD4, secureUid) || secureUid != uid
                    || !ReadAt(actor, 0xE4, actorMap))
                { Error(snapshot, "invalid_town_player_identity"); return p; }
                p.uid = {true, uid}; p.userToken = user; p.actorToken = actor; p.mapToken = actorMap;
                p.sameMap = {true, actorMap != 0 && actorMap == map}; p.detached = {true, actorMap == 0};
                if (p.sameMap.value)
                {
                    if (Getter(actor, 0x80, 0x471610)) p.position = Coordinates(actor);
                    if (!p.position.known) Error(snapshot, "town_position_unknown");
                }
                // Town's virtual character is not the Dungeon combat actor.
                // HP, max HP and death remain unknown even if stale offsets are readable.
                Mp(p,localMp);
                return p;
            }
        };

        std::uint32_t FindUser(Reads& r, std::uint32_t actor, std::uint32_t uid,
                               std::uint32_t bindingOffset = 0x110) noexcept
        {
            std::uint32_t manager = 0, head = 0, node = 0;
            if (!r.Read(0xB5DCB8, manager) || !manager || !r.ReadAt(manager, 12, head)
                || !head || !r.Read(head, node)) return 0;
            std::array<std::uint32_t, 256> visited{}; std::size_t count = 0;
            while (node && node != head && count < visited.size())
            {
                if (std::find(visited.begin(), visited.begin() + count, node) != visited.begin() + count) break;
                visited[count++] = node;
                std::uint32_t user = 0, binding = 0; std::uint16_t candidate = 0;
                if (!r.ReadAt(node, 12, user) || !user) return 0;
                if (r.ReadAt(user, 4, candidate) && candidate == uid && r.ReadAt(user, bindingOffset, binding) && binding == actor) return user;
                if (!r.Read(node, node)) return 0;
            }
            return 0;
        }
        void UiState(Reads& r, Snapshot& s, std::uint32_t game) noexcept
        {
            if(s.scene.known && s.scene.value==2) ui_detail::Commands(r,s.ui,s.me,false,2);
            if (s.scene.known && s.scene.value == 2) ui_detail::Selection(r, s.ui, game);
            std::uint32_t ui = 0, object = 0, table = 0, panel = 0, repeated = 0;
            if (r.ReadAt(game, 0x20, object) && object && r.Read(object, table) && table == 0x99A5B8)
            {
                unsigned char active = 0; std::int32_t selected = 0, hover = 0;
                if (r.ReadAt(object, 4, active) && r.ReadAt(object, 0x200, selected) && r.ReadAt(object, 0x204, hover)
                    && r.ReadAt(game, 0x20, repeated) && repeated == object)
                {
                    s.ui.selectorActive = {true, active}; s.ui.selectorSelected = {true, selected}; s.ui.selectorHover = {true, hover};
                }
            }
            if (!r.Read(0xB5DCE8, ui) || !ui) return;
            Ui candidate = s.ui; std::int32_t mode = 0;
            candidate.mouseButtonsDeferred=ui_detail::MouseButtons(r);
            candidate.dialogs = ui_detail::Inventory(r, ui);
            if (r.ReadAt(ui, 0xFC4, mode)) candidate.selectionMode = {true, mode};
            if (r.ReadAt(ui, 0xF08, object) && object && r.Read(object, table) && table == 0xA7CF2C)
            {
                std::int32_t state = 0;
                if (r.ReadAt(object, 96, state) && r.ReadAt(ui, 0xF08, repeated) && repeated == object)
                    candidate.countdownState = {true, state};
            }
            if (r.ReadAt(ui, 0xFF0, object) && object && r.Read(object, table) && table == 0xA7CF3C
                && r.ReadAt(object, 4, panel) && panel)
            {
                unsigned char visible = 0, enabled = 0;
                if (r.ReadAt(panel, 0x10, visible) && visible <= 1 && r.ReadAt(panel, 0x11, enabled) && enabled <= 1
                    && r.ReadAt(object, 4, repeated) && repeated == panel
                    && r.ReadAt(ui, 0xFF0, repeated) && repeated == object)
                { candidate.minimapVisible = {true, visible != 0}; candidate.minimapEnabled = {true, enabled != 0}; }
            }
            std::uint32_t menu = 0;
            if (r.ReadAt(ui, 0x78, menu) && menu && r.ReadAt(menu, 0x58, panel) && panel)
            {
                unsigned char visible = 0;
                if (r.ReadAt(panel, 0x10, visible) && visible <= 1 && r.ReadAt(menu, 0x58, repeated) && repeated == panel)
                {
                    candidate.menuVisible = {true, visible != 0};
                    if (visible)
                    {
                        std::uint32_t actor = 0, uid = 0, targetUser = 0;
                        if (r.ReadAt(menu, 8, actor) && actor && r.Read(actor, table) && table == 0x9988DC
                            && r.SecureAt(actor, 0xD4, uid) && uid && uid <= 65535)
                        {
                            targetUser = FindUser(r, actor, uid, 0x10C);
                            if (targetUser) candidate.menuTargetUid = {true, uid};
                        }
                        std::uint32_t begin = 0, end = 0;
                        if (r.ReadAt(menu, 0xFC, begin) && r.ReadAt(menu, 0x100, end) && end >= begin
                            && (end - begin) % 4 == 0 && (end - begin) / 4 <= candidate.menuActions.size())
                        {
                            candidate.menuActionCount = (end - begin) / 4; candidate.menuActionsKnown = true;
                            for (std::size_t i = 0; i < candidate.menuActionCount; ++i)
                                if (!r.ReadAt(begin, static_cast<std::uint32_t>(i * 4), candidate.menuActions[i])) candidate.menuActionsKnown = false;
                            std::uint32_t finalBegin = 0, finalEnd = 0;
                            candidate.menuActionsKnown = candidate.menuActionsKnown
                                && r.ReadAt(menu, 0xFC, finalBegin) && finalBegin == begin
                                && r.ReadAt(menu, 0x100, finalEnd) && finalEnd == end;
                        }
                        unsigned char finalVisible = 0; std::uint32_t finalActor = 0, finalUid = 0, finalBinding = 0;
                        if (!r.ReadAt(panel, 0x10, finalVisible) || finalVisible != visible
                            || !r.ReadAt(menu, 8, finalActor) || finalActor != actor
                            || (candidate.menuTargetUid.known && (!r.SecureAt(actor, 0xD4, finalUid) || finalUid != uid
                                || !r.ReadAt(targetUser, 0x10C, finalBinding) || finalBinding != actor)))
                        { candidate.menuVisible = {}; candidate.menuTargetUid = {}; candidate.menuActionsKnown = false; }
                    }
                }
                if (!r.ReadAt(ui, 0x78, repeated) || repeated != menu
                    || !r.ReadAt(menu, 0x58, repeated) || repeated != panel)
                { candidate.menuVisible = {}; candidate.menuTargetUid = {}; candidate.menuActionsKnown = false; }
            }
            if (r.Read(0xB5DCE8, repeated) && repeated == ui) s.ui = candidate;
        }
        bool TownState(Reads& r, Snapshot& s, std::uint32_t game) noexcept
        {
            std::uint32_t context = 0, area = 0, actor = 0, uid = 0, map = 0;
            std::int32_t townId = -1, areaId = -1;
            if (!r.ReadAt(game, 0x1C, context) || !context || !r.ReadAt(context, 0x88, townId) || townId < 0
                || !r.ReadAt(context, 0x8C, area) || !area || !r.Read(area, areaId) || areaId < 0
                || !r.Read(0xB75A18, actor) || !actor || !r.ReadAt(actor, 0xE4, map) || !map
                || !r.SecureAt(actor, 0xD4, uid) || !uid || uid > 65535)
            { Error(s, "town_identity_unreadable"); return false; }
            const auto user = FindUser(r, actor, uid, 0x10C);
            s.me = r.TownActor(user, actor, map, true); s.haveMe = s.me.uid.known;
            if (!s.haveMe) return false;
            ui_detail::Commands(r,s.ui,s.me,true,1);
            auto& town = s.town; town.present = true; town.townId = townId; town.areaId = areaId;
            town.contextToken = context; town.mapToken = map; town.position = s.me.position;
            unsigned char autoMove = 0;
            if (r.Read(0xB5F0A0, autoMove) && autoMove <= 1) town.autoMoveActive = {true, autoMove != 0};
            std::int32_t x = 0, y = 0;
            if (r.ReadAt(actor, 0x23C, x) && r.ReadAt(actor, 0x240, y)) { town.destinationX = {true, x}; town.destinationY = {true, y}; }
            if (r.ReadAt(actor, 0x234, x) && r.ReadAt(actor, 0x238, y)) { town.directionX = {true, x}; town.directionY = {true, y}; }
            std::uint32_t party = 0;
            if (!r.Read(0xB5DCC8, party) || !party) { Error(s, "party_unavailable"); s.truncated = true; }
            else for (std::uint32_t slot = 0; slot < MaximumPlayers; ++slot)
            {
                std::uint32_t memberUser = 0, memberActor = 0;
                if (!r.ReadAt(party, 0x10 + slot * 4, memberUser)) { s.truncated = true; Error(s, "party_slot_unreadable"); continue; }
                if (!memberUser) continue;
                if (!r.ReadAt(memberUser, 0x10C, memberActor) || !memberActor) { s.truncated = true; Error(s, "town_party_actor_unreadable"); continue; }
                const auto member = r.TownActor(memberUser, memberActor, map, memberUser==user && memberActor==actor);
                if (!member.uid.known) { s.truncated = true; continue; }
                bool duplicate = false;
                for (std::size_t i = 0; i < s.playerCount; ++i) duplicate = duplicate || s.players[i].uid.value == member.uid.value;
                if (duplicate) { s.truncated = true; Error(s, "duplicate_player_uid"); continue; }
                s.players[s.playerCount++] = member;
            }
            bool haveMe = false;
            for (std::size_t i = 0; i < s.playerCount; ++i) haveMe = haveMe || s.players[i].actorToken == actor;
            if (!haveMe)
            {
                s.truncated = true; Error(s, "self_missing_from_party");
                if (s.playerCount < MaximumPlayers) s.players[s.playerCount++] = s.me;
            }
            std::uint32_t final = 0, finalUid = 0; std::int32_t finalId = -1;
            const bool stable = r.Read(0xB5D6F0, final) && final == game
                && r.SecureAt(game, 0x10, final) && final == 1
                && r.ReadAt(game, 0x1C, final) && final == context
                && r.ReadAt(context, 0x88, finalId) && finalId == townId
                && r.ReadAt(context, 0x8C, final) && final == area && r.Read(area, finalId) && finalId == areaId
                && r.Read(0xB75A18, final) && final == actor
                && r.ReadAt(user, 0x10C, final) && final == actor
                && r.ReadAt(actor, 0xE4, final) && final == map && r.SecureAt(actor, 0xD4, finalUid) && finalUid == uid
                && r.Read(0xCFD458, final) && final == r.manager && r.Read(0xCFD45C, final) && final == r.key;
            if (!stable) Error(s, "identity_changed_during_capture");
            return stable;
        }
        void Players(Reads& r, Snapshot& s) noexcept
        {
            std::uint32_t party = 0;
            if (!r.Read(0xB5DCC8, party) || !party) { Error(s, "party_unavailable"); s.truncated = true; }
            else for (std::uint32_t slot = 0; slot < MaximumPlayers; ++slot)
            {
                std::uint32_t user = 0, actor = 0;
                if (!r.ReadAt(party, 0x10 + slot * 4, user)) { s.truncated = true; Error(s, "party_slot_unreadable"); continue; }
                if (!user) continue;
                if (!r.ReadAt(user, 0x110, actor) || !actor) { s.truncated = true; Error(s, "party_actor_unreadable"); continue; }
                const auto player = r.Actor(user, actor, s.room.mapToken, user == s.me.userToken && actor == s.me.actorToken);
                if (!player.uid.known) { s.truncated = true; continue; }
                bool duplicate = false;
                for (std::size_t i = 0; i < s.playerCount; ++i)
                    duplicate = duplicate || s.players[i].uid.value == player.uid.value;
                if (duplicate) { s.truncated = true; Error(s, "duplicate_player_uid"); continue; }
                s.players[s.playerCount++] = player;
            }
            bool haveMe = false;
            for (std::size_t i = 0; i < s.playerCount; ++i) haveMe = haveMe || s.players[i].actorToken == s.me.actorToken;
            if (!haveMe && s.haveMe)
            {
                s.truncated = true; Error(s, "self_missing_from_party");
                if (s.playerCount < MaximumPlayers) s.players[s.playerCount++] = s.me;
            }
        }
        void Doors(Reads& r, Snapshot& s) noexcept
        {
            std::uint32_t definition = 0, phase = 0; unsigned char mask = 0, boss = 0;
            std::int32_t mapClear = 0;
            if (r.SignedAt(s.room.mapToken, 0x10C, mapClear)) s.room.mapClearState = {true, mapClear};
            else Error(s, "map_clear_state_unknown");
            if (r.ReadAt(s.room.stageToken, 0x110, boss)) s.room.bossDead = {true, boss != 0};
            if (r.ReadAt(s.room.stageToken, 0x954, phase)) s.room.assemblyPhase = {true, phase};
            if (r.ReadAt(s.room.stageToken, 0x50, definition) && definition && r.ReadAt(definition, 0x1F8, mask))
            {
                s.room.directionMask = {true, mask};
                constexpr unsigned char Bits[] = {4, 1, 2, 8};
                for (std::uint32_t i = 0; i < 4; ++i)
                {
                    // Definition mask describes exit topology, not battle completion/passability.
                    auto& door = s.room.doors[i]; door.enabled = {true, (mask & Bits[i]) != 0};
                    auto& rect = door.rect; const auto offset = 0x1FC + i * 16;
                    rect.known = r.ReadAt(definition, offset, rect.left) && r.ReadAt(definition, offset + 4, rect.top)
                        && r.ReadAt(definition, offset + 8, rect.width) && r.ReadAt(definition, offset + 12, rect.height)
                        && rect.width > 0 && rect.height > 0 && rect.width < 100000 && rect.height < 100000;
                }
            }
            bool complete = !s.truncated; std::uint32_t living = 0;
            for (std::size_t i = 0; i < s.playerCount; ++i)
            {
                const auto& player = s.players[i];
                if (!player.dead.known || !player.sameMap.known) complete = false;
                else if (!player.dead.value && player.sameMap.value)
                {
                    if (!player.position.known) complete = false;
                    else ++living;
                }
            }
            if (complete) { s.room.livingCount = {true, living}; s.room.majorityRequired = {true, living / 2 + 1}; }
        }
        void Enemies(Reads& r, Snapshot& s) noexcept
        {
            std::uint32_t begin = 0, end = 0;
            if (!r.ReadAt(s.room.mapToken, 0x10, begin) || !r.ReadAt(s.room.mapToken, 0x14, end)
                || end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > 4096)
            { s.truncated = true; Error(s, "invalid_map_vector"); return; }
            const auto count = (end - begin) / 4;
            if (count > MaximumEnemies) { s.truncated = true; Error(s, "map_object_limit"); }
            for (std::uint32_t i = 0; i < (std::min)(count, static_cast<std::uint32_t>(MaximumEnemies)); ++i)
            {
                std::uint32_t object = 0, kind = 0;
                if (!r.ReadAt(begin, i * 4, object)) { s.truncated = true; Error(s, "map_object_unreadable"); continue; }
                if (!object) continue;
                if (!r.ReadAt(object, 0xD0, kind)) { s.truncated = true; Error(s, "map_kind_unreadable"); continue; }
                if (kind != 529) continue;
                if (!r.Getter(object, 0x34, 0x4073D0) || !r.Getter(object, 0x330, 0x652A20))
                { s.truncated = true; Error(s, "unsupported_enemy_layout"); continue; }
                Enemy enemy; std::uint32_t uid = 0, value = 0; std::int32_t signedValue = 0;
                if (!r.SecureAt(object, 0xD4, uid) || uid > 65535)
                { s.truncated = true; Error(s, "invalid_enemy_uid"); continue; }
                bool duplicate = false;
                for (std::size_t n = 0; n < s.enemyCount; ++n) duplicate = duplicate || s.enemies[n].uid.value == uid;
                if (duplicate) { s.truncated = true; Error(s, "duplicate_enemy_uid"); continue; }
                enemy.uid = {true, uid};
                if (r.ReadAt(object, 0x1A0, value)) enemy.monsterId = {true, value};
                if (r.SignedAt(object, 0x1168, signedValue) && signedValue >= 0) enemy.hp = {true, signedValue};
                if (r.SignedAt(object, 0x142C, signedValue)) enemy.dead = {true, signedValue != 0};
                unsigned char active = 0;
                if (r.ReadAt(object, 0x128, active)) enemy.active = {true, active != 0};
                if (r.SignedAt(object, 0x1434, signedValue)) enemy.action = {true, signedValue};
                if (r.ReadAt(object, 0x42C, value)) enemy.queuedAction = {true, value};
                if (r.ReadAt(object, 0x1864, value)) enemy.authorityToken = {true, value};
                enemy.position = r.Coordinates(object);
                s.enemies[s.enemyCount++] = enemy;
            }
        }
        void Skills(Reads& r, Snapshot& s) noexcept
        {
            const auto actor = s.me.actorToken;
            if (!r.Getter(actor, 0x508, 0x493DC0) || !r.Getter(actor, 0x51C, 0x4A3510))
            { Error(s, "unsupported_skill_layout"); return; }
            s.skillLevelsSupported = true;
            s.skillCooldownSupported = s.skillUsableSupported = true;
            const auto clock = client_skill_state::Clock(r);
            client_skill_resources::InventoryCache resourceInventory{};
            for (std::uint32_t id = 0; id < MaximumSkills; ++id)
            {
                std::uint32_t skill = 0;
                if (!r.ReadAt(actor, 0x1AEC + id * 4, skill)) { s.truncated = true; Error(s, "skill_slot_unreadable"); continue; }
                if (!skill) continue;
                std::int32_t level = 0;
                if (!r.SignedAt(skill, 0x2E0, level) || level < 0 || level > 1000)
                { s.truncated = true; Error(s, "invalid_skill_level"); continue; }
                if (level > 0)
                {
                    Skill result{id,level};std::uint32_t table=0,owner=0,again=0,actualId=0;std::int32_t finalLevel=0;
                    const bool bound=r.Read(skill,table)&&table==0x99EE78&&r.ReadAt(skill,4,owner)&&owner==actor
                        &&r.SecureAt(skill,8,actualId)&&actualId==id;
                    if(bound)
                    {
                        client_skill_state::Cooldown(r,skill,clock,result);
                        client_skill_state::Resources(r,resourceInventory,s.me,skill,result);
                        client_skill_state::Usability(r,s.me,skill,result);
                    }
                    if(!r.ReadAt(actor,0x1AEC+id*4,again)||again!=skill||!r.SignedAt(skill,0x2E0,finalLevel)||finalLevel!=level
                        ||(bound&&(!r.ReadAt(skill,4,again)||again!=actor||!r.SecureAt(skill,8,again)||again!=id
                            ||!r.Read(skill,again)||again!=table)))
                    {s.truncated=true;Error(s,"skill_changed_during_capture");return;}
                    s.skills[s.skillCount++]=result;
                }
            }
        }
    }

    bool Capture(const Reader& reader, History& history, Snapshot& output) noexcept
    {
        output = {}; output.pid = reader.pid; output.processStartedFileTime = reader.processStartedFileTime;
        if (!reader.fileTime || !reader.tickMs || !reader.counterUs) return false;
        output.capturedFileTime = reader.fileTime(reader.context);
        output.capturedTickMs = reader.tickMs(reader.context);
        const auto began = reader.counterUs(reader.context);
        output.sequence = ++history.sequence;
        output.layoutSupported = reader.supported && reader.supported(reader.context);
        auto finish = [&]() noexcept
        {
            const auto ended = reader.counterUs(reader.context);
            output.captureDurationUs = ended >= began ? ended - began : 0;
            const auto scene = output.scene.known ? output.scene.value : 0;
            const auto uid = output.haveMe ? output.me.uid.value : 0;
            const auto map = output.town.present ? output.town.mapToken : output.room.mapToken;
            const auto context = output.town.present ? output.town.contextToken : 0;
            const auto town = output.town.present ? output.town.townId : -1;
            const auto area = output.town.present ? output.town.areaId : -1;
            if (!history.haveIdentity || history.scene != scene || history.stage != output.room.stageToken
                || history.map != map || history.uid != uid || history.actor != output.me.actorToken
                || history.context != context || history.town != town || history.area != area) ++history.generation;
            history.haveIdentity = true; history.scene = scene; history.stage = output.room.stageToken;
            history.map = map; history.uid = uid; history.actor = output.me.actorToken;
            history.context = context; history.town = town; history.area = area; output.generation = history.generation;
            return output.valid;
        };
        if (!output.layoutSupported || !reader.read || !reader.pid || !reader.processStartedFileTime)
        { Error(output, "unsupported_layout"); return finish(); }
        Reads r{reader, output}; std::uint32_t game = 0, ui = 0, scene = 0;
        if (!r.Read(0xCFD458, r.manager) || !r.manager || !r.Read(0xCFD45C, r.key)
            || !r.Read(0xB5D6F0, game) || !game || !r.SecureAt(game, 0x10, scene))
        { Error(output, "scene_unreadable"); return finish(); }
        output.scene = {true, scene};
        UiState(r, output, game);
        if (scene == 1) { output.valid = TownState(r, output, game); return finish(); }
        if (scene == 2)
        {
            std::uint32_t final = 0;
            output.valid = ui_detail::StableSelection(r, output.ui, game)
                && r.Read(0xB5D6F0, final) && final == game
                && r.SecureAt(game, 0x10, final) && final == scene
                && r.ReadAt(game, 0x20, final) && final == output.ui.selector.instanceToken
                && r.Read(final, final) && final == 0x99A5B8
                && r.Read(0xCFD458, final) && final == r.manager
                && r.Read(0xCFD45C, final) && final == r.key;
            if (!output.valid) Error(output, "identity_changed_during_capture");
            return finish();
        }
        if (scene != 3) { output.valid = true; return finish(); }
        std::uint32_t stage = 0, map = 0, actor = 0, uid = 0;
        if (!r.Read(0xB5DCE0, ui) || !ui || !r.ReadAt(ui, 0x200010, stage) || !stage
            || !r.ReadAt(stage, 0xB0, map) || !map || !r.Read(0xB75A14, actor) || !actor
            || !r.SecureAt(actor, 0xD4, uid) || !uid || uid > 65535)
        { Error(output, "dungeon_identity_unreadable"); return finish(); }
        output.room.present = true; output.room.stageToken = stage; output.room.mapToken = map;
        const auto user = FindUser(r, actor, uid);
        output.me = r.Actor(user, actor, map, true); output.haveMe = output.me.uid.known;
        if(output.haveMe) ui_detail::Commands(r,output.ui,output.me,true,3);
        if (!output.haveMe) return finish();
        Players(r, output); Enemies(r, output); Skills(r, output); Doors(r, output);
        std::uint32_t finalScene = 0, finalStage = 0, finalMap = 0, finalActor = 0, finalBinding = 0;
        std::uint32_t finalActorMap = 0, finalUid = 0, manager = 0, key = 0;
        output.valid = r.SecureAt(game, 0x10, finalScene) && finalScene == scene
            && r.ReadAt(ui, 0x200010, finalStage) && finalStage == stage
            && r.ReadAt(stage, 0xB0, finalMap) && finalMap == map
            && r.Read(0xB75A14, finalActor) && finalActor == actor
            && r.ReadAt(user, 0x110, finalBinding) && finalBinding == actor
            && r.ReadAt(actor, 0xE4, finalActorMap) && finalActorMap == output.me.mapToken
            && r.SecureAt(actor, 0xD4, finalUid) && finalUid == uid
            && r.Read(0xCFD458, manager) && manager == r.manager
            && r.Read(0xCFD45C, key) && key == r.key;
        if (!output.valid) Error(output, "identity_changed_during_capture");
        return finish();
    }
    bool TryPublish(Publication& publication, const Snapshot& snapshot) noexcept
    {
        if (!TryAcquireSRWLockExclusive(&publication.lock)) return false;
        publication.latest = snapshot; publication.have = true;
        ReleaseSRWLockExclusive(&publication.lock); return true;
    }
    bool TryCopy(Publication& publication, Snapshot& snapshot) noexcept
    {
        if (!TryAcquireSRWLockShared(&publication.lock)) return false;
        const auto have = publication.have;
        if (have) snapshot = publication.latest;
        ReleaseSRWLockShared(&publication.lock); return have;
    }

    namespace
    {
        Publication Published;
        History CaptureHistory;
        bool Enabled = false, GateChecked = false, HaveCapture = false;
        volatile LONG LayoutSupported = 0;
        std::uint64_t LastCapture = 0, LastExported = 0, Skipped = 0, Started = 0;
        wchar_t Directory[MAX_PATH]{};
        bool ReadNative(void*, std::uint32_t address, void* output, std::size_t bytes) noexcept
        {
            if (address < 0x10000 || bytes > 4096 || address > UINT32_MAX - bytes) return false;
            __try { std::memcpy(output, reinterpret_cast<const void*>(address), bytes); return true; }
            __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
        }
        std::uint64_t FileTime(void*) noexcept
        {
            FILETIME value{}; GetSystemTimeAsFileTime(&value);
            return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
        }
        std::uint64_t Tick(void*) noexcept { return GetTickCount64(); }
        std::uint64_t Counter(void*) noexcept
        {
            LARGE_INTEGER value{}, frequency{};
            if (!QueryPerformanceCounter(&value) || !QueryPerformanceFrequency(&frequency) || frequency.QuadPart <= 0) return 0;
            return static_cast<std::uint64_t>(value.QuadPart / frequency.QuadPart) * 1000000
                + static_cast<std::uint64_t>(value.QuadPart % frequency.QuadPart) * 1000000 / static_cast<std::uint64_t>(frequency.QuadPart);
        }
        bool Supported(void*) noexcept { return InterlockedCompareExchange(&LayoutSupported, 0, 0) != 0; }
        bool OriginalImage() noexcept
        {
            // Worker-only disk IO. No client address is probed until this gate succeeds.
            if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != 0x400000) return false;
            wchar_t file[MAX_PATH]{};
            const DWORD pathLength = GetModuleFileNameW(nullptr, file, MAX_PATH);
            if (pathLength == 0 || pathLength >= MAX_PATH) return false;
            const HANDLE input = CreateFileW(file, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (input == INVALID_HANDLE_VALUE) return false;
            BCRYPT_ALG_HANDLE algorithm = nullptr; BCRYPT_HASH_HANDLE hash = nullptr;
            bool okay = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0;
            DWORD length = 0, copied = 0;
            if (okay) okay = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&length), sizeof(length), &copied, 0) >= 0;
            // Fixed worker buffers also make allocation failure fail closed under noexcept.
            std::array<unsigned char, 1024> storage{};
            std::array<unsigned char, 65536> buffer{};
            okay = okay && length > 0 && length <= storage.size();
            if (okay) okay = BCryptCreateHash(algorithm, &hash, storage.data(), length, nullptr, 0, 0) >= 0;
            DWORD count = 0; std::uint64_t total = 0;
            while (okay)
            {
                if (!ReadFile(input, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr)) { okay = false; break; }
                if (!count) break;
                total += count; okay = BCryptHashData(hash, buffer.data(), count, 0) >= 0;
            }
            unsigned char digest[32]{};
            if (okay) okay = BCryptFinishHash(hash, digest, sizeof(digest), 0) >= 0 && total == 9846272;
            constexpr unsigned char Expected[] = {0x6B,0x48,0x50,0xBE,0xAF,0x67,0xEC,0x61,0xAD,0xB0,0x4A,0xE1,0x7F,0x11,0x9F,0x16,
                0x27,0x8D,0xD0,0x5E,0x97,0xB8,0xFF,0xCA,0x87,0x1A,0x9B,0xDB,0x54,0x7D,0xE0,0xD5};
            okay = okay && std::memcmp(digest, Expected, sizeof(Expected)) == 0;
            if (hash) BCryptDestroyHash(hash);
            if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
            CloseHandle(input);
            if (!okay) return false;
            unsigned char level[16]{};
            constexpr unsigned char Level[] = {0x55,0x8B,0xEC,0x8B,0x45,0x08,0x8B,0x94,0x81,0xEC,0x1A,0,0,0x85,0xD2,0x74};
            return ReadNative(nullptr, 0x493DC0, level, sizeof(level)) && std::memcmp(level, Level, sizeof(level)) == 0;
        }
    }
    void Initialize(const wchar_t* settingsPath, const wchar_t* directory) noexcept
    {
        Enabled = settingsPath && directory && GetPrivateProfileIntW(L"DebugOverlay", L"ExportState", 0, settingsPath) == 1;
        if (!Enabled || wcsnlen_s(directory, MAX_PATH) >= MAX_PATH - 64) { Enabled = false; return; }
        wcscpy_s(Directory, directory); CreateDirectoryW(Directory, nullptr);
        FILETIME creation{}, exit{}, kernel{}, user{};
        if (!GetProcessTimes(GetCurrentProcess(), &creation, &exit, &kernel, &user)) { Enabled = false; return; }
        Started = (static_cast<std::uint64_t>(creation.dwHighDateTime) << 32) | creation.dwLowDateTime;
        GateChecked = false; HaveCapture = false; LastCapture = LastExported = Skipped = 0;
        InterlockedExchange(&LayoutSupported, 0);
        AcquireSRWLockExclusive(&Published.lock); Published.have = false; ReleaseSRWLockExclusive(&Published.lock);
    }
    void CaptureFrame() noexcept
    {
        if (!Enabled) return;
        const auto now = GetTickCount64();
        if (HaveCapture && now - LastCapture < 200) return;
        HaveCapture = true; LastCapture = now;
        const auto reader = NativeReader();
        Snapshot snapshot; Capture(reader, CaptureHistory, snapshot); snapshot.skippedPublications = Skipped;
        if (!TryPublish(Published, snapshot)) ++Skipped;
    }
    Reader NativeReader() noexcept
    {
        return Reader{nullptr, &ReadNative, &Supported, &FileTime, &Tick, &Counter, GetCurrentProcessId(), Started};
    }
    bool CopyLatest(Snapshot& snapshot) noexcept { return TryCopy(Published, snapshot); }
    void ExportLatest() noexcept
    {
        if (!Enabled) return;
        try
        {
            if (!GateChecked)
            {
                GateChecked = true;
                InterlockedExchange(&LayoutSupported, OriginalImage() ? 1 : 0);
            }
            Snapshot snapshot;
            if (!TryCopy(Published, snapshot) || snapshot.sequence == LastExported) return;
            const auto json = ToJson(snapshot, FileTime(nullptr));
            wchar_t file[MAX_PATH]{}, temporary[MAX_PATH]{};
            if (swprintf_s(file, L"%s\\dfdebug-client-%lu.json", Directory, GetCurrentProcessId()) < 0
                || swprintf_s(temporary, L"%s.tmp", file) < 0) return;
            const HANDLE output = CreateFileW(temporary, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (output == INVALID_HANDLE_VALUE) return;
            DWORD written = 0;
            const bool saved = json.size() <= MAXDWORD && WriteFile(output, json.data(), static_cast<DWORD>(json.size()), &written, nullptr)
                && written == json.size();
            CloseHandle(output);
            // Rebuildable observation: complete temporary write + atomic
            // replacement is sufficient; this is not an economic commit.
            if (saved && MoveFileExW(temporary, file, MOVEFILE_REPLACE_EXISTING)) LastExported = snapshot.sequence;
            else DeleteFileW(temporary);
        }
        catch (...) { /* Export failure must never escape into the existing worker. */ }
    }
    void Shutdown() noexcept { Enabled = false; }
}
