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
    volatile LONG Notifications = 0;

    void WriteDiagnostic(const char* action,
        const dfl::ground_item_removal::detail::PickupRemoval& removal = {}) noexcept
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
        char line[192]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%s picker=%u item=%u ground=%u\r\n", action,
            static_cast<unsigned int>(removal.PickerUserId),
            static_cast<unsigned int>(removal.ItemId),
            static_cast<unsigned int>(removal.GroundId));
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

    void* CurrentMap(void* dispatcher)
    {
        // This is the same dispatcher->context->stage->map chain used by
        // NOTI39 at 421EF7..421F0B. Only a live dungeon map can own a drop.
        if (dispatcher == nullptr) return nullptr;
        const auto context = *reinterpret_cast<const unsigned char* const*>(
            static_cast<const unsigned char*>(dispatcher) + 0x24);
        if (context == nullptr) return nullptr;
        const auto stage = *reinterpret_cast<const unsigned char* const*>(context + 0x10);
        if (stage == nullptr) return nullptr;
        void* const map = *reinterpret_cast<void* const*>(stage + 0xB0);
        const auto localActor = *reinterpret_cast<const unsigned char* const*>(0x00B75A14);
        if (localActor == nullptr || map == nullptr
            || *reinterpret_cast<void* const*>(localActor + 0xE4) != map)
            return nullptr;
        const auto game = *reinterpret_cast<const unsigned char* const*>(0x00B5D6F0);
        if (game == nullptr) return nullptr;
        using ReadScene = int (__thiscall*)(const void*);
        return reinterpret_cast<ReadScene>(0x00402030)(game + 16) == 3 ? map : nullptr;
    }

    int __stdcall HandleExtension(const unsigned char* frame, void* dispatcher) noexcept
    {
        bool recognized = false;
        __try
        {
            if (frame == nullptr || frame[0] != 0 || frame[1] != 86) return 0;
            std::uint32_t frameSize = 0;
            std::memcpy(&frameSize, frame + 2, sizeof(frameSize));
            if (frameSize != 15) return 0;
            // 40D7C3..40D7E1 decrypts into a separate buffer and initializes
            // BF29E0. The original frame+6 remains ciphertext: never parse it.
            auto& reader = *reinterpret_cast<const unsigned char**>(0x00BF29E0);
            dfl::ground_item_removal::detail::PickupRemoval removal;
            if (!dfl::ground_item_removal::detail::ReadExtension(reader, 9, removal))
                return 0;
            recognized = true;
            reader += 9;
            void* const map = CurrentMap(dispatcher);
            const bool removed = dfl::ground_item_removal::detail::ApplyRemoval(
                map, removal, Functions);
            if (InterlockedIncrement(&Notifications) <= 128)
                WriteDiagnostic(removed ? "deactivated" : "absent-inactive-or-outside-map", removal);
            return 1;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
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

    bool detail::ApplyRemoval(void* map, const PickupRemoval& removal,
        const ClientFunctions& functions) noexcept
    {
        if (map == nullptr || functions.Find == nullptr || functions.Deactivate == nullptr)
            return false;
        const auto ground = functions.Find(map, GroundItemObjectType, removal.GroundId);
        if (ground.Actor == nullptr || ground.Map != map || !ground.Active
            || ground.ObjectType != GroundItemObjectType || ground.GroundId != removal.GroundId)
            return false;
        functions.Deactivate(ground.Actor);
        return true;
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
