#pragma once
#include <cstdint>
namespace dfl::native_incidents
{
    bool Initialize(const wchar_t* directory,const char* session,const char* version) noexcept;
    void Install() noexcept;
    bool InstallNetworkHooks(std::uintptr_t image) noexcept;
    // Called only at validated ACT1 user-visible failure notification sites.
    // Raw EOF, WSAEWOULDBLOCK, successful connect and silent channel handoff never call this.
    void NetworkNotification(bool connecting,unsigned socketError,bool visibleDisconnect=false) noexcept;
}
