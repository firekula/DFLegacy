#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "countdown_coin.h"
#include "memory_patch.h"
#include <cstring>

namespace
{
    template<class T> T Read(const void* object, unsigned offset=0) noexcept
    {
        T value; std::memcpy(&value, static_cast<const unsigned char*>(object)+offset, sizeof(value)); return value;
    }
    bool HasRoom(const void* pointer, unsigned size) noexcept
    {
        const auto address=reinterpret_cast<std::uintptr_t>(pointer);
        return address>=0x10000 && address<=UINT32_MAX-size;
    }
    bool Context(void* ui, void*& shop, void*& countdown) noexcept
    {
        if (!HasRoom(ui,0xF28)) return false;
        shop=Read<void*>(ui,0xF24); countdown=Read<void*>(ui,0xF08);
        if (!HasRoom(shop,0x3500) || !HasRoom(countdown,112)
            || Read<unsigned>(shop)!=0xA7D504 || Read<unsigned>(countdown)!=0xA7CF2C) return false;
        const auto state=Read<int>(countdown,96);
        return state==1 || state==2;
    }
    bool Product(const void* shop) noexcept
    {
        const auto* row=static_cast<const unsigned char*>(shop)+0x341C;
        const auto cash=Read<unsigned>(row,40);
        return Read<unsigned>(row)!=0 && Read<int>(row,4)==1 && Read<int>(row,8)==0
            && Read<unsigned>(row,12)==1 && row[16]==0xFF && Read<unsigned>(row,20)==1
            && Read<unsigned short>(row,26)==0 && Read<unsigned>(row,32)==0
            && Read<unsigned>(row,36)==0 && cash>0 && cash<=INT_MAX;
    }
    constexpr unsigned ReplyOffset=0x462D80, PriceOffset=0x472F41;
    constexpr unsigned char ReplyOriginal[]={0x55,0x8B,0xEC,0x6A,0xFF};
    constexpr unsigned char PriceOriginal[]={0xE8,0xCA,0xE6,0xB8,0xFF};
    volatile LONG Active{};
    bool Matches(std::uintptr_t address,const void* bytes,std::size_t size) noexcept
    {
        __try { return std::memcmp(reinterpret_cast<const void*>(address),bytes,size)==0; }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    bool KnownContext(std::uintptr_t image) noexcept
    {
        constexpr unsigned char entryTail[]={0x68,0x24,0xB1,0x98,0,0x64,0xA1,0,0,0,0,0x50,0x64,0x89,0x25,
            0,0,0,0,0x83,0xEC,0x1C,0x8B,0x45,0x10,0x56,0x57};
        constexpr unsigned char prefix[]={0x8B,0x4E,0x0C,0x74,4,0x6A,0x2B,0xEB,2,0x6A,0x25,0xE8,0x20,0x12,7,0,
            0x8B,0x0D,0x48,0x16,0xBF,0,0x50,0x68,0x30,1,0,0,0x68,0x48,1,0,0};
        constexpr unsigned char suffix[]={0x8B,0x0D,0x48,0x16,0xBF,0,0x8B,0x11,0x8D,0x46,0x14,0x50,0xFF,0x52,0x4C,
            0x8B,0x0D,0x48,0x16,0xBF,0,0x8B};
        constexpr unsigned char sprite[]={0x55,0x8B,0xEC,0x8B,0x41,0x54,0x8B,0x51,0x58,0x2B,0xD0,0x8B,0x45,8,0xC1,
            0xFA,4,0x3B,0xC2,0x72,0x0F,0x8B,0x81,0xAC};
        constexpr unsigned char draw[]={0x55,0x8B,0xEC,0x8B,0x45,0x10,0x85,0xC0,0x74,0x13,0x8B,0x10,0x51,0x8B,0x4D,0x0C,
            0x51,0x8B,0x4D,8,0x51,0x8B,0xC8,0xFF};
        return image && image<=UINT32_MAX-0x4E4168
            && Matches(image+ReplyOffset+5,entryTail,sizeof(entryTail))
            && Matches(image+0x472F20,prefix,sizeof(prefix)) && Matches(image+PriceOffset+5,suffix,sizeof(suffix))
            && Matches(image+0x4E4150,sprite,sizeof(sprite)) && Matches(image+0x1610,draw,sizeof(draw));
    }
    bool __cdecl NativeReceipt(void* ui,void* shop,const dfl::countdown_coin::detail::Receipt* receipt) noexcept
    {
        return InterlockedCompareExchange(&Active,0,0)==1
            && dfl::countdown_coin::detail::HandleReceipt(ui,shop,receipt);
    }
    bool __cdecl NativePrice(void* ui,void* renderer,const dfl::countdown_coin::detail::PriceDraw* draw,int price) noexcept
    {
        using namespace dfl::countdown_coin::detail;
        if(InterlockedCompareExchange(&Active,0,0)!=1)return false;
        __try
        {
            if(!renderer || renderer!=*reinterpret_cast<void**>(0xBF1648))return false;
            auto** table=Read<void**>(renderer);
            const Drawing operations{reinterpret_cast<decltype(Drawing::Sprite)>(0x8E4150),
                reinterpret_cast<decltype(Drawing::SelectFont)>(table[19]),
                reinterpret_cast<decltype(Drawing::Text)>(table[22]),
                reinterpret_cast<decltype(Drawing::RestoreFont)>(table[20])};
            return DrawPrice(ui,renderer,draw,price,operations);
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
}
namespace dfl::countdown_coin::detail
{
    static_assert(sizeof(void*)==4 && sizeof(Receipt)==24 && sizeof(PriceDraw)==12,"ACT1 x86 stack contracts");
    bool HandleReceipt(void* ui, void* shop, const Receipt* receipt) noexcept
    {
        __try
        {
            if (!receipt || receipt->Success!=1 || receipt->Category!=1 || receipt->Error!=-1
                || receipt->Slot!=65535 || receipt->Gift!=0) return false;
            void* ownedShop{}; void* countdown{};
            return Context(ui,ownedShop,countdown) && shop==ownedShop
                && Read<unsigned char>(shop,9680)==1 && Product(shop)
                && Read<unsigned>(shop,0x341C)==receipt->Commodity;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    bool DrawPrice(void* ui, void* renderer, const PriceDraw* draw, int effectivePrice, const Drawing& operations) noexcept
    {
        __try
        {
            if (!renderer || !draw || !draw->Sprite || effectivePrice<0
                || !operations.Sprite || !operations.SelectFont || !operations.Text || !operations.RestoreFont) return false;
            void* shop{}; void* countdown{};
            if (!Context(ui,shop,countdown) || !Product(shop)
                || static_cast<unsigned>(effectivePrice)>Read<unsigned>(shop,0x341C+40)) return false;
            void* collection=Read<void*>(countdown,12);
            if (!collection) return false;
            void* normal=operations.Sprite(collection,37);
            if (!normal || normal!=draw->Sprite) return false;
            operations.SelectFont(renderer,static_cast<unsigned char*>(countdown)+20);
            __try
            {
                // CP936, matching the original ACT1 font/string table.
                operations.Text(renderer,draw->X,draw->Y,-1,"%d \xC8\xFB\xC0\xAD",effectivePrice);
            }
            __finally { operations.RestoreFont(renderer); }
            return true;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    ReplyStub BuildReplyStub(std::uintptr_t address,std::uintptr_t helper,std::uintptr_t uiAddress,std::uintptr_t resume) noexcept
    {
        // Snapshot native this/arguments before making a cdecl policy call.
        // Both branches restore caller DF/EFLAGS; only the helper runs CLD.
        ReplyStub code{0x9C,0x60,0xFC,0x8D,0x44,0x24,0x28,0x50,0xFF,0x74,0x24,0x1C,
            0xFF,0x35,0,0,0,0,0xE8,0,0,0,0,0x83,0xC4,0x0C,0x84,0xC0,0x75,0x0C,
            0x61,0x9D,0x55,0x8B,0xEC,0x6A,0xFF,0xE9,0,0,0,0,0x61,0x9D,0xC2,0x18,0};
        const auto call=static_cast<std::uint32_t>(helper-address-23);
        const auto jump=static_cast<std::uint32_t>(resume-address-42);
        std::memcpy(code.data()+14,&uiAddress,4);std::memcpy(code.data()+19,&call,4);
        std::memcpy(code.data()+38,&jump,4);return code;
    }
    PriceStub BuildPriceStub(std::uintptr_t address,std::uintptr_t helper,std::uintptr_t uiAddress,
        std::uintptr_t priceAddress,std::uintptr_t originalDraw) noexcept
    {
        PriceStub code{0x9C,0x60,0xFC,0x8D,0x44,0x24,0x28,0xFF,0x35,0,0,0,0,0x50,
            0xFF,0x74,0x24,0x20,0xFF,0x35,0,0,0,0,0xE8,0,0,0,0,0x83,0xC4,0x10,
            0x84,0xC0,0x75,7,0x61,0x9D,0xE9,0,0,0,0,0x61,0x9D,0xC2,0x0C,0};
        const auto call=static_cast<std::uint32_t>(helper-address-29);
        const auto jump=static_cast<std::uint32_t>(originalDraw-address-43);
        std::memcpy(code.data()+9,&priceAddress,4);std::memcpy(code.data()+20,&uiAddress,4);
        std::memcpy(code.data()+25,&call,4);std::memcpy(code.data()+39,&jump,4);return code;
    }
    bool InstallImage(std::uintptr_t image,std::uintptr_t replyStub,std::uintptr_t priceStub,PatchWriter write) noexcept
    {
        if(!replyStub||!priceStub||!write||!KnownContext(image))return false;
        unsigned char reply[]={0xE9,0,0,0,0},price[]={0xE8,0,0,0,0};
        const auto replyDelta=static_cast<std::uint32_t>(replyStub-image-ReplyOffset-5);
        const auto priceDelta=static_cast<std::uint32_t>(priceStub-image-PriceOffset-5);
        std::memcpy(reply+1,&replyDelta,4);std::memcpy(price+1,&priceDelta,4);
        if(Matches(image+ReplyOffset,reply,5)&&Matches(image+PriceOffset,price,5))return true;
        if(!Matches(image+ReplyOffset,ReplyOriginal,5)||!Matches(image+PriceOffset,PriceOriginal,5))return false;
        if(!write(image+ReplyOffset,reply,5)||!write(image+PriceOffset,price,5)
            ||!Matches(image+ReplyOffset,reply,5)||!Matches(image+PriceOffset,price,5))
        {
            // A writer may fail after mutation, including protection restore.
            // Keep both stubs alive; the production active gate stays closed.
            write(image+PriceOffset,PriceOriginal,5);write(image+ReplyOffset,ReplyOriginal,5);return false;
        }
        return true;
    }
}
namespace dfl::countdown_coin
{
    bool Install() noexcept
    {
        const auto image=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if(image!=0x400000||!KnownContext(image))return false;
        // Called during DLL initialization, before the native event loop.
        // Never free an allocation that an uncertain rollback could reference.
        static unsigned char* stubs=nullptr;
        InterlockedExchange(&Active,0);
        if(!stubs)
        {
            if(!Matches(image+ReplyOffset,ReplyOriginal,5)||!Matches(image+PriceOffset,PriceOriginal,5))return false;
            auto* allocated=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
            if(!allocated)return false;
            const auto address=reinterpret_cast<std::uintptr_t>(allocated);
            const auto reply=detail::BuildReplyStub(address,reinterpret_cast<std::uintptr_t>(&NativeReceipt),image+0x75DCE8,image+ReplyOffset+5);
            const auto price=detail::BuildPriceStub(address+256,reinterpret_cast<std::uintptr_t>(&NativePrice),
                image+0x75DCE8,image+0x7539D0,image+0x1610);
            std::memcpy(allocated,reply.data(),reply.size());std::memcpy(allocated+256,price.data(),price.size());
            DWORD old{};
            if(!VirtualProtect(allocated,4096,PAGE_EXECUTE_READ,&old)||!FlushInstructionCache(GetCurrentProcess(),allocated,4096))
            {VirtualFree(allocated,0,MEM_RELEASE);return false;}
            stubs=allocated;
        }
        if(!detail::InstallImage(image,reinterpret_cast<std::uintptr_t>(stubs),reinterpret_cast<std::uintptr_t>(stubs+256),memory_patch::WriteBytes))return false;
        InterlockedExchange(&Active,1);return true;
    }
}
