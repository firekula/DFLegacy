#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoMouseWheel.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include "../DFLegacy.Ijl15/src/pe_import.h"
#include "../DFLegacy.Ijl15/src/window_input_scaling.h"
#include <array>
#include <cstring>
#include <limits>

namespace
{
    constexpr std::uintptr_t Base=0x400000, ModelVtable=0xc11df4;
    constexpr std::size_t ImageSize=0xe57000;
    constexpr std::uintptr_t TreeUpdate=0xa3c6f0, DrawSlot=ModelVtable+0x1c;
    constexpr std::uintptr_t DispatchSlot=0xaea3d8, PeekSlot=0xaea3a4;
    constexpr unsigned char TreePrologue[]{0x53,0x56,0x8b,0xf1,0x8b,0x06};
    using Dispatch=LRESULT(WINAPI*)(const MSG*);
    using Peek=BOOL(WINAPI*)(MSG*,HWND,UINT,UINT,UINT);
    using Draw=void(__thiscall*)(void*);
    using Update=int(__thiscall*)(void*);
    unsigned char* Image=nullptr;
    unsigned char* Trampoline=nullptr;
    Dispatch PreviousDispatch=nullptr;
    Peek PreviousPeek=nullptr;
    Draw PreviousDraw=nullptr;
    DWORD GameThread=0;
    bool DrawCompleted=false;
    unsigned TraversalDepth=0;
    bool TraversalEnabled=true;
    struct Ancestor { unsigned char* object=nullptr; unsigned id=0; };
    std::array<Ancestor,32> Traversal{};
    struct Bounds { int left,top,width,height; };
    struct Request
    {
        int x=0,y=0,delta=0;
        DWORD queued=0;
        unsigned char* owner=nullptr;
        unsigned id=0;
        int distance=std::numeric_limits<int>::max();
        std::array<Ancestor,32> route{};
        unsigned routeLength=0;
    };
    std::array<Request,32> Pending{};
    std::size_t PendingCount=0;
    unsigned char* RemainderOwner=nullptr;
    unsigned RemainderId=0;
    int Remainder=0;

    template<class T> T Read(const void* address) noexcept
    { T value{};std::memcpy(&value,address,sizeof(value));return value; }
    template<class T> T Function(std::uintptr_t address) noexcept
    { return reinterpret_cast<T>(Image+address-Base); }

    bool Usable(unsigned char* object,unsigned& id,Bounds& bounds) noexcept
    {
        __try
        {
            // The native vertical draw/drag functions use +0x10/+0x11.
            // Parent visibility is enforced by the ORIGINAL UI tree traversal
            // before DrawHook is reached, rather than inferred from these bytes.
            if(!object||Read<unsigned>(object)!=ModelVtable||!object[4]||!object[0x10]||!object[0x11])return false;
            const int current=Read<int>(object+0x70),maximum=Read<int>(object+0x74);
            if(current<0||maximum<current)return false;
            id=Read<unsigned>(object+0xc);bounds=Read<Bounds>(object+0x14);
            return bounds.left>=-4096&&bounds.left<=8192&&bounds.top>=-4096&&bounds.top<=8192&&
                bounds.width>0&&bounds.width<=16&&bounds.height>=16&&bounds.height<=8192;
        }
        __except(GetExceptionCode()==EXCEPTION_ACCESS_VIOLATION?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH)
        { return false; }
    }

    void __fastcall DrawHook(void* object,void*)
    {
        PreviousDraw(object);
        if(!PendingCount||GetCurrentThreadId()!=GameThread||!TraversalDepth||!TraversalEnabled||
            TraversalDepth>Traversal.size()||Traversal[TraversalDepth-1].object!=object)return;
        Bounds bounds{};unsigned id=0;
        if(!Usable(static_cast<unsigned char*>(object),id,bounds))return;
        for(std::size_t i=0;i<PendingCount;++i)
        {
            auto& request=Pending[i];
            if(request.y<bounds.top||request.y>=bounds.top+bounds.height)continue;
            const bool direct=request.x>=bounds.left&&request.x<bounds.left+bounds.width;
            const int left=bounds.left<=32?bounds.left:(bounds.left<320?0:bounds.left-320);
            const int right=bounds.left<=32?bounds.left+bounds.width+320:bounds.left+bounds.width;
            if(!direct&&(request.x<left||request.x>=right))continue;
            const int distance=direct?0:request.x<bounds.left?bounds.left-request.x:request.x-bounds.left-bounds.width+1;
            // At equal distance retain the LAST drawn control, matching native
            // front-to-back hit priority. End stops still own their event.
            if(distance<=request.distance)
            {
                request.owner=static_cast<unsigned char*>(object);request.id=id;request.distance=distance;
                request.routeLength=TraversalDepth;
                for(unsigned j=0;j<TraversalDepth;++j)request.route[j]=Traversal[j];
            }
        }
    }

    int __fastcall TreeHook(void* tree,void*)
    {
        if(!PendingCount||GetCurrentThreadId()!=GameThread)return reinterpret_cast<Update>(Trampoline)(tree);
        const auto* node=static_cast<const unsigned char*>(tree);
        const bool previous=TraversalEnabled;
        TraversalEnabled=previous&&node[0x10]&&node[0x11];
        if(TraversalDepth<Traversal.size())Traversal[TraversalDepth]={static_cast<unsigned char*>(tree),Read<unsigned>(node+0xc)};
        ++TraversalDepth;
        const int result=reinterpret_cast<Update>(Trampoline)(tree);
        --TraversalDepth;TraversalEnabled=previous;
        if(!TraversalDepth)DrawCompleted=true;
        return result;
    }

    bool RouteStillActive(const Request& request) noexcept
    {
        if(!request.routeLength)return false;
        for(unsigned i=0;i<request.routeLength;++i)
        {
            const auto& ancestor=request.route[i];const auto* parent=ancestor.object;
            if(Read<unsigned>(parent+0xc)!=ancestor.id||!parent[0x10]||!parent[0x11])return false;
            if(i+1==request.routeLength)continue;
            const auto* head=Read<unsigned char*>(parent+0x60);
            const auto count=Read<unsigned>(parent+0x64);
            if(!head||count>4096)return false;
            const auto* entry=Read<unsigned char*>(head);bool found=false;
            for(unsigned j=0;j<count&&entry!=head;++j)
            {
                if(Read<unsigned char*>(entry+8)==request.route[i+1].object){found=true;break;}
                entry=Read<unsigned char*>(entry);
            }
            if(!found)return false;
        }
        return true;
    }

    void FlushRequests() noexcept
    {
        if(!DrawCompleted)return;
        __try
        {
            for(std::size_t i=0;i<PendingCount;++i)
            {
                const auto& request=Pending[i];Bounds bounds{};unsigned id=0;
                if(GetTickCount()-request.queued>250||!Usable(request.owner,id,bounds)||id!=request.id||!RouteStillActive(request))
                {RemainderOwner=nullptr;Remainder=0;continue;}
                if(RemainderOwner!=request.owner||RemainderId!=id)
                {RemainderOwner=request.owner;RemainderId=id;Remainder=0;}
                Remainder+=request.delta;
                int steps=Remainder/WHEEL_DELTA;Remainder%=WHEEL_DELTA;
                if(steps>16)steps=16;if(steps<-16)steps=-16;
                const int current=Read<int>(request.owner+0x70),maximum=Read<int>(request.owner+0x74);
                const auto desired=static_cast<std::int64_t>(current)-steps;
                const int next=desired<0?0:desired>maximum?maximum:static_cast<int>(desired);
                if(next!=current)
                {using Set=int(__thiscall*)(void*,int);Function<Set>(0xa33690)(request.owner,next);}
            }
        }
        __except(GetExceptionCode()==EXCEPTION_ACCESS_VIOLATION?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH)
        {RemainderOwner=nullptr;Remainder=0;}
        PendingCount=0;DrawCompleted=false;
    }

    BOOL WINAPI PeekHook(MSG* message,HWND window,UINT first,UINT last,UINT remove)
    {
        // Flush before the existing keyboard-timing/IME hook sees the NEXT
        // input. Its arguments, result and once-per-call cleanup are preserved.
        if(GetCurrentThreadId()==GameThread&&!TraversalDepth)FlushRequests();
        return PreviousPeek(message,window,first,last,remove);
    }

    void QueueWheel(const MSG& message) noexcept
    {
        if(GetCurrentThreadId()!=GameThread||!message.hwnd||message.hwnd!=Read<HWND>(Image+(0xf2e634-Base)))return;
        const int delta=static_cast<short>(HIWORD(message.wParam));if(!delta)return;
        POINT point{static_cast<short>(LOWORD(message.lParam)),static_cast<short>(HIWORD(message.lParam))};
        if(!ScreenToClient(message.hwnd,&point))return;
        dfl::window_input_scaling::ClientToLogical(message.hwnd,point);
        __try
        {
            // Same protected offset getter and receivers as native mouse input
            // 0xa428f4; do not synthesize input that mutates button/capture state.
            using Offset=int(__thiscall*)(void*);const auto offset=Function<Offset>(0xa46ab0);
            const int dx=offset(Image+(0xf2e824-Base)),dy=offset(Image+(0xf2e82c-Base));
            if(dx<-4096||dx>8192||dy<-4096||dy>8192)return;
            if(PendingCount==Pending.size())return;
            if(!PendingCount)DrawCompleted=false;
            Pending[PendingCount++]={static_cast<int>(point.x)+dx,static_cast<int>(point.y)+dy,delta,GetTickCount()};
        }
        __except(GetExceptionCode()==EXCEPTION_ACCESS_VIOLATION?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH)
        {PendingCount=0;DrawCompleted=false;}
    }

    LRESULT WINAPI DispatchHook(const MSG* message)
    {
        if(message&&message->message==WM_MOUSEWHEEL)QueueWheel(*message);
        else if(message&&GetCurrentThreadId()==GameThread&&
            (message->message==WM_KEYDOWN||message->message==WM_SYSKEYDOWN||message->message==WM_KILLFOCUS||
             message->message==WM_LBUTTONDOWN||message->message==WM_RBUTTONDOWN||message->message==WM_MBUTTONDOWN))
        {PendingCount=0;DrawCompleted=false;RemainderOwner=nullptr;Remainder=0;}
        return PreviousDispatch(message);
    }

    void Jump(unsigned char* bytes,std::uintptr_t from,const void* to) noexcept
    {
        bytes[0]=0xe9;const auto relative=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(to)-from-5);
        std::memcpy(bytes+1,&relative,sizeof(relative));
    }
    template<std::size_t N> bool Matches(unsigned char* image,std::uintptr_t address,const unsigned char(&expected)[N]) noexcept
    {return std::memcmp(image+address-Base,expected,N)==0;}
    bool Executable(const void* value) noexcept
    {
        MEMORY_BASIC_INFORMATION info{};
        return value&&VirtualQuery(value,&info,sizeof(info))&&info.State==MEM_COMMIT&&!(info.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&
            (info.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));
    }
}

bool dfl::chapter_two::mouse_wheel_detail::InstallImage(unsigned char* image,std::size_t bytes,WriteMemory write) noexcept
{
    static_assert(sizeof(void*)==4);
    if(!image||bytes!=ImageSize||!write)return false;
    constexpr unsigned char setter[]{0x55,0x8b,0xec,0x8b,0x41,0x74,0x8b,0x55,0x08,0x3b,0xd0,0x7f,0x0a,0x33,0xc0,
        0x85,0xd2,0x0f,0x9c,0xc0,0x48,0x23,0xc2,0x89,0x41,0x70,0x5d,0xc2,0x04,0};
    constexpr unsigned char draw[]{0x55,0x8b,0xec,0x51,0x56,0x8b,0xf1,0x8a,0x46,0x10,0x84,0xc0,0x74,0x5b};
    constexpr unsigned char offsets[]{0xb9,0x24,0xe8,0xf2,0,0xe8,0xb2,0x41,0,0,0x0f,0xbf,0xd7,0x03,0xc2,0xb9,0x2c,0xe8,0xf2,0};
    constexpr unsigned char wheelDrop[]{0x81,0xff,0x16,0x02,0,0,0x75,0x50};
    constexpr unsigned char treeTail[]{0x8b,0xce,0xe8,0xc5,0xfa,0xff,0xff,0xb8,1,0,0,0,0x5e,0xc3};
    constexpr unsigned char visibility[]{0x8b,0x7b,0x08,0x85,0xff,0x74,0x23,0x8a,0x47,0x10,0x84,0xc0,0x74,0x1c};
    constexpr unsigned char childCount[]{0x8b,0x46,0x64,0x83,0xc4,0x04,0x48,0x89,0x46,0x64};
    if(!Matches(image,0xa33690,setter)||!Matches(image,0xa3a720,draw)||!Matches(image,0xa428f4,offsets)||
        !Matches(image,0xa3d4f4,wheelDrop)||!Matches(image,0xa3cc24,treeTail)||!Matches(image,0xa3c788,visibility)||
        !Matches(image,0xa3c5cf,childCount)||
        Read<unsigned>(image+ModelVtable+0x24-Base)!=0xa3a790||Read<unsigned>(image+ModelVtable+0x28-Base)!=0xa3a640)return false;
    const auto dispatchSlot=pe_import::FindNamed(image,bytes,"user32.dll","DispatchMessageA");
    const auto peekSlot=pe_import::FindNamed(image,bytes,"user32.dll","PeekMessageA");
    if(reinterpret_cast<unsigned char*>(dispatchSlot)!=image+DispatchSlot-Base||
        reinterpret_cast<unsigned char*>(peekSlot)!=image+PeekSlot-Base)return false;
    auto* target=image+TreeUpdate-Base;auto* drawSlot=image+DrawSlot-Base;
    const auto dispatch=Read<Dispatch>(dispatchSlot);const auto peek=Read<Peek>(peekSlot);const auto drawFn=Read<Draw>(drawSlot);
    unsigned char replacement[6]{0,0,0,0,0,0x90};Jump(replacement,reinterpret_cast<std::uintptr_t>(target),reinterpret_cast<const void*>(&TreeHook));
    if(dispatch==&DispatchHook)return Image==image&&Trampoline&&peek==&PeekHook&&drawFn==reinterpret_cast<Draw>(&DrawHook)&&
        std::memcmp(target,replacement,sizeof(replacement))==0;
    if(Image||!Matches(image,TreeUpdate,TreePrologue)||reinterpret_cast<std::uintptr_t>(drawFn)!=0xa3a720||
        !Executable(reinterpret_cast<const void*>(dispatch))||!Executable(reinterpret_cast<const void*>(peek)))return false;
    auto* trampoline=static_cast<unsigned char*>(VirtualAlloc(nullptr,11,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));if(!trampoline)return false;
    std::memcpy(trampoline,TreePrologue,6);Jump(trampoline+6,reinterpret_cast<std::uintptr_t>(trampoline+6),target+6);
    FlushInstructionCache(GetCurrentProcess(),trampoline,11);
    const Dispatch dispatchHook=&DispatchHook;const Peek peekHook=&PeekHook;const auto drawHook=reinterpret_cast<Draw>(&DrawHook);
    const bool okay=write(reinterpret_cast<std::uintptr_t>(target),replacement,6)&&
        write(reinterpret_cast<std::uintptr_t>(drawSlot),&drawHook,4)&&write(reinterpret_cast<std::uintptr_t>(peekSlot),&peekHook,4)&&
        write(reinterpret_cast<std::uintptr_t>(dispatchSlot),&dispatchHook,4);
    if(!okay)
    {
        write(reinterpret_cast<std::uintptr_t>(dispatchSlot),&dispatch,4);write(reinterpret_cast<std::uintptr_t>(peekSlot),&peek,4);
        write(reinterpret_cast<std::uintptr_t>(drawSlot),&drawFn,4);write(reinterpret_cast<std::uintptr_t>(target),TreePrologue,6);
        VirtualFree(trampoline,0,MEM_RELEASE);return false;
    }
    Image=image;Trampoline=trampoline;PreviousDispatch=dispatch;PreviousPeek=peek;
    PreviousDraw=reinterpret_cast<Draw>(image+0xa3a720-Base);GameThread=GetCurrentThreadId();
    FlushInstructionCache(GetCurrentProcess(),target,6);return true;
}
bool dfl::chapter_two::InstallMouseWheel(unsigned char* image,std::size_t bytes) noexcept
{return mouse_wheel_detail::InstallImage(image,bytes,&memory_patch::WriteBytes);}
