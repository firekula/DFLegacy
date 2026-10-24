#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cwchar>
#include "../include/debug_api.h"
#include "debug_model.h"

namespace
{
    dfl::debug_api::Host Host{};
    wchar_t Directory[MAX_PATH]{};
    HANDLE StopEvent = nullptr;
    HANDLE Worker = nullptr;
    SRWLOCK SnapshotLock = SRWLOCK_INIT;
    dfl::debug_model::Snapshot Snapshot{};
    bool HaveSnapshot = false;
    volatile LONG UserId = 0;
    bool Visible = true, Details = true, InDungeon = false;
    bool F9Down = false, F10Down = false;
    std::array<std::array<char, 512>, 16> TextBuffers{};
    std::size_t TextCount = 0;

    std::uint64_t Now() noexcept
    {
        FILETIME value{};
        GetSystemTimeAsFileTime(&value);
        return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
    }

    DWORD WINAPI ReadSnapshots(void*) noexcept
    {
        do
        {
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
        } while (WaitForSingleObject(StopEvent, 250) == WAIT_TIMEOUT);
        return 0;
    }

    void Text(int y, std::uint32_t color, const wchar_t* text) noexcept
    {
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
        || host->drawText == nullptr || directory == nullptr
        || wcsnlen_s(directory, MAX_PATH) >= MAX_PATH - 32) return 0;
    Host = *host;
    wcscpy_s(Directory, directory);
    StopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (StopEvent == nullptr) return 0;
    Worker = CreateThread(nullptr, 0, &ReadSnapshots, nullptr, 0, nullptr);
    if (Worker == nullptr) { CloseHandle(StopEvent); StopEvent = nullptr; return 0; }
    return 1;
}

extern "C" void __stdcall DebugRender(const dfl::debug_api::Frame* frame) noexcept
{
    if (Worker == nullptr || frame == nullptr || frame->size != sizeof(*frame)) return;
    InterlockedExchange(&UserId, frame->scene == 3 ? static_cast<LONG>(frame->userId) : 0);
    if (frame->scene != 3) { InDungeon = false; return; }
    if (!InDungeon) { Visible = true; InDungeon = true; }
    PollKeys();
    if (!Visible) return;
    TextCount = 0;
    constexpr std::uint32_t White = 0xFFF0F0F0, Cyan = 0xFFFFFF80;
    Text(65, Cyan, L"[DF DEBUG]  F9 详情  /  F10 隐藏");
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
    for (std::size_t i = 0; i < header.count && i < 7; ++i)
    {
        const auto& monster = snapshot.monsters[i];
        static const wchar_t* States[] = {L"待进房", L"藏在容器内", L"已释放", L"已死亡"};
        swprintf_s(line, L"容器[%u] (%u,%u)  怪%u Lv%u UID%u  %s",
            monster.box, monster.x, monster.y, monster.monster, monster.level, monster.uid, States[monster.state]);
        Text(130 + static_cast<int>(i) * 15, White, line);
    }
    if (header.count > 7) Text(235, White, L"更多记录请查看监听脚本输出。");
}

extern "C" int __stdcall DebugShutdown() noexcept
{
    if (Worker == nullptr) return 1;
    SetEvent(StopEvent);
    if (WaitForSingleObject(Worker, 2000) != WAIT_OBJECT_0) return 0;
    CloseHandle(Worker); CloseHandle(StopEvent);
    Worker = nullptr; StopEvent = nullptr;
    Host = {};
    return 1;
}
