// Exercise production Install/Render/LoadModule and INI parsing. Only the game
// memory/renderer and bootstrap's already-authenticated mode are fixture seams.
#include "../../DFLegacy.Ijl15/src/debug_module.cpp"
#include "../bootstrap_runtime.h"
#include <filesystem>
#include <fstream>
#include <string>
namespace fs = std::filesystem;
namespace { bool standalone = true; dfl::client_bootstrap::Mode mode = dfl::client_bootstrap::Mode::Normal; }
namespace dfl::client_bootstrap {
bool IsStandalone() noexcept { return standalone; }
Mode GetMode() noexcept { return mode; }
}
namespace dfl::player_runtime {
bool DiagnosticsAllowed() noexcept { return !client_bootstrap::IsStandalone() || client_bootstrap::GetMode() == client_bootstrap::Mode::Test; }
const SharedState* GetSharedState() noexcept { return nullptr; }
}
namespace dfl::performance_overlay { void DrawOverlayText(int, int, std::uint32_t, const char*) noexcept {} }
namespace dfl::native_loop_probe { void Configure(debug_api::PipelineEnabled,debug_api::RecordNativeLoop,void*,const std::uint32_t*) noexcept {} }
namespace dfl::memory_patch {
bool WriteBytes(std::uintptr_t,const void*,std::size_t) noexcept { return false; }
bool WriteRelativeCall(std::uintptr_t,std::uintptr_t) noexcept { return false; }
}
std::string Read(const fs::path& path) { std::ifstream stream(path, std::ios::binary); return {std::istreambuf_iterator<char>(stream), {}}; }
int wmain(int argc, wchar_t** argv)
{
    if (argc != 4) return 2;
    if (std::wstring(argv[1]) != L"--child")
    {
        wchar_t temp[MAX_PATH]{}, self[MAX_PATH]{};
        GetTempPathW(MAX_PATH,temp); GetModuleFileNameW(nullptr,self,MAX_PATH);
        const auto directory = fs::path(temp) / (L"dfl-debug-loading-" + std::to_wstring(GetCurrentProcessId()));
        fs::create_directory(directory);
        fs::copy_file(self,directory/L"fixture.exe");
        fs::copy_file(argv[1],directory/L"debug.dll");
        auto command = L"\"" + (directory/L"fixture.exe").wstring() + L"\" --child " + argv[2] + L" " + argv[3];
        STARTUPINFOW startup{}; startup.cb=sizeof(startup); PROCESS_INFORMATION child{};
        if (!CreateProcessW(nullptr,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,directory.c_str(),&startup,&child)) return 3;
        if (WaitForSingleObject(child.hProcess,30000) != WAIT_OBJECT_0)
        { TerminateProcess(child.hProcess,99); WaitForSingleObject(child.hProcess,5000); }
        DWORD code=99; GetExitCodeProcess(child.hProcess,&code);
        CloseHandle(child.hThread); CloseHandle(child.hProcess); fs::remove_all(directory);
        std::printf("debug loading mode=%ls key=%ls exit=%lu\n",argv[2],argv[3],code); return static_cast<int>(code);
    }
    standalone = std::wstring(argv[2]) != L"legacy";
    mode = std::wstring(argv[2]) == L"test" ? dfl::client_bootstrap::Mode::Test : dfl::client_bootstrap::Mode::Normal;
    const bool flag = std::wstring(argv[3]) == L"1";
    const bool expected = standalone ? mode == dfl::client_bootstrap::Mode::Test : flag;
    const auto config = fs::current_path()/L"Config.ini";
    { std::ofstream file(config); file << "[Features]\n"; if (std::wstring(argv[3]) != L"missing") file << "EnableDebugOverlay=" << (flag ? 1 : 0) << "\n"; }
    const auto before = Read(config);
    // A visible owned window is required by the real loader; never activate it.
    const auto window = CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"DFLegacy loading fixture",WS_POPUP,0,0,1,1,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    if (!window) return 4;
    ShowWindow(window,SW_SHOWNOACTIVATE);
    dfl::debug_module::Install(); dfl::debug_module::Render();
    const bool loaded = GetModuleHandleW(L"debug.dll") != nullptr;
    const bool unchanged = Read(config) == before;
    const bool inputOff = !dfl::client_bootstrap::AllowTestInput(dfl::ijl15_config::Get().EnableAutoInput);
    DestroyWindow(window);
    if (loaded != expected || !unchanged || !inputOff)
    { std::fprintf(stderr,"FAIL expected loaded=%d actual=%d config preserved=%d input off=%d\n",expected,loaded,unchanged,inputOff); return 1; }
    return 0;
}
