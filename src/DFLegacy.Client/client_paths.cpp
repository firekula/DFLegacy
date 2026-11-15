#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <cstdio>
#include <initializer_list>
#include "client_paths.h"
namespace
{
    bool Leaf(const wchar_t* leaf) noexcept
    {
        if(!leaf)return false;const auto n=wcsnlen_s(leaf,256);
        if(!n||n==256||leaf[n-1]==L'.'||leaf[n-1]==L' ')return false;
        for(std::size_t i=0;i<n;++i)if(leaf[i]<32||wcschr(L"\\/:*?\"<>|",leaf[i]))return false;
        wchar_t stem[256]{};wcscpy_s(stem,leaf);if(auto* dot=wcschr(stem,L'.'))*dot=0;
        for(auto reserved:{L"CON",L"PRN",L"AUX",L"NUL",L"CONIN$",L"CONOUT$"})if(!_wcsicmp(stem,reserved))return false;
        if(wcslen(stem)==4&&(!_wcsnicmp(stem,L"COM",3)||!_wcsnicmp(stem,L"LPT",3))&&stem[3]>=L'0'&&stem[3]<=L'9')return false;
        return true;
    }
    bool Directory(const wchar_t* path) noexcept
    {
        if(CreateDirectoryW(path,nullptr))return true;
        const DWORD attributes=GetFileAttributesW(path);
        return attributes!=INVALID_FILE_ATTRIBUTES&&(attributes&FILE_ATTRIBUTE_DIRECTORY)!=0;
    }
}
namespace dfl::client_paths
{
    bool BuildClientPath(const wchar_t* leaf,wchar_t* output,std::size_t capacity) noexcept
    {
        if(!output||!capacity)return false;output[0]=0;if(!Leaf(leaf)||capacity>32768)return false;
        wchar_t executable[32768]{};const DWORD length=GetModuleFileNameW(nullptr,executable,32768);
        if(!length||length>=32768)return false;auto* slash=wcsrchr(executable,L'\\');if(!slash)return false;slash[1]=0;
        if(wcslen(executable)+wcslen(leaf)+1>capacity)return false;
        return swprintf_s(output,capacity,L"%s%s",executable,leaf)>0;
    }
    bool BuildLogPath(const wchar_t* leaf,wchar_t* output,std::size_t capacity) noexcept
    {
        if(!output||!capacity)return false;output[0]=0;if(!Leaf(leaf)||capacity>32768)return false;
        wchar_t directory[32768]{};if(!BuildClientPath(L"Logs",directory,32768)||!Directory(directory))return false;
        if(wcscat_s(directory,L"\\DFLegacy")||!Directory(directory))return false;
        if(wcslen(directory)+1+wcslen(leaf)+1>capacity)return false;
        return swprintf_s(output,capacity,L"%s\\%s",directory,leaf)>0;
    }
}
