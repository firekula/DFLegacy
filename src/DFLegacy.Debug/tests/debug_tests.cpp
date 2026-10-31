#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <wct.h>
#include <dbghelp.h>
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "dbghelp.lib")
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#include <atomic>
#include "../include/debug_api.h"
#include "../src/debug_model.h"
#include "../../DFLegacy.Ijl15/src/client_debug_state.h"

namespace
{
    int Failures = 0;
    void Check(bool value,const char* message);
    constexpr UINT ResizeFixtureMessage = WM_APP + 0x315;
    std::atomic<WNDPROC> ResizeOriginalProcedure{nullptr};
    LRESULT CALLBACK ResizeFixtureProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
    {
        const auto original = ResizeOriginalProcedure.load(std::memory_order_acquire);
        if (message == ResizeFixtureMessage)
        {
            // Real nonclient sizing and WM_EXITSIZEMOVE run on this window's thread.
            // Keep that transaction together: the periodic worker must not re-place
            // the old bounds between a foreign-thread SetWindowPos and its commit.
            if (!SetWindowPos(window, nullptr, 0, 0, LOWORD(wparam), HIWORD(wparam),
                SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)) return 0;
            CallWindowProcW(original, window, WM_EXITSIZEMOVE, 0, 0);
            return 1;
        }
        return CallWindowProcW(original, window, message, wparam, lparam);
    }
    bool ResizeOnWindowThread(HWND window, int width, int height)
    {
        const auto original = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window, GWLP_WNDPROC));
        if (original == nullptr) return false;
        ResizeOriginalProcedure.store(original, std::memory_order_release);
        const auto previous = SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&ResizeFixtureProcedure));
        if (previous == 0) return false;
        const auto result = SendMessageW(window, ResizeFixtureMessage, MAKEWPARAM(width, height), 0);
        SetWindowLongPtrW(window, GWLP_WNDPROC, previous);
        return result == 1;
    }
    DWORD ObserverBefore=0,ObserverAfter=0;
    FARPROC ObserverEntry=nullptr;
    int CheckObserverAbi(const char* path)
    {
        const auto module=LoadLibraryA(path);
        Check(module!=nullptr,"debug DLL loads for observer ABI check");
        if(!module)return 1;
        for(const auto name:{"DebugObserveGameplay","DebugObserveGameplayV2"})
        {
            ObserverEntry=GetProcAddress(module,name);
            Check(ObserverEntry!=nullptr,name);
            if(!ObserverEntry)continue;
            const bool v2=std::strcmp(name,"DebugObserveGameplayV2")==0;
            // Restore ESP explicitly so an incompatible callee is reported,
            // without corrupting the test runner's own return stack.
            __asm { mov ObserverBefore,esp }
            if(v2){__asm { push 0 }}
            __asm
            {
                push 0
                push 0
                push 0
                push 0
                call ObserverEntry
                mov ObserverAfter,esp
                mov esp,ObserverBefore
            }
            Check(ObserverBefore==ObserverAfter,"optional observer export preserves its stdcall stack width");
        }
        FreeLibrary(module);return Failures?1:0;
    }
    std::vector<std::wstring> Lines;
    std::vector<const char*> RawLines;
    std::vector<std::string> ExpectedLines;
    void Check(bool value, const char* message)
    {
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL: %s\n", message); }
    }
    void __stdcall Draw(int, int, std::uint32_t, const char* gbk)
    {
        wchar_t value[512]{};
        MultiByteToWideChar(936, 0, gbk, -1, value, 512);
        Lines.emplace_back(value);
        RawLines.push_back(gbk);
        ExpectedLines.emplace_back(gbk);
    }
    bool Contains(const wchar_t* text)
    {
        for (const auto& line : Lines) if (line.find(text) != std::wstring::npos) return true;
        return false;
    }
    std::uint64_t Now()
    {
        FILETIME time{}; GetSystemTimeAsFileTime(&time);
        return (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
    }
}

int RunClientStateTests();
int RunGameplayTraceTests();
int RunNativeLoopProfileTests();
int RunFrameProfileTests();
int RunFrameProfileRuntimeTests();
int RunTownRouteCatalogTests();
int RunTownRouteControllerTests();
int RunTownRouteRuntimeTests();
int RunTownRouteIoTests();
int RunClientNavigationTests();
int RunClientNavigationRuntimeTests();
int RunClientPartyTests();
int RunClientPartyRuntimeTests();
int RunMapClickFeedbackTests();
int RunMapClickFeedbackRuntimeTests();
int RunClientTestControlTests();
int RunClientTestControlRuntimeTests();

int main(int argc, char** argv)
{
    if(argc==2&&std::strcmp(argv[1],"--native-loop")==0)return RunNativeLoopProfileTests();
    if(argc==3&&std::strcmp(argv[1],"--gameplay-abi")==0)return CheckObserverAbi(argv[2]);
    if(argc==2&&std::strcmp(argv[1],"--gameplay-trace")==0)return RunGameplayTraceTests();
    if(argc==2&&std::strcmp(argv[1],"--frame-profile")==0)return RunFrameProfileTests()+RunFrameProfileRuntimeTests();
    if(argc==2&&std::strcmp(argv[1],"--client-test-control")==0)return RunClientTestControlTests()+RunClientTestControlRuntimeTests();
    if (argc == 2 && std::strcmp(argv[1], "--map-feedback") == 0) return RunMapClickFeedbackTests()+RunMapClickFeedbackRuntimeTests();
    if (argc == 2 && std::strcmp(argv[1], "--client-party") == 0) return RunClientPartyTests()+RunClientPartyRuntimeTests();
    if (argc == 2 && std::strcmp(argv[1], "--client-state") == 0) return RunClientStateTests();
    if (argc == 2 && std::strcmp(argv[1], "--client-navigation") == 0)
        return RunClientNavigationTests() + RunClientNavigationRuntimeTests();
    if (argc == 2 && std::strcmp(argv[1], "--town-route") == 0) return RunTownRouteCatalogTests()+RunTownRouteControllerTests()+RunTownRouteRuntimeTests()+RunTownRouteIoTests();
    using namespace dfl;
    if (argc < 2 || argc > 3) return 2;
    Failures += RunMapClickFeedbackTests()+RunMapClickFeedbackRuntimeTests();
    Failures += RunClientTestControlTests()+RunClientTestControlRuntimeTests();
    std::uint32_t manager[32]{}, valuesA[4]{}, valuesB[4]{}, wrapper[2]{};
    constexpr std::uint32_t key = 0x12345678;
    manager[1] = 4;
    manager[0x44 / 4] = reinterpret_cast<std::uintptr_t>(valuesA);
    manager[0x48 / 4] = reinterpret_cast<std::uintptr_t>(valuesB);
    wrapper[0] = key ^ (reinterpret_cast<std::uintptr_t>(wrapper) + 1);
    wrapper[1] = 1;
    valuesA[1] = 2 ^ key ^ reinterpret_cast<std::uintptr_t>(&valuesA[1]);
    valuesB[1] = 2 ^ key ^ reinterpret_cast<std::uintptr_t>(&valuesB[1]);
    std::uint32_t uid = 0;
    Check(debug_module::detail::ReadProtected(reinterpret_cast<std::uintptr_t>(wrapper),
        reinterpret_cast<std::uintptr_t>(manager), key, uid) && uid == 2,
        "player UID comes from the protected value, never object kind 0x111");
    wrapper[1] = 999;
    Check(!debug_module::detail::ReadProtected(reinterpret_cast<std::uintptr_t>(wrapper),
        reinterpret_cast<std::uintptr_t>(manager), key, uid), "transitional wrappers do not call native assertion or repair routines");
    std::ifstream fixture(argv[1], std::ios::binary);
    std::vector<char> bytes((std::istreambuf_iterator<char>(fixture)), {});
    debug_model::Snapshot snapshot{};
    Check(debug_model::Decode(bytes.data(), bytes.size(), snapshot), "C# generated fixture decodes in C++");
    if (Failures) return 1;
    Check(snapshot.header.userId == 77 && snapshot.header.count == 2
        && snapshot.monsters[0].box == 2 && snapshot.monsters[0].x == 353
        && snapshot.monsters[0].y == 264 && snapshot.monsters[1].state == 3, "cross-language roster and coordinates match");
    Check(snapshot.drop.enabled == 1 && snapshot.drop.allowed == 1 && snapshot.drop.result == 1
        && snapshot.drop.requestId == 123, "server control trailer exposes actual state, permission and acknowledgment");
    auto legacy = bytes; legacy.resize(legacy.size() - 12); legacy[4] = 1;
    debug_model::Snapshot old{};
    Check(debug_model::Decode(legacy.data(), legacy.size(), old) && old.drop.allowed == 0,
        "v1 snapshots remain readable with drop control unavailable");
    auto invalidControl = bytes; invalidControl[invalidControl.size() - 12] = 2;
    Check(!debug_model::Decode(invalidControl.data(), invalidControl.size(), old), "invalid control flags rejected");
    auto bad = bytes;
    bad[38] = static_cast<char>(255);
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "truncated advertised roster rejected");
    bad = bytes; bad.push_back(0);
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "trailing garbage rejected");
    bad = bytes; bad[46] = 4;
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "unknown monster state rejected");
    bad = bytes; bad[52] = bad[40]; bad[53] = bad[41];
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "duplicate actor IDs rejected");
    debug_model::Decode(bytes.data(), bytes.size(), snapshot);
    const auto now = Now(); snapshot.header.fileTime = now;
    Check(debug_model::IsCurrent(snapshot, 77, now, now - 1), "fresh matching roster accepted");
    Check(!debug_model::IsCurrent(snapshot, 78, now, 0)
        && !debug_model::IsCurrent(snapshot, 77, now + 16000000, 0)
        && !debug_model::IsCurrent(snapshot, 77, now, now + 1), "wrong user, stale and previous-entry snapshots rejected");

    wchar_t temp[MAX_PATH]{}, directory[MAX_PATH]{}, file[MAX_PATH]{};
    GetTempPathW(MAX_PATH, temp);
    swprintf_s(directory, L"%sdfdebug-test-%lu", temp, GetCurrentProcessId());
    CreateDirectoryW(directory, nullptr);
    swprintf_s(file, L"%s\\dfdebug-77.bin", directory);
    std::memcpy(bytes.data() + 8, &now, sizeof(now));
    const HANDLE output = CreateFileW(file, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (output == INVALID_HANDLE_VALUE)
    {
        std::fwprintf(stderr, L"fixture path error=%lu path=%s\n", GetLastError(), file);
        return 2;
    }
    DWORD written = 0;
    Check(output != INVALID_HANDLE_VALUE && WriteFile(output, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr), "write own temporary diagnostic fixture");
    if (output != INVALID_HANDLE_VALUE) CloseHandle(output);
    const HMODULE module = LoadLibraryW(L"debug.dll");
    Check(module != nullptr, "standalone debug.dll loads");
    if (module == nullptr) return 1;
    const auto initialize = reinterpret_cast<debug_api::Initialize>(GetProcAddress(module, "DebugInitialize"));
    const auto render = reinterpret_cast<debug_api::Render>(GetProcAddress(module, "DebugRender"));
    const auto shutdown = reinterpret_cast<debug_api::Shutdown>(GetProcAddress(module, "DebugShutdown"));
    Check(initialize && render && shutdown, "versioned undecorated entry points exist");
    if (!initialize || !render || !shutdown) return 1;
    wchar_t stateSettings[MAX_PATH]{}, clientStateFile[MAX_PATH]{};
    swprintf_s(stateSettings, L"%s\\state-settings.ini", directory);
    swprintf_s(clientStateFile, L"%s\\dfdebug-client-%lu.json", directory, GetCurrentProcessId());
    WritePrivateProfileStringW(L"DebugOverlay", L"ExportState", L"1", stateSettings);
    debug_api::Host host{sizeof(debug_api::Host), debug_api::Version, &Draw, nullptr, stateSettings};
    Check(initialize(&host, directory) == 1, "DLL initializes outside loader lock");
    debug_api::Frame frame{sizeof(debug_api::Frame), 3, 77, 0, 0};
    for (int i = 0; i < 15; ++i)
    {
        Lines.clear(); RawLines.clear(); ExpectedLines.clear(); render(&frame);
        if (Contains(L"BOSS房箱怪：有")) break;
        Sleep(50);
    }
    Check(Contains(L"BOSS房箱怪：有") && Contains(L"容器[2] (353,264)"), "real DLL draws Chinese game overlay through host callback");
    bool stable = !RawLines.empty();
    for (std::size_t i = 0; i < RawLines.size(); ++i)
        stable = stable && std::strcmp(RawLines[i], ExpectedLines[i].c_str()) == 0;
    Check(stable, "every queued native text pointer remains valid after the render callback returns");
    Lines.clear(); frame.userId = 78; render(&frame);
    Check(!Contains(L"BOSS房箱怪：有"), "another player's snapshot is never shown");
    auto clientStateSequence = [&]() -> std::uint64_t
    {
        std::ifstream state(clientStateFile, std::ios::binary);
        const std::string value((std::istreambuf_iterator<char>(state)), {});
        const auto at = value.find("\"sequence\":");
        if (at == std::string::npos) return 0;
        return std::strtoull(value.c_str() + at + 11, nullptr, 10);
    };
    const auto priorClientStateSequence = clientStateSequence();
    Lines.clear(); frame.scene = 1; render(&frame);
    Check(Lines.empty(), "overlay does not render outside dungeon scene");
    // Publication is deliberately nonblocking and may skip a contended frame.
    // Drive later frames until the asynchronous worker publishes, with a bound.
    bool exportedHiddenScene = false;
    const auto exportDeadline = GetTickCount64() + 2000;
    do
    {
        render(&frame); Sleep(25);
        std::ifstream state(clientStateFile, std::ios::binary);
        const std::string exported((std::istreambuf_iterator<char>(state)), {});
        exportedHiddenScene = exported.find("\"layout_supported\":false") != std::string::npos
            && clientStateSequence() > priorClientStateSequence;
    } while (!exportedHiddenScene && GetTickCount64() < exportDeadline);
    Check(exportedHiddenScene,
        "real DLL captures and worker exports even when old Frame scene hides overlay");
    Check(shutdown() == 1, "diagnostic worker stops before DLL unload");

    const HWND owner = CreateWindowExW(0, L"STATIC", L"DFLegacy Debug Overlay Preview", WS_OVERLAPPEDWINDOW,
        160, 120, 680, 540, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    ShowWindow(owner, SW_SHOW); SetForegroundWindow(owner);
    host.gameWindow = owner;
    Check(initialize(&host, directory) == 1, "floating window initializes without deadlocking the owner thread");
    frame.scene = 3; frame.userId = 77;
    auto pump = [&](unsigned milliseconds)
    {
        const auto deadline = GetTickCount64() + milliseconds;
        do
        {
            render(&frame);
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
            Sleep(10);
        } while (GetTickCount64() < deadline);
    };
    const bool interactiveCursor = argc == 3 && std::strcmp(argv[2], "--interactive-cursor") == 0;
    if (interactiveCursor)
    {
        std::printf("Interactive cursor fixture ready; activate its preview window.\n"); std::fflush(stdout);
        const auto deadline = GetTickCount64() + 120000;
        while (GetForegroundWindow() != owner && GetTickCount64() < deadline) pump(25);
        Check(GetForegroundWindow() == owner, "interactive cursor fixture owns the foreground before testing real cursor visibility");
        const auto fresh = Now(); std::memcpy(bytes.data() + 8, &fresh, sizeof(fresh));
        const HANDLE refreshed = CreateFileW(file, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE,
            nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (refreshed != INVALID_HANDLE_VALUE)
        {
            WriteFile(refreshed, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
            CloseHandle(refreshed);
        }
    }
    pump(400); // allow one full 250 ms snapshot poll after the first render supplies the UID
    HWND panel = nullptr;
    do { panel = FindWindowExW(nullptr, panel, L"DFLegacy.DebugOverlay.v2", nullptr); }
    while (panel != nullptr && GetWindow(panel, GW_OWNER) != owner);
    Check(panel != nullptr && GetWindow(panel, GW_OWNER) == owner, "floating panel belongs to the game window");
    Check((GetWindowLongPtrW(panel, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0,
        "floating panel never takes keyboard focus from gameplay");
    if (interactiveCursor || GetForegroundWindow() == owner)
    {
    // Reproduce the game hiding the OS cursor beneath an opaque owned panel.
    // Move only our fixture window beneath the existing pointer; do not inject input.
    POINT pointer{}; GetCursorPos(&pointer);
    RECT panelBefore{}, ownerBefore{};
    GetWindowRect(panel, &panelBefore); GetWindowRect(owner, &ownerBefore);
    int hiddenCount = 0, hideCalls = 0;
    do { hiddenCount = ShowCursor(FALSE); ++hideCalls; } while (hiddenCount >= 0 && hideCalls < 32);
    SetWindowPos(owner, nullptr,
        ownerBefore.left + pointer.x - panelBefore.left - 20,
        ownerBefore.top + pointer.y - panelBefore.top - 48,
        0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    pump(100);
    CURSORINFO cursorInfo{sizeof(cursorInfo)}; GetCursorInfo(&cursorInfo);
    if ((cursorInfo.flags & CURSOR_SHOWING) == 0)
    {
        POINT observed{}; GetCursorPos(&observed);
        RECT observedRect{}; GetWindowRect(panel, &observedRect);
        std::fprintf(stderr, "pointer diag fg=%p owner=%p panel=%p visible=%d hit=%p pointer=%ld,%ld rect=%ld,%ld,%ld,%ld counter=%d calls=%d\n",
            GetForegroundWindow(), owner, panel, IsWindowVisible(panel), WindowFromPoint(observed), observed.x, observed.y,
            observedRect.left, observedRect.top, observedRect.right, observedRect.bottom, hiddenCount, hideCalls);
    }
    Check((cursorInfo.flags & CURSOR_SHOWING) != 0,
        "OS cursor becomes visible over the panel even when the game hides it");
    SendMessageW(panel, WM_CLOSE, 0, 0); pump(75);
    const int restoredCounter = ShowCursor(TRUE);
    Check(restoredCounter == hiddenCount + 1,
        "hiding the panel exactly restores the game's cursor display counter");
    for (int i = 1; i < hideCalls; ++i) ShowCursor(TRUE);
    frame.scene = 1; pump(150); frame.scene = 3; pump(150);
    SetWindowPos(owner, nullptr, ownerBefore.left, ownerBefore.top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    pump(100);
    }
    else std::printf("Real cursor check requires --interactive-cursor; message/decoder tests continue.\n");
    auto refreshFixture = [&]()
    {
        const auto currentTime = Now(); std::memcpy(bytes.data() + 8, &currentTime, 8);
        const HANDLE refresh = CreateFileW(file, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (refresh != INVALID_HANDLE_VALUE) { WriteFile(refresh, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr); CloseHandle(refresh); }
        pump(350);
    };
    refreshFixture();
    wchar_t requestPath[MAX_PATH]{};
    swprintf_s(requestPath, L"%s\\dfdebug-drop-77.bin", directory);
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48));
    SendMessageW(panel, WM_LBUTTONUP, 0, MAKELPARAM(25, 48));
    pump(40);
    std::ifstream command(requestPath, std::ios::binary);
    std::vector<char> requestBytes((std::istreambuf_iterator<char>(command)), {}); command.close();
    Check(requestBytes.size() == 32, "panel switch publishes the bounded local control request");
    debug_model::DropRequest control{};
    if (requestBytes.size() == sizeof(control)) std::memcpy(&control, requestBytes.data(), sizeof(control));
    Check(control.magic == 0x31504444 && control.version == 1 && control.userId == 77
        && control.runId == 123 && control.enabled == 0 && control.requestId > 0,
        "checked switch requests explicit off for the current player/run");
    DeleteFileW(requestPath);
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48)); pump(20);
    Check(GetFileAttributesW(requestPath) == INVALID_FILE_ATTRIBUTES,
        "pending switch cannot emit a second conflicting request");
    DeleteFileW(file); pump(350);
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48)); pump(20);
    Check(GetFileAttributesW(requestPath) == INVALID_FILE_ATTRIBUTES,
        "missing snapshot disables control while the outstanding request is preserved");
    refreshFixture();
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48)); pump(20);
    Check(GetFileAttributesW(requestPath) == INVALID_FILE_ATTRIBUTES,
        "restoring an unacknowledged snapshot keeps the original request pending");
    DeleteFileW(requestPath);
    // Acknowledge off through the same server snapshot format, then click on.
    bytes[bytes.size() - 12] = 0;
    std::memcpy(bytes.data() + bytes.size() - 8, &control.requestId, 8);
    refreshFixture();
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48)); pump(20);
    Check(GetFileAttributesW(requestPath) != INVALID_FILE_ATTRIBUTES, "acknowledgment enables the switch for the next request");
    DeleteFileW(requestPath);
    frame.userId = 78; pump(50);
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(25, 48)); pump(20);
    Check(GetFileAttributesW(requestPath) == INVALID_FILE_ATTRIBUTES, "different user disables the old snapshot's switch");
    frame.userId = 77; refreshFixture();
    RECT full{}; GetWindowRect(panel, &full);
    const LPARAM titlePoint = MAKELPARAM(full.left + 20, full.top + 15);
    Check(SendMessageW(panel, WM_NCHITTEST, 0, titlePoint) == HTCAPTION
        && SendMessageW(panel, WM_NCHITTEST, 0, MAKELPARAM(full.right - 2, full.bottom - 2)) == HTBOTTOMRIGHT,
        "title dragging and corner resizing have native hit targets");
    const HWND focusedBefore = GetForegroundWindow();
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(full.right - full.left - 45, 15));
    pump(50);
    RECT dot{}; GetWindowRect(panel, &dot);
    const HRGN region = CreateRectRgn(0, 0, 0, 0);
    Check(dot.right - dot.left == 30 && dot.bottom - dot.top == 30 && GetWindowRgn(panel, region) == COMPLEXREGION,
        "minimize produces a circular 30-pixel button");
    if (dot.right - dot.left != 30 || dot.bottom - dot.top != 30)
        std::fprintf(stderr, "dot dimensions=%ldx%ld\n", dot.right - dot.left, dot.bottom - dot.top);
    DeleteObject(region);
    SendMessageW(panel, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(15, 15));
    SendMessageW(panel, WM_LBUTTONUP, 0, MAKELPARAM(15, 15));
    pump(50);
    RECT restored{}; GetWindowRect(panel, &restored);
    Check(restored.right - restored.left == full.right - full.left,
        "clicking the dot restores the panel size");
    Check(GetForegroundWindow() == focusedBefore, "panel controls preserve the foreground window");
    Check(ResizeOnWindowThread(panel, 320, 180), "resize transaction completes on the panel thread");
    pump(80);
    RECT resized{}; GetWindowRect(panel, &resized);
    std::printf("Resize transaction: final=%ldx%ld foreground-owner=%d\n",
        resized.right-resized.left, resized.bottom-resized.top, GetForegroundWindow()==owner);
    Check(resized.right - resized.left == 320 && resized.bottom - resized.top == 180,
        "user resizing survives subsequent render updates");
    SendMessageW(panel, WM_CLOSE, 0, 0); pump(50);
    Check(!IsWindowVisible(panel), "close hides without destroying the restorable panel");
    frame.scene = 1; pump(150); frame.scene = 3; pump(150);
    Check(GetForegroundWindow() != owner || IsWindowVisible(panel), "a new dungeon reveals the panel when its game is foreground");
    if (argc == 3 && std::strcmp(argv[2], "--preview") == 0)
    {
        SetWindowPos(panel, nullptr, 0, 0, 500, 260, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        SendMessageW(panel, WM_EXITSIZEMOVE, 0, 0);
        const auto deadline = GetTickCount64() + 300000;
        while (IsWindow(owner) && GetTickCount64() < deadline)
        {
            const auto currentTime = Now(); std::memcpy(bytes.data() + 8, &currentTime, sizeof(currentTime));
            const HANDLE refresh = CreateFileW(file, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (refresh != INVALID_HANDLE_VALUE) { WriteFile(refresh, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr); CloseHandle(refresh); }
            pump(100);
        }
    }
    if (interactiveCursor)
        for (unsigned i = 0; i < 256; ++i) PostMessageW(panel, WM_PAINT, 0, 0);
    const bool stopped = shutdown() == 1;
    Check(stopped, "floating window and worker stop safely before unloading");
    if (!stopped)
    {
        const auto waitingThread = GetWindowThreadProcessId(panel, nullptr);
        const HANDLE threadHandle = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, waitingThread);
        if (threadHandle)
        {
            SymInitialize(GetCurrentProcess(), nullptr, TRUE);
            SuspendThread(threadHandle);
            CONTEXT context{}; context.ContextFlags = CONTEXT_FULL;
            if (GetThreadContext(threadHandle, &context))
            {
                STACKFRAME64 stack{};
                stack.AddrPC = {context.Eip, 0, AddrModeFlat};
                stack.AddrStack = {context.Esp, 0, AddrModeFlat};
                stack.AddrFrame = {context.Ebp, 0, AddrModeFlat};
                for (unsigned i = 0; i < 24 && StackWalk64(IMAGE_FILE_MACHINE_I386, GetCurrentProcess(), threadHandle,
                    &stack, &context, nullptr, SymFunctionTableAccess64, SymGetModuleBase64, nullptr); ++i)
                {
                    char storage[sizeof(SYMBOL_INFO) + 256]{};
                    auto* symbol = reinterpret_cast<SYMBOL_INFO*>(storage); symbol->SizeOfStruct = sizeof(SYMBOL_INFO); symbol->MaxNameLen = 255;
                    DWORD64 displacement = 0;
                    SymFromAddr(GetCurrentProcess(), stack.AddrPC.Offset, &displacement, symbol);
                    std::fprintf(stderr, "worker frame %llX %s + %llu\n", stack.AddrPC.Offset, symbol->Name, displacement);
                }
            }
            ResumeThread(threadHandle); CloseHandle(threadHandle); SymCleanup(GetCurrentProcess());
        }
        const auto session = OpenThreadWaitChainSession(0, nullptr);
        if (session)
        {
            WAITCHAIN_NODE_INFO nodes[WCT_MAX_NODE_COUNT]{};
            DWORD count = WCT_MAX_NODE_COUNT; BOOL cycle = FALSE;
            const auto thread = GetWindowThreadProcessId(panel, nullptr);
            if (GetThreadWaitChain(session, 0, WCTP_GETINFO_ALL_FLAGS, thread, &count, nodes, &cycle))
                for (DWORD i = 0; i < count; ++i)
                    std::fprintf(stderr, "worker wait type=%u status=%u pid=%lu tid=%lu cycle=%d\n",
                        nodes[i].ObjectType, nodes[i].ObjectStatus, nodes[i].ThreadObject.ProcessId, nodes[i].ThreadObject.ThreadId, cycle);
            CloseThreadWaitChainSession(session);
        }
        return 1; // never unload code still executing on the diagnostic worker
    }
    Check(!IsWindow(panel), "shutdown destroys the owned panel");
    if (IsWindow(owner)) DestroyWindow(owner);
    FreeLibrary(module);
    DeleteFileW(clientStateFile); DeleteFileW(stateSettings);
    DeleteFileW(file); RemoveDirectoryW(directory);
    std::printf("Debug overlay tests: %d failures\n", Failures);
    return Failures ? 1 : 0;
}
