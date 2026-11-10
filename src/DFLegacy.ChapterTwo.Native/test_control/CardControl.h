#pragma once
// Included only by the explicitly linked private test runtime.
struct Cards {
 U screen=0,phase=0,count=0,slot=4;
 std::array<U,4> free{},paid{};
 std::array<bool,4> freeVisible{},paidVisible{};
 U freeSelected=~0u,paidSelected=~0u,freePending=~0u,paidPending=~0u;
};
Cards ReadCards(const State&s){
 Cards c{};if(s.scene!=3||!s.actor)return c;
 const auto manager=Read<U>(0xd56d24);if(!manager)return c;
 const auto screen=Read<U>(manager+0x1278);if(!screen||Read<U>(screen)!=0xc11c1c||Read<U>(screen+0x1b0)!=4)return c;
 const auto vt=Read<U>(s.actor);if(vt<0x400000||vt>=0x1257000||Read<U>(vt+0x6bc)!=0x6f54e0)return c;
 using Slot=U(__thiscall*)(void*);const auto slot=reinterpret_cast<Slot>(0x6f54e0)(reinterpret_cast<void*>(s.actor));if(slot>=4)return c;
 c.screen=screen;c.phase=Read<U>(screen+4);c.count=4;c.slot=slot;
 for(unsigned i=0;i<4;++i){
  c.free[i]=Read<U>(screen+0x2cc+i*4);c.paid[i]=Read<U>(screen+0x304+i*4);
  if((c.free[i]!=~0u&&c.free[i]>=4)||(c.paid[i]!=~0u&&c.paid[i]>=4))return {};
  const auto f=Read<U>(screen+0x2dc+i*4),p=Read<U>(screen+0x314+i*4);if(!f||!p)return {};
  c.freeVisible[i]=Read<unsigned char>(f+0x10)!=0;c.paidVisible[i]=Read<unsigned char>(p+0x10)!=0;
 }
 c.freeSelected=Read<U>(screen+0x2ec);c.paidSelected=Read<U>(screen+0x324);
 c.freePending=Read<U>(screen+0x368);c.paidPending=Read<U>(screen+0x348);return c;
}
const char* SelectCard(const Request&r,const State&s,const Cards&before,unsigned&applied){
 if((r.operation!=4&&r.operation!=5)||r.target>=4||!before.screen)return "card_unavailable";
 constexpr unsigned char freeEntry[]={0x55,0x8b,0xec,0x51,0x56,0x8b,0xf1,0x83,0xbe,0x68,0x03,0,0,0xff,0x75,0x46};
 constexpr unsigned char paidEntry[]={0x55,0x8b,0xec,0x81,0xec,0x04,0x01,0,0,0x56,0x8b,0xf1,0x83,0xbe,0x48,0x03,0,0,0xff};
 if(std::memcmp(reinterpret_cast<void*>(0x958fb0),freeEntry,sizeof(freeEntry))||std::memcmp(reinterpret_cast<void*>(0x959cf0),paidEntry,sizeof(paidEntry)))return "card_layout";
 const auto c=ReadCards(s);if(!c.screen||c.screen!=before.screen||c.phase!=before.phase||c.slot!=before.slot||c.free!=before.free||c.paid!=before.paid)return "card_changed";
 const bool free=r.operation==4;const auto&owners=free?c.free:c.paid;const auto&visible=free?c.freeVisible:c.paidVisible;
 if(!visible[r.target]||!(free?before.freeVisible:before.paidVisible)[r.target]||owners[r.target]!=~0u||(free?c.freeSelected:c.paidSelected)!=~0u||(free?c.freePending:c.paidPending)!=~0u)return "card_taken_hidden_or_pending";
 for(auto owner:owners)if(owner==c.slot)return "card_already_owned";
 using Pick=void(__thiscall*)(void*,U,U);applied=1;
 reinterpret_cast<Pick>(free?0x958fb0:0x959cf0)(reinterpret_cast<void*>(c.screen),c.slot,r.target);
 return Read<U>(c.screen+(free?0x368:0x348))==r.target?"applied_native_card_selection":"native_card_not_pending";
}
