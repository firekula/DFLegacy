#pragma once
#include "../src/loot_gather.h"
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <map>
#include <stdexcept>
#include <string>
using namespace dfl::loot_gather::detail;
namespace
{
    void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
    struct Fixture
    {
        std::map<std::uint32_t, unsigned char> memory;
        unsigned int index = 0, sets = 0;
        bool changeMapAfterFirst = false;
        static constexpr std::uint32_t Game = 0x1000000, Stage = 0x1100000, Map = 0x1200000,
            Player = 0x1300000, User = 0x1400000, Party = 0x1500000, Vector = 0x1600000,
            Drop = 0x1700000, Table = 0x1800000, Manager = 0x1900000,
            MirrorA = 0x1A00000, MirrorB = 0x1B00000, Key = 0xA56F9123;
        template<class T> void Put(std::uint32_t a, T value)
        { const auto* p = reinterpret_cast<const unsigned char*>(&value); for (std::size_t i=0;i<sizeof(T);++i) memory[a+static_cast<unsigned int>(i)]=p[i]; }
        void Secure(std::uint32_t a, std::uint32_t value)
        {
            const auto slot=index++;
            Put(a,(a+slot)^Key); Put(a+4,slot);
            Put(MirrorA+slot*4,value^(MirrorA+slot*4)^Key);
            Put(MirrorB+slot*4,value^(MirrorB+slot*4)^Key);
        }
        void Bytes(std::uint32_t a, std::initializer_list<unsigned char> bytes)
        { for (auto b:bytes) memory[a++]=b; }
        void Position(std::uint32_t actor, float x, float y)
        {
            const auto controller=actor+0x2000;
            Put(actor+0xE0,controller); Put(controller,0U); Put(controller+0x78,0U); Put(controller+0x7C,static_cast<unsigned char>(0));
            Put(controller+8,Table+0x1000); Put(Table+0x1000,0x4736A0U); Put(Table+0x1004,0x4736B0U);
            Put(controller+12,x); Put(controller+16,y); Put(controller+20,0.0f);
        }
        void AddDrop(unsigned int n, bool active=true, bool pending=false)
        {
            const auto actor=Drop+n*0x10000;
            Put(Vector+4+n*4,actor); Put(Map+0x14,Vector+8+n*4);
            Put(actor,0x999574U); Put(actor+0xD0,0x121U); Put(actor+0xE4,Map);
            Secure(actor+0xD4,100+n); Put(actor+0x7A0,actor+0x4000);
            Put(actor+0x128,static_cast<unsigned char>(active)); Put(actor+0x7A5,static_cast<unsigned char>(pending));
            Position(actor,500.0f+static_cast<float>(n),600.0f);
        }
        void ProtectedPosition(std::uint32_t actor)
        {
            const auto vector=actor+0x3000;
            Put(actor+0x2000,vector); Put(vector,Table+0x2000);
            Put(Table+0x2000,0x4CAC70U); Put(Table+0x2004,0x4CAC90U);
            for(unsigned int axis=0;axis<3;++axis)
            {
                const float value=axis==0?500.0f:axis==1?600.0f:0.0f;
                std::uint32_t bits=0; std::memcpy(&bits,&value,4);
                Secure(vector+0x35+axis*8,bits); Secure(vector+0x65+axis*8,bits);
            }
        }
        Fixture()
        {
            Put(0xCFD458,Manager); Put(0xCFD45C,Key); Put(Manager+4,1000U); Put(Manager+0x44,MirrorA); Put(Manager+0x48,MirrorB);
            Put(0xB5D6F0,Game); Secure(Game+0x10,3); Put(0xB5DCE0,0x2000000U); Put(0x2200010,Stage); Put(Stage+0xB0,Map);
            Put(0xB75A14,Player); Put(Player,Table); Put(Player+0xD0,0x111U); Put(Player+0xE4,Map); Secure(Player+0xD4,7); Secure(Player+0x142C,0);
            Put(Table+0x318,0x652A00U); Put(Table+0x74,0x4715F0U); Put(Table+0x80,0x471610U);
            Put(0x999574+0x74,0x4715F0U); Put(0x999574+0x80,0x471610U);
            Put(0xB5DCC8,Party); Put(Party+0x10,User); for (unsigned int i=1;i<4;++i) Put(Party+0x10+i*4,0U);
            Put(User+4,static_cast<std::uint16_t>(7)); Put(User+0x110,Player);
            Put(Map+0x10,Vector); Put(Map+0x14,Vector+4); Put(Vector,Player); Position(Player,123.0f,234.0f);
            Bytes(0x4715F0,{0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0x51,0x87,0x05,0});
            Bytes(0x4C9D50,{0x55,0x8B,0xEC,0x8B,0x01,0x85,0xC0,0xC6,0x81,0x8C,0,0,0,0x01,0x74,0x16});
            Bytes(0x471610,{0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0xD1,0x85,0x05,0});
            Bytes(0x4736B0,{0x55,0x8B,0xEC,0xD9,0x45,0x0C,0x8B,0x45,0x08,0xD9,0x5C,0x81,0x04});
            Bytes(0x4CAC90,{0x55,0x8B,0xEC,0x53,0x56,0x8B,0x75,0x08,0x8B,0x54,0xF1,0x69,0x57,0x8B,0x3D,0x58});
        }
        static bool Read(void* state,std::uint32_t a,void* output,std::size_t size)
        {
            auto& self=*static_cast<Fixture*>(state); auto* p=static_cast<unsigned char*>(output);
            for (std::size_t i=0;i<size;++i) { const auto it=self.memory.find(a+static_cast<unsigned int>(i)); if(it==self.memory.end()) return false; p[i]=it->second; } return true;
        }
        static bool Set(void* state,std::uint32_t actor,int x,int y)
        {
            auto& self=*static_cast<Fixture*>(state); ++self.sets;
            self.Put(actor+0x200C,static_cast<float>(x)); self.Put(actor+0x2010,static_cast<float>(y));
            if(self.changeMapAfterFirst) self.Put(Stage+0xB0,Map+0x10000);
            return true;
        }
        Runtime RuntimeFor() { return {this,&Read,&Set}; }
        float X(unsigned int n) { float value=0; Check(Read(this,Drop+n*0x10000+0x200C,&value,4),"position readable"); return value; }
    };
}
