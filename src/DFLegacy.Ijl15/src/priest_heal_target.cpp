#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "memory_patch.h"
#include "priest_heal_target.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t PriestStateSlot = 0x00A05348;
    constexpr std::uintptr_t PriestStateFunction = 0x00627770;
    constexpr std::uintptr_t CompleteCastSlot = 0x00A05764;
    constexpr std::uintptr_t CompleteCastFunction = 0x0049BE80;
    constexpr std::uintptr_t HealTargetSite = 0x0062DD9A;
    constexpr std::array<unsigned char, 6> OriginalTargetCode{
        0x8B, 0x86, 0x4C, 0x21, 0x00, 0x00};
    using StateFunction = unsigned int (__thiscall*)(void*, int, const void*,
        std::uint32_t, std::uint32_t);
    using CompleteFunction = void (__thiscall*)(void*, int, void*);

    struct PendingCast
    {
        void* Caster = nullptr;
        dfl::priest_heal_target::detail::CastTarget Target;
    };
    thread_local PendingCast Pending;
    volatile LONG Repairs = 0;

    void WriteDiagnostic(const char* action, void* caster = nullptr,
        void* target = nullptr, std::uint32_t reference = 0) noexcept
    {
        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return;
        DWORD filename = length;
        while (filename != 0 && path[filename - 1] != L'\\') --filename;
        if (_snwprintf_s(path + filename, MAX_PATH - filename, _TRUNCATE,
                L"priest-heal-target-%lu.log", GetCurrentProcessId()) < 0) return;
        const HANDLE file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        char line[256]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%s caster=%p target=%p reference=%08X\r\n",
            action, caster, target, reference);
        DWORD written = 0;
        if (size > 0) WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        CloseHandle(file);
    }

    void* __cdecl LookupUserActor(std::uint16_t userId)
    {
        void* const manager = *reinterpret_cast<void* const*>(0x00B5DCB8);
        if (manager == nullptr) return nullptr;
        using Lookup = void* (__thiscall*)(void*, unsigned int);
        const auto user = static_cast<const unsigned char*>(
            reinterpret_cast<Lookup>(0x004FDDB0)(manager, userId));
        if (user == nullptr
            || *reinterpret_cast<const std::uint16_t*>(user + 4) != userId)
            return nullptr;
        return *reinterpret_cast<void* const*>(user + 0x110);
    }

    std::uint32_t __cdecl ObjectReference(void* actor)
    {
        const auto table = *reinterpret_cast<const std::uintptr_t*>(actor);
        using Read = std::uint32_t (__thiscall*)(void*);
        return reinterpret_cast<Read>(
            *reinterpret_cast<const std::uintptr_t*>(table + 0x3C))(actor);
    }

    bool __cdecl MapContains(void* map, void* actor)
    {
        using Contains = unsigned char (__thiscall*)(void*, void*);
        return reinterpret_cast<Contains>(0x004C5310)(map, actor) != 0;
    }

    const dfl::priest_heal_target::detail::ClientFunctions Functions{
        &LookupUserActor, &ObjectReference, &MapContains};

    dfl::priest_heal_target::detail::CastTarget CaptureTarget(
        int state, const void* parameters) noexcept
    {
        __try
        {
            if (state != 10 || parameters == nullptr) return {};
            const auto vector = static_cast<const std::uintptr_t*>(parameters);
            const auto begin = vector[1];
            const auto end = vector[2];
            if (begin == 0 || end < begin || (end - begin) % sizeof(int) != 0)
                return {};
            return dfl::priest_heal_target::detail::ReadCastTarget(state,
                reinterpret_cast<const int*>(begin), (end - begin) / sizeof(int));
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return {}; }
    }

    unsigned int __fastcall StateHook(void* caster, void*, int state,
        const void* parameters, std::uint32_t a4, std::uint32_t a5)
    {
        const PendingCast previous = Pending;
        Pending = {caster, CaptureTarget(state, parameters)};
        const auto result = reinterpret_cast<StateFunction>(PriestStateFunction)(
            caster, state, parameters, a4, a5);
        Pending = previous;
        return result;
    }

    void* ExplicitSelfTarget(void* caster, void* target, int phase) noexcept
    {
        __try
        {
            if (caster == nullptr
                || *reinterpret_cast<const std::uintptr_t*>(caster) != 0x00A0515C)
                return target;
            const auto bytes = static_cast<const unsigned char*>(caster);
            const auto game = *reinterpret_cast<const unsigned char* const*>(0x00B5D6F0);
            if (game == nullptr) return target;
            using Read = int (__thiscall*)(const void*);
            const int scene = reinterpret_cast<Read>(0x00402030)(game + 16);
            const int skill = reinterpret_cast<Read>(0x00402030)(bytes + 0x2120);
            if (dfl::priest_heal_target::detail::ShouldIncludeSelfTarget(caster,
                    *reinterpret_cast<void* const*>(0x00B75A14),
                    *reinterpret_cast<void* const*>(bytes + 0x214C),
                    target, scene, phase, skill))
                return caster;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { }
        return target;
    }

    void __fastcall CompleteHook(void* caster, void*, int phase, void* target)
    {
        // 49BE80 obtains the reference through the original target.v3C() and
        // queues exactly one native state request. Preserve every other cast.
        reinterpret_cast<CompleteFunction>(CompleteCastFunction)(
            caster, phase, ExplicitSelfTarget(caster, target, phase));
    }

    void __stdcall RestoreTarget(void* caster) noexcept
    {
        __try
        {
            if (caster != Pending.Caster || !Pending.Target.Present
                || *reinterpret_cast<const std::uintptr_t*>(caster) != 0x00A0515C)
                return;
            const auto bytes = static_cast<unsigned char*>(caster);
            using Read = int (__thiscall*)(const void*);
            const auto game = *reinterpret_cast<const unsigned char* const*>(0x00B5D6F0);
            if (game == nullptr) return;
            const int scene = reinterpret_cast<Read>(0x00402030)(game + 16);
            const int skill = reinterpret_cast<Read>(0x00402030)(bytes + 0x2120);
            void* const map = *reinterpret_cast<void* const*>(bytes + 0xE4);
            void* const localActor = *reinterpret_cast<void* const*>(0x00B75A14);
            if (scene != 3 || (skill != 51 && skill != 55)
                || caster == localActor || localActor == nullptr || map == nullptr
                || !MapContains(map, caster))
                return;
            const auto casterReference = ObjectReference(caster);
            if ((casterReference >> 16) != dfl::priest_heal_target::detail::PlayerObjectType
                || LookupUserActor(static_cast<std::uint16_t>(casterReference)) != caster)
                return;
            void* target = dfl::priest_heal_target::detail::ResolveForeignTarget(
                Pending.Target, caster, localActor, map, scene, skill, Functions);
            if (target != nullptr
                && *reinterpret_cast<void* const*>(static_cast<unsigned char*>(target) + 0xE4) != map)
                target = nullptr;
            auto& nativeTarget = *reinterpret_cast<void**>(bytes + 0x214C);
            if (nativeTarget == target) return;
            // An explicit but stale reference cannot reuse a previous cast's
            // pointer. Native code skips the heal when this pointer is null.
            nativeTarget = target;
            if (InterlockedIncrement(&Repairs) <= 32)
                WriteDiagnostic(target != nullptr ? "restored-foreign-heal-target"
                    : "cleared-stale-foreign-heal-target", caster, target, Pending.Target.Reference);
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { }
    }

    __declspec(naked) void TargetHook()
    {
        __asm
        {
            pushfd
            pushad
            push esi
            call RestoreTarget
            popad
            popfd
            mov eax, dword ptr [esi + 214Ch]
            push 0062DDA0h
            ret
        }
    }

    template<std::size_t Size>
    bool Matches(std::uintptr_t address,
        const std::array<unsigned char, Size>& expected) noexcept
    {
        __try
        {
            return std::memcmp(reinterpret_cast<const void*>(address),
                expected.data(), expected.size()) == 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::priest_heal_target
{
    detail::CastTarget detail::ReadCastTarget(int state, const int* parameters,
        std::size_t count) noexcept
    {
        if (state != 10 || parameters == nullptr || count != 2 || parameters[0] != 1)
            return {};
        const auto reference = static_cast<std::uint32_t>(parameters[1]);
        if ((reference >> 16) != PlayerObjectType || (reference & 0xFFFF) == 0xFFFF)
            return {};
        return {true, reference};
    }

    bool detail::ShouldIncludeSelfTarget(void* caster, void* localActor,
        void* nativeTarget, void* requestedTarget, int scene,
        int phase, int skill) noexcept
    {
        return caster != nullptr && caster == localActor && nativeTarget == caster
            && requestedTarget == nullptr && scene == 3 && phase == 1
            && (skill == 51 || skill == 55);
    }

    void* detail::ResolveForeignTarget(const CastTarget& target, void* caster,
        void* localActor, void* map, int scene, int skill,
        const ClientFunctions& functions) noexcept
    {
        if (!target.Present || caster == nullptr || caster == localActor
            || localActor == nullptr || map == nullptr || scene != 3
            || (skill != 51 && skill != 55)
            || functions.LookupUserActor == nullptr || functions.ObjectReference == nullptr
            || functions.MapContains == nullptr || !functions.MapContains(map, caster))
            return nullptr;
        const auto casterReference = functions.ObjectReference(caster);
        if ((casterReference >> 16) != PlayerObjectType
            || functions.LookupUserActor(static_cast<std::uint16_t>(casterReference)) != caster)
            return nullptr;
        void* const resolved = functions.LookupUserActor(
            static_cast<std::uint16_t>(target.Reference));
        return resolved != nullptr && functions.ObjectReference(resolved) == target.Reference
            && functions.MapContains(map, resolved) ? resolved : nullptr;
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != ImageBase)
            return false;
        std::array<unsigned char, OriginalTargetCode.size()> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&TargetHook) - HealTargetSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        const auto hook = reinterpret_cast<std::uintptr_t>(&StateHook);
        const auto completeHook = reinterpret_cast<std::uintptr_t>(&CompleteHook);
        const auto current = *reinterpret_cast<const std::uintptr_t*>(PriestStateSlot);
        const auto currentComplete = *reinterpret_cast<const std::uintptr_t*>(CompleteCastSlot);
        if (current == hook && currentComplete == completeHook && Matches(HealTargetSite, patch))
            return true;
        constexpr std::array<unsigned char, 10> StatePrologue{
            0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0x6F, 0x38, 0x97, 0x00};
        constexpr std::array<unsigned char, 10> CompletePrologue{
            0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0xE2, 0x05, 0x96, 0x00};
        if (current != PriestStateFunction || currentComplete != CompleteCastFunction
            || !Matches(CompleteCastFunction, CompletePrologue)
            || !Matches(HealTargetSite, OriginalTargetCode)
            || !Matches(PriestStateFunction, StatePrologue))
        {
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        if (!memory_patch::WriteBytes(HealTargetSite, patch.data(), patch.size()))
            return false;
        if (!memory_patch::WriteBytes(PriestStateSlot, &hook, sizeof(hook)))
        {
            memory_patch::WriteBytes(HealTargetSite, OriginalTargetCode.data(), OriginalTargetCode.size());
            return false;
        }
        if (!memory_patch::WriteBytes(CompleteCastSlot, &completeHook, sizeof(completeHook)))
        {
            const std::uintptr_t originalState = PriestStateFunction;
            memory_patch::WriteBytes(PriestStateSlot, &originalState, sizeof(originalState));
            memory_patch::WriteBytes(HealTargetSite, OriginalTargetCode.data(), OriginalTargetCode.size());
            return false;
        }
        WriteDiagnostic("installed-62DD9A");
        return true;
    }
}
