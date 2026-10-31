#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <winhttp.h>

#include <algorithm>
#include <cstddef>
#include <cstring>

#include "ijl15_config.h"
#include "../../DFLegacy.Client/client_paths.h"

namespace
{
    constexpr int BuiltinWidth = 640;
    constexpr int BuiltinHeight = 480;
    constexpr int DefaultWindowWidth = 800;
    constexpr int DefaultWindowHeight = 600;
    constexpr int MaximumWindowDimension = 8192;
    DWORD ReadAscii(const char* section,const char* key,const char*,char* output,DWORD capacity,const wchar_t* path) noexcept
    {
        wchar_t wideSection[64]{},wideKey[64]{},value[2048]{};
        if(!MultiByteToWideChar(CP_ACP,0,section,-1,wideSection,64)||!MultiByteToWideChar(CP_ACP,0,key,-1,wideKey,64))return 0;
        const auto length=GetPrivateProfileStringW(wideSection,wideKey,L"",value,2048,path);
        if(length>=capacity){output[capacity-1]=0;return capacity-1;}
        for(DWORD i=0;i<length;++i){if(value[i]>127){output[0]=0;return 0;}output[i]=static_cast<char>(value[i]);}
        output[length]=0;return length;
    }
    UINT ReadInt(const char* section,const char* key,int fallback,const wchar_t* path) noexcept
    {
        wchar_t wideSection[64]{},wideKey[64]{};
        if(!MultiByteToWideChar(CP_ACP,0,section,-1,wideSection,64)||!MultiByteToWideChar(CP_ACP,0,key,-1,wideKey,64))return fallback;
        return GetPrivateProfileIntW(wideSection,wideKey,fallback,path);
    }

    // net_dump: 0 payload bytes means "dump the whole packet"; the size cap keeps
    // an unattended client from filling the disk.
    constexpr int DefaultDumpNetworkPayloadBytes = 0;
    constexpr int DefaultDumpNetworkMaxMegabytes = 256;
    constexpr int MaximumDumpNetworkPayloadBytes = 65536;
    constexpr int MaximumDumpNetworkMaxMegabytes = 4096;

    const dfl::ijl15_config::Settings DefaultSettings{
        DefaultWindowWidth,
        DefaultWindowHeight,
        true,
        true,
        false,
        dfl::ijl15_config::ScaleFilterMode::Native,
        false,
        DefaultDumpNetworkPayloadBytes,
        DefaultDumpNetworkMaxMegabytes,
        false,
        "http://127.0.0.1:8081/client/client_pay.htm",
        "http://127.0.0.1:8081/client/client_diamond.htm"};

    dfl::ijl15_config::Settings CurrentSettings = DefaultSettings;
    bool ConfigLoaded = false;

    void ReadClientServiceUrls(const wchar_t* path) noexcept
    {
        // One extra byte beyond the accepted maximum distinguishes a truncated
        // INI read from a valid URL at the boundary. Never patch a truncated URL.
        char base[dfl::ijl15_config::MaximumClientServiceUrlLength + 2]{};
        const DWORD length = ReadAscii("Network", "ClientServiceUrl",
            "", base, static_cast<DWORD>(sizeof(base)), path);
        if (length == 0 || length > dfl::ijl15_config::MaximumClientServiceUrlLength)
            return;

        wchar_t wide[dfl::ijl15_config::MaximumClientServiceUrlLength + 1]{};
        for (DWORD i = 0; i < length; ++i)
        {
            const unsigned char value = static_cast<unsigned char>(base[i]);
            // The legacy URL builder treats this string as a format string.
            // Exclude format directives, INI injection, ambiguous separators,
            // credentials and query/fragment suffixes from the service base.
            if (value <= 32 || value >= 127 || std::strchr("%\\\"?#@", value) != nullptr)
                return;
            wide[i] = static_cast<wchar_t>(value);
        }
        if (_strnicmp(base, "http://", 7) != 0 && _strnicmp(base, "https://", 8) != 0)
            return;
        const char* authority = std::strstr(base, "://") + 3;
        if (*authority == '/' || *authority == '\0')
            return;
        URL_COMPONENTS parts{};
        parts.dwStructSize = sizeof(parts);
        parts.dwHostNameLength = static_cast<DWORD>(-1);
        parts.dwUserNameLength = static_cast<DWORD>(-1);
        parts.dwPasswordLength = static_cast<DWORD>(-1);
        if (!WinHttpCrackUrl(wide, length, 0, &parts)
            || (parts.nScheme != INTERNET_SCHEME_HTTP && parts.nScheme != INTERNET_SCHEME_HTTPS)
            || parts.dwHostNameLength == 0 || parts.dwUserNameLength != 0
            || parts.dwPasswordLength != 0 || parts.nPort == 0)
            return;

        std::size_t baseLength = length;
        while (baseLength > 0 && base[baseLength - 1] == '/')
            base[--baseLength] = '\0';
        constexpr char PaySuffix[] = "/client/client_pay.htm";
        constexpr char DiamondSuffix[] = "/client/client_diamond.htm";
        static_assert(dfl::ijl15_config::MaximumClientServiceUrlLength + sizeof(DiamondSuffix)
            <= dfl::ijl15_config::ClientPageUrlCapacity);
        std::memcpy(CurrentSettings.ClientPayUrl, base, baseLength);
        std::memcpy(CurrentSettings.ClientPayUrl + baseLength, PaySuffix, sizeof(PaySuffix));
        std::memcpy(CurrentSettings.ClientDiamondUrl, base, baseLength);
        std::memcpy(CurrentSettings.ClientDiamondUrl + baseLength, DiamondSuffix, sizeof(DiamondSuffix));
    }

    bool IsTrue(const char* value) noexcept
    {
        return _stricmp(value, "1") == 0
            || _stricmp(value, "true") == 0
            || _stricmp(value, "yes") == 0
            || _stricmp(value, "on") == 0;
    }

    bool IsFalse(const char* value) noexcept
    {
        return _stricmp(value, "0") == 0
            || _stricmp(value, "false") == 0
            || _stricmp(value, "no") == 0
            || _stricmp(value, "off") == 0;
    }

    dfl::ijl15_config::ScaleFilterMode ReadScaleFilter(
        const wchar_t* path,
        dfl::ijl15_config::ScaleFilterMode defaultValue) noexcept
    {
        char value[32]{};
        const DWORD length = ReadAscii(
            "Rendering",
            "ScaleFilter",
            "",
            value,
            static_cast<DWORD>(sizeof(value)),
            path);
        if (length == 0)
        {
            return defaultValue;
        }

        if (_stricmp(value, "native") == 0
            || _stricmp(value, "system") == 0
            || _stricmp(value, "auto") == 0
            || _stricmp(value, "off") == 0
            || std::strcmp(value, "0") == 0)
        {
            return dfl::ijl15_config::ScaleFilterMode::Native;
        }
        if (_stricmp(value, "linear") == 0
            || _stricmp(value, "bilinear") == 0
            || std::strcmp(value, "2") == 0)
        {
            return dfl::ijl15_config::ScaleFilterMode::Linear;
        }
        if (_stricmp(value, "point") == 0
            || _stricmp(value, "nearest") == 0
            || _stricmp(value, "sharp") == 0
            || std::strcmp(value, "1") == 0)
        {
            return dfl::ijl15_config::ScaleFilterMode::Point;
        }
        if (_stricmp(value, "anisotropic") == 0
            || _stricmp(value, "aniso") == 0
            || std::strcmp(value, "3") == 0)
        {
            return dfl::ijl15_config::ScaleFilterMode::Anisotropic;
        }
        return defaultValue;
    }

    bool ReadBoolean(
        const wchar_t* path,
        const char* section,
        const char* key,
        bool defaultValue) noexcept
    {
        char value[32]{};
        const DWORD length = ReadAscii(
            section,
            key,
            "",
            value,
            static_cast<DWORD>(sizeof(value)),
            path);
        if (length == 0)
        {
            return defaultValue;
        }
        if (IsTrue(value))
        {
            return true;
        }
        if (IsFalse(value))
        {
            return false;
        }
        return defaultValue;
    }

    int ReadWindowDimension(
        const wchar_t* path,
        const char* key,
        int fallback) noexcept
    {
        const int value = static_cast<int>(ReadInt(
            "Resolution",
            key,
            fallback,
            path));
        return std::clamp(value, 1, MaximumWindowDimension);
    }
}

namespace dfl::ijl15_config
{
    bool Load() noexcept
    {
        if (ConfigLoaded)
        {
            return true;
        }

        CurrentSettings = DefaultSettings;
        wchar_t path[MAX_PATH * 2]{};
        if (client_paths::BuildClientPath(L"Config.ini",path,MAX_PATH*2))
        {
            ReadClientServiceUrls(path);
            CurrentSettings.WindowWidth = ReadWindowDimension(
                path,
                "Width",
                DefaultWindowWidth);
            CurrentSettings.WindowHeight = ReadWindowDimension(
                path,
                "Height",
                DefaultWindowHeight);
            CurrentSettings.EnablePerformanceOverlay = ReadBoolean(
                path,
                "Features",
                "EnablePerformanceOverlay",
                DefaultSettings.EnablePerformanceOverlay);
            CurrentSettings.EnableMouseWheel = ReadBoolean(
                path,
                "Features",
                "EnableMouseWheel",
                DefaultSettings.EnableMouseWheel);
            CurrentSettings.EnableAutoInput = ReadBoolean(
                path,
                "Features",
                "EnableAutoInput",
                DefaultSettings.EnableAutoInput);
            CurrentSettings.EnableDebugOverlay = ReadBoolean(
                path, "Features", "EnableDebugOverlay", false);
            CurrentSettings.ExportGameplayTrace = ReadBoolean(
                path, "DebugOverlay", "ExportGameplayTrace", false);
            CurrentSettings.ExportNativeLoopProfile = ReadBoolean(
                path, "DebugOverlay", "ExportNativeLoopProfile", false);
            CurrentSettings.ScaleFilter = ReadScaleFilter(
                path,
                DefaultSettings.ScaleFilter);
            char presentation[64]{};
            ReadAscii("Rendering","PresentationFilter","",presentation,sizeof(presentation),path);
            CurrentSettings.PresentationFilter = presentation::ParseMode(presentation);
            CurrentSettings.InvalidPresentationFilter = presentation[0] != '\0'
                && CurrentSettings.PresentationFilter == presentation::Mode::Legacy
                && _stricmp(presentation,"legacy") != 0;
            CurrentSettings.DumpNetwork = ReadBoolean(
                path,
                "Features",
                "DumpNetwork",
                DefaultSettings.DumpNetwork);
            CurrentSettings.DumpNetworkPayloadBytes = std::clamp(
                static_cast<int>(ReadInt(
                    "Features",
                    "DumpNetworkPayloadBytes",
                    DefaultSettings.DumpNetworkPayloadBytes,
                    path)),
                0,
                MaximumDumpNetworkPayloadBytes);
            CurrentSettings.DumpNetworkMaxMegabytes = std::clamp(
                static_cast<int>(ReadInt(
                    "Features",
                    "DumpNetworkMaxMegabytes",
                    DefaultSettings.DumpNetworkMaxMegabytes,
                    path)),
                1,
                MaximumDumpNetworkMaxMegabytes);
        }

        // This module only stretches the 640x480 client surface. Do not
        // allow the external window size to become smaller than that surface.
        CurrentSettings.WindowWidth = std::max(
            CurrentSettings.WindowWidth,
            BuiltinWidth);
        CurrentSettings.WindowHeight = std::max(
            CurrentSettings.WindowHeight,
            BuiltinHeight);
        CurrentSettings.WindowWidth = std::min(
            CurrentSettings.WindowWidth,
            MaximumWindowDimension);
        CurrentSettings.WindowHeight = std::min(
            CurrentSettings.WindowHeight,
            MaximumWindowDimension);

        ConfigLoaded = true;
        return true;
    }

    const Settings& Get() noexcept
    {
        Load();
        return CurrentSettings;
    }
}
