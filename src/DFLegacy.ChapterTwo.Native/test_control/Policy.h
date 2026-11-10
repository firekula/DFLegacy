#pragma once
#include <cstddef>
#include <cstdint>
namespace dfl::chapter_two::test_control {
#pragma pack(push,1)
struct Request {
 std::uint32_t magic=0x43543244,version=1,pid=0,operation=1;
 std::uint64_t processStarted=0,nonce=0,id=0,issued=0,snapshot=0;
 std::uint32_t actor=0,map=0,stage=0,uid=0,target=0,reserved=0;
};
#pragma pack(pop)
static_assert(sizeof(Request)==80);
struct State {
 bool enabled=false,supported=false,valid=false;
 std::uint32_t pid=0,actor=0,map=0,stage=0,uid=0;
 std::uint64_t processStarted=0,nonce=0,snapshot=0,tick=0;
};
const char* Validate(const Request&,const State&,std::uint64_t consumed,std::uint64_t now) noexcept;
void Install(unsigned char*,std::size_t) noexcept;
}
