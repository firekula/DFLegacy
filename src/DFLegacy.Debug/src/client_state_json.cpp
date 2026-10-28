#include "client_state.h"
#include <cmath>
#include <cstdio>
#include <iomanip>
#include <locale>
#include <sstream>

namespace dfl::client_state
{
    namespace
    {
        template<class T> void Nullable(std::ostream& out, const Value<T>& value)
        {
            if (value.known) out << value.value; else out << "null";
        }
        void Token(std::ostream& out, std::uint32_t token)
        {
            if (!token) { out << "null"; return; }
            char text[16]{}; std::snprintf(text, sizeof(text), "0x%08X", token); out << '"' << text << '"';
        }
        void Point(std::ostream& out, const Position& position)
        {
            if (!position.known || !std::isfinite(position.x) || !std::isfinite(position.y) || !std::isfinite(position.z)) out << "null";
            else out << "{\"x\":" << position.x << ",\"y\":" << position.y << ",\"z\":" << position.z << '}';
        }
        void Person(std::ostream& out, const Player& p)
        {
            out << "{\"uid\":"; Nullable(out, p.uid); out << ",\"user_token\":"; Token(out, p.userToken);
            out << ",\"actor_token\":"; Token(out, p.actorToken); out << ",\"map_token\":"; Token(out, p.mapToken);
            out << ",\"hp\":"; Nullable(out, p.hp); out << ",\"max_hp\":"; Nullable(out, p.maxHp);
            out << ",\"mp\":null,\"max_mp\":null";
            out << ",\"dead\":"; Nullable(out, p.dead); out << ",\"same_map\":"; Nullable(out, p.sameMap);
            out << ",\"detached\":"; Nullable(out, p.detached); out << ",\"position\":"; Point(out, p.position); out << '}';
        }
    }
    std::string ToJson(const Snapshot& s, std::uint64_t exportedFileTime)
    {
        std::ostringstream out; out.imbue(std::locale::classic()); out << std::boolalpha << std::setprecision(9);
        out << "{\"schema\":\"dfl-client-state/v1\",\"pid\":" << s.pid
            << ",\"process_started_filetime\":" << s.processStartedFileTime << ",\"sequence\":" << s.sequence
            << ",\"captured_filetime\":" << s.capturedFileTime << ",\"captured_tick_ms\":" << s.capturedTickMs
            << ",\"exported_filetime\":" << exportedFileTime << ",\"layout_supported\":" << s.layoutSupported
            << ",\"valid\":" << s.valid << ",\"scene\":"; Nullable(out, s.scene);
        out << ",\"generation\":" << s.generation << ",\"capture_duration_us\":" << s.captureDurationUs
            << ",\"skipped_publications\":" << s.skippedPublications << ",\"truncated\":" << s.truncated << ",\"errors\":[";
        for (std::size_t i = 0; i < s.errorCount; ++i) { if (i) out << ','; out << '"' << s.errors[i].data() << '"'; }
        out << "],\"capabilities\":{\"skill_levels\":" << s.skillLevelsSupported << ",\"skill_cooldown\":false,\"skill_usable\":false},\"server_context\":null,\"room\":";
        if (!s.room.present) out << "null";
        else
        {
            out << "{\"stage_token\":"; Token(out, s.room.stageToken); out << ",\"map_token\":"; Token(out, s.room.mapToken);
            out << ",\"boss_dead\":"; Nullable(out, s.room.bossDead); out << ",\"assembly_phase\":"; Nullable(out, s.room.assemblyPhase);
            out << ",\"map_clear_state\":"; Nullable(out, s.room.mapClearState);
            out << ",\"direction_mask\":"; Nullable(out, s.room.directionMask); out << ",\"living_count\":"; Nullable(out, s.room.livingCount);
            out << ",\"majority_required\":"; Nullable(out, s.room.majorityRequired); out << ",\"doors\":{";
            constexpr const char* Names[] = {"left", "right", "up", "down"};
            for (std::size_t i = 0; i < s.room.doors.size(); ++i)
            {
                if (i) out << ',';
                const auto& d = s.room.doors[i]; out << '"' << Names[i] << "\":{\"enabled\":"; Nullable(out, d.enabled);
                out << ",\"rect\":";
                if (!d.rect.known) out << "null";
                else out << "{\"left\":" << d.rect.left << ",\"top\":" << d.rect.top << ",\"width\":" << d.rect.width << ",\"height\":" << d.rect.height << '}';
                out << '}';
            }
            out << "}}";
        }
        out << ",\"me\":"; if (s.haveMe) Person(out, s.me); else out << "null";
        out << ",\"players\":[";
        for (std::size_t i = 0; i < s.playerCount; ++i) { if (i) out << ','; Person(out, s.players[i]); }
        out << "],\"enemies\":[";
        for (std::size_t i = 0; i < s.enemyCount; ++i)
        {
            if (i) out << ',';
            const auto& e = s.enemies[i]; out << "{\"uid\":"; Nullable(out, e.uid);
            out << ",\"monster_id\":"; Nullable(out, e.monsterId); out << ",\"hp\":"; Nullable(out, e.hp);
            out << ",\"dead\":"; Nullable(out, e.dead); out << ",\"active\":"; Nullable(out, e.active);
            out << ",\"position\":"; Point(out, e.position); out << ",\"action\":"; Nullable(out, e.action);
            out << ",\"queued_action\":"; Nullable(out, e.queuedAction); out << ",\"authority_token\":"; Nullable(out, e.authorityToken); out << '}';
        }
        out << "],\"skills\":[";
        for (std::size_t i = 0; i < s.skillCount; ++i)
        {
            if (i) out << ',';
            out << "{\"id\":" << s.skills[i].id << ",\"level\":" << s.skills[i].level << ",\"cooldown_ms\":null,\"usable\":null}";
        }
        out << "]}\n"; return out.str();
    }
}
