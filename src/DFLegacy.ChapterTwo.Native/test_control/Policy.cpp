#include "Policy.h"
namespace dfl::chapter_two::test_control {
const char* Validate(const Request&r,const State&s,std::uint64_t consumed,std::uint64_t now) noexcept {
 if(!s.enabled)return "disabled";
 if(!s.supported)return "layout";
 if(!s.valid||!s.pid||!s.processStarted||!s.nonce||!s.snapshot||!s.tick||!s.actor||!s.map||!s.stage||!s.uid||s.uid>65535)return "context";
 if(r.magic!=0x43543244||r.version!=1||r.operation!=1||r.reserved||!r.target||r.target>65535)return "request";
 if(r.pid!=s.pid||r.processStarted!=s.processStarted||r.nonce!=s.nonce)return "process";
 if(r.actor!=s.actor||r.map!=s.map||r.stage!=s.stage||r.uid!=s.uid||r.snapshot!=s.snapshot)return "snapshot";
 if(!r.id||r.id<=consumed)return "replayed";
 if(s.tick>now||now-s.tick>750||r.issued<s.tick||r.issued>now)return "expired";
 return nullptr;
}
}
