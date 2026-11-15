#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // The official launcher copies the executing host into the Game directory.
    // Plugin/dnf.str is relative to that executable, not the process CWD.
    bool InstallExternalStringTable(unsigned char* image,std::size_t bytes) noexcept;

    namespace external_string_table_detail
    {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        using ResolveClientPath=const char*(__cdecl*)(const char*);
        using BuiltinExtractor=int(__cdecl*)(const char*);
        int ExtractForExecutable(const wchar_t* executable,const char* destination,
            ResolveClientPath resolve,BuiltinExtractor builtin) noexcept;
        bool InstallImage(unsigned char* image,std::size_t bytes,
            std::uintptr_t hook,PatchWriter write) noexcept;
    }
}
