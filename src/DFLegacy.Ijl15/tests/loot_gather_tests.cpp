#include "loot_gather_fixture.h"
namespace
{
    void Run()
    {
        constexpr char marker[]="@DFLegacy:loot-magnet:v1@";
        Check(IsMarker(16,marker,sizeof(marker)-1),"exact Type16 marker must be recognized");
        Check(!IsMarker(0,marker,sizeof(marker)-1)&&!IsMarker(16,marker,sizeof(marker))&&!IsMarker(16,"hello",5),"ordinary chat must pass through");
        Fixture f; auto runtime=f.RuntimeFor(); Request request;
        Check(Capture(runtime,7,request),"alive solo context captured");
        Request wrong; Check(!Capture(runtime,8,wrong),"another sender cannot authorize");
        f.AddDrop(0); f.AddDrop(1,false); f.AddDrop(2,true,true);
        const auto before=f.memory;
        std::size_t moved=0; Check(Gather(runtime,request,moved)==nullptr&&moved==1&&f.sets==1,"only visible active nonpending ground moves");
        Check(f.X(0)==123&&f.X(1)==501&&f.X(2)==502,"preserve hidden and pending coordinates");
        for(const auto& byte:before)
        {
            if(byte.first>=Fixture::Drop+0x200C&&byte.first<Fixture::Drop+0x2014) continue;
            Check(f.memory.at(byte.first)==byte.second,"identity, descriptor, metadata and other memory unchanged");
        }
        f.AddDrop(3); Check(Gather(runtime,request,moved)==nullptr&&moved==2&&f.X(3)==123,"repeat gathers newly spawned ground");
        const auto prior=f.sets; f.Put(Fixture::Party+0x14,Fixture::User+0x1000);
        Check(!Capture(runtime,7,wrong)&&Gather(runtime,request,moved)!=nullptr&&f.sets==prior,"party changes reject pending request");
        Fixture dead; dead.Secure(Fixture::Player+0x142C,1); Check(!Capture(dead.RuntimeFor(),7,wrong),"dead players rejected");
        Fixture town; town.Secure(Fixture::Game+0x10,2); Check(!Capture(town.RuntimeFor(),7,wrong),"town rejected");
        Fixture changed; changed.AddDrop(0); changed.AddDrop(1); Check(Capture(changed.RuntimeFor(),7,request),"capture before transition"); changed.changeMapAfterFirst=true;
        Check(Gather(changed.RuntimeFor(),request,moved)!=nullptr&&moved==1&&changed.sets==1,"map revalidated before every actor");
        Fixture signature; signature.AddDrop(0); Check(Capture(signature.RuntimeFor(),7,request),"capture signature fixture"); signature.Put(0x4715F0,static_cast<unsigned char>(0xCC));
        Check(Gather(signature.RuntimeFor(),request,moved)!=nullptr&&signature.sets==0,"signature mismatch cannot write");
        Fixture malformed; malformed.AddDrop(0); Check(Capture(malformed.RuntimeFor(),7,request),"capture malformed fixture"); malformed.Put(Fixture::Drop,Fixture::Table);
        Check(Gather(malformed.RuntimeFor(),request,moved)!=nullptr&&malformed.sets==0,"unknown ground layout cannot write");
        Fixture mirror; mirror.Put(Fixture::MirrorB,0U); Check(!Capture(mirror.RuntimeFor(),7,wrong),"broken protected mirror rejects context");
        Fixture missing; missing.AddDrop(0); Check(Capture(missing.RuntimeFor(),7,request),"capture member fixture"); missing.Put(Fixture::Vector,0U);
        Check(Gather(missing.RuntimeFor(),request,moved)!=nullptr&&missing.sets==0,"detached local player rejects batch");
        Fixture duplicate; duplicate.AddDrop(0); duplicate.AddDrop(1); Check(Capture(duplicate.RuntimeFor(),7,request),"capture duplicate fixture"); duplicate.Secure(Fixture::Drop+0x10000+0xD4,100);
        Check(Gather(duplicate.RuntimeFor(),request,moved)!=nullptr&&duplicate.sets==0,"duplicate ground IDs fail before any write");
        Fixture oversized; Check(Capture(oversized.RuntimeFor(),7,request),"capture oversized fixture"); oversized.Put(Fixture::Map+0x14,Fixture::Vector+4097*4);
        Check(Gather(oversized.RuntimeFor(),request,moved)!=nullptr&&oversized.sets==0,"oversized actor list rejected");
        Fixture secure; secure.AddDrop(0); secure.ProtectedPosition(Fixture::Drop); Check(Capture(secure.RuntimeFor(),7,request),"capture protected position fixture");
        Check(Gather(secure.RuntimeFor(),request,moved)==nullptr&&secure.sets==1,"valid protected position can use native setter");
        Fixture broken; broken.AddDrop(0); broken.ProtectedPosition(Fixture::Drop); Check(Capture(broken.RuntimeFor(),7,request),"capture broken position fixture");
        broken.Put(Fixture::MirrorB+(broken.index-1)*4,0U);
        Check(Gather(broken.RuntimeFor(),request,moved)!=nullptr&&broken.sets==0,"second protected position mirror corruption rejects before writes");
        Fixture stale; stale.AddDrop(0); Check(Capture(stale.RuntimeFor(),7,request),"capture old room"); stale.Put(Fixture::Stage+0xB0,Fixture::Map+0x10000);
        Check(Gather(stale.RuntimeFor(),request,moved)!=nullptr&&stale.sets==0,"old room request cannot move");
    }
}
int main()
{
    try { Run(); std::puts("loot_gather_tests PASS"); return 0; }
    catch(const std::exception& error) { std::fprintf(stderr,"FAIL: %s\n",error.what()); return 1; }
}
