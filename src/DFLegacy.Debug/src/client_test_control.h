#pragma once
#include "client_state.h"
#include <cstdint>
#include <array>
#include <string>
namespace dfl::client_test_control
{
#pragma pack(push,1)
    struct Request
    {
        char magic[4]={'D','L','T','C'};
        std::uint16_t version=1,size=112;
        std::uint32_t pid=0,uid=0;
        std::uint64_t processStartedFileTime=0,requestId=0,issuedTickMs=0,snapshotTickMs=0,generation=0,snapshotSequence=0;
        std::uint32_t actorToken=0,mapToken=0,userToken=0,operation=0;
        std::int32_t targetId=0,x=0,y=0,reserved=0;
        std::uint64_t sessionNonce=0,runId=0;
    };
    struct Lease
    {
        std::uint32_t magic=0x4C545444,version=1;
        std::uint64_t nonce=0,runId=0,issuedFileTime=0;
        std::uint32_t uid=0,flags=1;
    };
#pragma pack(pop)
    static_assert(sizeof(Request)==112,"test control request ABI");
    static_assert(sizeof(Lease)==40,"test control lease ABI");
    const char* ValidateRequest(const Request&,const client_state::Snapshot&,const Lease&,std::uint64_t tick,std::uint64_t fileTime,std::uint64_t consumed) noexcept;
    struct PositionBinding { std::uint32_t actor=0,controller=0,vector=0; };
    bool ReadPositionBinding(const client_state::Reader&,std::uint32_t actor,PositionBinding&) noexcept;
    constexpr std::size_t MaximumGround=4096;
    struct Ground {std::uint32_t actor=0,id=0,descriptor=0;bool active=false,pending=false;};
    struct Move {Ground ground{};std::int32_t x=0,y=0;};
    struct MovePlan {std::array<Move,MaximumGround> moves{};std::size_t count=0;};
    const char* PlanGroundMoves(const Request&,const Ground*,std::size_t,MovePlan&) noexcept;
    struct PlayerTeleportOperations
    {
        void* context=nullptr;
        bool (*set)(void*,std::uint32_t,std::int32_t,std::int32_t,bool&) noexcept=nullptr;
        bool (*publish)(void*,std::uint32_t) noexcept=nullptr;
    };
    const char* CompletePlayerTeleport(const PlayerTeleportOperations&,std::uint32_t actor,
        std::int32_t x,std::int32_t y,bool& touched,std::size_t& applied) noexcept;
    void Initialize(const wchar_t*,const wchar_t*,client_state::Reader) noexcept;
    void OnFrame() noexcept;
    void OnFrame(const client_state::Snapshot&) noexcept;
    void PumpWorker() noexcept;
    void Shutdown() noexcept;
}
