// Exercise the real protocol COM implementation without a browser or network.
// Keep private test access in this translation unit, as for the native hook tests.
#include "ChapterTwoPaymentProtocol.cpp"
#include <cstdio>

namespace
{
    constexpr wchar_t FixturePrefix[] = L"http://127.0.0.2:48123/0123456789abcdef0123456789abcdef";
    constexpr unsigned char FixtureBody[]{0x00, 0x41, 0xff, 0x7f, 0x42};
    unsigned Checks = 0, Failures = 0, FetchCalls = 0;
    std::string FetchedRoute, FetchedForm;

    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL payment protocol: %s\n", message); }
    }

    bool Fetch(std::string_view route, std::string_view form, dfl::chapter_two::PaymentReply& reply)
    {
        ++FetchCalls;
        FetchedRoute = route;
        FetchedForm = form;
        reply.mime = "text/html; charset=utf-8";
        reply.body.assign(std::begin(FixtureBody), std::end(FixtureBody));
        return true;
    }

    // Stack-owned COM boundary objects; the protocol only borrows them during Start.
    class Sink final : public IInternetProtocolSink
    {
    public:
        unsigned Calls = 0, Results = 0;
        DWORD Progress = 0, DataFlags = 0;
        ULONG Size = 0, Maximum = 0;
        HRESULT Result = E_PENDING;
        std::wstring Mime;

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
        {
            if (!out) return E_POINTER;
            *out = nullptr;
            if (iid != IID_IUnknown && iid != IID_IInternetProtocolSink) return E_NOINTERFACE;
            *out = static_cast<IInternetProtocolSink*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
        ULONG STDMETHODCALLTYPE Release() override { return 1; }
        HRESULT STDMETHODCALLTYPE Switch(PROTOCOLDATA*) override { ++Calls; return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE ReportProgress(ULONG status, LPCWSTR text) override
        {
            ++Calls; Progress = status; Mime = text ? text : L"";
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE ReportData(DWORD flags, ULONG size, ULONG maximum) override
        {
            ++Calls; DataFlags = flags; Size = size; Maximum = maximum;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE ReportResult(HRESULT result, DWORD, LPCWSTR) override
        {
            ++Calls; ++Results; Result = result;
            return S_OK;
        }
    };

    class Bind final : public IInternetBindInfo
    {
        const DWORD _verb;
        const std::string _form;
    public:
        unsigned Calls = 0;
        Bind(DWORD verb, std::string form = {}) : _verb(verb), _form(std::move(form)) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
        {
            if (!out) return E_POINTER;
            *out = nullptr;
            if (iid != IID_IUnknown && iid != IID_IInternetBindInfo) return E_NOINTERFACE;
            *out = static_cast<IInternetBindInfo*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
        ULONG STDMETHODCALLTYPE Release() override { return 1; }
        HRESULT STDMETHODCALLTYPE GetBindInfo(DWORD* flags, BINDINFO* info) override
        {
            ++Calls;
            if (!flags || !info || info->cbSize != sizeof(*info)) return E_INVALIDARG;
            *flags = 0;
            *info = {};
            info->cbSize = sizeof(*info);
            info->dwBindVerb = _verb;
            if (!_form.empty())
            {
                const auto memory = GlobalAlloc(GMEM_MOVEABLE, _form.size());
                if (!memory) return E_OUTOFMEMORY;
                const auto data = GlobalLock(memory);
                if (!data) { GlobalFree(memory); return E_OUTOFMEMORY; }
                std::memcpy(data, _form.data(), _form.size());
                GlobalUnlock(memory);
                info->cbstgmedData = static_cast<ULONG>(_form.size());
                info->stgmedData.tymed = TYMED_HGLOBAL;
                info->stgmedData.hGlobal = memory;
            }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetBindString(ULONG, LPOLESTR*, ULONG, ULONG*) override { return E_NOTIMPL; }
    };

    struct ProtocolInstance
    {
        IInternetProtocol* Value = nullptr;
        ProtocolInstance()
        {
            auto* factory = new Factory(FixturePrefix, &Fetch);
            const auto status = factory->CreateInstance(nullptr, IID_IInternetProtocol,
                reinterpret_cast<void**>(&Value));
            factory->Release();
            Check(status == S_OK && Value, "the production factory creates its IInternetProtocol");
        }
        ~ProtocolInstance() { if (Value) Value->Release(); }
    };

    void AllowedRoutes()
    {
        // Literal expectations are independent of the production route table.
        struct Allowed { const wchar_t* Path; const char* Id; const char* Form = ""; };
        const Allowed allowed[]{
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
            {L"/api/client-pay/test-cera", "cera-submit", "characterName=fixture&amount=1"},
            {L"/api/client-pay/test-diamond", "diamond-submit", "characterName=fixture&days=1"},
        };
        for (const auto& route : allowed)
        {
            ProtocolInstance protocol;
            if (!protocol.Value) continue;
            Sink sink;
            Bind bind(*route.Form ? BINDVERB_POST : BINDVERB_GET, route.Form);
            const auto before = FetchCalls;
            const auto url = std::wstring(FixturePrefix) + route.Path;
            Check(protocol.Value->Start(url.c_str(), &sink, &bind, 0, 0) == S_OK,
                "the current local page, asset or POST is accepted");
            Check(FetchCalls == before + 1 && FetchedRoute == route.Id && FetchedForm == route.Form,
                "one request forwards the exact allowlisted route and form once");
            Check(bind.Calls == 1 && sink.Calls == 3 && sink.Results == 1 && sink.Result == S_OK &&
                sink.Progress == BINDSTATUS_MIMETYPEAVAILABLE && sink.Mime == L"text/html" &&
                sink.Size == sizeof(FixtureBody) && sink.Maximum == sizeof(FixtureBody) &&
                sink.DataFlags == (BSCF_FIRSTDATANOTIFICATION | BSCF_LASTDATANOTIFICATION | BSCF_DATAFULLYAVAILABLE),
                "accepted requests publish MIME, complete byte count and one success result");
            unsigned char bytes[sizeof(FixtureBody)]{};
            ULONG read = 0;
            Check(protocol.Value->Read(bytes, 2, &read) == S_OK && read == 2,
                "the first read preserves partial response delivery");
            Check(protocol.Value->Read(bytes + 2, 3, &read) == S_OK && read == 3 &&
                std::memcmp(bytes, FixtureBody, sizeof(bytes)) == 0,
                "the remaining response bytes are unchanged");
            Check(protocol.Value->Read(bytes, sizeof(bytes), &read) == S_FALSE && read == 0,
                "completed responses report end of stream");
            Check(protocol.Value->Abort(E_ABORT, 0) == S_OK && protocol.Value->Terminate(0) == S_OK,
                "accepted requests retain the protocol abort and close contract");
        }
    }

    void RejectedOrigins()
    {
        const wchar_t* rejected[]{
            L"http://remote.invalid:48123/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L"http://127.0.0.1:48123/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L"http://127.0.0.3:48123/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L"http://127.0.0.2:48124/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L"http://127.0.0.2:48123/fedcba9876543210fedcba9876543210/client/client_pay.htm",
            L"http://127.0.0.2:48123/0123456789abcdef0123456789abcdef-extra/client/client_pay.htm",
            L"https://127.0.0.2:48123/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L"http://127.0.0.2:48123/0123456789abcdef0123456789abcdef",
            L"http://127.0.0.2:48123/0123456789abcdef0123456789abcde",
            L"http://127.0.0.2:48123@remote.invalid/0123456789abcdef0123456789abcdef/client/client_pay.htm",
            L""
        };
        for (const auto* url : rejected)
        {
            ProtocolInstance protocol;
            if (!protocol.Value) continue;
            Sink sink;
            Bind bind(BINDVERB_POST, "characterName=fixture&amount=1");
            const auto before = FetchCalls;
            const auto status = protocol.Value->Start(url, &sink, &bind, 0, 0);
            Check(status == INET_E_RESOURCE_NOT_FOUND,
                "out-of-prefix URLs fail closed instead of selecting the default HTTP handler");
            Check(FetchCalls == before && bind.Calls == 0 && sink.Calls == 0,
                "rejected origins cannot read or submit a form or publish content");
        }
    }

    void RejectedLocalRequests()
    {
        struct Rejected { const wchar_t* Path; DWORD Verb; const char* Form; HRESULT Error; };
        const Rejected rejected[]{
            {L"/client/missing.htm", BINDVERB_GET, "", INET_E_RESOURCE_NOT_FOUND},
            {L"/client/client_pay.htm?other=1", BINDVERB_GET, "", INET_E_RESOURCE_NOT_FOUND},
            {L"/api/client-pay/test-cera", BINDVERB_GET, "", INET_E_INVALID_REQUEST},
            {L"/client/client_pay.htm", BINDVERB_POST, "characterName=fixture&amount=1", INET_E_INVALID_REQUEST},
            {L"/api/client-pay/test-cera", BINDVERB_POST, "", INET_E_INVALID_REQUEST},
        };
        for (const auto& request : rejected)
        {
            ProtocolInstance protocol;
            if (!protocol.Value) continue;
            Sink sink;
            Bind bind(request.Verb, request.Form);
            const auto before = FetchCalls;
            const auto url = std::wstring(FixturePrefix) + request.Path;
            Check(protocol.Value->Start(url.c_str(), &sink, &bind, 0, 0) == request.Error,
                "unknown local routes and invalid methods or forms remain rejected");
            Check(FetchCalls == before && sink.Calls == 0,
                "invalid local requests never reach payment transport");
        }
    }
}

int main()
{
    static_assert(sizeof(void*) == 4, "Exercise the shipping x86 payment COM implementation");
    const auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) { std::fprintf(stderr, "COM initialization failed\n"); return 2; }
    AllowedRoutes();
    RejectedOrigins();
    RejectedLocalRequests();
    CoUninitialize();
    std::printf("Chapter Two payment protocol: %u checks, %u failures; no browser or network\n", Checks, Failures);
    return Failures ? 1 : 0;
}
