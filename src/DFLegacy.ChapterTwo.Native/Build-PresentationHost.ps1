param(
    [Parameter(Mandatory)][string]$AcceptedHostSource,
    [Parameter(Mandatory)][string]$RebuiltImage,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CMake = 'cmake',
    [string]$TownMapTestPvf,
    [string]$ShopLayoutTestNpk
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AcceptedHostSource).Path
$image = (Resolve-Path -LiteralPath $RebuiltImage).Path
$townMapPvf = if ($TownMapTestPvf) { (Resolve-Path -LiteralPath $TownMapTestPvf).Path }
$shopLayoutNpkPath = if ($ShopLayoutTestNpk) { $ShopLayoutTestNpk } else {
    Join-Path (Split-Path -Parent $image) 'ImagePacks2/sprite_interface_cashshop.NPK'
}
# Layout depends on the actual Chinese resource bounds. Never publish a build
# that silently skipped the native constructor/resource regression.
$shopLayoutNpk = (Resolve-Path -LiteralPath $shopLayoutNpkPath).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Specify a new isolated output directory.' }

$expected = @{
    'fixed-loader/main.cpp' = '06C783122C05D04E9AF989D387CC18F72123FE05219D928BD3A266235709AEE2'
    'fixed-loader/LoopbackClient.h' = '3502733817BC4E69B5EB2C8F393203AF18BB0A8FB18B676129BFB996B36C5D53'
    'LoopbackWire.h' = 'D232C6AEDDCDC44F6CBD7FD15F0F33FD2E544E50BC97A369FC48F8BC80BA0600'
}
foreach ($entry in $expected.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $entry.Key) -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Unrecognized Chapter Two host source file $($entry.Key)"
    }
}
if ((Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ne '9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB') {
    throw 'Unrecognized rebuilt Chapter Two game image.'
}

$copy = Join-Path $out 'host'
$loader = Join-Path $copy 'fixed-loader'
New-Item -ItemType Directory -Path $loader | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LoopbackWire.h') -Destination $copy
Copy-Item -LiteralPath (Join-Path $source 'fixed-loader/LoopbackClient.h') -Destination $loader
$original = [IO.File]::ReadAllText((Join-Path $source 'fixed-loader/main.cpp')).Replace("`r`n", "`n")
$original = $original.Replace('#include <windows.h>', "#include <windows.h>`n#include `"ChapterTwoKeyboardTiming.h`"")
$original = $original.Replace('#include "ChapterTwoKeyboardTiming.h"', "#include `"ChapterTwoKeyboardTiming.h`"`n#include `"ChapterTwoRelaySockets.h`"")
foreach ($routing in @(
    @('const int result=sendto(socket,buffer,length,flags,address,addressLength);', 'const int result=dfl::chapter_two::RelayGameSendTo(socket,buffer,length,flags,address,addressLength);'),
    @('const int result=recvfrom(socket,buffer,length,flags,address,addressLength);', 'const int result=dfl::chapter_two::RelayGameRecvFrom(socket,buffer,length,flags,address,addressLength);')
)) {
    if (!$original.Contains($routing[0])) { throw 'Missing verified game datagram routing anchor.' }
    $original = $original.Replace($routing[0], $routing[1])
}
$rendererImport = @'
        HMODULE dll = nullptr;
        if (_stricmp(library, "d3d8.dll") == 0) {
            wchar_t bridgePath[32768]{};
            const auto count = GetEnvironmentVariableW(L"DFLEGACY_CH2_D3D11_BRIDGE", bridgePath, _countof(bridgePath));
            if (count) {
                if (count >= _countof(bridgePath) || !std::filesystem::path(bridgePath).is_absolute()) {
                    report("LOAD_IMPORT d3d8.dll invalid D3D11 bridge path\n");
                    return 8;
                }
                dll = LoadLibraryExW(bridgePath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
                if (dll) report("D3D11_BRIDGE_LOADED dgVoodoo2 2.87.6\n");
            } else {
                dll = LoadLibraryExW(L"d3d8.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            }
        } else {
            dll = LoadLibraryA(library);
        }
'@
$rendererImportAnchor = '        HMODULE dll = LoadLibraryA(library);'
if ([regex]::Matches($original, [regex]::Escape($rendererImportAnchor)).Count -ne 1) {
    throw 'Missing verified renderer import anchor.'
}
$original = $original.Replace($rendererImportAnchor, $rendererImport.Replace("`r`n", "`n"))
$original = $original.Replace('static char ipv4[4]{127,0,0,1};', 'static char ipv4[4]{127,0,0,2};')
$original = $original.Replace('static char ipv4[4]{127,0,0,2};', @'
static char ipv4[4]{127,0,0,2};
    char relayHost[16]{}; in_addr relayAddress{};
    ipv4[3]=dfl::chapter_two::ReadLocalRelayIpv4(relayHost,relayAddress)?1:2;
'@)
# The desktop process receives a per-attempt directory from the launcher, with
# the same scratch layout as before. No container profile or folder API remains.
if (!$original.Contains('L"DFL_S1C2_CONTAINER_DATA"') -or !$original.Contains('CONTAINER_DATA_UNAVAILABLE')) {
    throw 'Missing verified Chapter Two scratch directory anchors.'
}
$original = $original.Replace('L"DFL_S1C2_CONTAINER_DATA"','L"DFL_S1C2_SESSION_DATA"').Replace('CONTAINER_DATA_UNAVAILABLE','SESSION_DATA_UNAVAILABLE')
$identityStart = $original.IndexOf('static bool confined()')
$identityEnd = $original.IndexOf('static constexpr uintptr_t gameBase=', $identityStart)
if ($identityStart -lt 0 -or $identityEnd -le $identityStart -or
    !$original.Contains('REFUSED: this diagnostic host requires a capability-free AppContainer')) {
    throw 'Missing verified Chapter Two process identity anchors.'
}
$desktopIdentity = @'
static bool ordinaryDesktopProcess()
{
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    DWORD size = 0, container = 1;
    const bool result = GetTokenInformation(token, TokenIsAppContainer, &container, sizeof(container), &size)
        && size == sizeof(container) && container == 0;
    CloseHandle(token);
    return result;
}

'@
$original = $original.Remove($identityStart, $identityEnd - $identityStart).Insert($identityStart, $desktopIdentity + "`n")
$original = $original.Replace('if (!confined())', 'if (!ordinaryDesktopProcess())').Replace(
    'REFUSED: this diagnostic host requires a capability-free AppContainer',
    'REFUSED: this Chapter Two host requires an ordinary desktop process')
$original = $original.Replace('containerFolder', 'sessionFolder')
$settingsAnchor = 'if(lstrcmpiA(tail,"DNF_CHINA.cfg")==0)return profileDirectory+"\\DNF_CHINA.cfg";'
if (!$original.Contains($settingsAnchor)) { throw 'Missing verified game settings path anchor.' }
$settingsRouting = @'
if(lstrcmpiA(tail,"DNF_CHINA.cfg")==0) {
        static const std::string saved=[] {
            char path[32768]{};
            const auto size=GetEnvironmentVariableA("DFLEGACY_CH2_SETTINGS_PATH",path,_countof(path));
            return size && size<_countof(path) ? std::string(path,size) : std::string{};
        }();
        return saved.empty() ? profileDirectory+"\\DNF_CHINA.cfg" : saved;
    }
'@
$original = $original.Replace($settingsAnchor,$settingsRouting.Replace("`r`n","`n"))
$debugArchiveAnchor = '    const auto path=ProfilePath(filename);'
if (!$original.Contains($debugArchiveAnchor)) { throw 'Missing verified local file opening anchor.' }
$debugArchiveCode = @'
    // Only the debug session redirects the native resource file. The launcher
    // creates a separate checked archive and leaves Script.pvf untouched.
    if(filename && disposition==OPEN_EXISTING && (access & (GENERIC_WRITE | FILE_WRITE_DATA | FILE_APPEND_DATA)) == 0) {
        const char* tail=filename;
        for(const char* p=filename;*p;++p)if(*p=='/' || *p=='\\')tail=p+1;
        if(lstrcmpiA(tail,"Script.pvf")==0) {
            static const auto debugPath=[] {
                wchar_t enabled[4]{};
                if(GetEnvironmentVariableW(L"DFLEGACY_CH2_DEBUG_ITEMS",enabled,4)!=1 || enabled[0]!=L'1')return std::wstring{};
                std::wstring path(32768,L'\0');
                const auto size=GetEnvironmentVariableW(L"DFLEGACY_CH2_DEBUG_PVF",path.data(),32768);
                if(!size || size>=32768)return std::wstring{};
                path.resize(size);return path;
            }();
            if(!debugPath.empty())
                return CreateFileW(debugPath.c_str(),access,sharing,attributes,disposition,flags,templateFile);
        }
    }
'@
# ProfilePath is also used by attribute probes; insert only inside CreateFile.
$debugOpenAnchor = "{`n" + $debugArchiveAnchor + "`n    const auto file=CreateFileA"
if (!$original.Contains($debugOpenAnchor)) { throw 'Missing verified native CreateFile body.' }
$original = $original.Replace($debugOpenAnchor, "{`n" + $debugArchiveCode.Replace("`r`n", "`n") + "`n" + $debugArchiveAnchor + "`n    const auto file=CreateFileA")
$originalPartyRelay = @'
    if(name && std::strcmp(name,"127.0.0.2")==0){
        static char isolatedName[]="127.0.0.2";
        static char isolatedIp[4]{127,0,0,2};
        static char* isolatedAddresses[]{isolatedIp,nullptr};
        static hostent isolated{isolatedName,aliases,AF_INET,4,isolatedAddresses};
        report("GAME_HOST_LOOKUP loopback 127.0.0.2\n");
        return &isolated;
    }
'@
$originalPartyRelay = $originalPartyRelay.Replace("`r`n", "`n")
if (!$original.Contains($originalPartyRelay)) { throw 'Missing verified game party relay lookup.' }
$localPartyRelay = @'
    if(auto* relay=dfl::chapter_two::ResolveLocalPartyRelayHost(name)){
        report("GAME_HOST_LOOKUP loopback %s\n", relay->h_name);
        return relay;
    }
'@
$original = $original.Replace($originalPartyRelay, $localPartyRelay)
$peerLookupAnchor = '    static char loopbackName[]="localhost";'
$peerLookup = @'
    if(auto* peer=dfl::chapter_two::ResolveGamePeerHost(name,reinterpret_cast<uintptr_t>(_ReturnAddress()))){
        report("GAME_HOST_LOOKUP peer %s\n", peer->h_name);
        return peer;
    }
'@
if (!$original.Contains($peerLookupAnchor)) { throw 'Missing verified game peer lookup.' }
$original = $original.Replace($peerLookupAnchor, $peerLookup.Replace("`r`n", "`n") + "`n" + $peerLookupAnchor)
$channelReset = 'if(port==47001 || (port>=7100 && port<=7105))InterlockedExchange(&resetChannelCipher,1);'
if (!$original.Contains($channelReset)) { throw 'Missing verified channel reset anchor.' }
$original = $original.Replace($channelReset, 'if(dfl::chapter_two::IsTownChannelPort(port) || dfl::chapter_two::IsPvpPort(port))InterlockedExchange(&resetChannelCipher,1);')
$pvpExitAnchor = 'static int WSAAPI GameBind(SOCKET socket,const sockaddr* address,int length)'
if (!$original.Contains($pvpExitAnchor)) { throw 'Missing verified game socket hook section.' }
$pvpExitHook = @'
static void __fastcall CloseAndResumeChannelSwitch(unsigned char* network, void*)
{
    using NativeRoutine = void(__thiscall*)(void*);
    using ReadSecure = int(__thiscall*)(void*);
    // This is the successful EXIT path in the channel transition scene.
    // A close performed here can consume the native FD_CLOSE before it arms
    // the reconnect timer for the selected town or duel channel.
    reinterpret_cast<NativeRoutine>(0x009cc190)(network);
    const auto* game = *reinterpret_cast<unsigned char**>(0x00d564ac);
    if (!game || network != *reinterpret_cast<unsigned char**>(0x00dfcc98)
        || *reinterpret_cast<const SOCKET*>(network+0x1b4) != INVALID_SOCKET) return;

    auto* channel = *reinterpret_cast<unsigned char* const*>(game+0x2c);
    const auto read = reinterpret_cast<ReadSecure>(0x00a46ab0);
    if (!channel || read(channel+0x2b4) != 0 || read(channel+0x2f4) != 0) return;
    const int group = *reinterpret_cast<const int*>(channel+0x290);
    const int category = *reinterpret_cast<const int*>(channel+0x298);
    const int row = *reinterpret_cast<const int*>(channel+0x2a0);
    if (group < 0 || group >= 109 || (category != 0 && category != 2 && category != 4) || row < 0) return;

    const auto* table = channel+0x2044+group*0xcc+category*0x10;
    const auto begin = *reinterpret_cast<const uintptr_t*>(table);
    const auto end = *reinterpret_cast<const uintptr_t*>(table+4);
    const auto capacity = *reinterpret_cast<const uintptr_t*>(table+8);
    constexpr size_t recordSize = 104;
    if (!begin || end <= begin || capacity < end || (end-begin)%recordSize
        || static_cast<size_t>(row) >= (end-begin)/recordSize) return;

    const auto* record = reinterpret_cast<const unsigned char*>(begin+row*recordSize);
    const int port = *reinterpret_cast<const int*>(record+0x54);
    char host[16]{};
    in_addr remote{}, dial{};
    if (port < 1 || port > 65535 || !dfl::chapter_two::IsChannelSwitchPort(category, static_cast<unsigned short>(port))
        || record[0x58] == 1 || !std::memchr(record+0x40,0,16)
        || InetPtonA(AF_INET,reinterpret_cast<const char*>(record+0x40),&dial) != 1) return;
    const bool localRelay = dfl::chapter_two::ReadLocalRelayIpv4(host,remote);
    if (localRelay ? !dfl::chapter_two::IsLocalGameAddress(dial)
        : (!dfl::chapter_two::ReadRemoteIpv4(host,remote) || remote.s_addr != dial.s_addr)) return;
    reinterpret_cast<NativeRoutine>(0x009a5df0)(channel);
}

'@
$original = $original.Replace($pvpExitAnchor, $pvpExitHook.Replace("`r`n", "`n") + "`n" + $pvpExitAnchor)
$pvpInstallAnchor = '        *reinterpret_cast<DWORD*>(base+0x6ea53c)=reinterpret_cast<DWORD>(GameSend);'
if (!$original.Contains($pvpInstallAnchor)) { throw 'Missing verified remote duel patch section.' }
$pvpInstall = @'
        char duelHost[16]{};
        in_addr duelAddress{};
        if (dfl::chapter_two::ReadRemoteIpv4(duelHost,duelAddress) || dfl::chapter_two::ReadLocalRelayIpv4(duelHost,duelAddress)) {
            constexpr unsigned char exitScene[]{0x83,0xf8,0x05,0x75,0x10,0x8b,0x0d,0x98,0xcc,0xdf,0x00,0xe8,0xfc,0xb9,0x5b,0x00};
            constexpr unsigned char armRoutine[]{0x83,0xb9,0xa0,0x02,0x00,0x00,0xff,0x74,0x72};
            if (std::memcmp(base+0x10784,exitScene,sizeof(exitScene)) ||
                std::memcmp(base+0x5a5df0,armRoutine,sizeof(armRoutine))) {
                report("REMOTE_DUEL_TRANSITION_SIGNATURE_MISMATCH\n");
                return 46;
            }
            auto* call = base+0x1078f;
            const DWORD displacement = reinterpret_cast<DWORD>(CloseAndResumeChannelSwitch)
                - reinterpret_cast<DWORD>(call+5);
            std::memcpy(call+1,&displacement,sizeof(displacement));
        }
'@
$original = $original.Replace($pvpInstallAnchor, $pvpInstall.Replace("`r`n", "`n") + "`n" + $pvpInstallAnchor)
$timedInput = @(
    @{
        Site = 'struct WindowKey { bool down; ULONGLONG until; };'
        Replacement = 'struct WindowKey { bool down; bool pending; };'
    },
    @{
        Site = '    if(message==WM_KEYDOWN || message==WM_KEYUP){'
        Code = '    const dfl::chapter_two::performance::FrameStageScope timing(dfl::chapter_two::performance::ClassifyKeyMessage(message,key,details));'
    },
    @{
        Site = '        default: if(!scan)scan=MapVirtualKeyW(static_cast<UINT>(key),MAPVK_VK_TO_VSC);break;'
        Replacement = '        default: if(!scan)scan=MapVirtualKeyW(imeKey ? imeKey : static_cast<UINT>(key),MAPVK_VK_TO_VSC);break;'
    },
    @{
        Site = '        const auto flags=static_cast<DWORD>(details);'
        Code = '        const auto imeKey=message==WM_KEYDOWN && key==VK_PROCESSKEY ? dfl::chapter_two::performance::TakeImeGameplayKey(window,details) : 0u;'
    },
    @{
        Site = '        if(scan && scan<256){'
        Replacement = @'
        if(GetForegroundWindow()!=window){
            std::memset(windowKeys,0,sizeof(windowKeys));
        }else if(scan && scan<256 && (key!=VK_PROCESSKEY || imeKey)){
'@
    },
    @{
        Site = "            state.down=message==WM_KEYDOWN;`n            if(state.down)state.until=GetTickCount64()+60;"
        Replacement = @'
            state.down=message==WM_KEYDOWN && !imeKey;
            if(message==WM_KEYDOWN && !(flags&0x40000000))state.pending=true;
            if(dfl::chapter_two::performance::IsKeyboardTextEntryActive())std::memset(windowKeys,0,sizeof(windowKeys));
'@
    },
    @{
        Site = '        const auto now=GetTickCount64();'
        Replacement = @'
        if(dfl::chapter_two::performance::IsKeyboardTextEntryActive()){
            std::memset(windowKeys,0,sizeof(windowKeys));
            return status;
        }
        // A message tap missed between polls is sampled once. Observed releases remain immediate.
'@
    },
    @{
        Site = '            if(GetAsyncKeyState(arrow.vk)&0x8000)windowKeys[arrow.scan].until=now+60;'
        Replacement = '            if(GetAsyncKeyState(arrow.vk)&0x8000)keys[arrow.scan]|=0x80;'
    },
    @{
        Site = '            if(GetAsyncKeyState(vk)&0x8000)windowKeys[MapVirtualKeyW(vk,MAPVK_VK_TO_VSC)].until=now+60;'
        Replacement = @'
            if(GetAsyncKeyState(vk)&0x8000){
                const auto scan=MapVirtualKeyW(vk,MAPVK_VK_TO_VSC);
                if(scan && scan<256)keys[scan]|=0x80;
            }
'@
    },
    @{
        Site = '            if(key.down || key.until>now)keys[scan]|=0x80;'
        Replacement = @'
            if(key.down || key.pending)keys[scan]|=0x80;
            key.pending=false;
'@
    },
    @{
        Site = '            const auto& key=windowKeys[scan];'
        Replacement = '            auto& key=windowKeys[scan];'
    },
    @{
        Site = '    const HRESULT status=poll(device,size,state);'
        Replacement = '    const HRESULT status=dfl::chapter_two::performance::MeasureKeyboardPoll(poll,device,size,state);'
    },
    @{
        Site = '    const HRESULT status=dfl::chapter_two::performance::MeasureKeyboardPoll(poll,device,size,state);'
        Append = '    if(keyboardWindow && GetForegroundWindow()!=keyboardWindow)std::memset(windowKeys,0,sizeof(windowKeys));'
    },
    @{
        Site = '       GetWindowThreadProcessId(keyboardWindow,nullptr)==GetCurrentThreadId()){'
        Append = '        const dfl::chapter_two::performance::FrameStageScope timing(dfl::chapter_two::performance::FrameStage::KeyboardMerge);'
    }
)
foreach ($site in $timedInput) {
    if (!$original.Contains($site.Site)) { throw 'Missing verified game input timing anchor.' }
    $replacement = if ($site.Replacement) { $site.Replacement }
        elseif ($site.Code) { $site.Code + "`n" + $site.Site }
        else { $site.Site + "`n" + $site.Append }
    $original = $original.Replace($site.Site, $replacement)
}
$anchor = '    auto* pebImageBase = reinterpret_cast<void**>(__readfsdword(0x30)+8);'
if (!$original.Contains($anchor)) { throw 'Missing verified game entry anchor.' }
$nl = [Environment]::NewLine
$configure = @(
    '    if (!dfl::chapter_two::InstallStartupMutex(base, nt->OptionalHeader.SizeOfImage))',
    '    {',
    '        report("DFLegacy Chapter Two: startup mutex isolation initialization failed.\n");',
    '        return 42;',
    '    }',
    '    OutputDebugStringA("DFLegacy Chapter Two: startup mutex isolation installed.\n");',
    '    if (!dfl::chapter_two::InstallLocalPaths(base, nt->OptionalHeader.SizeOfImage))',
    '    {',
    '        report("DFLegacy Chapter Two: local session path initialization failed.\n");',
    '        return 41;',
    '    }',
    '    OutputDebugStringA("DFLegacy Chapter Two: native local session paths installed.\n");',
    '    dfl::chapter_two::ConfigurePresentation(base, nt->OptionalHeader.SizeOfImage);',
    '    OutputDebugStringA(dfl::chapter_two::InstallExternalStringTable(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: external string table installed.\n"',
    '        : "DFLegacy Chapter Two: external string table signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallKeyboardMessageTiming(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: keyboard message hooks installed.\n"',
    '        : "DFLegacy Chapter Two: keyboard message hook signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallImeComposition(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: IME composition guards installed.\n"',
    '        : "DFLegacy Chapter Two: IME composition signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallBackgroundPolicy(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: background policy installed.\n"',
    '        : "DFLegacy Chapter Two: background policy signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallStartupWindowPolicy(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: startup window policy installed.\n"',
    '        : "DFLegacy Chapter Two: startup window policy signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallMouseWheel(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native list wheel installed.\n"',
    '        : "DFLegacy Chapter Two: native list wheel signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallNativeLog(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native local logs installed.\n"',
    '        : "DFLegacy Chapter Two: native local logs unavailable.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallType16Audio(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: local notification audio installed.\n"',
    '        : "DFLegacy Chapter Two: local notification audio signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallPopupAnnouncement(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: local popup body installed.\n"',
    '        : "DFLegacy Chapter Two: local popup body signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallShopLayout(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: Chinese shop tab layout installed.\n"',
    '        : "DFLegacy Chapter Two: Chinese shop tab layout signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCountdownCoin(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: countdown coin receipt installed.\n"',
    '        : "DFLegacy Chapter Two: countdown coin receipt signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallDungeonTooltip(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: local dungeon tooltip cleanup installed.\n"',
    '        : "DFLegacy Chapter Two: local dungeon tooltip cleanup signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCemeteryGuard(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: Cemetery gate synchronization installed.\n"',
    '        : "DFLegacy Chapter Two: Cemetery gate synchronization signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallAdultBlood(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native adult blood selection installed.\n"',
    '        : "DFLegacy Chapter Two: native adult blood selection signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallPerformanceOverlay(base, nt->OptionalHeader.SizeOfImage))',
    '    {',
    '        const bool lootGatherInstalled = dfl::chapter_two::InstallLootGather(base, nt->OptionalHeader.SizeOfImage);',
    '        OutputDebugStringA(lootGatherInstalled',
    '            ? "DFLegacy Chapter Two: solo loot gather installed.\n"',
    '            : "DFLegacy Chapter Two: solo loot gather signature mismatch.\n");',
    '        OutputDebugStringA(lootGatherInstalled && dfl::chapter_two::InstallDungeonRating(base, nt->OptionalHeader.SizeOfImage)',
    '            ? "DFLegacy Chapter Two: dungeon rating hooks installed.\n"',
    '            : "DFLegacy Chapter Two: dungeon rating hooks signature mismatch.\n");',
    '        const bool timingInstalled = dfl::chapter_two::InstallSlowFrameTiming(base, nt->OptionalHeader.SizeOfImage);',
    '        dfl::chapter_two::StartPerformancePanel(timingInstalled);',
    '        OutputDebugStringA(timingInstalled ? "DFLegacy Chapter Two: slow-frame timing installed.\n" :',
    '            "DFLegacy Chapter Two: slow-frame timing signature mismatch.\n");',
    '        OutputDebugStringA("DFLegacy Chapter Two: FPS and memory display installed.\n");',
    '    }',
    '    else OutputDebugStringA("DFLegacy Chapter Two: FPS display signature mismatch.\n");',
    '    if (!dfl::chapter_two::InstallPaymentProtocol(base, nt->OptionalHeader.SizeOfImage))',
    '    {',
    '        report("LOCAL_PAYMENT_INITIALIZATION_FAILED\n");',
    '        return 40;',
    '    }',
    '    OutputDebugStringA("DFLegacy Chapter Two: native payment URL installed.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallGuildNavigation(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native guild website navigation installed.\n"',
    '        : "DFLegacy Chapter Two: guild website navigation signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallPaymentViewport(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: native payment layout installed.\n");',
    '    if (dfl::chapter_two::InstallNpcDialogueLayout(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: NPC dialogue layout installed.\n");',
    '    if (dfl::chapter_two::InstallWarehouseUpgradeButton(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: warehouse Upgrade button installed.\n");',
    '    if (dfl::chapter_two::InstallInventoryTabPreservation(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: inventory tab preservation installed.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCreatureTabs(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native creature tabs installed.\n"',
    '        : "DFLegacy Chapter Two: native creature tabs signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallJarCapacity(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: reward-aware jar capacity installed.\n"',
    '        : "DFLegacy Chapter Two: jar capacity signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallJarFeedback(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: jar weight feedback installed.\n"',
    '        : "DFLegacy Chapter Two: jar weight feedback signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallStoreTitle(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: blank store title guard installed.\n"',
    '        : "DFLegacy Chapter Two: blank store title guard signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallQuickStore(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: warehouse quick store installed.\n"',
    '        : "DFLegacy Chapter Two: warehouse quick store signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCashItemUse(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: supported cash item use installed.\n"',
    '        : "DFLegacy Chapter Two: cash item use signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCashPackageCheckout(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: contract package checkout installed.\n"',
    '        : "DFLegacy Chapter Two: contract package checkout signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallMailboxRefresh(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: mailbox refresh installed.\n"',
    '        : "DFLegacy Chapter Two: mailbox refresh signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallMallCreature(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: native creature shop tab installed.\n"',
    '        : "DFLegacy Chapter Two: native creature shop tab signature mismatch.\n");',
    '    OutputDebugStringA(dfl::chapter_two::InstallCursorAuto(base, nt->OptionalHeader.SizeOfImage)',
    '        ? "DFLegacy Chapter Two: cursor auto result guards installed.\n"',
    '        : "DFLegacy Chapter Two: cursor auto signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallHudPortraitCache(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: local HUD portrait cache patch installed.\n");',
    '    else OutputDebugStringA("DFLegacy Chapter Two: HUD portrait cache signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallFreeCardDisplay(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: local free card display patch installed.\n");',
    '    else OutputDebugStringA("DFLegacy Chapter Two: free card display signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallTownMapMovement(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: town map right-click movement installed.\n");',
    $anchor
) -join $nl
$integrated = '#include "ChapterTwoPresentation.h"' + $nl + '#include "ChapterTwoPerformance.h"' + $nl +
    '#include "ChapterTwoPaymentProtocol.h"' + $nl + '#include "ChapterTwoPaymentViewport.h"' + $nl +
    '#include "ChapterTwoGuildNavigation.h"' + $nl +
    '#include "ChapterTwoNpcDialogue.h"' + $nl +
    '#include "ChapterTwoWarehouseUpgrade.h"' + $nl +
    '#include "ChapterTwoInventoryTab.h"' + $nl +
    '#include "ChapterTwoCreatureTabs.h"' + $nl +
    '#include "ChapterTwoExternalStringTable.h"' + $nl +
    '#include "ChapterTwoMailboxRefresh.h"' + $nl +
    '#include "ChapterTwoJarCapacity.h"' + $nl +
    '#include "ChapterTwoJarFeedback.h"' + $nl +
    '#include "ChapterTwoStoreTitle.h"' + $nl +
    '#include "ChapterTwoQuickStore.h"' + $nl +
    '#include "ChapterTwoCashItemUse.h"' + $nl +
    '#include "ChapterTwoMallCreature.h"' + $nl +
    '#include "ChapterTwoCursorAuto.h"' + $nl +
    '#include "ChapterTwoHudPortraitCache.h"' + $nl +
    '#include "ChapterTwoFreeCardDisplay.h"' + $nl +
    '#include "ChapterTwoParty.h"' + $nl +
    '#include "ChapterTwoRemoteEndpoint.h"' + $nl +
    '#include "ChapterTwoTownMap.h"' + $nl +
    '#include "ChapterTwoLootGather.h"' + $nl +
    '#include "ChapterTwoDungeonRating.h"' + $nl +
    '#include "ChapterTwoBackgroundPolicy.h"' + $nl +
    '#include "ChapterTwoMouseWheel.h"' + $nl +
    '#include "ChapterTwoNativeLog.h"' + $nl +
    '#include "ChapterTwoLocalPaths.h"' + $nl +
    '#include "ChapterTwoStartupMutex.h"' + $nl +
    '#include "ChapterTwoType16Audio.h"' + $nl +
    '#include "ChapterTwoPopupAnnouncement.h"' + $nl +
    '#include "ChapterTwoShopLayout.h"' + $nl +
    '#include "ChapterTwoCountdownCoin.h"' + $nl +
    '#include "ChapterTwoImeComposition.h"' + $nl +
    '#include "ChapterTwoDungeonTooltip.h"' + $nl +
    '#include "ChapterTwoCemeteryGuard.h"' + $nl +
    '#include "ChapterTwoAdultBlood.h"' + $nl +
    $original.Replace($anchor, $configure)
$main = Join-Path $loader 'main.cpp'
[IO.File]::WriteAllText($main, $integrated)

$build = Join-Path $out 'build'
& $CMake -S $PSScriptRoot -B $build -G 'Visual Studio 18 2026' -A Win32 "-DCHAPTER_TWO_HOST_SOURCE=$main"
if ($LASTEXITCODE) { throw 'CMake configuration failed.' }
& $CMake --build $build --config Release
if ($LASTEXITCODE) { throw 'Chapter Two native build failed.' }
$map = Get-Content -LiteralPath (Join-Path $build 'Release/chapter_two_presentation_host.map') -Raw
$tlsStart = [regex]::Match($map, '(?im)^\s+[0-9a-f]+:[0-9a-f]+\s+__tls_start\s+([0-9a-f]+)\s+')
$gameTls = [regex]::Match($map, '(?im)^\s+[0-9a-f]+:[0-9a-f]+\s+\?gameTls@@3PAEA\s+([0-9a-f]+)\s+')
if (!$tlsStart.Success -or !$gameTls.Success -or
    [Convert]::ToInt64($gameTls.Groups[1].Value, 16) - [Convert]::ToInt64($tlsStart.Groups[1].Value, 16) -ne 8) {
    throw 'The rebuilt host does not preserve the required game TLS layout.'
}
& (Join-Path $build 'Release/chapter_two_presentation_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two image adapter tests failed.' }
& (Join-Path $build 'Release/chapter_two_startup_mutex_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two startup mutex tests failed.' }
& (Join-Path $build 'Release/chapter_two_performance_window_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two performance window tests failed.' }
& (Join-Path $build 'Release/chapter_two_inventory_tab_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two inventory tab tests failed.' }
& (Join-Path $build 'Release/chapter_two_creature_tab_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native creature tab tests failed.' }
& (Join-Path $build 'Release/chapter_two_external_string_table_tests.exe') $image (Join-Path $out 'external-string-table-fixture')
if ($LASTEXITCODE) { throw 'Chapter Two external string table tests failed.' }
& (Join-Path $build 'Release/chapter_two_jar_capacity_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two jar capacity checks failed.' }
& (Join-Path $build 'Release/chapter_two_jar_feedback_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two jar feedback tests failed.' }
& (Join-Path $build 'Release/chapter_two_store_title_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two blank store title checks failed.' }
& (Join-Path $build 'Release/chapter_two_quick_store_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two warehouse quick store checks failed.' }
& (Join-Path $build 'Release/chapter_two_cash_item_use_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two supported cash item use checks failed.' }
& (Join-Path $build 'Release/chapter_two_mailbox_refresh_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two mailbox refresh tests failed.' }
& (Join-Path $build 'Release/chapter_two_mall_creature_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native creature shop tests failed.' }
& (Join-Path $build 'Release/chapter_two_countdown_coin_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two countdown coin receipt tests failed.' }
& (Join-Path $build 'Release/chapter_two_ime_composition_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two IME composition tests failed.' }
& (Join-Path $build 'Release/chapter_two_cursor_auto_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two cursor auto tests failed.' }
& (Join-Path $build 'Release/chapter_two_payment_protocol_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two local payment routing tests failed.' }
& (Join-Path $build 'Release/chapter_two_guild_navigation_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native guild website navigation tests failed.' }
& (Join-Path $build 'Release/chapter_two_hud_portrait_cache_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two HUD portrait cache tests failed.' }
& (Join-Path $build 'Release/chapter_two_hud_portrait_fallback_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two private-room HUD portrait fallback tests failed.' }
& (Join-Path $build 'Release/chapter_two_performance_text_lifetime_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two performance text lifetime tests failed.' }
& (Join-Path $build 'Release/chapter_two_keyboard_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two keyboard state tests failed.' }
& (Join-Path $build 'Release/chapter_two_xbrz_frame_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two xBRZ frame tests failed.' }
& (Join-Path $build 'Release/chapter_two_free_card_display_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two free card display tests failed.' }
& (Join-Path $build 'Release/chapter_two_presentation_policy_tests.exe')
if ($LASTEXITCODE) { throw 'Presentation policy tests failed.' }
& (Join-Path $build 'Release/chapter_two_party_relay_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two party relay lookup tests failed.' }
& (Join-Path $build 'Release/chapter_two_relay_endpoint_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two local relay endpoint tests failed.' }
& (Join-Path $build 'Release/chapter_two_town_map_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two town map tests failed.' }
& (Join-Path $build 'Release/chapter_two_loot_gather_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two solo loot gather tests failed.' }
& (Join-Path $build 'Release/chapter_two_loot_gather_image_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two solo loot gather image tests failed.' }
& (Join-Path $build 'Release/chapter_two_loot_gather_hook_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two solo loot gather hook tests failed.' }
& (Join-Path $build 'Release/chapter_two_background_policy_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two background and startup policy tests failed.' }
& (Join-Path $build 'Release/chapter_two_mouse_wheel_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native list wheel tests failed.' }
& (Join-Path $build 'Release/chapter_two_native_log_tests.exe') $image (Join-Path $out 'native-log-fixture')
if ($LASTEXITCODE) { throw 'Chapter Two native local log tests failed.' }
& (Join-Path $build 'Release/chapter_two_local_path_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native session path tests failed.' }
& (Join-Path $build 'Release/chapter_two_type16_audio_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two notification audio tests failed.' }
& (Join-Path $build 'Release/chapter_two_popup_announcement_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two popup body tests failed.' }
& (Join-Path $build 'Release/chapter_two_shop_layout_tests.exe') $image $shopLayoutNpk
if ($LASTEXITCODE) { throw 'Chapter Two Chinese shop tab layout tests failed.' }
& (Join-Path $build 'Release/chapter_two_dungeon_tooltip_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native dungeon tooltip tests failed.' }
& (Join-Path $build 'Release/chapter_two_adult_blood_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two native blood selection tests failed.' }
if ($townMapPvf) {
    $fixture = Join-Path $out 'town-map-fixture'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Copy-Item -LiteralPath $townMapPvf -Destination (Join-Path $fixture 'Script.pvf')
    $fixtureTests = Join-Path $fixture 'chapter_two_town_map_tests.exe'
    Copy-Item -LiteralPath (Join-Path $build 'Release/chapter_two_town_map_tests.exe') -Destination $fixtureTests
    & $fixtureTests $image --cross
    if ($LASTEXITCODE) { throw 'Chapter Two cross-area town map tests failed.' }
}
Get-FileHash -LiteralPath (Join-Path $build 'Release/chapter_two_presentation_host.exe') -Algorithm SHA256
