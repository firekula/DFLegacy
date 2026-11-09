#include "window_input_scaling.h"
#include "d3d8_hooks.h"
#include "memory_patch.h"
#include "pe_import.h"
#include <psapi.h>
#include <intrin.h>

namespace
{
    using Dispatch=LRESULT(WINAPI*)(const MSG*);
    Dispatch OriginalA=nullptr,OriginalW=nullptr;
    using ScreenPoint=BOOL(WINAPI*)(HWND,LPPOINT);
    ScreenPoint OriginalClientToScreen=nullptr;
    bool Installed=false;
    bool ScaledClient(HWND window, RECT& client) noexcept
    {
        return window && dfl::d3d8_hooks::UsesFixedFrameInput(window)
            && GetClientRect(window,&client) && client.right>=640 && client.bottom>=480
            && client.right<=8192 && client.bottom<=8192 && client.right*3==client.bottom*4;
    }
    BOOL WINAPI CursorTargetToScreen(HWND window, LPPOINT point)
    {
        // These two original call sites immediately pass the converted point to SetCursorPos.
        const auto caller=reinterpret_cast<std::uintptr_t>(_ReturnAddress());
        if(point && (caller==0x008FC6C5 || caller==0x008FCD40))
            dfl::window_input_scaling::LogicalToClient(window,*point);
        return OriginalClientToScreen(window,point);
    }
    LRESULT Forward(const MSG* message,Dispatch original)
    {
        if(!original)return 0;
        MSG logical{};
        return message&&dfl::window_input_scaling::Translate(*message,logical)?original(&logical):original(message);
    }
    LRESULT WINAPI DispatchA(const MSG* message){return Forward(message,OriginalA);}
    LRESULT WINAPI DispatchW(const MSG* message){return Forward(message,OriginalW);}
}
namespace dfl::window_input_scaling
{
    bool ClientToLogical(HWND window,POINT& point) noexcept
    {
        RECT client{};
        if(!ScaledClient(window,client))return false;
        const auto map=[](LONG value,LONG size,LONG logical)
        {
            const std::int64_t numerator=(static_cast<std::int64_t>(value)*2+1)*logical,denominator=static_cast<std::int64_t>(size)*2;
            return static_cast<LONG>(numerator<0?(numerator-denominator+1)/denominator:numerator/denominator);
        };
        point.x=map(point.x,client.right,640);point.y=map(point.y,client.bottom,480);return true;
    }
    bool LogicalToClient(HWND window,POINT& point) noexcept
    {
        RECT client{};
        if(!ScaledClient(window,client) || point.x<0 || point.x>=640 || point.y<0 || point.y>=480)
            return false;
        point.x=((2*point.x+1)*client.right)/(2*640);
        point.y=((2*point.y+1)*client.bottom)/(2*480);
        return true;
    }
    bool Translate(const MSG& physical,MSG& logical) noexcept
    {
        logical=physical;
        const UINT message=physical.message;
        if(!((message>=WM_MOUSEMOVE&&message<=WM_MBUTTONDBLCLK)||(message>=WM_XBUTTONDOWN&&message<=WM_XBUTTONDBLCLK)))return false;
        POINT cursor{static_cast<short>(LOWORD(physical.lParam)),static_cast<short>(HIWORD(physical.lParam))};
        if(!ClientToLogical(physical.hwnd,cursor))return false;
        logical.lParam=MAKELPARAM(static_cast<short>(cursor.x),static_cast<short>(cursor.y));return true;
    }
    bool Install() noexcept
    {
        if(Installed)return true;
        const auto module=GetModuleHandleW(nullptr);
        if(reinterpret_cast<std::uintptr_t>(module)!=0x00400000)return false;
        MODULEINFO info{};if(!GetModuleInformation(GetCurrentProcess(),module,&info,sizeof(info)))return false;
        auto* a=reinterpret_cast<Dispatch*>(pe_import::FindNamed(module,info.SizeOfImage,"user32.dll","DispatchMessageA"));
        auto* w=reinterpret_cast<Dispatch*>(pe_import::FindNamed(module,info.SizeOfImage,"user32.dll","DispatchMessageW"));
        if(!a&&!w)return false;
        if(a){OriginalA=*a;const auto proxy=&DispatchA;if(!memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(a),&proxy,sizeof(proxy)))return false;}
        if(w)
        {
            OriginalW=*w;const auto proxy=&DispatchW;
            if(!memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(w),&proxy,sizeof(proxy)))
            {if(a)memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(a),&OriginalA,sizeof(OriginalA));return false;}
        }
        auto* screen=reinterpret_cast<ScreenPoint*>(pe_import::FindNamed(module,info.SizeOfImage,"user32.dll","ClientToScreen"));
        if(screen)
        {
            OriginalClientToScreen=*screen;
            const auto proxy=&CursorTargetToScreen;
            memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(screen),&proxy,sizeof(proxy));
        }
        Installed=true;return true;
    }
}
