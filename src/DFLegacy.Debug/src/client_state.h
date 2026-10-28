#pragma once
#include <windows.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <string>

namespace dfl::client_state
{
    constexpr std::size_t MaximumPlayers = 4, MaximumEnemies = 256;
    constexpr std::size_t MaximumSkills = 216, MaximumErrors = 16;
    template<class T> struct Value { bool known = false; T value{}; };
    struct Position { bool known = false; float x = 0, y = 0, z = 0; };
    struct Rectangle { bool known = false; std::int32_t left = 0, top = 0, width = 0, height = 0; };
    struct Door { Value<bool> enabled{}; Rectangle rect{}; };
    struct Player
    {
        Value<std::uint32_t> uid{};
        std::uint32_t userToken = 0, actorToken = 0, mapToken = 0;
        Value<std::int32_t> hp{}, maxHp{};
        Value<bool> dead{}, sameMap{}, detached{};
        Position position{};
    };
    struct Enemy
    {
        Value<std::uint32_t> uid{}, monsterId{}, queuedAction{}, authorityToken{};
        Value<std::int32_t> hp{}, action{};
        Value<bool> dead{}, active{};
        Position position{};
    };
    struct Skill { std::uint32_t id = 0; std::int32_t level = 0; };
    struct Room
    {
        bool present = false;
        std::uint32_t stageToken = 0, mapToken = 0;
        Value<bool> bossDead{};
        Value<std::int32_t> mapClearState{}; // Native protected raw value; never an inferred door-open flag.
        Value<std::uint32_t> assemblyPhase{}, directionMask{}, livingCount{}, majorityRequired{};
        std::array<Door, 4> doors{}; // left, right, up, down
    };
    struct Snapshot
    {
        std::uint32_t pid = 0;
        std::uint64_t processStartedFileTime = 0, sequence = 0;
        std::uint64_t capturedFileTime = 0, capturedTickMs = 0, generation = 0;
        std::uint64_t captureDurationUs = 0, skippedPublications = 0;
        bool layoutSupported = false, valid = false, truncated = false, haveMe = false;
        bool skillLevelsSupported = false;
        Value<std::uint32_t> scene{};
        Room room{};
        Player me{};
        std::array<Player, MaximumPlayers> players{};
        std::array<Enemy, MaximumEnemies> enemies{};
        std::array<Skill, MaximumSkills> skills{};
        std::size_t playerCount = 0, enemyCount = 0, skillCount = 0, errorCount = 0;
        std::array<std::array<char, 64>, MaximumErrors> errors{};
    };
    struct Reader
    {
        void* context = nullptr;
        bool (*read)(void*, std::uint32_t, void*, std::size_t) noexcept = nullptr;
        bool (*supported)(void*) noexcept = nullptr;
        std::uint64_t (*fileTime)(void*) noexcept = nullptr;
        std::uint64_t (*tickMs)(void*) noexcept = nullptr;
        std::uint64_t (*counterUs)(void*) noexcept = nullptr;
        std::uint32_t pid = 0;
        std::uint64_t processStartedFileTime = 0;
    };
    struct History
    {
        std::uint64_t sequence = 0, generation = 0;
        std::uint32_t scene = 0, stage = 0, map = 0, uid = 0;
        bool haveIdentity = false;
    };
    // Fixed-size mailbox; IO and serialization must happen after releasing lock.
    struct Publication
    {
        SRWLOCK lock = SRWLOCK_INIT;
        Snapshot latest{};
        bool have = false;
    };
    bool Capture(const Reader&, History&, Snapshot&) noexcept;
    bool TryPublish(Publication&, const Snapshot&) noexcept;
    bool TryCopy(Publication&, Snapshot&) noexcept;
    std::string ToJson(const Snapshot&, std::uint64_t exportedFileTime);
    void Initialize(const wchar_t* settingsPath, const wchar_t* directory) noexcept;
    void CaptureFrame() noexcept;
    void ExportLatest() noexcept;
    void Shutdown() noexcept;
}
