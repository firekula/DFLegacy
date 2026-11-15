#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <cstdio>
#include <cwchar>
#include <string>
#include "LoopbackWire.h"
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "ws2_32.lib")

static std::wstring Environment(const wchar_t* name)
{
    wchar_t value[32768]{};
    const auto length = GetEnvironmentVariableW(name, value, _countof(value));
    return length && length < _countof(value) ? value : L"";
}
static bool DesktopMatches(HANDLE desktop, const std::wstring& expected)
{
    wchar_t name[256]{}; DWORD needed = 0;
    return desktop && GetUserObjectInformationW(desktop, UOI_NAME, name, sizeof(name), &needed) && name == expected;
}
static bool WriteLog(HANDLE file)
{
    char line[64]{}; const auto length = sprintf_s(line, "fixture pid=%lu\n", GetCurrentProcessId());
    DWORD written = 0;
    return WriteFile(file, line, length, &written, nullptr) && written == static_cast<DWORD>(length);
}
static bool InheritedLog(const wchar_t* name)
{
    const auto text = Environment(name);
    if (text.empty()) return false;
    return WriteLog(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(text.c_str(), nullptr, 10))));
}
static bool LoopbackBoundary()
{
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2,2), &data)) return false;
    const auto inputText = Environment(L"DFL_S1C2_BROKER_IN"), outputText = Environment(L"DFL_S1C2_BROKER_OUT");
    const auto input = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(inputText.c_str(), nullptr, 10)));
    const auto output = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(outputText.c_str(), nullptr, 10)));
    const auto exchange = [input,output](const localwire::Request& request, localwire::Reply& reply) {
        return localwire::write(output, &request, sizeof(request)) && localwire::read(input, &reply, sizeof(reply));
    };
    localwire::Request request{}; localwire::Reply reply{};
    request.op = localwire::Operation::Open; request.family = AF_INET; request.type = SOCK_DGRAM; request.protocol = IPPROTO_UDP;
    if (!exchange(request, reply) || reply.result || !reply.id) return false;
    auto socketInfo = reply.socketInfo;
    request.id = reply.id; request.op = localwire::Operation::Bind;
    request.address.sin_family = AF_INET; request.address.sin_addr.s_addr = htonl(INADDR_ANY); request.address.sin_port = 0;
    if (!exchange(request, reply) || reply.result) return false;
    // Construct the duplicate after binding so its provider state starts bound.
    const auto socket = WSASocketA(FROM_PROTOCOL_INFO, FROM_PROTOCOL_INFO, FROM_PROTOCOL_INFO, &socketInfo, 0, 0);
    if (socket == INVALID_SOCKET) return false;
    sockaddr_in local{}; int length = sizeof(local);
    const auto localResult = getsockname(socket, reinterpret_cast<sockaddr*>(&local), &length);
    if (localResult || ntohl(local.sin_addr.s_addr) != 0x7f000002) {
        char diagnostic[160]{};
        sprintf_s(diagnostic, "DFLegacy Chapter Two: FIXTURE_LOOPBACK_FAILURE getsockname=%d address=%08lx error=%d", localResult, ntohl(local.sin_addr.s_addr), WSAGetLastError());
        OutputDebugStringA(diagnostic);
        return false;
    }
    request.op = localwire::Operation::Connect; request.address.sin_addr.s_addr = htonl(0xcb007101); request.address.sin_port = htons(42312);
    if (!exchange(request, reply) || reply.result != SOCKET_ERROR || reply.error != WSAEACCES) return false;
    request.address.sin_addr.s_addr = htonl(0x7f000001); request.address.sin_port = htons(9);
    if (!exchange(request, reply) || reply.result != SOCKET_ERROR || reply.error != WSAEACCES) return false;
    request.op = localwire::Operation::Close;
    if (!exchange(request, reply) || reply.result) return false;
    closesocket(socket); WSACleanup();
    OutputDebugStringA("DFLegacy Chapter Two: FIXTURE_LOOPBACK bind=127.0.0.2 external=denied wrongport=denied");
    return true;
}
static bool IsolatedFallbackBoundary()
{
    const auto inputText = Environment(L"DFL_S1C2_BROKER_IN"), outputText = Environment(L"DFL_S1C2_BROKER_OUT");
    const auto input = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(inputText.c_str(), nullptr, 10)));
    const auto output = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(outputText.c_str(), nullptr, 10)));
    const auto exchange = [input, output](const localwire::Request& request, localwire::Reply& reply) {
        return localwire::write(output, &request, sizeof(request)) && localwire::read(input, &reply, sizeof(reply));
    };
    // Connect a datagram socket to exercise the broker route without a listener.
    localwire::Request request{};
    localwire::Reply reply{};
    request.op = localwire::Operation::Open;
    request.family = AF_INET;
    request.type = SOCK_DGRAM;
    request.protocol = IPPROTO_UDP;
    if (!exchange(request, reply) || reply.result || !reply.id) return false;
    request.id = reply.id;
    request.op = localwire::Operation::Connect;
    request.address.sin_family = AF_INET;
    request.address.sin_addr.s_addr = htonl(0x7f000003);
    request.address.sin_port = htons(47001);
    if (!exchange(request, reply) || reply.result != SOCKET_ERROR || reply.error != WSAEACCES) return false;
    request.address.sin_port = htons(47005);
    if (!exchange(request, reply) || reply.result) {
        OutputDebugStringA("DFLegacy Chapter Two: FIXTURE_FALLBACK isolated endpoint denied");
        return false;
    }
    request.op = localwire::Operation::Close;
    if (!exchange(request, reply) || reply.result) return false;
    OutputDebugStringA("DFLegacy Chapter Two: FIXTURE_FALLBACK connected");
    return true;
}
static void HandledFault()
{
    __try { RaiseException(0xE0424242, 0, 0, nullptr); }
    __except(EXCEPTION_EXECUTE_HANDLER) { }
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 41;
    HANDLE token = nullptr; DWORD appContainer = 1, bytes = 0;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 42;
    const auto tokenRead = GetTokenInformation(token, TokenIsAppContainer, &appContainer, sizeof(appContainer), &bytes);
    CloseHandle(token);
    if (!tokenRead || appContainer != 0) return 43;
    const auto expectedDesktop = Environment(L"DFL_FIXTURE_EXPECTED_DESKTOP");
    const auto input = OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS);
    const bool desktopOk = DesktopMatches(input, expectedDesktop) && DesktopMatches(GetThreadDesktop(GetCurrentThreadId()), expectedDesktop);
    if (input) CloseDesktop(input);
    if (!desktopOk) return 44;
    const auto session = Environment(L"DFL_S1C2_SESSION_DATA");
    if (session.empty() || session != Environment(L"DFL_FIXTURE_EXPECTED_SESSION") || !Environment(L"DFL_S1C2_CONTAINER_DATA").empty()) return 45;
    if (!Environment(L"DFLEGACY_CH2_DIAGNOSTIC_EVENTS").empty()) return 46;
    const auto nativeLog = CreateFileW((session + L"\\fixture-native.log").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (nativeLog == INVALID_HANDLE_VALUE) return 47;
    const bool wrote = WriteLog(nativeLog); CloseHandle(nativeLog);
    if (!wrote) return 48;
    if (wcscmp(argv[1], L"without-broker")) {
        if (!InheritedLog(L"DFL_S1C2_PERF_INTERVAL_HANDLE") || !InheritedLog(L"DFL_S1C2_PERF_TOTAL_HANDLE") || !InheritedLog(L"DFL_S1C2_PERF_SLOW_HANDLE")) return 49;
        if (!LoopbackBoundary()) return 50;
        if (!IsolatedFallbackBoundary()) return 51;
    }
    OutputDebugStringA("DFLegacy Chapter Two: FIXTURE_IDENTITY appcontainer=0 desktop=unchanged session=unchanged");
    HandledFault();
    OutputDebugStringA("DFLegacy Chapter Two: FIXTURE_HANDLED_EXCEPTION recovered");
    if (!wcscmp(argv[1], L"normal")) {
        OutputDebugStringA("DFL_S1C2_GUILD_OPEN");
        std::fputs("NATIVE_CHANNEL_BODY", stdout);
        std::fflush(stdout);
        OutputDebugStringA("DFL_S1C2_GUILD_OPEN");
    }
    if (!wcscmp(argv[1], L"crash")) *reinterpret_cast<volatile int*>(0) = 7;
    return !wcscmp(argv[1], L"nonzero") ? 5 : 0;
}
