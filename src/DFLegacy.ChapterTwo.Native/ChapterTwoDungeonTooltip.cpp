#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoDungeonTooltip.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstring>
#include <limits>

namespace dfl::chapter_two::dungeon_tooltip_detail
{
    namespace
    {
        struct Vector {std::uint32_t begin=0,end=0,capacity=0;};
        struct Reads
        {
            const Reader& reader;
            template<class T> bool At(std::uintptr_t base,std::size_t offset,T& value) const noexcept
            {
                const auto at=static_cast<std::uint64_t>(base)+offset;
                return base>=0x10000&&at+sizeof(T)<=0x100000000ULL&&reader.read&&
                    reader.read(reader.context,static_cast<std::uintptr_t>(at),&value,sizeof(T));
            }
            bool GetVector(std::uintptr_t owner,std::size_t offset,Vector& v,unsigned maximum) const noexcept
            {
                return At(owner,offset,v)&&v.begin<=v.end&&v.end<=v.capacity&&
                    (v.begin%4)==0&&(v.end%4)==0&&(v.capacity%4)==0&&
                    (v.end-v.begin)/4<=maximum&&
                    (v.capacity==0?v.begin==0&&v.end==0:v.begin>=0x10000);
            }
        };
    }
    Result Cleanup(int oldScene,int newScene,std::uintptr_t manager,const Reader& reader,void* context,Close close) noexcept
    {
        if(oldScene!=2||newScene!=1)return Result::NotApplicable;
        const Reads read{reader};Vector active,pending;std::uint32_t current=0;
        // C2 class 0x44 has a native maximum of one (C126C0). Its active
        // vector is manager+518, not C1's class 0x3F at manager+4C8.
        if(!read.At(0xd56d24,0,current)||current!=manager||
            !read.GetVector(manager,0x518,active,1)||!read.GetVector(manager,0x1258,pending,64))return Result::Unavailable;
        std::uint32_t window=0,table=0,kind=0,owner=0;bool queued=false;
        if(active.begin!=active.end)
        {
            if(!read.At(active.begin,0,window)||!read.At(window,0,table)||table!=0xc138bc||
                !read.At(window,4,kind)||kind!=0x44||!read.At(window,0x10,owner)||owner!=manager)return Result::Unavailable;
            for(auto at=pending.begin;at<pending.end;at+=4)
            {std::uint32_t object=0;if(!read.At(at,0,object))return Result::Unavailable;if(object==window)queued=true;}
        }
        Vector finalActive,finalPending;
        if(!read.At(0xd56d24,0,current)||current!=manager||
            !read.GetVector(manager,0x518,finalActive,1)||!read.GetVector(manager,0x1258,finalPending,64)||
            std::memcmp(&active,&finalActive,sizeof(Vector))||std::memcmp(&pending,&finalPending,sizeof(Vector)))return Result::Unavailable;
        if(!window)return Result::NoWindows;
        if(queued)return Result::AlreadyQueued;
        return close&&close(context,window)?Result::CloseRequested:Result::CloseFailed;
    }
}
namespace
{
    using namespace dfl::chapter_two::dungeon_tooltip_detail;
    constexpr std::size_t ImageSize=0xe57000,CallOffset=0x37926;
    constexpr unsigned char OriginalCall[]{0xe8,0x45,0xe0,0x42,0x00};
    struct Signature {std::size_t offset,size;std::uint64_t hash;};
    // FNV-1a fingerprints of full immutable native routines keep the sizeable
    // transition readable here. The fixture mutates every covered byte. This
    // identifies the supported image; it is not a security hash of external data.
    constexpr Signature Signatures[]{
        {0x37906,0x20,0x03ba863a4406c38fULL}, // caller, arguments, manager, timing
        {0x3792b,0x6,0xba7cc6407162ae2eULL},  // untouched continuation
        {0x465970,0x6d4,0x1681e8985fa8346bULL}, // complete interface transition
        {0x48bb80,0xa,0x43e941a83e23fcf0ULL}, // virtual close, manager+10
        {0x46d960,0x54,0x0cfd358d3dca53c4ULL}, // native deferred enqueue
        {0x4dea70,0xb,0x3cd0a3de8506b966ULL}, // native dungeon-key reset
        {0x8138bc,0x3c,0x7b0f372f9423b45aULL}, // actual class 44 vtable
        {0x8126c0,0x4,0xad2aca7747985764ULL}, // native maximum = 1
        {0x491590,0x58,0x9789e15a8af34bb7ULL}, // class 44 constructor
        {0x465600,0xf2,0xfe3601bdcfc8a8ceULL}, // active removal and virtual reset
        {0x466cb0,0x89,0x2530ff88258dace0ULL}, // native deferred drain
        {0x5b8c63,0x10,0x8a4aa276bd159faeULL}, // selector creates class 44
    };
    std::uint64_t Fingerprint(const unsigned char* bytes,std::size_t size) noexcept
    {std::uint64_t hash=14695981039346656037ULL;for(std::size_t i=0;i<size;++i)hash=(hash^bytes[i])*1099511628211ULL;return hash;}
    bool ReadNative(void*,std::uintptr_t address,void* output,std::size_t size) noexcept
    {__try{std::memcpy(output,reinterpret_cast<const void*>(address),size);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
    bool CloseNative(void*,std::uintptr_t window) noexcept
    {
        __try{reinterpret_cast<void(__thiscall*)(void*)>(0x88bb80)(reinterpret_cast<void*>(window));return true;}
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
    void __fastcall StateChange(void* manager,void*,int oldScene,int newScene)
    {
        // Original thiscall ret8 runs first and retains all original behavior.
        reinterpret_cast<void(__thiscall*)(void*,int,int)>(0x865970)(manager,oldScene,newScene);
        const auto result=Cleanup(oldScene,newScene,reinterpret_cast<std::uintptr_t>(manager),{nullptr,ReadNative},nullptr,CloseNative);
        // Scene transitions are a slow path; no output occurs per frame/hover.
        if(result==Result::NotApplicable)return;
        constexpr const char* messages[]{
            "", "DFLegacy Chapter Two: local dungeon tooltip old=2 new=1 result=unavailable\n",
            "DFLegacy Chapter Two: local dungeon tooltip old=2 new=1 result=no-windows\n",
            "DFLegacy Chapter Two: local dungeon tooltip old=2 new=1 result=already-queued\n",
            "DFLegacy Chapter Two: local dungeon tooltip old=2 new=1 result=close-requested\n",
            "DFLegacy Chapter Two: local dungeon tooltip old=2 new=1 result=close-failed\n"};
        OutputDebugStringA(messages[static_cast<unsigned>(result)]);
    }
}
bool dfl::chapter_two::dungeon_tooltip_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept
{
    if(!image||bytes!=ImageSize||!write||reinterpret_cast<std::uintptr_t>(image)>std::numeric_limits<std::uintptr_t>::max()-ImageSize)return false;
    for(const auto& signature:Signatures)
        if(Fingerprint(image+signature.offset,signature.size)!=signature.hash)return false;
    std::array<unsigned char,5> replacement{0xe8,0,0,0,0};
    auto* call=image+CallOffset;const auto address=reinterpret_cast<std::uintptr_t>(call);
    const auto delta=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&StateChange)-address-5);
    std::memcpy(replacement.data()+1,&delta,4);
    if(std::memcmp(call,replacement.data(),replacement.size())==0)return true;
    if(std::memcmp(call,OriginalCall,sizeof(OriginalCall)))return false;
    if(write(address,replacement.data(),replacement.size())&&std::memcmp(call,replacement.data(),replacement.size())==0)return true;
    write(address,OriginalCall,sizeof(OriginalCall));return false;
}
bool dfl::chapter_two::InstallDungeonTooltip(unsigned char* image,std::size_t bytes) noexcept
{
    static_assert(sizeof(void*)==4,"C2 native tooltip patch requires x86");
    return reinterpret_cast<std::uintptr_t>(image)==0x400000&&dungeon_tooltip_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
}
