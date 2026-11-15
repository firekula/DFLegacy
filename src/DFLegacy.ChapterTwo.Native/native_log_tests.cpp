#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <winioctl.h>
#include <array>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
#include "ChapterTwoNativeLog.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char nativeLogFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    constexpr std::array<std::size_t,8> Sites{0x6094c1,0x609962,0x609a21,0x609a79,0x609b01,0x609b58,0x6457f5,0x6610d0};
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
    bool FailAfter=false;
    void Check(bool ok,const char* message)
    { ++Checks; if(!ok){++Failures;std::fprintf(stderr,"FAIL native log: %s\n",message);} }
    std::vector<unsigned char> Read(const std::filesystem::path& path)
    { std::ifstream f(path,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}}; }
    std::vector<unsigned char> LoadImage(const std::filesystem::path& path)
    {
        const auto file=Read(path); if(file.size()<4096)return {};
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>file.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize)return {};
        std::vector<unsigned char> image(ImageSize);
        if(nt->OptionalHeader.SizeOfHeaders>file.size()||nt->OptionalHeader.SizeOfHeaders>image.size())return {};
        std::memcpy(image.data(),file.data(),nt->OptionalHeader.SizeOfHeaders);
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>file.data()+file.size())return {};
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
            if(s.PointerToRawData>file.size()||s.SizeOfRawData>file.size()-s.PointerToRawData||s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress)return {};
            std::memcpy(image.data()+s.VirtualAddress,file.data()+s.PointerToRawData,s.SizeOfRawData);}
        return image;
    }
    bool Write(std::uintptr_t address,const void* bytes,std::size_t size)
    { ++Writes; if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(address),bytes,size);return Writes!=FailAt; }
    void Jump(std::uintptr_t address,std::uintptr_t target)
    { unsigned char code[]{0xe9,0,0,0,0}; const auto delta=static_cast<std::uint32_t>(target-address-5);std::memcpy(code+1,&delta,4);std::memcpy(reinterpret_cast<void*>(address),code,5); }
    const char* __cdecl Resolve(const char* path){return path;}
    void* __cdecl FsOpen(const char* path,const char* mode,int sharing)
    { Check(sharing==0x40,"native fopen keeps _SH_DENYNO");FILE* file=nullptr;fopen_s(&file,path,mode);return file; }
    int __cdecl Format(char* output,std::size_t count,const char* format,...)
    { va_list args;va_start(args,format);const int result=vsnprintf(output,count,format,args);va_end(args);return result; }
    int __stdcall Copy(char* output,std::size_t count,const char* text)
    { return strcpy_s(output,count,text); }
    unsigned char Decode(unsigned char value)
    { value^=0x9d;return static_cast<unsigned char>((value<<2)|(value>>6)); }
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
    bool Junction(const std::filesystem::path& link,const std::filesystem::path& target)
    {
        // Real directory junctions need no symlink privilege or system setting.
        struct ReparseData
        {
            DWORD tag;WORD length,reserved,substituteOffset,substituteLength,printOffset,printLength;
            wchar_t names[2048];
        } data{};
        const auto substitute=L"\\??\\"+target.wstring(),print=target.wstring();
        if(substitute.size()+print.size()+2>std::size(data.names))return false;
        data.tag=IO_REPARSE_TAG_MOUNT_POINT;
        data.substituteLength=static_cast<WORD>(substitute.size()*sizeof(wchar_t));
        data.printOffset=static_cast<WORD>((substitute.size()+1)*sizeof(wchar_t));
        data.printLength=static_cast<WORD>(print.size()*sizeof(wchar_t));
        data.length=static_cast<WORD>(8+data.printOffset+data.printLength+sizeof(wchar_t));
        std::memcpy(data.names,substitute.c_str(),data.substituteLength+sizeof(wchar_t));
        std::memcpy(reinterpret_cast<unsigned char*>(data.names)+data.printOffset,print.c_str(),data.printLength+sizeof(wchar_t));
        if(!CreateDirectoryW(link.c_str(),nullptr))return false;
        const auto handle=CreateFileW(link.c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
        if(handle==INVALID_HANDLE_VALUE)return false;
        DWORD returned=0;
        const bool ok=DeviceIoControl(handle,FSCTL_SET_REPARSE_POINT,&data,8+data.length,nullptr,0,&returned,nullptr)!=FALSE;
        CloseHandle(handle);return ok;
    }
    void DirectoryCase(unsigned char* mapped,const std::vector<unsigned char>& original,const std::filesystem::path& output,const std::wstring& name)
    {
        auto root=output/L"session";
        const auto outside=output/L"outside";
        const auto pid=std::to_wstring(GetCurrentProcessId());
        std::filesystem::create_directory(root);
        std::filesystem::create_directory(outside);
        SetEnvironmentVariableW(L"DFL_S1C2_CONTAINER_DATA",nullptr);
        SetEnvironmentVariableW(L"DFL_S1C2_SESSION_DATA",nullptr);
        if(name==L"legacy-only")SetEnvironmentVariableW(L"DFL_S1C2_CONTAINER_DATA",root.c_str());
        else if(name==L"missing-session"){}
        else
        {
            if(name==L"session-with-legacy"||name==L"invalid-session-with-legacy")SetEnvironmentVariableW(L"DFL_S1C2_CONTAINER_DATA",outside.c_str());
            if(name==L"missing-root"||name==L"invalid-session-with-legacy")root/=L"missing";
            else if(name==L"root-file"){root/=L"file";std::ofstream file(root);file<<"preserve";}
            else if(name==L"temp-file"){std::ofstream file(root/L"Temp");file<<"preserve";}
            else if(name==L"logs-file"){
                std::filesystem::create_directory(root/L"Temp");std::ofstream file(root/L"Temp"/L"NativeLogs");file<<"preserve";}
            else if(name==L"pid-reuse"){
                const auto existing=root/L"Temp"/L"NativeLogs"/pid;
                std::filesystem::create_directories(existing);std::ofstream file(existing/L"DNF.trc");file<<"previous process";}
            else if(name==L"root-junction"){
                root=output/L"linked-session";Check(Junction(root,outside),"root junction fixture created");}
            else if(name==L"ancestor-junction"){
                std::filesystem::create_directory(outside/L"child");
                const auto link=output/L"linked-parent";Check(Junction(link,outside),"ancestor junction fixture created");root=link/L"child";}
            else if(name==L"temp-junction")Check(Junction(root/L"Temp",outside),"Temp junction fixture created");
            else if(name==L"logs-junction"){
                std::filesystem::create_directory(root/L"Temp");Check(Junction(root/L"Temp"/L"NativeLogs",outside),"NativeLogs junction fixture created");}
            else if(name==L"pid-junction"){
                std::filesystem::create_directories(root/L"Temp"/L"NativeLogs");Check(Junction(root/L"Temp"/L"NativeLogs"/pid,outside),"PID junction fixture created");}
            else if(name==L"relative-root")root=L"session";
            else if(name==L"dot-root")root/=L".";
            else if(name==L"long-root")root=std::wstring(1100,L'x');
            else if(name==L"unencodable-root"){
                // An unpaired UTF-16 surrogate is never losslessly representable,
                // including when the machine's ANSI codepage is UTF-8.
                root/=std::wstring(1,static_cast<wchar_t>(0xd800));
                Check(CreateDirectoryW(root.c_str(),nullptr)!=FALSE,"unencodable directory fixture created");}
            SetEnvironmentVariableW(L"DFL_S1C2_SESSION_DATA",root.c_str());
        }
        const bool installed=dfl::chapter_two::InstallNativeLog(mapped,ImageSize);
        std::string description;for(const auto character:name)description+=static_cast<char>(character);
        if(name==L"session-with-legacy"){
            Check(installed,"valid session installs even when the obsolete variable is present");
            Check(std::filesystem::is_directory(root/L"Temp"/L"NativeLogs"/pid),"session value alone selects the installed log directory");
            Check(std::filesystem::is_empty(outside),"obsolete container directory is never consumed");
            std::printf("directory case %s: %u checks, %u failures\n",description.c_str(),Checks,Failures);return;}
        Check(!installed,(description+" rejects the public installer").c_str());
        Check(std::memcmp(mapped,original.data(),original.size())==0,"directory rejection leaves every image byte unchanged");
        if(name==L"pid-reuse"){
            const auto bytes=Read(root/L"Temp"/L"NativeLogs"/pid/L"DNF.trc");
            Check(std::string(bytes.begin(),bytes.end())=="previous process","PID reuse rejection preserves previous logs");}
        else if(name!=L"long-root"&&name!=L"pid-junction")Check(!std::filesystem::exists(root/L"Temp"/L"NativeLogs"/pid),"rejected preparation leaves no PID directory");
        Check(std::filesystem::is_empty(outside)||(name==L"ancestor-junction"&&std::filesystem::is_empty(outside/L"child")),"rejected junctions do not create files in their targets");
        std::printf("directory case %s: %u checks, %u failures\n",description.c_str(),Checks,Failures);
    }
    void DirectoryCases(const std::filesystem::path& image,const std::filesystem::path& output)
    {
        wchar_t executable[32768]{};
        Check(GetModuleFileNameW(nullptr,executable,_countof(executable))!=0,"fixture executable resolved");
        for(const auto* name:{L"legacy-only",L"missing-session",L"session-with-legacy",L"invalid-session-with-legacy",L"missing-root",L"root-file",L"temp-file",L"logs-file",L"pid-reuse",L"root-junction",L"ancestor-junction",L"temp-junction",L"logs-junction",L"pid-junction",L"relative-root",L"dot-root",L"long-root",L"unencodable-root"})
        {
            auto command=L"\""+std::wstring(executable)+L"\" \""+image.wstring()+L"\" \""+(output/name).wstring()+L"\" "+name;
            STARTUPINFOW startup{};startup.cb=sizeof(startup);startup.dwFlags=STARTF_USESTDHANDLES;
            startup.hStdInput=GetStdHandle(STD_INPUT_HANDLE);startup.hStdOutput=GetStdHandle(STD_OUTPUT_HANDLE);startup.hStdError=GetStdHandle(STD_ERROR_HANDLE);
            PROCESS_INFORMATION process{};
            const bool started=CreateProcessW(nullptr,command.data(),nullptr,nullptr,TRUE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&process)!=FALSE;
            Check(started,"isolated directory fixture process starts without a window");
            if(!started)continue;
            const auto waited=WaitForSingleObject(process.hProcess,30000);
            DWORD result=2;
            if(waited==WAIT_OBJECT_0)GetExitCodeProcess(process.hProcess,&result);
            else {TerminateProcess(process.hProcess,2);WaitForSingleObject(process.hProcess,5000);}
            Check(waited==WAIT_OBJECT_0&&result==0,"isolated real-directory case passes");
            CloseHandle(process.hThread);CloseHandle(process.hProcess);
        }
    }
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::native_log_detail::InstallImage;
        const auto hook=reinterpret_cast<std::uintptr_t>(&FsOpen);
        auto image=original;Writes=FailAt=0;
        Check(!InstallImage(nullptr,ImageSize,hook,Write)&&!InstallImage(image.data(),ImageSize-1,hook,Write)&&
            !InstallImage(image.data(),ImageSize,0,Write)&&!InstallImage(image.data(),ImageSize,hook,nullptr)&&Writes==0,"invalid inputs do not write");
        for(const auto offset:{0x6094b0u,0x609962u,0x609a19u,0x609a71u,0x609af9u,0x609b50u,0x6457ebu,0x6610d0u,0x6455f0u}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),hook,Write)&&Writes==0&&image==before,"all call contexts, fopen and shared transform require exact signatures");}
        for(unsigned at=1;at<=Sites.size();++at)for(const bool after:{false,true}){
            image=original;Writes=0;FailAt=at;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),hook,Write)&&image==original,"every failed write rolls back this installation, including after-write failure");}
        image=original;Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),hook,Write)&&Writes==Sites.size(),"all seven encoders and fopen are installed");
        bool only=true;std::size_t changed=0;
        for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]){
            ++changed;bool expected=false;for(const auto site:Sites)if(i>=site&&i<site+(site==Sites.back()?6:5))expected=true;only&=expected;}
        Check(only&&changed>=35,"only local encoder calls and the fopen thunk change");
        Writes=0;Check(InstallImage(image.data(),image.size(),hook,Write)&&Writes==0,"repeated installation is idempotent");
        std::memcpy(image.data()+Sites[2],original.data()+Sites[2],5);const auto partial=image;Writes=0;FailAt=1;FailAfter=true;
        Check(!InstallImage(image.data(),image.size(),hook,Write)&&image==partial,"rollback preserves patches already installed by an earlier call");
        FailAt=0;
    }
    void Paths()
    {
        using dfl::chapter_two::native_log_detail::ResolveLogPath;char output[200]{};
        Check(ResolveLogPath("DNF.trc","C:\\logs",output,sizeof(output))&&std::strcmp(output,"C:\\logs\\DNF.trc")==0,"DNF relative path is redirected");
        Check(ResolveLogPath("C:/game/nEoPlEeNgInE.lOg","C:\\logs",output,sizeof(output))&&std::strcmp(output,"C:\\logs\\NeopleEngine.LOG")==0,"native absolute paths use a canonical basename");
        for(const auto* path:{"DNF.trc.bak","evilDNF.trc","NeopleEngine.LOG:stream","Script.pvf","","C:\\game\\"})
            Check(!ResolveLogPath(path,"C:\\logs",output,sizeof(output)),"unrelated files and alternate streams are untouched");
        Check(!ResolveLogPath(nullptr,"C:\\logs",output,sizeof(output))&&!ResolveLogPath("DNF.trc","",output,sizeof(output))&&
            !ResolveLogPath("DNF.trc","C:\\logs",output,3),"null, missing destination and truncation fail closed");
    }
#endif
}

int wmain(int argc,wchar_t** argv)
{
    if(argc!=3&&argc!=4){std::fprintf(stderr,"usage: native_log_tests IMAGE OUTPUT_DIRECTORY [DIRECTORY_CASE]\n");return 2;}
    const auto imagePath=std::filesystem::absolute(argv[1]);
    const auto original=LoadImage(imagePath);if(original.empty())return 2;
    const std::filesystem::path output=std::filesystem::absolute(argv[2]);std::filesystem::create_directories(output);
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
    if(argc==3){Installation(original);Paths();DirectoryCases(imagePath,output/L"directory-cases");}
#endif
    auto* mapped=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    const bool allocated=mapped!=nullptr;
    if(!mapped){const auto begin=reinterpret_cast<std::uintptr_t>(nativeLogFixtureReserve);DWORD old=0;
        if(begin<=0x400000&&0x400000+ImageSize<=begin+sizeof(nativeLogFixtureReserve)&&
            VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))mapped=reinterpret_cast<unsigned char*>(0x400000);}
    if(mapped!=reinterpret_cast<void*>(0x400000)){std::fprintf(stderr,"fixture image mapping failed %lu\n",GetLastError());return 2;}
    std::memcpy(mapped,original.data(),original.size());
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
    if(argc==4){std::filesystem::current_path(output);DirectoryCase(mapped,original,output,argv[3]);if(allocated)VirtualFree(mapped,0,MEM_RELEASE);return Failures?1:0;}
#endif
    Jump(0xa61074,reinterpret_cast<std::uintptr_t>(&FsOpen));
    Jump(0xa61023,reinterpret_cast<std::uintptr_t>(&std::fclose));
    Jump(0xa61531,reinterpret_cast<std::uintptr_t>(&std::fwrite));
    Jump(0x4016b0,reinterpret_cast<std::uintptr_t>(&Resolve));
    Jump(0x401d90,reinterpret_cast<std::uintptr_t>(&Format));
    Jump(0x401180,reinterpret_cast<std::uintptr_t>(&Copy));
    std::filesystem::current_path(output);
    std::filesystem::path logDirectory=output;
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
    SetEnvironmentVariableW(L"DFL_S1C2_CONTAINER_DATA",nullptr);
    SetEnvironmentVariableW(L"DFL_S1C2_SESSION_DATA",output.c_str());
    logDirectory=output/L"Temp"/L"NativeLogs"/std::to_wstring(GetCurrentProcessId());
    mapped[0x6094b0]^=1;
    Check(!dfl::chapter_two::InstallNativeLog(mapped,ImageSize)&&!std::filesystem::exists(logDirectory),"signature failure removes only its newly-created empty directory");
    mapped[0x6094b0]^=1;
    Check(dfl::chapter_two::InstallNativeLog(mapped,ImageSize),"session-only public installer retries, prepares writable paths and verified native patches");
    auto* unrelated=reinterpret_cast<FILE*(__cdecl*)(const char*,const char*)>(0xa610d0)("unrelated-file.txt","wb");
    Check(unrelated!=nullptr,"unrelated native fopen still succeeds");
    if(unrelated){std::fputs("unaltered",unrelated);std::fclose(unrelated);}
    const auto unrelatedBytes=Read(output/L"unrelated-file.txt");
    Check(std::string(unrelatedBytes.begin(),unrelatedBytes.end())=="unaltered"&&!std::filesystem::exists(logDirectory/L"unrelated-file.txt"),"unrelated native fopen uses its original filename and mode");
#endif
    constexpr char message[]="C2 native log fixture: town ready\r\n";
    const std::string engineMessage=std::string(message)+"\n";
    reinterpret_cast<void(__cdecl*)(const char*)>(0xa45790)(message);
    const auto engine=Read(logDirectory/L"NeopleEngine.LOG");
    Check(engine==std::vector<unsigned char>(engineMessage.begin(),engineMessage.end()),"native NeopleEngine writer preserves readable plaintext");
#ifdef CHAPTER_TWO_NATIVE_LOG_BASELINE
    auto decoded=engine;for(auto& x:decoded)x=Decode(x);
    Check(decoded==std::vector<unsigned char>(engineMessage.begin(),engineMessage.end()),"old NeopleEngine bytes decode to the same message");
    std::ofstream decodeFile(output/L"old-engine-decoded.txt",std::ios::binary);decodeFile.write(reinterpret_cast<const char*>(decoded.data()),static_cast<std::streamsize>(decoded.size()));
#endif
    std::array<unsigned char,4096> buffer{};
    *reinterpret_cast<void**>(0xe023cc)=buffer.data();*reinterpret_cast<unsigned*>(0xe023d0)=0;*reinterpret_cast<unsigned*>(0xe023d4)=0;
    auto* file=reinterpret_cast<FILE*(__cdecl*)(const char*,const char*)>(0xa610d0)("DNF.trc","wb");
    Check(file!=nullptr,"native fopen opens the DNF output");*reinterpret_cast<void**>(0xe023c8)=file;
    reinterpret_cast<void(__cdecl*)(const char*)>(0xa093b0)(message);
    const auto count=*reinterpret_cast<unsigned*>(0xe023d0);
    Check(count==sizeof(message)+4&&std::memcmp(buffer.data()+4,message,sizeof(message))==0,"native DNF buffered writer preserves text and its existing framing");
    if(file){std::fwrite(buffer.data(),1,count,file);std::fclose(file);}
    Check(std::filesystem::exists(logDirectory/L"DNF.trc"),"DNF file is actually present at the isolated destination");
#ifndef CHAPTER_TWO_NATIVE_LOG_BASELINE
    Check(!std::filesystem::exists(output/L"DNF.trc")&&!std::filesystem::exists(output/L"NeopleEngine.LOG"),"native log writes do not fall back into the current game directory");
#endif
    if(allocated)VirtualFree(mapped,0,MEM_RELEASE);
    std::printf("native log: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
