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
        Value<std::int32_t> hp{}, maxHp{}, mp{}, maxMp{};
        std::uint32_t mpActorToken = 0;
        bool mpLocal = false; // Peer values are the receiving client's native observation.
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
    struct Skill
    {
        std::uint32_t id = 0; std::int32_t level = 0;
        Value<std::int32_t> cooldownMs{};Value<bool> cooldownBlocked{},usable{};
        struct Resources
        {
            bool present=false;
            Value<std::int32_t> mpCost{},useLimit{},useCount{};
            Value<bool> itemsSufficient{};
        } resources{};
    };
    struct Room
    {
        bool present = false;
        std::uint32_t stageToken = 0, mapToken = 0;
        Value<bool> bossDead{};
        Value<std::int32_t> mapClearState{}; // Native protected raw value; never an inferred door-open flag.
        Value<std::uint32_t> assemblyPhase{}, directionMask{}, livingCount{}, majorityRequired{};
        std::array<Door, 4> doors{}; // left, right, up, down
    };
    struct Town
    {
        bool present = false;
        std::int32_t townId = -1, areaId = -1;
        std::uint32_t contextToken = 0, mapToken = 0;
        Position position{};
        Value<bool> autoMoveActive{};
        Value<std::int32_t> destinationX{}, destinationY{}, directionX{}, directionY{};
    };
    inline constexpr std::array<std::uint32_t,8> DialogClassIds={7,9,0x23,0x25,0x30,0x56,0x59,0x6A};
    struct Dialog
    {
        std::uint32_t classId = 0, instanceToken = 0, rootToken = 0;
        bool shownRaw = false, inputEnabledRaw = false;
        struct Placement
        {
            bool present=false,attached=false,layerShownRaw=false,layerInputEnabledRaw=false;
            std::uint32_t layerToken=0,order=0;
        } placement{};
    };
    struct Dialogs
    {
        bool valid = false, truncated = false;
        std::array<Dialog, 64> items{};
        std::size_t count = 0;
    };
    struct Selector
    {
        std::uint32_t instanceToken = 0, stageToken = 0;
        std::int32_t selectedIndex = -1;
        Value<bool> transitionPending{};
        Value<std::uint32_t> phase{}, recordCount{};
        Value<std::int32_t> inputSuppression{}, dungeonId{}, difficulty{}, maximumDifficulty{};
    };
    struct Ui
    {
        struct CommandInput
        {
            bool present=false;
            std::uint32_t actorToken=0,checkerToken=0,uid=0;
            std::int32_t keyboardLockLevel=0;
            Value<std::int32_t> mouseLockLevel{};
            Value<bool> mouseFlag90{};
        } commandInput{};
        Value<bool> mouseButtonsDeferred{};
        Value<std::int32_t> selectionMode{}, selectorSelected{}, selectorHover{}, countdownState{};
        Value<std::uint32_t> selectorActive{}, menuTargetUid{};
        Value<bool> menuVisible{}, minimapVisible{}, minimapEnabled{};
        bool menuActionsKnown = false;
        std::array<std::uint32_t, 32> menuActions{};
        std::size_t menuActionCount = 0;
        Selector selector{};
        Dialogs dialogs{};
    };
    struct Snapshot
    {
        std::uint32_t pid = 0;
        std::uint64_t processStartedFileTime = 0, sequence = 0;
        std::uint64_t capturedFileTime = 0, capturedTickMs = 0, generation = 0;
        std::uint64_t captureDurationUs = 0, skippedPublications = 0;
        bool layoutSupported = false, valid = false, truncated = false, haveMe = false;
        bool skillLevelsSupported = false;
        bool skillCooldownSupported = false, skillUsableSupported = false;
        Value<std::uint32_t> scene{};
        Room room{};
        Town town{};
        Ui ui{};
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
        std::uint32_t scene = 0, stage = 0, map = 0, uid = 0, actor = 0, context = 0;
        std::int32_t town = -1, area = -1;
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
    // Read-only callbacks reuse the worker's original-image gate. Capture itself
    // belongs on the game thread; copying a published snapshot never blocks it.
    Reader NativeReader() noexcept;
    bool CopyLatest(Snapshot&) noexcept;
    void Shutdown() noexcept;
}
