#include "../../DFLegacy.Ijl15/src/performance_overlay.cpp"
#include <windows.h>
#include <cstdio>
#include "../player_runtime.h"
#include "../../DFLegacy.Debug/include/debug_api.h"
#include "../bootstrap_runtime.h"
#include "../../DFLegacy.Debug/src/client_navigation.h"
namespace { unsigned captures=0,frames=0,starts=0,stops=0;bool standalone=true;dfl::client_bootstrap::Mode mode=dfl::client_bootstrap::Mode::Normal; }
namespace dfl::debug_module { Pipeline::Pipeline() noexcept{} void Pipeline::Mark() noexcept{} Pipeline::~Pipeline() noexcept{} void Render() noexcept{} }
namespace dfl::ijl15_config { const Settings& Get() noexcept{static const Settings settings{};return settings;} }
namespace dfl::auto_input { bool IsEnabled() noexcept{return false;}void Pump() noexcept{} }
namespace dfl::memory_patch { bool WriteRelativeCall(std::uintptr_t,std::uintptr_t) noexcept{return false;} }
namespace dfl::client_bootstrap { bool IsStandalone() noexcept{return standalone;}Mode GetMode() noexcept{return mode;} }
namespace dfl::client_state {
void Initialize(const wchar_t*,const wchar_t*,bool) noexcept{}
void CaptureFrame() noexcept{++captures;}
void ExportLatest() noexcept{}
Reader NativeReader() noexcept{return {};}
bool CopyLatest(Snapshot&) noexcept{return true;}
void Shutdown() noexcept{}
}
namespace dfl::client_navigation {
void Initialize(const wchar_t*,const wchar_t*,client_state::Reader,bool,bool) noexcept{++starts;}
void OnFrame() noexcept{++frames;}
void PumpWorker() noexcept{}
bool Shutdown() noexcept{++stops;return true;}
}
void __stdcall NoDraw(int,int,std::uint32_t,const char*) {}
int main()
{
 int failed=0;auto check=[&](bool ok,const char* s){if(!ok){++failed;std::fprintf(stderr,"FAIL player: %s\n",s);}};
 using namespace dfl::player_runtime;
 check(!DiagnosticsAllowed(),"normal_mode_ignores_leftover_debug");
 check(!dfl::client_bootstrap::AllowTestInput(true),"normal mode ignores stale input opt-in");
 const auto rendererPage=VirtualAlloc(reinterpret_cast<void*>(0xBF0000),0x10000,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);
 if(!rendererPage){MEMORY_BASIC_INFORMATION info{};VirtualQuery(reinterpret_cast<void*>(0xBF0000),&info,sizeof(info));std::fprintf(stderr,"renderer allocation error=%lu base=%p owner=%p state=%lx type=%lx image=%p\n",GetLastError(),info.BaseAddress,info.AllocationBase,info.State,info.Type,GetModuleHandleW(nullptr));}
 check(rendererPage!=nullptr,"synthetic renderer page allocated without any game");
 if(rendererPage){DrawFpsAndMemory(nullptr,nullptr);DrawFpsAndMemory(nullptr,nullptr);VirtualFree(rendererPage,0,MEM_RELEASE);}
 check(captures==2&&frames==2&&starts==1,"ordinary_navigation_without_debug_or_export_state_and_FPS_off");
 const auto* shared=GetSharedState();check(shared&&shared->size==sizeof(*shared)&&shared->version==SharedStateVersion,"versioned shared state");
 Shutdown();mode=dfl::client_bootstrap::Mode::Test;
 OnFrame();Initialize();OnFrame();check(starts==2&&frames==4,"test_mode_does_not_duplicate_hooks");
 const auto module=LoadLibraryW(L"debug.dll");check(module!=nullptr,"matching diagnostic DLL available");
 if(module)
 {
    const auto initialize=reinterpret_cast<dfl::debug_api::Initialize>(GetProcAddress(module,"DebugInitialize"));
    const auto shutdown=reinterpret_cast<dfl::debug_api::Shutdown>(GetProcAddress(module,"DebugShutdown"));
    dfl::debug_api::Host host{sizeof(dfl::debug_api::Host),dfl::debug_api::Version,&NoDraw,nullptr,nullptr,GetSharedState()};
    check(initialize&&shutdown,"diagnostic ABI exports");
    if(initialize)
    {
        auto invalid=host;invalid.version=2;check(!initialize(&invalid,L"unused-observer-fixture"),"reject v2 host");
        invalid=host;invalid.size--;check(!initialize(&invalid,L"unused-observer-fixture"),"reject wrong Host size");
        auto invalidState=*GetSharedState();invalidState.snapshotSize--;invalid=host;invalid.sharedState=&invalidState;
        check(!initialize(&invalid,L"unused-observer-fixture"),"reject wrong shared snapshot size");
        invalidState=*GetSharedState();invalidState.version++;check(!initialize(&invalid,L"unused-observer-fixture"),"reject wrong shared version");
    }
    if(initialize&&shutdown)for(unsigned i=0;i<3;++i)
    {
        check(initialize(&host,L"unused-observer-fixture")==1,"diagnostics attach to shared owner");
        check(shutdown()==1,"diagnostics shutdown succeeds");
        const auto prior=frames;OnFrame();check(starts==2&&stops==1&&frames==prior+1,"diagnostic shutdown leaves ordinary hooks owned and ticking");
    }
    FreeLibrary(module);
 }
 check(DiagnosticsAllowed(),"test diagnostics available");
 check(!dfl::client_bootstrap::AllowTestInput(false)&&dfl::client_bootstrap::AllowTestInput(true),"test_mode_keeps_input_control_opt_in");Shutdown();check(stops==2,"core owns shutdown once");
 std::printf("player runtime: %d failure(s)\n",failed);return failed;
}
