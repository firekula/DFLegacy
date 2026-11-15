#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <string>

namespace dfl::chapter_two
{
bool InstallStartupMutex(unsigned char* image, std::size_t bytes) noexcept;

namespace startup_mutex_detail
{
using CreateMutexFunction = HANDLE (WINAPI*)(LPSECURITY_ATTRIBUTES, BOOL, LPCSTR);
using PatchWriter = bool (*)(std::uintptr_t, const void*, std::size_t);
struct Context
{
    bool ordinaryDesktop = false;
    wchar_t instance = 0;
    std::wstring modulePath, sessionPath;
    DWORD volume = 0, fileHigh = 0, fileLow = 0;
};
bool MakeName(const Context& context, char* destination, std::size_t capacity) noexcept;
HANDLE CreateForName(const char* scopedName, CreateMutexFunction original,
    LPSECURITY_ATTRIBUTES attributes, BOOL initialOwner, LPCSTR name) noexcept;
bool InstallImage(unsigned char* image, std::size_t bytes, const Context& context,
    CreateMutexFunction original, PatchWriter write) noexcept;
}
}
