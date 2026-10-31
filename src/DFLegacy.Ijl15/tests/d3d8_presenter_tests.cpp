#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <vector>
#include <dbghelp.h>
#include <fstream>
#include <iterator>
#include <string>
#include "../src/d3d8_abi.h"
#include "../src/d3d8_presenter.h"
#include "../src/d3d8_hooks.h"

namespace a = dfl::d3d8;
namespace p = dfl::presentation;
namespace
{
    int Failures = 0;
    void** OriginalDrawTable=nullptr;
    bool SawWrongResultDestination=false;
    LONG WINAPI Crash(EXCEPTION_POINTERS* exception)
    {
        const HANDLE process=GetCurrentProcess();SymSetOptions(SYMOPT_UNDNAME|SYMOPT_LOAD_LINES);SymInitialize(process,nullptr,TRUE);
        auto context=*exception->ContextRecord;STACKFRAME64 frame{};frame.AddrPC.Offset=context.Eip;frame.AddrPC.Mode=AddrModeFlat;
        frame.AddrFrame.Offset=context.Ebp;frame.AddrFrame.Mode=AddrModeFlat;frame.AddrStack.Offset=context.Esp;frame.AddrStack.Mode=AddrModeFlat;
        std::fprintf(stderr,"GPU_CRASH code%08lX at%08lX access%lu address%lX\n",exception->ExceptionRecord->ExceptionCode,context.Eip,exception->ExceptionRecord->ExceptionInformation[0],exception->ExceptionRecord->ExceptionInformation[1]);
        std::fprintf(stderr,"REG eax%08lX ebx%08lX ecx%08lX edx%08lX esi%08lX edi%08lX\n",context.Eax,context.Ebx,context.Ecx,context.Edx,context.Esi,context.Edi);
        for(int i=0;i<24;++i)
        {
            alignas(SYMBOL_INFO) char bytes[sizeof(SYMBOL_INFO)+1024]{};auto* symbol=reinterpret_cast<SYMBOL_INFO*>(bytes);symbol->SizeOfStruct=sizeof(SYMBOL_INFO);symbol->MaxNameLen=1023;DWORD64 delta=0;
            if(SymFromAddr(process,frame.AddrPC.Offset,&delta,symbol))std::fprintf(stderr,"STACK %llX %s+%llu\n",frame.AddrPC.Offset,symbol->Name,delta);
            else std::fprintf(stderr,"STACK %llX\n",frame.AddrPC.Offset);
            IMAGEHLP_LINE64 line{};line.SizeOfStruct=sizeof(line);DWORD displacement=0;
            if(SymGetLineFromAddr64(process,frame.AddrPC.Offset,&displacement,&line))std::fprintf(stderr,"LINE %s:%lu\n",line.FileName,line.LineNumber);
            BYTE instructions[64]{};SIZE_T read=0;
            if(ReadProcessMemory(process,reinterpret_cast<const void*>(static_cast<ULONG_PTR>(frame.AddrPC.Offset-48)),instructions,sizeof(instructions),&read)&&read==sizeof(instructions))
            {std::fprintf(stderr,"CODE %llX ",frame.AddrPC.Offset-48);for(const auto byte:instructions)std::fprintf(stderr,"%02X",byte);std::fputs("\n",stderr);}
            if(!StackWalk64(IMAGE_FILE_MACHINE_I386,process,GetCurrentThread(),&frame,&context,nullptr,SymFunctionTableAccess64,SymGetModuleBase64,nullptr))break;
        }
        return EXCEPTION_EXECUTE_HANDLER;
    }
    void Check(bool result, const char* text) { if (!result) { std::fprintf(stderr,"FAIL: %s\n",text); ++Failures; } }
    HRESULT WINAPI ObserveDraw(void* device,DWORD primitive,UINT count,const void* vertices,UINT stride)
    {
        DWORD destination=0;const auto hr=a::Call<HRESULT>(device,62,DWORD(0),DWORD(28),&destination);
        if((FAILED(hr)||destination!=1)&&!SawWrongResultDestination)
        {SawWrongResultDestination=true;Check(false,"production fixed-function draw explicitly targets CURRENT rather than inherited TEMP");}
        return a::CallTable<HRESULT>(device,OriginalDrawTable,72,primitive,count,vertices,stride);
    }
    struct Ref
    {
        void* Value = nullptr;
        ~Ref() { Reset(); }
        void Reset() { if (Value != nullptr) { a::Call<ULONG>(Value, 2); Value = nullptr; } }
    };
    unsigned Channel(DWORD pixel, int c) { return (pixel >> (c * 8)) & 255; }
    bool EffectiveSharp()
    {
        wchar_t executable[MAX_PATH]{};GetModuleFileNameW(nullptr,executable,MAX_PATH);std::wstring path(executable);path.resize(path.find_last_of(L"\\/"));
        path+=L"\\render-diagnostics\\"+std::to_wstring(GetCurrentProcessId())+L".json";
        std::ifstream file(path,std::ios::binary);const std::string text((std::istreambuf_iterator<char>(file)),{});
        return text.find("\"effective\":\"sharp\"")!=std::string::npos;
    }
    DWORD Pixel(unsigned x, unsigned y)
    {
        return 0xff000000U | (((x * 7 + y * 3) & 255) << 16) | (((x * 11 + y * 13) & 255) << 8) | ((x * 23 + y * 5) & 255);
    }
    HRESULT WINAPI LostDescription(void*, a::SurfaceDescription*) { return a::DeviceLost; }
    unsigned Reference(unsigned x, unsigned y, unsigned w, unsigned h, p::Mode mode, int c)
    {
        if (mode == p::Mode::Point || (mode == p::Mode::Sharp && w % 640 == 0))
            return Channel(Pixel(static_cast<unsigned>((x + .5) * 640 / w), static_cast<unsigned>((y + .5) * 480 / h)),c);
        const unsigned factor = mode == p::Mode::Sharp ? (w + 639) / 640 : 1;
        const double u = (x + .5) * (640 * factor) / w - .5, v = (y + .5) * (480 * factor) / h - .5;
        const int x0 = static_cast<int>(std::floor(u)), y0 = static_cast<int>(std::floor(v));
        const double fx = u - x0, fy = v - y0;
        auto sample = [&](int sx, int sy) { return Channel(Pixel(static_cast<unsigned>(std::clamp(sx,0,static_cast<int>(640*factor)-1)) / factor,
            static_cast<unsigned>(std::clamp(sy,0,static_cast<int>(480*factor)-1)) / factor),c); };
        return static_cast<unsigned>(std::lround((sample(x0,y0)*(1-fx)+sample(x0+1,y0)*fx)*(1-fy)+(sample(x0,y0+1)*(1-fx)+sample(x0+1,y0+1)*fx)*fy));
    }
}

int main()
{
    std::setvbuf(stdout,nullptr,_IONBF,0);
    SetUnhandledExceptionFilter(&Crash);
    const auto instance=GetModuleHandleW(nullptr);
    WNDCLASSW wc{};wc.hInstance=instance;wc.lpfnWndProc=DefWindowProcW;wc.lpszClassName=L"DFLegacy.Presentation.GpuTest";
    if (!RegisterClassW(&wc)) return 2;
    HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,wc.lpszClassName,L"D3D8 GPU acceptance",WS_POPUP,0,0,800,600,nullptr,nullptr,instance,nullptr);
    if (!window) return 2;
    const auto library=LoadLibraryExW(L"d3d8.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!library) { std::fprintf(stderr,"GPU_UNAVAILABLE: d3d8.dll %lu\n",GetLastError());return 2; }
    using Create = void* (WINAPI*)(UINT);
    const auto create=reinterpret_cast<Create>(GetProcAddress(library,"Direct3DCreate8"));
    Ref factory;factory.Value=create?create(220):nullptr;
    if (!factory.Value) { std::fputs("GPU_UNAVAILABLE: Direct3DCreate8\n",stderr);return 2; }
    a::DisplayMode display{};
    if (FAILED(a::Call<HRESULT>(factory.Value,8,UINT(0),&display))) return 2;
    a::PresentationParameters pp{};pp.BackBufferWidth=640;pp.BackBufferHeight=480;pp.BackBufferFormat=display.Format;pp.BackBufferCount=1;
    pp.SwapEffect=1;pp.DeviceWindow=window;pp.Windowed=TRUE;pp.Flags=1; // test-only lockable backbuffer
    Ref device;
    HRESULT hr=a::Call<HRESULT>(factory.Value,15,UINT(0),DWORD(1),window,DWORD(0x22),&pp,&device.Value);
    if (FAILED(hr)) { std::fprintf(stderr,"GPU_UNAVAILABLE: CreateDevice 0x%08lX\n",static_cast<unsigned long>(hr));return 2; }
    {
        a::Caps caps{};Check(SUCCEEDED(a::Call<HRESULT>(device.Value,7,&caps)),"actual device caps readable");
        Ref source;Check(SUCCEEDED(a::Call<HRESULT>(device.Value,16,UINT(0),DWORD(0),&source.Value)),"actual640 backbuffer available");
        a::SurfaceDescription desc{};Check(SUCCEEDED(a::Call<HRESULT>(source.Value,8,&desc)),"source description");
        if (desc.Format!=21 && desc.Format!=22) { std::fputs("GPU_UNAVAILABLE: requires32bit test surface\n",stderr);return 2; }
        a::LockedRect lock{};Check(SUCCEEDED(a::Call<HRESULT>(source.Value,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0))),"source upload lock");
        if (!lock.Bits) return 2;
        for (unsigned y=0;y<480;y++) for(unsigned x=0;x<640;x++) reinterpret_cast<DWORD*>(static_cast<unsigned char*>(lock.Bits)+y*lock.Pitch)[x]=Pixel(x,y);
        Check(SUCCEEDED(a::Call<HRESULT>(source.Value,10)),"source upload unlock");
        Ref vertex,index,texture,depth;
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,26,UINT(640),UINT(480),DWORD(80),DWORD(0),&depth.Value)),"saved depth surface");
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,31,source.Value,depth.Value)),"saved render/depth binding");
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,23,UINT(256),DWORD(0),DWORD(0),DWORD(0),&vertex.Value)),"saved vertex buffer");
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,24,UINT(12),DWORD(0),DWORD(101),DWORD(0),&index.Value)),"saved index buffer");
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,20,UINT(64),UINT(64),UINT(1),DWORD(0),desc.Format,DWORD(1),&texture.Value)),"saved game texture");
        a::Call<HRESULT>(device.Value,83,UINT(0),vertex.Value,UINT(24));a::Call<HRESULT>(device.Value,85,index.Value,UINT(7));
        a::Call<HRESULT>(device.Value,61,DWORD(0),texture.Value);a::Call<HRESULT>(device.Value,50,DWORD(27),DWORD(1));
        a::Call<HRESULT>(device.Value,50,DWORD(60),DWORD(0x11223344));a::Call<HRESULT>(device.Value,76,DWORD(0x142));
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,63,DWORD(0),DWORD(28),DWORD(5))),"incoming game stage writes its result to TEMP");
        const a::Viewport viewport{3,4,600,450,.2f,.8f};a::Call<HRESULT>(device.Value,40,&viewport);
        OriginalDrawTable=*reinterpret_cast<void***>(device.Value);
        std::array<void*,a::DeviceMethods> observedTable{};std::copy_n(OriginalDrawTable,observedTable.size(),observedTable.begin());observedTable[72]=reinterpret_cast<void*>(&ObserveDraw);
        dfl::d3d8_presenter::Presenter presenter(device.Value,observedTable.data());
        const p::Capabilities actual{caps.MaxTextureWidth,caps.MaxTextureHeight,(caps.TextureCaps&2)!=0,(caps.TextureCaps&0x20)!=0};
        const auto unsupported=presenter.Render(source.Value,source.Value,p::MakePlan(800,600,p::Mode::Sharp,actual));
        Check(unsupported.Status==a::InvalidCall&&SUCCEEDED(unsupported.RestoreStatus),"wrong target dimensions are rejected before rendering");
        void* lostMethods[9]{};lostMethods[8]=reinterpret_cast<void*>(&LostDescription);
        struct FaultSurface {void** Table;} fault{lostMethods};
        const auto lost=presenter.Render(&fault,source.Value,p::MakePlan(800,600,p::Mode::Sharp,actual));
        Check(lost.Status==a::DeviceLost&&SUCCEEDED(lost.RestoreStatus),"driver device-lost HRESULT is not hidden by enhancement fallback");
        const struct {unsigned W,H;p::Mode Mode;bool ForcePot;} cases[]={{640,480,p::Mode::Sharp,false},{800,600,p::Mode::Sharp,false},
            {960,720,p::Mode::Sharp,false},{1024,768,p::Mode::Sharp,false},{1280,960,p::Mode::Sharp,false},{1600,1200,p::Mode::Sharp,false},
            {800,600,p::Mode::Point,false},{800,600,p::Mode::Linear,false},{800,600,p::Mode::Sharp,true},{800,600,p::Mode::Linear,true}};
        for (const auto& trial:cases)
        {
            auto requested=actual;if(trial.ForcePot)requested.PowerOfTwo=true;
            const auto plan=p::MakePlan(trial.W,trial.H,trial.Mode,requested);
            Check(plan.Supported,"actual GPU supports required test plan");if(!plan.Supported)continue;
            Ref target;hr=a::Call<HRESULT>(device.Value,25,trial.W,trial.H,desc.Format,DWORD(0),BOOL(TRUE),&target.Value);
            Check(SUCCEEDED(hr),"test output render target available");if(FAILED(hr))continue;
            const auto rendered=presenter.Render(source.Value,target.Value,plan);
            if(FAILED(rendered.Status))std::fprintf(stderr,"GPU_RENDER: %s 0x%08lX\n",rendered.Stage,static_cast<unsigned long>(rendered.Status));
            Check(SUCCEEDED(rendered.Status)&&SUCCEEDED(rendered.RestoreStatus),"production rendering succeeds and restores state");
            if(FAILED(rendered.Status))continue;
            a::Viewport restored{};a::Call<HRESULT>(device.Value,41,&restored);Check(std::memcmp(&viewport,&restored,sizeof(viewport))==0,"viewport exactly restored");
            Ref stream,indices,oldTexture,rt,oldDepth;UINT stride=0,base=0;DWORD state=0,fvf=0;
            a::Call<HRESULT>(device.Value,84,UINT(0),&stream.Value,&stride);a::Call<HRESULT>(device.Value,86,&indices.Value,&base);
            a::Call<HRESULT>(device.Value,60,DWORD(0),&oldTexture.Value);a::Call<HRESULT>(device.Value,32,&rt.Value);
            Check(stream.Value==vertex.Value&&stride==24&&indices.Value==index.Value&&base==7,"stream and index side effects restored");
            Check(oldTexture.Value==texture.Value&&rt.Value==source.Value,"game texture and render target restored");
            a::Call<HRESULT>(device.Value,33,&oldDepth.Value);Check(oldDepth.Value==depth.Value,"game depth surface restored");
            a::Call<HRESULT>(device.Value,51,DWORD(27),&state);Check(state==1,"alpha blend restored");
            a::Call<HRESULT>(device.Value,51,DWORD(60),&state);Check(state==0x11223344,"texture factor restored");
            a::Call<HRESULT>(device.Value,77,&fvf);Check(fvf==0x142,"game vertex shader/FVF restored");
            DWORD resultArgument=0;a::Call<HRESULT>(device.Value,62,DWORD(0),DWORD(28),&resultArgument);
            Check(resultArgument==5,"game texture-stage TEMP result destination restored");
            lock={};hr=a::Call<HRESULT>(target.Value,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0x10));Check(SUCCEEDED(hr),"test-only output readback");
            unsigned maximum=0;std::size_t wrong=0;
            if(SUCCEEDED(hr))
            {
                const unsigned tolerance=(trial.Mode==p::Mode::Point||(trial.Mode==p::Mode::Sharp&&trial.W%640==0))?0:2;
                for(unsigned y=0;y<trial.H;y++)for(unsigned x=0;x<trial.W;x++)for(int c=0;c<3;c++)
                {
                    const DWORD pixel=reinterpret_cast<const DWORD*>(static_cast<const unsigned char*>(lock.Bits)+y*lock.Pitch)[x];
                    const unsigned expected=Reference(x,y,trial.W,trial.H,trial.Mode,c),observed=Channel(pixel,c);
                    const unsigned difference=observed>expected?observed-expected:expected-observed;
                    maximum=std::max(maximum,difference);
                    if(difference>tolerance)
                    {
                        ++wrong;
                        if(wrong<=6)std::printf("PIXEL %u,%u channel%d expected=%u observed=%u sample=%.6f,%.6f\n",x,y,c,expected,observed,(x+.5)*640/trial.W,(y+.5)*480/trial.H);
                    }
                }
                a::Call<HRESULT>(target.Value,10);
            }
            std::printf("GPU %ux%u %s pot=%d max_error=%u bad_channels=%zu\n",trial.W,trial.H,p::ModeName(trial.Mode),trial.ForcePot?1:0,maximum,wrong);
            Check(wrong==0,"actual GPU output matches independent center-aligned reference including edges");
            if(trial.W==800&&trial.Mode==p::Mode::Sharp&&!trial.ForcePot)
            {
                Check(SUCCEEDED(a::Call<HRESULT>(device.Value,34)),"caller begins a scene");
                const auto rejected=presenter.Render(source.Value,target.Value,plan);
                Check(FAILED(rejected.Status)&&SUCCEEDED(rejected.RestoreStatus),"presenter does not end a scene owned by its caller");
                Check(SUCCEEDED(a::Call<HRESULT>(device.Value,35)),"caller scene remains active after rejection");
                const auto retried=presenter.Render(source.Value,target.Value,plan);
                Check(SUCCEEDED(retried.Status)&&SUCCEEDED(retried.RestoreStatus),"valid rendering resumes after a rejected call");
            }
            presenter.Clear();presenter.Clear();
        }
        lock={};hr=a::Call<HRESULT>(source.Value,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0x10));
        Check(SUCCEEDED(hr),"source backbuffer remains readable");bool unchanged=true;
        if(SUCCEEDED(hr))
        {
            for(unsigned y=0;y<480;y++)for(unsigned x=0;x<640;x++)
                unchanged=unchanged&&((reinterpret_cast<const DWORD*>(static_cast<const unsigned char*>(lock.Bits)+y*lock.Pitch)[x]&0xffffff)==(Pixel(x,y)&0xffffff));
            a::Call<HRESULT>(source.Value,10);
        }
        Check(unchanged,"game backbuffer pixels remain untouched");
        std::printf("D3D8 device texture caps: %ux%u flags=0x%08lX\n",caps.MaxTextureWidth,caps.MaxTextureHeight,static_cast<unsigned long>(caps.TextureCaps));
    }
    const ULONG remaining=a::Call<ULONG>(device.Value,2);device.Value=nullptr;Check(remaining==0,"all presenter and captured COM resources released");
    Check(dfl::d3d8_hooks::AttachFactory(factory.Value,{p::Mode::Sharp,dfl::ijl15_config::ScaleFilterMode::Point,nullptr,false}),"real factory attaches production hooks");
    for(unsigned cycle=0;cycle<4;++cycle)
    {
        std::printf("HOOK_GPU cycle%u create\n",cycle);
        Ref hooked;pp.BackBufferWidth=640;pp.BackBufferHeight=480;
        hr=a::Call<HRESULT>(factory.Value,15,UINT(0),DWORD(1),window,DWORD(0x22),&pp,&hooked.Value);
        Check(SUCCEEDED(hr),"real hooked device creation");if(FAILED(hr))break;
        for(const auto size:{640U,800U,1280U})
        {
            std::printf("HOOK_GPU %u present\n",size);
            SetWindowPos(window,nullptr,0,0,static_cast<int>(size),static_cast<int>(size*3/4),SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOZORDER);
            std::puts("HOOK_GPU resized");
            a::Call<HRESULT>(hooked.Value,36,DWORD(0),static_cast<const RECT*>(nullptr),DWORD(1),DWORD(0xff17395b),float(1),DWORD(0));
            std::puts("HOOK_GPU cleared");
            const auto present=a::Call<HRESULT>(hooked.Value,15,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr));
            Check(SUCCEEDED(present),"real additional swap-chain production presentation");
            Check(EffectiveSharp(),"real hooked rendering diagnostic confirms enhancement rather than successful original fallback");
            Ref back;Check(SUCCEEDED(a::Call<HRESULT>(hooked.Value,16,UINT(0),DWORD(0),&back.Value)),"hooked original backbuffer still available");
            a::SurfaceDescription main{};a::Call<HRESULT>(back.Value,8,&main);Check(main.Width==640&&main.Height==480,"hook never rewrites main device logical geometry");
        }
        ShowWindow(window,SW_SHOWMINNOACTIVE);
        const auto minimized=a::Call<HRESULT>(hooked.Value,15,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr));
        Check(SUCCEEDED(minimized)||minimized==a::DeviceLost,"real minimized device propagates loss or native present result");
        ShowWindow(window,SW_RESTORE);ShowWindow(window,SW_HIDE);
        auto restored=a::Call<HRESULT>(hooked.Value,15,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr));
        if(restored==a::DeviceLost||restored==a::DeviceNotReset)
        {
            Check(SUCCEEDED(a::Call<HRESULT>(hooked.Value,14,&pp)),"real minimized device resets after restoration");
            restored=a::Call<HRESULT>(hooked.Value,15,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),static_cast<HWND>(nullptr),static_cast<const RGNDATA*>(nullptr));
        }
        Check(SUCCEEDED(restored),"real GPU resumes enhanced presentation after minimize/restore");
        Check(SUCCEEDED(a::Call<HRESULT>(hooked.Value,14,&pp)),"real Reset succeeds after production hook releases default resources");
        std::puts("HOOK_GPU reset complete");
        const ULONG refs=a::Call<ULONG>(hooked.Value,2);hooked.Value=nullptr;Check(refs==0,"real hooked device final Release has no resource cycle");
    }
    factory.Reset();FreeLibrary(library);DestroyWindow(window);UnregisterClassW(wc.lpszClassName,instance);
    if(Failures==0)std::puts("D3D8 GPU presentation tests passed");return Failures==0?0:1;
}
