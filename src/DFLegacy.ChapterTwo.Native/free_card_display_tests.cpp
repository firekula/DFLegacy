#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>

#ifndef CHAPTER_TWO_CARDS_BASELINE
#include "ChapterTwoFreeCardDisplay.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char cardFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    std::uintptr_t EntryPoint = 0x418bc6;
    unsigned InputEbx = 0, BeforeEsp = 0, BeforeEbp = 0;
    unsigned SeenEax = 0, SeenEbx = 0, SeenEcx = 0, SeenEdx = 0, SeenEsi = 0, SeenEdi = 0;
    unsigned SeenEsp = 0, SeenEbp = 0, SeenFlags = 0, SeenLocal = 0;
    unsigned Checks = 0;
    int Failures = 0;
    void Check(bool ok, const char* why)
    {
        ++Checks;
        if (!ok) { ++Failures; std::fprintf(stderr, "FAIL C2 cards: %s\n", why); }
    }
    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    template<class T> std::uintptr_t Addr(T& value) { return reinterpret_cast<std::uintptr_t>(&value); }
    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t at, std::size_t size, std::size_t total) { return at <= total && size <= total - at; };
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(dos->e_lfanew, sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(reinterpret_cast<const unsigned char*>(sections) - file.data(),
                nt->FileHeader.NumberOfSections * sizeof(*sections), file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size())) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned n = 0; n < nt->FileHeader.NumberOfSections; ++n)
        {
            const auto& s = sections[n];
            if (!fits(s.VirtualAddress, s.SizeOfRawData, image.size()) ||
                !fits(s.PointerToRawData, s.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + s.VirtualAddress, file.data() + s.PointerToRawData, s.SizeOfRawData);
        }
        return image;
    }
    void Jump(std::uintptr_t address, const void* to)
    {
        At<unsigned char>(address) = 0xe9;
        At<std::uint32_t>(address + 1) = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(to) - address - 5);
    }
    __declspec(naked) void EndCard()
    {
        __asm {
            mov SeenEax,eax
            mov SeenEbx,ebx
            mov SeenEcx,ecx
            mov SeenEdx,edx
            mov SeenEsi,esi
            mov SeenEdi,edi
            mov SeenEsp,esp
            mov SeenEbp,ebp
            pushfd
            pop SeenFlags
            mov eax,dword ptr [ebp-308h]
            mov SeenLocal,eax
            lea esp, [ebp - 40Ch]
            pop edi
            pop esi
            pop ebx
            mov esp, ebp
            pop ebp
            ret
        }
    }
    __declspec(naked) void __cdecl RunCard(const void*, int)
    {
        __asm {
            push ebp
            mov ebp, esp
            sub esp, 400h
            push ebx
            push esi
            push edi
            mov BeforeEsp,esp
            mov BeforeEbp,ebp
            mov eax,11112222h
            mov ebx,InputEbx
            mov ecx,22223333h
            mov edx,33334444h
            mov esi,44445555h
            mov edi,55556666h
            push 8D7h
            popfd
            jmp dword ptr [EntryPoint]
        }
    }
    using Add = void (__thiscall*)(void*, unsigned, unsigned, int, int);
    const auto NativeAdd = reinterpret_cast<Add>(0x95d1f0);
    using Count = unsigned (__thiscall*)(void*, int, int);
    const auto NativeCount = reinterpret_cast<Count>(0x40dd20);
    struct Record { unsigned item, unused[9], value, more[3]; };
    static_assert(sizeof(Record) == 56);
    struct Fixture
    {
        std::array<unsigned char, 0x500> screen{};
        std::array<unsigned char, 0x1300> manager{};
        std::array<std::array<std::array<Record, 16>, 4>, 4> records{};
        std::array<std::array<unsigned char, 0x20>, 8> buttons{};
        std::array<void*, 0x6c0 / 4> actorMethods{};
        void** actor = nullptr;
    };
    Fixture State;
    std::array<unsigned char,6> CurrentFrame{};
    unsigned __cdecl Metadata(unsigned) { return 0; }
    const char* __cdecl Name(unsigned) { return ""; }
    void* __cdecl ItemObject(unsigned, int, int, int, int, int) { return nullptr; }
    void __cdecl Log(unsigned, ...) {}
    void __cdecl Sound(const char*, int, int, int) {}
#ifndef CHAPTER_TWO_CARDS_BASELINE
    void* __cdecl Allocate(std::size_t size) { return std::malloc(size); }
    void __cdecl Deallocate(void* memory) { std::free(memory); }
#endif
    unsigned __fastcall Time(void*, void*) { return 1234; }
    unsigned ObserverSlot = 0;
    unsigned __fastcall OwnSlot(void*, void*) { return ObserverSlot; }
    void PrepareDependencies()
    {
        // Only external metadata, sound and time are substituted. The original
        // packet readers, owner setters, record builder, destructor, string
        // copies and vector insertion/count instructions execute unchanged.
        for (auto a : {0x46dc80u,0x46d810u,0x46d880u,0x46e350u,0x46d7f0u}) Jump(a, reinterpret_cast<void*>(&Metadata));
        Jump(0x46d750, reinterpret_cast<void*>(&Name));
        Jump(0x557cc0, reinterpret_cast<void*>(&ItemObject));
        Jump(0x43c160, reinterpret_cast<void*>(&Sound));
        Jump(0xa39820, reinterpret_cast<void*>(&Time));
        Jump(0x41cbe9, reinterpret_cast<void*>(&EndCard));
        At<void*>(0xd4e3b0) = reinterpret_cast<void*>(&Log);
        FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(Base), ImageSize);
    }
    void Reset()
    {
        State = {};
        const auto screen = Addr(State.screen);
        At<unsigned>(screen) = 0xc11c1c;
        At<void*>(0xd56d24) = State.manager.data();
        At<void*>(Addr(State.manager) + 0x1278) = State.screen.data();
        State.actorMethods[0x6bc / 4] = reinterpret_cast<void*>(&OwnSlot);
        State.actor = State.actorMethods.data();
        At<void*>(0xd7a8bc) = &State.actor;
        for (unsigned owner = 0; owner < 4; ++owner)
        {
            At<unsigned>(screen + 0x2cc + owner * 4) = 0xffffffff;
            At<unsigned>(screen + 0x304 + owner * 4) = 0xffffffff;
            At<void*>(screen + 0x2dc + owner * 4) = State.buttons[owner].data();
            At<void*>(screen + 0x314 + owner * 4) = State.buttons[owner + 4].data();
            for (unsigned group = 0; group < 4; ++group)
            {
                const auto vec = screen + 0x1b8 + 16 * (owner * 4 + group);
                At<void*>(vec + 4) = State.records[owner][group].data();
                At<void*>(vec + 8) = State.records[owner][group].data();
                At<void*>(vec + 12) = State.records[owner][group].data() + 16;
            }
            NativeAdd(State.screen.data(), 0, 5, owner, 0);
            NativeAdd(State.screen.data(), 35901, 0, owner, 0);
            NativeAdd(State.screen.data(), 0, 900 + owner, owner, 1);
            NativeAdd(State.screen.data(), 0, 800 + owner, owner, 2);
        }
    }
    struct Card { unsigned char owner; unsigned gold; unsigned short item; };
    std::vector<unsigned char> Wire(const std::array<Card,4>& cards, bool trailer = true)
    {
        std::vector<unsigned char> body;
        for (const auto& card : cards) body.insert(body.end(), {card.owner, 255, 0});
        if (!trailer) return body;
        body.insert(body.end(), {'D','F','C','R',1,4});
        for (const auto& card : cards)
        {
            body.push_back(card.owner < 4 ? 1 : 0);
            const auto* gold = reinterpret_cast<const unsigned char*>(&card.gold);
            body.insert(body.end(), gold, gold + 4);
            const auto* item = reinterpret_cast<const unsigned char*>(&card.item);
            body.insert(body.end(), item, item + 2);
        }
        return body;
    }
    void Replay(std::vector<unsigned char>& body)
    {
        At<void*>(0xe23ee8) = body.data();
        At<unsigned>(0xe23eec) = static_cast<unsigned>(body.size());
        CurrentFrame={1,74,0,0,0,0};
        const auto size=static_cast<unsigned>(body.size()+7);
        std::memcpy(CurrentFrame.data()+2,&size,4);
        RunCard(CurrentFrame.data(), 0);
    }
    unsigned ActiveItem(unsigned owner)
    {
        return NativeCount(State.screen.data(),owner,0)>1 ? State.records[owner][0][1].item : 0;
    }
    void CheckReward(unsigned owner, unsigned gold, unsigned item)
    {
        const auto& rows = State.records[owner][0];
        Check(NativeCount(State.screen.data(), owner, 0) == (item ? 2u : 1u), "native free vector has one gold row and only the owner's optional item row");
        Check(rows[0].item == 0 && rows[0].value == gold, "native free vector contains this owner's actual gold");
        if (item) Check(rows[1].item == item && rows[1].value == 0, "native free vector contains this owner's actual item");
        Check(State.records[owner][1][0].value == 900 + owner && State.records[owner][2][0].value == 800 + owner,
            "personal reward groups keep their original records");
    }
    // Test-only DFC4 fixtures are emitted by the actual C2 catalog/store matrix.
    // This executes the original C2 card parser/builders and the installed local
    // patch. It is cache/ownership evidence, not a D3D screenshot or premium
    // initialization-handler acceptance test.
    void CheckMatrix(const wchar_t* path)
    {
        std::ifstream stream(path,std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)),{});
        if(file.size()<8 || file.size()>4096 || std::memcmp(file.data(),"DFC4",4)!=0)
        {Check(false,"matrix fixture has bounded DFC4 header");return;}
        std::size_t at=4;
        const auto take=[&](std::size_t count,void* destination) {
            if(at>file.size() || count>file.size()-at)return false;
            std::memcpy(destination,file.data()+at,count);at+=count;return true;
        };
        unsigned count=0;if(!take(4,&count)||count!=4){Check(false,"matrix fixture contains exactly four cases");return;}
        for(unsigned n=0;n<4;++n)
        {
            unsigned char id=0,flags=0;std::array<unsigned char,4> freeOwners{},paidOwners{};
            std::array<unsigned,4> gold{};std::array<unsigned short,4> item{},paidItem{};
            if(!take(1,&id)||!take(1,&flags)||!take(4,freeOwners.data())||!take(4,paidOwners.data()))
            {Check(false,"matrix case ownership header fits");return;}
            if(id!=n+1 || flags!=(n==0?0:n==1?2:n==2?7:6))
            {Check(false,"matrix fixture retains the four requested membership/manual/paid conditions");return;}
            for(unsigned owner=0;owner<4;++owner)
                if(!take(4,&gold[owner])||!take(2,&item[owner])||!take(2,&paidItem[owner]))
                {Check(false,"matrix frozen owner contents fit");return;}
            unsigned size=0;
            if(!take(4,&size)||size<47||size>101||at>file.size()||size>file.size()-at||file[at]!=1)
            {Check(false,"matrix actual publication is bounded and successful");return;}
            std::vector<unsigned char> body(file.begin()+at+1,file.begin()+at+size);at+=size;
            for(unsigned observer=0;observer<4;++observer)
            {
                ObserverSlot=observer;Reset();
                // Reproduce the receiver's initial preview cached for every
                // possible owner, including an empty receiver preview.
                for(unsigned owner=0;owner<4;++owner)
                {
                    const auto vec=Addr(State.screen)+0x1b8+64*owner;
                    At<void*>(vec+8)=State.records[owner][0].data();
                    NativeAdd(State.screen.data(),0,gold[observer],owner,0);
                    if(item[observer])NativeAdd(State.screen.data(),item[observer],0,owner,0);
                }
                const auto before=State.records;auto current=body;Replay(current);
                for(unsigned col=0;col<4;++col)
                {
                    const auto owner=freeOwners[col];
                    Check(owner<4,"matrix free card has a real owner");if(owner>=4)continue;
                    CheckReward(owner,gold[owner],item[owner]);
                    Check(At<int>(Addr(State.screen)+0x2cc+col*4)==owner,
                        "all four observer contexts retain the actual cross-selected free owner");
                    const auto buyer=paidOwners[col];
                    Check(At<int>(Addr(State.screen)+0x304+col*4)==(buyer<4?buyer:-1),
                        "all four observer contexts retain actual paid ownership, including unselected cards");
                    if(buyer<4)
                        Check(NativeCount(State.screen.data(),buyer,3)==(paidItem[buyer]?2u:1u) &&
                            State.records[buyer][3][0].item==0 && State.records[buyer][3][0].value==0 &&
                            (!paidItem[buyer]||State.records[buyer][3][1].item==paidItem[buyer]),
                            "original paid builder retains this buyer's actual item or empty award");
                }
                for(unsigned owner=0;owner<4;++owner)for(unsigned group=1;group<=2;++group)
                    Check(std::memcmp(State.records[owner][group].data(),before[owner][group].data(),sizeof(State.records[owner][group]))==0,
                        "matrix free/paid updates preserve protected nonfree cached records");
            }
            std::printf("C2 offline matrix R%02u: four observer contexts; original free/paid parser and cache checks executed\n",id);
        }
        Check(at==file.size(),"matrix fixture consumed exactly");ObserverSlot=0;
    }
    void FourPersonRegression()
    {
        Reset();
        auto body = Wire({Card{0,5,0},Card{1,6,0},Card{2,6,13201},Card{3,5,35901}});
        Replay(body);
        for (unsigned owner = 0; owner < 4; ++owner)
            Check(At<int>(Addr(State.screen) + 0x2cc + owner*4) == static_cast<int>(owner), "the actual original loop retains each free column owner");
        CheckReward(0,5,0); CheckReward(1,6,0); CheckReward(2,6,13201); CheckReward(3,5,35901);
        Check(At<unsigned>(0xe23eec) == 34 && At<unsigned char*>(0xe23ee8) == body.data()+12,
            "the actual original readers consume only the legacy prefix and retain the display trailer inside this frame");
        std::printf("C2 cards original-loop free gold/item: %u/%u %u/%u %u/%u %u/%u; remaining=%u\n",
            State.records[0][0][0].value,ActiveItem(0),State.records[1][0][0].value,ActiveItem(1),
            State.records[2][0][0].value,ActiveItem(2),State.records[3][0][0].value,ActiveItem(3),At<unsigned>(0xe23eec));
    }
#ifndef CHAPTER_TWO_CARDS_BASELINE
    unsigned Writes = 0, FailAt = 0;
    bool FailAfter = false;
    bool Write(std::uintptr_t address, const void* data, std::size_t size)
    {
        ++Writes;
        if (Writes == FailAt && !FailAfter) return false;
        std::memcpy(reinterpret_cast<void*>(address), data, size);
        return Writes != FailAt;
    }
    void CheckInstaller(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::free_card_detail::InstallImage;
        auto image = original;
        Writes = FailAt = 0;
        Check(!dfl::chapter_two::InstallFreeCardDisplay(image.data(), image.size()) && image == original,
            "production installer refuses detached images outside400000 without mutation");
        Check(!InstallImage(nullptr, ImageSize, &Write) && !InstallImage(image.data(), ImageSize-1, &Write) &&
            !InstallImage(image.data(), ImageSize, nullptr) && Writes == 0, "bad image extent or writer refuses before mutation");
        const std::array<std::pair<std::size_t,std::size_t>,8> guards{
            std::pair<std::size_t,std::size_t>{0x18bc6,0x1a6}, {0x55d1f0,0x186}, {0x5566d0,0x65},
            {0xdd20,0x43}, {0x63acc0,0x40}, {0x63ad00,0xb9}, {0x811c1c,4}, {0xfdd2,0x10}};
        for (const auto& range : guards)
            for (std::size_t i=0; i<range.second; ++i)
            {
                image = original; image[range.first+i] ^= 1; const auto before = image; Writes = 0;
                Check(!InstallImage(image.data(), image.size(), &Write) && Writes == 0 && image == before,
                    "each original context signature byte is required before any write");
            }
        for (bool after : {false,true})
        {
            image=original; Writes=0; FailAt=1; FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),&Write) && image==original,
                "writer failure before or after copying restores the original instruction sequence");
        }
        image=original; Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),&Write) && Writes==1, "production installer changes the one complete guarded entry sequence");
        for (std::size_t i=0;i<image.size();++i)
            if (image[i]!=original[i]) Check(i>=0x18bd5 && i<0x18bdb, "no byte outside the six-byte hook site changes");
        Writes=0;
        Check(InstallImage(image.data(),image.size(),&Write) && Writes==0, "complete matching reinstall is a no-write success");
        for (std::size_t i=0;i<6;++i)
        {
            auto mixed=image; mixed[0x18bd5+i]=original[0x18bd5+i];
            if (mixed==image) continue;
            const auto before=mixed; Writes=0;
            Check(!InstallImage(mixed.data(),mixed.size(),&Write) && Writes==0 && mixed==before,
                "partial hook installation is rejected without writing");
        }
        for (const auto& range : guards)
        {
            auto bad=image;bad[range.first]^=1;const auto before=bad;Writes=0;
            Check(!InstallImage(bad.data(),bad.size(),&Write) && Writes==0 && bad==before,
                "a complete hook still requires every other guarded context on reinstall");
        }
    }
    std::vector<unsigned char> FreeBytes()
    {
        std::vector<unsigned char> result;
        for (unsigned owner=0;owner<4;++owner)
        {
            const auto* begin=reinterpret_cast<const unsigned char*>(State.records[owner][0].data());
            result.insert(result.end(),begin,begin+sizeof(State.records[owner][0]));
            const auto* vector=State.screen.data()+0x1b8+owner*64;
            result.insert(result.end(),vector,vector+16);
        }
        return result;
    }
    std::vector<unsigned char> OtherGroupBytes()
    {
        std::vector<unsigned char> result;
        for (unsigned owner=0;owner<4;++owner)
            for (unsigned group=1;group<4;++group)
            {
                const auto* begin=reinterpret_cast<const unsigned char*>(State.records[owner][group].data());
                result.insert(result.end(),begin,begin+sizeof(State.records[owner][group]));
                const auto* vector=State.screen.data()+0x1b8+16*(owner*4+group);
                result.insert(result.end(),vector,vector+16);
            }
        return result;
    }
    void InvokeHookOnly(const unsigned char* frame)
    {
        std::array<unsigned char,5> following{};
        std::memcpy(following.data(),reinterpret_cast<void*>(0x418bdb),following.size());
        Jump(0x418bdb,reinterpret_cast<void*>(&EndCard)); EntryPoint=0x418bd5;
        RunCard(frame,0);
        EntryPoint=0x418bc6;
        std::memcpy(reinterpret_cast<void*>(0x418bdb),following.data(),following.size());
    }
    void SetPacket(const unsigned char* body,std::size_t size)
    {
        At<const unsigned char*>(0xe23ee8)=body;At<unsigned>(0xe23eec)=static_cast<unsigned>(size);
        CurrentFrame={1,74,0,0,0,0};const auto frameSize=static_cast<unsigned>(size+7);
        std::memcpy(CurrentFrame.data()+2,&frameSize,4);
    }
    void HookOnly(std::vector<unsigned char>& body)
    {
        SetPacket(body.data(),body.size());InvokeHookOnly(CurrentFrame.data());
    }
    void CheckPeopleAndSlots()
    {
        for (unsigned mask=1;mask<16;++mask)
            for (bool reversed : {false,true})
            {
                std::array<Card,4> cards{Card{255,0,0},Card{255,0,0},Card{255,0,0},Card{255,0,0}};
                unsigned column=0;
                for (unsigned n=0;n<4;++n)
                {
                    const unsigned owner=reversed?3-n:n;
                    if ((mask&(1u<<owner))==0) continue;
                    cards[3-column++]={static_cast<unsigned char>(owner),111u+owner*111u,
                        static_cast<unsigned short>(owner%2?13201u+owner:0)};
                }
                Reset(); const auto other=OtherGroupBytes();auto body=Wire(cards); Replay(body);
                Check(OtherGroupBytes()==other,"free-card publication preserves all paid and personal vector bytes");
                for (unsigned col=0;col<4;++col)
                    Check(At<int>(Addr(State.screen)+0x2cc+col*4)==(cards[col].owner<4?cards[col].owner:-1),
                        "actual owner setter preserves sparse column-to-owner mapping");
                for (unsigned owner=0;owner<4;++owner)
                    if (mask&(1u<<owner)) CheckReward(owner,111+owner*111,owner%2?13201+owner:0);
                    else CheckReward(owner,5,35901);
                // Repeated publication replaces records, never appends duplicates.
                Replay(body);
                for (unsigned owner=0;owner<4;++owner)
                    if (mask&(1u<<owner)) CheckReward(owner,111+owner*111,owner%2?13201+owner:0);
            }
    }
    void CheckPaidAndLegacy()
    {
        const std::array<Card,4> cards{Card{3,111,0},Card{255,0,0},Card{0,222,13201},Card{255,0,0}};
        for (bool extension : {false,true})
        {
            Reset(); auto body=Wire(cards,extension);
            // Column1 paid owner2: gold777 plus item19999. Insert before column2.
            body[4]=2;body[5]=2;
            body.insert(body.begin()+6,{0,0,9,3,0,0,0x1f,0x4e,0,0,0,0});
            Replay(body);
            Check(NativeCount(State.screen.data(),2,3)==2 && State.records[2][3][0].item==0 &&
                State.records[2][3][0].value==777 && State.records[2][3][1].item==19999,
                "actual original paid parser and builder retain both paid records");
            Check(At<int>(Addr(State.screen)+0x304+4)==2, "native paid-owner setter retains column-to-owner mapping");
            CheckReward(0,extension?222:5,extension?13201:35901);
            CheckReward(3,extension?111:5,extension?0:35901);
            Check(At<unsigned>(0xe23eec)==(extension?34u:0u) && At<unsigned char*>(0xe23ee8)==body.data()+24,
                "paid variable-length records consume their original prefix without advancing into the trailer");
        }
        Reset(); auto zero=Wire({Card{0,0,0},Card{255,0,0},Card{255,0,0},Card{255,0,0}}); Replay(zero);
        CheckReward(0,0,0);
        Reset();auto extrema=Wire({Card{0,0xffffffff,0xffff},Card{1,0x80000000,0},Card{255,0,0},Card{255,0,0}});
        Replay(extrema);CheckReward(0,0xffffffff,0xffff);CheckReward(1,0x80000000,0);
        Reset();auto empty=Wire({Card{255,0,0},Card{255,0,0},Card{255,0,0},Card{255,0,0}});
        const auto noOwners=FreeBytes();Replay(empty);
        Check(FreeBytes()==noOwners,"a valid all-unassigned trailer makes no free-cache changes");
    }
    std::vector<unsigned char> MaximumBody()
    {
        std::vector<unsigned char> body;
        for (unsigned col=0;col<4;++col)
        {
            body.insert(body.end(),{static_cast<unsigned char>(col),static_cast<unsigned char>(3-col),2});
            const std::array<unsigned char,6> gold{0,0,static_cast<unsigned char>((700+col)&255),
                static_cast<unsigned char>((700+col)>>8),0,0};
            body.insert(body.end(),gold.begin(),gold.end());
            const std::array<unsigned char,6> item{static_cast<unsigned char>((10000+col)&255),
                static_cast<unsigned char>((10000+col)>>8),0,0,0,0};
            body.insert(body.end(),item.begin(),item.end());
        }
        const auto standard=Wire({Card{0,5,0},Card{1,6,0},Card{2,6,13201},Card{3,5,35901}});
        body.insert(body.end(),standard.begin()+12,standard.end());
        return body;
    }
    void CheckMaximumBody()
    {
        Reset();auto body=MaximumBody();Check(body.size()==94,"maximum legal body contains all eight paid records and one trailer");
        Replay(body);
        CheckReward(0,5,0);CheckReward(1,6,0);CheckReward(2,6,13201);CheckReward(3,5,35901);
        for (unsigned col=0;col<4;++col)
        {
            const auto owner=3-col;
            Check(NativeCount(State.screen.data(),owner,3)==2 && State.records[owner][3][0].value==700+col &&
                State.records[owner][3][1].item==10000+col,"maximum-length prefix preserves every owner's actual native paid rows");
        }
        Check(At<unsigned char*>(0xe23ee8)==body.data()+60 && At<unsigned>(0xe23eec)==34,
            "maximum prefix stops exactly60 bytes into its frame before the34-byte extension");
    }
    void Rejected(std::vector<unsigned char> body)
    {
        Reset(); const auto before=FreeBytes(); HookOnly(body);
        Check(FreeBytes()==before, "malformed or absent extension leaves every free vector byte unchanged");
        Check(At<unsigned char*>(0xe23ee8)==body.data() && At<unsigned>(0xe23eec)==body.size(),
            "rejected input leaves both original packet cursor globals unchanged");
    }
    void CheckMalformed()
    {
        const auto valid=Wire({Card{0,5,0},Card{1,6,0},Card{2,6,13201},Card{3,5,35901}});
        for (std::size_t size=0;size<valid.size();++size) Rejected({valid.begin(),valid.begin()+size});
        for (unsigned byte : {12u,16u,17u,18u})
        {auto bad=valid;bad[byte]^=0x80;Rejected(bad);}
        auto bad=valid;bad.push_back(0);Rejected(bad);
        bad=valid;bad[0]=4;Rejected(bad);
        bad=valid;bad[3]=0;Rejected(bad); // duplicate owner
        bad=valid;bad[1]=4;Rejected(bad); // bad paid owner
        bad=valid;bad[2]=3;Rejected(bad); // too many paid records
        bad=valid;bad[18]=0;Rejected(bad); // assigned/owner mismatch
        bad=valid;bad[0]=255;Rejected(bad);
        bad=valid;bad[0]=255;bad[18]=0;Rejected(bad); // unassigned carries gold
        Reset(); auto body=valid;At<unsigned>(Addr(State.screen))^=1; const auto before=FreeBytes();HookOnly(body);
        Check(FreeBytes()==before, "wrong reward-window RTTI vtable refuses all cache mutation");
        Reset(); At<unsigned>(Addr(State.screen)+0x1c0+3*64)+=1; const auto invalid=FreeBytes();HookOnly(body);
        Check(FreeBytes()==invalid, "all owner vectors are checked before any earlier owner is changed");
        Reset();SetPacket(nullptr,46);
        // A null cursor is also rejected by the production hook under its SEH boundary.
        const auto unchanged=FreeBytes();
        InvokeHookOnly(CurrentFrame.data());
        Check(FreeBytes()==unchanged, "null cursor does not modify the cache");
    }
    void CheckAbiAndFrameBounds()
    {
        auto body=Wire({Card{0,5,0},Card{1,6,0},Card{2,6,13201},Card{3,5,35901}});
        Reset();const auto other=OtherGroupBytes();InputEbx=0x66778899;HookOnly(body);InputEbx=0;
        Check(SeenEax==0x11112222 && SeenEbx==0x66778899 && SeenEcx==0x22223333 && SeenEdx==0x33334444 &&
            SeenEsi==0x44445555 && SeenEdi==0x55556666,"compiled entry hook preserves every live general-purpose register");
        Check(SeenEsp==BeforeEsp && SeenEbp==BeforeEbp && (SeenFlags&0x8d5)==0x8d5 && SeenLocal==0x66778899,
            "compiled entry hook balances the stack, preserves flags/frame and replays its displaced local store");
        Check(OtherGroupBytes()==other,"the production hook alone leaves every non-free vector byte unchanged");
        Check(At<const unsigned char*>(0xe23ee8)==body.data() && At<unsigned>(0xe23eec)==46,
            "successful hook does not advance either native cursor global");
        for (unsigned which=0;which<10;++which)
        {
            Reset();SetPacket(body.data(),body.size());const auto before=FreeBytes();
            switch (which)
            {
            case 0:CurrentFrame[0]=0;break;
            case 1:CurrentFrame[1]=73;break;
            case 2:At<unsigned>(Addr(CurrentFrame)+2)=52;break;
            case 3:At<unsigned>(Addr(CurrentFrame)+2)=102;break;
            case 4:At<unsigned>(Addr(CurrentFrame)+2)=0xffffffff;break;
            case 5:At<unsigned>(0xe23eec)=45;break;
            case 6:At<unsigned>(0xe23eec)=47;break;
            case 7:At<void*>(0xd56d24)=nullptr;break;
            case 8:At<void*>(Addr(State.manager)+0x1278)=nullptr;break;
            case 9:break;
            }
            const auto* cursor=At<const unsigned char*>(0xe23ee8);const auto remaining=At<unsigned>(0xe23eec);
            InvokeHookOnly(which==9?nullptr:CurrentFrame.data());
            Check(FreeBytes()==before,"wrong frame, length, remaining count or missing native owner refuses all cache writes");
            Check(At<const unsigned char*>(0xe23ee8)==cursor && At<unsigned>(0xe23eec)==remaining,
                "frame-boundary rejection preserves both cursor globals");
        }
        SYSTEM_INFO info{};GetSystemInfo(&info);const auto page=info.dwPageSize;
        auto* pages=static_cast<unsigned char*>(VirtualAlloc(nullptr,page*3,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        Check(pages!=nullptr,"bounded fixture allocates guard pages");if (!pages) return;
        DWORD previous=0;
        Check(VirtualProtect(pages,page,PAGE_NOACCESS,&previous) && VirtualProtect(pages+page*2,page,PAGE_NOACCESS,&previous),
            "body fixture has inaccessible pages on both sides");
        auto* end=pages+page*2;
        for (bool front : {false,true})
        {
            Reset();auto* guarded=front?pages+page:end-body.size();std::memcpy(guarded,body.data(),body.size());
            SetPacket(guarded,body.size());InvokeHookOnly(CurrentFrame.data());
            CheckReward(0,5,0);CheckReward(1,6,0);CheckReward(2,6,13201);CheckReward(3,5,35901);
            Check(At<const unsigned char*>(0xe23ee8)==guarded && At<unsigned>(0xe23eec)==46,
                "exact front/end guard-page body is read within bounds without cursor changes");
        }
        Reset();auto* incomplete=end-body.size()+1;std::memcpy(incomplete,body.data(),body.size()-1);SetPacket(incomplete,body.size());
        const auto inaccessible=FreeBytes();InvokeHookOnly(CurrentFrame.data());
        Check(FreeBytes()==inaccessible,"an inaccessible final trailer byte is rejected before any vector mutation");
        Reset();auto maximum=MaximumBody();auto* guardedMaximum=end-maximum.size();std::memcpy(guardedMaximum,maximum.data(),maximum.size());
        SetPacket(guardedMaximum,maximum.size());InvokeHookOnly(CurrentFrame.data());
        CheckReward(0,5,0);CheckReward(1,6,0);CheckReward(2,6,13201);CheckReward(3,5,35901);
        VirtualFree(pages,0,MEM_RELEASE);
        Reset();auto twoFrames=body;twoFrames.insert(twoFrames.end(),{1,12,9,0,0,0,0xab,0xcd,0xef});
        const auto following=twoFrames;SetPacket(twoFrames.data(),body.size());InvokeHookOnly(CurrentFrame.data());
        Check(twoFrames==following && At<const unsigned char*>(0xe23ee8)==twoFrames.data() && At<unsigned>(0xe23eec)==46,
            "following-frame sentinel remains untouched and excluded by the current header/cursor bound");
    }
    unsigned Released = 0;
    void __fastcall ReleaseRecord(void*,void*,unsigned flags)
    {Check(flags==1,"native record destructor retains its release flag");++Released;}
    void CheckOriginalDestructorAndVectorBounds()
    {
        auto body=Wire({Card{0,5,0},Card{1,6,0},Card{2,6,13201},Card{3,5,35901}});
        void* methods[3]{nullptr,nullptr,reinterpret_cast<void*>(&ReleaseRecord)};void** reference=methods;
        Reset();Released=0;
        for (unsigned owner=0;owner<4;++owner)
            At<void*>(Addr(State.records[owner][0][0])+0x30)=&reference;
        HookOnly(body);Check(Released==4,"real C2 record destructor releases every replaced owner record exactly once");
        for (unsigned which=0;which<7;++which)
        {
            Reset();auto* v=reinterpret_cast<unsigned*>(State.screen.data()+0x1bc+3*64);
            switch(which)
            {
            case 0:v[0]+=1;break;
            case 1:v[1]=v[0]-4;break;
            case 2:v[2]=v[1]-4;break;
            case 3:v[1]=v[0]+3*56;break;
            case 4:v[2]=v[0]+17*56;break;
            case 5:v[2]+=1;break;
            case 6:v[0]=0;break;
            }
            const auto before=FreeBytes();HookOnly(body);
            Check(FreeBytes()==before,"unaligned, reversed, oversized or inconsistent late-owner vector refuses every owner update");
        }
        // An empty native vector is valid. Exercise its original allocation,
        // grow/copy path with only the external CRT heap replaced by this EXE.
        Reset();Jump(0xa5da4a,reinterpret_cast<void*>(&Allocate));Jump(0xa5f27d,reinterpret_cast<void*>(&Deallocate));
        auto* vector=reinterpret_cast<unsigned*>(State.screen.data()+0x1bc);
        vector[0]=vector[1]=vector[2]=0;HookOnly(body);
        Check(NativeCount(State.screen.data(),0,0)==1 && At<unsigned>(vector[0])==0 && At<unsigned>(vector[0]+0x28)==5,
            "real native vector allocation and insertion handle a previously empty free cache");
        std::free(reinterpret_cast<void*>(vector[0]));vector[0]=vector[1]=vector[2]=0;
    }
    void CheckActualPacket(const wchar_t* path)
    {
        std::ifstream stream(path,std::ios::binary);const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)),{});
        Check(file.size()==47 && file[0]==1,"actual server CMD74 success fixture has the observed47-byte payload");
        if (file.size()!=47 || file[0]!=1) return;
        Reset();std::vector<unsigned char> body(file.begin()+1,file.end());Replay(body);
        CheckReward(0,5,0);CheckReward(1,6,0);CheckReward(2,6,13201);CheckReward(3,5,35901);
    }
#endif
}

int wmain(int argc, wchar_t** argv)
{
    const bool matrix=argc==4 && std::wcscmp(argv[2],L"--matrix")==0;
    if (argc != 2 && argc != 3 && !matrix) return 2;
    const auto image = ReadImage(argv[1]);
    Check(image.size() == ImageSize, "fixture loads the actual original C2 PE");
    if (image.empty()) return 2;
#ifndef CHAPTER_TWO_CARDS_BASELINE
    CheckInstaller(image);
#endif
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
        MEM_RESERVE|MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        DWORD old = 0; const auto reserve = reinterpret_cast<std::uintptr_t>(cardFixtureReserve);
        if (reserve <= Base && Base + ImageSize <= reserve + sizeof(cardFixtureReserve) &&
            VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &old)) mapped = reinterpret_cast<unsigned char*>(Base);
    }
    Check(mapped == reinterpret_cast<void*>(Base), "fixture maps original native instructions at400000");
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
#ifndef CHAPTER_TWO_CARDS_BASELINE
    Check(dfl::chapter_two::InstallFreeCardDisplay(mapped, ImageSize), "compiled production patch installs on the original mapped C2 image");
#endif
    PrepareDependencies();
    FourPersonRegression();
#ifndef CHAPTER_TWO_CARDS_BASELINE
    CheckPeopleAndSlots();CheckPaidAndLegacy();CheckMaximumBody();CheckMalformed();
    CheckAbiAndFrameBounds();CheckOriginalDestructorAndVectorBounds();
    if (argc==3) CheckActualPacket(argv[2]);
#endif
    if(matrix)CheckMatrix(argv[3]);
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two free card display: %u checks, %d failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
