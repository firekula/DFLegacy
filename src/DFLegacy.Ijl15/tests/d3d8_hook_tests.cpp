#include "../src/d3d8_hooks.h"
#include "../src/d3d8_abi.h"
#include "../src/window_input_scaling.h"
#include "../src/diagnostic_delivery.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

namespace a=dfl::d3d8;
namespace h=dfl::d3d8_hooks;
namespace p=dfl::presentation;
namespace c=dfl::ijl15_config;
namespace
{
    int Failures=0;
    void Check(bool result,const char* label){if(!result){++Failures;std::fprintf(stderr,"FAIL: %s\n",label);}}
    void DiagnosticDelivery()
    {
        wchar_t directory[MAX_PATH]{},path[MAX_PATH]{};
        Check(GetTempPathW(MAX_PATH,directory)!=0&&GetTempFileNameW(directory,L"dfl",0,path)!=0,"diagnostic test owns a unique temporary path");
        if(!path[0])return;
        DeleteFileW(path);Check(CreateDirectoryW(path,nullptr)!=FALSE,"directory deliberately blocks file publication");
        struct Cleanup {const wchar_t* Path;~Cleanup(){DeleteFileW(Path);RemoveDirectoryW(Path);}} cleanup{path};
        dfl::diagnostics::Delivery delivery;unsigned attempts=0;std::string published;
        const auto write=[&](const std::string& bytes)
        {
            ++attempts;const auto file=CreateFileW(path,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return false;
            DWORD written=0;const bool okay=WriteFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&written,nullptr)&&written==bytes.size();CloseHandle(file);
            if(okay)published=bytes;return okay;
        };
        delivery.Update("first");for(const auto now:{0ULL,10ULL,100ULL,999ULL})delivery.Flush(now,write);
        Check(attempts==1,"persistently failing destination receives at most one attempt in the first second, including tick zero");
        delivery.Update("newest");delivery.Flush(1000,write);Check(attempts==2,"failed attempts themselves advance the rate limit");
        Check(RemoveDirectoryW(path)!=FALSE,"test destination becomes writable without changing security settings");
        delivery.Flush(1999,write);Check(attempts==2,"destination recovery cannot trigger an early retry");
        Check(delivery.Flush(2000,write)&&attempts==3&&published=="newest","pending newest status is published after recovery");
        std::ifstream read(path,std::ios::binary);const std::string actual((std::istreambuf_iterator<char>(read)),{});read.close();
        Check(actual=="newest","recovery writes actual latest bytes");
        delivery.Update("newest");delivery.Flush(2001,write);Check(attempts==3,"an unchanged successful record is never rewritten");
        const auto locked=CreateFileW(path,GENERIC_READ,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
        Check(locked!=INVALID_HANDLE_VALUE,"published diagnostic can be exclusively locked");
        delivery.Update("after-success");delivery.Flush(3000,write);delivery.Flush(3010,write);delivery.Flush(3999,write);
        Check(attempts==4,"a failed write after an aged successful record is also rate limited");
        if(locked!=INVALID_HANDLE_VALUE)
        {
            char preserved[32]{};DWORD received=0;ReadFile(locked,preserved,sizeof(preserved),&received,nullptr);CloseHandle(locked);
            Check(std::string(preserved,received)=="newest","a failed locked write leaves the old published bytes intact");
        }
        Check(delivery.Flush(4000,write)&&attempts==5&&published=="after-success","unlocking eventually publishes the latest pending record");
    }
    struct GuardedTable
    {
        void* Allocation=VirtualAlloc(nullptr,8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);
        void** Table;
        explicit GuardedTable(std::size_t count):Table(reinterpret_cast<void**>(static_cast<char*>(Allocation)+4096-count*sizeof(void*)))
        {DWORD old=0;VirtualProtect(static_cast<char*>(Allocation)+4096,4096,PAGE_NOACCESS,&old);}
        ~GuardedTable(){VirtualFree(Allocation,0,MEM_RELEASE);}
    };
    struct Device;
    struct Resource
    {
        void** Table=nullptr;ULONG References=1;Device* Owner=nullptr;
        UINT Width=640,Height=480;bool Heap=false;
    };
    struct Device
    {
        void** Table=nullptr;void** Identity=nullptr;ULONG References=1;int Tag=0,Destroyed=0,Presents=0,Resets=0,Allocations=0,Draws=0;
        DWORD LastFilter=0;HRESULT PresentResult=S_OK,ResetResult=S_OK,AllocationResult=S_OK,RestoreResult=S_OK,DrawResult=S_OK,EndResult=S_OK,RestoreTargetResult=S_OK;
        bool Restoring=false;
        Resource Source{},Target{},Chain{};
        a::Viewport View{0,0,640,480,0,1};
    };
    std::array<void*,16> ResourceTable{};
    ULONG WINAPI Add(void* value){return ++static_cast<Device*>(value)->References;}
    ULONG WINAPI Release(void* value){auto& d=*static_cast<Device*>(value);const auto n=--d.References;if(!n)++d.Destroyed;return n;}
    ULONG WINAPI RAdd(void* value){return ++static_cast<Resource*>(value)->References;}
    ULONG WINAPI RRelease(void* value)
    {
        auto* r=static_cast<Resource*>(value);const auto n=--r->References;
        if(!n){if(r->Owner)a::Call<ULONG>(r->Owner,2);if(r->Heap)delete r;}return n;
    }
    HRESULT WINAPI Description(void* value,a::SurfaceDescription* desc)
    {auto& r=*static_cast<Resource*>(value);*desc={22,1,1,0,r.Width*r.Height*4,0,r.Width,r.Height};return S_OK;}
    Resource* NewResource(Device* owner,UINT w,UINT height)
    {auto* r=new Resource{ResourceTable.data(),1,owner,w,height,true};a::Call<ULONG>(owner,1);return r;}
    HRESULT WINAPI Surface(void* value,UINT,void** output)
    {auto* r=static_cast<Resource*>(value);*output=NewResource(r->Owner,r->Width,r->Height);return S_OK;}
    HRESULT WINAPI SwapBuffer(void* value,UINT,DWORD,void** output){return Surface(value,0,output);}
    HRESULT WINAPI SwapPresent(void*,const RECT*,const RECT*,HWND,const RGNDATA*){return S_OK;}
    std::array<void*,5> SwapTable{};
    HRESULT WINAPI Caps(void*,a::Caps* caps){caps->MaxTextureWidth=4096;caps->MaxTextureHeight=4096;caps->TextureCaps=0;return S_OK;}
    HRESULT WINAPI Chain(void* value,a::PresentationParameters* pp,void** output)
    {
        auto& d=*static_cast<Device*>(value);++d.Allocations;
        if(FAILED(d.AllocationResult))return d.AllocationResult;
        auto* r=NewResource(&d,pp->BackBufferWidth,pp->BackBufferHeight);r->Table=SwapTable.data();*output=r;return S_OK;
    }
    HRESULT WINAPI Texture(void* value,UINT w,UINT height,UINT,DWORD,DWORD,DWORD,void** output)
    {auto* d=static_cast<Device*>(value);*output=NewResource(d,w,height);return S_OK;}
    HRESULT WINAPI BackBuffer(void* value,UINT,DWORD,void** output)
    {auto& d=*static_cast<Device*>(value);*output=&d.Source;RAdd(*output);return S_OK;}
    HRESULT WINAPI Target(void* value,void** output){auto& d=*static_cast<Device*>(value);*output=&d.Target;RAdd(*output);return S_OK;}
    HRESULT WINAPI Depth(void*,void** output){*output=nullptr;return a::NotFound;}
    HRESULT WINAPI Stream(void*,UINT,void** output,UINT* stride){*output=nullptr;*stride=0;return S_OK;}
    HRESULT WINAPI Indices(void*,void** output,UINT* base){*output=nullptr;*base=0;return S_OK;}
    HRESULT WINAPI GetView(void* value,a::Viewport* output){*output=static_cast<Device*>(value)->View;return S_OK;}
    HRESULT WINAPI SetView(void*,const a::Viewport*){return S_OK;}
    HRESULT WINAPI State(void*,DWORD,DWORD){return S_OK;}
    HRESULT WINAPI Stage(void* value,DWORD,DWORD,DWORD filter){auto& d=*static_cast<Device*>(value);d.LastFilter=filter;return static_cast<HRESULT>(d.Tag);}
    HRESULT WINAPI One(void*,DWORD){return S_OK;}
    HRESULT WINAPI None(void*){return S_OK;}
    HRESULT WINAPI Begin(void* value){static_cast<Device*>(value)->Restoring=false;return S_OK;}
    HRESULT WINAPI End(void* value){auto& d=*static_cast<Device*>(value);d.Restoring=true;return d.EndResult;}
    HRESULT WINAPI SetTarget(void* value,void*,void*){auto& d=*static_cast<Device*>(value);return d.Restoring?d.RestoreTargetResult:S_OK;}
    HRESULT WINAPI SetTexture(void*,DWORD,void*){return S_OK;}
    HRESULT WINAPI SetStream(void*,UINT,void*,UINT){return S_OK;}
    HRESULT WINAPI SetIndices(void*,void*,UINT){return S_OK;}
    HRESULT WINAPI Block(void*,DWORD,DWORD* output){*output=1;return S_OK;}
    HRESULT WINAPI Apply(void* value,DWORD){return static_cast<Device*>(value)->RestoreResult;}
    HRESULT WINAPI Copy(void*,void*,const RECT*,UINT,void*,const POINT*){return S_OK;}
    HRESULT WINAPI Draw(void* value,DWORD,UINT,const void*,UINT){auto& d=*static_cast<Device*>(value);++d.Draws;return d.DrawResult;}
    HRESULT WINAPI Present(void* value,const RECT*,const RECT*,HWND,const RGNDATA*)
    {auto& d=*static_cast<Device*>(value);++d.Presents;return d.PresentResult;}
    HRESULT WINAPI Reset(void* value,a::PresentationParameters*)
    {auto& d=*static_cast<Device*>(value);++d.Resets;Check(d.Table==d.Identity,"device Reset preserves compatibility-layer identity");Check(d.References==1,"Reset releases every default-pool resource before driver call");return d.ResetResult;}
    struct Factory{void** Table;ULONG References=1;Device* Created;int Calls=0;void** Identity=nullptr;};
    ULONG WINAPI FRelease(void* value){auto& f=*static_cast<Factory*>(value);Check(f.Table==f.Identity,"factory Release sees its registered native identity");return --f.References;}
    HRESULT WINAPI FQuery(void* value,const GUID*,void** output)
    {auto& f=*static_cast<Factory*>(value);if(f.Table!=f.Identity)return E_FAIL;*output=value;++f.References;return S_OK;}
    HRESULT WINAPI Create(void* value,UINT,DWORD,HWND,DWORD,a::PresentationParameters*,void** output)
    {auto& f=*static_cast<Factory*>(value);++f.Calls;if(f.Table!=f.Identity)return E_FAIL;*output=f.Created;return S_OK;}
    void Initialize(GuardedTable& storage,Device& d,int tag)
    {
        auto** t=storage.Table;d.Table=t;d.Identity=t;d.Tag=tag;
        d.Source.Table=ResourceTable.data();d.Target.Table=ResourceTable.data();
        t[1]=reinterpret_cast<void*>(&Add);t[2]=reinterpret_cast<void*>(&Release);t[7]=reinterpret_cast<void*>(&Caps);
        t[13]=reinterpret_cast<void*>(&Chain);t[14]=reinterpret_cast<void*>(&Reset);t[15]=reinterpret_cast<void*>(&Present);
        t[16]=reinterpret_cast<void*>(&BackBuffer);t[20]=reinterpret_cast<void*>(&Texture);t[28]=reinterpret_cast<void*>(&Copy);
        t[31]=reinterpret_cast<void*>(&SetTarget);t[32]=reinterpret_cast<void*>(&Target);t[33]=reinterpret_cast<void*>(&Depth);
        t[34]=reinterpret_cast<void*>(&Begin);t[35]=reinterpret_cast<void*>(&End);t[40]=reinterpret_cast<void*>(&SetView);t[41]=reinterpret_cast<void*>(&GetView);
        t[50]=reinterpret_cast<void*>(&State);t[54]=reinterpret_cast<void*>(&Apply);t[56]=reinterpret_cast<void*>(&One);t[57]=reinterpret_cast<void*>(&Block);
        t[61]=reinterpret_cast<void*>(&SetTexture);t[63]=reinterpret_cast<void*>(&Stage);t[72]=reinterpret_cast<void*>(&Draw);
        t[76]=reinterpret_cast<void*>(&One);t[83]=reinterpret_cast<void*>(&SetStream);t[84]=reinterpret_cast<void*>(&Stream);
        t[85]=reinterpret_cast<void*>(&SetIndices);t[86]=reinterpret_cast<void*>(&Indices);t[88]=reinterpret_cast<void*>(&One);
    }
    bool Attach(GuardedTable& storage,Factory& f,Device& d,HWND window,p::Mode mode,c::ScaleFilterMode filter=c::ScaleFilterMode::Point)
    {
        f.Table=storage.Table;f.Identity=storage.Table;f.Created=&d;f.Table[0]=reinterpret_cast<void*>(&FQuery);f.Table[2]=reinterpret_cast<void*>(&FRelease);f.Table[15]=reinterpret_cast<void*>(&Create);
        if(!h::AttachFactory(&f,{mode,filter,nullptr,false}))return false;
        a::PresentationParameters pp{};pp.DeviceWindow=window;pp.Windowed=TRUE;pp.BackBufferWidth=640;pp.BackBufferHeight=480;void* result=nullptr;
        const auto hr=a::Call<HRESULT>(&f,15,UINT(0),DWORD(1),window,DWORD(0),&pp,&result);
        return SUCCEEDED(hr)&&result==&d;
    }
    HRESULT Frame(Device& d){return a::Call<HRESULT>(&d,15,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr));}
    std::string Diagnostic()
    {
        wchar_t path[MAX_PATH]{};GetModuleFileNameW(nullptr,path,MAX_PATH);std::wstring file(path);file.resize(file.find_last_of(L"\\/"));
        file+=L"\\Logs\\DFLegacy\\render-diagnostics\\"+std::to_wstring(GetCurrentProcessId())+L".json";
        std::ifstream stream(file,std::ios::binary);return {(std::istreambuf_iterator<char>(stream)),{}};
    }
    void Hooks(HWND window)
    {
        ResourceTable[1]=reinterpret_cast<void*>(&RAdd);ResourceTable[2]=reinterpret_cast<void*>(&RRelease);ResourceTable[8]=reinterpret_cast<void*>(&Description);ResourceTable[15]=reinterpret_cast<void*>(&Surface);
        SwapTable[1]=ResourceTable[1];SwapTable[2]=ResourceTable[2];SwapTable[3]=reinterpret_cast<void*>(&SwapPresent);SwapTable[4]=reinterpret_cast<void*>(&SwapBuffer);
        GuardedTable dt1(97),dt2(97),ft1(16),ft2(16);Device d1,d2;Factory f1{},f2{};
        Initialize(dt1,d1,11);Initialize(dt2,d2,22);
        Check(Attach(ft1,f1,d1,window,p::Mode::Sharp),"attach first factory and device");
        Check(Attach(ft2,f2,d2,window,p::Mode::Legacy),"attach independent second factory/device");
        MSG physical{};physical.hwnd=window;physical.message=WM_MOUSEMOVE;physical.wParam=MK_LBUTTON;physical.lParam=MAKELPARAM(400,300);physical.pt={1200,-40};MSG logical{};
        POINT target{320,240};
        Check(dfl::window_input_scaling::LogicalToClient(window,target)
            && dfl::window_input_scaling::ClientToLogical(window,target)
            && target.x==320 && target.y==240,"auto cursor target round-trips at scaled resolution");
        POINT corner{639,479};
        Check(dfl::window_input_scaling::LogicalToClient(window,corner)
            && dfl::window_input_scaling::ClientToLogical(window,corner)
            && corner.x==639 && corner.y==479,"auto cursor reaches the last logical pixel");
        Check(dfl::window_input_scaling::Translate(physical,logical)&&LOWORD(logical.lParam)==320&&HIWORD(logical.lParam)==240
            &&logical.wParam==MK_LBUTTON&&logical.pt.x==1200&&logical.pt.y==-40,"physical mouse coordinates map to fixed logical frame without changing capture/buttons/screen point");
        physical.message=WM_LBUTTONDBLCLK;physical.lParam=MAKELPARAM(799,599);
        Check(dfl::window_input_scaling::Translate(physical,logical)&&LOWORD(logical.lParam)==639&&HIWORD(logical.lParam)==479,"double clicks reach exact last logical pixel");
        physical.message=WM_MOUSEMOVE;physical.lParam=MAKELPARAM(-1,-1);
        Check(dfl::window_input_scaling::Translate(physical,logical)&&static_cast<short>(LOWORD(logical.lParam))==-1
            &&static_cast<short>(HIWORD(logical.lParam))==-1,"captured dragging preserves negative coordinates");
        physical.message=WM_MOUSEWHEEL;physical.lParam=MAKELPARAM(800,500);
        Check(!dfl::window_input_scaling::Translate(physical,logical)&&logical.lParam==physical.lParam,"screen-coordinate wheel messages are not double transformed");
        physical.hwnd=nullptr;physical.message=WM_LBUTTONDOWN;
        Check(!dfl::window_input_scaling::Translate(physical,logical)&&logical.lParam==physical.lParam,"unrelated windows are untouched");
        GUID iid{};void* queried=nullptr;
        Check(a::Call<HRESULT>(&f1,0,&iid,&queried)==S_OK&&queried==&f1&&a::Call<ULONG>(&f1,2)==1,"factory QueryInterface preserves identity and hook lifetime");
        Check(a::Call<HRESULT>(&d1,63,DWORD(0),DWORD(16),DWORD(2))==11&&d1.LastFilter==2,"enhanced mode preserves game's texture sampler");
        Check(a::Call<HRESULT>(&d2,63,DWORD(0),DWORD(16),DWORD(2))==22&&d2.LastFilter==1,"legacy original call table and sampler independent");
        Check(Frame(d1)==S_OK&&d1.Draws==18&&d1.Presents==0,"sharp fractional frame routes through shared presenter and only extra chain presents");
        const int allocations=d1.Allocations;
        Check(Frame(d1)==S_OK&&d1.Allocations==allocations,"second frame caches chain and textures");
        Check(d1.References>1,"presenter resources hold real simulated device references");
        Check(Diagnostic().find("\"effective\":\"sharp\"")!=std::string::npos,"effective sharp diagnostic observable");
        for(int frame=0;frame<330;++frame)Check(Frame(d1)==S_OK,"warm presentation sample succeeds");
        Sleep(1100);Frame(d1);
        Check(Diagnostic().find("\"performanceSamples\":256")!=std::string::npos,"bounded warmed CPU preparation and frame interval summary observable");
        a::PresentationParameters pp{};pp.Windowed=TRUE;pp.DeviceWindow=window;d1.ResetResult=a::DeviceLost;
        d1.Source.Width=320;Frame(d1);Sleep(1100);Frame(d1);
        Check(Diagnostic().find("original-fallback")!=std::string::npos,"rate-limited permanent fallback eventually replaces previous success record");
        d1.Source.Width=640;d1.Presents=0;
        Check(a::Call<HRESULT>(&d1,14,&pp)==a::DeviceLost&&d1.References==1,"failed reset propagates and leaves no internal references");
        d1.ResetResult=S_OK;Check(a::Call<HRESULT>(&d1,14,&pp)==S_OK&&Frame(d1)==S_OK,"successful reset lazily rebuilds");
        // Release with an extra EXTERNAL reference must clear owned resources, without destroying a live device.
        a::Call<ULONG>(&d1,1);Check(a::Call<ULONG>(&d1,2)==1&&d1.Destroyed==0,"external release breaks owned resource cycles under reentrancy guard");
        Check(Frame(d1)==S_OK&&a::Call<ULONG>(&d1,2)==0&&d1.Destroyed==1,"final Release frees live cloned table only after original release");
        // Reuse the exact COM address with a different original table.
        Initialize(dt1,d1,33);d1.References=1;
        Check(h::AttachFactory(&f1,{p::Mode::Sharp,c::ScaleFilterMode::Point,nullptr,false}),"repeated factory attach idempotent");
        void* recreated=nullptr;Check(a::Call<HRESULT>(&f1,15,UINT(0),DWORD(1),window,DWORD(0),&pp,&recreated)==S_OK,"recreated device at same address");
        Check(a::Call<HRESULT>(&d1,63,DWORD(0),DWORD(16),DWORD(2))==33,"address reuse never uses stale context");
        d1.AllocationResult=E_OUTOFMEMORY;Check(Frame(d1)==S_OK&&d1.Presents==1,"allocation failure falls back to original intact frame");
        Check(Diagnostic().find("original-fallback")!=std::string::npos,"fallback cannot claim enhanced mode");
        d1.PresentResult=a::DeviceLost;Sleep(1100);
        Check(Frame(d1)==a::DeviceLost&&Diagnostic().find("\"effective\":\"device-lost\"")!=std::string::npos,"loss during latched original fallback is observable");
        d1.PresentResult=S_OK;d1.Presents=1;
        d1.AllocationResult=S_OK;a::Call<HRESULT>(&d1,14,&pp);d1.AllocationResult=a::DeviceLost;
        Check(Frame(d1)==a::DeviceLost&&d1.Presents==1,"device-lost allocation never swallowed by fallback");
        d1.AllocationResult=S_OK;a::Call<HRESULT>(&d1,14,&pp);d1.RestoreResult=E_FAIL;
        Check(Frame(d1)==E_FAIL&&d1.Presents==1,"state restore failure never concealed as original presentation");
        d1.RestoreResult=S_OK;a::Call<HRESULT>(&d1,14,&pp);
        const auto originalPresents=d1.Presents;
        d1.DrawResult=a::DeviceLost;d1.RestoreResult=a::InvalidCall;
        Check(Frame(d1)==a::DeviceLost&&d1.Presents==originalPresents,"draw loss takes precedence over a generic restoration failure without fallback Present");
        d1.DrawResult=E_FAIL;d1.RestoreResult=S_OK;d1.EndResult=a::DeviceLost;a::Call<HRESULT>(&d1,14,&pp);
        Check(Frame(d1)==a::DeviceLost&&d1.Presents==originalPresents,"EndScene device loss cannot be erased by an earlier generic draw failure");
        d1.DrawResult=S_OK;d1.EndResult=S_OK;d1.RestoreTargetResult=E_FAIL;d1.RestoreResult=a::DeviceNotReset;a::Call<HRESULT>(&d1,14,&pp);
        Check(Frame(d1)==a::DeviceNotReset&&d1.Presents==originalPresents,"later restoration loss takes precedence over the first generic restoration error");
        d1.RestoreTargetResult=S_OK;d1.RestoreResult=S_OK;d1.Presents=originalPresents;a::Call<HRESULT>(&d1,14,&pp);
        const RECT source{0,0,320,240};
        Check(a::Call<HRESULT>(&d1,15,&source,static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr))==S_OK&&d1.Presents==2,"unsupported Present arguments use explicit fallback");
        ShowWindow(window,SW_MINIMIZE);const int before=d1.Allocations;d1.PresentResult=a::DeviceLost;
        Sleep(1100);Check(Frame(d1)==a::DeviceLost,"minimized native device-lost result propagates");Sleep(1100);Frame(d1);
        Check(d1.Allocations==before,"minimized window never allocates");
        Check(Diagnostic().find("\"effective\":\"device-lost\"")!=std::string::npos,"minimized device loss cannot be recorded as a successful fallback");
        d1.PresentResult=S_OK;ShowWindow(window,SW_HIDE);
        Check(Frame(d2)==S_OK&&d2.Presents==1&&d2.Draws==0,"legacy uses its own original Present");
        Check(a::Call<ULONG>(&d1,2)==0&&a::Call<ULONG>(&d2,2)==0,"all recreated devices reach final release");
        Check(a::Call<ULONG>(&f1,2)==0&&a::Call<ULONG>(&f2,2)==0,"both factories final Release succeeds");
    }
    bool Child(const std::wstring& executable,const wchar_t* mode,int w=800,int height=600)
    {
        std::wstring command=L"\""+executable+L"\" --config-probe "+mode+L" "+std::to_wstring(w)+L" "+std::to_wstring(height);
        STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION info{};
        if(!CreateProcessW(executable.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&info))return false;
        const auto waited=WaitForSingleObject(info.hProcess,10000);DWORD code=1;GetExitCodeProcess(info.hProcess,&code);CloseHandle(info.hThread);CloseHandle(info.hProcess);return waited==WAIT_OBJECT_0&&code==0;
    }
    void Config()
    {
        wchar_t path[MAX_PATH]{};GetModuleFileNameW(nullptr,path,MAX_PATH);const std::wstring executable(path);
        const auto file=executable.substr(0,executable.find_last_of(L"\\/"))+L"\\Config.ini";
        std::ifstream input(file,std::ios::binary);const bool exists=input.good();const std::string original((std::istreambuf_iterator<char>(input)),{});input.close();
        struct Restore{std::wstring Path;std::string Bytes;bool Exists;~Restore(){if(Exists)std::ofstream(Path,std::ios::binary|std::ios::trunc)<<Bytes;else DeleteFileW(Path.c_str());}} restore{file,original,exists};
        Check(original.find("PresentationFilter=sharp")!=std::string::npos,"new distribution default explicitly selects sharp");
        const std::vector<std::pair<std::string,const wchar_t*>> cases{
            {"",L"legacy"},{"[Rendering]\r\nPresentationFilter=unknown\r\n",L"legacy"},
            {"[Rendering]\r\nPresentationFilter=SHARP\r\n",L"sharp"},
            {"[Rendering]\r\nPresentationFilter=point\r\n",L"point"},{"[Rendering]\r\nPresentationFilter=linear\r\n",L"linear"},
            {"\xEF\xBB\xBF; comment\r\n[Rendering]\r\nPresentationFilter=sharp\r\n",L"sharp"},
            {"[Rendering]\r\nPresentationFilter=sharp\r\n[Rendering]\r\nPresentationFilter=linear\r\n",L"sharp"},
            {"[Rendering]\r\nPresentationFilter="+std::string(100,'x')+"\r\n",L"legacy"}};
        for(const auto& item:cases){std::ofstream(file,std::ios::binary|std::ios::trunc)<<item.first;Check(Child(executable,item.second),"native reader mode/encoding case");}
        const std::wstring wide=L"[Resolution]\r\nWidth=1280\r\nHeight=960\r\n[Rendering]\r\nPresentationFilter=sharp\r\n";
        {std::ofstream out(file,std::ios::binary|std::ios::trunc);out.put('\xFF');out.put('\xFE');out.write(reinterpret_cast<const char*>(wide.data()),wide.size()*sizeof(wchar_t));}
        Check(Child(executable,L"sharp",1280,960),"actual UTF16LE native reader dimensions and mode");
        DeleteFileW(file.c_str());Check(Child(executable,L"legacy"),"absent existing file keeps old compatibility default");
    }
}
int wmain(int argc,wchar_t** argv)
{
    if(argc==5&&std::wcscmp(argv[1],L"--config-probe")==0)
    {
        const auto& settings=c::Get();char mode[32]{};WideCharToMultiByte(CP_UTF8,0,argv[2],-1,mode,32,nullptr,nullptr);
        return settings.PresentationFilter==p::ParseMode(mode)&&settings.WindowWidth==_wtoi(argv[3])&&settings.WindowHeight==_wtoi(argv[4])?0:1;
    }
    WNDCLASSW cls{};cls.lpfnWndProc=DefWindowProcW;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"DFLegacy.Hook.Fixture";RegisterClassW(&cls);
    const auto window=CreateWindowExW(WS_EX_NOACTIVATE,cls.lpszClassName,L"DFLegacy hook test",WS_POPUP,0,0,800,600,nullptr,nullptr,cls.hInstance,nullptr);
    if(!window)return 2;DiagnosticDelivery();Config();Hooks(window);DestroyWindow(window);
    std::printf("D3D8 native configuration and lifecycle fixtures: %d failures\n",Failures);return Failures?1:0;
}
