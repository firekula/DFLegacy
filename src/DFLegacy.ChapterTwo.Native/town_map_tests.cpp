#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>

#ifndef TOWN_MAP_BASELINE
#include "ChapterTwoTownMap.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char townMapFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::uintptr_t HitCall = 0xa3cc1f;
    constexpr std::uintptr_t MouseX = 0xf2e7f4, MouseY = 0xf2e7f8;
    constexpr std::uintptr_t RightEvent = 0xf2e800, RightDown = 0xf2e80a, RightCapture = 0xd4dfc0;
    constexpr std::uintptr_t LeftEvent = 0xf2e7fc, LeftFocus = 0xd4dfc8, NativeHit = 0xd4dfc4;
    constexpr std::uintptr_t AutoMove = 0xd586b0;
    constexpr std::uint32_t PanelId = 73;

    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    template<class T> std::uintptr_t Address(T& object) { return reinterpret_cast<std::uintptr_t>(&object); }
    void Dword(std::uintptr_t address, std::uint32_t value) { At<std::uint32_t>(address) = value; }
    void Byte(std::uintptr_t address, std::uint8_t value) { At<std::uint8_t>(address) = value; }

    unsigned char* MapImage(const wchar_t* path)
    {
        auto* image = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        if (!image)
        {
            const auto begin = reinterpret_cast<std::uintptr_t>(townMapFixtureReserve);
            auto* preferred = reinterpret_cast<unsigned char*>(Base);
            DWORD protect = 0;
            if (begin > Base || Base + ImageSize > begin + sizeof(townMapFixtureReserve) ||
                !VirtualProtect(preferred, ImageSize, PAGE_EXECUTE_READWRITE, &protect))
            {
                std::printf("Cannot reserve the fixture image at its preferred base (Windows error %lu).\n", GetLastError());
                return nullptr;
            }
            image = preferred;
        }
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return nullptr;
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        const auto fits = [](std::size_t offset, std::size_t length, std::size_t limit) {
            return offset <= limit && length <= limit - offset;
        };
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return nullptr;
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.ImageBase != Base ||
            nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                sizeof(*sections) * nt->FileHeader.NumberOfSections, file.size())) return nullptr;
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, ImageSize) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return nullptr;
            std::memcpy(image + section.VirtualAddress, file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
        return image;
    }

    struct Rect { std::int32_t x, y, width, height; };
    struct RegionFixture
    {
        std::uint32_t sentinel[2]{}, node[4]{}, record[3]{};
        Rect rect;
        RegionFixture(std::uintptr_t table, std::uint32_t area, Rect bounds) : rect(bounds)
        {
            sentinel[0] = sentinel[1] = Address(node);
            node[0] = node[1] = Address(sentinel);
            node[2] = 1024 + area;
            node[3] = Address(record);
            record[0] = 1;
            record[1] = area;
            record[2] = Address(rect);
            Dword(table + 8, Address(sentinel));
            Dword(table + 12, 1);
        }
    };

    struct Fixture
    {
        std::uint32_t root[30]{}, panel[30]{}, occluder[30]{}, list[2]{}, item[3]{}, regionsMenu[6]{}, npcsMenu[6]{};
        std::uint32_t input[0x1360 / 4]{}, owner[30]{}, plane[23]{}, markers[2]{}, quest[8]{}, npc[5]{}, sprite[12]{};
        std::uint32_t game[9]{}, town[40]{}, userManager[12]{}, user[90]{}, actor[0x510 / 4]{};
        std::uint32_t map[67]{}, area[21]{}, definition[95]{}, sceneManager[19]{}, areaId = 0;
        std::uint32_t sceneA[2]{}, sceneB[2]{};
        Rect floor{500, 210, 1400, 140};
        RegionFixture project{Address(plane) + 0x20, 0, {100, 150, 180, 90}};
        RegionFixture world{0xdfaf5c, 0, {500, 210, 1400, 140}};

        Fixture()
        {
            Dword(0xd56d24, Address(input));
            Dword(Address(input) + 0x1354, Address(owner));
            Dword(Address(owner), 0xc11890);
            Dword(Address(owner) + 4, Address(panel));
            Dword(Address(owner) + 0x0c, Address(plane));
            Dword(Address(owner) + 0x1c, Address(regionsMenu));
            Dword(Address(owner) + 0x60, Address(npcsMenu));
            for (const auto offset : {0x14, 0x18, 0x58, 0x5c, 0x74})
                Dword(Address(owner) + offset, Address(npcsMenu));
            Byte(Address(root) + 0x10, 1);
            Byte(Address(root) + 0x11, 1);
            Dword(Address(root) + 0x60, Address(list));
            Dword(Address(root) + 0x64, 1);
            list[0] = list[1] = Address(item);
            item[1] = Address(item);
            item[2] = Address(panel);
            Dword(Address(panel) + 0x0c, PanelId);
            Byte(Address(panel) + 0x10, 1);
            Byte(Address(panel) + 0x11, 1);
            Dword(Address(panel) + 0x14, 50);
            Dword(Address(panel) + 0x18, 26);
            Dword(Address(panel) + 0x1c, 420);
            Dword(Address(panel) + 0x20, 370);
            std::memcpy(occluder, panel, sizeof(panel));
            occluder[0x0c / 4] = PanelId + 1;
            Dword(0xdfaefc, 50);
            Dword(0xd42a20, 26);
            Dword(Address(plane) + 4, static_cast<std::uint32_t>(-50));
            Dword(Address(plane) + 8, static_cast<std::uint32_t>(-26));
            Dword(Address(plane) + 0x14, Address(markers));
            Dword(Address(plane) + 0x18, Address(markers) + sizeof(markers));
            Dword(Address(plane) + 0x1c, Address(markers) + sizeof(markers));
            quest[0] = 0xc194d4;
            quest[2] = 190;
            quest[3] = 200;
            quest[4] = Address(sprite);
            quest[7] = Address(sprite);
            sprite[0x24 / 4] = 80;
            sprite[0x28 / 4] = 80;
            sprite[0x14 / 4] = 30;
            sprite[0x18 / 4] = 40;
            npc[0] = 0xc04150;
            npc[2] = 190;
            npc[3] = 200;
            npc[4] = Address(sprite);

            Dword(0xd564ac, Address(game));
            Dword(Address(game) + 0x1c, Address(town));
            Dword(Address(town) + 0x8c, 1);
            Dword(Address(town) + 0x90, Address(areaId));
            Dword(0xf35af0, Address(sceneManager));
            Dword(0xf35af4, 0xfafe6789);
            sceneManager[1] = 2;
            sceneManager[0x44 / 4] = Address(sceneA);
            sceneManager[0x48 / 4] = Address(sceneB);
            game[0x14 / 4] = 1;
            game[0x10 / 4] = (Address(game) + 0x11) ^ At<std::uint32_t>(0xf35af4);
            sceneA[1] = (Address(sceneA) + 4) ^ At<std::uint32_t>(0xf35af4) ^ 1;
            sceneB[1] = (Address(sceneB) + 4) ^ At<std::uint32_t>(0xf35af4) ^ 1;
            Dword(0xd56cf4, Address(userManager));
            userManager[0x2c / 4] = Address(user);
            user[0x164 / 4] = Address(actor);
            user[8 / 4] = 12;
            Dword(0xd7a8c0, Address(actor));
            actor[0] = 0xaea99c;
            actor[0xfc / 4] = Address(map);
            map[0x104 / 4] = Address(area);
            area[0x50 / 4] = Address(definition);
            definition[0x174 / 4] = Address(floor);
            definition[0x178 / 4] = Address(floor) + sizeof(floor);
            Byte(0x4917f0, 0xc3); // The real setter still writes the destination. Stub its live movement callback.
        }

        int Hit(int x, int y, unsigned event = 16)
        {
            Dword(MouseX, static_cast<unsigned>(x));
            Dword(MouseY, static_cast<unsigned>(y));
            Dword(RightEvent, event);
            Dword(RightCapture, UINT32_MAX);
            const auto displacement = At<std::uint32_t>(HitCall + 1);
            return reinterpret_cast<int (__thiscall*)(void*)>(HitCall + 5 + displacement)(root);
        }

        bool MovedTo(int x, int y) const
        {
            return At<std::uint8_t>(AutoMove) == 1 && actor[0x278 / 4] == static_cast<unsigned>(x) &&
                actor[0x27c / 4] == static_cast<unsigned>(y);
        }

        void ResetMovement()
        {
            Byte(AutoMove, 0);
            actor[0x278 / 4] = actor[0x27c / 4] = 0;
        }
    };
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2 || argc > 3 || (argc == 3 && std::wcscmp(argv[2], L"--cross") != 0)) return 2;
    auto* image = MapImage(argv[1]);
    if (!image) return 2;
    int failures = 0;
    const auto check = [&](bool accepted, const char* name) {
        if (!accepted) { std::printf("FAIL %s\n", name); ++failures; }
    };
#ifndef TOWN_MAP_BASELINE
    const auto original = At<std::uint32_t>(HitCall + 1);
    Byte(0x492290, 0xcc);
    check(!dfl::chapter_two::InstallTownMapMovement(image, ImageSize) &&
        At<std::uint32_t>(HitCall + 1) == original, "signature mismatch leaves native capture untouched");
    Byte(0x492290, 0x55);
    check(dfl::chapter_two::InstallTownMapMovement(image, ImageSize), "verified image installs movement");
#endif
    Fixture f;
    check(f.Hit(240, 216) == PanelId && At<unsigned>(RightEvent) == 0 &&
        At<unsigned>(RightCapture) == PanelId, "native capture returns its full panel id and consumes release");
    check(f.MovedTo(1200, 272), "same-area ground click sets the native destination");
    f.ResetMovement();
    f.markers[0] = Address(f.quest);
    const auto questHit = reinterpret_cast<bool (__thiscall*)(void*, int, int)>(0x965a00);
    check(questHit(f.quest, 190, 190), "quest fixture overlaps the selected point by the native hit test");
    f.Hit(240, 216);
    check(f.MovedTo(1200, 272), "quest marker passes right release through to valid ground");
    f.ResetMovement();
    f.markers[1] = Address(f.npc);
    const auto npcHit = reinterpret_cast<bool (__thiscall*)(void*, int, int)>(0x965700);
    check(npcHit(f.npc, 190, 190), "NPC fixture overlaps the quest by the native hit test");
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "NPC and quest overlap still preserves the NPC marker");
#ifndef TOWN_MAP_BASELINE
    f.Hit(258, 216);
    check(f.MovedTo(1340, 272), "a visible NPC elsewhere leaves valid ground actionable");
    f.markers[1] = 0;
    f.Hit(240, 216);
    check(f.MovedTo(1200, 272), "a fresh right release replaces the previous destination");
    f.Hit(290, 134);
    check(f.MovedTo(1200, 272), "a rejected click leaves the current destination intact");
    f.ResetMovement();
#endif
    f.markers[1] = 0;
    Dword(LeftEvent, 1);
    Dword(LeftFocus, UINT32_MAX);
    check(f.Hit(240, 216, 0) == PanelId && At<unsigned>(NativeHit) == PanelId &&
        At<std::uint8_t>(AutoMove) == 0, "left click keeps native panel handling without movement");
    Dword(LeftEvent, 0);
    f.Hit(290, 134);
    check(At<std::uint8_t>(AutoMove) == 0, "right release outside mapped ground leaves native movement idle");
    f.item[2] = Address(f.occluder);
    check(f.Hit(240, 216) == PanelId + 1 && At<unsigned>(RightCapture) == PanelId + 1 &&
        At<std::uint8_t>(AutoMove) == 0, "another captured control owns the release");
    f.item[2] = Address(f.panel);
    Byte(Address(f.regionsMenu) + 0x10, 1);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "open region menu blocks canvas movement");
    Byte(Address(f.regionsMenu) + 0x10, 0);
    Byte(Address(f.npcsMenu) + 0x10, 1);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "open NPC menu blocks canvas movement");
    Byte(Address(f.npcsMenu) + 0x10, 0);
    Byte(Address(f.panel) + 0x10, 0);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "hidden map preserves native movement state");
    Byte(Address(f.panel) + 0x10, 1);
    Byte(RightDown, 1);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "held physical button cannot issue a destination");
    Byte(RightDown, 0);
    Dword(0xdfaefc, 51);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "mismatched viewport cannot project a destination");
    Dword(0xdfaefc, 50);
    Dword(Address(f.plane) + 0x2c, 2);
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "incomplete projection table cannot select a destination");
    Dword(Address(f.plane) + 0x2c, 1);
    f.floor.width = 500;
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "native point validation rejects a non-walkable world point");
    f.floor.width = 1400;
    f.user[0x30 / 4] = 5;
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "inactive local character cannot receive a destination");
    f.user[0x30 / 4] = 0;
    f.sceneB[1] ^= 1;
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "inconsistent native scene identity leaves movement idle");
    f.sceneB[1] ^= 1;
    f.areaId = 1;
    f.Hit(240, 216);
    check(At<std::uint8_t>(AutoMove) == 0, "world point is never submitted against another region's geometry");
    if (argc == 3)
    {
        // Actual catalog geometry from the second-chapter PVF, with real image UI and movement entry points.
        f.areaId = 0;
        f.ResetMovement();
        f.markers[0] = 0;
        std::uint32_t spare[4]{}, spare2[4]{}, map2[67]{}, area2[21]{}, definition2[95]{};
        RegionFixture destinationProject(Address(spare), 1, {300, 150, 100, 100});
        RegionFixture destinationWorld(Address(spare2), 1, {329, 165, 291, 170});
        const auto append = [](RegionFixture& first, RegionFixture& second) {
            first.sentinel[1] = Address(second.node);
            first.node[0] = Address(second.node);
            second.node[0] = Address(first.sentinel);
            second.node[1] = Address(first.node);
        };
        append(f.project, destinationProject);
        append(f.world, destinationWorld);
        Dword(Address(f.plane) + 0x2c, 2);
        Dword(0xdfaf5c + 12, 2);
        Rect sourceFloor[]{{9,196,1548,142},{729,157,109,40},{1048,322,89,33}};
        Rect destFloor[]{{397,165,155,170},{329,165,73,170},{547,165,73,170}};
        struct Door { Rect bounds; int town, area; };
        Door sourceDoors[]{{{-5,192,29,141},2,1},{{730,147,110,22},1,1},{{1540,189,28,144},1,2},{{1049,341,88,19},1,3}};
        Door destDoors[]{{{330,324,289,24},1,0}};
        f.definition[0x164 / 4] = Address(sourceDoors);
        f.definition[0x168 / 4] = Address(sourceDoors) + sizeof(sourceDoors);
        f.definition[0x174 / 4] = Address(sourceFloor);
        f.definition[0x178 / 4] = Address(sourceFloor) + sizeof(sourceFloor);
        definition2[0x164 / 4] = Address(destDoors);
        definition2[0x168 / 4] = Address(destDoors) + sizeof(destDoors);
        definition2[0x174 / 4] = Address(destFloor);
        definition2[0x178 / 4] = Address(destFloor) + sizeof(destFloor);
        map2[0x104 / 4] = Address(area2);
        area2[0x50 / 4] = Address(definition2);
        const unsigned char readX[]{0x8b,0x81,0x00,0x05,0,0,0xc3};
        const unsigned char readY[]{0x8b,0x81,0x04,0x05,0,0,0xc3};
        std::memcpy(reinterpret_cast<void*>(0x406b50), readX, sizeof(readX));
        std::memcpy(reinterpret_cast<void*>(0x406b60), readY, sizeof(readY));
        f.actor[0x500 / 4] = 784;
        f.actor[0x504 / 4] = 215;
        f.actor[0x270 / 4] = f.actor[0x274 / 4] = 4;
        const auto tick = [&] {
            constexpr std::uintptr_t call = 0x868bb1;
            const auto displacement = At<std::uint32_t>(call + 1);
            reinterpret_cast<void (__thiscall*)(void*)>(call + 5 + displacement)(f.owner);
        };
        for (int attempt = 0; attempt < 60 && At<std::uint8_t>(AutoMove) == 0; ++attempt)
        {
            f.Hit(400, 216);
            Sleep(20);
            tick();
        }
        const auto atDoor = f.MovedTo(784, 163);
        check(atDoor, "cross-area click selects the actual source gate instead of destination coordinates");
        if (atDoor)
        {
            const auto enterDestination = [&] {
                f.areaId = 1;
                f.actor[0xfc / 4] = Address(map2);
                f.actor[0x500 / 4] = 475;
                f.actor[0x504 / 4] = 300;
                f.ResetMovement();
            };
            const auto enterSource = [&] {
                f.areaId = 0;
                f.actor[0xfc / 4] = Address(f.map);
                f.actor[0x500 / 4] = 784;
                f.actor[0x504 / 4] = 215;
                f.actor[0x270 / 4] = f.actor[0x274 / 4] = 4;
                f.ResetMovement();
            };
            const auto frame = [&] { Sleep(20); tick(); };
            enterDestination();
            frame();
            check(At<std::uint8_t>(AutoMove) == 0, "route validates map transition before continuing");
            frame();
            check(f.MovedTo(475, 233), "route resumes after the expected gate into fresh native geometry");
            f.actor[0x504 / 4] = 233;
            frame();
            f.Hit(240, 216);
            frame();
            check(f.MovedTo(474, 329), "return route selects the opposite side of the same gate");
            enterSource();
            frame();
            frame();
            check(f.MovedTo(1200, 272), "return route resumes into its source area");
            enterSource();
            f.Hit(400, 216);
            frame();
            check(f.MovedTo(784, 163), "route can start again after returning");
            Byte(AutoMove, 0);
            f.actor[0x270 / 4] = 0;
            frame();
            enterDestination();
            frame();
            frame();
            check(At<std::uint8_t>(AutoMove) == 0, "native manual takeover prevents post-gate continuation");
            enterSource();
            sourceDoors[1].area = 2;
            f.Hit(400, 216);
            frame();
            check(At<std::uint8_t>(AutoMove) == 0, "mismatched live portal geometry rejects an otherwise valid route");
            sourceDoors[1].area = 1;
            f.Hit(400, 216);
            frame();
            check(f.MovedTo(784, 163), "restored native geometry can plan again");
            f.areaId = 1;
            f.ResetMovement();
            frame();
            enterDestination();
            frame();
            frame();
            check(At<std::uint8_t>(AutoMove) == 0, "area id change alone cannot prove a map transition");
        }
    }
    std::printf("Chapter Two town map tests: %d failure(s)\n", failures);
    return failures ? 1 : 0;
}
