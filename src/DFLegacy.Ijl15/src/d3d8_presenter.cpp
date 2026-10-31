#include "d3d8_presenter.h"
#include <array>
#include <new>
#include <utility>

namespace
{
    namespace a = dfl::d3d8;
    bool Lost(HRESULT hr) noexcept {return hr==a::DeviceLost||hr==a::DeviceNotReset;}
    struct Reference
    {
        void* Value = nullptr;
        ~Reference() { Reset(); }
        void Reset() noexcept { if (Value) { a::Call<ULONG>(Value,2); Value=nullptr; } }
        Reference() = default;
        Reference(const Reference&) = delete;
        Reference& operator=(const Reference&) = delete;
    };
    struct Vertex {float X,Y,Z,Rhw,U,V;};
}

namespace dfl::d3d8_presenter
{
    struct Presenter::Implementation
    {
        void* Device;
        void** Table;
        Reference SourceTexture,SourceSurface,IntermediateTexture,IntermediateSurface;
        presentation::Plan Cached{};
        DWORD Format = 0;

        Implementation(void* device,void** table) : Device(device),Table(table) {}
        template<class... Args> HRESULT D(std::size_t index,Args... args) { return a::CallTable<HRESULT>(Device,Table,index,args...); }
        void Clear() noexcept
        {
            IntermediateSurface.Reset();IntermediateTexture.Reset();SourceSurface.Reset();SourceTexture.Reset();
            Format=0;Cached={};
        }
        HRESULT Texture(UINT w,UINT h,DWORD format,Reference& texture,Reference& surface)
        {
            HRESULT hr=D(20,w,h,UINT(1),DWORD(1),format,DWORD(0),&texture.Value);
            if (SUCCEEDED(hr)) hr=a::Call<HRESULT>(texture.Value,15,UINT(0),&surface.Value);
            return hr;
        }
        HRESULT Ensure(const presentation::Plan& plan,DWORD format)
        {
            if (Format==format && SourceTexture.Value && Cached.SourceAllocationWidth==plan.SourceAllocationWidth
                && Cached.SourceAllocationHeight==plan.SourceAllocationHeight && Cached.UseIntermediate==plan.UseIntermediate
                && Cached.IntermediateAllocationWidth==plan.IntermediateAllocationWidth && Cached.IntermediateAllocationHeight==plan.IntermediateAllocationHeight)
                return S_OK;
            Clear();
            HRESULT hr=Texture(plan.SourceAllocationWidth,plan.SourceAllocationHeight,format,SourceTexture,SourceSurface);
            if (SUCCEEDED(hr)&&plan.UseIntermediate)hr=Texture(plan.IntermediateAllocationWidth,plan.IntermediateAllocationHeight,format,IntermediateTexture,IntermediateSurface);
            if (FAILED(hr)) {Clear();return hr;}
            Format=format;Cached=plan;return S_OK;
        }
        struct Saved
        {
            Implementation& Owner;
            Reference Target,Depth,Stream,Indices;
            a::Viewport View{};
            UINT Stride=0,Base=0;
            DWORD Block=0;
            explicit Saved(Implementation& owner) : Owner(owner) {}
            ~Saved() {if(Block)Owner.D(56,Block);}
            HRESULT Capture()
            {
                HRESULT hr=Owner.D(32,&Target.Value);
                if(FAILED(hr))return hr;
                hr=Owner.D(33,&Depth.Value);
                if(FAILED(hr)&&hr!=a::NotFound)return hr;
                hr=Owner.D(41,&View);if(FAILED(hr))return hr;
                hr=Owner.D(84,UINT(0),&Stream.Value,&Stride);if(FAILED(hr))return hr;
                hr=Owner.D(86,&Indices.Value,&Base);if(FAILED(hr))return hr;
                return Owner.D(57,DWORD(1),&Block); // D3DSBT_ALL
            }
            HRESULT Restore()
            {
                HRESULT result=S_OK;
                const auto keep=[&](HRESULT hr){if(FAILED(hr)&&!Lost(result)&&(SUCCEEDED(result)||Lost(hr)))result=hr;};
                keep(Owner.D(31,Target.Value,Depth.Value));
                keep(Owner.D(54,Block));
                keep(Owner.D(40,&View));
                // DrawPrimitiveUP resets stream0. Preserve these explicitly even on runtimes with partial state blocks.
                keep(Owner.D(83,UINT(0),Stream.Value,Stride));keep(Owner.D(85,Indices.Value,Base));
                keep(Owner.D(56,Block));Block=0;
                return result;
            }
        };

        HRESULT Configure()
        {
            HRESULT hr=D(76,DWORD(0x104));if(FAILED(hr))return hr; // XYZRHW | TEX1
            hr=D(88,DWORD(0));if(FAILED(hr))return hr;
            constexpr std::pair<DWORD,DWORD> states[]={{7,0},{8,3},{9,2},{14,0},{15,0},{19,2},{20,1},{22,1},{26,0},
                {27,0},{28,0},{52,0},{128,0},{136,0},{137,0},{152,0},{161,0},{168,15}};
            for(const auto& state:states){hr=D(50,state.first,state.second);if(FAILED(hr))return hr;}
            constexpr std::pair<DWORD,DWORD> stage[]={{1,2},{2,2},{4,2},{5,2},{11,0},{13,3},{14,3},{18,0},{24,0},{28,1}};
            for(const auto& state:stage){hr=D(63,DWORD(0),state.first,state.second);if(FAILED(hr))return hr;}
            hr=D(63,DWORD(1),DWORD(1),DWORD(1));if(FAILED(hr))return hr;
            return D(63,DWORD(1),DWORD(4),DWORD(1));
        }

        HRESULT Draw(void* target,void* texture,UINT validW,UINT validH,UINT allocationW,UINT allocationH,UINT outputW,UINT outputH,bool linear)
        {
            HRESULT hr=D(31,target,static_cast<void*>(nullptr));if(FAILED(hr))return hr;
            const a::Viewport viewport{0,0,outputW,outputH,0.f,1.f};
            hr=D(40,&viewport);if(FAILED(hr))return hr;
            hr=D(61,DWORD(0),texture);if(FAILED(hr))return hr;
            const DWORD filter=linear?2U:1U;
            hr=D(63,DWORD(0),DWORD(16),filter);if(FAILED(hr))return hr;
            hr=D(63,DWORD(0),DWORD(17),filter);if(FAILED(hr))return hr;
            // Nine regions clamp to the VALID rectangle, not the allocated POT padding.
            const float w=static_cast<float>(outputW),h=static_cast<float>(outputH);
            const float sw=static_cast<float>(validW),sh=static_cast<float>(validH);
            const float aw=static_cast<float>(allocationW),ah=static_cast<float>(allocationH);
            const std::array<float,4> xs{0,w/(2*sw),w-w/(2*sw),w},ys{0,h/(2*sh),h-h/(2*sh),h};
            // Point sampling at an exact texel boundary otherwise rounds to either neighbour
            // after raster interpolation. A small positive texel bias pins floor(center*scale).
            const float bias=linear?0.f:1.f/1024.f;
            const std::array<float,4> us{(.5f+bias)/aw,(.5f+bias)/aw,(sw-.5f+bias)/aw,(sw-.5f+bias)/aw};
            const std::array<float,4> vs{(.5f+bias)/ah,(.5f+bias)/ah,(sh-.5f+bias)/ah,(sh-.5f+bias)/ah};
            for(std::size_t y=0;y<3;y++)for(std::size_t x=0;x<3;x++)
            {
                const Vertex tl{xs[x]-.5f,ys[y]-.5f,0,1,us[x],vs[y]},tr{xs[x+1]-.5f,ys[y]-.5f,0,1,us[x+1],vs[y]};
                const Vertex bl{xs[x]-.5f,ys[y+1]-.5f,0,1,us[x],vs[y+1]},br{xs[x+1]-.5f,ys[y+1]-.5f,0,1,us[x+1],vs[y+1]};
                const Vertex vertices[]{tl,tr,bl,tr,br,bl};
                hr=D(72,DWORD(4),UINT(2),vertices,UINT(sizeof(Vertex)));if(FAILED(hr))return hr;
            }
            return S_OK;
        }

        RenderResult Render(void* source,void* target,const presentation::Plan& plan)
        {
            if(!source||!target||!plan.Supported)return {E_INVALIDARG,S_OK,"arguments"};
            a::SurfaceDescription input{},output{};
            HRESULT hr=a::Call<HRESULT>(source,8,&input);if(FAILED(hr))return {hr,S_OK,"source-description"};
            hr=a::Call<HRESULT>(target,8,&output);if(FAILED(hr))return {hr,S_OK,"target-description"};
            if(input.Width!=640||input.Height!=480||output.Width!=plan.Width||output.Height!=plan.Height||input.Format!=output.Format)
                return {a::InvalidCall,S_OK,"surface-layout"};
            hr=Ensure(plan,input.Format);if(FAILED(hr))return {hr,S_OK,"allocate"};
            const RECT rect{0,0,640,480};const POINT point{0,0};
            hr=D(28,source,&rect,UINT(1),SourceSurface.Value,&point);if(FAILED(hr))return {hr,S_OK,"copy"};
            Saved saved(*this);hr=saved.Capture();if(FAILED(hr))return {hr,S_OK,"capture"};
            hr=D(34);if(FAILED(hr))return {hr,S_OK,"begin-scene"};
            hr=Configure();const char* stage="configure";
            if(SUCCEEDED(hr)&&plan.UseIntermediate)
            {
                stage="integer-pass";hr=Draw(IntermediateSurface.Value,SourceTexture.Value,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,
                    plan.IntermediateWidth,plan.IntermediateHeight,false);
            }
            if(SUCCEEDED(hr))
            {
                stage="output-pass";
                hr=plan.UseIntermediate?Draw(target,IntermediateTexture.Value,plan.IntermediateWidth,plan.IntermediateHeight,
                    plan.IntermediateAllocationWidth,plan.IntermediateAllocationHeight,plan.Width,plan.Height,true)
                    :Draw(target,SourceTexture.Value,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,plan.Width,plan.Height,plan.Filter==presentation::Mode::Linear);
            }
            const HRESULT ended=D(35);
            if(FAILED(ended)&&(SUCCEEDED(hr)||(!Lost(hr)&&Lost(ended)))){hr=ended;stage="end-scene";}
            const HRESULT restored=saved.Restore();
            if(FAILED(restored))return {Lost(hr)?hr:restored,restored,Lost(hr)?stage:"restore"};
            return {hr,S_OK,SUCCEEDED(hr)?"ok":stage};
        }
    };

    Presenter::Presenter(void* device,void** originalTable) noexcept : implementation_(new(std::nothrow) Implementation(device,originalTable)) {}
    Presenter::~Presenter() {Clear();}
    void Presenter::Clear() noexcept {if(implementation_)implementation_->Clear();}
    RenderResult Presenter::Render(void* source,void* target,const presentation::Plan& plan) noexcept
    {
        if(!implementation_)return {E_OUTOFMEMORY,S_OK,"context"};
        return implementation_->Render(source,target,plan);
    }
}
