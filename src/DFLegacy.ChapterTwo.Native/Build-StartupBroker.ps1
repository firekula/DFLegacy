param(
    [Parameter(Mandatory)][string]$AcceptedBrokerSource,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CMake = 'cmake'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AcceptedBrokerSource).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Specify a new isolated output directory.' }
$expected = @{
    'container/trace.cpp' = '7DA9CA0C03E8A32F65CFBB007FAE2724A3CEF426887C6C03537BFBDA07518891'
    'container/LoopbackBroker.h' = '8B7D9123315FD68F007AFE47AD57ADC8B4149DEF9547994EC2C683F50D1B5345'
    'LoopbackWire.h' = 'D232C6AEDDCDC44F6CBD7FD15F0F33FD2E544E50BC97A369FC48F8BC80BA0600'
}
foreach ($entry in $expected.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $entry.Key) -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Unrecognized Chapter Two startup source file $($entry.Key)"
    }
}

$copy = Join-Path $out 'source'
$container = Join-Path $copy 'container'
New-Item -ItemType Directory -Path $container | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LoopbackWire.h') -Destination $copy
Copy-Item -LiteralPath (Join-Path $source 'container/LoopbackBroker.h') -Destination $container
$code = [IO.File]::ReadAllText((Join-Path $source 'container/trace.cpp')).Replace("`r`n", "`n")

function Replace-Verified([string]$Old, [string]$New) {
    $offset = $script:code.IndexOf($Old, [StringComparison]::Ordinal)
    if ($offset -lt 0 -or $script:code.IndexOf($Old, $offset + $Old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected one verified broker site. $Old"
    }
    $script:code = $script:code.Remove($offset, $Old.Length).Insert($offset, $New)
}

Replace-Verified ' const bool traceDispatch=' " const bool detailedDiagnostics=GetEnvironmentVariableW(L`"DFL_S1C2_DETAILED_DIAGNOSTICS`",nullptr,0)!=0;`n const bool traceDispatch="
Replace-Verified 'if(s.fUnicode)std::wprintf(L"DEBUG %ls\n",(wchar_t*)b.data());else std::printf("DEBUG %s\n",b.data());' @'
if(s.fUnicode){if(detailedDiagnostics)std::wprintf(L"DEBUG %ls\n",(wchar_t*)b.data());}
else if(GetEnvironmentVariableW(L"DFLEGACY_CH2_PAYMENT_URL",nullptr,0) &&
        std::strncmp(b.data(),"DFL_S1C2_PAY_REQUEST ",21)==0){
    const auto request=b.data()+21;
    const auto length=std::strlen(request);
    if(length>0 && length<=2150 && std::strspn(request,"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz- ")==length){
        std::printf("LOCAL_PAY_REQUEST %lu %s\n",pi.dwProcessId,request);
        std::fflush(stdout);
    }
}
else if(detailedDiagnostics || std::strstr(b.data(),"DFLegacy Chapter Two:") ||
        std::strstr(b.data(),"CHAPTER_TWO_PERF ") || std::strstr(b.data(),"PREFERRED_BASE_UNAVAILABLE") ||
        std::strstr(b.data(),"LOAD_IMPORT ") || std::strstr(b.data(),"MISSING_IMPORT "))
    std::printf("DEBUG %s\n",b.data());
'@
Replace-Verified '   if(r.ExceptionCode==0xe06d7363 && x.dwFirstChance && r.NumberParameters>=3){' '   if(detailedDiagnostics && r.ExceptionCode==0xe06d7363 && x.dwFirstChance && r.NumberParameters>=3){'
Replace-Verified '   if(r.ExceptionCode==0xc000071c && x.dwFirstChance && imageBase){' '   if(detailedDiagnostics && r.ExceptionCode==0xc000071c && x.dwFirstChance && imageBase){'
Replace-Verified '       std::printf("BP %s EIP=' '       if(detailedDiagnostics){ std::printf("BP %s EIP='
Replace-Verified '       DWORD caller=0;' "       }`n       DWORD caller=0;"
Replace-Verified '       if(ReadProcessMemory(pi.hProcess,reinterpret_cast<void*>(static_cast<uintptr_t>(context.Esp)),&caller,4,&count) && caller>32){' '       if((detailedDiagnostics || protectWorker) && ReadProcessMemory(pi.hProcess,reinterpret_cast<void*>(static_cast<uintptr_t>(context.Esp)),&caller,4,&count) && caller>32 && detailedDiagnostics){'
$postRead = @'
         if(ReadProcessMemory(pi.hProcess,(void*)(uintptr_t)context.Esp,args,sizeof(args),&count)
            && args[1]>=6 && args[1]<=4096){
'@
$postLog = @'
         if((detailedDiagnostics || GetEnvironmentVariableW(L"DFL_S1C2_TRACE_TOWN_DETAIL",nullptr,0)) &&
            ReadProcessMemory(pi.hProcess,(void*)(uintptr_t)context.Esp,args,sizeof(args),&count)
            && args[1]>=6 && args[1]<=4096){
'@
Replace-Verified ($postRead.Replace("`r`n", "`n")) ($postLog.Replace("`r`n", "`n"))
Replace-Verified '       if(std::strstr(bp->second.name,"vendor_create_") && context.Eax){' '       if(detailedDiagnostics && std::strstr(bp->second.name,"vendor_create_") && context.Eax){'
Replace-Verified '       if(std::strstr(bp->second.name,"MessageBox")){' '       if(detailedDiagnostics && std::strstr(bp->second.name,"MessageBox")){'
Replace-Verified '    if(++exceptions<100 || !x.dwFirstChance){' '    if(!x.dwFirstChance || (detailedDiagnostics && ++exceptions<100)){'

[IO.File]::WriteAllText((Join-Path $container 'trace.cpp'), $code)
Copy-Item -LiteralPath (Join-Path $source 'container/CMakeLists.txt') -Destination $container
$build = Join-Path $out 'build'
& $CMake -S $container -B $build -G 'Visual Studio 18 2026' -A x64
if ($LASTEXITCODE) { throw 'Chapter Two startup broker configuration failed.' }
& $CMake --build $build --config Release
if ($LASTEXITCODE) { throw 'Chapter Two startup broker build failed.' }
Get-FileHash -LiteralPath (Join-Path $build 'Release/dnf_startup_trace.exe') -Algorithm SHA256
