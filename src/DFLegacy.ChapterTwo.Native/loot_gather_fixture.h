#pragma once
#include "ChapterTwoLootGather.h"
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <map>
#include <stdexcept>
#include <string>
using namespace dfl::chapter_two::loot_gather_detail;
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
            MirrorA = 0x1A00000, MirrorB = 0x1B00000, Registry = 0x1C00000,
            RegistryHead = 0x1D00000, RegistryNode = 0x1E00000, Key = 0xA56F9123;
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
            Put(actor+0xF4,controller); Put(controller,0U); Put(controller+0x78,0U); Put(controller+0x7C,static_cast<unsigned char>(0));
            Put(controller+8,Table+0x1000); Put(Table+0x1000,0x489BE0U); Put(Table+0x1004,0x489BF0U);
            Put(controller+12,x); Put(controller+16,y); Put(controller+20,0.0f);
        }
        void AddDrop(unsigned int n, bool active=true, bool pending=false)
        {
            const auto actor=Drop+n*0x10000;
            Put(Vector+4+n*4,actor); Put(Map+0x14,Vector+8+n*4);
            Put(actor,0xAEC474U); Put(actor+0xE0,0x121U); Put(actor+0xFC,Map);
            Secure(actor+0xE8,100+n); Put(actor+0x840,actor+0x4000);
            Put(actor+0x160,static_cast<unsigned char>(active)); Put(actor+0x845,static_cast<unsigned char>(pending));
            Position(actor,500.0f+static_cast<float>(n),600.0f);
        }
        void ProtectedPosition(std::uint32_t actor)
        {
            const auto vector=actor+0x3000;
            Put(actor+0x2000,vector); Put(vector,Table+0x2000);
            Put(Table+0x2000,0x4F4210U); Put(Table+0x2004,0x4F4230U);
            for(unsigned int axis=0;axis<3;++axis)
            {
                const float value=axis==0?500.0f:axis==1?600.0f:0.0f;
                std::uint32_t bits=0; std::memcpy(&bits,&value,4);
                Secure(vector+0x35+axis*8,bits); Secure(vector+0x65+axis*8,bits);
            }
        }
        Fixture()
        {
            Put(0xF35AF0,Manager); Put(0xF35AF4,Key); Put(Manager+4,1000U); Put(Manager+0x44,MirrorA); Put(Manager+0x48,MirrorB);
            Put(0xD564AC,Game); Secure(Game+0x10,3); Put(0xD56D1C,0x2000000U); Put(0x2200010,Stage); Put(Stage+0xB0,Map);
            Put(0xD7A8BC,Player); Put(Player,Table); Put(Player+0xE0,0x111U); Put(Player+0xFC,Map); Secure(Player+0xE8,7); Put(Player+0x1524,0U);
            Put(Table+0x3D0,0x6F4500U); Put(Table+0x84,0x487050U); Put(Table+0x90,0x487070U);
            Put(0xAEC474+0x84,0x487050U); Put(0xAEC474+0x90,0x487070U);
            Put(0xD56D04,Party); Put(Party+0x10,User); for (unsigned int i=1;i<4;++i) Put(Party+0x10+i*4,0U);
            Put(User+8,static_cast<std::uint16_t>(0)); Put(User+0x168,Player);
            Put(0xD56CF4,Registry); Put(Registry+0x2C,User);
            Put(Registry+0xC,RegistryHead); Put(Registry+0x10,1U);
            Put(RegistryHead,RegistryNode); Put(RegistryHead+4,RegistryNode);
            Put(RegistryNode,RegistryHead); Put(RegistryNode+4,RegistryHead);
            Put(RegistryNode+8,7U); Put(RegistryNode+12,User);
            Secure(0xD7A8E4,7);
            Put(Map+0x10,Vector); Put(Map+0x14,Vector+4); Put(Vector,Player); Position(Player,123.0f,234.0f);
            Bytes(0x487050,{0x55,0x8B,0xEC,0x8B,0x89,0xF4,0,0,0,0x5D,0xE9,0x71,0xBF,0x06,0});
            Bytes(0x4F2FD0,{0x55,0x8B,0xEC,0x8B,0x01,0x85,0xC0,0xC6,0x81,0x8C,0,0,0,0x01,0x74,0x16});
            Bytes(0x487070,{0x55,0x8B,0xEC,0x8B,0x89,0xF4,0,0,0,0x5D,0xE9,0xF1,0xBD,0x06,0});
            Bytes(0x489BF0,{0x55,0x8B,0xEC,0xD9,0x45,0x0C,0x8B,0x45,0x08,0xD9,0x5C,0x81,0x04});
            Bytes(0x4F4230,{0x55,0x8B,0xEC,0x53,0x56,0x8B,0x75,0x08,0x8B,0x54,0xF1,0x69,0x57,0x8B,0x3D,0xF0});
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
