#include "ChapterTwoPaymentProtocol.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <cstring>

namespace
{
    using dfl::chapter_two::PaymentReply;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr unsigned NavigateCall = 0x4fb023;
    constexpr unsigned NavigateFunction = 0x5c0d50;
    constexpr char CeraUrl[]{"http://dnf.qq.com/client/client_pay.htm"};
    constexpr char DiamondUrl[]{"http://dnf.qq.com/client/client_diamond.htm"};
    using Navigate = void (WINAPI*)(HWND, const char*);
    Navigate OriginalNavigate;
    std::wstring PaymentOrigin;
    std::wstring ReplyDirectory;
    std::string LocalCera;
    std::string LocalDiamond;
    bool ProtocolRegistered;
    volatile LONG Sequence;

    std::wstring ReadEnvironment(const wchar_t* name, DWORD limit)
    {
        const auto size = GetEnvironmentVariableW(name, nullptr, 0);
        if (size < 2 || size > limit) return {};
        std::wstring value(size, L'\0');
        const auto length = GetEnvironmentVariableW(name, value.data(), size);
        if (length != size - 1) return {};
        value.resize(length);
        return value;
    }

    bool IsLocalOrigin(std::wstring_view value) noexcept
    {
        constexpr std::wstring_view host = L"http://127.0.0.2:";
        if (value.substr(0, host.size()) != host) return false;
        value.remove_prefix(host.size());
        const auto separator = value.find(L'/');
        if (separator == std::wstring_view::npos || separator == 0 || separator > 5 ||
            value.size() - separator != 33) return false;
        unsigned port = 0;
        for (auto digit : value.substr(0, separator))
        {
            if (digit < L'0' || digit > L'9') return false;
            port = port * 10 + (digit - L'0');
        }
        return port && port <= 65535 && std::all_of(value.begin() + separator + 1, value.end(), [](wchar_t c) {
            return (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f');
        });
    }

    bool ReadExact(HANDLE file, void* data, DWORD bytes) noexcept
    {
        DWORD read{};
        return !bytes || (ReadFile(file, data, bytes, &read, nullptr) && read == bytes);
    }

    HANDLE OpenReply(const std::wstring& path) noexcept
    {
        return CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    }

    bool ReadReply(HANDLE file, PaymentReply& reply)
    {
        struct Close { HANDLE file; ~Close() { CloseHandle(file); } } close{file};
        BY_HANDLE_FILE_INFORMATION information{};
        LARGE_INTEGER length{};
        struct { std::uint32_t status, mime, body; } header{};
        static_assert(sizeof(header) == 12);
        if (!GetFileInformationByHandle(file, &information) ||
            (information.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) ||
            !GetFileSizeEx(file, &length) || !ReadExact(file, &header, sizeof(header)) ||
            header.status < 200 || header.status > 599 ||
            !header.mime || header.mime > 100 || header.body > dfl::chapter_two::MaximumPaymentReply ||
            length.QuadPart != static_cast<LONGLONG>(sizeof(header) + header.mime + header.body))
            return false;

        reply.mime.resize(header.mime);
        reply.body.resize(header.body);
        return ReadExact(file, reply.mime.data(), header.mime) &&
            std::all_of(reply.mime.begin(), reply.mime.end(), [](char c) { return c >= 32 && c <= 126; }) &&
            ReadExact(file, reply.body.data(), header.body);
    }

    bool FetchFromLauncher(std::string_view route, std::string_view form, PaymentReply& reply)
    {
        if (form.empty())
        {
            // Each supported page and asset is prepared before the game launches.
            const auto path = ReplyDirectory + L"\\" + std::wstring(route.begin(), route.end()) + L".rsp";
            const auto file = OpenReply(path);
            if (file != INVALID_HANDLE_VALUE && ReadReply(file, reply)) return true;
            OutputDebugStringA("DFLegacy Chapter Two: local payment page could not be read.\n");
            return false;
        }

        const auto id = InterlockedIncrement(&Sequence);
        if (id <= 0) return false;
        std::string message = "DFL_S1C2_PAY_REQUEST " + std::to_string(id) + ' ' + std::string(route) + ' ';
        constexpr char hex[] = "0123456789abcdef";
        message.reserve(message.size() + form.size() * 2);
        for (const auto byte : form)
        {
            const auto value = static_cast<unsigned char>(byte);
            message += hex[value >> 4];
            message += hex[value & 15];
        }
        const auto path = ReplyDirectory + L"\\" + std::to_wstring(GetCurrentProcessId()) +
            L"-" + std::to_wstring(id) + L".rsp";
        OutputDebugStringA(message.c_str());
        const auto expires = GetTickCount64() + 12000;
        do
        {
            const auto file = OpenReply(path);
            if (file != INVALID_HANDLE_VALUE) return ReadReply(file, reply);
            const auto error = GetLastError();
            if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND && error != ERROR_SHARING_VIOLATION)
            {
                OutputDebugStringA("DFLegacy Chapter Two: could not open local payment reply.\n");
                return false;
            }
            Sleep(15);
        } while (GetTickCount64() < expires);
        OutputDebugStringA("DFLegacy Chapter Two: timed out waiting for local payment reply.\n");
        return false;
    }

    void WINAPI NavigateLocalPayment(HWND browser, const char* url)
    {
        const char* local = nullptr;
        if (url && std::strcmp(url, CeraUrl) == 0) local = LocalCera.c_str();
        else if (url && std::strcmp(url, DiamondUrl) == 0) local = LocalDiamond.c_str();
        if (!local)
        {
            OriginalNavigate(browser, url);
            return;
        }
        if (!ProtocolRegistered)
            ProtocolRegistered = dfl::chapter_two::RegisterPaymentProtocol(PaymentOrigin, &FetchFromLauncher);
        if (!ProtocolRegistered)
        {
            OutputDebugStringA("DFLegacy Chapter Two: process-private payment transport could not be registered.\n");
            OriginalNavigate(browser, "about:blank");
            return;
        }
        OriginalNavigate(browser, local);
    }
}

bool dfl::chapter_two::InstallPaymentProtocol(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4, "Chapter Two host must be x86.");
    if (!image || bytes != ImageSize) return false;

    // Preserve the stock browser creation, native ownership, and cleanup.
    // Redirect only this verified browser navigation call and only its two
    // exact old payment URLs.
    constexpr unsigned char call[]{
        0x8d,0x8d,0xe0,0xfe,0xff,0xff,0x51,0x50,0xe8,0x28,0x5d,0x0c,0};
    constexpr unsigned char navigate[]{0x55,0x8b,0xec,0x8b,0x45,0x0c,0x8b,0x4d,0x08,0x50,0x51};
    if (std::memcmp(image + NavigateCall - 8, call, sizeof(call)) != 0 ||
        std::memcmp(image + NavigateFunction, navigate, sizeof(navigate)) != 0 ||
        std::memcmp(image + 0x816a6c, CeraUrl, sizeof(CeraUrl)) != 0 ||
        std::memcmp(image + 0x816a94, DiamondUrl, sizeof(DiamondUrl)) != 0) return false;

    try
    {
        const auto origin = ReadEnvironment(L"DFLEGACY_CH2_PAYMENT_URL", 80);
        const auto replies = ReadEnvironment(L"DFLEGACY_CH2_PAYMENT_REPLIES", 8192);
        if (!IsLocalOrigin(origin) || replies.empty()) return false;
        std::string ascii;
        ascii.reserve(origin.size());
        for (const auto character : origin) ascii += static_cast<char>(character);
        LocalCera = ascii + "/client/client_pay.htm";
        LocalDiamond = ascii + "/client/client_diamond.htm";
        PaymentOrigin = origin;
        ReplyDirectory = replies;
    }
    catch (...) { return false; }

    OriginalNavigate = reinterpret_cast<Navigate>(image + NavigateFunction);
    const auto relative = reinterpret_cast<std::uintptr_t>(&NavigateLocalPayment)
        - reinterpret_cast<std::uintptr_t>(image + NavigateCall + 5);
    std::memcpy(image + NavigateCall + 1, &relative, sizeof(relative));
    return true;
}
