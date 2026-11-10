param(
    [Parameter(Mandatory)][string]$AcceptedHostSource,
    [Parameter(Mandatory)][string]$RebuiltImage,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CMake = 'cmake',
    [string]$TownMapTestPvf
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AcceptedHostSource).Path
$image = (Resolve-Path -LiteralPath $RebuiltImage).Path
$townMapPvf = if ($TownMapTestPvf) { (Resolve-Path -LiteralPath $TownMapTestPvf).Path }
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
$anchor = '    auto* pebImageBase = reinterpret_cast<void**>(__readfsdword(0x30)+8);'
if (!$original.Contains($anchor)) { throw 'Missing verified game entry anchor.' }
$nl = [Environment]::NewLine
$configure = @(
    '    dfl::chapter_two::ConfigurePresentation(base, nt->OptionalHeader.SizeOfImage);',
    '    if (dfl::chapter_two::InstallPerformanceOverlay(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: FPS and memory display installed.\n");',
    '    else OutputDebugStringA("DFLegacy Chapter Two: FPS display signature mismatch.\n");',
    '    if (dfl::chapter_two::InstallPaymentProtocol(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: native payment URL installed.\n");',
    '    if (dfl::chapter_two::InstallPaymentViewport(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: native payment layout installed.\n");',
    '    if (dfl::chapter_two::InstallNpcDialogueLayout(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: NPC dialogue layout installed.\n");',
    '    if (dfl::chapter_two::InstallWarehouseUpgradeButton(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: warehouse Upgrade button installed.\n");',
    '    if (dfl::chapter_two::InstallInventoryTabPreservation(base, nt->OptionalHeader.SizeOfImage))',
    '        OutputDebugStringA("DFLegacy Chapter Two: inventory tab preservation installed.\n");',
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
    '#include "ChapterTwoNpcDialogue.h"' + $nl +
    '#include "ChapterTwoWarehouseUpgrade.h"' + $nl +
    '#include "ChapterTwoInventoryTab.h"' + $nl +
    '#include "ChapterTwoHudPortraitCache.h"' + $nl +
    '#include "ChapterTwoFreeCardDisplay.h"' + $nl +
    '#include "ChapterTwoParty.h"' + $nl +
    '#include "ChapterTwoTownMap.h"' + $nl +
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
& (Join-Path $build 'Release/chapter_two_inventory_tab_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two inventory tab tests failed.' }
& (Join-Path $build 'Release/chapter_two_hud_portrait_cache_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two HUD portrait cache tests failed.' }
& (Join-Path $build 'Release/chapter_two_performance_text_lifetime_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two performance text lifetime tests failed.' }
& (Join-Path $build 'Release/chapter_two_free_card_display_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two free card display tests failed.' }
& (Join-Path $build 'Release/chapter_two_presentation_policy_tests.exe')
if ($LASTEXITCODE) { throw 'Presentation policy tests failed.' }
& (Join-Path $build 'Release/chapter_two_party_relay_tests.exe')
if ($LASTEXITCODE) { throw 'Chapter Two party relay lookup tests failed.' }
& (Join-Path $build 'Release/chapter_two_town_map_tests.exe') $image
if ($LASTEXITCODE) { throw 'Chapter Two town map tests failed.' }
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
