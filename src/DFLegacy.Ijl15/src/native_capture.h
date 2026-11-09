#pragma once
#include <windows.h>
#include <cstdint>

namespace dfl::native_capture
{
    // Private inherited-handle protocol; never an upload object. All buffers are
    // allocated before a fault, and no pointer into the faulting stack is retained.
    constexpr DWORD Magic=0x334c4644;
    enum State : LONG { Idle=0, Provisional=1, Confirmed=2, Cancelled=3 };
    struct NetworkRecord {char Id[33]{};DWORD Connecting{},SocketError{};};
    struct Shared
    {
        DWORD MagicValue{},Size{},Pid{};
        std::uint64_t CreatedTicks{};
        wchar_t Directory[1024]{};
        char Session[33]{},Capability[33]{},Ids[8][33]{};
        DWORD SmallDumps{};
        volatile LONG StateValue{},Sequence{},Acknowledged{},Ready{};
        DWORD ThreadId{},OriginalEntry{};
        ULONGLONG Started{};
        EXCEPTION_RECORD Exception{};
        CONTEXT Context{};
        volatile LONG NetworkCount{};
        NetworkRecord Network[16]{};
    };
    bool Initialize() noexcept;
    // Returns a slot number, or -1 when unavailable/reentrant. Prior handlers
    // still run, regardless of whether diagnostics can be captured.
    LONG Begin(EXCEPTION_POINTERS*,bool originalEntry) noexcept;
    void Finish(LONG slot,LONG disposition) noexcept;
    void NetworkNotification(const char* id,bool connecting,unsigned socketError) noexcept;
    bool InstallOriginalHooks() noexcept;
}
