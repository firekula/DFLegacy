#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "countdown_coin.h"
#include "memory_patch.h"
#include <array>
#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace
{
    using namespace dfl::countdown_coin::detail;
    bool Check(bool value, const char* message)
    {
        if (!value) std::fprintf(stderr, "FAIL: countdown coin: %s\n", message);
        return value;
    }
    template<class T> void Put(unsigned char* at, T value) { std::memcpy(at, &value, sizeof(value)); }
    struct Fixture
    {
        std::array<unsigned char, 0xF28> Ui{};
        std::array<unsigned char, 0x3500> Shop{};
        std::array<unsigned char, 112> Countdown{};
        Fixture()
        {
            Put(Ui.data()+0xF24, Shop.data()); Put(Ui.data()+0xF08, Countdown.data());
            Put(Shop.data(), 0xA7D504u); Put(Countdown.data(), 0xA7CF2Cu);
            Put(Countdown.data()+96, 1); Shop[9680]=1;
            // Literal original A row through its scalar price fields. Padding
            // is intentionally different below for the observed B row.
            const unsigned char row[]={0xA4,0x86,1,0,1,0,0,0,0,0,0,0,1,0,0,0,
                0xFF,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,100,0,0,0};
            std::memcpy(Shop.data()+0x341C,row,sizeof(row));
        }
        unsigned char* Row() { return Shop.data()+0x341C; }
    };
    void* ExpectedCollection{}; void* ExpectedFont{}; void* ExpectedRenderer{};
    unsigned char Sprite37{}, Sprite43{};
    unsigned DrawCalls{}, FontDepth{}; int LastPrice{}, LastX{}, LastY{};
    bool CallbackArguments{};
    bool FailText{};
    void* __fastcall SpriteBridge(void* collection, void*, int index)
    {
        CallbackArguments &= collection==ExpectedCollection && index==37;
        return &Sprite37;
    }
    void __fastcall FontBridge(void* renderer, void*, void* font)
    {
        CallbackArguments &= renderer==ExpectedRenderer && font==ExpectedFont;
        ++FontDepth;
    }
    void __cdecl TextBridge(void* renderer, int x, int y, int color, const char* format, ...)
    {
        va_list values; va_start(values, format); LastPrice=va_arg(values,int); va_end(values);
        CallbackArguments &= renderer==ExpectedRenderer && color==-1 && FontDepth==1;
        // The unmodified death-window background already says "coin price".
        const char expected[]="%d \xC8\xFB\xC0\xAD";
        CallbackArguments &= std::strcmp(format,expected)==0;
        LastX=x; LastY=y; ++DrawCalls;
        if(FailText)RaiseException(EXCEPTION_ACCESS_VIOLATION,0,0,nullptr);
    }
    void __fastcall RestoreBridge(void* renderer, void*)
    {
        CallbackArguments &= renderer==ExpectedRenderer && FontDepth==1;
        --FontDepth;
    }
    bool TestReceiptPolicy()
    {
        Fixture f; Receipt receipt{1,1,100004,-1,65535,0};
        const auto before=f.Shop;
        bool ok=Check(HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"successful pending hidden purchase is handled without mall result");
        ok &= Check(f.Shop==before,"presentation never changes cash,coin,pending or product");
        f.Row()[17]=0x60; f.Row()[25]=0x60; f.Row()[78]=0xFD;
        ok &= Check(HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"native B scalar widths ignore uninitialized padding");
        for (int field=0;field<6;++field)
        {
            auto other=receipt;
            switch(field){case 0:other.Success=0;break;case 1:other.Category=2;break;
                case 2:other.Commodity=100000;break;case 3:other.Error=12;break;
                case 4:other.Slot=2;break;case 5:other.Gift=1;break;}
            ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&other),"unrelated or rejected result stays native");
        }
        for (const unsigned offset : {0u,4u,8u,12u,16u,20u,26u,32u,36u,40u})
        {
            Fixture invalid; invalid.Row()[offset]^=1;
            // commodity and cash may legitimately vary; positive alternate price
            // remains valid only when the reply names that actual commodity.
            if(offset==40)Put(invalid.Row()+40,0);
            ok &= Check(!HandleReceipt(invalid.Ui.data(),invalid.Shop.data(),&receipt),"invalid hidden product scalar fails closed");
        }
        f.Shop[9680]=0;
        ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"no pending request stays native");
        f.Shop[9680]=1; Put(f.Countdown.data()+96,0);
        ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"inactive countdown stays native");
        Put(f.Countdown.data()+96,2);
        ok &= Check(HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"second active countdown state is supported");
        Put(f.Countdown.data(),0xA7CF30u);
        ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"wrong native countdown type stays native");
        Put(f.Countdown.data(),0xA7CF2Cu);Put(f.Shop.data(),0xA7D508u);
        ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"wrong native shop type stays native");
        Put(f.Shop.data(),0xA7D504u);
        Put(f.Ui.data()+0xF24,static_cast<void*>(nullptr));
        ok &= Check(!HandleReceipt(f.Ui.data(),f.Shop.data(),&receipt),"foreign/stale shop ownership stays native");
        ok &= Check(!HandleReceipt(nullptr,nullptr,&receipt)&&!HandleReceipt(reinterpret_cast<void*>(1),f.Shop.data(),&receipt)
            &&!HandleReceipt(f.Ui.data(),f.Shop.data(),nullptr),"invalid readonly pointers fail closed");
        void* inaccessible=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_NOACCESS);
        if(inaccessible)
        {
            ok &= Check(!HandleReceipt(inaccessible,f.Shop.data(),&receipt)
                &&!HandleReceipt(f.Ui.data(),f.Shop.data(),static_cast<Receipt*>(inaccessible)),"inaccessible pages fail closed");
            VirtualFree(inaccessible,0,MEM_RELEASE);
        }
        else ok &= Check(false,"inaccessible page fixture allocation");
        return ok;
    }
    bool TestDrawing()
    {
        Fixture f; unsigned char collection{},renderer{};
        Put(f.Countdown.data()+12,&collection);
        ExpectedCollection=&collection; ExpectedFont=f.Countdown.data()+20; ExpectedRenderer=&renderer;
        Drawing ops{reinterpret_cast<decltype(Drawing::Sprite)>(&SpriteBridge),
            reinterpret_cast<decltype(Drawing::SelectFont)>(&FontBridge),&TextBridge,
            reinterpret_cast<decltype(Drawing::RestoreFont)>(&RestoreBridge)};
        PriceDraw draw{328,304,&Sprite37};
        DrawCalls=0;FontDepth=0;CallbackArguments=true;
        bool ok=Check(DrawPrice(f.Ui.data(),&renderer,&draw,100,ops),"normal price sprite is replaced by actual effective price");
        ok &= Check(DrawCalls==1&&LastPrice==100&&LastX==328&&LastY==304&&FontDepth==0&&CallbackArguments,
            "native text receives correct price,position,renderer,font and balanced font state");
        draw.Sprite=&Sprite43;
        ok &= Check(!DrawPrice(f.Ui.data(),&renderer,&draw,100,ops)&&DrawCalls==1,"alternate price presentation stays native");
        draw.Sprite=&Sprite37;
        ok &= Check(DrawPrice(f.Ui.data(),&renderer,&draw,80,ops)&&LastPrice==80,"native effective discounted price is retained");
        ok &= Check(!DrawPrice(f.Ui.data(),&renderer,&draw,-1,ops),"invalid effective price stays native");
        Put(f.Row()+40,240);
        ok &= Check(DrawPrice(f.Ui.data(),&renderer,&draw,240,ops)&&LastPrice==240,"price is not hardcoded to100 or150");
        FailText=true;
        ok &= Check(!DrawPrice(f.Ui.data(),&renderer,&draw,240,ops)&&FontDepth==0,"text callback fault still restores selected font");
        FailText=false;
        auto missing=ops;missing.RestoreFont=nullptr;const auto calls=DrawCalls;
        ok &= Check(!DrawPrice(f.Ui.data(),&renderer,&draw,240,missing)&&DrawCalls==calls&&FontDepth==0,"incomplete font operations draw nothing");
        Put(f.Countdown.data()+96,0);
        ok &= Check(!DrawPrice(f.Ui.data(),&renderer,&draw,240,ops),"inactive UI does not alter other rendering");
        return ok;
    }

    // Execute the exact emitted bridges, not a model of their stack offsets.
    void* HarnessUi{}; void* HarnessObject{}; void* HarnessEntry{};
    Receipt HarnessReceipt{1,1,100004,-1,65535,0};
    PriceDraw HarnessDraw{328,304,&Sprite37}; int HarnessPrice=100;
    Drawing HarnessDrawing{};
    unsigned BeforeEsp{}, AfterEsp{}, BeforeFlags{}, AfterFlags{}, HelperFlags{};
    unsigned AfterEbx{},AfterEsi{},AfterEdi{},AfterEbp{},NativeCalls{},NativeFlags{};
    unsigned NativeArgs[6]{}; void* NativeObject{};
    bool HelperArguments{};
    __declspec(naked) void OriginalReceipt()
    {
        __asm
        {
            pushfd
            pop NativeFlags
            mov NativeObject,ecx
            mov eax,[ebp+8]
            mov NativeArgs[0],eax
            mov eax,[ebp+12]
            mov NativeArgs[4],eax
            mov eax,[ebp+16]
            mov NativeArgs[8],eax
            mov eax,[ebp+20]
            mov NativeArgs[12],eax
            mov eax,[ebp+24]
            mov NativeArgs[16],eax
            mov eax,[ebp+28]
            mov NativeArgs[20],eax
            inc NativeCalls
            mov esp,ebp
            pop ebp
            ret 24
        }
    }
    __declspec(naked) void OriginalDrawing()
    {
        __asm
        {
            pushfd
            pop NativeFlags
            mov NativeObject,ecx
            mov eax,[esp+4]
            mov NativeArgs[0],eax
            mov eax,[esp+8]
            mov NativeArgs[4],eax
            mov eax,[esp+12]
            mov NativeArgs[8],eax
            inc NativeCalls
            ret 12
        }
    }
    bool __cdecl ReceiptHelper(void* ui,void* shop,const Receipt* receipt)
    {
        __asm { pushfd }
        __asm { pop HelperFlags }
        HelperArguments=ui==HarnessUi && shop==HarnessObject
            && std::memcmp(receipt,&HarnessReceipt,sizeof(Receipt))==0;
        return HandleReceipt(ui,shop,receipt);
    }
    bool __cdecl PriceHelper(void* ui,void* renderer,const PriceDraw* draw,int price)
    {
        __asm { pushfd }
        __asm { pop HelperFlags }
        HelperArguments=ui==HarnessUi && renderer==HarnessObject && price==HarnessPrice
            && std::memcmp(draw,&HarnessDraw,sizeof(PriceDraw))==0;
        return DrawPrice(ui,renderer,draw,price,HarnessDrawing);
    }
    __declspec(naked) void RunReceiptBridge()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp,esp
            mov ebx,22334455h
            mov esi,33445566h
            mov edi,44556677h
            mov ebp,55667788h
            push 0ED7h
            popfd
            pushfd
            pop BeforeFlags
            push HarnessReceipt[20]
            push HarnessReceipt[16]
            push HarnessReceipt[12]
            push HarnessReceipt[8]
            push HarnessReceipt[4]
            push HarnessReceipt[0]
            mov ecx,HarnessObject
            call HarnessEntry
            mov AfterEsp,esp
            mov AfterEbx,ebx
            mov AfterEsi,esi
            mov AfterEdi,edi
            mov AfterEbp,ebp
            pushfd
            pop AfterFlags
            cld
            popad
            popfd
            ret
        }
    }
    __declspec(naked) void RunPriceBridge()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp,esp
            mov ebx,22334455h
            mov esi,33445566h
            mov edi,44556677h
            mov ebp,55667788h
            push 0ED7h
            popfd
            pushfd
            pop BeforeFlags
            push HarnessDraw[8]
            push HarnessDraw[4]
            push HarnessDraw[0]
            mov ecx,HarnessObject
            call HarnessEntry
            mov AfterEsp,esp
            mov AfterEbx,ebx
            mov AfterEsi,esi
            mov AfterEdi,edi
            mov AfterEbp,ebp
            pushfd
            pop AfterFlags
            cld
            popad
            popfd
            ret
        }
    }
    bool Registers()
    {
        return AfterEsp==BeforeEsp && AfterEbx==0x22334455 && AfterEsi==0x33445566
            && AfterEdi==0x44556677 && AfterEbp==0x55667788;
    }
    bool TestBridges()
    {
        Fixture f; unsigned char renderer{},collection{};
        Put(f.Countdown.data()+12,&collection); ExpectedCollection=&collection;
        ExpectedRenderer=&renderer; ExpectedFont=f.Countdown.data()+20;
        HarnessDrawing={reinterpret_cast<decltype(Drawing::Sprite)>(&SpriteBridge),
            reinterpret_cast<decltype(Drawing::SelectFont)>(&FontBridge),&TextBridge,
            reinterpret_cast<decltype(Drawing::RestoreFont)>(&RestoreBridge)};
        HarnessUi=f.Ui.data();
        auto* memory=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(!memory)return Check(false,"bridge fixture allocation");
        const auto address=reinterpret_cast<std::uintptr_t>(memory);
        const auto reply=BuildReplyStub(address,reinterpret_cast<std::uintptr_t>(&ReceiptHelper),
            reinterpret_cast<std::uintptr_t>(&HarnessUi),reinterpret_cast<std::uintptr_t>(&OriginalReceipt));
        const auto price=BuildPriceStub(address+256,reinterpret_cast<std::uintptr_t>(&PriceHelper),
            reinterpret_cast<std::uintptr_t>(&HarnessUi),reinterpret_cast<std::uintptr_t>(&HarnessPrice),
            reinterpret_cast<std::uintptr_t>(&OriginalDrawing));
        bool ok=Check(reply[0]==0x9C&&reply[44]==0xC2&&price[0]==0x9C&&price[45]==0xC2,
            "actual bridges have bounded callable shape");
        if(ok)
        {
            std::memcpy(memory,reply.data(),reply.size());std::memcpy(memory+256,price.data(),price.size());
            DWORD old{};ok &= Check(VirtualProtect(memory,4096,PAGE_EXECUTE_READ,&old)!=0,"bridge fixture executable");
            FlushInstructionCache(GetCurrentProcess(),memory,4096);
            if(ok)
            {
                HarnessObject=f.Shop.data();HarnessEntry=memory;NativeCalls=0;
                RunReceiptBridge();
                ok &= Check(HelperArguments&&(HelperFlags&0x400)==0&&Registers()&&AfterFlags==BeforeFlags&&NativeCalls==0,
                    "handled receipt preserves nonvolatile registers,ESP,DF/EFLAGS and ret24");
                HarnessReceipt.Success=0;RunReceiptBridge();
                ok &= Check(HelperArguments&&Registers()&&NativeCalls==1&&NativeObject==HarnessObject
                    &&NativeFlags==BeforeFlags&&std::memcmp(NativeArgs,&HarnessReceipt,24)==0,
                    "unhandled receipt recreates native prologue and exact six arguments");
                HarnessReceipt.Success=1;
                HarnessObject=&renderer;HarnessEntry=memory+256;NativeCalls=0;FontDepth=0;CallbackArguments=true;
                HarnessDraw.Sprite=&Sprite37;RunPriceBridge();
                ok &= Check(HelperArguments&&(HelperFlags&0x400)==0&&Registers()&&AfterFlags==BeforeFlags
                    &&NativeCalls==0&&FontDepth==0&&CallbackArguments,"handled drawing preserves ABI and balances native font");
                HarnessDraw.Sprite=&Sprite43;RunPriceBridge();
                ok &= Check(HelperArguments&&Registers()&&NativeCalls==1&&NativeObject==HarnessObject
                    &&NativeFlags==BeforeFlags&&std::memcmp(NativeArgs,&HarnessDraw,12)==0,
                    "alternate drawing tail-calls native with exact this/three arguments and ret12");
                HarnessDraw.Sprite=&Sprite37;
            }
        }
        VirtualFree(memory,0,MEM_RELEASE);return ok;
    }
    unsigned Writes{},FailWrite{};
    bool FixtureWriter(std::uintptr_t at,const void* bytes,std::size_t size)
    {
        ++Writes;
        const bool written=dfl::memory_patch::WriteBytes(at,bytes,size);
        return written&&Writes!=FailWrite;
    }
    bool LyingWriter(std::uintptr_t at,const void* bytes,std::size_t size)
    {
        ++Writes;
        return Writes==2 || dfl::memory_patch::WriteBytes(at,bytes,size);
    }
    bool TestInstaller()
    {
        auto* image=static_cast<unsigned char*>(VirtualAlloc(nullptr,0x4F0000,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(!image)return Check(false,"installer fixture allocation");
        // Independent literal original PE bytes, including both call contexts.
        const unsigned char entry[]={0x55,0x8B,0xEC,0x6A,0xFF,0x68,0x24,0xB1,0x98,0,0x64,0xA1,0,0,0,0,
            0x50,0x64,0x89,0x25,0,0,0,0,0x83,0xEC,0x1C,0x8B,0x45,0x10,0x56,0x57};
        const unsigned char price[]={0x8B,0x4E,0x0C,0x74,4,0x6A,0x2B,0xEB,2,0x6A,0x25,0xE8,0x20,0x12,7,0,
            0x8B,0x0D,0x48,0x16,0xBF,0,0x50,0x68,0x30,1,0,0,0x68,0x48,1,0,0,0xE8,0xCA,0xE6,0xB8,0xFF};
        const unsigned char suffix[]={0x8B,0x0D,0x48,0x16,0xBF,0,0x8B,0x11,0x8D,0x46,0x14,0x50,0xFF,0x52,0x4C,
            0x8B,0x0D,0x48,0x16,0xBF,0,0x8B};
        const unsigned char sprite[]={0x55,0x8B,0xEC,0x8B,0x41,0x54,0x8B,0x51,0x58,0x2B,0xD0,0x8B,0x45,8,0xC1,
            0xFA,4,0x3B,0xC2,0x72,0x0F,0x8B,0x81,0xAC};
        const unsigned char draw[]={0x55,0x8B,0xEC,0x8B,0x45,0x10,0x85,0xC0,0x74,0x13,0x8B,0x10,0x51,0x8B,0x4D,0x0C,
            0x51,0x8B,0x4D,8,0x51,0x8B,0xC8,0xFF};
        const unsigned offsets[]={0x462D80,0x472F20,0x472F46,0x4E4150,0x1610};
        const unsigned lengths[]={sizeof(entry),sizeof(price),sizeof(suffix),sizeof(sprite),sizeof(draw)};
        const unsigned char* signatures[]={entry,price,suffix,sprite,draw};
        for(unsigned i=0;i<5;++i)std::memcpy(image+offsets[i],signatures[i],lengths[i]);
        const auto base=reinterpret_cast<std::uintptr_t>(image);const auto reply=base+0x480000,priceStub=reply+256;
        bool ok=true;
        for(unsigned part=0;part<5;++part)for(unsigned i=0;i<lengths[part];++i)
        {
            image[offsets[part]+i]^=1;Writes=0;FailWrite=0;
            ok &= Check(!InstallImage(base,reply,priceStub,FixtureWriter)&&Writes==0,"any original signature mismatch performs no writes");
            image[offsets[part]+i]^=1;
        }
        for(unsigned failure=1;failure<=2;++failure)
        {
            Writes=0;FailWrite=failure;
            ok &= Check(!InstallImage(base,reply,priceStub,FixtureWriter)
                &&std::memcmp(image+0x462D80,entry,5)==0&&std::memcmp(image+0x472F41,price+33,5)==0,
                "write-after-failure restores both complete native patch spans");
        }
        Writes=0;
        ok &= Check(!InstallImage(base,reply,priceStub,LyingWriter)
            &&std::memcmp(image+0x462D80,entry,5)==0&&std::memcmp(image+0x472F41,price+33,5)==0,
            "post-write verification failure also restores both original spans");
        // Restore the intentionally incomplete RED installation to make later
        // assertions independent and keep failed evidence unambiguous.
        std::memcpy(image+0x462D80,entry,5);std::memcpy(image+0x472F41,price+33,5);
        Writes=0;FailWrite=0;
        ok &= Check(InstallImage(base,reply,priceStub,FixtureWriter)&&Writes==2,"both validated sites install");
        unsigned rel{};std::memcpy(&rel,image+0x462D81,4);
        ok &= Check(image[0x462D80]==0xE9&&base+0x462D85+rel==reply,"receipt JMP reaches supplied bridge");
        std::memcpy(&rel,image+0x472F42,4);
        ok &= Check(image[0x472F41]==0xE8&&base+0x472F46+rel==priceStub,"drawing CALL reaches supplied bridge");
        ok &= Check(InstallImage(base,reply,priceStub,FixtureWriter)&&Writes==2,"own complete installation is idempotent");
        ok &= Check(!InstallImage(base,reply+1,priceStub,FixtureWriter)&&Writes==2,"foreign entry target is never overwritten");
        // A partial installation is not implicitly owned by a new attempt.
        std::memcpy(image+0x472F41,price+33,5);
        ok &= Check(!InstallImage(base,reply,priceStub,FixtureWriter)&&Writes==2,"mixed original/patched state fails closed");
        VirtualFree(image,0,MEM_RELEASE);return ok;
    }
}
bool TestCountdownCoin()
{
    const bool receipt=TestReceiptPolicy(),drawing=TestDrawing();
    const bool bridges=TestBridges(),installer=TestInstaller();
    return receipt&&drawing&&bridges&&installer;
}
