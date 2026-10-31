#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <limits>
#include <memory>
#include <string>
#include <unordered_map>
#include "../src/client_state.h"

namespace
{
    using namespace dfl::client_state;
    int Failures = 0;
    void Check(bool condition, const char* label)
    {
        if (!condition) { ++Failures; std::fprintf(stderr, "FAIL client state: %s\n", label); }
    }
    struct Memory
    {
        std::unordered_map<std::uint32_t, unsigned char> bytes;
        bool layout = true, changeActorAtEnd = false, changeTownAreaAtEnd = false, changeMenuTargetAtEnd = false, changeMenuAtEnd = false;
        bool readWrappedAddress = false, readNullMirrorAddress = false;
        bool changeSkillAtEnd = false;unsigned skillSlotReads = 0;
        bool changeSelectorAtEnd = false;unsigned selectorReads = 0;
        bool changeSelectorIndex = false;unsigned selectorIndexReads = 0;
        bool changeCommandLock=false;unsigned commandLockReads=0;
        bool changeMouseLock=false,changeMouseDeferral=false;unsigned mouseLockReads=0,mouseDeferralReads=0;
        bool changePeerBindingAtMp=false;unsigned peerBindingReads=0;
        bool changeUseCount=false,changeUseBucket=false,changeInventory=false;unsigned useCountReads=0,useBucketReads=0,inventoryReads=0;
        std::uint64_t utc = 123456789000ULL, tick = 1000, counter = 5;
        std::uint32_t index = 0, readCount = 0, townAreaReads = 0, menuTargetReads = 0, menuReads = 0;
        static constexpr std::uint32_t Key = 0x12345678, Manager = 0x01002000;
        static constexpr std::uint32_t Actor = 0x04000000, Stage = 0x05000000, Map = 0x06000000;
        static constexpr std::uint32_t User = 0x07000000, Party = 0x08000000, Vtable = 0x03000000;
        template<class T> void Put(std::uint32_t address, T value)
        {
            const auto* data = reinterpret_cast<const unsigned char*>(&value);
            for (std::size_t i = 0; i < sizeof(value); ++i) bytes[address + static_cast<std::uint32_t>(i)] = data[i];
        }
        void U32(std::uint32_t address, std::uint32_t value) { Put(address, value); }
        void Secure(std::uint32_t address, std::uint32_t bits)
        {
            const auto slot = index++;
            U32(address, (address + slot) ^ Key); U32(address + 4, slot);
            const auto a = 0x02000000 + slot * 4, b = 0x02100000 + slot * 4;
            U32(a, bits ^ a ^ Key); U32(b, bits ^ b ^ Key);
        }
        void SecureFloat(std::uint32_t address, float value)
        {
            std::uint32_t bits = 0; std::memcpy(&bits, &value, sizeof(bits)); Secure(address, bits);
        }
        void SetScene(std::uint32_t scene) { Secure(0x01000010, scene); }
        void SkillTimer(std::uint32_t now)
        {
            U32(0xB75978,0x99875C);Secure(0xB75988,1);Secure(0xB75990,1);Secure(0xB75998,0);
            Secure(0xB75980,1);Secure(0xB759A0,0);Put<unsigned char>(0xCFD44C,0);U32(0xCFD27C,now);
            U32(0x04400000,0x99EE78);U32(0x04400004,Actor);Secure(0x04400008,20);Secure(0x0440034C,3000);Secure(0x04400354,8000);
        }
        void SkillResources()
        {
            U32(Vtable+0x138,0x493880);U32(Vtable+0x368,0x492A60);U32(Actor+0x154,0);
            Secure(0x04400008,20);Secure(0x044002EC,1);Secure(0x04400208,0);
            U32(0x04400184,0x09200000);U32(0x04400188,0x09200008);Secure(0x09200000,6);
            U32(0x044001BC,0);U32(0x044001C0,0);U32(0x044001CC,0);U32(0x044001D0,0);
            U32(Actor+0x1E7C,0x09110000);U32(Actor+0x1E80,0);U32(0x09110000,0x09110000);U32(0x09110004,0x09110000);
            for(unsigned i=0;i<23;++i){U32(Actor+0x13C4+i*4,0);U32(Actor+0x1FCC+i*4,0);}
        }
        void SkillLimit(std::uint32_t used)
        {
            Secure(0x044002E0,1);
            U32(0x044001CC,0x09F80000);U32(0x044001D0,0x09F80008);Secure(0x09F80000,2);
            U32(Actor+0x1E54,0x09F90000);U32(Actor+0x1E58,1);
            U32(Actor+0x1E60,0x09FA0000);U32(Actor+0x1E64,0x09FA0008);U32(Actor+0x1E6C,0);U32(Actor+0x1E70,1);
            U32(0x09FA0000,0x09FB0000);U32(0x09FA0004,0x09F90000);
            U32(0x09F90000,0x09FB0000);U32(0x09F90004,0x09FB0000);
            U32(0x09FB0000,0x09F90000);U32(0x09FB0004,0x09F90000);U32(0x09FB0008,20);Secure(0x09FB000C,used);
        }
        void SkillItems(std::uint32_t quantity)
        {
            U32(0x044001BC,0x09F40000);U32(0x044001C0,0x09F40018);Secure(0x04400020,10);
            Secure(0x09F40000,1001);Secure(0x09F40008,2);Secure(0x09F40010,4);
            U32(0xB5DCEC,0x09F00000);U32(Actor+0x242C,0x09F00000);Secure(0x09F00010,2);U32(0x09F00018,0x09F10000);
            U32(0x09F10000,0x09F20000);U32(0x09F10004,0);
            U32(0x09F20000,0x09F30000);U32(0x09F30018,0x4619A0);U32(0x09F30034,0x461AB0);
            U32(0x09F2000C,1001);Secure(0x09F200A0,quantity);
        }
        void Selector()
        {
            Town();SetScene(2);U32(0x99A5BC,0x8A2A20);U32(0x99A5C0,0x8A0EB0);
            Put<unsigned char>(0x01090100,0);U32(0x010901A0,1);U32(0x01090068,0);Secure(0x01090144,0);
            U32(0x010901F4,0x09C00000);U32(0x010901F8,0x09C00020);U32(0x010901FC,0x09C00020);
            U32(0x09C00000,1);U32(0x09C00004,0);U32(0x09C00008,3);U32(0x09C00010,31);U32(0x09C00014,2);U32(0x09C00018,3);
            Put<std::int32_t>(0x01090200,1);Put<std::int32_t>(0x01090204,0);
        }
        void Dialog()
        {
            constexpr std::uint32_t ui=0x01050000,window=0x09A00000,root=0x09B00000;
            U32(0xB5DCE8,ui);
            for(auto type:{7U,9U,0x23U,0x25U,0x30U,0x56U,0x59U,0x6AU}){U32(ui+0xD8+type*16,0);U32(ui+0xDC+type*16,0);U32(ui+0xE0+type*16,0);}
            U32(ui+0xEF8,0);U32(ui+0xEFC,0);U32(ui+0xF00,0);
            U32(ui+0xD8+0x59*16,0x09A10000);U32(ui+0xDC+0x59*16,0x09A10004);U32(ui+0xE0+0x59*16,0x09A10004);U32(0x09A10000,window);
            U32(window,0xA7EA5C);U32(0xA7EA88,0x7BF640);U32(0xA7EA8C,0x7BF650);
            U32(window+4,0x59);U32(window+8,root);U32(window+0x10,ui);U32(root,0xA7E09C);U32(root+0x44,window);
            Put<unsigned char>(root+0x10,1);Put<unsigned char>(root+0x11,1);
        }
        void Command(std::uint32_t checker=0x09D00000)
        {
            U32(Vtable+0x5D4,0x493770);U32(Actor+0x2384,checker);
            if(!checker)checker=0xB5DE68;
            U32(checker,0x99A88C);U32(0x99A8B4,0x8BA0C0);U32(0x99A8BC,0x8BA7F0);
            U32(0x99A8CC,0x8BA030);U32(0x99A8D0,0x43BC30);Put<std::int32_t>(checker+0x64,0);
            Put<std::int32_t>(checker+0x68,0);Put<unsigned char>(checker+0x90,0);
        }
        void RegisteredWindow(std::uint32_t type,std::uint32_t table,std::uint32_t window,std::uint32_t root,std::uint32_t vector)
        {
            constexpr std::uint32_t ui=0x01050000;
            U32(ui+0xD8+type*16,vector);U32(ui+0xDC+type*16,vector+4);U32(ui+0xE0+type*16,vector+4);U32(vector,window);
            U32(window,table);U32(table+0x2C,0x7BF640);U32(table+0x30,0x7BF650);
            U32(window+4,type);U32(window+8,root);U32(window+0x10,ui);U32(root,0xA7E09C);U32(root+0x44,window);
            Put<unsigned char>(root+0x10,1);Put<unsigned char>(root+0x11,1);
        }
        void LowDialogLayer()
        {
            constexpr std::uint32_t layer=0x09E01000,head=0x09E11000,node=0x09E21000;
            U32(0x010500C8,layer);U32(layer,0x9A1608);U32(0x9A1618,0x8F6880);
            Put<unsigned char>(layer+0x10,1);Put<unsigned char>(layer+0x11,1);
            U32(layer+0x64,head);U32(layer+0x68,1);U32(head,node);U32(head+4,node);
            U32(node,head);U32(node+4,head);U32(node+8,0x09B01000);
        }
        void MouseDeferral(unsigned char value)
        {
            const unsigned char read[]={0xA0,0x08,0xD2,0xCF,0x00};
            const unsigned char acquire[]={0xC6,0x05,0x08,0xD2,0xCF,0x00,0x01};
            const unsigned char release[]={0xC6,0x05,0x08,0xD2,0xCF,0x00,0x00};
            for(unsigned i=0;i<5;++i)Put<unsigned char>(0x8FCAE8+i,read[i]);
            for(unsigned i=0;i<7;++i){Put<unsigned char>(0x7993C4+i,acquire[i]);Put<unsigned char>(0x8FCC9D+i,release[i]);}
            Put<unsigned char>(0xCFD208,value);
        }
        void DialogLayer()
        {
            constexpr std::uint32_t layer=0x09E00000,head=0x09E10000,node=0x09E20000;
            U32(0x010500D0,layer);U32(layer,0x9A1608);U32(0x9A1618,0x8F6880);
            Put<unsigned char>(layer+0x10,1);Put<unsigned char>(layer+0x11,0);
            U32(layer+0x64,head);U32(layer+0x68,1);U32(head,node);U32(head+4,node);
            U32(node,head);U32(node+4,head);U32(node+8,0x09B00000);
        }
        void PlayerAt(std::uint32_t actor, std::uint32_t uid)
        {
            U32(actor, Vtable); U32(actor + 0xCC, 0x111); U32(actor + 0xD0, 0x111); Secure(actor + 0xD4, uid);
            U32(actor + 0xE4, Map); U32(actor + 0xE0, 0);
            Secure(actor + 0x1168, 470); Secure(actor + 0xE38, 470);
            SecureFloat(actor + 0xE40, 1.0f); Secure(actor + 0x142C, 0);
            Secure(actor + 0x1170, 123); Secure(actor + 0xE48, 1000); SecureFloat(actor + 0xE50, 1.25f);
            Put<unsigned char>(actor + 0x20A2, 0);
            for (std::uint32_t id = 0; id < MaximumSkills; ++id) U32(actor + 0x1AEC + id * 4, 0);
        }
        void Town()
        {
            SetScene(1); U32(0xB75A18, Actor); U32(Actor, 0x9988DC);
            Put<unsigned char>(0xB5F0A0, 1);
            U32(0x9988DC + 0x80, 0x471610); U32(User + 0x10C, Actor);
            U32(0x0100001C, 0x01030000); Put<std::int32_t>(0x01030088, 1);
            U32(0x0103008C, 0x01040000); Put<std::int32_t>(0x01040000, 2);
            Put<std::int32_t>(Actor + 0x23C, 300); Put<std::int32_t>(Actor + 0x240, 240);
            Put<std::int32_t>(Actor + 0x234, 1); Put<std::int32_t>(Actor + 0x238, 4);
            U32(0xB5DCE8, 0x01050000); Put<std::int32_t>(0x01050FC4, 0);
            U32(0x01050FF0, 0x01060000); U32(0x01060000, 0xA7CF3C); U32(0x01060004, 0x01070000);
            Put<unsigned char>(0x01070010, 1); Put<unsigned char>(0x01070011, 1);
            U32(0x01050F08, 0x01080000); U32(0x01080000, 0xA7CF2C); Put<std::int32_t>(0x01080060, 2);
            U32(0x01000020, 0x01090000); U32(0x01090000, 0x99A5B8);
            Put<unsigned char>(0x01090004, 1); Put<std::int32_t>(0x01090200, 3); Put<std::int32_t>(0x01090204, 4);
        }
        Memory()
        {
            U32(0xCFD458, Manager); U32(0xCFD45C, Key); U32(Manager + 4, 4096);
            U32(Manager + 0x44, 0x02000000); U32(Manager + 0x48, 0x02100000);
            U32(0xB5D6F0, 0x01000000); U32(0xB5DCE0, 0x01010000);
            SetScene(3); U32(0x01210010, Stage); U32(0xB75A14, Actor); U32(0xB55EE0, 1);
            U32(Stage + 0xB0, Map); U32(Stage + 0x50, 0x05010000); U32(Stage + 0x954, 0);
            Put<unsigned char>(Stage + 0x110, 0); U32(Map + 0x10, 0x06100000); U32(Map + 0x14, 0x06100000);
            Secure(Map + 0x10C, 0);
            U32(Vtable + 0x330, 0x652A20); U32(Vtable + 0x334, 0x652A30);
            U32(Vtable + 0x340, 0x652A70); U32(Vtable + 0x344, 0x652A80); U32(Vtable + 0x2E0, 0x652670);
            U32(Vtable + 0x80, 0x471610);
            U32(Vtable + 0x318, 0x652A00); U32(Vtable + 0x508, 0x493DC0); U32(Vtable + 0x51C, 0x4A3510);
            PlayerAt(Actor, 1); U32(Actor + 0xE0, 0x04100000);
            U32(0x04100000, 0x04200000); U32(0x04100078, 0); Put<unsigned char>(0x0410007C, 0);
            U32(0x04200000, 0x04300000); U32(0x04300000, 0x4CAC70);
            SecureFloat(0x04200035, 120.5f); SecureFloat(0x0420003D, 240.0f); SecureFloat(0x04200045, 0.0f);
            U32(Actor + 0x1AEC + 20 * 4, 0x04400000); Secure(0x044002E0, 5);
            U32(0xB5DCB8, 0x09000000); U32(0x0900000C, 0x09001000); U32(0x09001000, 0x09002000);
            U32(0x09002000, 0x09001000); U32(0x0900200C, User);
            Put<std::uint16_t>(User + 4, 1); U32(User + 0x110, Actor);
            U32(0xB5DCC8, Party);
            for (std::uint32_t slot = 0; slot < 4; ++slot) U32(Party + 0x10 + slot * 4, slot == 0 ? User : 0);
            Put<unsigned char>(0x050101F8, 1);
            for (std::uint32_t door = 0; door < 4; ++door)
            {
                const auto rect = 0x050101FC + door * 16;
                Put<std::int32_t>(rect, 100); Put<std::int32_t>(rect + 4, 200);
                Put<std::int32_t>(rect + 8, 40); Put<std::int32_t>(rect + 12, 60);
            }
        }
        static bool Read(void* context, std::uint32_t address, void* output, std::size_t size) noexcept
        {
            auto& self = *static_cast<Memory*>(context); ++self.readCount;
            if(self.changeUseBucket&&address==0x09FA0000&&++self.useBucketReads==2)
            {self.U32(0x09FA0000,0x09FB0000);self.U32(0x09FA0004,0x09FB0000);}
            if(self.changeUseCount&&address==0x09FB000C&&++self.useCountReads==2)self.Secure(address,2);
            if(self.changeInventory&&address==0x09F10000&&++self.inventoryReads==2)self.U32(address,0);
            if(self.changeMouseLock&&address==0x09D00068&&++self.mouseLockReads==2)self.Put<std::int32_t>(address,2);
            if(self.changeMouseDeferral&&address==0xCFD208&&++self.mouseDeferralReads==2)self.Put<unsigned char>(address,1);
            if(self.changePeerBindingAtMp && address==User+0x1110 && ++self.peerBindingReads==3)
                self.U32(address,Actor+0x20000);
            if(self.changeCommandLock&&address==0x09D00064&&++self.commandLockReads==2)self.Put<std::int32_t>(address,2);
            if(self.changeSelectorAtEnd&&address==0x01000020&&++self.selectorReads==3)self.U32(address,0x010A0000);
            if(self.changeSelectorIndex&&address==0x01090200&&++self.selectorIndexReads==3)self.U32(address,0);
            if(self.changeSkillAtEnd&&address==Actor+0x1AEC+20*4&&++self.skillSlotReads==2)self.U32(address,0x04410000);
            if (address == 0x00010010) self.readWrappedAddress = true;
            if (address == 0x00010004) self.readNullMirrorAddress = true;
            if (self.changeActorAtEnd && self.readCount > 200 && address == 0xB75A14) self.U32(address, Actor + 0x1000);
            if (self.changeTownAreaAtEnd && address == 0x01040000 && ++self.townAreaReads == 2)
                self.Put<std::int32_t>(address, 3);
            if (self.changeMenuTargetAtEnd && address == 0x010C0008 && ++self.menuTargetReads == 2)
                self.U32(address, Actor + 0x1000);
            if (self.changeMenuAtEnd && address == 0x01050078 && ++self.menuReads == 2) self.U32(address, 0);
            auto* out = static_cast<unsigned char*>(output);
            for (std::size_t i = 0; i < size; ++i)
            {
                const auto found = self.bytes.find(address + static_cast<std::uint32_t>(i));
                if (found == self.bytes.end()) return false;
                out[i] = found->second;
            }
            return true;
        }
        Reader Make()
        {
            return Reader{this, &Read,
                [](void* p) noexcept { return static_cast<Memory*>(p)->layout; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->utc; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->tick; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->counter++; }, 123, 999};
        }
        void MonsterAt(std::uint32_t object, std::uint32_t uid)
        {
            U32(object, 0x06300000); U32(0x06300034, 0x4073D0); U32(0x06300330, 0x652A20);
            U32(object + 0xD0, 529); Secure(object + 0xD4, uid); U32(object + 0x1A0, 1234);
            Secure(object + 0x1168, 100); Secure(object + 0x142C, 0); Secure(object + 0x1434, 7);
            Put<unsigned char>(object + 0x128, 1); U32(object + 0x42C, 9); U32(object + 0x1864, 0x1234);
            U32(object + 0xE0, 0);
        }
    };
}

static void RunTownStateTests()
{
    using namespace dfl::client_state;
    Memory town; town.Town(); History thistory; Snapshot ts;
    Check(Capture(town.Make(), thistory, ts) && ts.valid && ts.scene.value == 1
          && !ts.room.present && ts.haveMe && ts.playerCount == 1 && ts.me.uid.value == 1,
          "Town uses independent user+10C identity and does not reuse Dungeon actor data");
    Check(!ts.me.hp.known && !ts.me.maxHp.known && !ts.me.dead.known && ts.me.position.known
          && ts.me.position.x == 120.5f && ts.me.sameMap.value && !ts.me.detached.value,
          "Town position is captured while unverified combat values remain unknown");
    const auto townJson = ToJson(ts, town.utc);
    Check(townJson.find("\"town\":{\"town_id\":1,\"area_id\":2") != std::string::npos
          && townJson.find("\"destination\":{\"x\":300,\"y\":240}") != std::string::npos
          && townJson.find("\"direction_raw\":{\"x\":1,\"y\":4}") != std::string::npos
          && townJson.find("\"auto_move_active\":true") != std::string::npos,
          "Town region and original destination serialize without interpretation");
    town.Put<unsigned char>(0xB5F0A0, 2); Snapshot unknownAutoMove;
    Capture(town.Make(), thistory, unknownAutoMove);
    Check(ToJson(unknownAutoMove, 0).find("\"auto_move_active\":null") != std::string::npos,
          "out-of-domain native auto-move flag is unknown rather than active");
    town.Put<unsigned char>(0xB5F0A0, 0); Snapshot inactiveAutoMove;
    Capture(town.Make(), thistory, inactiveAutoMove);
    Check(ToJson(inactiveAutoMove, 0).find("\"auto_move_active\":false") != std::string::npos,
          "native auto-move flag zero is explicit inactive");
    Check(townJson.find("\"town_minimap\":{\"visible\":true,\"enabled\":true}") != std::string::npos
          && townJson.find("\"dungeon_selector\":{\"visible\":null,\"active_raw\":1,\"selected\":3,\"hover\":4,") != std::string::npos
          && townJson.find("\"countdown\":{\"state_raw\":2}") != std::string::npos,
          "UI distinguishes raw selector/countdown state from proven visibility");
    wchar_t townFixturePath[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"DFLEGACY_CLIENT_STATE_TOWN_FIXTURE", townFixturePath, MAX_PATH))
    {
        const HANDLE fixture = CreateFileW(townFixturePath, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD fixtureBytes = 0;
        const bool wrote = fixture != INVALID_HANDLE_VALUE
            && WriteFile(fixture, townJson.data(), static_cast<DWORD>(townJson.size()), &fixtureBytes, nullptr)
            && fixtureBytes == townJson.size();
        if (fixture != INVALID_HANDLE_VALUE) CloseHandle(fixture);
        Check(wrote, "unique Town C++ capture serializer fixture is preserved");
    }
    const auto oldTownGeneration = ts.generation;
    town.Put<std::int32_t>(0x01040000, 3); Capture(town.Make(), thistory, ts);
    Check(ts.generation > oldTownGeneration, "Town area change invalidates prior destination generation");
    Memory badTown; badTown.Town(); badTown.U32(Memory::User + 0x10C, Memory::Actor + 0x1000);
    History badTownHistory; Snapshot badTownSnapshot;
    Check(!Capture(badTown.Make(), badTownHistory, badTownSnapshot) && !badTownSnapshot.haveMe,
          "Town rejects stale user binding instead of falling back to Dungeon binding");
    Memory nullTownMap; nullTownMap.Town(); nullTownMap.U32(Memory::Actor + 0xE4, 0);
    History nullTownHistory; Snapshot nullTownSnapshot;
    Check(!Capture(nullTownMap.Make(), nullTownHistory, nullTownSnapshot), "Town map zero is not an actionable context");
    Memory townTransition; townTransition.Town(); townTransition.changeTownAreaAtEnd = true;
    History townTransitionHistory; Snapshot townTransitionSnapshot;
    Check(!Capture(townTransition.Make(), townTransitionHistory, townTransitionSnapshot),
          "Town area changing during capture invalidates the full snapshot");
    Memory badTownLayout; badTownLayout.Town(); badTownLayout.U32(Memory::Actor, Memory::Vtable);
    History badTownLayoutHistory; Snapshot badTownLayoutSnapshot;
    Check(!Capture(badTownLayout.Make(), badTownLayoutHistory, badTownLayoutSnapshot) && !badTownLayoutSnapshot.haveMe,
          "Town cannot interpret a Dungeon actor vtable as virtual Town character");
    Memory unknownUi; unknownUi.Town(); unknownUi.U32(0x01060000, 0xA7CF3D); unknownUi.U32(0x01080000, 0xA7CF2D);
    History unknownUiHistory; Snapshot unknownUiSnapshot; Capture(unknownUi.Make(), unknownUiHistory, unknownUiSnapshot);
    const auto unknownUiJson = ToJson(unknownUiSnapshot, 0);
    Check(unknownUiSnapshot.valid && unknownUiJson.find("\"town_minimap\":{\"visible\":null,\"enabled\":null}") != std::string::npos
          && unknownUiJson.find("\"countdown\":{\"state_raw\":null}") != std::string::npos,
          "unknown UI layouts do not manufacture closed state or erase known Town identity");
    Memory menu; menu.Town();
    // Original 79B5CB is mov ecx,[esi+78], not lea: the menu is a pointer.
    menu.U32(0x01050078, 0x010C0000);
    menu.U32(0x010C0058, 0x010A0000); menu.Put<unsigned char>(0x010A0010, 1); menu.U32(0x010C0008, Memory::Actor);
    menu.U32(0x010C00FC, 0x010B0000); menu.U32(0x010C0100, 0x010B0008);
    menu.U32(0x010B0000, 1); menu.U32(0x010B0004, 6);
    History menuHistory; Snapshot menuSnapshot; Capture(menu.Make(), menuHistory, menuSnapshot);
    Check(menuSnapshot.ui.menuVisible.known && menuSnapshot.ui.menuVisible.value
          && menuSnapshot.ui.menuTargetUid.known && menuSnapshot.ui.menuTargetUid.value == 1
          && menuSnapshot.ui.menuActionsKnown && menuSnapshot.ui.menuActionCount == 2 && menuSnapshot.ui.menuActions[0] == 1,
          "visible native menu exports bound target UID and bounded action IDs");
    menu.changeMenuTargetAtEnd = true; menu.menuTargetReads = 0; Capture(menu.Make(), menuHistory, menuSnapshot);
    Check(!menuSnapshot.ui.menuTargetUid.known && !menuSnapshot.ui.menuActionsKnown,
          "menu target change during capture cannot authorize an old invitation row");
    menu.changeMenuTargetAtEnd = false; menu.U32(0x010C0008, Memory::Actor); menu.changeMenuAtEnd = true;
    Capture(menu.Make(), menuHistory, menuSnapshot);
    Check(!menuSnapshot.ui.menuVisible.known && !menuSnapshot.ui.menuTargetUid.known && !menuSnapshot.ui.menuActionsKnown,
          "menu controller pointer changing during capture invalidates all menu fields");
    Memory townParty; townParty.Town();
    for (std::uint32_t slot = 1; slot < 4; ++slot)
    {
        const auto memberActor = Memory::Actor + slot * 0x10000, memberUser = Memory::User + slot * 0x1000;
        townParty.PlayerAt(memberActor, slot + 1); townParty.U32(memberActor, 0x9988DC);
        townParty.Put<std::uint16_t>(memberUser + 4, static_cast<std::uint16_t>(slot + 1));
        townParty.U32(memberUser + 0x10C, memberActor); townParty.U32(Memory::Party + 0x10 + slot * 4, memberUser);
    }
    townParty.U32(Memory::Actor + 0x30000 + 0xE4, 0);
    History townPartyHistory; Snapshot townPartySnapshot; Capture(townParty.Make(), townPartyHistory, townPartySnapshot);
    Check(townPartySnapshot.valid && townPartySnapshot.playerCount == 4 && !townPartySnapshot.truncated
          && townPartySnapshot.players[3].detached.value && !townPartySnapshot.players[3].position.known
          && !townPartySnapshot.players[3].dead.known,
          "Town four-member roster retains detached peer identity without inventing combat death");
}

static int RunDungeonAndExportTests()
{
    using namespace dfl::client_state;
    Memory memory; History history; auto owned_first=std::make_unique<Snapshot>();auto& first=*owned_first;
    auto reader = memory.Make();
    Check(Capture(reader, history, first) && first.valid && first.scene.known && first.scene.value == 3,
          "supported independent native scene is captured");
    Check(first.haveMe && first.me.uid.known && first.me.uid.value == 1 && first.me.hp.known && first.me.hp.value == 470,
          "same native user actor gives UID and HP");
    Check(first.me.maxHp.known && first.me.maxHp.value == 470 && first.me.position.known && first.me.position.x == 120.5f,
          "guarded max HP and protected coordinate getter replica");
    Check(first.playerCount == 1 && first.players[0].uid.value == 1 && first.room.doors[1].enabled.value,
          "players include self and right door uses native bit one");
    Check(first.skillCount == 1 && first.skills[0].id == 20 && first.skills[0].level == 5,
          "learned skill levels are captured without game getter calls");
    const auto json = ToJson(first, memory.utc + 500);
    Check(json.find("\"schema\":\"dfl-client-state/v2\"") != std::string::npos
          && json.find("\"cooldown_ms\":null,\"cooldown_blocked\":null,\"usable\":null") != std::string::npos
          && json.find("\"server_context\":null") != std::string::npos,
          "JSON schema and unsupported cooldown/context remain explicit");
    const auto captured = first.capturedTickMs;
    const auto newerExport = ToJson(first, memory.utc + 999999);
    Check(first.capturedTickMs == captured && newerExport.find("\"captured_tick_ms\":1000") != std::string::npos,
          "serialization cannot refresh capture time");
    Check(json.find("\"mp\":123,\"max_mp\":1250") != std::string::npos,
          "verified local MP fields are serialized through the actual capture");
    Check(first.room.mapClearState.known && first.room.mapClearState.value == 0
          && json.find("\"map_clear_state\":0") != std::string::npos,
          "native zero map-clear value remains raw zero rather than door-open inference");
    Memory cleared; cleared.Secure(Memory::Map + 0x10C, 1); History clearHistory; auto owned_clearSnapshot=std::make_unique<Snapshot>();auto& clearSnapshot=*owned_clearSnapshot;
    Capture(cleared.Make(), clearHistory, clearSnapshot);
    Check(clearSnapshot.room.mapClearState.known && clearSnapshot.room.mapClearState.value == 1,
          "native one map-clear value remains raw one");
    Memory badClear; const auto clearIndex = badClear.index; badClear.Secure(Memory::Map + 0x10C, 1);
    badClear.U32(0x02100000 + clearIndex * 4, 0); History badClearHistory; auto owned_badClearSnapshot=std::make_unique<Snapshot>();auto& badClearSnapshot=*owned_badClearSnapshot;
    Capture(badClear.Make(), badClearHistory, badClearSnapshot);
    Check(!badClearSnapshot.room.mapClearState.known
          && ToJson(badClearSnapshot, 0).find("\"map_clear_state\":null") != std::string::npos,
          "damaged map-clear mirror is explicit unknown");

    memory.Secure(Memory::Actor + 0x1168, 0); memory.Secure(Memory::Actor + 0x142C, 1);
    memory.Put<unsigned char>(Memory::Actor + 0x20A2, 1); memory.U32(Memory::Actor + 0xE4, 0);
    auto owned_dead=std::make_unique<Snapshot>();auto& dead=*owned_dead; Check(Capture(reader, history, dead) && dead.valid && dead.scene.value == 3,
          "dead map zero does not erase independent native Dungeon scene");
    Check(dead.me.dead.known && dead.me.dead.value && dead.me.detached.known && dead.me.detached.value
          && !dead.me.position.known, "dead detached retains identity and marks position unknown");

    Memory unsupported; unsupported.layout = false; History uhistory; auto owned_bad=std::make_unique<Snapshot>();auto& bad=*owned_bad;
    Check(!Capture(unsupported.Make(), uhistory, bad) && !bad.layoutSupported && !bad.valid
          && unsupported.readCount == 0, "unsupported PE never probes hard-coded addresses");
    Memory wrapped; wrapped.U32(0xB5DCE0, 0xFFE10000); wrapped.U32(0x00010010, Memory::Stage);
    History wrappedHistory; auto owned_wrappedSnapshot=std::make_unique<Snapshot>();auto& wrappedSnapshot=*owned_wrappedSnapshot;
    Check(!Capture(wrapped.Make(), wrappedHistory, wrappedSnapshot) && !wrappedSnapshot.valid && !wrapped.readWrappedAddress,
          "corrupt UI base plus stage offset fails before any wrapped-address read");
    Memory nullMirror;
    nullMirror.U32(Memory::Manager + 0x44, 0); nullMirror.U32(Memory::Manager + 0x48, 0);
    nullMirror.U32(Memory::Manager + 4, 0x8000);
    nullMirror.U32(0x01000010, (0x01000010 + 0x4001) ^ Memory::Key);
    nullMirror.U32(0x01000014, 0x4001); nullMirror.U32(0x00010004, 3 ^ 0x00010004 ^ Memory::Key);
    History nullMirrorHistory; auto owned_nullMirrorSnapshot=std::make_unique<Snapshot>();auto& nullMirrorSnapshot=*owned_nullMirrorSnapshot;
    Capture(nullMirror.Make(), nullMirrorHistory, nullMirrorSnapshot);
    Check(!nullMirror.readNullMirrorAddress && !nullMirrorSnapshot.valid,
          "null secure mirror base is rejected before adding a large valid index");
    Memory provider; provider.U32(0x04100078, 0x04110000); provider.Put<unsigned char>(0x0410007C, 1);
    History phistory; auto owned_ps=std::make_unique<Snapshot>();auto& ps=*owned_ps; Capture(provider.Make(), phistory, ps);
    Check(!ps.me.position.known, "unknown active coordinate provider is not guessed");
    Memory plain; plain.U32(0x04300000, 0x4736A0); plain.Put<float>(0x04200004, 22.0f);
    plain.Put<float>(0x04200008, 33.0f); plain.Put<float>(0x0420000C, 44.0f);
    History plainHistory; auto owned_plainSnapshot=std::make_unique<Snapshot>();auto& plainSnapshot=*owned_plainSnapshot; Capture(plain.Make(), plainHistory, plainSnapshot);
    Check(plainSnapshot.me.position.known && plainSnapshot.me.position.z == 44.0f, "plain position getter replica");
    Memory unknownLayout; unknownLayout.U32(Memory::Vtable + 0x80, 0x471611);
    History unknownLayoutHistory; auto owned_unknownPosition=std::make_unique<Snapshot>();auto& unknownPosition=*owned_unknownPosition; Capture(unknownLayout.Make(), unknownLayoutHistory, unknownPosition);
    Check(!unknownPosition.me.position.known, "unverified player position vtable never reads a guessed controller layout");
    Memory moved; History movedHistory; auto owned_beforeMove=std::make_unique<Snapshot>();auto& beforeMove=*owned_beforeMove; Capture(moved.Make(), movedHistory, beforeMove);
    moved.U32(Memory::Stage + 0xB0, Memory::Map + 0x1000); auto owned_afterMove=std::make_unique<Snapshot>();auto& afterMove=*owned_afterMove;
    Capture(moved.Make(), movedHistory, afterMove);
    Check(afterMove.generation > beforeMove.generation, "map identity changes advance scene generation");
    Memory corrupt; const auto nextIndex = corrupt.index;
    corrupt.Secure(Memory::Actor + 0x1168, 123); corrupt.U32(0x02100000 + nextIndex * 4, 0);
    History corruptHistory; auto owned_corrupted=std::make_unique<Snapshot>();auto& corrupted=*owned_corrupted; Capture(corrupt.Make(), corruptHistory, corrupted);
    Check(!corrupted.me.hp.known && corrupted.errorCount != 0, "broken secure mirrors do not fabricate HP");
    Memory nan; nan.SecureFloat(0x04200035, std::numeric_limits<float>::quiet_NaN());
    History nanHistory; auto owned_nanSnapshot=std::make_unique<Snapshot>();auto& nanSnapshot=*owned_nanSnapshot; Capture(nan.Make(), nanHistory, nanSnapshot);
    Check(!nanSnapshot.me.position.known && ToJson(nanSnapshot, 0).find("nan") == std::string::npos,
          "NaN coordinate is unknown and never invalid JSON");
    Memory changing; changing.changeActorAtEnd = true; History changingHistory; auto owned_changed=std::make_unique<Snapshot>();auto& changed=*owned_changed;
    Check(!Capture(changing.Make(), changingHistory, changed) && !changed.valid,
          "actor identity transition invalidates the whole capture");
    Memory duplicate; duplicate.U32(Memory::Party + 0x14, Memory::User);
    History duplicateHistory; auto owned_ds=std::make_unique<Snapshot>();auto& ds=*owned_ds; Capture(duplicate.Make(), duplicateHistory, ds);
    Check(ds.truncated && ds.playerCount == 1, "duplicate player UID makes complete roster unavailable");
    Memory party;
    for (std::uint32_t slot = 1; slot < 4; ++slot)
    {
        const auto memberActor = Memory::Actor + slot * 0x10000;
        const auto memberUser = Memory::User + slot * 0x1000;
        party.PlayerAt(memberActor, slot + 1); party.Put<std::uint16_t>(memberUser + 4, static_cast<std::uint16_t>(slot + 1));
        party.U32(memberUser + 0x110, memberActor); party.U32(Memory::Party + 0x10 + slot * 4, memberUser);
    }
    History partyHistory; auto owned_fullParty=std::make_unique<Snapshot>();auto& fullParty=*owned_fullParty; Capture(party.Make(), partyHistory, fullParty);
    Check(fullParty.playerCount == 4 && !fullParty.truncated && !fullParty.room.livingCount.known,
          "four players are bounded and unknown member positions prevent assembly inference");
    Memory allSkills;
    for (std::uint32_t id = 0; id < MaximumSkills; ++id)
    {
        const auto skill = 0x04400000 + id * 0x1000;
        allSkills.U32(Memory::Actor + 0x1AEC + id * 4, skill); allSkills.Secure(skill + 0x2E0, 1);
    }
    History skillsHistory; auto owned_fullSkills=std::make_unique<Snapshot>();auto& fullSkills=*owned_fullSkills; Capture(allSkills.Make(), skillsHistory, fullSkills);
    Check(fullSkills.skillCount == MaximumSkills && fullSkills.skills.back().id == 215 && !fullSkills.truncated,
          "all 216 verified skill slots remain bounded and complete");
    Memory enemies; enemies.MonsterAt(0x06200000, 0); enemies.MonsterAt(0x06210000, 1);
    enemies.MonsterAt(0x06220000, 2); enemies.MonsterAt(0x06230000, 3);
    enemies.U32(0x06100000, 0x06200000); enemies.U32(0x06100004, 0x06210000);
    enemies.U32(0x06100008, 0x06220000); enemies.U32(0x0610000C, 0x06230000);
    enemies.U32(Memory::Map + 0x14, 0x06100010);
    History enemyHistory; auto owned_es=std::make_unique<Snapshot>();auto& es=*owned_es; Capture(enemies.Make(), enemyHistory, es);
    Check(es.enemyCount == 4 && es.enemies[0].uid.known && es.enemies[0].uid.value == 0
          && es.enemies[1].uid.value == 1 && es.enemies[2].uid.value == 2 && es.enemies[3].uid.value == 3
          && es.enemies[1].monsterId.value == 1234
          && es.enemies[0].authorityToken.value == 0x1234 && !es.truncated,
          "native enemy UID zero is included in complete four-monster enumeration");
    const auto enemyJson = ToJson(es, enemies.utc + 100);
    Check(es.errorCount == 0 && enemyJson.find("\"enemies\":[{\"uid\":0,") != std::string::npos
          && enemyJson.find("\"truncated\":false") != std::string::npos,
          "enemy UID zero is serialized as known zero without invalid UID or truncation");
    wchar_t fixturePath[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"DFLEGACY_CLIENT_STATE_FIXTURE", fixturePath, MAX_PATH))
    {
        // Test-only cross-language fixture, always from the actual fake-reader capture.
        const auto fixtureJson = ToJson(es, enemies.utc + 100);
        const HANDLE fixture = CreateFileW(fixturePath, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD fixtureBytes = 0;
        const bool wrote = fixture != INVALID_HANDLE_VALUE
            && WriteFile(fixture, fixtureJson.data(), static_cast<DWORD>(fixtureJson.size()), &fixtureBytes, nullptr)
            && fixtureBytes == fixtureJson.size();
        if (fixture != INVALID_HANDLE_VALUE) CloseHandle(fixture);
        Check(wrote, "unique C++ capture serializer fixture is created without overwriting evidence");
    }
    enemies.U32(0x06100004, 0x06200000); enemies.U32(Memory::Map + 0x14, 0x06100008);
    auto owned_repeatedEnemy=std::make_unique<Snapshot>();auto& repeatedEnemy=*owned_repeatedEnemy; Capture(enemies.Make(), enemyHistory, repeatedEnemy);
    Check(repeatedEnemy.truncated && repeatedEnemy.enemyCount == 1 && repeatedEnemy.enemies[0].uid.value == 0,
          "duplicate zero enemy UID still prevents complete clear");
    Memory highEnemy; highEnemy.MonsterAt(0x06200000, 65535); highEnemy.U32(0x06100000, 0x06200000);
    highEnemy.U32(Memory::Map + 0x14, 0x06100004); History highEnemyHistory; auto owned_highEnemySnapshot=std::make_unique<Snapshot>();auto& highEnemySnapshot=*owned_highEnemySnapshot;
    Capture(highEnemy.Make(), highEnemyHistory, highEnemySnapshot);
    Check(highEnemySnapshot.enemyCount == 1 && highEnemySnapshot.enemies[0].uid.value == 65535 && !highEnemySnapshot.truncated,
          "enemy UID upper u16 boundary remains accepted");
    highEnemy.Secure(0x062000D4, 65536); Capture(highEnemy.Make(), highEnemyHistory, highEnemySnapshot);
    Check(highEnemySnapshot.truncated && highEnemySnapshot.enemyCount == 0,
          "out-of-u16 enemy UID still prevents complete enumeration");
    Memory corruptEnemy; corruptEnemy.MonsterAt(0x06200000, 0); corruptEnemy.U32(0x06100000, 0x06200000);
    corruptEnemy.U32(Memory::Map + 0x14, 0x06100004); const auto uidIndex = corruptEnemy.index;
    corruptEnemy.Secure(0x062000D4, 0);
    corruptEnemy.U32(0x02100000 + uidIndex * 4, 0); History corruptEnemyHistory; auto owned_corruptEnemySnapshot=std::make_unique<Snapshot>();auto& corruptEnemySnapshot=*owned_corruptEnemySnapshot;
    Capture(corruptEnemy.Make(), corruptEnemyHistory, corruptEnemySnapshot);
    Check(corruptEnemySnapshot.truncated && corruptEnemySnapshot.enemyCount == 0,
          "broken zero enemy UID secure mirror still prevents complete enumeration");
    Memory zeroPlayer; zeroPlayer.Put<std::uint16_t>(Memory::User + 4, 0);
    zeroPlayer.Secure(Memory::Actor + 0xD4, 0); History zeroPlayerHistory; auto owned_zeroPlayerSnapshot=std::make_unique<Snapshot>();auto& zeroPlayerSnapshot=*owned_zeroPlayerSnapshot;
    Capture(zeroPlayer.Make(), zeroPlayerHistory, zeroPlayerSnapshot);
    Check(!zeroPlayerSnapshot.valid && !zeroPlayerSnapshot.haveMe,
          "player UID zero remains invalid independently of enemy UID zero");
    enemies.U32(Memory::Map + 0x14, 0x06100000 + 257 * 4);
    auto owned_limited=std::make_unique<Snapshot>();auto& limited=*owned_limited; Capture(enemies.Make(), enemyHistory, limited);
    Check(limited.truncated && limited.enemyCount <= MaximumEnemies, "map object traversal has a visible hard cap");

    Publication publication; Check(TryPublish(publication, first), "bounded publication succeeds");
    AcquireSRWLockExclusive(&publication.lock);
    Check(!TryPublish(publication, dead), "TryLock contention skips publication without blocking frame");
    ReleaseSRWLockExclusive(&publication.lock);
    auto owned_copied=std::make_unique<Snapshot>();auto& copied=*owned_copied; Check(TryCopy(publication, copied) && copied.sequence == first.sequence,
          "contention does not replace prior capture with stale or partial data");

    wchar_t temporary[MAX_PATH]{}, directory[MAX_PATH]{}, settings[MAX_PATH]{}, stateFile[MAX_PATH]{};
    const auto temporaryLength = GetTempPathW(MAX_PATH, temporary);
    swprintf_s(directory, L"%sdfstate-tests-%lu", temporary, GetCurrentProcessId());
    const bool directoryCreated = CreateDirectoryW(directory, nullptr) != 0;
    const auto directoryError = directoryCreated ? ERROR_SUCCESS : GetLastError();
    Check(temporaryLength > 0 && temporaryLength < MAX_PATH
          && (directoryCreated || directoryError == ERROR_ALREADY_EXISTS), "state export test has a writable private temporary directory");
    swprintf_s(settings, L"%s\\settings.ini", directory);
    swprintf_s(stateFile, L"%s\\dfdebug-client-%lu.json", directory, GetCurrentProcessId());
    DeleteFileW(stateFile);
    WritePrivateProfileStringW(L"DebugOverlay", L"ExportState", L"0", settings);
    Initialize(settings, directory); CaptureFrame(); ExportLatest(); Shutdown();
    Check(GetFileAttributesW(stateFile) == INVALID_FILE_ATTRIBUTES, "new state export defaults off");
    WritePrivateProfileStringW(L"DebugOverlay", L"ExportState", L"1", settings);
    Initialize(settings, directory); CaptureFrame(); ExportLatest();
    auto contents = [&]()
    {
        std::ifstream input(stateFile, std::ios::binary);
        return std::string((std::istreambuf_iterator<char>(input)), {});
    };
    const auto originalFile = contents();
    if (originalFile.empty())
    {
        auto owned_diagnostic=std::make_unique<Snapshot>();auto& diagnostic=*owned_diagnostic;
        const auto have = CopyLatest(diagnostic);
        std::fprintf(stderr, "state export diagnostic: configured=%u have=%u sequence=%llu started=%llu error=%lu\n",
            GetPrivateProfileIntW(L"DebugOverlay", L"ExportState", 0, settings), have ? 1U : 0U,
            static_cast<unsigned long long>(diagnostic.sequence),
            static_cast<unsigned long long>(NativeReader().processStartedFileTime), GetLastError());
        std::fwprintf(stderr, L"state export paths: temp_length=%lu created=%u dir_error=%lu temp=%ls dir=%ls settings=%ls\n",
            temporaryLength, directoryCreated ? 1U : 0U, directoryError, temporary, directory, settings);
    }
    Check(originalFile.find("\"layout_supported\":false") != std::string::npos
          && originalFile.find("\"valid\":false") != std::string::npos,
          "wrong EXE exports unsupported without touching client addresses");
    Sleep(225); ExportLatest();
    Check(contents() == originalFile, "stopped rendering plus worker IO cannot refresh an old capture");
    CaptureFrame(); ExportLatest(); const auto secondFile = contents();
    Check(secondFile != originalFile && secondFile.find("\"sequence\":2") != std::string::npos,
          "new render capture advances sequence and capture time");
    CaptureFrame(); ExportLatest();
    Check(contents() == secondFile, "samples within 200ms do not advance sequence");
    Shutdown(); Sleep(10); CaptureFrame(); ExportLatest();
    Check(contents() == secondFile, "shutdown prevents both capture and export");
    Initialize(settings, directory); CaptureFrame(); ExportLatest();
    Check(contents().find("\"sequence\":3") != std::string::npos,
          "same-process reinitialization never regresses the sample sequence");
    Shutdown();
    DeleteFileW(stateFile); DeleteFileW(settings); RemoveDirectoryW(directory);
    std::printf("Client state tests: %d failures\n", Failures);
    return Failures ? 1 : 0;
}

int RunClientStateTests()
{
    {
        Memory m;m.Selector();History h;Snapshot s;Capture(m.Make(),h,s);
        Check(s.valid&&s.scene.value==2&&!s.haveMe&&!s.room.present&&!s.town.present&&s.playerCount==0,"selector observation never exposes stale town or combat actors");
        auto text=ToJson(s,0);Check(text.find("\"selected_dungeon_id\":31,\"difficulty_raw\":2")!=std::string::npos,"selector record index maps to actual dungeon ID and difficulty");
        m.Put<unsigned char>(0x01090100,1);m.U32(0x010901A0,3);m.U32(0x01090068,Memory::Stage);Capture(m.Make(),h,s);text=ToJson(s,0);
        Check(s.valid&&!s.room.present&&text.find("\"transition_pending\":true")!=std::string::npos&&text.find("\"phase_raw\":3")!=std::string::npos,"scene2 can retain loading-stage metadata without claiming dungeon capture");
        m.Put<std::int32_t>(0x01090200,-1);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"selected_dungeon_id\":null")!=std::string::npos,"unselected state does not silently choose the first dungeon");
        m.Put<std::int32_t>(0x01090200,5);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"selected_dungeon_id\":null")!=std::string::npos,"out-of-range selection cannot project another row");
        m.changeSelectorAtEnd=true;m.selectorReads=0;Capture(m.Make(),h,s);Check(!s.valid,"selector replacement during capture invalidates scene2 identity");
    }
    {
        Memory m;m.Dialog();History h;Snapshot s;Capture(m.Make(),h,s);auto text=ToJson(s,0);
        Check(text.find("\"dialogs\":{\"valid\":true,\"truncated\":false,\"items\":[{\"class_id\":89")!=std::string::npos,"recognized registered native dialog is observed through active bank only");
        m.Put<unsigned char>(0x09B00010,0);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"shown_raw\":false")!=std::string::npos,"native hidden flag is not mistaken for rendered visibility");
        m.U32(0x01050EF8,0x09D00000);m.U32(0x01050EFC,0x09D00004);m.U32(0x01050F00,0x09D00004);m.U32(0x09D00000,0x09A00000);Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid&&s.ui.dialogs.count==0,"queued-close objects are excluded before active-bank removal");
        m.U32(0x01050EF8,0);m.U32(0x01050EFC,0);m.U32(0x01050F00,0);m.U32(0x09B00044,0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"dialogs\":{\"valid\":false")!=std::string::npos,"broken root ownership means unknown dialogs rather than known empty");
    }
    {
        Memory m;m.Dialog();History h;Snapshot s;
        m.U32(0x09A00004,0x30);Capture(m.Make(),h,s);
        Check(!s.ui.dialogs.valid&&s.ui.dialogs.count==0,"dialog class must match registered bucket");
        m.U32(0x09A00004,0x59);m.U32(0x09A00000,0xA7E044);Capture(m.Make(),h,s);
        Check(!s.ui.dialogs.valid,"unrecognized dialog derivative remains unknown");
        m.U32(0x09A00000,0xA7EA5C);m.Put<unsigned char>(0x09B00011,0);Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid&&s.ui.dialogs.count==1&&!s.ui.dialogs.items[0].inputEnabledRaw,"disabled root is explicit raw false");
        const auto header=0x01050000+0xD8+0x59*16;
        m.U32(header+4,0x09A10008);m.U32(header+8,0x09A10008);m.U32(0x09A10004,0x09A00000);Capture(m.Make(),h,s);
        Check(!s.ui.dialogs.valid&&s.ui.dialogs.count==0,"duplicate registration does not publish a partial inventory");
        m.U32(header+4,0x09A10104);m.U32(header+8,0x09A10104);Capture(m.Make(),h,s);
        Check(!s.ui.dialogs.valid&&s.ui.dialogs.truncated,"oversize active bank is explicitly incomplete");
        m.U32(header,0);m.U32(header+4,0);m.U32(header+8,0);
        const auto pool=0x01050000+0x7E8+0x59*16;
        m.U32(pool,0x09A10000);m.U32(pool+4,0x09A10004);m.U32(pool+8,0x09A10004);Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid&&s.ui.dialogs.count==0,"pooled windows are never registered active dialogs");
        m.U32(header,0x09A10000);m.U32(header+4,0x09A10002);m.U32(header+8,0x09A10004);Capture(m.Make(),h,s);
        Check(!s.ui.dialogs.valid,"unaligned vector length is unknown");
    }
    {
        constexpr std::uint32_t classes[]={7,9,0x23,0x25,0x30,0x56,0x59,0x6A};
        constexpr std::uint32_t tables[]={0xA7E614,0xA7E6B4,0xA7E254,0xA7E2F4,0xA7E434,0xA7F154,0xA7EA5C,0xA7F4C4};
        for(std::size_t i=0;i<8;++i)
        {
            Memory m;m.Dialog();History h;Snapshot s;
            const auto old=0x01050000+0xD8+0x59*16;
            m.U32(old,0);m.U32(old+4,0);m.U32(old+8,0);
            const auto header=0x01050000+0xD8+classes[i]*16;
            m.U32(header,0x09A10000);m.U32(header+4,0x09A10004);m.U32(header+8,0x09A10004);
            m.U32(0x09A00000,tables[i]);m.U32(0x09A00004,classes[i]);
            m.U32(tables[i]+0x2C,0x7BF640);m.U32(tables[i]+0x30,0x7BF650);
            Capture(m.Make(),h,s);
            Check(s.ui.dialogs.valid&&s.ui.dialogs.count==1&&s.ui.dialogs.items[0].classId==classes[i],"each exact allowlisted native window class is observed");
        }
    }
    {
        Memory m;m.Selector();History h;Snapshot s;
        m.changeSelectorIndex=true;Capture(m.Make(),h,s);
        Check(!s.valid,"selector projection and legacy index cannot describe different selections");
    }
    {
        Memory m;m.Selector();History h;Snapshot s;
        m.U32(0x010901F8,0x09C00021);Capture(m.Make(),h,s);
        Check(!s.valid&&!s.ui.selector.dungeonId.known,"malformed selector vector cannot expose a projection");
        m.U32(0x010901F8,0x09C00020);m.U32(0x010901FC,0x09C00010);Capture(m.Make(),h,s);
        Check(!s.valid,"selector capacity before end fails closed");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.SkillLimit(1);History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_limit\":2,\"use_count\":1")!=std::string::npos
            &&ToJson(s,0).find("\"usable\":true")!=std::string::npos,"nonzero native use limit reads actual count and permits remaining use");
        m.Secure(0x09FB000C,2);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":2")!=std::string::npos&&ToJson(s,0).find("\"usable\":false")!=std::string::npos,
            "signed native count equal to limit proves exhausted");
        m.U32(0x09FB0008,21);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":0")!=std::string::npos&&ToJson(s,0).find("\"usable\":true")!=std::string::npos,
            "proven missing native counter defaults to zero");
        m.U32(0x09FB0008,1);m.U32(0x09FB0000,0x09FB0000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_limit\":2,\"use_count\":null")!=std::string::npos
            &&ToJson(s,0).find("\"usable\":null")!=std::string::npos,"corrupt count lookup stays unknown rather than absent");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.SkillLimit(1);History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;
        for(auto endpoint:{0U,0x09FC0000U})
        {
            m.U32(0x09FA0000,endpoint);m.U32(0x09FA0004,endpoint);Capture(m.Make(),h,s);
            Check(ToJson(s,0).find("\"use_count\":null")!=std::string::npos&&ToJson(s,0).find("\"usable\":null")!=std::string::npos,
                "equal null or unreadable bucket endpoints are not a proven absent counter");
        }
        m.U32(0x09FA0000,0x09F90000);m.U32(0x09FA0004,0x09F90000);m.changeUseBucket=true;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":null")!=std::string::npos&&ToJson(s,0).find("\"usable\":null")!=std::string::npos,
            "changing empty-bucket endpoints cannot pass a two-read consistency check");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.SkillLimit(1);m.changeUseCount=true;History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":null")!=std::string::npos&&ToJson(s,0).find("\"usable\":null")!=std::string::npos,
            "use count changing between reads cannot grant readiness");
        m.changeUseCount=false;m.U32(Memory::Actor+0x1E58,65);m.readCount=0;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":null")!=std::string::npos&&m.readCount<10000,"count traversal cap fails closed within bounded reads");
        m.U32(Memory::Actor+0x1E58,1);m.Secure(0x09FB000C,0xFFFFFFFF);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"use_count\":null")!=std::string::npos,"negative decoded use counter is unsupported rather than exhausted");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.SkillItems(2);History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"resources\":{\"scope\":\"native_manual_precast_resources\",\"mp_cost\":6,\"use_limit\":0,\"use_count\":null,\"items_sufficient\":true}")!=std::string::npos
            &&ToJson(s,0).find("\"usable\":true")!=std::string::npos,"native resource scope publishes exact MP cost and sufficient first item slot");
        m.Secure(0x09F200A0,1);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":false")!=std::string::npos&&ToJson(s,0).find("\"usable\":false")!=std::string::npos,
            "first matching native item stack below requirement blocks readiness");
        m.U32(0x09F10004,0x09F21000);m.U32(0x09F21000,0x09F30000);m.U32(0x09F2100C,1001);m.Secure(0x09F210A0,100);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":false")!=std::string::npos,"second same-ID stack is not summed or preferred over native first match");
        m.U32(0x09F2000C,2002);m.U32(0x09F2100C,2002);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":false")!=std::string::npos,"complete native inventory can prove item absent");
        m.Secure(0x09F40008,0);m.Secure(0x09F40010,0);m.U32(0xB5DCEC,0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":true")!=std::string::npos,"zero interpolated item cost bypasses inventory lookup");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.SkillItems(2);History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;
        m.changeInventory=true;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos&&ToJson(s,0).find("\"usable\":null")!=std::string::npos,
            "changing first inventory slot cannot prove sufficiency or shortage");
        m.changeInventory=false;m.U32(0x09F10000,0x09F20000);m.U32(Memory::Actor+0x242C,0x09F00004);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos,"unbound global and actor inventory remains unknown");
        m.U32(Memory::Actor+0x242C,0x09F00000);m.Secure(0x09F00010,513);m.readCount=0;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos&&m.readCount<10000,"inventory cap prevents unbounded per-skill scans");
        m.Secure(0x09F00010,2);m.Secure(0x04400020,1);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos,"native zero interpolation denominator is not evaluated or guessed");
        m.Secure(0x04400020,10);m.U32(0x044001C0,0x09F40019);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos,"malformed item vector remains unknown");
        m.U32(0x044001C0,0x09F40018);m.Secure(0x044002E0,2);m.Secure(0x04400020,3);
        m.Secure(0x09F40008,4);m.Secure(0x09F40010,1);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":false")!=std::string::npos,"descending item interpolation truncates toward zero rather than flooring");
        m.Secure(0x044002E0,5);m.Secure(0x04400020,10);m.Secure(0x09F40008,0);m.Secure(0x09F40010,INT32_MAX);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos,"unsupported native int32 product overflow stays unknown without division");
        m.U32(0x044001C0,0x09F40000+33*0x18);m.readCount=0;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"items_sufficient\":null")!=std::string::npos&&m.readCount<10000,"item-row cap prevents unbounded scans");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();m.Secure(0x044002EC,0);History h;auto owned=std::make_unique<Snapshot>();auto& s=*owned;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":false,\"resources\":{\"scope\":\"native_manual_precast_resources\",\"mp_cost\":6")!=std::string::npos,
            "context blocker preserves independently available resource observations");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();History h;Snapshot s;
        m.U32(Memory::Vtable+0x54C,0x498D90);m.U32(Memory::Vtable+0x4DC,0x494410);
        m.U32(Memory::Actor+0x13E8,0x09500000);m.U32(0x09500000,0x09510000);
        m.U32(0x095100DC,0x4A7B50);m.U32(0x095100E0,0x4A7B60);m.U32(0x09510118,0x4A7BE0);
        m.Secure(0x095000D0,0);m.U32(0x09500390,0);m.U32(0x09500394,0);m.U32(0x095006A0,0);
        m.U32(Memory::Actor+0x2040,0x09600000);m.U32(Memory::Actor+0x2044,0x09600058);
        m.SecureFloat(0x09600038,0.9f);m.Secure(0x04400208,1);m.Secure(0x09200000,7);m.Secure(Memory::Actor+0x1170,6);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":true")!=std::string::npos,"weapon MP factor and truncated cost permit exact available MP");
        m.Secure(Memory::Actor+0x1170,5);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"usable\":false")!=std::string::npos,"weapon-factor cost below needed MP is unavailable");
        m.Secure(Memory::Actor+0x1170,6);m.U32(0x095006A0,0x09800000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"uninterpreted attached equipment modifiers cannot silently grant readiness");
        m.U32(0x095006A0,0);m.SecureFloat(0x09600038,0.6f);m.Secure(0x09200000,5);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"unmeasured precision near integer MP boundary stays unknown");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();History h;Snapshot s;
        m.U32(Memory::Actor+0x1E80,1);m.U32(0x09110000,0x09800000);m.U32(0x09110004,0x09800000);
        m.U32(Memory::Actor+0x1E88,0x09700000);m.U32(Memory::Actor+0x1E8C,0x09700008);m.U32(Memory::Actor+0x1E94,0);m.U32(Memory::Actor+0x1E98,1);
        m.U32(0x09700000,0x09800000);m.U32(0x09700004,0x09110000);m.U32(0x09800000,0x09110000);m.U32(0x09800008,20);m.SecureFloat(0x0980000C,0.5f);
        m.Secure(Memory::Actor+0x1170,3);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"usable\":true")!=std::string::npos,"bounded per-skill factor lookup uses actual factor rather than default one");
        m.U32(0x09800008,1);m.U32(0x09800000,0x09800000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"cyclic per-skill factor map fails closed within its bound");
        m.U32(0x09700000,0x09110000);m.U32(0x09700004,0x09800000);m.Secure(Memory::Actor+0x1170,123);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"unexpected sentinel before bucket end is not a missing-factor result");
        m.changeSkillAtEnd=true;m.skillSlotReads=0;Capture(m.Make(),h,s);Check(s.truncated,"skill replacement while sampling cannot publish a mixed complete snapshot");
    }
    {
        Memory m;m.SkillTimer(9000);m.SkillResources();History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":true")!=std::string::npos,"native context ready plus verified resources and cooldown gives known usable");
        m.Secure(0x04400008,21);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":null,\"cooldown_blocked\":null,\"usable\":null")!=std::string::npos,"same-owner skill ID mismatch cannot supply another slot's cooldown");m.Secure(0x04400008,20);
        m.Secure(Memory::Actor+0x1170,5);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"usable\":false")!=std::string::npos,"MP shortage blocks a cooldown-ready skill");
        m.Secure(Memory::Actor+0x1170,123);m.Secure(0x044002EC,0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":false")!=std::string::npos,"original context bit blocks despite enough MP and zero cooldown");
        m.Secure(0x044002EC,1);m.Secure(0x04400354,10000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":false")!=std::string::npos,"positive native cooldown blocks actual precast readiness");
        m.Secure(0x04400354,8000);m.U32(0x044001BC,0x09300000);m.U32(0x044001C0,0x09300018);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"unsupported item-cost gate remains unknown rather than assumed sufficient");
        m.U32(0x044001BC,0);m.U32(0x044001C0,0);m.U32(0x044001CC,0x09400000);m.U32(0x044001D0,0x09400008);m.Secure(0x09400000,1);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"unproven nonzero use limit cannot grant known usability");
        m.Secure(0x09400000,0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"usable\":null")!=std::string::npos,"use-limit table does not clamp an out-of-range learned level to a final zero");
        m.U32(0x044001CC,0);m.U32(0x044001D0,0);m.Secure(0x0440034C,0xFFFFFFFE);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":null,\"cooldown_blocked\":null,\"usable\":null")!=std::string::npos,"unsupported negative duration cannot silently become known ready");
    }
    {
        Memory m;m.SkillTimer(5000);History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":3000")!=std::string::npos,"skill cooldown uses actual native game clock rather than SDK wall tick");
        m.U32(0xCFD27C,7999);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"cooldown_ms\":1")!=std::string::npos,"native cooldown one millisecond before deadline remains blocked");
        m.U32(0xCFD27C,8000);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"cooldown_ms\":0")!=std::string::npos,"exact deadline is known zero");
        m.Secure(0xB75998,1);m.Secure(0xB759A8,5000);m.U32(0xCFD27C,9000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":3000")!=std::string::npos,"paused timer uses frozen native time despite base clock advancement");
        m.Secure(0x0440034C,0xFFFFFFFF);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":null,\"cooldown_blocked\":true")!=std::string::npos,"native indefinite duration is blocked and cannot become known ready");
        m.Secure(0x0440034C,3000);m.Secure(0x04400354,0x20);m.Secure(0xB75998,0);m.U32(0xCFD27C,0xFFFFFFF0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":48")!=std::string::npos,"native unsigned clock wrap is decoded with signed remaining bits");
        m.Put<unsigned char>(0xCFD44C,1);m.U32(0xCFD27C,0x09010000);m.Secure(0x09010000,0xFFFFFFF0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":48")!=std::string::npos,"secure clock mode decodes the pointer target rather than the slot");
        m.Secure(0xB759A0,0x30);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":0")!=std::string::npos,"clock offset and uint32 wrap participate in actual native time");
        m.Secure(0xB75980,16);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":null,\"cooldown_blocked\":null")!=std::string::npos,"invalid indexed clock cannot fabricate known ready");
        m.Secure(0xB75988,0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"cooldown_ms\":32")!=std::string::npos,"disabled native clock has original zero value independent of wall time");
        m.U32(0x04400004,Memory::Actor+0x1000);Capture(m.Make(),h,s);
        Check(s.skillCount==1&&ToJson(s,0).find("\"cooldown_ms\":null,\"cooldown_blocked\":null")!=std::string::npos,"foreign skill owner preserves level inventory but cannot supply readiness");
    }
    {
        Memory m;m.U32(Memory::Vtable+0x2E0,0x652670);m.U32(Memory::Vtable+0x340,0x652A70);m.U32(Memory::Vtable+0x344,0x652A80);
        m.Secure(Memory::Actor+0x1170,123);m.Secure(Memory::Actor+0xE48,151);m.SecureFloat(Memory::Actor+0xE50,0.999f);
        History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mp\":123,\"max_mp\":150")!=std::string::npos,"local MP and truncated maxMP follow verified native getters");
        m.Secure(Memory::Actor+0xE48,16777217);m.SecureFloat(Memory::Actor+0xE50,1.0f);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mp\":123,\"max_mp\":16777216")!=std::string::npos,"maxMP rounds base to float32 before native multiplication");
        m.SecureFloat(Memory::Actor+0xE50,std::numeric_limits<float>::infinity());Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mp\":123,\"max_mp\":null")!=std::string::npos,"invalid maxMP does not discard independently valid current MP");
        m.SecureFloat(Memory::Actor+0xE50,256.0f);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"max_mp\":null")!=std::string::npos,"out-of-int32 maxMP is unknown rather than saturated");
        m.U32(Memory::Vtable+0x340,0x12345678);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mp\":null,\"max_mp\":null")!=std::string::npos&&s.me.hp.known,"foreign MP getter does not fabricate MP or corrupt HP observation");
    }
    RunTownStateTests();
    {
        Memory m;m.Town();m.Dialog();m.DialogLayer();m.LowDialogLayer();
        m.RegisteredWindow(7,0xA7E614,0x09A01000,0x09B01000,0x09A11000);History h;Snapshot s;Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid&&s.ui.dialogs.count==2,"new low-layer and existing high-layer dialogs share one bounded inventory");
        Check(ToJson(s,0).find("\"covered_class_ids\":[7,9,35,37,48,86,89,106]")!=std::string::npos,"dialog coverage is explicit instead of implying all game windows");
        bool low=false,high=false;
        for(std::size_t i=0;i<s.ui.dialogs.count;++i){const auto& d=s.ui.dialogs.items[i];if(d.classId==7)low=d.placement.present&&d.placement.layerToken==0x09E01000&&d.placement.order==0;if(d.classId==0x59)high=d.placement.present&&d.placement.layerToken==0x09E00000&&d.placement.order==0;}
        Check(low&&high,"equal order zero in two distinct native layers is valid");
        m.U32(0x09E21000,0x09E21000);Capture(m.Make(),h,s);low=false;high=false;
        for(std::size_t i=0;i<s.ui.dialogs.count;++i){const auto& d=s.ui.dialogs.items[i];if(d.classId==7)low=!d.placement.present;if(d.classId==0x59)high=d.placement.present;}
        Check(s.ui.dialogs.valid&&low&&high,"corrupt low layer does not erase valid high placement or registration");
    }
    {
        Memory m;m.Command();m.Put<std::int32_t>(0x09D00068,2);History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mouse\":{\"scope\":\"native_command_checker_type1\",\"lock_level\":2,\"lock_suppressed\":true,\"checker_flag90_raw\":false,\"physical_excluded\":true}")!=std::string::npos,"mouse command lock is independent of keyboard and has explicit native scope");
        m.Put<std::int32_t>(0x09D00068,0);m.Put<unsigned char>(0x09D00090,1);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"lock_suppressed\":false,\"checker_flag90_raw\":true,\"physical_excluded\":true")!=std::string::npos,"original flag90 can reject physical mouse commands with lock zero");
        m.changeMouseLock=true;m.mouseLockReads=0;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"keyboard_suppressed\":false,\"mouse\":null")!=std::string::npos,"changing mouse state preserves independently known keyboard state");
        m.changeMouseLock=false;m.Put<unsigned char>(0x09D00090,2);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mouse\":null")!=std::string::npos,"out-of-domain native flag90 remains unknown");
    }
    {
        Memory m;m.Town();m.MouseDeferral(1);History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"mouse_button_state\":{\"scope\":\"engine_mouse_button_state\",\"deferred\":true}")!=std::string::npos,"native mouse-button update deferral is reported independently from global input");
        m.MouseDeferral(0);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"deferred\":false")!=std::string::npos,"known mouse-button deferral zero is explicit false");
        m.changeMouseDeferral=true;Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"mouse_button_state\":null")!=std::string::npos,"changing native deferral flag does not fabricate a stable value");
        m.changeMouseDeferral=false;m.MouseDeferral(2);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"mouse_button_state\":null")!=std::string::npos,"invalid deferral byte stays unknown");
        m.MouseDeferral(0);m.Put<unsigned char>(0x8FCAE8,0xCC);Capture(m.Make(),h,s);Check(ToJson(s,0).find("\"mouse_button_state\":null")!=std::string::npos,"foreign native mouse code cannot authorize raw-global interpretation");
    }
    {
        Memory m;m.Town();m.Dialog();m.DialogLayer();History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"placement\":{\"layer_token\":\"0x09E00000\",\"attached\":true,\"order\":0,\"layer_shown_raw\":true,\"layer_input_enabled_raw\":false}")!=std::string::npos,
              "dialog placement reports verified native layer attachment/order and raw layer flags");
        m.U32(0x09E20008,0x09B10000);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"attached\":false,\"order\":null")!=std::string::npos,
              "registered dialog absent from complete native layer is explicitly detached");
        m.U32(0x09E20000,0x09E20000);Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid && ToJson(s,0).find("\"placement\":null")!=std::string::npos,
              "cyclic native layer cannot invent placement or erase known dialog registration");
        m.DialogLayer();m.U32(0x09E00068,129);Capture(m.Make(),h,s);
        Check(s.ui.dialogs.valid && ToJson(s,0).find("\"placement\":null")!=std::string::npos,
              "oversize layer is bounded unknown rather than scanning unbounded UI");
    }
    {
        Memory m;m.Command();History h;Snapshot s;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"keyboard_lock_level\":0,\"keyboard_suppressed\":false")!=std::string::npos,
              "known zero keyboard command lock is admitted without claiming world input");
        m.Put<std::int32_t>(0x09D00064,3);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"keyboard_lock_level\":3,\"keyboard_suppressed\":true")!=std::string::npos,
              "nonzero command lock suppresses type0 independently of any dialog");
        Check(ToJson(s,0).find("\"input_blocked\":null")!=std::string::npos,"command gate never claims a global input predicate");
        m.changeCommandLock=true;m.commandLockReads=0;Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"command_input\":null")!=std::string::npos,"command lock changing during sample is unknown");
        m.changeCommandLock=false;m.U32(0x99A8CC,0x12345678);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"command_input\":null")!=std::string::npos,"unverified command checker cannot claim input admission");
        m.Command(0);Capture(m.Make(),h,s);
        Check(ToJson(s,0).find("\"checker_token\":\"0x00B5DE68\"")!=std::string::npos,
              "native command fallback is the static object address, never a pointer slot");
        m.Town();m.Command();constexpr auto foreign=Memory::Actor+0x10000;
        m.PlayerAt(foreign,1);m.U32(foreign+0x2384,0x09D00000);m.U32(0xB75A14,foreign);Capture(m.Make(),h,s);
        Check(s.valid && ToJson(s,0).find("\"command_input\":null")!=std::string::npos,
              "town command capture waits for local user binding and rejects same-UID foreign combat object");
        m.U32(Memory::User+0x110,foreign);m.U32(foreign+0xD0,0);Capture(m.Make(),h,s);
        Check(s.valid && ToJson(s,0).find("\"keyboard_lock_level\":0,\"keyboard_suppressed\":false")!=std::string::npos,
              "bound ACT1 town stat object kind zero supports the same original command getter");
    }
    {
        Memory m; constexpr auto peer = Memory::Actor + 0x10000, peerUser = Memory::User + 0x1000;
        m.PlayerAt(peer, 2);m.Put<std::uint16_t>(peerUser+4,2);m.U32(peerUser+0x110,peer);
        m.U32(Memory::Party+0x14,peerUser);m.Secure(peer+0x1170,87);
        History h;Snapshot s;Capture(m.Make(),h,s);
        Check(s.playerCount==2 && s.players[1].mp.known && s.players[1].mp.value==87,
              "peer MP follows its own UID-bound combat object");
        Check(ToJson(s,0).find("\"mp_source\":{\"actor_token\":\"0x04010000\",\"local\":false}")!=std::string::npos,
              "peer MP explicitly identifies the receiver-native source");
        m.Secure(peer+0x1170,81);Capture(m.Make(),h,s);
        Check(s.players[1].mp.known && s.players[1].mp.value==81 && s.me.mp.value==123,
              "peer and local MP observations do not substitute for one another");
        m.PlayerAt(Memory::Actor+0x20000,2);m.changePeerBindingAtMp=true;m.peerBindingReads=0;Capture(m.Make(),h,s);
        Check(s.valid && s.playerCount==2 && !s.players[1].mp.known,
              "peer replacement before MP sampling cannot mix an old actor with a new MP source");
        m.changePeerBindingAtMp=false;m.U32(peerUser+0x110,peer);
        m.Secure(peer+0xD4,3);Capture(m.Make(),h,s);
        Check(s.truncated,"peer UID mismatch cannot publish another user's MP");
    }
    {
        Memory m;m.Town();constexpr auto combat=Memory::Actor+0x10000;
        m.PlayerAt(combat,1);m.U32(Memory::User+0x110,combat);m.U32(0xB75A14,combat);
        m.U32(combat+0xD0,0); // Actual ACT1 town combat-stat objects are not map actors.
        m.Secure(combat+0x1170,0);History h;Snapshot s;Capture(m.Make(),h,s);
        Check(s.valid && s.me.mp.known && s.me.mp.value==0 && s.me.maxMp.value==1250
              && !s.me.hp.known && !s.me.dead.known,
              "town MP uses separate combat stats and preserves town combat unknowns");
        Check(ToJson(s,0).find("\"mp_source\":{\"actor_token\":\"0x04010000\",\"local\":true}")!=std::string::npos,
              "town source never relabels rendering actor as combat stats");
        m.U32(0xB75A14,combat+0x1000);Capture(m.Make(),h,s);
        Check(s.valid && !s.me.mp.known && !s.me.maxMp.known,
              "local town MP requires the live global local-combat binding");
        m.U32(0xB75A14,combat);m.U32(combat+0xD0,0x222);Capture(m.Make(),h,s);
        Check(s.valid && !s.me.mp.known,"unsupported town combat type does not reuse render actor offsets");
    }
    return RunDungeonAndExportTests();
}
