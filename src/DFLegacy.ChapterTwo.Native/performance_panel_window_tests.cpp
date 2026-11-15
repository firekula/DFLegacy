#include "ChapterTwoPerformancePanel.cpp"
#include <cstdio>
#include "ChapterTwoPanelCursor.h"

namespace {int TestCursorCount=-3;int CursorShow(bool value){return value?++TestCursorCount:--TestCursorCount;}}

namespace {
constexpr COLORREF HeaderInk=RGB(215,205,174);

void CheckMinimumTextLayout(unsigned& failed)
{
    HDC screen=GetDC(nullptr);
    HDC canvas=CreateCompatibleDC(screen);
    const auto bitmap=CreateCompatibleBitmap(screen,630,464);
    ReleaseDC(nullptr,screen);
    const auto oldBitmap=SelectObject(canvas,bitmap);
    BodyFont=CreateFontW(-14,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,
        NONANTIALIASED_QUALITY,0,L"SimSun");
    HeadingFont=CreateFontW(-18,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,
        NONANTIALIASED_QUALITY,0,L"SimSun");
    const auto previous=SelectObject(canvas,BodyFont);
    constexpr wchar_t caption[]=L"慢帧 ≥33.3 ms";
    SIZE text{};
    GetTextExtentPoint32W(canvas,caption,static_cast<int>(std::size(caption)-1),&text);
    SelectObject(canvas,previous);
    Paint(canvas,{0,0,630,464});
    int first=630,last=0;
    for(int y=111;y<129;++y)
        for(int x=510;x<630;++x)
            if(GetPixel(canvas,x,y)==HeaderInk){first=std::min(first,x);last=std::max(last,x);}
    if(first>590 || last>606 || last-first<text.cx-6)
    {
        ++failed;
        std::printf("FAIL full slow-frame caption fits at minimum panel width (ink %d..%d, measured %ld)\n",first,last,text.cx);
    }
    SelectObject(canvas,oldBitmap);
    DeleteObject(bitmap);
    DeleteDC(canvas);
    DeleteObject(HeadingFont);
    DeleteObject(BodyFont);
    HeadingFont=BodyFont=nullptr;
}
}

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
    check((GetWindowLongPtrW(Panel,GWL_STYLE)&WS_CAPTION)!=WS_CAPTION,"classic panel has no Windows caption");
    const auto close=GetWindow(Panel,GW_CHILD);
    check(close!=nullptr,"classic panel provides a real close button");
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
    if(close)
    {
        const auto active=GetActiveWindow(), focus=GetFocus();
        SendMessageW(close,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(8,8));
        check(GetActiveWindow()==active&&GetFocus()==focus,"pressing classic close leaves game activation and focus alone");
        SendMessageW(close,WM_LBUTTONUP,0,MAKELPARAM(8,8));
    }
    check(!Visible&&!IsWindowVisible(Panel),"classic close button hides the owned panel");
    HidePanel();UpdatePanel(instance,GetTickCount64()+1000);
    check(!IsWindowVisible(Panel),"user-hidden panel stays hidden");
    CheckMinimumTextLayout(failed);
    DestroyWindow(Panel);Panel=nullptr;DestroyWindow(game);
    std::printf("Performance window: %u failures\n",failed);
    return failed?1:0;
}
