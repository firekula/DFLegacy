#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cwchar>
#include "../include/debug_api.h"
#include "debug_model.h"
#include "debug_window.h"
#include "client_state.h"
#include "shared_state_bridge.h"
#include "client_party.h"
#include "client_test_control.h"
#include "frame_profile.h"
#include "gameplay_trace.h"
#include "native_loop_profile.h"

namespace
{
    dfl::debug_api::Host Host{};
    wchar_t Directory[MAX_PATH]{};
    HANDLE StopEvent = nullptr;
    HANDLE Worker = nullptr;
    HANDLE ReadyEvent = nullptr;
    bool WindowReady = false;
    SRWLOCK SnapshotLock = SRWLOCK_INIT;
    dfl::debug_model::Snapshot Snapshot{};
    bool HaveSnapshot = false;
    volatile LONG UserId = 0;
    bool Visible = true, Details = true, InDungeon = false;
    bool F9Down = false, F10Down = false;
    std::array<std::array<char, 512>, 16> TextBuffers{};
    std::size_t TextCount = 0;
    SRWLOCK ContentLock = SRWLOCK_INIT;
    dfl::debug_window::Content Published{}, Building{};
    wchar_t Settings[MAX_PATH]{};

    std::int64_t SubmitDropTest(const dfl::debug_window::Content& content, bool enabled) noexcept;

    std::uint64_t Now() noexcept
    {
        FILETIME value{};
        GetSystemTimeAsFileTime(&value);
        return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
    }

    DWORD WINAPI ReadSnapshots(void*) noexcept
    {
        WindowReady = dfl::debug_window::Create(static_cast<HWND>(Host.gameWindow), Settings[0] ? Settings : nullptr, &SubmitDropTest);
        SetEvent(ReadyEvent);
        if (!WindowReady) return 1;
        ULONGLONG lastRead = 0;
        do
        {
            MSG message{};
            for (unsigned count = 0; count < 64
                && WaitForSingleObject(StopEvent, 0) != WAIT_OBJECT_0
                && PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE); ++count)
            {
                TranslateMessage(&message); DispatchMessageW(&message);
            }
            // A busy paint/input queue must not postpone shutdown indefinitely.
            if (WaitForSingleObject(StopEvent, 0) == WAIT_OBJECT_0) break;
            dfl::client_party::PumpWorker();
            dfl::client_test_control::PumpWorker();
            dfl::frame_profile::Export();
            dfl::gameplay_trace::Export();
            dfl::native_loop_profile::Export();
            dfl::debug_window::Content content{};
            AcquireSRWLockShared(&ContentLock); content = Published; ReleaseSRWLockShared(&ContentLock);
            dfl::debug_window::Update(content);
            const auto tick = GetTickCount64();
            if (tick - lastRead < 250) continue;
            lastRead = tick;
            const LONG userId = InterlockedCompareExchange(&UserId, 0, 0);
            dfl::debug_model::Snapshot next{};
            bool valid = false;
            if (userId > 0)
            {
                wchar_t path[MAX_PATH]{};
                if (swprintf_s(path, L"%s\\dfdebug-%ld.bin", Directory, userId) > 0)
                {
                    const HANDLE file = CreateFileW(path, GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
                    if (file != INVALID_HANDLE_VALUE)
                    {
                        std::array<unsigned char, dfl::debug_model::MaximumBytes + 1> bytes{};
                        DWORD read = 0;
                        if (ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr))
                            valid = dfl::debug_model::Decode(bytes.data(), read, next);
                        CloseHandle(file);
                    }
                }
            }
            AcquireSRWLockExclusive(&SnapshotLock);
            Snapshot = next;
            HaveSnapshot = valid;
            ReleaseSRWLockExclusive(&SnapshotLock);
        } while (MsgWaitForMultipleObjects(1, &StopEvent, FALSE, 33, QS_ALLINPUT) != WAIT_OBJECT_0);
        dfl::debug_window::Destroy();
        return 0;
    }

    std::int64_t SubmitDropTest(const dfl::debug_window::Content& content, bool enabled) noexcept
    {
        const auto now = Now();
        if (!content.visible || !content.dropReady || !content.dropAllowed
            || content.userId != InterlockedCompareExchange(&UserId, 0, 0)
            || content.fileTime > now || now - content.fileTime > 15000000ULL) return 0;
        dfl::debug_model::DropRequest request{};
        request.requestId = static_cast<std::int64_t>(now);
        request.runId = content.runId; request.userId = content.userId; request.enabled = enabled ? 1 : 0;
        wchar_t path[MAX_PATH]{}, temporary[MAX_PATH]{};
        if (swprintf_s(path, L"%s\\dfdebug-drop-%u.bin", Directory, content.userId) <= 0
            || swprintf_s(temporary, L"%s.tmp-%lu", path, GetCurrentProcessId()) <= 0) return 0;
        const HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return 0;
        DWORD written = 0;
        const bool saved = WriteFile(file, &request, sizeof(request), &written, nullptr) && written == sizeof(request);
        CloseHandle(file);
        if (!saved || !MoveFileExW(temporary, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        {
            DeleteFileW(temporary); return 0;
        }
        return request.requestId;
    }

    void Text(int y, std::uint32_t color, const wchar_t* text) noexcept
    {
        if (Building.count < Building.lines.size())
        {
            const auto index = Building.count++;
            wcsncpy_s(Building.lines[index].data(), Building.lines[index].size(), text, _TRUNCATE);
            Building.colors[index] = color;
        }
        if (Host.gameWindow != nullptr || Host.drawText == nullptr) return;
        if (TextCount >= TextBuffers.size()) return;
        auto& gbk = TextBuffers[TextCount++];
        if (WideCharToMultiByte(936, 0, text, -1, gbk.data(), static_cast<int>(gbk.size()), nullptr, nullptr) == 0) return;
        // Native rendering may consume queued text later in this frame.
        // Every line owns stable storage until the next render batch.
        Host.drawText(11, y + 1, 0xFF000000, gbk.data());
        Host.drawText(10, y, color, gbk.data());
    }

    void PollKeys() noexcept
    {
        DWORD foregroundPid = 0;
        GetWindowThreadProcessId(GetForegroundWindow(), &foregroundPid);
        const bool focused = foregroundPid == GetCurrentProcessId();
        const bool f9 = focused && (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
        const bool f10 = focused && (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
        if (f9 && !F9Down) Details = !Details;
        if (f10 && !F10Down) Visible = !Visible;
        F9Down = f9;
        F10Down = f10;
    }
}

extern "C" int __stdcall DebugInitialize(const dfl::debug_api::Host* host, const wchar_t* directory) noexcept
{
    if (Worker != nullptr) return 0;
    if (host == nullptr || host->size != sizeof(*host) || host->version != dfl::debug_api::Version
        || (host->drawText == nullptr && host->gameWindow == nullptr) || directory == nullptr
        || wcsnlen_s(directory, MAX_PATH) >= MAX_PATH - 32) return 0;
    if(!dfl::client_state::BindShared(host->sharedState))return 0;
    Host = *host;
    wcscpy_s(Directory, directory);
    if (host->settingsPath != nullptr) wcsncpy_s(Settings, host->settingsPath, _TRUNCATE);
    StopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (StopEvent == nullptr) return 0;
    ReadyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (ReadyEvent == nullptr) { CloseHandle(StopEvent); StopEvent = nullptr; return 0; }
    dfl::client_party::Initialize(host->settingsPath, directory, dfl::client_state::NativeReader());
    dfl::client_test_control::Initialize(host->settingsPath, directory, dfl::client_state::NativeReader());
    dfl::frame_profile::Initialize(host->settingsPath, directory, host->gameWindow, dfl::client_state::NativeReader());
    dfl::gameplay_trace::Initialize(host->settingsPath, directory, dfl::client_state::NativeReader());
    dfl::native_loop_profile::Initialize(host->settingsPath, directory, dfl::client_state::NativeReader());
    Worker = CreateThread(nullptr, 0, &ReadSnapshots, nullptr, 0, nullptr);
    if (Worker == nullptr)
    {
        dfl::client_party::Shutdown();
        dfl::frame_profile::Shutdown();
        dfl::gameplay_trace::Shutdown();
        dfl::native_loop_profile::Shutdown();
        dfl::client_test_control::Shutdown();
        dfl::client_state::Shutdown();
        CloseHandle(StopEvent); CloseHandle(ReadyEvent); StopEvent = ReadyEvent = nullptr; return 0;
    }
    // Initialize is called outside DllMain. Cross-thread owned windows can
    // send synchronous messages to the owner while being created, so pump
    // sent messages instead of blocking the game's window thread.
    while (MsgWaitForMultipleObjects(1, &ReadyEvent, FALSE, INFINITE, QS_SENDMESSAGE) == WAIT_OBJECT_0 + 1)
    {
        MSG message{}; PeekMessageW(&message, nullptr, 0, 0, PM_NOREMOVE);
    }
    CloseHandle(ReadyEvent); ReadyEvent = nullptr;
    if (!WindowReady)
    {
        WaitForSingleObject(Worker, INFINITE);
        dfl::client_party::Shutdown();
        dfl::frame_profile::Shutdown();
        dfl::gameplay_trace::Shutdown();
        dfl::native_loop_profile::Shutdown();
        dfl::client_test_control::Shutdown();
        dfl::client_state::Shutdown();
        CloseHandle(Worker); CloseHandle(StopEvent); Worker = StopEvent = nullptr;
        return 0;
    }
    return 1;
}

extern "C" int __stdcall DebugPipelineEnabled() noexcept
{
    return dfl::frame_profile::PipelineEnabled()?1:0;
}
extern "C" int __stdcall DebugGameplayTraceEnabled() noexcept
{
    return dfl::gameplay_trace::Enabled()?1:0;
}
extern "C" int __stdcall DebugNativeLoopEnabled() noexcept{return dfl::native_loop_profile::Enabled()?1:0;}
extern "C" void __stdcall DebugRecordNativeLoop(const dfl::debug_api::NativeLoopSample* input) noexcept
{if(input)dfl::native_loop_profile::Record(*input);}
extern "C" void __stdcall DebugObserveGameplay(std::uint32_t context,const void* bytes,std::uint32_t length,std::uint32_t provider) noexcept
{
    if(provider<=1)dfl::gameplay_trace::Observe(context,bytes,length,provider==1,0);
}
extern "C" void __stdcall DebugObserveGameplayV2(std::uint32_t context,const void* bytes,std::uint32_t length,std::uint32_t provider,std::uint32_t frame) noexcept
{
    if(provider<=1)dfl::gameplay_trace::Observe(context,bytes,length,provider==1,frame);
}
extern "C" void __stdcall DebugObserveMovementGate(const dfl::debug_api::MovementGate* input) noexcept
{
    if(input&&input->size==sizeof(*input)&&input->version==1)dfl::gameplay_trace::ObserveGate(*input);
}
extern "C" void __stdcall DebugRecordPipeline(const dfl::debug_api::PipelineSample* input) noexcept
{
    if(!input||input->size!=sizeof(*input)||input->version!=1||input->foregroundKnown>1
        ||input->foregroundBegin>1||input->foregroundEnd>1)return;
    dfl::frame_profile::Sample sample;
    sample.beginUs=input->beginUs;sample.endUs=input->endUs;sample.tickMs=input->tickMs;sample.scene=input->scene;
    sample.foregroundKnown=input->foregroundKnown==1;sample.foregroundBegin=input->foregroundBegin==1;
    sample.foregroundEnd=input->foregroundEnd==1;
    for(std::size_t i=0;i<sample.parts.size();++i)sample.parts[i]=input->partsUs[i];
    dfl::frame_profile::RecordPipeline(sample);
}
extern "C" void __stdcall DebugRender(const dfl::debug_api::Frame* frame) noexcept
{
    if (Worker == nullptr || frame == nullptr || frame->size != sizeof(*frame)) return;
    const auto coreTiming=dfl::client_state::SharedTiming();
    dfl::frame_profile::Frame timing(frame->scene,coreTiming.captureUs,coreTiming.navigationUs);
    dfl::client_party::OnFrame();
    timing.Mark();
    dfl::client_test_control::OnFrame();
    timing.Mark();
    Building = {};
    struct Publish
    {
        ~Publish() noexcept
        {
            AcquireSRWLockExclusive(&ContentLock); Published = Building; ReleaseSRWLockExclusive(&ContentLock);
        }
    } publish;
    InterlockedExchange(&UserId, frame->scene == 3 ? static_cast<LONG>(frame->userId) : 0);
    if (frame->scene != 3) { InDungeon = false; return; }
    if (!InDungeon) { Visible = true; InDungeon = true; }
    if (Host.gameWindow == nullptr) PollKeys();
    if (!Visible) return;
    Building.visible = true;
    Building.userId = static_cast<std::uint16_t>(frame->userId);
    TextCount = 0;
    constexpr std::uint32_t White = 0xFFF0F0F0, Cyan = 0xFFFFFF80;
    Text(65, Cyan, L"BOSS 房调试信息");
    dfl::debug_model::Snapshot snapshot{};
    bool current = false;
    if (TryAcquireSRWLockShared(&SnapshotLock))
    {
        current = HaveSnapshot;
        if (current) snapshot = Snapshot;
        ReleaseSRWLockShared(&SnapshotLock);
    }
    if (!current || !dfl::debug_model::IsCurrent(snapshot, frame->userId, Now(), frame->enteredFileTime))
    {
        Text(80, White, L"BOSS房箱怪：等待服务端调试数据...");
        return;
    }
    const auto& header = snapshot.header;
    Building.dropReady = header.version == 2;
    Building.dropAllowed = snapshot.drop.allowed != 0;
    Building.dropEnabled = snapshot.drop.enabled != 0;
    Building.dropResult = snapshot.drop.result;
    Building.dropRequestId = snapshot.drop.requestId;
    Building.userId = header.userId; Building.runId = static_cast<std::int64_t>(header.runId);
    Building.fileTime = header.fileTime;
    wchar_t line[180]{};
    swprintf_s(line, L"副本%u  当前(%u,%u)/%u  BOSS(%u,%u)/%u",
        header.dungeonId, header.roomX, header.roomY, header.currentMapId,
        header.bossX, header.bossY, header.bossMapId);
    Text(80, White, line);
    unsigned pending = 0, spawned = 0, dead = 0;
    for (std::size_t i = 0; i < header.count; ++i)
    {
        const auto state = snapshot.monsters[i].state;
        if (state <= 1) ++pending;
        else if (state == 2) ++spawned;
        else ++dead;
    }
    swprintf_s(line, L"BOSS房箱怪：%s  共%u只  未释放%u / 已出%u / 死亡%u",
        header.count == 0 ? L"无" : L"有", header.count, pending, spawned, dead);
    Text(96, header.count == 0 ? 0xFF80FF80 : 0xFF60DFFF, line);
    swprintf_s(line, L"BOSS已死：%s  通关结算：%s%s",
        frame->bossDead != 0 ? L"是" : L"否", header.clearEnabled ? L"已开启" : L"未开启",
        header.atBoss ? L"  [当前BOSS房]" : L"  [已确定的出怪表]");
    Text(112, White, line);
    if (!Details) return;
    const std::size_t limit = Host.gameWindow == nullptr ? 7 : header.count;
    for (std::size_t i = 0; i < header.count && i < limit; ++i)
    {
        const auto& monster = snapshot.monsters[i];
        static const wchar_t* States[] = {L"待进房", L"藏在容器内", L"已释放", L"已死亡"};
        swprintf_s(line, L"容器[%u] (%u,%u)  怪%u Lv%u UID%u  %s",
            monster.box, monster.x, monster.y, monster.monster, monster.level, monster.uid, States[monster.state]);
        Text(130 + static_cast<int>(i) * 15, White, line);
    }
    if (header.count > 7 && Host.gameWindow == nullptr) Text(235, White, L"更多记录请查看监听脚本输出。");
}

extern "C" int __stdcall DebugShutdown() noexcept
{
    if (Worker == nullptr) return 1;
    SetEvent(StopEvent);
    const auto deadline = GetTickCount64() + 2000;
    while (WaitForSingleObject(Worker, 0) != WAIT_OBJECT_0)
    {
        if (GetTickCount64() >= deadline) return 0;
        MSG message{}; PeekMessageW(&message, nullptr, 0, 0, PM_NOREMOVE);
        MsgWaitForMultipleObjects(1, &Worker, FALSE, 10, QS_SENDMESSAGE);
    }
    // A retained native CALL must keep this DLL loaded if restoration fails.
    dfl::frame_profile::Shutdown();
    dfl::gameplay_trace::Shutdown();
    dfl::native_loop_profile::Shutdown();
    dfl::client_party::Shutdown();
    dfl::client_test_control::Shutdown();
    CloseHandle(Worker); CloseHandle(StopEvent);
    dfl::client_state::Shutdown();
    Worker = nullptr; StopEvent = nullptr;
    Host = {};
    Published = {}; Building = {}; Settings[0] = 0;
    Visible = Details = true; InDungeon = F9Down = F10Down = false;
    InterlockedExchange(&UserId, 0);
    return 1;
}
