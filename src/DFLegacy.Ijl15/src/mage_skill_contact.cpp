#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "mage_skill_contact.h"
#include "memory_patch.h"

namespace
{
    constexpr std::uintptr_t ImageBase = 0x00400000;
    constexpr std::uintptr_t ContactSite = 0x0060E301;
    constexpr std::uintptr_t ContinueContact = 0x0060E307;
    constexpr std::uintptr_t CleanupContact = 0x0060E37C;
    constexpr std::array<unsigned char, 6> OriginalContactCode{
        0x8B, 0x16, 0x8B, 0xCE, 0x8B, 0xD8};
    volatile LONG InvalidContactDiagnostics = 0;

    void WriteDiagnostic(const char* action, const void* actor = nullptr,
        const void* skill = nullptr, int learnedLevel = 0) noexcept
    {
        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return;
        DWORD filename = length;
        while (filename != 0 && path[filename - 1] != L'\\') --filename;
        if (_snwprintf_s(path + filename, MAX_PATH - filename, _TRUNCATE,
                L"mage-skill-contact-%lu.log", GetCurrentProcessId()) < 0) return;
        const HANDLE file = CreateFileW(path, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME time{};
        GetLocalTime(&time);
        char line[256]{};
        const int size = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%04u-%02u-%02u %02u:%02u:%02u.%03u T=%lu %s actor=%p skill20=%p level=%d\r\n",
            time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute,
            time.wSecond, time.wMilliseconds, GetCurrentThreadId(),
            action, actor, skill, learnedLevel);
        DWORD written = 0;
        if (size > 0) WriteFile(file, line, static_cast<DWORD>(size), &written, nullptr);
        CloseHandle(file);
    }

    std::uintptr_t __stdcall SelectContactContinuation(const void* actor,
        const void* skill, int learnedLevel) noexcept
    {
        if (dfl::mage_skill_contact::detail::CanEvaluateContactSkill(skill, learnedLevel))
            return ContinueContact;
        if (InterlockedIncrement(&InvalidContactDiagnostics) <= 16)
            WriteDiagnostic("skipped-missing-skill20", actor, skill, learnedLevel);
        return CleanupContact;
    }

    __declspec(naked) void ContactHook()
    {
        __asm
        {
            // v508 has returned the learned level in EAX; EDI holds v51C's
            // template, ESI the Mage. Preserve every incoming register/flag.
            pushfd
            pushad
            push eax
            push edi
            push esi
            call SelectContactContinuation
            cmp eax, 0060E307h
            jne missing_skill
            popad
            popfd
            mov edx, dword ptr [esi]
            mov ecx, esi
            mov ebx, eax
            push 0060E307h
            ret

        missing_skill:
            popad
            popfd
            // var_38 is already constructed and EBX was pushed at 60E2E8.
            // The native cleanup destroys it, pops EBX/EDI/ESI, restores FS
            // and returns 0 with ret 0Ch. Never bypass that lifetime boundary.
            push 0060E37Ch
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
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }
}

namespace dfl::mage_skill_contact
{
    bool detail::CanEvaluateContactSkill(const void* skill, int learnedLevel) noexcept
    {
        return skill != nullptr && learnedLevel > 0;
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != ImageBase)
            return false;
        std::array<unsigned char, OriginalContactCode.size()> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&ContactHook) - ContactSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        if (Matches(ContactSite, patch)) return true;
        constexpr std::array<unsigned char, 12> SkillLevelCall{
            0x8B, 0x06, 0x6A, 0x14, 0x8B, 0xCE, 0xFF, 0x90, 0x08, 0x05, 0x00, 0x00};
        constexpr std::array<unsigned char, 6> FollowingActorCall{
            0xFF, 0x92, 0x98, 0x01, 0x00, 0x00};
        constexpr std::array<unsigned char, 8> Cleanup{
            0x8D, 0x4D, 0xC8, 0xE8, 0xEC, 0x73, 0xF4, 0xFF};
        constexpr std::array<unsigned char, 13> SkillActionGate{
            0x83, 0xBE, 0xD8, 0x26, 0x00, 0x00, 0x14,
            0x0F, 0x85, 0xEA, 0x01, 0x00, 0x00};
        if (!Matches(ContactSite, OriginalContactCode)
            || !Matches(0x0060E2F5, SkillLevelCall)
            || !Matches(ContinueContact, FollowingActorCall)
            || !Matches(CleanupContact, Cleanup)
            || !Matches(0x0060E267, SkillActionGate))
        {
            WriteDiagnostic("skipped-byte-mismatch");
            return false;
        }
        if (!memory_patch::WriteBytes(ContactSite, patch.data(), patch.size()))
        {
            memory_patch::WriteBytes(ContactSite,
                OriginalContactCode.data(), OriginalContactCode.size());
            WriteDiagnostic("install-failed");
            return false;
        }
        WriteDiagnostic("installed-60E301");
        return true;
    }
}
