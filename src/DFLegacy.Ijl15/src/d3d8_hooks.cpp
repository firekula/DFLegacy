#include "../../DFLegacy.Client/client_paths.h"
#include "d3d8_hooks.h"
#include "d3d8_abi.h"
#include "d3d8_presenter.h"
#include "diagnostic_delivery.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include <algorithm>

namespace
{
    namespace a=dfl::d3d8;
    namespace p=dfl::presentation;
    using Options=dfl::d3d8_hooks::Options;
    thread_local unsigned InternalDepth=0;
    struct Internal {Internal(){++InternalDepth;}~Internal(){--InternalDepth;}};
    bool ReadableSlot(void** slot)
    {
        MEMORY_BASIC_INFORMATION info{};
        if(!VirtualQuery(slot,&info,sizeof(info))||info.State!=MEM_COMMIT||(info.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return false;
        return reinterpret_cast<std::uintptr_t>(slot)+sizeof(void*)<=reinterpret_cast<std::uintptr_t>(info.BaseAddress)+info.RegionSize;
    }
    std::vector<void*> CloneTable(void* object,std::size_t publicMethods)
    {
        auto** original=*static_cast<void***>(object);
        std::vector<void*> clone(original,original+publicMethods);
        // Native Windows D3D8 calls private virtual methods too (Clear calls slot133
        // on the tested runtime). Preserve contiguous executable extension entries,
        // stopping before unreadable storage or a non-code pointer; never assume104.
        for(std::size_t index=publicMethods;index<512&&ReadableSlot(original+index);++index)
        {
            void* const function=original[index];MEMORY_BASIC_INFORMATION info{};
            if(!function||!VirtualQuery(function,&info,sizeof(info))||info.State!=MEM_COMMIT
                ||(info.Protect&PAGE_GUARD)||!(info.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY)))break;
            clone.push_back(function);
        }
        return clone;
    }
    struct Reference
    {
        void* Value=nullptr;
        void Clear(){if(Value){a::Call<ULONG>(Value,2);Value=nullptr;}}
        ~Reference(){Clear();}
    };
    struct DeviceContext
    {
        void* Device;
        void** Identity;
        std::array<void*,a::DeviceMethods> Original{};
        std::vector<void*> Clone;
        Options Policy;
        std::recursive_mutex Gate;
        HWND Window;
        bool Windowed,Disabled=false,FixedFrame=false;
        const char* DisabledReason="unsupported";
        HRESULT DisabledStatus=E_FAIL;
        a::Caps Caps{};
        dfl::d3d8_presenter::Presenter Presenter;
        Reference Chain,Output;
        UINT Width=0,Height=0;DWORD Format=0;
        dfl::diagnostics::Delivery Records;
        LARGE_INTEGER Frequency{},LastTick{};
        unsigned WarmFrames=0,SampleCount=0,ChainCreations=0,MeasuredChainCreations=0;
        std::array<std::uint64_t,256> Preparation{},Intervals{};
        std::uint64_t PreparationP50=0,PreparationP95=0,PreparationPeak=0,IntervalP50=0,IntervalP95=0,IntervalPeak=0;
        DeviceContext(void* device,const Options& policy,HWND window,bool windowed):Device(device),Identity(*static_cast<void***>(device)),Policy(policy),Window(window),Windowed(windowed),Presenter(device,Original.data())
        {
            std::memcpy(Original.data(),*static_cast<void***>(device),sizeof(Original));Clone=CloneTable(device,a::DeviceMethods);
            a::CallTable<HRESULT>(Device,Original.data(),7,&Caps);
            QueryPerformanceFrequency(&Frequency);
        }
        template<class R=HRESULT,class... Args> R Call(std::size_t index,Args... args){return a::CallTable<R>(Device,Original.data(),index,args...);}
        void Clear(){Presenter.Clear();Output.Clear();Chain.Clear();Width=Height=0;Format=0;}
    };
    struct FactoryContext
    {
        void** Identity;
        std::array<void*,a::FactoryMethods> Original{};
        std::vector<void*> Clone;
        Options Policy;
        std::recursive_mutex Gate;
        FactoryContext(void* factory,const Options& policy):Identity(*static_cast<void***>(factory)),Policy(policy)
        {std::memcpy(Original.data(),*static_cast<void***>(factory),sizeof(Original));Clone=CloneTable(factory,a::FactoryMethods);}
    };
    std::mutex ContextMutex,DiagnosticMutex;
    std::map<void*,std::shared_ptr<DeviceContext>> Devices;
    std::map<void*,std::shared_ptr<FactoryContext>> Factories;

    template<class T> std::shared_ptr<T> Find(std::map<void*,std::shared_ptr<T>>& table,void* key)
    {std::lock_guard<std::mutex> lock(ContextMutex);const auto it=table.find(key);return it==table.end()?nullptr:it->second;}
    std::shared_ptr<DeviceContext> FindDevice(void* device)
    {
        // Avoid a global lock on each game texture-state call. A weak cache cannot keep a dead clone alive.
        thread_local std::weak_ptr<DeviceContext> previous;
        auto context=previous.lock();
        if(context&&context->Device==device&&*static_cast<void***>(device)==context->Clone.data())return context;
        context=Find(Devices,device);previous=context;return context;
    }
    template<class T> void Erase(std::map<void*,std::shared_ptr<T>>& table,void* key,const std::shared_ptr<T>& context)
    {std::lock_guard<std::mutex> lock(ContextMutex);const auto it=table.find(key);if(it!=table.end()&&it->second==context)table.erase(it);}
    bool Replace(void* object,void** replacement)
    {
        DWORD old=0;if(!VirtualProtect(object,sizeof(void*),PAGE_READWRITE,&old))return false;
        *static_cast<void***>(object)=replacement;DWORD ignored=0;VirtualProtect(object,sizeof(void*),old,&ignored);return true;
    }
    template<class T> struct OriginalObject
    {
        void* Object;T& Context;bool Valid,Alive=true;
        OriginalObject(void* object,T& context):Object(object),Context(context),Valid(Replace(object,context.Identity)){}
        ~OriginalObject(){if(Valid&&Alive)Replace(Object,Context.Clone.data());}
    };
    bool Lost(HRESULT hr){return hr==a::DeviceLost||hr==a::DeviceNotReset;}
    void Observe(DeviceContext& c,LARGE_INTEGER started)
    {
        if(c.SampleCount==c.Preparation.size()||c.Frequency.QuadPart<=0)return;
        LARGE_INTEGER ended{};QueryPerformanceCounter(&ended);
        const auto interval=c.LastTick.QuadPart?started.QuadPart-c.LastTick.QuadPart:0;c.LastTick=started;
        if(c.WarmFrames++<64)return;
        c.Preparation[c.SampleCount]=static_cast<std::uint64_t>((ended.QuadPart-started.QuadPart)*1000000/c.Frequency.QuadPart);
        c.Intervals[c.SampleCount]=static_cast<std::uint64_t>(interval*1000000/c.Frequency.QuadPart);
        if(++c.SampleCount!=c.Preparation.size())return;
        auto preparation=c.Preparation,intervals=c.Intervals;std::sort(preparation.begin(),preparation.end());std::sort(intervals.begin(),intervals.end());
        c.PreparationP50=preparation[127];c.PreparationP95=preparation[243];c.PreparationPeak=preparation.back();
        c.IntervalP50=intervals[127];c.IntervalP95=intervals[243];c.IntervalPeak=intervals.back();c.MeasuredChainCreations=c.ChainCreations;
    }
    std::uint64_t FileTimeValue(FILETIME value){return (static_cast<std::uint64_t>(value.dwHighDateTime)<<32)|value.dwLowDateTime;}
    void Record(DeviceContext& context,const char* effective,const char* reason,HRESULT hr,const p::Plan* plan=nullptr) noexcept
    {
        try
        {
            char fields[1536]{};
            if(effective)
            {
            std::snprintf(fields,sizeof(fields),"\"requested\":\"%s\",\"effective\":\"%s\",\"reason\":\"%s\",\"hresult\":\"0x%08lX\",\"logicalWidth\":640,\"logicalHeight\":480,\"width\":%u,\"height\":%u,\"intermediateWidth\":%u,\"intermediateHeight\":%u,\"format\":%lu,\"maxTextureWidth\":%lu,\"maxTextureHeight\":%lu",
                p::ModeName(context.Policy.Filter),effective,reason,static_cast<unsigned long>(hr),plan?plan->Width:context.Width,plan?plan->Height:context.Height,
                plan?plan->IntermediateWidth:0,plan?plan->IntermediateHeight:0,context.Format,context.Caps.MaxTextureWidth,context.Caps.MaxTextureHeight);
            if(context.SampleCount==context.Preparation.size())
            {
                const auto length=std::strlen(fields);
                std::snprintf(fields+length,sizeof(fields)-length,",\"performanceSamples\":256,\"warmupFrames\":64,\"prepareP50Us\":%llu,\"prepareP95Us\":%llu,\"preparePeakUs\":%llu,\"intervalP50Us\":%llu,\"intervalP95Us\":%llu,\"intervalPeakUs\":%llu,\"outputChainCreationsAtSample\":%u",
                    context.PreparationP50,context.PreparationP95,context.PreparationPeak,context.IntervalP50,context.IntervalP95,context.IntervalPeak,context.MeasuredChainCreations);
            }
            context.Records.Update(fields);
            }
            context.Records.Flush(GetTickCount64(),[&](const std::string& pending)
            {
            std::lock_guard<std::mutex> lock(DiagnosticMutex);
            wchar_t path[32768]{};
            if(!dfl::client_paths::BuildLogPath(L"render-diagnostics",path,32768))return false;
            if(!CreateDirectoryW(path,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return false;
            const std::wstring target=std::wstring(path)+L"\\"+std::to_wstring(GetCurrentProcessId())+L".json",temporary=target+L".tmp";
            FILETIME created{},exit{},kernel{},user{},utc{};GetProcessTimes(GetCurrentProcess(),&created,&exit,&kernel,&user);GetSystemTimeAsFileTime(&utc);
            char prefix[256]{};std::snprintf(prefix,sizeof(prefix),"{\"version\":1,\"pid\":%lu,\"processStartFileTime\":%llu,\"writtenFileTime\":%llu,",GetCurrentProcessId(),FileTimeValue(created),FileTimeValue(utc));
            const std::string bytes=std::string(prefix)+pending+"}\n";
            const HANDLE file=CreateFileW(temporary.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return false;
            DWORD written=0;const bool okay=WriteFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&written,nullptr)&&written==bytes.size();CloseHandle(file);
            if(okay&&MoveFileExW(temporary.c_str(),target.c_str(),MOVEFILE_REPLACE_EXISTING))return true;
            DeleteFileW(temporary.c_str());return false;
            });
        }
        catch(...) {OutputDebugStringA("DFLegacy: render diagnostic could not be written.\n");}
    }
    HRESULT OriginalPresent(DeviceContext& c,const RECT* source,const RECT* target,HWND window,const RGNDATA* dirty)
    {return c.Call(15,source,target,window,dirty);}
    HRESULT Fallback(DeviceContext& c,const char* reason,HRESULT hr,const RECT* source,const RECT* target,HWND window,const RGNDATA* dirty,const p::Plan* plan=nullptr,bool disable=false)
    {
        c.Clear();c.Disabled=disable;c.DisabledReason=reason;c.DisabledStatus=hr;Record(c,"original-fallback",reason,hr,plan);
        const HRESULT native=OriginalPresent(c,source,target,window,dirty);
        if(Lost(native))Record(c,"device-lost","original-present",native,plan);
        return native;
    }
    HRESULT WINAPI PresentProxy(void* device,const RECT* source,const RECT* target,HWND overrideWindow,const RGNDATA* dirty)
    {
        const auto lease=FindDevice(device);if(!lease)return E_FAIL;auto& c=*lease;
        if(InternalDepth)return OriginalPresent(c,source,target,overrideWindow,dirty);
        Internal guard;
        std::lock_guard<std::recursive_mutex> lock(c.Gate);
        OriginalObject original(device,c);if(!original.Valid)return E_FAIL;
        LARGE_INTEGER started{};QueryPerformanceCounter(&started);
        Record(c,nullptr,nullptr,S_OK); // Flush a throttled transition even after permanent fallback.
        if(c.Policy.Filter==p::Mode::Legacy)
        {
            RECT client{};if(GetClientRect(c.Window,&client)){c.Width=static_cast<UINT>(client.right);c.Height=static_cast<UINT>(client.bottom);}
            Observe(c,started);Record(c,"legacy",c.Policy.InvalidMode?"unknown-mode":"legacy",S_OK);return OriginalPresent(c,source,target,overrideWindow,dirty);
        }
        if(c.Disabled)
        {
            const HRESULT native=OriginalPresent(c,source,target,overrideWindow,dirty);
            Record(c,Lost(native)?"device-lost":"original-fallback",Lost(native)?"original-present":c.DisabledReason,Lost(native)?native:c.DisabledStatus);
            return native;
        }
        if(!c.Windowed||source||target||dirty||(overrideWindow&&overrideWindow!=c.Window))
            return Fallback(c,"present-arguments",a::InvalidCall,source,target,overrideWindow,dirty);
        RECT client{};
        if(!IsWindow(c.Window)||!GetClientRect(c.Window,&client))return Fallback(c,"window",E_INVALIDARG,source,target,overrideWindow,dirty);
        if(IsIconic(c.Window)||client.right<=0||client.bottom<=0)
        {
            const HRESULT native=OriginalPresent(c,source,target,overrideWindow,dirty);
            if(Lost(native))c.Clear();Record(c,Lost(native)?"device-lost":"original-fallback","minimized",native);return native;
        }
        const p::Capabilities caps{c.Caps.MaxTextureWidth,c.Caps.MaxTextureHeight,(c.Caps.TextureCaps&2U)!=0,(c.Caps.TextureCaps&32U)!=0};
        using PhysicalPoint=BOOL(WINAPI*)(HWND,LPPOINT);
        static const auto physical=[]()
        {
            const auto user=GetModuleHandleW(L"user32.dll");auto proc=GetProcAddress(user,"LogicalToPhysicalPointForPerMonitorDPI");
            if(!proc)proc=GetProcAddress(user,"LogicalToPhysicalPoint");return reinterpret_cast<PhysicalPoint>(proc);
        }();
        POINT first{0,0},last{client.right,client.bottom};
        if(!physical||!ClientToScreen(c.Window,&first)||!ClientToScreen(c.Window,&last)||!physical(c.Window,&first)||!physical(c.Window,&last))
            return Fallback(c,"window-coordinates",E_FAIL,source,target,overrideWindow,dirty);
        const auto plan=p::MakeWindowPlan(static_cast<UINT>(client.right),static_cast<UINT>(client.bottom),static_cast<UINT>(last.x-first.x),static_cast<UINT>(last.y-first.y),c.Policy.Filter,caps);
        if(!plan.Supported)return Fallback(c,plan.Reason,a::InvalidCall,source,target,overrideWindow,dirty,&plan);
        Reference input;HRESULT hr=c.Call(16,UINT(0),DWORD(0),&input.Value);
        if(FAILED(hr))
        {if(Lost(hr)){c.Clear();Record(c,"device-lost","backbuffer",hr,&plan);return hr;}return Fallback(c,"backbuffer",hr,source,target,overrideWindow,dirty,&plan,true);}
        a::SurfaceDescription description{};hr=a::Call<HRESULT>(input.Value,8,&description);
        if(SUCCEEDED(hr)){c.FixedFrame=description.Width==640&&description.Height==480;if(!c.FixedFrame)hr=a::InvalidCall;}
        if(FAILED(hr))
        {if(Lost(hr)){c.Clear();Record(c,"device-lost","backbuffer-layout",hr,&plan);return hr;}return Fallback(c,"backbuffer-layout",hr,source,target,overrideWindow,dirty,&plan,true);}
        if(!c.Chain.Value||c.Width!=plan.Width||c.Height!=plan.Height||c.Format!=description.Format)
        {
            c.Clear();a::PresentationParameters pp{};pp.BackBufferWidth=plan.Width;pp.BackBufferHeight=plan.Height;pp.BackBufferFormat=description.Format;
            pp.BackBufferCount=1;pp.SwapEffect=1;pp.DeviceWindow=c.Window;pp.Windowed=TRUE;
            hr=c.Call(13,&pp,&c.Chain.Value);
            if(SUCCEEDED(hr))++c.ChainCreations;
            if(SUCCEEDED(hr))hr=a::Call<HRESULT>(c.Chain.Value,4,UINT(0),DWORD(0),&c.Output.Value);
            if(FAILED(hr))
            {if(Lost(hr)){c.Clear();Record(c,"device-lost","output-chain",hr,&plan);return hr;}return Fallback(c,"output-chain",hr,source,target,overrideWindow,dirty,&plan,true);}
            c.Width=plan.Width;c.Height=plan.Height;c.Format=description.Format;
        }
        const auto result=c.Presenter.Render(input.Value,c.Output.Value,plan);
        if(FAILED(result.Status))
        {
            if(FAILED(result.RestoreStatus)||Lost(result.Status))
            {c.Clear();Record(c,Lost(result.Status)?"device-lost":"render-error",result.Stage,result.Status,&plan);return result.Status;}
            return Fallback(c,result.Stage,result.Status,source,target,overrideWindow,dirty,&plan,true);
        }
        Observe(c,started);
        hr=a::Call<HRESULT>(c.Chain.Value,3,static_cast<const RECT*>(nullptr),static_cast<const RECT*>(nullptr),c.Window,static_cast<const RGNDATA*>(nullptr));
        if(FAILED(hr))
        {if(Lost(hr)){c.Clear();Record(c,"device-lost","output-present",hr,&plan);return hr;}return Fallback(c,"output-present",hr,source,target,overrideWindow,dirty,&plan,true);}
        Record(c,p::ModeName(c.Policy.Filter),"ok",hr,&plan);return hr;
    }
    HRESULT WINAPI ResetProxy(void* device,a::PresentationParameters* pp)
    {
        const auto lease=FindDevice(device);if(!lease)return E_FAIL;auto& c=*lease;
        if(InternalDepth)return c.Call(14,pp);
        Internal guard;std::lock_guard<std::recursive_mutex> lock(c.Gate);
        OriginalObject original(device,c);if(!original.Valid)return E_FAIL;
        c.Clear();const HRESULT hr=c.Call(14,pp);
        if(SUCCEEDED(hr)&&pp){c.Windowed=pp->Windowed!=FALSE;if(pp->DeviceWindow)c.Window=pp->DeviceWindow;c.FixedFrame=pp->BackBufferWidth==640&&pp->BackBufferHeight==480;c.Disabled=false;}
        else if(FAILED(hr))Record(c,"device-lost","reset",hr);
        return hr;
    }
    ULONG WINAPI ReleaseProxy(void* device)
    {
        const auto lease=FindDevice(device);if(!lease)return 0;auto& c=*lease;
        if(InternalDepth)return c.Call<ULONG>(2);
        Internal guard;std::lock_guard<std::recursive_mutex> lock(c.Gate);
        OriginalObject original(device,c);if(!original.Valid)return 0;
        c.Clear();const ULONG count=c.Call<ULONG>(2);
        if(!count){original.Alive=false;Erase(Devices,device,lease);}
        return count; // lease keeps the executing clone/context alive until after the driver returns.
    }
    HRESULT WINAPI DeviceQuery(void* device,const GUID* iid,void** output)
    {
        const auto lease=FindDevice(device);if(!lease)return E_FAIL;auto& c=*lease;
        if(InternalDepth)return c.Call(0,iid,output);
        Internal guard;std::lock_guard<std::recursive_mutex> lock(c.Gate);
        OriginalObject original(device,c);if(!original.Valid)return E_FAIL;
        return c.Call(0,iid,output);
    }
    HRESULT WINAPI StageProxy(void* device,DWORD stage,DWORD type,DWORD value)
    {
        const auto lease=FindDevice(device);if(!lease)return E_FAIL;auto& c=*lease;
        if(InternalDepth)return c.Call(63,stage,type,value);
        Internal guard;if(c.Policy.Pump)c.Policy.Pump();
        const bool managed=c.Policy.Filter==p::Mode::Legacy&&stage==0&&(type==16||type==17||type==18);
        const auto filter=c.Policy.LegacyFilter;
        using F=dfl::ijl15_config::ScaleFilterMode;
        if(managed&&filter!=F::Native)value=filter==F::Point?1U:filter==F::Anisotropic?3U:2U;
        HRESULT hr=c.Call(63,stage,type,value);
        if(FAILED(hr)&&managed&&filter==F::Anisotropic)hr=c.Call(63,stage,type,DWORD(2));
        return hr;
    }
    bool AttachDevice(void* device,const Options& policy,HWND focus,const a::PresentationParameters* pp) noexcept
    {
        try
        {
            if(!device)return false;if(Find(Devices,device))return true;
            auto context=std::make_shared<DeviceContext>(device,policy,pp&&pp->DeviceWindow?pp->DeviceWindow:focus,pp&&pp->Windowed);
            context->FixedFrame=pp&&pp->BackBufferWidth==640&&pp->BackBufferHeight==480;
            context->Clone[0]=reinterpret_cast<void*>(&DeviceQuery);context->Clone[2]=reinterpret_cast<void*>(&ReleaseProxy);context->Clone[14]=reinterpret_cast<void*>(&ResetProxy);
            context->Clone[15]=reinterpret_cast<void*>(&PresentProxy);context->Clone[63]=reinterpret_cast<void*>(&StageProxy);
            std::lock_guard<std::mutex> lock(ContextMutex);
            const auto inserted=Devices.emplace(device,context);
            if(!inserted.second)return true;
            if(!Replace(device,context->Clone.data())){Devices.erase(inserted.first);return false;}return true;
        }
        catch(...){return false;}
    }
    HRESULT WINAPI CreateProxy(void* factory,UINT adapter,DWORD type,HWND focus,DWORD flags,a::PresentationParameters* pp,void** device)
    {
        const auto context=Find(Factories,factory);if(!context)return E_FAIL;
        std::lock_guard<std::recursive_mutex> lock(context->Gate);
        OriginalObject original(factory,*context);if(!original.Valid)return E_FAIL;
        OutputDebugStringA("DFLegacy: entering hooked D3D8 CreateDevice.\n");
        const HRESULT hr=a::CallTable<HRESULT>(factory,context->Original.data(),15,adapter,type,focus,flags,pp,device);
        OutputDebugStringA(SUCCEEDED(hr)?"DFLegacy: original D3D8 CreateDevice succeeded.\n":"DFLegacy: original D3D8 CreateDevice failed.\n");
        if(SUCCEEDED(hr)&&device&&*device&&!AttachDevice(*device,context->Policy,focus,pp))
            OutputDebugStringA("DFLegacy: D3D8 presentation hook failed; original device retained.\n");
        return hr;
    }
    ULONG WINAPI FactoryRelease(void* factory)
    {
        const auto context=Find(Factories,factory);if(!context)return 0;
        std::lock_guard<std::recursive_mutex> lock(context->Gate);
        OriginalObject original(factory,*context);if(!original.Valid)return 0;
        const ULONG count=a::CallTable<ULONG>(factory,context->Original.data(),2);
        if(!count){original.Alive=false;Erase(Factories,factory,context);}return count;
    }
    HRESULT WINAPI FactoryQuery(void* factory,const GUID* iid,void** output)
    {
        const auto context=Find(Factories,factory);if(!context)return E_FAIL;
        std::lock_guard<std::recursive_mutex> lock(context->Gate);
        OriginalObject original(factory,*context);if(!original.Valid)return E_FAIL;
        return a::CallTable<HRESULT>(factory,context->Original.data(),0,iid,output);
    }
}
namespace dfl::d3d8_hooks
{
    bool UsesFixedFrameInput(HWND window) noexcept
    {
        std::lock_guard<std::mutex> lock(ContextMutex);
        for(const auto& entry:Devices)
        {
            const auto& context=*entry.second;
            if(context.Window==window&&context.Windowed&&context.FixedFrame&&context.Policy.Filter!=p::Mode::Legacy)return true;
        }
        return false;
    }
    bool AttachFactory(void* factory,const Options& options) noexcept
    {
        try
        {
            if(!factory||!*static_cast<void***>(factory))return false;if(Find(Factories,factory))return true;
            auto context=std::make_shared<FactoryContext>(factory,options);
            context->Clone[0]=reinterpret_cast<void*>(&FactoryQuery);context->Clone[2]=reinterpret_cast<void*>(&FactoryRelease);context->Clone[15]=reinterpret_cast<void*>(&CreateProxy);
            std::lock_guard<std::mutex> lock(ContextMutex);
            const auto inserted=Factories.emplace(factory,context);
            if(!inserted.second)return true;
            if(!Replace(factory,context->Clone.data())){Factories.erase(inserted.first);return false;}return true;
        }
        catch(...){return false;}
    }
}
