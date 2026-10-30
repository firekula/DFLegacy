#pragma once
#include "client_state.h"
#include <cstdint>

namespace dfl::client_party
{
#pragma pack(push, 1)
    struct Request
    {
        char magic[4]={'D','L','P','T'};
        std::uint16_t version=1,size=108;
        std::uint32_t pid=0,uid=0;
        std::uint64_t processStartedFileTime=0,requestId=0,issuedTickMs=0,snapshotTickMs=0,generation=0,snapshotSequence=0;
        std::uint32_t actorToken=0,mapToken=0,userToken=0,contextToken=0;
        std::int32_t townId=-1,areaId=-1;
        std::uint32_t operation=0,targetUid=0,windowToken=0;
        std::uint64_t invitationSerial=0;
    };
#pragma pack(pop)
    static_assert(sizeof(Request)==108,"party file ABI");
    struct Pending
    {
        bool known=false,present=false,canReply=false;
        std::uint32_t windowToken=0,requesterUid=0,peerType=0,remainingMs=0,timerOffset=0;
        std::uint64_t serial=0;
        const char* reason="unknown";
    };
    // Read only; never invokes a game function or modifies a control/capture ID.
    bool ReadPending(const client_state::Reader&,std::uint32_t manager,Pending&) noexcept;
    void AssignInvitationSerial(Pending&,Pending& lastObserved,std::uint64_t& nextSerial) noexcept;
    bool ReadLiveContext(const client_state::Reader&,const Request&,std::uint32_t& manager,bool requirePeer) noexcept;
    // Diagnostic only. A one-member party still counts as a party in ACT1.
    const char* ReadRequestFailure(const client_state::Reader&,const Request&) noexcept;
    const char* ValidateRequest(const Request&,const client_state::Snapshot&,const Pending&,
                               std::uint64_t now,std::uint64_t consumedId,std::uint64_t consumedPrompt,std::uint64_t sessionFloor=0) noexcept;
    struct Operations
    {
        void* context=nullptr;
        bool (*live)(void*,const Request&) noexcept=nullptr;
        bool (*request)(void*,const Request&) noexcept=nullptr;
        bool (*accept)(void*,const Request&) noexcept=nullptr;
        bool (*reject)(void*,const Request&) noexcept=nullptr;
    };
    const char* Perform(const Request&,const client_state::Snapshot&,const Pending&,std::uint64_t now,
                        std::uint64_t& consumedId,std::uint64_t& consumedPrompt,const Operations&,std::uint64_t sessionFloor=0) noexcept;
    struct RejectionWriter
    {
        void* context=nullptr;
        std::int32_t (*begin)(void*,std::uint32_t) noexcept=nullptr;
        std::int32_t (*write)(void*,const void*,std::uint32_t) noexcept=nullptr;
        std::int32_t (*send)(void*) noexcept=nullptr;
    };
    bool SendRejection(std::uint32_t requesterUid,const RejectionWriter&) noexcept;
    void Initialize(const wchar_t* settingsPath,const wchar_t* directory,client_state::Reader) noexcept;
    void OnFrame() noexcept;
    void PumpWorker() noexcept;
    void Shutdown() noexcept;
}
