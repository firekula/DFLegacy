#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "channel_switch.h"
#include "memory_patch.h"
#include <array>
#include <cstdio>
#include <cstring>

namespace
{
    using namespace dfl::channel_switch::detail;
    bool Check(bool value, const char* message)
    {
        if (!value) std::fprintf(stderr,"FAIL: channel switch: %s\n",message);
        return value;
    }
    template<class T> void Put(unsigned char* at, T value) { std::memcpy(at,&value,sizeof(value)); }
    template<class T> T Get(const unsigned char* at) { T value{};std::memcpy(&value,at,sizeof(value));return value; }
    int Secure(void* at) { return Get<int>(static_cast<unsigned char*>(at)); }
    struct Fixture
    {
        std::array<unsigned char,64> Game{};
        std::array<unsigned char,0x7500> Channel{};
        std::array<unsigned char,0x200> Network{};
        std::array<unsigned char,208> Records{};
        void* CurrentGame{Game.data()};
        void* CurrentChannel{Channel.data()};
        void* CurrentNetwork{Network.data()};
        int Closes{}, Arms{}, ArmedBeforeClose{}, Tick{1000}, Deadline{};
        int Mutation{};
        Fixture()
        {
            Put(Game.data()+16,5);Put(Game.data()+44,Channel.data());
            Put(Channel.data()+0x288,0);Put(Channel.data()+0x290,4);Put(Channel.data()+0x298,0);
            const auto table=Channel.data()+0x203c+4*16;
            Put(table,Records.data());Put(table+4,Records.data()+Records.size());Put(table+8,Records.data()+Records.size());
            std::memcpy(Records.data()+64,"192.168.3.177",13);Put(Records.data()+84,7004);
            std::memcpy(Records.data()+104+64,"127.0.0.1",10);Put(Records.data()+104+84,7001);
            Put(Network.data()+0x1b4,123u);
        }
        static bool Capture(void* c,Selection& s)
        {
            auto& f=*static_cast<Fixture*>(c);
            return ReadSelection(f.CurrentGame,f.CurrentChannel,f.CurrentNetwork,Secure,s);
        }
        static void Close(void* c,void* n)
        {
            auto& f=*static_cast<Fixture*>(c);++f.Closes;
            Put(static_cast<unsigned char*>(n)+0x1b4,0u);
            switch(f.Mutation)
            {
            case 1:f.CurrentGame=nullptr;break;
            case 2:f.CurrentChannel=nullptr;break;
            case 3:f.CurrentNetwork=nullptr;break;
            case 4:Put(f.Channel.data()+0x298,1);break;
            case 5:Put(f.Records.data()+84,8000);break;
            case 6:Put(f.Game.data()+16,1);break;
            case 7:Put(f.Network.data()+0x1b4,124u);break;
            case 8:ArmPending(c,f.Channel.data(),Ops());break; // FD_CLOSE delivered within close.
            }
        }
        static void Arm(void* c,void*)
        {
            auto& f=*static_cast<Fixture*>(c);++f.Arms;
            if (Get<unsigned>(f.Network.data()+0x1b4)!=0) ++f.ArmedBeforeClose;
            // The real original armer sets this secure flag and restarts500ms.
            Put(f.Channel.data()+0x2ac,1);f.Deadline=f.Tick+500;
        }
        static Operations Ops() { return {Capture,Close,Arm}; }
    };
    std::uintptr_t FailAddress{};
    bool FailSecond(std::uintptr_t a,const void* b,std::size_t n)
    {
        if(a==FailAddress){FailAddress=0;return false;}
        return dfl::memory_patch::WriteBytes(a,b,n);
    }
}

bool TestChannelSwitch()
{
    using namespace dfl::channel_switch::detail;
    bool ok=true;
    Fixture f;Selection s;
    ok &= Check(Fixture::Capture(&f,s)&&s.Group==0&&s.Category==4&&s.Row==0
        &&Get<int>(s.Metadata.data()+84)==7004,"current legacy-layout catalog resolves actual PvP endpoint");
    CloseAndArm(&f,f.Network.data(),Fixture::Ops());
    ok &= Check(f.Closes==1&&f.Arms==1&&f.ArmedBeforeClose==0&&f.Deadline==1500
        &&Get<unsigned>(f.Network.data()+0x1b4)==0,"ACK closes original socket before arming native reconnect");
    f.Tick=1400;ArmPending(&f,f.Channel.data(),Fixture::Ops());
    CloseAndArm(&f,f.Network.data(),Fixture::Ops());
    ok &= Check(f.Arms==1&&f.Deadline==1500,"late FD_CLOSE and repeated ACK preserve earliest timer");
    // Native selection consumes the row and flags; a later user choice must
    // start a new timer, rather than being suppressed by a process-global latch.
    Put(f.Channel.data()+0x2ac,0);Put(f.Channel.data()+0x298,0);
    Put(f.Network.data()+0x1b4,456u);f.Tick=2000;
    CloseAndArm(&f,f.Network.data(),Fixture::Ops());
    ok &= Check(f.Arms==2&&f.Deadline==2500,"a later channel selection starts its own native timer");
    Fixture nested;nested.Mutation=8;
    CloseAndArm(&nested,nested.Network.data(),Fixture::Ops());
    ok &= Check(nested.Arms==1&&nested.Deadline==1500,"FD_CLOSE inside original close cannot double arm");
    Fixture manual;Put(manual.Channel.data()+0x298,-1);
    CloseAndArm(&manual,manual.Network.data(),Fixture::Ops());
    ok &= Check(manual.Closes==1&&manual.Arms==0,"manual channel return remains close-only");
    Fixture normal;Put(normal.Channel.data()+0x298,1);
    Put(normal.Channel.data()+0x290,2);
    std::memcpy(normal.Channel.data()+0x203c+2*16,normal.Channel.data()+0x203c+4*16,12);
    CloseAndArm(&normal,normal.Network.data(),Fixture::Ops());
    ok &= Check(normal.Arms==1&&Fixture::Capture(&normal,s)&&Get<int>(s.Metadata.data()+84)==7001,
        "pending normal endpoint is selected from metadata without a PvP hardcode");
    for(int mutation=1;mutation<=7;++mutation)
    {
        Fixture stale;stale.Mutation=mutation;
        CloseAndArm(&stale,stale.Network.data(),Fixture::Ops());
        ok &= Check(stale.Closes==1&&stale.Arms==0,"stale identity, target, scene or replacement socket cannot arm");
    }
    for(int invalid=0;invalid<11;++invalid)
    {
        Fixture bad;
        switch(invalid)
        {
        case 0:Put(bad.Channel.data()+0x288,109);break;
        case 1:Put(bad.Channel.data()+0x290,8);break;
        case 2:Put(bad.Channel.data()+0x298,2);break;
        case 3:Put(bad.Channel.data()+0x203c+64+4,bad.Records.data()+103);break;
        case 4:Put(bad.Records.data()+84,0);break;
        case 5:Put(bad.Records.data()+84,65536);break;
        case 6:std::memset(bad.Records.data()+64,'x',16);break;
        case 7:bad.Records[64]=0;break;
        case 8:bad.Records[88]=1;break;
        case 9:Put(bad.Game.data()+44,static_cast<void*>(nullptr));break;
        case 10:Put(bad.Channel.data()+0x2ec,1);break;
        }
        CloseAndArm(&bad,bad.Network.data(),Fixture::Ops());
        ok &= Check(bad.Closes==1&&bad.Arms==0,"invalid or already connecting selection fails closed after original close");
    }
    Fixture foreign;
    std::array<unsigned char,0x200> oldNetwork{};
    CloseAndArm(&foreign,oldNetwork.data(),Fixture::Ops());
    ok &= Check(foreign.Closes==1&&foreign.Arms==0&&Get<unsigned>(foreign.Network.data()+0x1b4)==123,
        "old network close cannot arm or close the current network");
    Fixture unreadable;
    Put(unreadable.Channel.data()+0x203c+64,std::uintptr_t{1});
    Put(unreadable.Channel.data()+0x203c+64+4,std::uintptr_t{105});
    Put(unreadable.Channel.data()+0x203c+64+8,std::uintptr_t{105});
    CloseAndArm(&unreadable,unreadable.Network.data(),Fixture::Ops());
    ok &= Check(unreadable.Closes==1&&unreadable.Arms==0,"unreadable catalog fails closed without skipping original close");
    Fixture pending;
    ok &= Check(!ArmPending(&pending,pending.Channel.data(),Fixture::Ops())&&pending.Arms==0,
        "FD_CLOSE guard cannot arm while a live socket remains");
    Put(pending.Network.data()+0x1b4,0u);
    ok &= Check(!ArmPending(&pending,reinterpret_cast<void*>(1),Fixture::Ops())&&pending.Arms==0,
        "stale channel passed by FD_CLOSE cannot arm current selection");
    ok &= Check(ArmPending(&pending,pending.Channel.data(),Fixture::Ops())&&pending.Arms==1,
        "native FD_CLOSE alone retains its valid pending transition");

    auto* page=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if(!page)return Check(false,"fixture allocation");
    const std::array<unsigned char,5> fd{0xE8,0x6B,0x07,0xFE,0xFF},ack{0xE8,0xEA,0x2C,0x4A,0x00};
    const std::array<std::uintptr_t,2> sites{reinterpret_cast<std::uintptr_t>(page),reinterpret_cast<std::uintptr_t>(page+16)};
    const std::array<std::uintptr_t,2> targets{sites[0]+200,sites[0]+300};
    std::memcpy(page,fd.data(),5);std::memcpy(page+16,ack.data(),5);page[16]=0x90;
    ok &= Check(!InstallCalls(sites,targets,dfl::memory_patch::WriteBytes)&&std::memcmp(page,fd.data(),5)==0,
        "second-site byte mismatch leaves first CALL unchanged");
    std::memcpy(page+16,ack.data(),5);FailAddress=sites[1];
    ok &= Check(!InstallCalls(sites,targets,FailSecond)&&std::memcmp(page,fd.data(),5)==0&&std::memcmp(page+16,ack.data(),5)==0,
        "partial install write failure restores both original calls");
    ok &= Check(InstallCalls(sites,targets,dfl::memory_patch::WriteBytes)
        &&page[0]==0xE8&&page[16]==0xE8
        &&sites[0]+5+Get<std::int32_t>(page+1)==targets[0]
        &&sites[1]+5+Get<std::int32_t>(page+17)==targets[1],"transaction installs two actual relative CALL targets");
    ok &= Check(InstallCalls(sites,targets,dfl::memory_patch::WriteBytes),"repeated install recognizes its complete owned pair");
    VirtualFree(page,0,MEM_RELEASE);
    // Exercise the actual production image preflight and two-site installation.
    auto* executable=static_cast<unsigned char*>(VirtualAlloc(nullptr,0x500000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if(!executable)return Check(false,"image fixture allocation");
    const auto base=reinterpret_cast<std::uintptr_t>(executable);
    auto bytes=[&](std::size_t rva,std::initializer_list<unsigned char> data)
    { std::memcpy(executable+rva,data.begin(),data.size()); };
    bytes(0x4B2810,{0x56,0x8B,0xF1,0x8B,0x0D,0xF0,0xD6,0xB5});
    bytes(0x493480,{0x83,0xB9,0x98,0x02,0x00,0x00,0xFF,0x74,0x5F});
    bytes(0x002030,{0x53,0x8B,0x1D,0x5C,0xD4,0xCF,0x00,0x56,0x57,0x8B,0xF9});
    bytes(0x005310,{0x8B,0x41,0x1C,0x8B,0x15,0x58,0xD4,0xCF});
    bytes(0x4F3E80,{0x55,0x8B,0xEC,0x51,0x56,0x8B,0xF1,0x8D});
    bytes(0x00FB16,{0x83,0xF8,0x05,0x75,0x10});
    bytes(0x4B2CFF,{0x39,0xB8,0xB4,0x01,0x00,0x00,0x75,0x0E});
    std::memcpy(executable+0x4B2D10,fd.data(),5);std::memcpy(executable+0xFB21,ack.data(),5);
    const std::array<std::uintptr_t,2> hookTargets{base+0x100,base+0x200};
    for(std::size_t rva : {0x4B2810u,0x493480u,0x2030u,0x5310u,0x4F3E80u,0xFB16u,0x4B2CFFu})
    {
        executable[rva]^=1;
        ok &= Check(!InstallImage(base,hookTargets,dfl::memory_patch::WriteBytes)
            &&std::memcmp(executable+0x4B2D10,fd.data(),5)==0
            &&std::memcmp(executable+0xFB21,ack.data(),5)==0,"entrypoint/scene/socket mismatch changes neither hook site");
        executable[rva]^=1;
    }
    ok &= Check(InstallImage(base,hookTargets,dfl::memory_patch::WriteBytes)
        &&base+0x4B2D15+Get<std::int32_t>(executable+0x4B2D11)==hookTargets[0]
        &&base+0xFB26+Get<std::int32_t>(executable+0xFB22)==hookTargets[1],"validated production image preflight installs both wrappers");
    VirtualFree(executable,0,MEM_RELEASE);
    return ok;
}
