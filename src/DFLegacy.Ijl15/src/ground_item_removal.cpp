#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "ground_item_removal.h"
#include "memory_patch.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t Notification86Site = 0x00427104;
    constexpr std::array<unsigned char, 7> OriginalNotification86Code{
        0xC6, 0x85, 0xC3, 0xCF, 0xFF, 0xFF, 0x00};
    constexpr std::array<unsigned char, 16> OriginalDeactivateCode{
        0x55, 0x8B, 0xEC, 0x8A, 0x45, 0x08, 0x88, 0x81,
        0x28, 0x01, 0x00, 0x00, 0x5D, 0xC2, 0x04, 0x00};
    constexpr std::array<unsigned char, 18> OriginalDeathGetterCode{
        0x81, 0xC1, 0x2C, 0x14, 0x00, 0x00, 0xE8, 0x25, 0xF6,
        0xDA, 0xFF, 0xF7, 0xD8, 0x1B, 0xC0, 0xF7, 0xD8, 0xC3};
    volatile LONG Notifications = 0;

    void WriteDiagnostic(const char* action,
        const dfl::ground_item_removal::detail::PickupRemoval& removal = {},
        const dfl::ground_item_removal::detail::MapContext* context = nullptr) noexcept
    {
        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return;
        DWORD filename = length;
        while (filename != 0 && path[filename - 1] != L'\\') --filename;
        if (_snwprintf_s(path + filename, MAX_PATH - filename, _TRUNCATE,
                L"ground-item-removal-%lu.log", GetCurrentProcessId()) < 0) return;
        const HANDLE file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME now{}; GetLocalTime(&now);
        char line[512]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%s picker=%u item=%u ground=%u time=%02u:%02u:%02u.%03u T=%lu scene=%d stage=%p ui-stage=%p map=%p actor=%p actor-map=%p death=%d context=%s\r\n", action,
            static_cast<unsigned int>(removal.PickerUserId),
            static_cast<unsigned int>(removal.ItemId),
            static_cast<unsigned int>(removal.GroundId), now.wHour, now.wMinute, now.wSecond, now.wMilliseconds,
            GetCurrentThreadId(), context ? context->Scene : -1, context ? context->Stage : nullptr,
            context ? context->UiStage : nullptr, context ? context->Map : nullptr,
            context ? context->Actor : nullptr, context ? context->ActorMap : nullptr,
            context && context->DeathKnown ? (context->Dead ? 1 : 0) : -1,
            context ? context->Reason : "none");
        DWORD written = 0;
        if (size > 0) WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        CloseHandle(file);
    }

    dfl::ground_item_removal::detail::GroundItem __cdecl FindGroundItem(
        void* map, std::uint16_t objectType, std::uint16_t groundId)
    {
        using Lookup = void* (__thiscall*)(void*, unsigned int, unsigned int);
        void* const actor = reinterpret_cast<Lookup>(0x004C5AA0)(map, objectType, groundId);
        if (actor == nullptr) return {};
        const auto bytes = static_cast<const unsigned char*>(actor);
        const auto table = *reinterpret_cast<const std::uintptr_t*>(actor);
        // Both ordinary and gold drops use the native item-object activation
        // methods. Reject an incompatible actor rather than calling an unknown
        // mutator merely because a stale network id matched.
        if (*reinterpret_cast<const std::uintptr_t*>(table + 0x58) != 0x00568540
            || *reinterpret_cast<const std::uintptr_t*>(table + 0x5C) != 0x00407410)
            return {};
        using Read = unsigned int (__thiscall*)(void*);
        const auto readType = reinterpret_cast<Read>(
            *reinterpret_cast<const std::uintptr_t*>(table + 0x34));
        const auto readId = reinterpret_cast<Read>(
            *reinterpret_cast<const std::uintptr_t*>(table + 0x38));
        const auto type = readType(actor);
        const auto id = readId(actor);
        if (type > 0xFFFF || id > 0xFFFF) return {};
        return {actor, *reinterpret_cast<void* const*>(bytes + 0xE4),
            static_cast<std::uint16_t>(type), static_cast<std::uint16_t>(id),
            bytes[0x128] != 0};
    }

    void __cdecl Deactivate(void* actor)
    {
        // NOTI39 at 421F58 uses this exact v58(false). 4C6CB0 checks v5C
        // each frame; for kind 0121 it invokes 4C6C90 (map removal + deleting
        // destructor). 42DA40 -> 46E700 also frees an unclaimed descriptor.
        // Calling 417C40 here would only detach that descriptor and leak it.
        using SetActive = void (__thiscall*)(void*, unsigned char);
        reinterpret_cast<SetActive>(0x00568540)(actor, 0);
    }

    const dfl::ground_item_removal::detail::ClientFunctions Functions{
        &FindGroundItem, &Deactivate};

    struct ContextReads
    {
        const dfl::ground_item_removal::detail::ContextReader& Reader;
        std::uint32_t Manager = 0, Key = 0;
        template<class T> bool At(std::uintptr_t base, std::uintptr_t offset, T& value) const noexcept
        {
            return Reader.Read && base >= 0x10000 && offset <= UINTPTR_MAX - base
                && base + offset <= UINTPTR_MAX - sizeof(T)
                && Reader.Read(Reader.State, base + offset, &value, sizeof(T));
        }
        bool Secure(std::uintptr_t base, std::uintptr_t offset, std::uint32_t& value) const noexcept
        {
            if (offset > UINTPTR_MAX - base) return false;
            const auto address = base + offset;
            std::uint32_t encoded = 0, index = 0, count = 0, first = 0, second = 0;
            if (!At(address, 0, encoded) || !At(address, 4, index)
                || ((encoded ^ Key) - address) != index || !At(Manager, 4, count)
                || index >= count || index > 0x1000000
                || !At(Manager, 0x44, first) || !At(Manager, 0x48, second)
                || first < 0x10000 || second < 0x10000
                || first > UINT32_MAX - index * 4 || second > UINT32_MAX - index * 4) return false;
            first += index * 4; second += index * 4;
            std::uint32_t a = 0, b = 0;
            if (!At(first, 0, a) || !At(second, 0, b)) return false;
            a ^= first ^ Key; b ^= second ^ Key;
            if (a != b) return false;
            value = a; return true;
        }
    };

    bool __cdecl NativeRead(void*, std::uintptr_t address, void* output, std::size_t size)
    {
        __try { std::memcpy(output, reinterpret_cast<const void*>(address), size); return true; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    const dfl::ground_item_removal::detail::ContextReader NativeReader{nullptr, &NativeRead};

    int __stdcall HandleExtension(const unsigned char* frame, void* dispatcher) noexcept
    {
        bool recognized = false;
        dfl::ground_item_removal::detail::PickupRemoval removal;
        dfl::ground_item_removal::detail::MapContext context;
        __try
        {
            if (frame == nullptr || frame[0] != 0 || frame[1] != 86) return 0;
            std::uint32_t frameSize = 0;
            std::memcpy(&frameSize, frame + 2, sizeof(frameSize));
            if (frameSize != 15) return 0;
            // 40D7C3..40D7E1 decrypts into a separate buffer and initializes
            // BF29E0. The original frame+6 remains ciphertext: never parse it.
            auto& reader = *reinterpret_cast<const unsigned char**>(0x00BF29E0);
            if (!dfl::ground_item_removal::detail::ReadExtension(reader, 9, removal))
                return 0;
            recognized = true;
            reader += 9;
            void* const map = dfl::ground_item_removal::detail::ReadCurrentMap(
                dispatcher, NativeReader, context);
            const char* outcome = context.Reason;
            if (map != nullptr)
                dfl::ground_item_removal::detail::ApplyRemoval(map, removal, Functions, &outcome);
            if (InterlockedIncrement(&Notifications) <= 128)
                WriteDiagnostic(outcome, removal, &context);
            return 1;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            if (recognized && InterlockedIncrement(&Notifications) <= 128)
                WriteDiagnostic("receiver-exception", removal, &context);
            return recognized ? 1 : 0;
        }
    }

    __declspec(naked) void Notification86Hook()
    {
        __asm
        {
            pushfd
            pushad
            mov eax, dword ptr [ebp + 8]
            mov ecx, dword ptr [ebp - 6110h]
            push ecx
            push eax
            call HandleExtension
            test eax, eax
            jnz handled
            popad
            popfd
            mov byte ptr [ebp - 303Dh], 0
            push 0042710Bh
            ret
        handled:
            popad
            popfd
            push 0042A069h
            ret
        }
    }

    template<std::size_t Size>
    bool Matches(std::uintptr_t address, const std::array<unsigned char, Size>& code) noexcept
    {
        return std::memcmp(reinterpret_cast<const void*>(address), code.data(), Size) == 0;
    }
}

namespace dfl::ground_item_removal
{
    bool detail::ReadExtension(const unsigned char* payload, std::size_t size,
        PickupRemoval& removal) noexcept
    {
        if (payload == nullptr || size != 9 || payload[0] != 5) return false;
        removal.PickerUserId = static_cast<std::uint16_t>(payload[1] | (payload[2] << 8));
        removal.ItemId = static_cast<std::uint32_t>(payload[3])
            | (static_cast<std::uint32_t>(payload[4]) << 8)
            | (static_cast<std::uint32_t>(payload[5]) << 16)
            | (static_cast<std::uint32_t>(payload[6]) << 24);
        removal.GroundId = static_cast<std::uint16_t>(payload[7] | (payload[8] << 8));
        return true;
    }

    void* detail::ReadCurrentMap(void* dispatcher, const ContextReader& reader,
        MapContext& context) noexcept
    {
        context = {};
        ContextReads r{reader};
        std::uint32_t game = 0, scene = 0, ui = 0, owner = 0, stage = 0, map = 0;
        std::uint32_t uiStage = 0, uiMap = 0, actor = 0, actorMap = 0, kind = 0, table = 0, method = 0, dead = 0;
        const auto dispatcherAddress = reinterpret_cast<std::uintptr_t>(dispatcher);
        if (!r.At(0xCFD458, 0, r.Manager) || !r.Manager || !r.At(0xCFD45C, 0, r.Key)
            || !r.At(0xB5D6F0, 0, game) || !r.Secure(game, 0x10, scene)) return nullptr;
        context.Scene = scene <= INT32_MAX ? static_cast<int>(scene) : -1;
        if (scene != 3) { context.Reason = "scene-mismatch"; return nullptr; }
        if (!r.At(dispatcherAddress, 0x24, owner) || !r.At(owner, 0x10, stage) || !stage
            || !r.At(stage, 0xB0, map) || !map
            || !r.At(0xB5DCE0, 0, ui) || !r.At(ui, 0x200010, uiStage) || !uiStage
            || !r.At(uiStage, 0xB0, uiMap)) return nullptr;
        context.Stage = reinterpret_cast<void*>(stage); context.UiStage = reinterpret_cast<void*>(uiStage);
        context.Map = reinterpret_cast<void*>(map);
        if (uiStage != stage || uiMap != map) { context.Reason = "current-stage-mismatch"; return nullptr; }
        if (!r.At(0xB75A14, 0, actor) || !actor || !r.At(actor, 0xD0, kind) || kind != 0x111
            || !r.At(actor, 0xE4, actorMap) || !r.At(actor, 0, table) || !table
            || !r.At(table, 0x318, method)) return nullptr;
        context.Actor = reinterpret_cast<void*>(actor); context.ActorMap = reinterpret_cast<void*>(actorMap);
        context.DeathKnown = method == 0x652A00 && r.Secure(actor, 0x142C, dead);
        context.Dead = context.DeathKnown && dead != 0;
        const auto same = [&](std::uintptr_t base, std::uintptr_t offset, auto expected) noexcept
        {
            decltype(expected) actual{}; return r.At(base, offset, actual) && actual == expected;
        };
        std::uint32_t finalScene = 0, finalDead = 0;
        if (!same(0xCFD458, 0, r.Manager) || !same(0xCFD45C, 0, r.Key)
            || !same(0xB5D6F0, 0, game) || !r.Secure(game, 0x10, finalScene) || finalScene != scene
            || !same(dispatcherAddress, 0x24, owner) || !same(owner, 0x10, stage) || !same(stage, 0xB0, map)
            || !same(0xB5DCE0, 0, ui) || !same(ui, 0x200010, uiStage) || !same(uiStage, 0xB0, uiMap)
            || !same(0xB75A14, 0, actor) || !same(actor, 0xE4, actorMap) || !same(actor, 0xD0, kind)
            || !same(actor, 0, table) || !same(table, 0x318, method)
            || (context.DeathKnown && (!r.Secure(actor, 0x142C, finalDead) || finalDead != dead)))
        { context.Reason = "context-changed"; return nullptr; }
        // Native death removes the local actor from its map. NOTI39 still uses
        // the current stage map; never search the dead actor's former map.
        if (actorMap != map && (!context.DeathKnown || !context.Dead))
        {
            context.Reason = context.DeathKnown ? "alive-actor-map-mismatch" : "death-unknown";
            return nullptr;
        }
        context.Reason = actorMap != map ? "ready-dead-current-stage" : "ready-attached";
        return reinterpret_cast<void*>(map);
    }

    bool detail::ApplyRemoval(void* map, const PickupRemoval& removal,
        const ClientFunctions& functions, const char** outcome) noexcept
    {
        const auto result = [outcome](const char* reason, bool removed = false) noexcept
        { if (outcome) *outcome = reason; return removed; };
        if (map == nullptr) return result("missing-map");
        if (functions.Find == nullptr || functions.Deactivate == nullptr) return result("missing-callback");
        const auto ground = functions.Find(map, GroundItemObjectType, removal.GroundId);
        if (ground.Actor == nullptr) return result("absent-or-unsupported");
        if (ground.Map != map) return result("foreign-object-map");
        if (!ground.Active) return result("already-inactive");
        if (ground.ObjectType != GroundItemObjectType) return result("wrong-object-type");
        if (ground.GroundId != removal.GroundId) return result("wrong-ground-id");
        functions.Deactivate(ground.Actor);
        return result("deactivated", true);
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != ImageBase)
            return false;
        std::array<unsigned char, OriginalNotification86Code.size()> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&Notification86Hook) - Notification86Site - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        if (Matches(Notification86Site, patch)) return true;
        if (!Matches(Notification86Site, OriginalNotification86Code)
            || !Matches(0x00568540, OriginalDeactivateCode)
            || !Matches(0x00652A00, OriginalDeathGetterCode)
            || *reinterpret_cast<const std::uintptr_t*>(0x00999574 + 0x58) != 0x00568540
            || *reinterpret_cast<const std::uintptr_t*>(0x00999574 + 0x5C) != 0x00407410)
        {
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        if (!memory_patch::WriteBytes(Notification86Site, patch.data(), patch.size()))
            return false;
        WriteDiagnostic("installed-427104");
        return true;
    }
}
