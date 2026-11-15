#include "ChapterTwoPaymentProtocol.h"
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <urlmon.h>
#include <algorithm>
#include <cstring>
#include <iterator>
#include <new>

namespace
{
    using dfl::chapter_two::PaymentFetch;
    using dfl::chapter_two::PaymentReply;
    using dfl::chapter_two::MaximumPaymentForm;
    using dfl::chapter_two::MaximumPaymentReply;
    constexpr CLSID PaymentClass{0x108a669b,0xefde,0x4453,{0xa9,0x66,0x27,0xb2,0x7f,0x41,0x25,0xd0}};

    struct Route { std::wstring_view path; std::string_view id; bool post = false; };
    constexpr Route Routes[]{
        {L"/client/client_pay.htm", "cera-page"},
        {L"/client/client_diamond.htm", "diamond-page"},
        {L"/client/client_pay_bank.htm", "cera-bank-page"},
        {L"/client/client_pay_netbar.htm", "cera-netbar-page"},
        {L"/client/client_diamond_netbar.htm", "diamond-netbar-page"},
        {L"/client/client-pay-settings.js", "settings"},
        {L"/client/client-auth.js", "auth"},
        {L"/client/client-layout.js", "layout"},
        {L"/client/css/commen.css", "style"},
        {L"/client/images/pic.png", "image-pic"},
        {L"/client/images/hz.gif", "image-hz"},
        {L"/client/images/cz.png", "image-cz"},
        {L"/api/client-pay/test-cera", "cera-submit", true},
        {L"/api/client-pay/test-diamond", "diamond-submit", true},
    };

    class Protocol final : public IInternetProtocol
    {
        const std::wstring _prefix;
        const PaymentFetch _fetch;
        volatile LONG _references = 1;
        std::vector<unsigned char> _body;
        std::size_t _read = 0;

    public:
        Protocol(const std::wstring& prefix, PaymentFetch fetch) : _prefix(prefix), _fetch(fetch) {}

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
        {
            if (!out) return E_POINTER;
            *out = nullptr;
            if (iid != IID_IUnknown && iid != IID_IInternetProtocol && iid != IID_IInternetProtocolRoot)
                return E_NOINTERFACE;
            *out = static_cast<IInternetProtocol*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto count = InterlockedDecrement(&_references);
            if (!count) delete this;
            return count;
        }

        HRESULT STDMETHODCALLTYPE Start(LPCWSTR url, IInternetProtocolSink* sink, IInternetBindInfo* bind,
            DWORD, HANDLE_PTR) override
        {
            if (!url || !sink || !bind) return E_POINTER;
            const std::wstring_view full(url);
            if (full.size() <= _prefix.size() || full.compare(0, _prefix.size(), _prefix) != 0 ||
                full[_prefix.size()] != L'/') return INET_E_RESOURCE_NOT_FOUND;
            const auto path = full.substr(_prefix.size());
            const auto route = std::find_if(std::begin(Routes), std::end(Routes),
                [path](const Route& candidate) { return candidate.path == path; });
            if (route == std::end(Routes)) return INET_E_RESOURCE_NOT_FOUND;

            BINDINFO info{};
            info.cbSize = sizeof(info);
            DWORD flags{};
            const auto bindResult = bind->GetBindInfo(&flags, &info);
            if (FAILED(bindResult)) return bindResult;
            struct ReleaseInfo { BINDINFO& info; ~ReleaseInfo() { ReleaseBindInfo(&info); } } release{info};
            if (info.dwBindVerb != static_cast<DWORD>(route->post ? BINDVERB_POST : BINDVERB_GET))
                return INET_E_INVALID_REQUEST;
            try
            {
                std::string form;
                if (route->post)
                {
                    if (info.stgmedData.tymed != TYMED_HGLOBAL || !info.cbstgmedData ||
                        info.cbstgmedData > MaximumPaymentForm ||
                        GlobalSize(info.stgmedData.hGlobal) < info.cbstgmedData) return INET_E_INVALID_REQUEST;
                    const auto* data = static_cast<const char*>(GlobalLock(info.stgmedData.hGlobal));
                    if (!data) return INET_E_INVALID_REQUEST;
                    struct Unlock { HGLOBAL data; ~Unlock() { GlobalUnlock(data); } } unlock{info.stgmedData.hGlobal};
                    form.assign(data, info.cbstgmedData);
                }

                PaymentReply reply;
                if (!_fetch(route->id, form, reply) || reply.mime.empty() ||
                    reply.mime.size() > 100 || reply.body.size() > MaximumPaymentReply) return INET_E_DOWNLOAD_FAILURE;
                const auto semicolon = reply.mime.find(';');
                const std::wstring mime(reply.mime.begin(), reply.mime.begin() +
                    (semicolon == std::string::npos ? reply.mime.size() : semicolon));
                _body = std::move(reply.body);
                _read = 0;
                sink->ReportProgress(BINDSTATUS_MIMETYPEAVAILABLE, mime.c_str());
                const auto size = static_cast<ULONG>(_body.size());
                sink->ReportData(BSCF_FIRSTDATANOTIFICATION | BSCF_LASTDATANOTIFICATION | BSCF_DATAFULLYAVAILABLE, size, size);
                sink->ReportResult(S_OK, 0, nullptr);
                return S_OK;
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            catch (...) { return E_FAIL; }
        }
        HRESULT STDMETHODCALLTYPE Continue(PROTOCOLDATA*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE Abort(HRESULT, DWORD) override { _read = _body.size(); return S_OK; }
        HRESULT STDMETHODCALLTYPE Terminate(DWORD) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE Suspend() override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE Resume() override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE Read(void* buffer, ULONG capacity, ULONG* read) override
        {
            if (!read || (!buffer && capacity)) return E_POINTER;
            const auto count = std::min<std::size_t>(capacity, _body.size() - _read);
            if (count) std::memcpy(buffer, _body.data() + _read, count);
            _read += count;
            *read = static_cast<ULONG>(count);
            return count ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Seek(LARGE_INTEGER, DWORD, ULARGE_INTEGER*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE LockRequest(DWORD) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE UnlockRequest() override { return S_OK; }
    };

    class Factory final : public IClassFactory
    {
        const std::wstring _prefix;
        const PaymentFetch _fetch;
        volatile LONG _references = 1;

    public:
        Factory(std::wstring_view prefix, PaymentFetch fetch) : _prefix(prefix), _fetch(fetch) {}

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
        {
            if (!out) return E_POINTER;
            *out = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *out = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto count = InterlockedDecrement(&_references);
            if (!count) delete this;
            return count;
        }
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) override
        {
            if (!out) return E_POINTER;
            *out = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;
            try
            {
                const auto protocol = new Protocol(_prefix, _fetch);
                const auto result = protocol->QueryInterface(iid, out);
                protocol->Release();
                return result;
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }
        HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
    };
}

bool dfl::chapter_two::RegisterPaymentProtocol(std::wstring_view prefix, PaymentFetch fetch) noexcept
{
    if (prefix.empty() || !fetch) return false;
    try
    {
        auto* factory = new Factory(prefix, fetch);
        IInternetSession* session{};
        auto status = CoInternetGetSession(0, &session, 0);
        if (SUCCEEDED(status))
        {
            status = session->RegisterNameSpace(factory, PaymentClass, L"http", 0, nullptr, 0);
            session->Release();
        }
        factory->Release();
        return SUCCEEDED(status);
    }
    catch (...) { return false; }
}
