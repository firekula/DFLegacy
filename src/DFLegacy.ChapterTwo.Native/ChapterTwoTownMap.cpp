#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "ChapterTwoTownMap.h"
#include "map_click_feedback.h"
#include "town_route_controller.h"
#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
#include "ChapterTwoDebugControl.h"
#include "ChapterTwoDebugTrace.h"
#endif
#include <atomic>
#include <cwchar>
#include <memory>
#include <new>
#include <process.h>
#include <cstdint>
#include <cstdio>
#include <cstring>

#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
#define TOWNMAP_TRACE(...) dfl::chapter_two::debug_trace::Write(__VA_ARGS__)
#else
#define TOWNMAP_TRACE(...) ((void)0)
#endif

namespace
{
    using Address = std::uintptr_t;
    constexpr Address ImageBase = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr Address CaptureCall = 0xa3cc1f, Capture = 0xa3c8b0;
    constexpr Address MapPollCall = 0x868bb1, MapPoll = 0x963920;
    constexpr Address MapDrawCall = 0x963ece, MapDraw = 0x963a90;
    constexpr Address InputManager = 0xd56d24, Game = 0xd564ac, PlayerManager = 0xd56cf4, LocalActor = 0xd7a8c0;
    constexpr Address RightEvent = 0xf2e800, RightDown = 0xf2e80a, RightCapture = 0xd4dfc0, SelectionMode = 0xf2e816;
    constexpr Address MouseX = 0xf2e7f4, MouseY = 0xf2e7f8, ViewX = 0xdfaefc, ViewY = 0xd42a20;
    constexpr Address WorldRegions = 0xdfaf5c, MapVtable = 0xc11890, TownActorVtable = 0xaea99c;
    constexpr Address ValidatePoint = 0x492290, SetDestination = 0x490e60, AutoMove = 0xd586b0;
    constexpr Address QuestVtable = 0xc194d4, QuestHit = 0x965a00;
    constexpr Address Graphics = 0xe01fc8, GraphicsVtable = 0xc302c4, Renderer = 0xe01fd4;
    constexpr Address RingSpriteVtable = 0xc30824, ConstructRing = 0xa25860, DestroyRing = 0xa258a0;
    constexpr Address CreateRing = 0xa258f0, LockRing = 0xa13160, UnlockRing = 0xa131b0, DrawSprite = 0xa3eb30;
    constexpr Address SetRenderState = 0xa25a90, GetRenderState = 0xa25ac0;
    constexpr std::uint32_t RightRelease = 0x10, MaximumRegions = 256, MaximumMarkers = 256;

    struct Point { std::int32_t x, y; };
    struct Rect { std::int32_t x, y, width, height; };
    struct Region { std::int32_t town, area; Rect bounds; };
    struct Player { Address actor, user, context, map, area, definition; std::int32_t townId, areaId; };

    namespace route = dfl::town_route;
    namespace feedback = dfl::map_click_feedback;
    route::Controller Controller;
    feedback::Feedback ClickFeedback;
    Address ClickArea = 0;
    bool FeedbackEnabled = false;

    // One private native sprite on the game thread. Its D3D8 texture is managed
    // and survives a device reset. A replacement device gets its own texture.
    alignas(Address) unsigned char RingSprite[0xa0]{};
    Address RingDevice = 0, RingBatch = 0;
    bool RingConstructed = false, RingReady = false;
    std::atomic<const route::Catalog*> PublishedCatalog{nullptr};
    wchar_t CatalogPath[MAX_PATH]{};
    std::uint64_t ProcessStarted = 0, Sequence = 0, Generation = 0;
    Address LastMap = 0, LastArea = 0, LastDefinition = 0;
    DWORD GameThread = 0;
    bool RouteDisabled = false;

    unsigned __stdcall LoadRouteCatalog(void*) noexcept
    {
        // The mapped game and this hook live until process exit. Avoid CRT
        // static teardown racing a catalog load when the client closes early.
        auto catalog = std::unique_ptr<route::Catalog>(new (std::nothrow) route::Catalog());
        std::string error;
        if (catalog && route::LoadChapterTwoCatalog(CatalogPath, *catalog, error))
            PublishedCatalog.store(catalog.release(), std::memory_order_release);
        return 0;
    }

    void StartCatalogWorker() noexcept
    {
        FILETIME started{}, exited{}, kernel{}, user{};
        if (!GetProcessTimes(GetCurrentProcess(), &started, &exited, &kernel, &user)) return;
        ProcessStarted = (static_cast<std::uint64_t>(started.dwHighDateTime) << 32) | started.dwLowDateTime;
        const auto length = GetModuleFileNameW(nullptr, CatalogPath, MAX_PATH);
        if (!length || length >= MAX_PATH) return;
        auto* slash = std::wcsrchr(CatalogPath, L'\\');
        if (!slash || wcscpy_s(slash + 1, static_cast<std::size_t>(CatalogPath + MAX_PATH - slash - 1), L"Script.pvf") != 0) return;
        const auto thread = _beginthreadex(nullptr, 0, &LoadRouteCatalog, nullptr, 0, nullptr);
        if (thread) CloseHandle(reinterpret_cast<HANDLE>(thread));
    }

    template<class T>
    T Read(Address address) noexcept
    {
        T value{};
        std::memcpy(&value, reinterpret_cast<const void*>(address), sizeof(value));
        return value;
    }

    bool Coordinate(std::int32_t n) noexcept { return n >= -1000000 && n <= 1000000; }

    bool ValidRect(const Rect& rect) noexcept
    {
        return Coordinate(rect.x) && Coordinate(rect.y) && rect.width > 0 && rect.width <= 1000000 &&
            rect.height > 0 && rect.height <= 1000000;
    }

    bool Contains(const Rect& rect, Point point) noexcept
    {
        return point.x >= rect.x && point.x < rect.x + rect.width &&
            point.y >= rect.y && point.y < rect.y + rect.height;
    }

    template<class Matches>
    bool FindRegion(Address table, Matches matches, Region& output) noexcept
    {
        // The native table uses a sentinel-linked list of {next, prev, key, record}.
        const auto sentinel = Read<Address>(table + 8);
        const auto count = Read<std::uint32_t>(table + 12);
        if (!sentinel || !count || count > MaximumRegions) return false;
        auto node = Read<Address>(sentinel);
        auto previous = sentinel;
        bool found = false;
        for (std::uint32_t i = 0; i < count; ++i)
        {
            if (!node || node == sentinel || Read<Address>(node + 4) != previous) return false;
            const auto key = Read<std::int32_t>(node + 8);
            const auto record = Read<Address>(node + 12);
            if (!record) return false;
            const auto town = Read<std::int32_t>(record);
            const auto area = Read<std::int32_t>(record + 4);
            const auto rectangle = Read<Address>(record + 8);
            if (town < 0 || town > 255 || area < 0 || area > 255 || key != (town << 10) + area || !rectangle) return false;
            const Region region{town, area, Read<Rect>(rectangle)};
            if (!ValidRect(region.bounds)) return false;
            if (matches(region))
            {
                if (found) return false;
                output = region;
                found = true;
            }
            previous = node;
            node = Read<Address>(node);
        }
        return found && node == sentinel && Read<Address>(sentinel + 4) == previous;
    }

    bool CanClickThroughMarkers(Address plane, Point point) noexcept
    {
        const auto begin = Read<Address>(plane + 0x14), end = Read<Address>(plane + 0x18);
        if (end < begin || (end - begin) % sizeof(Address) ||
            (end - begin) / sizeof(Address) > MaximumMarkers) return false;
        for (auto slot = begin; slot != end; slot += sizeof(Address))
        {
            const auto marker = Read<Address>(slot);
            if (!marker) continue;
            const auto vtable = Read<Address>(marker);
            if (!vtable) return false;
            const auto method = Read<Address>(vtable + 4);
            // A quest badge is an overlay on the ground. Preserve the native hit
            // policy for NPC, object, dungeon and player markers even under a badge.
            if (vtable == QuestVtable && method == QuestHit) continue;
            if (method != 0x965600 && method != 0x965700 && method != 0x965770 && method != 0x965840) return false;
            using Hit = bool (__thiscall*)(void*, std::int32_t, std::int32_t);
            if (reinterpret_cast<Hit>(method)(reinterpret_cast<void*>(marker), point.x, point.y)) return false;
        }
        return true;
    }

    bool InTown(Address game) noexcept
    {
        if (!game) return false;
        // Decode the same duplicated scene fields read by the original map poll.
        const auto address = game + 0x10;
        const auto manager = Read<Address>(0xf35af0), key = Read<std::uint32_t>(0xf35af4);
        const auto index = Read<std::uint32_t>(address + 4);
        if (!manager || index > 0x1000000 || (Read<std::uint32_t>(address) ^ key) - address != index) return false;
        const auto first = Read<Address>(manager + 0x44), second = Read<Address>(manager + 0x48);
        if (!first || !second || first > UINT32_MAX - index * 4 || second > UINT32_MAX - index * 4) return false;
        const auto a = first + index * 4, b = second + index * 4;
        return (Read<std::uint32_t>(a) ^ a ^ key) == 1 && (Read<std::uint32_t>(b) ^ b ^ key) == 1;
    }

    bool ReadPlayer(Player& player) noexcept
    {
        const auto game = Read<Address>(Game), manager = Read<Address>(PlayerManager);
        if (!manager || !InTown(game)) return false;
        player.context = Read<Address>(game + 0x1c);
        player.user = Read<Address>(manager + 0x2c);
        player.actor = Read<Address>(LocalActor);
        if (!player.context || !player.user || !player.actor || Read<Address>(player.user + 0x164) != player.actor ||
            Read<std::int32_t>(player.user + 0x30) == 5 || Read<Address>(player.actor) != TownActorVtable) return false;
        const auto areaId = Read<Address>(player.context + 0x90);
        if (!areaId) return false;
        player.townId = Read<std::int32_t>(player.context + 0x8c);
        player.areaId = Read<std::int32_t>(areaId);
        player.map = Read<Address>(player.actor + 0xfc);
        if (!player.map) return false;
        player.area = Read<Address>(player.map + 0x104);
        if (!player.area) return false;
        player.definition = Read<Address>(player.area + 0x50);
        return player.definition != 0;
    }

    feedback::Binding FeedbackBinding(const Player& player, Address owner, Address plane) noexcept
    {
        return {GetCurrentProcessId(), Read<std::uint16_t>(player.user + 8), player.user,
            player.context, owner, plane, ProcessStarted};
    }

    bool MapPanelVisible(Address owner) noexcept
    {
        const auto panel = Read<Address>(owner + 4);
        return panel && Read<std::uint8_t>(panel + 0x10);
    }

    bool PrepareRing(Address device, Address batch) noexcept
    {
        if (RingDevice == device && RingBatch == batch) return RingReady;
        RingReady = false;
        using Destroy = void (__thiscall*)(void*);
        if (RingConstructed)
            reinterpret_cast<Destroy>(DestroyRing)(RingSprite);
        RingConstructed = false;
        RingDevice = device;
        RingBatch = batch;
        using Construct = void* (__thiscall*)(void*, int, Address, Address);
        RingConstructed = reinterpret_cast<Construct>(ConstructRing)(RingSprite, 14, device, batch) == RingSprite;
        if (!RingConstructed || Read<Address>(reinterpret_cast<Address>(RingSprite)) != RingSpriteVtable) return false;
        constexpr auto width = static_cast<int>(feedback::RingPixelWidth);
        constexpr auto height = static_cast<int>(feedback::RingPixelHeight);
        using Create = int (__thiscall*)(void*, int, int);
        using MapTexture = int (__thiscall*)(void*);
        if (reinterpret_cast<Create>(CreateRing)(RingSprite, width, height) < 0 ||
            reinterpret_cast<MapTexture>(LockRing)(RingSprite) < 0) return false;
        const auto sprite = reinterpret_cast<Address>(RingSprite);
        const auto pitch = Read<int>(sprite + 0x7c);
        auto* pixels = Read<unsigned char*>(sprite + 0x80);
        constexpr int rowBytes = width * sizeof(std::uint16_t);
        const bool mapped = pixels && pitch >= rowBytes && pitch <= 65536;
        if (mapped)
        {
            const auto& source = feedback::GetYellowPixels();
            for (int y = 0; y < height; ++y)
                std::memcpy(pixels + y * pitch, source.data() + y * width, rowBytes);
        }
        RingReady = reinterpret_cast<MapTexture>(UnlockRing)(RingSprite) >= 0 && mapped;
        return RingReady;
    }

    void DrawRings(Address graphics, Address renderer, Point center, std::uint64_t now) noexcept
    {
        using Get = DWORD (__thiscall*)(Address, DWORD);
        using Set = void (__thiscall*)(Address, DWORD, DWORD);
        using Draw = void (__thiscall*)(Address, int, int, void*, float, float, float, DWORD, int, int);
        const auto get = reinterpret_cast<Get>(GetRenderState);
        const auto set = reinterpret_cast<Set>(SetRenderState);
        constexpr DWORD sourceBlend = 19, destinationBlend = 20;
        const auto source = get(graphics, sourceBlend), destination = get(graphics, destinationBlend);
        __try
        {
            // The original minimap pulse uses the additive effect (SRCALPHA, ONE).
            // The black pixels in this texture contribute zero in that mode.
            set(graphics, sourceBlend, 5);
            set(graphics, destinationBlend, 2);
            for (std::size_t index = 0; index < 2; ++index)
            {
                const auto ring = ClickFeedback.Ring(index, now);
                if (!ring.visible) continue;
                const auto radius = static_cast<int>(feedback::RingPixelWidth * 0.5f * ring.scale + 0.5f);
                reinterpret_cast<Draw>(DrawSprite)(renderer, center.x - radius, center.y - radius,
                    RingSprite, ring.scale, ring.scale, 0.0f, ring.color, 0, 0);
            }
        }
        __finally
        {
            set(graphics, sourceBlend, source);
            set(graphics, destinationBlend, destination);
        }
    }

    void DrawClickFeedback(Address owner) noexcept
    {
        const auto plane = Read<Address>(owner + 0x0c);
        Player player{};
        if (!plane || !MapPanelVisible(owner) || !ReadPlayer(player) || player.area != ClickArea)
        {
            ClickFeedback.Reset();
            return;
        }
        const auto now = GetTickCount64();
        ClickFeedback.Update(FeedbackBinding(player, owner, plane), true, now);
        if (!ClickFeedback.Active()) return;

        const auto viewX = Read<int>(ViewX), viewY = Read<int>(ViewY);
        if (!Coordinate(viewX) || !Coordinate(viewY) || Read<int>(plane + 4) != -viewX ||
            Read<int>(plane + 8) != -viewY) return;
        const auto graphics = Read<Address>(Graphics), renderer = Read<Address>(Renderer);
        if (!graphics || Read<Address>(graphics) != GraphicsVtable || !renderer || Read<int>(renderer + 8) != 7) return;
        const auto device = Read<Address>(graphics + 0x203c), batch = Read<Address>(graphics + 0x2040);
        if (!device || !batch || !PrepareRing(device, batch)) return;

        const auto point = ClickFeedback.GetPoint();
        DrawRings(graphics, renderer, {point.x + viewX, point.y + viewY}, now);
    }

    void __fastcall DrawTownMap(void* owner, void*) noexcept
    {
        using Original = void (__thiscall*)(void*);
        reinterpret_cast<Original>(MapDraw)(owner);
        if (GetCurrentThreadId() != GameThread || !ClickFeedback.Active()) return;
        __try { DrawClickFeedback(reinterpret_cast<Address>(owner)); }
        __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH)
        {
            ClickFeedback.Reset();
            FeedbackEnabled = false;
        }
    }

    bool Walkable(Address definition, Point point) noexcept
    {
        const auto begin = Read<Address>(definition + 0x174), end = Read<Address>(definition + 0x178);
        if (!begin || end <= begin || (end - begin) % sizeof(Rect) || (end - begin) / sizeof(Rect) > MaximumRegions) return false;
        bool inside = false;
        for (auto row = begin; row != end; row += sizeof(Rect))
        {
            const auto rect = Read<Rect>(row);
            if (!ValidRect(rect)) return false;
            inside = inside || Contains(rect, point);
        }
        return inside;
    }

    std::int32_t ProjectAxis(std::int32_t pixel, std::int32_t left, std::int32_t span,
        std::int32_t worldStart, std::int32_t worldSpan) noexcept
    {
        const auto scaled = static_cast<std::int64_t>(pixel - left) * worldSpan;
        return worldStart + static_cast<std::int32_t>((scaled + span / 2) / span);
    }

    bool TransportBusy(const Player& player) noexcept
    {
        const auto transport = Read<Address>(player.actor + 0x4f4);
        return transport && Read<std::uint8_t>(transport + 0x1d8) != 0;
    }

    bool RouteDestination = false;

    bool ApplyDestination(const Player& player, Point target) noexcept
    {
        if (TransportBusy(player) || !Walkable(player.definition, target) || Read<std::uint8_t>(AutoMove) > 1) return false;
        const auto actor = reinterpret_cast<void*>(player.actor);
        using Validate = bool (__thiscall*)(void*, std::int32_t, std::int32_t, std::int32_t*);
        using Set = void (__thiscall*)(void*, std::int32_t, std::int32_t);
        if (!reinterpret_cast<Validate>(ValidatePoint)(actor, target.x, target.y, nullptr)) return false;
        RouteDestination = true;
        reinterpret_cast<Set>(SetDestination)(actor, target.x, target.y);
        RouteDestination = false;
        if (Read<Address>(LocalActor) != player.actor || Read<Address>(player.actor + 0xfc) != player.map ||
            Read<std::int32_t>(player.actor + 0x278) != target.x || Read<std::int32_t>(player.actor + 0x27c) != target.y) return false;
        *reinterpret_cast<volatile std::uint8_t*>(AutoMove) = 1;
        return true;
    }

#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
    using TownSetDestination = void (__thiscall*)(void*, std::int32_t, std::int32_t);
    TownSetDestination OriginalTownSetDestination = nullptr;
    volatile ULONGLONG RightTeleportUntil = 0;
    volatile ULONGLONG PendingRightClickAt = 0;
    bool TownSetDestinationReady = false;

    // The game's right-click auto-path calls this destination method directly.
    // In debug mode the call made just after a right-click is replaced by a
    // same-town teleport; the map window does not have to be open.
    void __fastcall TeleportTownDestination(void* actor, void*, std::int32_t x, std::int32_t y)
    {
        if (TownSetDestinationReady && !RouteDestination && GetTickCount64() <= RightTeleportUntil
            && dfl::chapter_two::debug_control::TeleportEnabled()
            && reinterpret_cast<Address>(actor) == Read<Address>(LocalActor)
            && dfl::chapter_two::debug_control::TryTeleport(
                reinterpret_cast<std::uint32_t>(actor), x, y))
        {
            TOWNMAP_TRACE("DFL_TOWNMAP teleport=right_click actor=%08x x=%d y=%d\n", actor, x, y);
            return;
        }
        OriginalTownSetDestination(actor, x, y);
    }

    bool InstallTeleportDestinationHook() noexcept
    {
        const auto entry = reinterpret_cast<unsigned char*>(SetDestination);
        constexpr unsigned char prologue[]{0x55, 0x8b, 0xec, 0x8b, 0x81, 0xf4, 0x04, 0x00, 0x00};
        if (std::memcmp(entry, prologue, sizeof(prologue))) return false;

        auto trampoline = static_cast<unsigned char*>(
            VirtualAlloc(nullptr, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        if (!trampoline) return false;
        std::memcpy(trampoline, prologue, sizeof(prologue));
        trampoline[sizeof(prologue)] = 0xe9;
        const auto continueAt = SetDestination + sizeof(prologue);
        const auto back = static_cast<std::int32_t>(
            continueAt - (reinterpret_cast<Address>(trampoline) + sizeof(prologue) + 5));
        std::memcpy(trampoline + sizeof(prologue) + 1, &back, sizeof(back));
        OriginalTownSetDestination = reinterpret_cast<TownSetDestination>(trampoline);

        DWORD previous = 0;
        if (!VirtualProtect(entry, sizeof(prologue), PAGE_EXECUTE_READWRITE, &previous))
        {
            VirtualFree(trampoline, 0, MEM_RELEASE);
            return false;
        }
        const auto hook = static_cast<std::int32_t>(
            reinterpret_cast<Address>(&TeleportTownDestination) - (SetDestination + 5));
        entry[0] = 0xe9;
        std::memcpy(entry + 1, &hook, sizeof(hook));
        std::memset(entry + 5, 0x90, 4);
        VirtualProtect(entry, sizeof(prologue), previous, &previous);
        FlushInstructionCache(GetCurrentProcess(), entry, sizeof(prologue));
        TownSetDestinationReady = true;
        TOWNMAP_TRACE("DFL_TOWNMAP teleport_hook=installed\n");
        return true;
    }
#endif

    bool SameRect(const Rect& left, const route::Rect& right) noexcept
    {
        return left.x == right.x && left.y == right.y && left.width == right.width && left.height == right.height;
    }

    bool GeometryMatches(const Player& player, const route::Area& area) noexcept
    {
        const auto floor = Read<Address>(player.definition + 0x174), floorEnd = Read<Address>(player.definition + 0x178);
        if (!floor || floorEnd < floor || floorEnd - floor != area.walkable.size() * sizeof(Rect)) return false;
        for (std::size_t i = 0; i < area.walkable.size(); ++i)
            if (!SameRect(Read<Rect>(floor + i * sizeof(Rect)), area.walkable[i])) return false;
        struct Door { Rect bounds; int town, area; };
        const auto doors = Read<Address>(player.definition + 0x164), doorEnd = Read<Address>(player.definition + 0x168);
        if ((!doors && doorEnd) || doorEnd < doors || doorEnd - doors != area.portals.size() * sizeof(Door)) return false;
        for (std::size_t i = 0; i < area.portals.size(); ++i)
        {
            const auto actual = Read<Door>(doors + i * sizeof(Door));
            const auto& expected = area.portals[i];
            if (!SameRect(actual.bounds, expected.rect) || actual.town != expected.target.town || actual.area != expected.target.area) return false;
        }
        return true;
    }

    route::Observation ObserveRoute(const Player& player, std::uint64_t now) noexcept
    {
        route::Observation observation{};
        observation.scene = 1;
        observation.pid = GetCurrentProcessId();
        observation.started = ProcessStarted;
        observation.uid = Read<std::uint16_t>(player.user + 8);
        observation.user = player.user;
        observation.actor = player.actor;
        observation.context = player.context;
        observation.area = {player.townId, player.areaId};
        // Town area ids may update independently of the actual loaded geometry.
        // A continuation requires replacement of the native map/area/definition chain.
        if (player.map != LastMap || player.area != LastArea || player.definition != LastDefinition)
        {
            LastMap = player.map;
            LastArea = player.area;
            LastDefinition = player.definition;
            ++Generation;
        }
        observation.map = static_cast<std::uint32_t>(Generation);
        observation.generation = Generation;
        observation.sequence = ++Sequence;
        observation.capturedTick = now;
        observation.busy = TransportBusy(player) || Read<std::uint8_t>(SelectionMode) != 0;
        observation.destination = {Read<int>(player.actor + 0x278), Read<int>(player.actor + 0x27c)};
        observation.directionX = Read<int>(player.actor + 0x270);
        observation.directionY = Read<int>(player.actor + 0x274);
        const auto autoMove = Read<std::uint8_t>(AutoMove);
        observation.autoKnown = autoMove <= 1;
        observation.autoActive = autoMove == 1;
        if (Read<Address>(TownActorVtable + 0x94) != 0x406b50 || Read<Address>(TownActorVtable + 0x98) != 0x406b60)
            return observation;
        using Position = int (__thiscall*)(void*);
        auto* actor = reinterpret_cast<void*>(player.actor);
        observation.position = {static_cast<double>(reinterpret_cast<Position>(0x406b50)(actor)),
            static_cast<double>(reinterpret_cast<Position>(0x406b60)(actor))};
        observation.valid = observation.uid && observation.autoKnown;
        return observation;
    }

    bool BeginRoute(const Player& player, const Region& region, Point point) noexcept
    {
        const auto* catalog = PublishedCatalog.load(std::memory_order_acquire);
        if (RouteDisabled || !catalog) return false;
        try
        {
            const auto now = GetTickCount64();
            const auto observation = ObserveRoute(player, now);
            const auto* area = catalog->Find(observation.area);
            if (!area || !GeometryMatches(player, *area)) return false;
            std::string error;
            return Controller.Start(*catalog, observation, {{region.town, region.area}, {point.x, point.y}}, now, error);
        }
        catch (...) { RouteDisabled = true; }
        return false;
    }

    void CancelRoute() noexcept
    {
        if (!Controller.Active()) return;
        try { Controller.Cancel("superseded"); }
        catch (...) { RouteDisabled = true; }
    }

    void AdvanceRoute() noexcept
    {
        const auto* catalog = PublishedCatalog.load(std::memory_order_acquire);
        if (RouteDisabled || !catalog || !Controller.Active()) return;
        try
        {
            const auto now = GetTickCount64();
            route::Observation observation{};
            Player player{};
            if (ReadPlayer(player)) observation = ObserveRoute(player, now);
            else if (!InTown(Read<Address>(Game))) observation.scene = 0;
            const auto action = Controller.Tick(*catalog, observation, now);
            if (action.kind != route::Action::Kind::SetDestination) return;
            const auto* area = catalog->Find(observation.area);
            const bool accepted = area && GeometryMatches(player, *area) &&
                ApplyDestination(player, {action.point.x, action.point.y});
            Controller.ConfirmDestination(observation, action.point, now, accepted);
        }
        catch (...) { RouteDisabled = true; }
    }

    void __fastcall PollTownMapRoute(void* owner, void*) noexcept
    {
        using Original = void (__thiscall*)(void*);
        reinterpret_cast<Original>(MapPoll)(owner);
        if (GetCurrentThreadId() != GameThread) return;
        __try
        {
            if (ClickFeedback.Active() && !MapPanelVisible(reinterpret_cast<Address>(owner))) ClickFeedback.Reset();
#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
            const auto pending = PendingRightClickAt;
            if (pending)
            {
                const auto now = GetTickCount64();
                if (now > pending + 600)
                {
                    PendingRightClickAt = 0;
                }
                else if (now >= pending + 60)
                {
                    const auto actor = Read<Address>(LocalActor);
                    if (actor && dfl::chapter_two::debug_control::TeleportEnabled())
                    {
                        const auto x = Read<std::int32_t>(actor + 0x278);
                        const auto y = Read<std::int32_t>(actor + 0x27c);
                        using Get = int(__thiscall*)(void*, int);
                        const int currentX = reinterpret_cast<Get>(0x487070)(reinterpret_cast<void*>(actor), 0);
                        const int currentY = reinterpret_cast<Get>(0x487070)(reinterpret_cast<void*>(actor), 1);
                        if (x != currentX || y != currentY)
                        {
                            const bool teleported = dfl::chapter_two::debug_control::TryTeleport(
                                static_cast<std::uint32_t>(actor), x, y);
                            PendingRightClickAt = 0;
                            if (teleported)
                            {
                                TOWNMAP_TRACE("DFL_TOWNMAP teleport=right_click actor=%08x x=%d y=%d\n", actor, x, y);
                            }
                        }
                    }
                }
            }
#endif
            AdvanceRoute();
        }
        __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH)
        { RouteDisabled = true; }
    }

    void ApplyMapRelease(std::int32_t captured) noexcept
    {
        auto reject = [captured](const char* reason) noexcept
        {
            (void)captured;
            (void)reason;
            TOWNMAP_TRACE("DFL_TOWNMAP reject=%s captured=%d\n", reason, captured);
        };
        if (captured < 0 || Read<std::uint32_t>(RightEvent) != 0 || Read<std::uint8_t>(RightDown) ||
            Read<std::uint8_t>(SelectionMode) || Read<std::uint32_t>(RightCapture) != static_cast<std::uint32_t>(captured))
        {
            TOWNMAP_TRACE("DFL_TOWNMAP reject=input captured=%d event=%u down=%u selection=%u rightCapture=%u\n",
                captured, Read<std::uint32_t>(RightEvent), Read<std::uint8_t>(RightDown),
                Read<std::uint8_t>(SelectionMode), Read<std::uint32_t>(RightCapture));
            return;
        }
        const auto input = Read<Address>(InputManager);
        if (!input) { reject("no_input"); return; }
        const auto owner = Read<Address>(input + 0x1354);
        if (!owner || Read<Address>(owner) != MapVtable) { reject("owner"); return; }
        const auto panel = Read<Address>(owner + 4), plane = Read<Address>(owner + 0x0c);
        const auto regions = Read<Address>(owner + 0x1c), npcs = Read<Address>(owner + 0x60);
        if (!panel || !plane || !regions || !npcs ||
            Read<std::int32_t>(panel + 0x0c) != captured || !Read<std::uint8_t>(panel + 0x10) ||
            !Read<std::uint8_t>(panel + 0x11) || Read<std::uint8_t>(regions + 0x10) || Read<std::uint8_t>(npcs + 0x10))
        {
            TOWNMAP_TRACE("DFL_TOWNMAP reject=panel panel=%08x plane=%08x regions=%08x npcs=%08x\n",
                panel, plane, regions, npcs);
            return;
        }

        const Point view{Read<std::int32_t>(ViewX), Read<std::int32_t>(ViewY)};
        const Point mouse{Read<std::int32_t>(MouseX), Read<std::int32_t>(MouseY)};
        if (!Coordinate(view.x) || !Coordinate(view.y) || !Coordinate(mouse.x) || !Coordinate(mouse.y) ||
            Read<std::int32_t>(plane + 4) != -view.x || Read<std::int32_t>(plane + 8) != -view.y)
        {
            TOWNMAP_TRACE("DFL_TOWNMAP reject=view view=%d,%d mouse=%d,%d\n", view.x, view.y, mouse.x, mouse.y);
            return;
        }
        const Point local{mouse.x - view.x, mouse.y - view.y};
        Player player{};
        if (!ReadPlayer(player)) { reject("player"); return; }
        Region projected{}, real{};
        if (!FindRegion(plane + 0x20, [&](const Region& region) { return Contains(region.bounds, local); }, projected) ||
            !FindRegion(WorldRegions, [&](const Region& region) { return region.town == projected.town && region.area == projected.area; }, real) ||
            !CanClickThroughMarkers(plane, local))
        {
            TOWNMAP_TRACE("DFL_TOWNMAP reject=region local=%d,%d\n", local.x, local.y);
            return;
        }

        const Point target{ProjectAxis(local.x, projected.bounds.x, projected.bounds.width, real.bounds.x, real.bounds.width),
            ProjectAxis(local.y, projected.bounds.y, projected.bounds.height, real.bounds.y, real.bounds.height)};
        if (!Contains(real.bounds, target))
        {
            TOWNMAP_TRACE("DFL_TOWNMAP reject=target local=%d,%d target=%d,%d\n", local.x, local.y, target.x, target.y);
            return;
        }
        bool accepted = false;
        if (projected.town != player.townId || projected.area != player.areaId)
        {
            TOWNMAP_TRACE("DFL_TOWNMAP route town=%d area=%d playerTown=%d playerArea=%d target=%d,%d\n",
                projected.town, projected.area, player.townId, player.areaId, target.x, target.y);
            accepted = BeginRoute(player, projected, target);
        }
        else
        {
#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
            const bool teleportEnabled = dfl::chapter_two::debug_control::TeleportEnabled();
            accepted = teleportEnabled
                && dfl::chapter_two::debug_control::TryTeleport(player.actor, target.x, target.y);
            TOWNMAP_TRACE("DFL_TOWNMAP same owner=%08x actor=%08x target=%d,%d teleport=%d accepted=%d\n",
                owner, player.actor, target.x, target.y, teleportEnabled ? 1 : 0, accepted ? 1 : 0);
            if (!accepted) accepted = ApplyDestination(player, target);
#else
            accepted = ApplyDestination(player, target);
#endif
            if (accepted) CancelRoute();
        }
        TOWNMAP_TRACE("DFL_TOWNMAP result accepted=%d target=%d,%d\n", accepted ? 1 : 0, target.x, target.y);
        if (accepted && FeedbackEnabled)
        {
            ClickArea = player.area;
            ClickFeedback.Start(FeedbackBinding(player, owner, plane), {local.x, local.y}, GetTickCount64());
        }
    }

    std::int32_t __fastcall CaptureTownMapRelease(void* root, void*) noexcept
    {
        const auto thread = GetCurrentThreadId();
        const auto event = thread == GameThread ? Read<std::uint32_t>(RightEvent) : 0;
        const bool rightDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        using Original = std::int32_t (__thiscall*)(void*);
        const auto captured = reinterpret_cast<Original>(Capture)(root);
        if (event == RightRelease || rightDown)
        {
#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
            RightTeleportUntil = GetTickCount64() + 500;
            if (event == RightRelease) PendingRightClickAt = GetTickCount64();
#endif
            TOWNMAP_TRACE(
                "DFL_TOWNMAP capture thread=%u game=%u event=%u rightDown=%u captured=%d mouse=%d,%d\n",
                thread, GameThread, event, rightDown ? 1u : 0u, captured,
                Read<std::int32_t>(MouseX), Read<std::int32_t>(MouseY));
        }
        if (event == RightRelease)
        {
            __try { ApplyMapRelease(captured); }
            __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {}
        }
        return captured;
    }

    template<std::size_t N>
    bool Matches(const unsigned char* image, Address address, const unsigned char (&signature)[N]) noexcept
    {
        return std::memcmp(image + (address - ImageBase), signature, N) == 0;
    }

    void RedirectCall(unsigned char* image, Address call, Address hook) noexcept
    {
        const auto displacement = hook - (call + 5);
        auto* operand = image + (call - ImageBase) + 1;
        std::memcpy(operand, &displacement, sizeof(displacement));
        FlushInstructionCache(GetCurrentProcess(), operand, sizeof(displacement));
    }

    bool InstallClickFeedback(unsigned char* image) noexcept
    {
        constexpr unsigned char caller[]{0x8b,0xce,0xe8,0xbd,0xfb,0xff,0xff,0x8b,0xce,0xe8,0xc6,0xf7};
        constexpr unsigned char original[]{0x55,0x8b,0xec,0x6a,0xff,0x68,0x3e,0x8d,0xad,0};
        constexpr unsigned char construct[]{0x55,0x8b,0xec,0x8b,0x45,0x10,0x8b,0x55,8};
        constexpr unsigned char draw[]{0x55,0x8b,0xec,0x8b,0x45,0x10,0x85,0xc0,0x74,0x2d};
        if (!Matches(image, MapDrawCall - 2, caller) || !Matches(image, MapDraw, original) ||
            !Matches(image, ConstructRing, construct) || !Matches(image, DrawSprite, draw) ||
            Read<Address>(GraphicsVtable + 0x1c) != SetRenderState || Read<Address>(GraphicsVtable + 0x20) != GetRenderState ||
            Read<Address>(RingSpriteVtable + 0x0c) != CreateRing || Read<Address>(RingSpriteVtable + 0x5c) != LockRing ||
            Read<Address>(RingSpriteVtable + 0x60) != UnlockRing || Read<Address>(RingSpriteVtable + 0xa4) != 0xa13ec0)
            return false;
        RedirectCall(image, MapDrawCall, reinterpret_cast<Address>(&DrawTownMap));
        return true;
    }
}

bool dfl::chapter_two::InstallTownMapMovement(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4);
    if (reinterpret_cast<Address>(image) != ImageBase || bytes != ImageSize)
    {
        TOWNMAP_TRACE("DFL_TOWNMAP install=fail image=%p bytes=%zu\n", image, bytes);
        return false;
    }
    constexpr unsigned char caller[]{0x8b, 0xce, 0xe8, 0x8c, 0xfc, 0xff, 0xff, 0x8b, 0xce, 0xe8, 0xc5, 0xfa, 0xff, 0xff};
    constexpr unsigned char capture[]{0x55, 0x8b, 0xec, 0x83, 0xec, 0x08, 0x53, 0x8b, 0xd9, 0x8a, 0x43, 0x10, 0x84, 0xc0, 0x74, 0x07};
    constexpr unsigned char validate[]{0x55, 0x8b, 0xec, 0x8b, 0x45, 0x10, 0x85, 0xc0, 0x74, 0x06, 0xc7, 0x00, 0, 0, 0, 0};
    constexpr unsigned char set[]{0x55, 0x8b, 0xec, 0x8b, 0x81, 0xf4, 0x04, 0, 0, 0x85, 0xc0, 0x74, 0x0a, 0x8a, 0x90, 0xd8};
    constexpr unsigned char activate[]{0xc6, 0x05, 0xb0, 0x86, 0xd5, 0, 1};
    constexpr unsigned char update[]{0xa0, 0xb0, 0x86, 0xd5, 0, 0x84, 0xc0, 0x0f, 0x84, 0xb1, 0, 0, 0};
    constexpr unsigned char pollCaller[]{0x8b,0x8e,0x54,0x13,0,0,0xe8,0x6a,0xad,0x0f,0,0x8b,0x8e,0x80,0x12,0};
    constexpr unsigned char poll[]{0x53,0x8b,0xd9,0x8b,0x43,0x1c,0x8a,0x48,0x10,0x56,0x57,0x8d,0x7b,0x1c,0x84,0xc9};
    auto check = [image](bool condition, const char* name) noexcept
    {
        (void)name;
        if (!condition) TOWNMAP_TRACE("DFL_TOWNMAP install=fail check=%s\n", name);
        return condition;
    };
    if (!check(Matches(image, MapPollCall - 6, pollCaller), "pollCaller") ||
        !check(Matches(image, MapPoll, poll), "poll") ||
        !check(Matches(image, CaptureCall - 2, caller), "captureCaller") ||
        !check(Matches(image, Capture, capture), "capture") ||
        !check(Matches(image, ValidatePoint, validate), "validate") ||
        !check(Matches(image, SetDestination, set), "set") ||
        !check(Matches(image, 0x8686e7, activate), "activate") ||
        !check(Matches(image, 0x493e50, update), "update") ||
        !check(Read<Address>(TownActorVtable + 0xac) == ValidatePoint, "actorValidate") ||
        !check(Read<Address>(TownActorVtable + 0x1a8) == SetDestination, "actorSet") ||
        !check(Read<Address>(QuestVtable + 4) == QuestHit, "questHit")) return false;

    GameThread = GetCurrentThreadId();
    RedirectCall(image, CaptureCall, reinterpret_cast<Address>(&CaptureTownMapRelease));
    RedirectCall(image, MapPollCall, reinterpret_cast<Address>(&PollTownMapRoute));
#ifdef DFL_CHAPTER_TWO_DEBUG_CONTROL
    const bool teleportHook = InstallTeleportDestinationHook();
    TOWNMAP_TRACE("DFL_TOWNMAP teleport_hook=%d\n", teleportHook ? 1 : 0);
#endif
    FeedbackEnabled = InstallClickFeedback(image);
    StartCatalogWorker();
    TOWNMAP_TRACE("DFL_TOWNMAP install=ok thread=%u feedback=%d\n", GameThread, FeedbackEnabled ? 1 : 0);
    return true;
}
