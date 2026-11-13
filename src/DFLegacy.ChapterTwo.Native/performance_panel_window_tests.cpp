#include "ChapterTwoPerformancePanel.cpp"
#include <cstdio>
#include "ChapterTwoPanelCursor.h"

namespace {int TestCursorCount=-3;int CursorShow(bool value){return value?++TestCursorCount:--TestCursorCount;}}

int main()
{
    const auto instance=GetModuleHandleW(nullptr);
    WNDCLASSW panelClass{};panelClass.lpfnWndProc=PanelProc;
    panelClass.hInstance=instance;panelClass.lpszClassName=WindowClass;
    RegisterClassW(&panelClass);
    const auto game=CreateWindowExW(0,L"STATIC",L"DFLegacy owned window fixture",WS_OVERLAPPEDWINDOW,
        30,40,480,360,nullptr,nullptr,instance,nullptr);
    TogglePanel(game,instance);
    unsigned failed=0;
    const auto check=[&](bool ok,const char* name){if(!ok){++failed;std::printf("FAIL %s\n",name);}};
    dfl::chapter_two::PanelCursor cursor;
    cursor.Update(true,false,CursorShow);
    check(TestCursorCount==0,"panel reveals a cursor hidden by the game");
    cursor.Update(true,true,CursorShow);
    check(TestCursorCount==0,"hover does not inflate a visible cursor counter");
    cursor.Update(false,true,CursorShow);
    check(TestCursorCount==-3,"leaving panel exactly restores game cursor counter");
    check(GetWindow(Panel,GW_OWNER)==game,"panel has its game as owner");
    check((GetWindowLongPtrW(Panel,GWL_EXSTYLE)&WS_EX_TOPMOST)==0,"panel is not globally topmost");
    SetWindowPos(game,nullptr,80,90,500,380,SWP_NOZORDER|SWP_NOACTIVATE);
    UpdatePanel(instance,GetTickCount64());
    RECT target{},actual{};GetWindowRect(game,&target);GetWindowRect(Panel,&actual);
    check(actual.left==target.right&&actual.top==target.top,"panel tracks moved game flush right");
    SetWindowPos(Panel,nullptr,0,0,680,500,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
    UpdatePanel(instance,GetTickCount64()+40);GetWindowRect(Panel,&actual);
    check(actual.right-actual.left==680&&actual.bottom-actual.top==500,"following preserves user resized dimensions");
    ShowWindow(game,SW_MINIMIZE);UpdatePanel(instance,GetTickCount64()+100);
    check(Visible&&!IsWindowVisible(Panel),"minimized game hides panel without losing user visibility preference");
    ShowWindow(game,SW_SHOWNOACTIVATE);ShowWindow(game,SW_RESTORE);UpdatePanel(instance,GetTickCount64()+200);
    check(Visible&&IsWindowVisible(Panel),"restored game restores panel");
    HidePanel();UpdatePanel(instance,GetTickCount64()+1000);
    check(!IsWindowVisible(Panel),"user-hidden panel stays hidden");
    DestroyWindow(Panel);Panel=nullptr;DestroyWindow(game);
    std::printf("Performance window: %u failures\n",failed);
    return failed?1:0;
}
