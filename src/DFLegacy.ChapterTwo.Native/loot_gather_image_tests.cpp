#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include "ChapterTwoCemeteryGuard.h"
#include "ChapterTwoLootGather.h"
#include "ChapterTwoDungeonRating.h"
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char cemeteryReplayReserve[0x1300000]{1};

namespace {
constexpr std::uintptr_t Base=0x400000;
constexpr std::size_t ImageSize=0xe57000;
unsigned Failures=0,Writes=0,FailureMode=0,FailAt=1;
void Check(bool ok,const char* text) {if(!ok){++Failures;std::fprintf(stderr,"FAIL: %s\n",text);}}
bool Write(std::uintptr_t address,const void* value,std::size_t size) noexcept {
 ++Writes;
 if(FailureMode==1&&Writes==FailAt)return false;
 std::memcpy(reinterpret_cast<void*>(address),value,size);
 return !(FailureMode==2&&Writes==FailAt);
}
    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t offset, std::size_t size, std::size_t limit) {
            return offset <= limit && size <= limit - offset;
        };
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                sizeof(*sections) * nt->FileHeader.NumberOfSections, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, ImageSize)) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, ImageSize) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress, file.data() + section.PointerToRawData,
                section.SizeOfRawData);
        }
        return image;
    }

    template<class T> T& Native(std::uintptr_t address)
    {
        return *reinterpret_cast<T*>(address);
    }

    void __cdecl IgnoreNativeTrace(unsigned, ...) {}
    unsigned GateOwner = 1;
    void* GateStage = nullptr;
    void* ReleasedGate = nullptr;
    unsigned ReleasedCollisions = 0;
    unsigned __fastcall GateController(void*, void*) { return GateOwner; }
    void __fastcall IgnoreGateAnimation(void*, void*, unsigned, unsigned, unsigned, unsigned, unsigned) {}
    void* __fastcall CurrentGateStage(void*, void*) { return GateStage; }
    void __fastcall RemoveGateCollision(void*, void*, void* gate, unsigned mode)
    {
        if (mode == 0)
        {
            ReleasedGate = gate;
            ++ReleasedCollisions;
        }
    }

    void JumpToFixture(std::uintptr_t address, std::uintptr_t target)
    {
        const auto delta = static_cast<std::uint32_t>(target - address - 5);
        Native<unsigned char>(address) = 0xE9;
        std::memcpy(reinterpret_cast<void*>(address + 1), &delta, sizeof(delta));
    }

    __declspec(naked) void __stdcall NotifyDungeonInfo(unsigned)
    {
        __asm {
            mov eax, [esp+4]
            push offset finished
            push 0
            push 0
            push eax
            mov ecx, 0DFD860h
            mov eax, 423DD0h
            jmp eax
        finished:
            ret 4
        }
    }

    void CheckCemeteryGateAuthority(const std::vector<unsigned char>& original)
    {
        auto* image = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base),
            ImageSize, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
        const bool allocated = image != nullptr;
        if (!image)
        {
            const auto begin = reinterpret_cast<std::uintptr_t>(cemeteryReplayReserve);
            DWORD oldProtection = 0;
            if (begin <= Base && Base + ImageSize <= begin + sizeof(cemeteryReplayReserve)
                && VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &oldProtection))
                image = reinterpret_cast<unsigned char*>(Base);
        }
        Check(image != nullptr, "original Ver_OB_216 address is available for the Cemetery replay");
        if (!image) return;
        std::memcpy(image, original.data(), ImageSize);
        const bool installed = dfl::chapter_two::InstallCemeteryGuard(image, ImageSize);
        Check(installed, "production Cemetery patch installs on Ver_OB_216");
        if (!installed)
        {
            if (allocated) VirtualFree(image, 0, MEM_RELEASE);
            return;
        }

        Native<unsigned char>(0x423DD5) = 0xC3; // End after the DUNGEON_INFO commit.
        Native<unsigned char>(0x42F549) = 0xC3; // Return to the fixture at the end of notification dispatch.
        Native<unsigned char>(0x424233) = 0xC3; // Only exercise the START_MAP extension hook.
        // Run the native gate state and collision routines. End the callback
        // before it touches the live text and animation managers.
        const unsigned char endGateCallback[]{0x5F, 0x5E, 0x5B, 0x5D, 0xC2, 0x10, 0};
        std::memcpy(reinterpret_cast<void*>(0x5C85C9), endGateCallback, sizeof(endGateCallback));
        Native<unsigned char>(0x43C160) = 0xC3;
        JumpToFixture(0x476AE0, reinterpret_cast<std::uintptr_t>(&RemoveGateCollision));
        const unsigned char threshold[]{0x31, 0xC0, 0xC3};
        const unsigned char elapsed[]{0xB8, 0xE8, 0x03, 0, 0, 0xC3};
        const unsigned char restartTimer[]{0xC2, 8, 0};
        std::memcpy(reinterpret_cast<void*>(0xA46AB0), threshold, sizeof(threshold));
        std::memcpy(reinterpret_cast<void*>(0xA39820), elapsed, sizeof(elapsed));
        std::memcpy(reinterpret_cast<void*>(0xA39880), restartTimer, sizeof(restartTimer));
        Native<unsigned char>(0x48B360) = 0xC3; // End the base actor tick.
        Native<std::uintptr_t>(0xD4E3B0) = reinterpret_cast<std::uintptr_t>(&IgnoreNativeTrace);
        FlushInstructionCache(GetCurrentProcess(), image, ImageSize);

        unsigned guardians[]{50073, 50074, 50075, 50076};
        using ChangeState = void(__thiscall*)(void*, unsigned, void*, unsigned, unsigned);
        Native<unsigned*>(0xD68EBC) = guardians;
        Native<unsigned*>(0xD68EC0) = guardians + std::size(guardians);
        Native<unsigned*>(0xD68EC4) = guardians + std::size(guardians);
        std::array<std::uintptr_t, 0x340 / 4> gateVtable{};
        std::array<std::uintptr_t, 0x84 / 4> uiVtable{};
        std::array<std::uintptr_t, 0x54 / 4> stage{};
        uiVtable[0x80 / 4] = reinterpret_cast<std::uintptr_t>(&CurrentGateStage);
        const auto ui = reinterpret_cast<std::uintptr_t>(uiVtable.data());
        Native<std::uintptr_t>(0xD56D1C) = reinterpret_cast<std::uintptr_t>(&ui);
        stage[0x50 / 4] = reinterpret_cast<std::uintptr_t>(&stage);
        GateStage = stage.data();
        std::array<unsigned, 8> gateOwners{};
        std::array<std::uintptr_t, 0x50 / 4> protectedValues{};
        std::array<unsigned, 2> mirrors{};
        alignas(unsigned) std::array<unsigned char, 0x900> gate{};
        const auto gateAddress = reinterpret_cast<std::uintptr_t>(gate.data());
        gateVtable[0x160 / 4] = reinterpret_cast<std::uintptr_t>(&GateController);
        gateVtable[0x284 / 4] = reinterpret_cast<std::uintptr_t>(&IgnoreGateAnimation);
        gateVtable[0x338 / 4] = 0x48A9C0;
        protectedValues[0x44 / 4] = reinterpret_cast<std::uintptr_t>(&mirrors[0]);
        protectedValues[0x48 / 4] = reinterpret_cast<std::uintptr_t>(&mirrors[1]);
        Native<std::uintptr_t>(0xF35AF0) = reinterpret_cast<std::uintptr_t>(protectedValues.data());
        Native<unsigned>(0xD7A8BC) = 1;
        Native<std::uintptr_t>(gateAddress) = reinterpret_cast<std::uintptr_t>(gateVtable.data());
        Native<std::uintptr_t>(gateAddress + 0xFC) = reinterpret_cast<std::uintptr_t>(gateOwners.data());
        Native<unsigned char>(gateAddress + 0x46C) = 1;
        Native<const unsigned*>(gateAddress + 0x884) = guardians;
        Native<const unsigned*>(gateAddress + 0x888) = guardians + std::size(guardians);
        const auto tickGate = reinterpret_cast<void(__thiscall*)(void*)>(0x5C8710);

        const auto sendGateState = [](std::uint32_t run, unsigned char action)
        {
            unsigned char packet[]{'C', 'E', 'M', '1', 0, 0, 0, 0, action};
            std::memcpy(packet + 4, &run, sizeof(run));
            Native<const unsigned char*>(0xE23EE8) = packet;
            Native<int>(0xE23EEC) = sizeof(packet);
            Native<void(__cdecl*)()>(0x42F564 + 186 * sizeof(std::uint32_t))();
        };
        const auto sendRoom = [](unsigned char x, unsigned char y)
        {
            const unsigned char packet[]{x, y, 0, 0, 0, 0, 2};
            Native<const unsigned char*>(0xE23EE8) = packet;
            Native<int>(0xE23EEC) = sizeof(packet);
            Native<void(__cdecl*)()>(0x42F564 + 29 * sizeof(std::uint32_t))();
        };
        const auto sendAbyssExit = [](std::uint32_t run, unsigned char x, unsigned char y)
        {
            unsigned char packet[]{'C', 'E', 'M', '1', 0, 0, 0, 0, 3, x, y};
            std::memcpy(packet + 4, &run, sizeof(run));
            Native<const unsigned char*>(0xE23EE8) = packet;
            Native<int>(0xE23EEC) = sizeof(packet);
            Native<void(__cdecl*)()>(0x42F564 + 186 * sizeof(std::uint32_t))();
        };
        const auto readPathState = [](std::uintptr_t call)
        {
            std::int32_t delta{};
            std::memcpy(&delta, reinterpret_cast<const void*>(call + 1), sizeof(delta));
            return reinterpret_cast<unsigned(__thiscall*)(void*)>(call + 5 + delta)(nullptr);
        };
        const auto tick = [&] {
            tickGate(gate.data());
            return Native<unsigned>(gateAddress + 0x890);
        };
        const auto recreateGate = [&] {
            Native<unsigned>(gateAddress + 0x890) = 0;
            Native<unsigned>(gateAddress + 0x500) = 1;
        };
        NotifyDungeonInfo(37);
        Check(tick() == 1, "other dungeons use the original native hunt predicate");
        NotifyDungeonInfo(35);
        recreateGate();
        Check(tick() == 0, "Cemetery waits for server authority as soon as DUNGEON_INFO arrives");
        sendGateState(100, 0);
        Check(tick() == 0, "a locked server snapshot keeps four local deaths from opening the original gate");
        sendRoom(2, 0);
        sendAbyssExit(99, 2, 0);
        sendAbyssExit(100, 3, 0);
        Check(readPathState(0x5CCCC4) == 0 && readPathState(0x5CCCEC) == 0,
            "only the current run and room can authorize an early Abyss exit");
        sendAbyssExit(100, 2, 0);
        Check(readPathState(0x5CCCC4) == 1 && readPathState(0x5CCCEC) == 1 && tick() == 0,
            "both native side-door checks honor the Abyss exit while the boss door stays locked");
        sendRoom(3, 0);
        sendAbyssExit(100, 2, 0);
        Check(readPathState(0x5CCCC4) == 0 && readPathState(0x5CCCEC) == 0,
            "a new START_MAP resets permission and ignores a delayed exit notification");
        const auto peerGateState = Native<ChangeState>(0xB3BED4 + 0x224);
        peerGateState(gate.data(), 0, nullptr, 0, 0);
        Check(ReleasedCollisions == 0 && Native<unsigned>(gateAddress + 0x500) == 1,
            "a peer's native open state cannot bypass the server lock");
        recreateGate();
        Native<unsigned*>(0xD68EC0) = guardians;
        GateOwner = 0;
        sendGateState(99, 2);
        Check(tick() == 0, "a stale run's unlock is ignored");
        sendGateState(100, 2);
        Native<unsigned char>(gateAddress + 0x46C) = 0;
        Check(tick() == 0, "an inactive gate waits to play the server-authorized animation");
        Native<unsigned char>(gateAddress + 0x46C) = 1;
        Check(tick() == 2 && Native<unsigned>(gateAddress + 0x500) == 0
            && ReleasedCollisions == 1 && ReleasedGate == gate.data(),
            "the native gate callback clears collision on a non-owner with no local kills or P2P");
        peerGateState(gate.data(), 0, nullptr, 0, 0);
        Check(ReleasedCollisions == 1, "late peer-open state does not replay an authorized opening");
        recreateGate();
        sendGateState(100, 0);
        Check(tick() == 2, "duplicate entry state cannot relock an unlocked run");
        NotifyDungeonInfo(35);
        recreateGate();
        sendGateState(101, 0);
        sendGateState(100, 2);
        Check(tick() == 0, "a new dungeon resets the previous unlock");
        sendGateState(101, 2);
        Check(tick() == 2, "the new matching unlock opens the recreated gate");
        NotifyDungeonInfo(35);
        recreateGate();
        sendGateState(102, 1);
        Check(tick() == 2, "an entrant joins an already-open run without receiving its past kill events");
        if (allocated) VirtualFree(image, 0, MEM_RELEASE);
    }

}
int wmain(int argc,wchar_t** argv) {
 if(argc!=2)return 2;
 const auto original=ReadImage(argv[1]);if(original.empty())return 2;
 CheckCemeteryGateAuthority(original);
 using dfl::chapter_two::loot_gather_detail::InstallImage;
 auto image=original;
 Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid input must not write");
 for(const std::size_t offset:{0x5272d0,0x527326,0x527381,0x527358,0x83f30,0x6ae0,0x2f4500}) {
  image=original;image[offset]^=1;const auto before=image;Writes=0;
  Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"mutated original signature fails closed");
 }
 for(unsigned mode:{1U,2U}) {
  image=original;Writes=0;FailureMode=mode;
  Check(!InstallImage(image.data(),image.size(),Write)&&Writes==2&&image==original,"failed write restores original prologue");
 }
 image=original;Writes=FailureMode=0;
 Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"verified C2 image accepts chat hook");
 const auto patched=image;
 Check(InstallImage(image.data(),image.size(),Write)&&Writes==1&&image==patched,"install is idempotent");
 image[0x527381]^=1;const auto bad=image;
 Check(!InstallImage(image.data(),image.size(),Write)&&Writes==1&&image==bad,"reinstall rechecks remaining signatures");

 for(const std::size_t offset:{0x1DC6A,0x23DD0,0x2F84C,0x1C8590,0x1C88CA,0x73C0F8}) {
  image=original;image[offset]^=1;const auto before=image;Writes=0;
  Check(!dfl::chapter_two::cemetery_guard_detail::InstallImage(image.data(),image.size(),Write)
      &&Writes==0&&image==before,"Cemetery patch rejects unknown instructions before writing");
 }
 for(unsigned fail:{1U,2U,4U}) {
  image=original;Writes=0;FailureMode=2;FailAt=fail;
  Check(!dfl::chapter_two::cemetery_guard_detail::InstallImage(image.data(),image.size(),Write)
      &&image==original,"partial Cemetery patch installation restores all original instructions");
 }

 for(const std::size_t offset:{0x23B5F,0x2F5D4,0x2F5D8,0xFD0B0,0x24F9E,0x24FB6,0x2594C,0x25962,
        0x11B2DB,0x555FD6,0x5560C6,0x5579F7}) {
  image=original;image[offset]^=1;const auto before=image;Writes=0;
  Check(!dfl::chapter_two::dungeon_rating_detail::InstallImage(image.data(),image.size(),Write)
      &&Writes==0&&image==before,"mutated native rank or lifecycle signature fails closed");
 }
 for(unsigned fail:{1U,4U,8U,9U}) {
  image=original;Writes=0;FailureMode=2;FailAt=fail;
  Check(!dfl::chapter_two::dungeon_rating_detail::InstallImage(image.data(),image.size(),Write)
      &&image==original,"partial rank install restores all original calls and dungeon dispatch");
 }
 image=original;Writes=FailureMode=0;
 Check(dfl::chapter_two::dungeon_rating_detail::InstallImage(image.data(),image.size(),Write)
     &&Writes==9,"verified C2 image installs eight local rank calls and the dungeon reset");
 Check(dfl::chapter_two::dungeon_rating_detail::InstallImage(image.data(),image.size(),Write)
     &&Writes==9,"the same rating patch is idempotent");
 std::printf("loot_gather_image_tests: %u failures\n",Failures);
 return Failures?1:0;
}
