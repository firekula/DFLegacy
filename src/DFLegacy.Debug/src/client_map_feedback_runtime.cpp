#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_map_feedback_runtime.h"
#include "map_click_feedback.h"
#include <array>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <iomanip>
#include <locale>
#include <limits>
#include <sstream>

namespace dfl::client_map_feedback
{
    namespace detail
    {
        using Descriptor=std::array<unsigned char,0x58>;
        struct Graphics { std::uint32_t system=0,surface=0,device=0,sprite=0; };
        struct LockedPixels { void* bits=nullptr;std::int32_t pitch=0; };
        struct TextureOperations
        {
            void* context=nullptr;
            std::uint32_t (*allocate)(void*) noexcept=nullptr;
            bool (*construct)(void*,std::uint32_t,const Graphics&) noexcept=nullptr;
            bool (*create)(void*,std::uint32_t) noexcept=nullptr;
            bool (*lock)(void*,std::uint32_t,LockedPixels&) noexcept=nullptr;
            bool (*unlock)(void*,std::uint32_t) noexcept=nullptr;
            bool (*destroy)(void*,std::uint32_t,bool constructed) noexcept=nullptr;
        };
        struct RenderOperations
        {
            void* context=nullptr;
            bool (*get)(void*,const Graphics&,std::uint32_t,std::uint32_t&) noexcept=nullptr;
            bool (*set)(void*,const Graphics&,std::uint32_t,std::uint32_t) noexcept=nullptr;
            bool (*draw)(void*,const Graphics&,map_click_feedback::Point,const Descriptor&) noexcept=nullptr;
        };
        struct Hook { bool installed=false;std::array<unsigned char,5> patched{}; };
        using PatchOperation=bool (*)(void*,const unsigned char*,const unsigned char*) noexcept;
        using OriginalOperation=std::int32_t (*)(void*) noexcept;
        using AfterOperation=void (*)(void*) noexcept;
        // 881860 has already selected this minimap's visible root child here.
        // ECX is the minimap owner; 880B90 still follows this CALL unchanged.
        constexpr std::uint32_t DrawCallAddress=0x88187E,DrawTargetAddress=0x8814A0;
        constexpr unsigned char OriginalCall[]={0xE8,0x1D,0xFC,0xFF,0xFF};
        bool CopyPixels(const LockedPixels& mapping) noexcept
        {
            if(!mapping.bits || mapping.pitch<88 || mapping.pitch>1048576)return false;
            const auto base=reinterpret_cast<std::uintptr_t>(mapping.bits);
            const auto span=static_cast<std::uintptr_t>(mapping.pitch)*43+88;
            if(base<0x10000 || base>std::numeric_limits<std::uintptr_t>::max()-span)return false;
            __try
            {
                auto* target=static_cast<unsigned char*>(mapping.bits);
                const auto& source=map_click_feedback::GetYellowPixels();
                for(std::size_t row=0;row<44;++row)
                    std::memcpy(target+row*static_cast<std::size_t>(mapping.pitch),source.data()+row*44,88);
                return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool BuildTexture(const TextureOperations& ops,const Graphics& graphics,std::uint32_t& output,
                          bool* constructedOutput=nullptr) noexcept
        {
            output=0;if(constructedOutput)*constructedOutput=false;
            if(!ops.allocate || !ops.construct || !ops.create || !ops.lock || !ops.unlock || !ops.destroy)return false;
            const auto texture=ops.allocate(ops.context);if(!texture)return false;
            const bool constructed=ops.construct(ops.context,texture,graphics);
            if(constructedOutput)*constructedOutput=constructed;
            bool okay=constructed && ops.create(ops.context,texture);
            if(okay)
            {
                LockedPixels mapping;
                const bool locked=ops.lock(ops.context,texture,mapping);
                okay=locked && CopyPixels(mapping);
                // A successful lock is always paired, including an invalid pitch/copy failure.
                if(locked){const bool unlocked=ops.unlock(ops.context,texture);okay=okay && unlocked;}
            }
            if(okay){output=texture;return true;}
            // Retain ownership on failed cleanup; the caller must block unload and retry.
            if(!ops.destroy(ops.context,texture,constructed))output=texture;
            return false;
        }
        bool DrawScoped(const RenderOperations& ops,const Graphics& graphics,map_click_feedback::Point point,
                        const Descriptor& source,std::uint32_t texture,std::uint32_t key,
                        const std::array<map_click_feedback::RingState,2>& rings,unsigned& draws) noexcept
        {
            draws=0;if(!ops.get || !ops.set || !ops.draw)return false;
            std::uint32_t src=0,dst=0;
            if(!ops.get(ops.context,graphics,19,src) || !ops.get(ops.context,graphics,20,dst))return false;
            bool okay=true;
            for(const auto& ring:rings)
            {
                if(!ring.visible)continue;
                Descriptor copy=source;
                const std::int32_t offset=-22;
                std::memcpy(copy.data(),&texture,4);std::memcpy(copy.data()+8,&offset,4);std::memcpy(copy.data()+12,&offset,4);
                std::memcpy(copy.data()+0x1C,&ring.color,4);
                std::uint32_t bits=0;std::memcpy(&bits,&ring.scale,4);
                for(const auto offsetScale:{0x10u,0x14u})
                {
                    const auto address=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(copy.data()+offsetScale));
                    const std::uint32_t encoded=(bits^key)+address;
                    std::memcpy(copy.data()+offsetScale,&encoded,4);
                }
                if(!ops.draw(ops.context,graphics,point,copy)){okay=false;break;}
                ++draws;
            }
            // Native leaves catch SEH and return false, so both restores also run after a native exception.
            const bool restoreSrc=ops.set(ops.context,graphics,19,src);
            const bool restoreDst=ops.set(ops.context,graphics,20,dst);
            return okay && restoreSrc && restoreDst;
        }
        bool RestoreHook(Hook& hook,void* context,PatchOperation replace) noexcept
        {
            if(!hook.installed)return true;
            if(!replace || !replace(context,hook.patched.data(),OriginalCall))return false;
            hook.installed=false;return true;
        }
        bool InstallHook(Hook& hook,const std::array<unsigned char,5>& patched,void* context,PatchOperation replace) noexcept
        {
            if(hook.installed)return true;
            if(!replace || !replace(context,OriginalCall,patched.data()))return false;
            hook.patched=patched;hook.installed=true;return true;
        }
        std::int32_t DrawWithOriginal(void* self,OriginalOperation original,AfterOperation after) noexcept
        {
            if(!original)return 0;
            const auto result=original(self);
            if(after)after(self);
            return result;
        }

        client_state::Reader Native{};
        DWORD GameThread=0;
        volatile LONG Enabled=0,StopRequested=0,ResourcesPresent=0,HookPublished=0,ActiveCalls=0;
        // White-box deterministic interleaving seam; null in the production lifecycle.
        void (*BeforeShutdownLock)() noexcept=nullptr;
        struct Activity
        {
            Activity() noexcept {InterlockedIncrement(&ActiveCalls);}
            ~Activity() {InterlockedDecrement(&ActiveCalls);}
        };
        Hook DrawHook{};
        SRWLOCK HookLock=SRWLOCK_INIT,PublicationLock=SRWLOCK_INIT;
        wchar_t Directory[MAX_PATH]{};
        bool FunctionsVerified=false,TextureReady=false,TextureConstructed=false,RetireTexture=false,TextureWasDrawn=false,NavigationReady=false;
        unsigned RetireObservations=0;
        volatile LONG StopObservations=0;
        std::uint32_t Texture=0;
        Graphics TextureGraphics{};
        map_click_feedback::Feedback Feedback;
        map_click_feedback::Point LastPoint{};
        struct LightState
        {
            map_click_feedback::Binding binding{};
            std::uint64_t sequence=0,capturedTick=0;
            bool town=false;
        } Current;
        struct Diagnostic
        {
            std::uint64_t tick=0,capturedTick=0,sequence=0,triggerSerial=0,revision=0,draws=0,textureCreates=0;
            map_click_feedback::Point point{};
            std::array<map_click_feedback::RingState,2> rings{};
            bool active=false,hook=false,cleanupPending=false;
            const char* reason="idle";
        } Stats,Published;
        bool HavePublication=false;
        std::uint64_t LastExportTick=0,LastExportRevision=0;
        struct Reads
        {
            template<class T> bool At(std::uint32_t base,std::uint32_t offset,T& output) const noexcept
            {
                const auto address=static_cast<std::uint64_t>(base)+offset;
                return base>=0x10000 && address+sizeof(T)<=0x100000000ULL && Native.read
                    && Native.read(Native.context,static_cast<std::uint32_t>(address),&output,sizeof(output));
            }
        };
        bool Layout() noexcept {return Native.supported && Native.supported(Native.context);}
        bool Thread() noexcept {return GameThread && GetCurrentThreadId()==GameThread;}
        std::uint64_t Now() noexcept {return Native.tickMs?Native.tickMs(Native.context):0;}
        void Reason(const char* reason) noexcept
        {if(std::strcmp(Stats.reason,reason)!=0){Stats.reason=reason;++Stats.revision;}}
        void Publish() noexcept
        {
            Stats.tick=Now();Stats.capturedTick=Current.capturedTick;Stats.sequence=Current.sequence;
            Stats.active=Feedback.Active();Stats.point=LastPoint;
            Stats.rings={Feedback.Ring(0,Stats.tick),Feedback.Ring(1,Stats.tick)};
            Stats.hook=InterlockedCompareExchange(&HookPublished,0,0)!=0;
            Stats.cleanupPending=RetireTexture || InterlockedCompareExchange(&StopRequested,0,0)!=0;
            if(!TryAcquireSRWLockExclusive(&PublicationLock))return;
            Published=Stats;HavePublication=true;ReleaseSRWLockExclusive(&PublicationLock);
        }
        bool Signatures() noexcept
        {
            if(FunctionsVerified)return true;
            struct Signature {std::uint32_t address;std::array<unsigned char,16> bytes;};
            constexpr Signature signatures[]={
                {DrawTargetAddress,{0x55,0x8B,0xEC,0x6A,0xFF,0x68,0x7B,0xBB,0x98,0x00,0x64,0xA1,0x00,0x00,0x00,0x00}},
                {0x881860,{0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8B,0x46,0x04,0x57,0x8B,0x7D,0x08,0x3B,0xF8,0x75}},
                {0x88187C,{0x8B,0xCE,0xE8,0x1D,0xFC,0xFF,0xFF,0x8B,0xCE,0xE8,0x06,0xF3,0xFF,0xFF,0x3B,0x7E}},
                {0x91857A,{0x56,0x8B,0x74,0x24,0x08,0xEB,0x10,0x56,0xE8,0x37,0x66,0x00,0x00,0x85,0xC0,0x59}},
                {0x919D7D,{0xE9,0xF6,0x0B,0x00,0x00,0x55,0x8B,0xEC,0x83,0xEC,0x20,0x8B,0x45,0x0C,0x56,0x8B}},
                {0x8E16A0,{0x55,0x8B,0xEC,0x8B,0x45,0x10,0x8B,0x55,0x08,0x56,0x8B,0xF1,0x8B,0x4D,0x0C,0x50}},
                {0x8E1730,{0x55,0x8B,0xEC,0x8B,0x45,0x0C,0x8B,0x55,0x08,0x6A,0x19,0x50,0x52,0xE8,0xDE,0x31}},
                {0x8D3380,{0x56,0x8B,0xF1,0x83,0x7E,0x14,0x01,0x75,0x06,0x83,0x7E,0x18,0x01,0x74,0x29,0x8A}},
                {0x8D33C0,{0x56,0x8B,0xF1,0x83,0x7E,0x14,0x01,0x75,0x06,0x83,0x7E,0x18,0x01,0x74,0x21,0x8A}},
                {0x8E1750,{0x55,0x8B,0xEC,0x56,0x8B,0xF1,0xE8,0x85,0xFF,0xFF,0xFF,0xF6,0x45,0x08,0x01,0x74}},
                {0x8E1920,{0x55,0x8B,0xEC,0x51,0x56,0x57,0x8B,0x7D,0x08,0x8D,0x55,0xFC,0x8B,0xF1,0x8B,0x86}},
                {0x8E18F0,{0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x08,0x56,0x57,0x8B,0x7D,0x0C,0x8B,0xF1,0x8B,0x86}},
                {0x82C160,{0x55,0x8B,0xEC,0x56,0x8B,0x75,0x18,0x85,0xF6,0x0F,0x84,0xD8,0x00,0x00,0x00,0x8B}}
            };
            Reads reads;std::array<unsigned char,16> actual{};
            for(const auto& signature:signatures)
                if(!reads.At(signature.address,0,actual) || actual!=signature.bytes)return false;
            FunctionsVerified=true;return true;
        }
        bool LeafGate() noexcept {return Thread() && Layout() && Signatures();}
        bool ReadGraphics(Graphics& output) noexcept
        {
            Reads r;Graphics g;std::uint32_t table=0,kind=0,deviceTable=0,spriteTable=0,method=0;
            if(!r.At(0xBF163C,0,g.system) || !r.At(g.system,0,table) || table!=0xA8EA4C
                || !r.At(0xBF1648,0,g.surface) || !r.At(g.surface,0,table) || table!=0xA8E75C
                || !r.At(g.surface,8,kind) || kind!=7 || !r.At(g.system,0x203C,g.device)
                || !r.At(g.system,0x2040,g.sprite) || !r.At(g.device,0,deviceTable) || !r.At(g.sprite,0,spriteTable)
                || !r.At(deviceTable,0xC8,method) || method<0x10000 || !r.At(deviceTable,0xCC,method) || method<0x10000
                || !r.At(spriteTable,0x14,method) || method<0x10000)return false;
            output=g;return true;
        }
        bool SameGraphics(const Graphics& a,const Graphics& b) noexcept
        {return a.system==b.system && a.surface==b.surface && a.device==b.device && a.sprite==b.sprite;}
        bool GraphicsGate(const Graphics& graphics) noexcept
        {Graphics actual;return LeafGate() && ReadGraphics(actual) && SameGraphics(actual,graphics);}
        bool TextureGate(std::uint32_t texture) noexcept
        {Reads r;std::uint32_t table=0;return LeafGate() && r.At(texture,0,table) && table==0xA8EFE4;}
        std::uint32_t NativeAllocate(void*) noexcept
        {
            if(!LeafGate())return 0;
            __try {using F=void* (__cdecl*)(std::size_t);return static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(reinterpret_cast<F>(0x91857A)(0x90)));}
            __except(EXCEPTION_EXECUTE_HANDLER){return 0;}
        }
        bool NativeConstruct(void*,std::uint32_t texture,const Graphics& graphics) noexcept
        {
            if(!texture || !GraphicsGate(graphics))return false;
            __try
            {
                using F=void* (__thiscall*)(void*,std::uint32_t,void*,void*);
                return reinterpret_cast<F>(0x8E16A0)(reinterpret_cast<void*>(texture),14,reinterpret_cast<void*>(graphics.device),reinterpret_cast<void*>(graphics.sprite))==reinterpret_cast<void*>(texture);
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeCreate(void*,std::uint32_t texture) noexcept
        {
            if(!TextureGate(texture))return false;
            __try {using F=std::int32_t (__thiscall*)(void*,int,int);return reinterpret_cast<F>(0x8E1730)(reinterpret_cast<void*>(texture),44,44)>=0;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeLock(void*,std::uint32_t texture,LockedPixels& mapping) noexcept
        {
            if(!TextureGate(texture))return false;
            bool locked=false;
            __try
            {
                using F=std::int32_t (__thiscall*)(void*);
                if(reinterpret_cast<F>(0x8D3380)(reinterpret_cast<void*>(texture))<0)return false;
                locked=true;
                // The native lock wrote this private object's current D3DLOCKED_RECT.
                mapping.pitch=*reinterpret_cast<const std::int32_t*>(texture+0x68);
                mapping.bits=*reinterpret_cast<void* const*>(texture+0x6C);return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER)
            {
                if(locked)
                {
                    __try {using F=std::int32_t (__thiscall*)(void*);reinterpret_cast<F>(0x8D33C0)(reinterpret_cast<void*>(texture));}
                    __except(EXCEPTION_EXECUTE_HANDLER){}
                }
                return false;
            }
        }
        bool NativeUnlock(void*,std::uint32_t texture) noexcept
        {
            if(!TextureGate(texture))return false;
            __try {using F=std::int32_t (__thiscall*)(void*);return reinterpret_cast<F>(0x8D33C0)(reinterpret_cast<void*>(texture))>=0;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeDestroy(void*,std::uint32_t texture,bool constructed) noexcept
        {
            if(!LeafGate() || !texture || (constructed && !TextureGate(texture)))return false;
            __try
            {
                if(constructed){using F=void (__thiscall*)(void*,unsigned);reinterpret_cast<F>(0x8E1750)(reinterpret_cast<void*>(texture),1);}
                else {using F=void (__cdecl*)(void*);reinterpret_cast<F>(0x919D7D)(reinterpret_cast<void*>(texture));}
                return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        const TextureOperations NativeTexture{nullptr,NativeAllocate,NativeConstruct,NativeCreate,NativeLock,NativeUnlock,NativeDestroy};
        const TextureOperations* CleanupOperations=&NativeTexture;
        bool NativeGet(void*,const Graphics& graphics,std::uint32_t state,std::uint32_t& value) noexcept
        {
            if(!GraphicsGate(graphics))return false;
            __try {using F=std::uint32_t (__thiscall*)(void*,std::uint32_t);value=reinterpret_cast<F>(0x8E1920)(reinterpret_cast<void*>(graphics.system),state);return true;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeSet(void*,const Graphics& graphics,std::uint32_t state,std::uint32_t value) noexcept
        {
            if(!GraphicsGate(graphics))return false;
            __try {using F=std::int32_t (__thiscall*)(void*,std::uint32_t,std::uint32_t);return reinterpret_cast<F>(0x8E18F0)(reinterpret_cast<void*>(graphics.system),state,value)>=0;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeDraw(void*,const Graphics& graphics,map_click_feedback::Point point,const Descriptor& descriptor) noexcept
        {
            if(!GraphicsGate(graphics))return false;
            __try
            {
                using F=void (__cdecl*)(void*,int,int,int,const void*);
                reinterpret_cast<F>(0x82C160)(reinterpret_cast<void*>(graphics.surface),point.x,point.y,0,descriptor.data());return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        const RenderOperations NativeRender{nullptr,NativeGet,NativeSet,NativeDraw};
        bool ReadCanvas(map_click_feedback::Binding& binding,std::uint32_t& root,map_click_feedback::Point& view) noexcept
        {
            if(!Current.town)return false;
            Reads r;std::uint32_t manager=0,minimap=0,plane=0,table=0,main=0,context=0;
            std::uint16_t uid=0;unsigned char visible=0,enabled=0;
            if(!r.At(0xB5DCE8,0,manager) || !r.At(manager,0xFF0,minimap) || !r.At(minimap,0,table) || table!=0xA7CF3C
                || !r.At(minimap,4,root) || !r.At(root,0x10,visible) || visible!=1 || !r.At(root,0x11,enabled) || enabled!=1
                || !r.At(minimap,12,plane) || !plane || !r.At(plane,4,view.x) || !r.At(plane,8,view.y)
                || view.x < -4096 || view.x>4096 || view.y < -4096 || view.y>4096
                || !r.At(0xB5D6F0,0,main) || !r.At(main,0x1C,context) || context!=Current.binding.contextToken
                || !r.At(Current.binding.userToken,4,uid) || uid!=Current.binding.uid)return false;
            binding=Current.binding;binding.minimapToken=minimap;binding.planeToken=plane;return true;
        }
        bool ReadTemplate(const map_click_feedback::Binding& binding,Descriptor& output,std::uint32_t& key) noexcept
        {
            Reads r;std::uint32_t begin=0,end=0,found=0;
            if(!r.At(binding.planeToken,0x14,begin) || !r.At(binding.planeToken,0x18,end) || !begin || end<begin || (end-begin)%4 || (end-begin)/4>256)return false;
            for(std::uint32_t i=0;i<(end-begin)/4;++i)
            {
                std::uint32_t marker=0,table=0,type=0,uid=0;
                if(!r.At(begin,i*4,marker))return false;
                if(!marker)continue;
                if(!r.At(marker,0,table) || !r.At(marker,4,type))return false;
                if(table!=0xA8480C || type!=2)continue;
                if(!r.At(marker,0x14,uid))return false;
                if(uid!=binding.uid)continue;
                if(found)return false;
                found=marker;
            }
            std::uint32_t animation=0,image=0,table=0,type=0,width=0,height=0,effect=0,rotation=0,mirror=0;
            std::int32_t offsetX=0,offsetY=0,imageX=0,imageY=0;
            if(!found || !r.At(found,0x18,animation) || !r.At(animation,0x8C,output))return false;
            std::memcpy(&image,output.data(),4);std::memcpy(&effect,output.data()+0x24,4);
            std::memcpy(&rotation,output.data()+0x18,4);std::memcpy(&mirror,output.data()+0x54,4);
            std::memcpy(&imageX,output.data()+8,4);std::memcpy(&imageY,output.data()+12,4);
            return effect==2 && rotation==0 && mirror==0 && imageX==-22 && imageY==-23 && r.At(image,0,table) && table==0xA8EFE4
                && r.At(image,8,type) && type==14 && r.At(image,0x14,width) && width==44 && r.At(image,0x18,height) && height==44
                && r.At(image,0x1C,offsetX) && offsetX==0 && r.At(image,0x20,offsetY) && offsetY==0 && r.At(0xBEF7D4,0,key);
        }
        bool Fresh() noexcept
        {const auto now=Now();return Current.town && now>=Current.capturedTick && now-Current.capturedTick<=750;}
        bool ReleaseTexture(const TextureOperations& ops=NativeTexture) noexcept
        {
            if(!Texture)return true;
            // ObserveFrame can be the tail of the same native Sprite Begin/End interval.
            // Two observations with draws stopped are required; an internal counter is not a native frame fence.
            if((TextureWasDrawn && RetireObservations<2) || !ops.destroy || !ops.destroy(ops.context,Texture,TextureConstructed))return false;
            Texture=0;TextureReady=TextureConstructed=RetireTexture=TextureWasDrawn=false;RetireObservations=0;TextureGraphics={};
            InterlockedExchange(&ResourcesPresent,0);return true;
        }
        void AfterDraw(void* self) noexcept
        {
            if(!InterlockedCompareExchange(&Enabled,0,0) || InterlockedCompareExchange(&StopRequested,0,0) || !NavigationReady || !Thread() || !Layout())return;
            map_click_feedback::Binding binding;std::uint32_t root=0;map_click_feedback::Point view;
            if(!Fresh() || !ReadCanvas(binding,root,view)){Feedback.Reset();Reason("map_hidden_or_context_unavailable");Publish();return;}
            if(reinterpret_cast<std::uintptr_t>(self)!=binding.minimapToken)return;
            Feedback.Update(binding,true,Now());
            if(!Feedback.Active()){Reason("idle_or_expired");Publish();return;}
            if(!LeafGate()){Feedback.Reset();Reason("native_signature_unavailable");Publish();return;}
            Graphics graphics;
            if(!ReadGraphics(graphics)){Feedback.Reset();Reason("graphics_unavailable");Publish();return;}
            if(Texture && !SameGraphics(TextureGraphics,graphics))
            {Feedback.Reset();RetireTexture=true;RetireObservations=0;Reason("graphics_identity_changed");Publish();return;}
            if(RetireTexture){Feedback.Reset();Reason("texture_cleanup_pending");Publish();return;}
            Descriptor source;std::uint32_t key=0;
            if(!ReadTemplate(binding,source,key)){Reason("player_template_unavailable");Publish();return;}
            if(!Texture)
            {
                TextureGraphics=graphics;
                TextureReady=BuildTexture(NativeTexture,graphics,Texture,&TextureConstructed);
                if(Texture)InterlockedExchange(&ResourcesPresent,1);
                if(!TextureReady){RetireTexture=Texture!=0;Reason("texture_creation_failed");Feedback.Reset();Publish();return;}
                ++Stats.textureCreates;++Stats.revision;
            }
            // Recheck the root/plane after native template/resource work before issuing any draw.
            map_click_feedback::Binding finalBinding;std::uint32_t finalRoot=0;map_click_feedback::Point finalView;
            if(!ReadCanvas(finalBinding,finalRoot,finalView) || finalRoot!=root || finalBinding.minimapToken!=binding.minimapToken
                || finalBinding.planeToken!=binding.planeToken || finalView.x!=view.x || finalView.y!=view.y)
            {Feedback.Reset();Reason("canvas_changed_before_draw");Publish();return;}
            const auto point=Feedback.GetPoint();
            const auto x=static_cast<std::int64_t>(point.x)-view.x,y=static_cast<std::int64_t>(point.y)-view.y;
            if(x<INT32_MIN || x>INT32_MAX || y<INT32_MIN || y>INT32_MAX){Feedback.Reset();Reason("point_overflow");Publish();return;}
            const std::array<map_click_feedback::RingState,2> rings={Feedback.Ring(0,Now()),Feedback.Ring(1,Now())};
            unsigned draws=0;
            if(rings[0].visible || rings[1].visible)TextureWasDrawn=true;
            const bool okay=DrawScoped(NativeRender,graphics,{static_cast<std::int32_t>(x),static_cast<std::int32_t>(y)},source,Texture,key,rings,draws);
            Stats.draws+=draws;
            if(!okay){Feedback.Reset();Reason("native_draw_or_state_restore_failed");}
            else Reason("rendered");
            Publish();
        }
        std::int32_t OriginalDraw(void* self) noexcept
        {using F=std::int32_t (__thiscall*)(void*);return reinterpret_cast<F>(DrawTargetAddress)(self);}
        std::int32_t __fastcall DrawBridge(void* self,void*) noexcept
        {Activity activity;return DrawWithOriginal(self,OriginalDraw,AfterDraw);}
        bool NativePatch(void*,const unsigned char* expected,const unsigned char* replacement) noexcept
        {
            if(!Thread() || !Layout())return false;
            Reads r;std::array<unsigned char,5> actual{};
            if(!r.At(DrawCallAddress,0,actual) || std::memcmp(actual.data(),expected,5)!=0)return false;
            auto* address=reinterpret_cast<unsigned char*>(DrawCallAddress);DWORD old=0,ignored=0;
            if(!VirtualProtect(address,5,PAGE_EXECUTE_READWRITE,&old))return false;
            std::memcpy(address,replacement,5);FlushInstructionCache(GetCurrentProcess(),address,5);
            if(VirtualProtect(address,5,old,&ignored))return true;
            std::memcpy(address,expected,5);FlushInstructionCache(GetCurrentProcess(),address,5);
            VirtualProtect(address,5,old,&ignored);return false;
        }
        void* HookPatchContext=nullptr;
        PatchOperation HookPatchOperation=NativePatch;
        void Install(bool navigationHooksReady) noexcept
        {
            if(!navigationHooksReady || InterlockedCompareExchange(&StopRequested,0,0))return;
            if(!LeafGate()){Reason("native_signature_unavailable");return;}
            if(!TryAcquireSRWLockExclusive(&HookLock)){Reason("draw_hook_install_busy");return;}
            if(!DrawHook.installed)
            {
                const auto target=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&DrawBridge));
                const std::uint32_t displacement=target-(DrawCallAddress+5);
                std::array<unsigned char,5> patched{};patched[0]=0xE8;std::memcpy(patched.data()+1,&displacement,4);
                if(InstallHook(DrawHook,patched,HookPatchContext,HookPatchOperation))
                {InterlockedExchange(&HookPublished,1);++Stats.revision;Reason("hook_ready");}
                else Reason("draw_hook_install_failed");
            }
            ReleaseSRWLockExclusive(&HookLock);
        }
    }
    void Initialize(client_state::Reader reader,const wchar_t* directory) noexcept
    {
        using namespace detail;
        if(InterlockedCompareExchange(&HookPublished,0,0) || InterlockedCompareExchange(&ResourcesPresent,0,0))return;
        Native=reader;Directory[0]=0;
        if(directory && wcsnlen_s(directory,MAX_PATH)<MAX_PATH-64)wcscpy_s(Directory,directory);
        GameThread=0;FunctionsVerified=NavigationReady=false;Current={};Stats={};Published={};
        BeforeShutdownLock=nullptr;
        HavePublication=false;LastExportTick=LastExportRevision=0;LastPoint={};Feedback.Reset();
        Texture=0;TextureGraphics={};TextureReady=TextureConstructed=RetireTexture=TextureWasDrawn=false;RetireObservations=0;DrawHook={};
        CleanupOperations=&NativeTexture;HookPatchContext=nullptr;HookPatchOperation=NativePatch;
        InterlockedExchange(&StopObservations,0);
        InterlockedExchange(&StopRequested,0);
        InterlockedExchange(&Enabled,reader.read && reader.supported && reader.tickMs && reader.pid && reader.processStartedFileTime?1:0);
    }
    void AcceptGoal(const client_state::Snapshot& snapshot,const client_navigation::CanvasGoal& goal) noexcept
    {
        using namespace detail;
        Activity activity;
        if(!InterlockedCompareExchange(&Enabled,0,0) || InterlockedCompareExchange(&StopRequested,0,0) || !Thread() || !Layout()
            || !NavigationReady || !InterlockedCompareExchange(&HookPublished,0,0) || !Fresh() || !snapshot.valid || !snapshot.haveMe
            || !snapshot.layoutSupported || snapshot.truncated || !snapshot.scene.known || snapshot.scene.value!=1 || !snapshot.town.present
            || snapshot.pid!=Current.binding.pid || snapshot.processStartedFileTime!=Current.binding.processStartedFileTime
            || !snapshot.me.uid.known || snapshot.me.uid.value!=Current.binding.uid || snapshot.me.userToken!=Current.binding.userToken
            || snapshot.town.contextToken!=Current.binding.contextToken)return;
        map_click_feedback::Binding binding;std::uint32_t root=0;map_click_feedback::Point view;
        if(!ReadCanvas(binding,root,view) || binding.minimapToken!=goal.minimapToken || binding.planeToken!=goal.planeToken)return;
        LastPoint={goal.mapX,goal.mapY};Feedback.Start(binding,LastPoint,Now());
        ++Stats.triggerSerial;++Stats.revision;Reason("accepted_goal");Publish();
    }
    void ObserveFrame(const client_state::Snapshot& snapshot,bool navigationHooksReady) noexcept
    {
        using namespace detail;
        Activity activity;
        if(!InterlockedCompareExchange(&Enabled,0,0) && !InterlockedCompareExchange(&StopRequested,0,0))return;
        if(!GameThread)GameThread=GetCurrentThreadId();
        if(!Thread())return;
        if(InterlockedCompareExchange(&StopRequested,0,0))
        {
            Feedback.Reset();RetireTexture=Texture!=0;
            const auto observations=InterlockedIncrement(&StopObservations);
            if(!TextureWasDrawn || observations>=2)RetireObservations=2;
            if((TextureWasDrawn && observations<2) || !ReleaseTexture(*CleanupOperations))
            {Reason("shutdown_texture_cleanup_pending");Publish();return;}
            // CALL restoration belongs to this bound game thread, never the shutdown worker.
            if(!TryAcquireSRWLockExclusive(&HookLock)){Reason("draw_hook_restore_pending");Publish();return;}
            const bool restored=RestoreHook(DrawHook,HookPatchContext,HookPatchOperation);
            if(restored){InterlockedExchange(&HookPublished,0);Reason("stopped");}
            else Reason("draw_hook_restore_failed");
            ReleaseSRWLockExclusive(&HookLock);
            Publish();return;
        }
        Current.sequence=snapshot.sequence;Current.capturedTick=snapshot.capturedTickMs;
        Current.binding={snapshot.pid,snapshot.me.uid.value,snapshot.me.userToken,snapshot.town.contextToken,0,0,snapshot.processStartedFileTime};
        Current.town=snapshot.valid && snapshot.layoutSupported && !snapshot.truncated && snapshot.haveMe && snapshot.me.uid.known
            && snapshot.scene.known && snapshot.scene.value==1 && snapshot.town.present && snapshot.me.userToken && snapshot.town.contextToken
            && snapshot.pid==Native.pid && snapshot.processStartedFileTime==Native.processStartedFileTime;
        NavigationReady=navigationHooksReady;
        if(!Layout()){Feedback.Reset();Reason("unsupported_image");Publish();return;}
        if(RetireTexture)
        {
            ++RetireObservations;
            if(!ReleaseTexture(*CleanupOperations)){Feedback.Reset();Reason("texture_cleanup_pending");Publish();return;}
        }
        if(!navigationHooksReady){Feedback.Reset();Reason("navigation_hooks_not_ready");Publish();return;}
        Install(navigationHooksReady);
        map_click_feedback::Binding binding;std::uint32_t root=0;map_click_feedback::Point view;
        if(!Fresh() || !ReadCanvas(binding,root,view)){Feedback.Reset();Reason("map_hidden_or_context_unavailable");}
        else
        {
            const bool wasActive=Feedback.Active();Feedback.Update(binding,true,Now());
            if(wasActive && !Feedback.Active())Reason("identity_changed_clock_regressed_or_expired");
        }
        Publish();
    }
    void PumpWorker() noexcept
    {
        using namespace detail;
        if(!Directory[0])return;
        try
        {
            Diagnostic snapshot;
            if(!TryAcquireSRWLockShared(&PublicationLock))return;
            const bool have=HavePublication;snapshot=Published;ReleaseSRWLockShared(&PublicationLock);
            const auto now=Now();
            if(!have || (snapshot.revision==LastExportRevision && now>=LastExportTick && now-LastExportTick<200))return;
            std::ostringstream out;out.imbue(std::locale::classic());out<<std::setprecision(9);
            out<<"{\"schema\":\"dfl-map-feedback/v1\",\"pid\":"<<Native.pid<<",\"process_started_filetime\":"<<Native.processStartedFileTime
                <<",\"tick_ms\":"<<snapshot.tick<<",\"capture_tick_ms\":"<<snapshot.capturedTick<<",\"capture_sequence\":"<<snapshot.sequence
                <<",\"trigger_serial\":"<<snapshot.triggerSerial<<",\"active\":"<<(snapshot.active?"true":"false")
                <<",\"hook_installed\":"<<(snapshot.hook?"true":"false")<<",\"cleanup_pending\":"<<(snapshot.cleanupPending?"true":"false")
                <<",\"target\":{\"x\":"<<snapshot.point.x<<",\"y\":"<<snapshot.point.y<<"},\"rings\":[";
            for(std::size_t i=0;i<snapshot.rings.size();++i)
            {
                if(i)out<<',';const auto& ring=snapshot.rings[i];
                out<<"{\"visible\":"<<(ring.visible?"true":"false")<<",\"scale\":"<<ring.scale<<",\"color\":"<<ring.color<<'}';
            }
            out<<"],\"draw_count\":"<<snapshot.draws<<",\"texture_creations\":"<<snapshot.textureCreates<<",\"reason\":\""<<snapshot.reason<<"\"}";
            const auto json=out.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-map-feedback-%lu.json",Directory,static_cast<unsigned long>(Native.pid))<0
                || swprintf_s(temporary,L"%s.tmp",path)<0)return;
            const auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return;
            DWORD written=0;const bool okay=json.size()<=MAXDWORD && WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr) && written==json.size();
            CloseHandle(file);
            if(okay && MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING)){LastExportTick=now;LastExportRevision=snapshot.revision;}
            else DeleteFileW(temporary);
        }
        catch(...){/* Only this worker performs diagnostics IO. */}
    }
    bool Shutdown() noexcept
    {
        using namespace detail;
        InterlockedExchange(&StopRequested,1);InterlockedExchange(&Enabled,0);
        if(InterlockedCompareExchange(&ActiveCalls,0,0) || InterlockedCompareExchange(&ResourcesPresent,0,0))return false;
        if(BeforeShutdownLock)BeforeShutdownLock();
        if(!TryAcquireSRWLockExclusive(&HookLock))return false;
        // The preliminary counts may become stale while the final game observer restores
        // the CALL. Recheck them with hook ownership under the same lock. This is a drain
        // observation, not a FreeLibrary fence: the host must also quiesce new frame entry.
        const bool released=!DrawHook.installed && !InterlockedCompareExchange(&HookPublished,0,0)
            && !InterlockedCompareExchange(&ResourcesPresent,0,0) && !InterlockedCompareExchange(&ActiveCalls,0,0);
        ReleaseSRWLockExclusive(&HookLock);return released;
    }
}
