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
function Replace-Verified([string]$Old, [string]$New) {
    $offset = $script:code.IndexOf($Old, [StringComparison]::Ordinal)
    if ($offset -lt 0 -or $script:code.IndexOf($Old, $offset + $Old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected one verified broker site. $Old"
    }
    $script:code = $script:code.Remove($offset, $Old.Length).Insert($offset, $New)
}

$code = [IO.File]::ReadAllText((Join-Path $source 'container/LoopbackBroker.h')).Replace("`r`n", "`n")
Replace-Verified 'endpoint.sin_addr.s_addr=htonl(INADDR_LOOPBACK);' 'endpoint.sin_addr.s_addr=htonl(0x7f000002);'
Replace-Verified 'address.sin_addr.s_addr==htonl(INADDR_LOOPBACK) || address.sin_addr.s_addr==htonl(INADDR_ANY)' 'address.sin_addr.s_addr==htonl(INADDR_LOOPBACK) || address.sin_addr.s_addr==htonl(0x7f000002) || address.sin_addr.s_addr==htonl(INADDR_ANY)'
Replace-Verified 'address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);' 'address.sin_addr.s_addr=htonl(0x7f000002);'
$connectBind = @'
                        sockaddr_in local{};
                        int localLength=sizeof(local);
                        if(getsockname(it->second,reinterpret_cast<sockaddr*>(&local),&localLength)==SOCKET_ERROR){
                            reply.error=WSAGetLastError();
                            if(reply.error!=WSAEINVAL)break;
                            local.sin_family=AF_INET;
                            local.sin_addr.s_addr=isolated.sin_addr.s_addr;
                            local.sin_port=0;
                            if(bind(it->second,reinterpret_cast<const sockaddr*>(&local),sizeof(local))==SOCKET_ERROR){
                                reply.error=WSAGetLastError();
                                break;
                            }
                        }
                        reply.result=connect(it->second,reinterpret_cast<const sockaddr*>(&isolated),sizeof(isolated));
'@
Replace-Verified '                        reply.result=connect(it->second,reinterpret_cast<const sockaddr*>(&isolated),sizeof(isolated));' ($connectBind.Replace("`r`n", "`n"))
$childHandles = @'
    bool inherit(LPPROC_THREAD_ATTRIBUTE_LIST attributes,HANDLE (&handles)[2])
    {
        handles[0]=childRead_; handles[1]=childWrite_;
        return UpdateProcThreadAttribute(attributes,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                                         handles,sizeof(handles),nullptr,nullptr);
    }
'@
$pipeHandles = @'
    void writeChildHandles(HANDLE* handles) const
    {
        handles[0]=childRead_; handles[1]=childWrite_;
    }
'@
Replace-Verified ($childHandles.Replace("`r`n", "`n")) $pipeHandles
[IO.File]::WriteAllText((Join-Path $container 'LoopbackBroker.h'), $code)

$code = [IO.File]::ReadAllText((Join-Path $source 'container/trace.cpp')).Replace("`r`n", "`n")
# Ordinary desktop mode is the only launch mode. Keep the verified debugger,
# mitigation, explicit handle inheritance and loopback transport intact.
Replace-Verified "#include <userenv.h>`n#include <sddl.h>`n#pragma comment(lib, `"userenv.lib`")`n" ''
Replace-Verified "#pragma comment(lib, `"ole32.lib`")`n" ''
$containerIdentity = @'
 std::wstring profileName=L"DFLegacy.S1C2.78687eb.PartyProbe";
 wchar_t instance[2]{};
 if(GetEnvironmentVariableW(L"DFL_S1C2_INSTANCE",instance,_countof(instance))==1){
    if(instance[0]!=L'A' && instance[0]!=L'B')return 2;
    profileName+=L'.';profileName+=instance[0];
 }
 const wchar_t* profile=profileName.c_str();
 if(argc==2 && wcscmp(argv[1],L"--cleanup")==0) return FAILED(DeleteAppContainerProfile(profile));
 if(argc!=3)return 2;
 PSID sid=nullptr;
 auto hr=CreateAppContainerProfile(profile,profile,L"Local S1C2 startup probe",nullptr,0,&sid);
 if(hr==HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS))hr=DeriveAppContainerSidFromAppContainerName(profile,&sid);
 if(FAILED(hr)){std::printf("PROFILE_ERROR %08lx\n",hr);return 3;}
 if(wcscmp(argv[1],L"--sid")==0){
  LPWSTR encoded=nullptr;if(!ConvertSidToStringSidW(sid,&encoded))return 3;
  std::wprintf(L"%ls\n",encoded);LocalFree(encoded);FreeSid(sid);return 0;
 }
 LPWSTR containerSid=nullptr,containerFolder=nullptr;
 if(ConvertSidToStringSidW(sid,&containerSid)){
    if(SUCCEEDED(GetAppContainerFolderPath(containerSid,&containerFolder))){
       SetEnvironmentVariableW(L"DFL_S1C2_CONTAINER_DATA",containerFolder);
       CoTaskMemFree(containerFolder);
    }
    LocalFree(containerSid);
 }
'@
Replace-Verified ($containerIdentity.Replace("`r`n", "`n")) ' if(argc!=3)return 2;'
Replace-Verified ' const DWORD attributeCount=useBroker?3:2;' ' const DWORD attributeCount=useBroker?2:1;'
$containerCapabilities = @'
 SECURITY_CAPABILITIES security{}; security.AppContainerSid=sid;
 if(!UpdateProcThreadAttribute(si.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES,&security,sizeof(security),nullptr,nullptr))return 3;
'@
$attributeCleanup = @'
 struct AttributeListCleanup {
    LPPROC_THREAD_ATTRIBUTE_LIST list;
    ~AttributeListCleanup(){if(list)DeleteProcThreadAttributeList(list);}
 } attributeCleanup{si.lpAttributeList};
'@
Replace-Verified ($containerCapabilities.Replace("`r`n", "`n")) ($attributeCleanup.Replace("`r`n", "`n"))
Replace-Verified ' DeleteProcThreadAttributeList(si.lpAttributeList); FreeSid(sid);' ' DeleteProcThreadAttributeList(si.lpAttributeList); attributeCleanup.list=nullptr;'
Replace-Verified ' HANDLE token=nullptr; DWORD confined=0,size=0;' ' HANDLE token=nullptr; DWORD confined=1,size=0;'
Replace-Verified '    || confined!=1){' '    || confined!=0){'
Replace-Verified '  std::puts("NO_APPCONTAINER");TerminateProcess(pi.hProcess,0xEC);return 3;' @'
  std::printf("ORDINARY_PROCESS_IDENTITY_FAILED appcontainer=%lu error=%lu\n",confined,GetLastError());
  if(token)CloseHandle(token);
  TerminateProcess(pi.hProcess,0xEC);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);return 3;
'@
Replace-Verified 'std::puts("APPCONTAINER_NO_NETWORK_CAPABILITIES");' 'std::puts("ORDINARY_DESKTOP_PROCESS appcontainer=0");'
Replace-Verified '#include "LoopbackBroker.h"' "#include `"LoopbackBroker.h`"`n#include `"ChapterTwoPerformanceLogHandles.h`"`n#include `"ChapterTwoDiagnosticEvents.h`""
Replace-Verified ' PROCESS_INFORMATION pi{};' " ChapterTwoDiagnosticEvents diagnosticEvents;`n PROCESS_INFORMATION pi{};"
Replace-Verified ' CloseHandle(token); if(useBroker)broker.start(pi.dwProcessId);' " diagnosticEvents.Started(pi.dwProcessId);`n CloseHandle(token); if(useBroker)broker.start(pi.dwProcessId);"
Replace-Verified '  if(ev.dwDebugEventCode==CREATE_PROCESS_DEBUG_EVENT && ev.dwProcessId==pi.dwProcessId)' "  diagnosticEvents.Observe(ev);`n  if(ev.dwDebugEventCode==CREATE_PROCESS_DEBUG_EVENT && ev.dwProcessId==pi.dwProcessId)"
$originalInheritance = @'
 HANDLE childPipes[2]{};
 if(useBroker && !broker.inherit(si.lpAttributeList,childPipes)){
    std::printf("BROKER_HANDLES_FAILED error=%lu\n",GetLastError());return 3;
 }
'@
$performanceInheritance = @'
 HANDLE childHandles[5]{};
 ChapterTwoPerformanceLogHandles performanceLogs;
 if(useBroker){
    broker.writeChildHandles(childHandles);
    const auto count=2+performanceLogs.Open(childHandles+2);
    if(!UpdateProcThreadAttribute(si.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                                 childHandles,count*sizeof(HANDLE),nullptr,nullptr)){
       std::printf("BROKER_HANDLES_FAILED error=%lu\n",GetLastError());return 3;
    }
 }
'@
Replace-Verified ($originalInheritance.Replace("`r`n", "`n")) $performanceInheritance

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
        std::strstr(b.data(),"LOCAL_PAYMENT_INITIALIZATION_FAILED") ||
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ChapterTwoPerformanceLogHandles.h') -Destination $container
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ChapterTwoDiagnosticEvents.h') -Destination $container
Copy-Item -LiteralPath (Join-Path $source 'container/CMakeLists.txt') -Destination $container
$build = Join-Path $out 'build'
& $CMake -S $container -B $build -G 'Visual Studio 18 2026' -A x64
if ($LASTEXITCODE) { throw 'Chapter Two startup broker configuration failed.' }
& $CMake --build $build --config Release
if ($LASTEXITCODE) { throw 'Chapter Two startup broker build failed.' }
Get-FileHash -LiteralPath (Join-Path $build 'Release/dnf_startup_trace.exe') -Algorithm SHA256
