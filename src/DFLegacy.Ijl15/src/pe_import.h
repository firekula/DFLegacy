#pragma once
#include "d3d8_abi.h"
#include <cstring>
#include <string_view>

namespace dfl::pe_import
{
    // Input is a mapped x86 image with a caller-verified readable extent.
    inline void** FindNamed(void* image,std::size_t bytes,const char* module,const char* symbol) noexcept
    {
        if(!image||!module||!symbol)return nullptr;
        auto* base=static_cast<unsigned char*>(image);
        const auto fits=[&](std::uint64_t offset,std::size_t count){return offset<=bytes&&count<=bytes-offset;};
        const auto text=[&](DWORD offset)->const char*
        {
            if(!fits(offset,1))return nullptr;
            return std::memchr(base+offset,0,bytes-offset)?reinterpret_cast<const char*>(base+offset):nullptr;
        };
        if(!fits(0,sizeof(IMAGE_DOS_HEADER)))return nullptr;
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||!fits(dos->e_lfanew,sizeof(IMAGE_NT_HEADERS32)))return nullptr;
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(base+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR32_MAGIC
            ||nt->OptionalHeader.NumberOfRvaAndSizes<=IMAGE_DIRECTORY_ENTRY_IMPORT)return nullptr;
        const auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if(!directory.VirtualAddress||!fits(directory.VirtualAddress,directory.Size))return nullptr;
        for(std::size_t index=0;index<directory.Size/sizeof(IMAGE_IMPORT_DESCRIPTOR);++index)
        {
            const auto* descriptor=reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base+directory.VirtualAddress)+index;
            if(!descriptor->Name)break;
            const char* const name=text(descriptor->Name);if(!name)return nullptr;
            if(_stricmp(name,module)!=0)continue;
            if(!descriptor->OriginalFirstThunk||!descriptor->FirstThunk)return nullptr;
            for(std::size_t thunk=0;thunk<bytes/sizeof(DWORD);++thunk)
            {
                const auto original=static_cast<std::uint64_t>(descriptor->OriginalFirstThunk)+thunk*sizeof(DWORD);
                const auto slot=static_cast<std::uint64_t>(descriptor->FirstThunk)+thunk*sizeof(DWORD);
                if(!fits(original,sizeof(DWORD))||!fits(slot,sizeof(DWORD)))return nullptr;
                const DWORD lookup=*reinterpret_cast<const DWORD*>(base+original);
                if(!lookup)return nullptr;
                if(lookup&IMAGE_ORDINAL_FLAG32)continue;
                if(!fits(lookup,sizeof(WORD)+1))return nullptr;
                const char* const imported=text(lookup+sizeof(WORD));if(!imported)return nullptr;
                if(std::strcmp(imported,symbol)==0)return reinterpret_cast<void**>(base+slot);
            }
            return nullptr;
        }
        return nullptr;
    }
}
