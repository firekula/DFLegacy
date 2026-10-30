#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <cstdio>
#include <unordered_map>
#include "../../../src/DFLegacy.Debug/src/client_skill_state.h"
struct Reads {
    std::unordered_map<unsigned,unsigned> words;
    bool change=false;unsigned bucketReads=0;
    bool Read(unsigned a,unsigned& out) {
        if(a==0x40000 && ++bucketReads==2 && change) words[0x40000]=words[0x40004]=0x50000;
        auto it=words.find(a);if(it==words.end())return false;out=it->second;return true;
    }
    template<class T> bool ReadAt(unsigned a,unsigned b,T& out) {unsigned x=0;if(!Read(a+b,x))return false;out=static_cast<T>(x);return true;}
    bool SignedAt(unsigned a,unsigned b,int& out) {unsigned x=0;if(!ReadAt(a,b,x))return false;out=static_cast<int>(x);return true;}
    bool Getter(unsigned,unsigned,unsigned) {return true;}
};
Reads fixture(unsigned endpoint) {
    Reads r;constexpr unsigned actor=0x10000,head=0x30000,node=0x50000;
    r.words[actor+0x1E54]=head;r.words[actor+0x1E58]=1;
    r.words[actor+0x1E60]=0x40000;r.words[actor+0x1E64]=0x40008;
    r.words[actor+0x1E6C]=0;r.words[actor+0x1E70]=1;
    r.words[head]=r.words[head+4]=node;r.words[node]=r.words[node+4]=head;
    r.words[node+8]=20;r.words[node+0xC]=2;
    r.words[0x40000]=r.words[0x40004]=endpoint;
    r.words[actor+0x154]=0;r.words[0x20000+0x2EC]=1;
    return r;
}
void report(const char* label,Reads& r) {
    const auto count=dfl::client_skill_resources::Counter(r,0x10000,20);
    dfl::client_state::Player me;me.actorToken=0x10000;me.dead={true,false};me.sameMap={true,true};me.detached={true,false};me.mp={true,100};
    dfl::client_state::Skill s;s.id=20;s.level=1;s.resources.mpCost={true,6};s.resources.useLimit={true,2};s.resources.useCount=count;s.resources.itemsSufficient={true,true};s.cooldownBlocked={true,false};
    dfl::client_skill_state::Usability(r,me,0x20000,s);
    std::printf("%s: count.known=%d count.value=%d usable.known=%d usable.value=%d\n",label,count.known,count.value,s.usable.known,s.usable.value);
}
int main() {
    auto a=fixture(0);report("corrupt null equal bucket boundaries",a);
    auto b=fixture(0x60000);report("corrupt unreadable equal bucket boundaries",b);
    auto c=fixture(0x30000);c.change=true;report("bucket endpoints change head -> existing node during double read",c);
}
