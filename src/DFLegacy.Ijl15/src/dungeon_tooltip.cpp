#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "dungeon_tooltip.h"
#include "memory_patch.h"
#include <algorithm>
#include <cstring>
namespace dfl::dungeon_tooltip
{
    namespace detail
    {
        namespace
        {
            struct Reads
            {
                const Reader& r;
                template<class T> bool At(std::uintptr_t base,std::size_t offset,T& value) const noexcept
                {
                    const auto at=static_cast<std::uint64_t>(base)+offset;
                    return base>=0x10000&&at+sizeof(T)<=0x100000000ULL&&r.read
                        &&r.read(r.context,static_cast<std::uintptr_t>(at),&value,sizeof(T));
                }
                bool Vector(std::uintptr_t owner,std::size_t offset,std::uint32_t& begin,std::uint32_t& end,std::size_t maximum) const noexcept
                {
                    std::uint32_t cap=0;
                    return At(owner,offset,begin)&&At(owner,offset+4,end)&&At(owner,offset+8,cap)&&end>=begin&&cap>=end
                        &&(end-begin)%4==0&&(end-begin)/4<=maximum&&(!cap||begin>=0x10000);
                }
            };
        }
        bool ReadWindows(const Reader& reader,std::uintptr_t manager,Windows& output) noexcept
        {
            output={};Reads r{reader};std::uint32_t actual=0,begin=0,end=0,qb=0,qe=0;
            if(!r.At(0xB5DCE8,0,actual)||actual!=manager||!r.Vector(manager,0x4C8,begin,end,MaximumWindows)
                ||!r.Vector(manager,0xEF8,qb,qe,64))return false;
            Windows candidate;candidate.count=(end-begin)/4;
            for(std::size_t i=0;i<candidate.count;++i)
            {
                std::uint32_t window=0,table=0,kind=0,owner=0;
                if(!r.At(begin,i*4,window)||!r.At(window,0,table)||table!=0xA7EBF4||!r.At(window,4,kind)||kind!=0x3F
                    ||!r.At(window,0x10,owner)||owner!=manager)return false;
                if(std::find(candidate.objects.begin(),candidate.objects.begin()+i,window)!=candidate.objects.begin()+i)return false;
                candidate.objects[i]=window;
                for(std::uint32_t at=qb;at<qe;at+=4)
                {std::uint32_t pending=0;if(!r.At(at,0,pending))return false;if(pending==window)candidate.alreadyQueued[i]=true;}
            }
            std::uint32_t finalBegin=0,finalEnd=0,finalQb=0,finalQe=0,finalManager=0;
            if(!r.At(0xB5DCE8,0,finalManager)||finalManager!=manager
                ||!r.Vector(manager,0x4C8,finalBegin,finalEnd,MaximumWindows)||begin!=finalBegin||end!=finalEnd
                ||!r.Vector(manager,0xEF8,finalQb,finalQe,64)||qb!=finalQb||qe!=finalQe)return false;
            output=candidate;return true;
        }
        Result Cleanup(int oldScene,int newScene,std::uintptr_t manager,const Reader& reader,void* context,Close close) noexcept
        {
            if(oldScene!=2||newScene!=1)return Result::NotApplicable;
            Windows windows;if(!ReadWindows(reader,manager,windows))return Result::Unavailable;
            if(!windows.count)return Result::NoWindows;
            bool requested=false;
            for(std::size_t i=0;i<windows.count;++i)
            {
                if(windows.alreadyQueued[i])continue;
                if(!close||!close(context,windows.objects[i]))return Result::CloseFailed;
                requested=true;
            }
            return requested?Result::CloseRequested:Result::AlreadyQueued;
        }
    }
    namespace
    {
        constexpr std::uintptr_t StateChangeCall=0x42F830;
        bool Installed=false;
        bool ReadNative(void*,std::uintptr_t at,void* output,std::size_t size) noexcept
        {
            __try{std::memcpy(output,reinterpret_cast<const void*>(at),size);return true;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool CloseNative(void*,std::uintptr_t window) noexcept
        {
            __try
            {
                using Function=void (__thiscall*)(void*);
                reinterpret_cast<Function>(0x7BDC50)(reinterpret_cast<void*>(window));return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        void __fastcall StateChange(void* manager,void*,int oldScene,int newScene)
        {
            // 42F815/816 push new then old; ECX is InterfaceMgr. Original ret8.
            // Do not suppress exceptions or change behavior of the original transition.
            using Function=void (__thiscall*)(void*,int,int);
            reinterpret_cast<Function>(0x79A160)(manager,oldScene,newScene);
            const detail::Reader reader{nullptr,&ReadNative};
            detail::Cleanup(oldScene,newScene,reinterpret_cast<std::uintptr_t>(manager),reader,nullptr,&CloseNative);
        }
        template<std::size_t N> bool Matches(std::uintptr_t at,const unsigned char (&expected)[N]) noexcept
        {
            std::array<unsigned char,N> actual{};
            return ReadNative(nullptr,at,actual.data(),N)&&std::memcmp(actual.data(),expected,N)==0;
        }
        bool OriginalImage() noexcept
        {
            __try
            {
                const auto base=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));if(base!=0x400000)return false;
                const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
                if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x1000)return false;
                const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(base+dos->e_lfanew);
                return nt->Signature==IMAGE_NT_SIGNATURE&&nt->FileHeader.Machine==IMAGE_FILE_MACHINE_I386
                    &&nt->FileHeader.TimeDateStamp==0x48576522&&nt->OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR32_MAGIC
                    &&nt->OptionalHeader.SizeOfImage==0xB3C000&&nt->OptionalHeader.AddressOfEntryPoint==0x51E6FD;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
    }
    bool Install() noexcept
    {
        if(Installed)return true;
        if(!OriginalImage())return false;
        constexpr unsigned char call[]={0xE8,0x2B,0xA9,0x36,0x00};
        constexpr unsigned char transition[]={0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x0C,0x56,0x8B,0xF1,0x8B,0x0D,0x60,0xE5,0xBE,0x00};
        constexpr unsigned char close[]={0x51,0x8B,0x49,0x10,0xE8,0x17,0x3A,0xFE,0xFF,0xC3};
        constexpr unsigned char reset[]={0xC7,0x05,0xB0,0x36,0xB5,0,0xFF,0xFF,0xFF,0xFF,0xC3};
        std::uint32_t closeMethod=0;
        if(!Matches(StateChangeCall,call)||!Matches(0x79A160,transition)||!Matches(0x7BDC50,close)||!Matches(0x806B00,reset)
            ||!ReadNative(nullptr,0xA7EBF4+0x28,&closeMethod,sizeof(closeMethod))||closeMethod!=0x7BDC50)return false;
        Installed=memory_patch::WriteRelativeCall(StateChangeCall,reinterpret_cast<std::uintptr_t>(&StateChange));return Installed;
    }
}
