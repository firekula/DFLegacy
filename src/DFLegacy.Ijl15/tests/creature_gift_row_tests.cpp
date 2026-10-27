// Exercise the production updater against independent literal ACT1 fixtures.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "creature_gift_row.h"
#include "memory_patch.h"
#include <array>
#include <cstdio>
#include <cstring>

namespace
{
    using dfl::creature_gift_row::detail::UpdateRow;
    bool Check(bool value,const char* message)
    {
        if(!value)std::fprintf(stderr,"FAIL: creature row gift: %s\n",message);
        return value;
    }
    template<class T> void Put(unsigned char* at,T value){std::memcpy(at,&value,sizeof(value));}
    void* StubEntry{};
    void* HarnessShop{};
    void* HarnessCommodity{};
    void* HarnessButton{};
    unsigned BeforeEsp{},AfterEsp{},BeforeFlags{},AfterFlags{};
    unsigned AfterEax{},AfterEbx{},AfterEcx{},AfterEdx{},AfterEsi{},AfterEdi{},AfterEbp{};
    unsigned HelperFlags{};
    __declspec(naked) void CaptureContinuation()
    {
        __asm
        {
            mov AfterEax,eax
            mov AfterEbx,ebx
            mov AfterEcx,ecx
            mov AfterEdx,edx
            mov AfterEsi,esi
            mov AfterEdi,edi
            mov AfterEbp,ebp
            mov AfterEsp,esp
            pushfd
            pop AfterFlags
            cld
            ret
        }
    }
    __declspec(naked) void RunStubHarness()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp,esp
            push 0ED7h
            popfd
            mov eax,11223344h
            mov ebx,22334455h
            mov ecx,33445566h
            mov edx,HarnessButton
            mov esi,HarnessShop
            mov edi,HarnessCommodity
            mov ebp,44556677h
            pushfd
            pop BeforeFlags
            call StubEntry
            popad
            popfd
            ret
        }
    }
    void __cdecl ObservedUpdate(void* shop,void* commodity,void* button)
    {
        __asm { pushfd }
        __asm { pop HelperFlags }
        UpdateRow(shop,commodity,button);
    }
    bool TestStub(void* shop,void* commodity,void* button)
    {
        using namespace dfl::creature_gift_row::detail;
        auto* memory=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
        if(!memory)return Check(false,"executable fixture allocation");
        const auto code=BuildStub(reinterpret_cast<std::uintptr_t>(memory),
            reinterpret_cast<std::uintptr_t>(&ObservedUpdate),reinterpret_cast<std::uintptr_t>(&CaptureContinuation));
        // Avoid executing a missing/broken RED stub; still fail the behavior test.
        bool ok=Check(code[0]==0x9C&&code[1]==0x60&&code[16]==0xE9,"production stub is executable preserving trampoline");
        if(ok)
        {
            std::memcpy(memory,code.data(),code.size());
            DWORD old{};
            ok &= Check(VirtualProtect(memory,4096,PAGE_EXECUTE_READ,&old)!=0,"test stub executable protection");
            FlushInstructionCache(GetCurrentProcess(),memory,code.size());
            if(ok)
            {
                StubEntry=memory;HarnessShop=shop;HarnessCommodity=commodity;HarnessButton=button;
                RunStubHarness();
                ok &= Check(AfterEax==0x11223344&&AfterEbx==0x22334455&&AfterEcx==0x33445566
                    &&AfterEdx==reinterpret_cast<unsigned>(button)&&AfterEsi==reinterpret_cast<unsigned>(shop)
                    &&AfterEdi==reinterpret_cast<unsigned>(commodity)&&AfterEbp==0x44556677
                    &&AfterEsp==BeforeEsp-4&&AfterFlags==BeforeFlags,
                    "actual emitted stub preserves all registers, EFLAGS/DF and ESP and reaches continuation");
                ok &= Check((HelperFlags&0x400)==0,"actual helper sees clearedDF while callerDF is preserved");
            }
        }
        VirtualFree(memory,0,MEM_RELEASE);return ok;
    }
    unsigned PatchWrites{};
    bool WriteFixture(std::uintptr_t address,const void* bytes,std::size_t size)
    {
        ++PatchWrites;return dfl::memory_patch::WriteBytes(address,bytes,size);
    }
    bool FailAfterWrite(std::uintptr_t address,const void* bytes,std::size_t size)
    {
        ++PatchWrites;
        const bool written=dfl::memory_patch::WriteBytes(address,bytes,size);
        return PatchWrites!=1&&written;
    }
    bool TestInstaller()
    {
        using namespace dfl::creature_gift_row::detail;
        auto* image=static_cast<unsigned char*>(VirtualAlloc(nullptr,0x470000,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(!image)return Check(false,"image fixture allocation");
        const unsigned char prefix[]={0x8B,0x8E,0xE0,0x01,0,0,0x8B,0x45,0xF8,0x8B,0x14,0x01,
            0xC6,0x42,0x10,0x01,0x8B,0x8E,0xE0,0x01,0,0,0x8B,0x54,0x01,0x04};
        const unsigned char original[]={0xC6,0x42,0x11,0,0xEB,0x19};
        const unsigned char suffix[]={0x8B,0x45,0xFC,0x8B,0x7D,0x08,0x83,0xC0,0x04};
        std::memcpy(image+0x4636A4,prefix,sizeof(prefix));
        std::memcpy(image+0x4636BE,original,sizeof(original));
        std::memcpy(image+0x4636DD,suffix,sizeof(suffix));
        const auto base=reinterpret_cast<std::uintptr_t>(image);
        const auto stub=base+0x460000;
        bool ok=true;
        // Every byte in each ABI signature participates in fail-closed install.
        const unsigned offsets[]={0x4636A4,0x4636BE,0x4636DD};
        const unsigned lengths[]={sizeof(prefix),sizeof(original),sizeof(suffix)};
        for(unsigned part=0;part<3;++part)for(unsigned i=0;i<lengths[part];++i)
        {
            image[offsets[part]+i]^=1;PatchWrites=0;
            ok &= Check(!InstallImage(base,stub,WriteFixture)&&PatchWrites==0,"signature mismatch performs no write");
            image[offsets[part]+i]^=1;
        }
        PatchWrites=0;
        ok &= Check(!InstallImage(base,stub,FailAfterWrite)&&PatchWrites==2
            &&std::memcmp(image+0x4636BE,original,6)==0,"writer failure after mutation restores original complete span");
        PatchWrites=0;
        ok &= Check(InstallImage(base,stub,WriteFixture)&&PatchWrites==1,"valid exact image installs single complete JMP6 span");
        std::uint32_t delta{};std::memcpy(&delta,image+0x4636BF,4);
        ok &= Check(image[0x4636BE]==0xE9&&image[0x4636C3]==0x90
            &&base+0x4636C3+delta==stub,"installed relative jump targets supplied stub with trailing NOP");
        ok &= Check(InstallImage(base,stub,WriteFixture)&&PatchWrites==1,"repeat installation does not rewrite own hook");
        ok &= Check(!InstallImage(base,stub+1,WriteFixture)&&PatchWrites==1,"foreign target is never overwritten");
        ok &= Check(std::memcmp(image+0x4636A4,prefix,sizeof(prefix))==0
            &&std::memcmp(image+0x4636DD,suffix,sizeof(suffix))==0,"installer preserves all neighboring native instructions");
        VirtualFree(image,0,MEM_RELEASE);return ok;
    }
    struct Row
    {
        std::array<unsigned char,0x3500> Shop{};
        std::array<unsigned char,88> Commodity{};
        std::array<unsigned char,32> Button{};
        std::array<unsigned char,72> Controls{};
        std::array<unsigned char,8> Item{};
        Row(unsigned commodity=105009,unsigned item=63007,unsigned cash=6600)
        {
            Put(Shop.data()+84,3);Put(Shop.data()+13064,0);
            Put(Shop.data()+456,Commodity.data());
            Put(Shop.data()+480,Controls.data());
            Put(Controls.data()+4,Button.data());
            Put(Commodity.data(),commodity);Put(Commodity.data()+4,3);
            Put(Commodity.data()+12,item);Commodity[16]=255;
            Put(Commodity.data()+20,1);Put(Commodity.data()+40,cash);
            Put(Commodity.data()+80,Item.data());
            Button[16]=1;
        }
        void Refresh(){UpdateRow(Shop.data(),Commodity.data(),Button.data());}
    };
    bool TestLiveNativePriceRecords()
    {
        // Actual PID52752 snapshot rows105010/105009/105008; price32/36=0, price40=cash.
        // Normalize only the process-local materialized pointer at+80.
        constexpr unsigned char records[][84]={
            {0x32,0x9A,0x01,0x00,0x03,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x25,0xF6,0x00,0x00,0xFF,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xAC,0x26,0x00,0x00,0xFF,0xD4,0x12,0x18,0x00,0x00,0x00,0x00,0xB3,0xE8,0xCE,0xEF,0xB5,0xB0,0x20,0x28,0xB2,0xA8,0xB5,0xCC,0xCB,0xB9,0x29,0x00,0x0F,0x00,0x00,0x00,0x0F,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0xD8,0x78,0x15,0x07},
            {0x31,0x9A,0x01,0x00,0x03,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x1F,0xF6,0x00,0x00,0xFF,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xC8,0x19,0x00,0x00,0xFF,0xD4,0x12,0x18,0x00,0x00,0x00,0x00,0xB3,0xE8,0xCE,0xEF,0xB5,0xB0,0x20,0x28,0xC7,0xA1,0xBF,0xCB,0xCB,0xB9,0x29,0x00,0x0F,0x00,0x00,0x00,0x0F,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0xB0,0x8E,0x15,0x07},
            {0x30,0x9A,0x01,0x00,0x03,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x1E,0xF6,0x00,0x00,0xFF,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xAC,0x26,0x00,0x00,0xFF,0xFE,0xCE,0xF1,0xC6,0xF7,0xC0,0xAE,0xB3,0xE8,0xCE,0xEF,0xB5,0xB0,0x20,0x28,0xB7,0xF0,0xC0,0xAD,0xCB,0xB9,0x29,0x00,0x0F,0x00,0x00,0x00,0x0F,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x63,0x15,0x07},
        };
        bool ok=true;
        for(const auto& raw:records)
        {
            Row row;std::memcpy(row.Commodity.data(),raw,sizeof(raw));
            Put(row.Commodity.data()+80,row.Item.data());
            const auto record=row.Commodity;
            row.Refresh();
            ok &= Check(row.Button[17]==1&&row.Commodity==record,
                "actual native record price at+40 enables eligible row without changing record");
        }
        Row swapped;std::memcpy(swapped.Commodity.data(),records[1],84);
        Put(swapped.Commodity.data()+80,swapped.Item.data());
        Put(swapped.Commodity.data()+32,6600u);Put(swapped.Commodity.data()+40,0u);
        swapped.Button[17]=1;swapped.Refresh();
        ok &= Check(swapped.Button[17]==0,"old mistaken price-at+32 layout is rejected");
        return ok;
    }
}

bool TestCreatureGiftRow()
{
    // Independent PVF/recon literals; removing a tuple or weakening any guard
    // must change these real button-state outcomes.
    constexpr unsigned tuples[][3]={
        {105008,63006,9900},{105009,63007,6600},{105010,63013,9900},
        {105011,63014,9900},{105012,63015,9900},{105013,63016,9900},
        {105014,63017,9900},{105015,63018,9900},{105016,63022,6600},
        {105017,63024,6600},{105018,63028,9900},{105019,63031,9900},
        {105020,63035,13200},{105028,63134,9900},{105030,63136,13200}};
    bool ok=TestLiveNativePriceRecords();
    for(const auto& t:tuples)
    {
        Row row(t[0],t[1],t[2]);
        const auto shop=row.Shop; const auto record=row.Commodity; const auto controls=row.Controls;
        auto button=row.Button;button[17]=1;
        row.Refresh();
        ok &= Check(row.Button==button&&row.Shop==shop&&row.Commodity==record&&row.Controls==controls,
            "verified egg row enables only its gift byte and mutates no shop or commodity data");
    }
    for(int bad=0;bad<23;++bad)
    {
        Row row;row.Button[17]=1;
        switch(bad)
        {
        case 0:Put(row.Shop.data()+84,2);break;
        case 1:Put(row.Shop.data()+13064,1);break;
        case 2:Put(row.Commodity.data()+4,2);break;
        case 3:Put(row.Commodity.data(),105005u);Put(row.Commodity.data()+12,25u);break;
        case 4:Put(row.Commodity.data(),999999u);break;
        case 5:Put(row.Commodity.data()+12,63006u);break;
        case 6:Put(row.Commodity.data()+40,6601u);break;
        case 7:Put(row.Commodity.data()+20,2);break;
        case 8:row.Commodity[16]=0;break;
        case 9:Put(row.Commodity.data()+26,static_cast<unsigned short>(1));break;
        case 10:row.Commodity[76]=1;break;
        case 11:row.Commodity[24]=99;break;
        case 12:Put(row.Commodity.data()+8,1);break;
        case 13:Put(row.Commodity.data()+32,1);break;
        case 14:Put(row.Commodity.data()+36,1);break;
        case 15:Put(row.Commodity.data()+80,static_cast<void*>(nullptr));break;
        case 16:Put(row.Shop.data()+456,static_cast<void*>(nullptr));break;
        case 17:Put(row.Controls.data()+4,static_cast<void*>(nullptr));break;
        case 18:Put(row.Shop.data()+456+4,row.Commodity.data());Put(row.Shop.data()+456,static_cast<void*>(nullptr));break;
        case 19:Put(row.Commodity.data()+12,24u);Put(row.Commodity.data(),105001u);break;
        case 20:Put(row.Commodity.data()+80,reinterpret_cast<void*>(1));break;
        case 21:Put(row.Shop.data()+480,reinterpret_cast<void*>(1));break;
        case 22:Put(row.Commodity.data()+20,0);break;
        }
        row.Refresh();
        ok &= Check(row.Button[17]==0,"wrong, disallowed or stale row overwrites prior enabled state with disabled");
    }
    // Every current row is supported, but a seventh entry cannot be borrowed.
    for(unsigned index=0;index<7;++index)
    {
        Row row;row.Shop[456]=0;Put(row.Shop.data()+456,static_cast<void*>(nullptr));
        Put(row.Controls.data()+4,static_cast<void*>(nullptr));
        Put(row.Shop.data()+456+4*index,row.Commodity.data());
        if(index<6)Put(row.Controls.data()+12*index+4,row.Button.data());
        row.Refresh();ok &= Check(row.Button[17]==(index<6?1:0),"only six associated current rows are eligible");
    }
    Row selected;
    selected.Refresh();
    Put(selected.Shop.data()+13064,1);selected.Refresh();
    ok &= Check(selected.Button[17]==0,"tab transition clears prior eligible row state");
    Put(selected.Shop.data()+13064,0);selected.Refresh();
    ok &= Check(selected.Button[17]==1,"returning to valid egg tab recalculates eligibility");
    // A readable button must still be disabled when the rest of the row cannot
    // be read. A failed button write is only fault containment, not proof of0.
    selected.Button[17]=1;
    UpdateRow(selected.Shop.data(),reinterpret_cast<void*>(1),selected.Button.data());
    ok &= Check(selected.Button[17]==0,"unreadable record cannot leave old enabled byte");
    // Match the inaccessible record in the current row so association succeeds
    // and the production record read itself reaches the SEH guard.
    Row unreadableRecord;
    void* inaccessibleRecord=reinterpret_cast<void*>(1);
    Put(unreadableRecord.Shop.data()+456,inaccessibleRecord);
    unreadableRecord.Button[17]=1;
    ok &= Check(UpdateRow(unreadableRecord.Shop.data(),inaccessibleRecord,unreadableRecord.Button.data())
        &&unreadableRecord.Button[17]==0,"associated unreadable record fault clears the readable button");
    selected.Button[17]=1;
    UpdateRow(reinterpret_cast<void*>(1),selected.Commodity.data(),selected.Button.data());
    ok &= Check(selected.Button[17]==0,"unreadable shop still clears readable button");
    ok &= Check(!UpdateRow(selected.Shop.data(),selected.Commodity.data(),nullptr)
        &&!UpdateRow(selected.Shop.data(),selected.Commodity.data(),reinterpret_cast<void*>(1)),"unwritable buttons are contained and reported");
    selected.Button[17]=0;
    ok &= TestStub(selected.Shop.data(),selected.Commodity.data(),selected.Button.data());
    ok &= Check(selected.Button[17]==1,"executed production stub updates the actual eligible row");
    ok &= TestInstaller();
    return ok;
}
