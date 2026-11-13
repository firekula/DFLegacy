#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif
#include <windows.h>
#include <algorithm>
#include <array>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#ifndef CHAPTER_TWO_POPUP_BASELINE
#include "ChapterTwoPopupAnnouncement.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char popupFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0,Popups=0;
    bool FailAfter=false;
    int PopupKind=-1,PopupFlag=-1;
    void* PopupManager=nullptr;
    const char* PopupPointer=nullptr;
    const char* ReceivePointer=nullptr;
    std::string PopupText,ReceiveText,FormattedText,Fallback,Format;
    std::array<unsigned char,16> Manager{};

    void Check(bool ok,const char* text)
    { ++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL popup announcement: %s\n",text);} }
    template<class T> T& At(std::uintptr_t address)
    { return *reinterpret_cast<T*>(address); }
    void Jump(std::uintptr_t from,std::uintptr_t to)
    {
        unsigned char bytes[]{0xe9,0,0,0,0};
        const auto delta=static_cast<std::uint32_t>(to-from-5);
        std::memcpy(bytes+1,&delta,4);
        std::memcpy(reinterpret_cast<void*>(from),bytes,sizeof(bytes));
    }
    bool Write(std::uintptr_t address,const void* bytes,std::size_t size)
    {
        ++Writes;
        if(Writes==FailAt&&!FailAfter)return false;
        std::memcpy(reinterpret_cast<void*>(address),bytes,size);
        return Writes!=FailAt;
    }
    std::vector<unsigned char> Load(const wchar_t* path)
    {
        std::ifstream input(path,std::ios::binary);
        const std::vector<unsigned char> raw{std::istreambuf_iterator<char>(input),{}};
        if(raw.size()<4096)return {};
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||
            static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||
            nt->OptionalHeader.SizeOfImage!=ImageSize||nt->OptionalHeader.SizeOfHeaders>raw.size())return {};
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
            if(s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData||
                s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress)return {};
            std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
        return image;
    }
    std::string ResourceRow(const std::string& decoded,unsigned id)
    {
        const auto key=std::to_string(id)+">";
        auto begin=decoded.find("\n"+key);
        if(begin==std::string::npos)return {};
        begin+=key.size()+1;
        auto text=decoded.substr(begin,decoded.find_first_of("\r\n",begin)-begin);
        for(auto at=text.find("\\n");at!=std::string::npos;at=text.find("\\n",at+1))text.replace(at,2,"\n");
        return text;
    }
    bool LoadStrings(const wchar_t* path)
    {
        const auto module=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE|LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if(!module)return false;
        const auto resource=FindResourceA(module,MAKEINTRESOURCEA(115),"File");
        const auto size=SizeofResource(module,resource);
        const auto* bytes=static_cast<const unsigned char*>(LockResource(LoadResource(module,resource)));
        if(!bytes||size!=118208){FreeLibrary(module);return false;}
        std::string decoded(size,'\0');for(std::size_t i=0;i<size;++i)decoded[i]=static_cast<char>(bytes[i]^0xbd);
        Fallback=ResourceRow(decoded,639);Format=ResourceRow(decoded,640);
        FreeLibrary(module);return !Fallback.empty()&&!Format.empty();
    }
    const char* __cdecl Lookup(int id)
    {
        if(id==639)return Fallback.c_str();
        if(id==640)return Format.c_str();
        std::fprintf(stderr,"unexpected native string id %d\n",id);std::exit(3);
    }
    void __cdecl Trace(unsigned,...){ }
    int __cdecl FormatCrt(char* destination,std::size_t count,const char* format,va_list args)
    { return _vsnprintf(destination,count,format,args); }
    char* __cdecl CopyCrt(char* destination,const char* source,std::size_t count)
    { std::strncpy(destination,source,count);return destination; }
    void __fastcall Popup(void* manager,void*,int kind,const char* text,int flag)
    {
        ++Popups;PopupManager=manager;PopupKind=kind;PopupFlag=flag;PopupPointer=text;PopupText=text;
    }
    void __cdecl Observe(const char* received,const char* formatted)
    { ReceivePointer=received;ReceiveText=received;FormattedText=formatted; }
    __declspec(naked) void Finish()
    {
        __asm {
            lea eax,[ebp-0x6cc8]
            push eax
            lea eax,[ebp-0x2040]
            push eax
            call Observe
            add esp,8
            pop esi
            pop edi
            pop ebx
            mov esp,ebp
            pop ebp
            ret
        }
    }
    __declspec(naked) void __cdecl Dispatch(unsigned)
    {
        // Enter the original verified switch with the native local-frame layout.
        // Only the packer-specific prologue and unrelated post-popup tail are omitted.
        __asm {
            push ebp
            mov ebp,esp
            mov eax,7
        probeStack:
            sub esp,0x1000
            mov byte ptr[esp],0
            dec eax
            jnz probeStack
            push ebx
            push edi
            xor ebx,ebx
            mov dl,byte ptr[ebp+8]
            mov byte ptr[ebp-0xd],dl
            mov byte ptr[ebp-0x6cc8],0
            mov eax,0x41dc5b
            jmp eax
        }
    }
    void Prepare()
    {
        // The native jump table, type/user/length readers, bounded string reader,
        // formatting wrappers and type-zero branch execute unchanged.
        // Doubles are limited to resource lookup, CRT, trace and the UI boundary.
        Jump(0x9bffe0,reinterpret_cast<std::uintptr_t>(&Lookup));
        Jump(0xa5f282,reinterpret_cast<std::uintptr_t>(&FormatCrt));
        Jump(0xa5fbd0,reinterpret_cast<std::uintptr_t>(&CopyCrt));
        Jump(0x8657d0,reinterpret_cast<std::uintptr_t>(&Popup));
        Jump(0x421443,reinterpret_cast<std::uintptr_t>(&Finish));
        At<std::uintptr_t>(0xd4e3b0)=reinterpret_cast<std::uintptr_t>(&Trace);
        At<void*>(0xd56d24)=Manager.data();
    }
    void Case(unsigned notification,unsigned char type,const std::string& body,bool accepted)
    {
        const auto length=static_cast<std::uint32_t>(body.size());
        std::vector<unsigned char> packet{type,0x34,0x12,0,0,0,0};
        std::memcpy(packet.data()+3,&length,4);packet.insert(packet.end(),body.begin(),body.end());
        At<const unsigned char*>(0xe23ee8)=packet.data();At<int>(0xe23eec)=static_cast<int>(packet.size());
        Popups=0;PopupText.clear();PopupPointer=nullptr;PopupKind=PopupFlag=-1;PopupManager=nullptr;
        Dispatch(notification);
        const auto expected=accepted?std::string(body.c_str()):Fallback;
        Check(ReceiveText==expected,"native bounded receiver preserves accepted bytes or its original fallback");
        const auto consumed=accepted?packet.size():7u;
        Check(At<const unsigned char*>(0xe23ee8)==packet.data()+consumed&&
            At<int>(0xe23eec)==static_cast<int>(packet.size()-consumed),"native payload cursor and length gate remain unchanged");
        if(type==0){
            Check(Popups==1&&PopupKind==46&&PopupFlag==0&&PopupManager==Manager.data(),"type zero preserves popup kind, manager, flags and one-call behavior");
            Check(PopupText==expected&&PopupPointer==ReceivePointer,"type zero sends original receive buffer without the old title");
            const auto prefix=Format.substr(0,Format.find("%s"));
            Check(FormattedText==prefix+expected,"native legacy formatter still runs without changing its scratch buffer");
        }else{
            Check(Popups==0&&FormattedText.empty(),"nonzero message types bypass popup and its formatting branch");
        }
    }
#ifndef CHAPTER_TWO_POPUP_BASELINE
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::popup_announcement_detail::InstallImage;
        auto image=original;Writes=FailAt=0;
        Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&
            !InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid installation arguments never write");
        for(const auto offset:{0x1dc5bu,0x2f75cu,0x2f770u,0x21384u,0x213b9u,0x21401u,0x21420u,0x21435u,0x2143eu,0x5ccfdau}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"unrecognized dispatch, reader or popup context rejects without writing");}
        for(bool after:{false,true}){
            image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"failed pointer write restores exact original instruction bytes");}
        image=original;Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"verified pointer change installs once");
        bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]&&(i<0x21435||i>=0x2143b))only=false;
        Check(only,"only the six-byte popup body address instruction can change");
        Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"already installed pointer is idempotent");
    }
#endif
}

int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    const auto original=Load(argv[1]);if(original.empty()||!LoadStrings(argv[1]))return 2;
    Check(Format=="           - \xb9\xab\xb8\xe6 - \n\n%s","original resource 640 retains its verified CP936 title and format");
#ifndef CHAPTER_TWO_POPUP_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    const bool allocated=image!=nullptr;
    if(!image){const auto start=reinterpret_cast<std::uintptr_t>(popupFixtureReserve);DWORD old=0;
        if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(popupFixtureReserve)&&
            VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_POPUP_BASELINE
    Check(dfl::chapter_two::InstallPopupAnnouncement(image,ImageSize),"production writer installs into executable native fixture");
#endif
    Prepare();
    for(unsigned notification:{126u,131u}){
        for(unsigned char type:{static_cast<unsigned char>(0),static_cast<unsigned char>(3),static_cast<unsigned char>(16)}){
            Case(notification,type,"A",true);
            Case(notification,type,"\xd6\xd0\xce\xc4\n100% %s %n unchanged",true);
            Case(notification,type,std::string(255,'X'),true);
            Case(notification,type,std::string("left\0right",10),true);
            Case(notification,type,"",false);
            Case(notification,type,std::string(256,'Y'),false);
        }
    }
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("popup announcement: %u checks, %u failures\n",Checks,Failures);
    return Failures?1:0;
}
