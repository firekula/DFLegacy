#include "d3d8_presenter.h"
#ifdef DFL_CHAPTER_TWO_XBRZ
#include "xbrz_frame.h"
#endif
#ifdef DFL_CHAPTER_TWO_NTSC
#include "ChapterTwoNtscRenderer.h"
#include <cstring>
#include <vector>
#endif
#include <array>
#include <algorithm>
#include <cmath>
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
    struct CrtVertex {float X,Y,Z,Rhw; std::array<float,8> Coordinates;};
    struct CrtSample
    {
        void* Texture = nullptr;
        UINT Width = 0, Height = 0, AllocationWidth = 0, AllocationHeight = 0;
        float OffsetX = 0;
        bool Linear = true, ScreenMask = false;
    };
#ifdef DFL_CHAPTER_TWO_XBRZ
    double Microseconds() noexcept
    {
        static const double frequency=[] {LARGE_INTEGER value{};QueryPerformanceFrequency(&value);return double(value.QuadPart);}();
        LARGE_INTEGER now{};QueryPerformanceCounter(&now);
        return double(now.QuadPart)*1000000.0/frequency;
    }
#endif
}

namespace dfl::d3d8_presenter
{
    struct Presenter::Implementation
    {
        void* Device;
        void** Table;
        Reference SourceTexture,SourceSurface,IntermediateTexture,IntermediateSurface;
        Reference CrtMask,CrtBrightTexture,CrtBrightSurface,CrtBlurTexture,CrtBlurSurface;
        DWORD CrtBrightShader = 0,CrtBlurShader = 0,CrtCompositeShader = 0;
#ifdef DFL_CHAPTER_TWO_XBRZ
        Reference XbrzReadback,XbrzTexture;
        std::unique_ptr<presentation::XbrzFrame> Xbrz;
        UINT XbrzFactor=0,XbrzAllocationWidth=0,XbrzAllocationHeight=0;
        DWORD XbrzFormat=0;
#endif
#ifdef DFL_CHAPTER_TWO_NTSC
        Reference NtscReadback, NtscTexture;
        chapter_two::NtscRenderer Ntsc;
        std::vector<std::uint32_t> NtscPixels;
#endif
        presentation::PostProcess ActiveEffect=presentation::PostProcess::None;
        presentation::Plan Cached{};
        DWORD Format = 0;

        Implementation(void* device,void** table) : Device(device),Table(table) {}
        template<class... Args> HRESULT D(std::size_t index,Args... args) { return a::CallTable<HRESULT>(Device,Table,index,args...); }
        void Clear() noexcept
        {
            for(auto* shader:{&CrtBrightShader,&CrtBlurShader,&CrtCompositeShader})
                if(*shader){D(90,*shader);*shader=0;}
            CrtMask.Reset();
            CrtBlurSurface.Reset();CrtBlurTexture.Reset();CrtBrightSurface.Reset();CrtBrightTexture.Reset();
#ifdef DFL_CHAPTER_TWO_XBRZ
            Xbrz.reset();XbrzReadback.Reset();XbrzTexture.Reset();
            XbrzFactor=XbrzAllocationWidth=XbrzAllocationHeight=0;XbrzFormat=0;
#endif
#ifdef DFL_CHAPTER_TWO_NTSC
            Ntsc.Clear();
            NtscReadback.Reset();
            NtscTexture.Reset();
            NtscPixels.clear();
#endif
            IntermediateSurface.Reset();IntermediateTexture.Reset();SourceSurface.Reset();SourceTexture.Reset();
            Format=0;Cached={};
        }
#ifdef DFL_CHAPTER_TWO_NTSC
        HRESULT PrepareNtsc(void* source, const presentation::Plan& plan, DWORD format, int preset)
        {
            constexpr UINT width = 640, height = 480, rowBytes = width * 4;
            if (format != 21 && format != 22) return a::InvalidCall;
            if (!NtscReadback.Value)
            {
                HRESULT hr = D(27, width, height, format, &NtscReadback.Value);
                if (FAILED(hr)) return hr;
                hr = D(20, plan.SourceAllocationWidth, plan.SourceAllocationHeight, UINT(1),
                    DWORD(0x200), format, DWORD(0), &NtscTexture.Value);
                if (FAILED(hr)) return hr;
                NtscPixels.resize(std::size_t(width) * height);
            }

            HRESULT hr = D(28, source, static_cast<const RECT*>(nullptr), UINT(0),
                NtscReadback.Value, static_cast<const POINT*>(nullptr));
            if (FAILED(hr)) return hr;
            a::LockedRect read{};
            hr = a::Call<HRESULT>(NtscReadback.Value, 9, &read, static_cast<const RECT*>(nullptr), DWORD(0x10));
            if (FAILED(hr)) return hr;
            hr = read.Bits && read.Pitch >= static_cast<int>(rowBytes)
                ? Ntsc.Render(read.Bits, std::size_t(read.Pitch), preset, NtscPixels.data(), rowBytes)
                : a::InvalidCall;
            const HRESULT unlocked = a::Call<HRESULT>(NtscReadback.Value, 10);
            if (FAILED(unlocked) && (SUCCEEDED(hr) || Lost(unlocked))) return unlocked;
            if (FAILED(hr)) return hr;

            a::LockedRect upload{};
            hr = a::Call<HRESULT>(NtscTexture.Value, 16, UINT(0), &upload, static_cast<const RECT*>(nullptr), DWORD(0x2000));
            if (FAILED(hr)) return hr;
            if (upload.Bits && upload.Pitch >= static_cast<int>(rowBytes))
            {
                for (UINT row = 0; row < height; ++row)
                    std::memcpy(static_cast<unsigned char*>(upload.Bits) + std::size_t(row) * upload.Pitch,
                        NtscPixels.data() + std::size_t(row) * width, rowBytes);
            }
            else hr = a::InvalidCall;
            const HRESULT uploaded = a::Call<HRESULT>(NtscTexture.Value, 17, UINT(0));
            return FAILED(uploaded) && (SUCCEEDED(hr) || Lost(uploaded)) ? uploaded : hr;
        }
#endif
#ifdef DFL_CHAPTER_TWO_XBRZ
        HRESULT EnsureXbrz(UINT factor,DWORD format)
        {
            if(format!=21&&format!=22)return a::InvalidCall;
            if(Xbrz&&XbrzReadback.Value&&XbrzTexture.Value&&XbrzFactor==factor&&XbrzFormat==format)return S_OK;
            Clear();
            a::Caps caps{};HRESULT hr=D(7,&caps);if(FAILED(hr))return hr;
            const auto power=[](UINT value){UINT result=1;while(result<value)result*=2;return result;};
            XbrzAllocationWidth=(caps.TextureCaps&2)?power(640*factor):640*factor;
            XbrzAllocationHeight=(caps.TextureCaps&2)?power(480*factor):480*factor;
            if(caps.TextureCaps&32)XbrzAllocationWidth=XbrzAllocationHeight=std::max(XbrzAllocationWidth,XbrzAllocationHeight);
            if(XbrzAllocationWidth>caps.MaxTextureWidth||XbrzAllocationHeight>caps.MaxTextureHeight)return a::InvalidCall;
            hr=D(27,UINT(640),UINT(480),format,&XbrzReadback.Value);if(FAILED(hr))return hr;
            hr=D(20,XbrzAllocationWidth,XbrzAllocationHeight,UINT(1),DWORD(0x200),format,DWORD(0),&XbrzTexture.Value);
            if(FAILED(hr))return hr;
            Xbrz=std::make_unique<presentation::XbrzFrame>(640,480);
            XbrzFactor=factor;XbrzFormat=format;return S_OK;
        }
        HRESULT PrepareXbrz(void* source,DWORD format,UINT factor,ProcessingTimes& times)
        {
            HRESULT hr=EnsureXbrz(factor,format);if(FAILED(hr))return hr;
            const auto readStart=Microseconds();
            hr=D(28,source,static_cast<const RECT*>(nullptr),UINT(0),XbrzReadback.Value,static_cast<const POINT*>(nullptr));
            if(FAILED(hr))return hr;
            a::LockedRect read{};
            hr=a::Call<HRESULT>(XbrzReadback.Value,9,&read,static_cast<const RECT*>(nullptr),DWORD(0x10));
            if(FAILED(hr))return hr;
            times.ReadbackUs=Microseconds()-readStart;
            presentation::XbrzWork work{};const auto scaleStart=Microseconds();
            const bool prepared=read.Pitch>=640*4&&Xbrz->Prepare(read.Bits,std::size_t(read.Pitch),factor,xbrz::ColorFormat::argbUnbuffered,work);
            times.ScaleUs=Microseconds()-scaleStart;times.ScaledRows=work.ScaledRows;times.Workers=work.Workers;
            const auto unlockStart=Microseconds();
            const auto unlocked=a::Call<HRESULT>(XbrzReadback.Value,10);
            times.ReadbackUs+=Microseconds()-unlockStart;
            if(FAILED(unlocked))return unlocked;
            if(!prepared)return E_FAIL;
            if(!work.ScaledRows)return S_OK;
            const auto uploadStart=Microseconds();a::LockedRect upload{};
            hr=a::Call<HRESULT>(XbrzTexture.Value,16,UINT(0),&upload,static_cast<const RECT*>(nullptr),DWORD(0x2000));
            if(FAILED(hr))return hr;
            const auto width=640*factor,height=480*factor;
            if(upload.Pitch<int(width*4)){a::Call<HRESULT>(XbrzTexture.Value,17,UINT(0));return a::InvalidCall;}
            for(UINT row=0;row<height;++row)
                std::memcpy(static_cast<unsigned char*>(upload.Bits)+std::size_t(row)*upload.Pitch,
                    Xbrz->Output().data()+std::size_t(row)*width,std::size_t(width)*4);
            hr=a::Call<HRESULT>(XbrzTexture.Value,17,UINT(0));
            times.UploadUs=Microseconds()-uploadStart;return hr;
        }
#endif
        HRESULT EnsureCrt(const presentation::Plan& plan,DWORD format)
        {
            if(CrtBrightShader && CrtBlurShader && CrtCompositeShader && CrtMask.Value &&
                CrtBrightTexture.Value && CrtBlurTexture.Value)return S_OK;
            // All three authored programs fit ps_1_1. The bright render target
            // clamps the threshold subtraction to zero before horizontal blur.
            // Keep constants inside the ps_1_1 [-1,1] range. Two additions
            // supply the remaining normalization gain without a >1 constant.
            // tex t0 / sub r0,t0,c0 / mul r0,r0,c1 / add r0,r0,r0 (twice)
            constexpr DWORD bright[]{
                0xffff0101,0x42,0xb00f0000,
                0x03,0x800f0000,0xb0e40000,0xa0e40000,
                0x05,0x800f0000,0x80e40000,0xa0e40001,
                0x02,0x800f0000,0x80e40000,0x80e40000,
                0x02,0x800f0000,0x80e40000,0x80e40000,0xffff
            };
            // Four bilinear samples, reused at two horizontal radii.
            constexpr DWORD blur[]{
                0xffff0101,0x42,0xb00f0000,0x42,0xb00f0001,0x42,0xb00f0002,0x42,0xb00f0003,
                0x05,0x800f0000,0xb0e40000,0xa0e40000,
                0x04,0x800f0000,0xb0e40001,0xa0e40001,0x80e40000,
                0x04,0x800f0000,0xb0e40002,0xa0e40001,0x80e40000,
                0x04,0x800f0000,0xb0e40003,0xa0e40000,0x80e40000,0xffff
            };
            // Screen-combine light only into the remaining headroom, then apply
            // the phosphor mask so bright areas retain visible scanlines.
            // (base + bloom * strength * (1 - base)) * mask
            constexpr DWORD composite[]{
                0xffff0101,0x42,0xb00f0000,0x42,0xb00f0001,0x42,0xb00f0002,
                0x03,0x800f0000,0xa0e40001,0xb0e40000,
                0x05,0x800f0000,0x80e40000,0xb0e40001,
                0x04,0x800f0000,0x80e40000,0xa0e40000,0xb0e40000,
                0x05,0x800f0000,0x80e40000,0xb0e40002,0xffff
            };
            HRESULT hr=D(87,bright,&CrtBrightShader);
            if(SUCCEEDED(hr))hr=D(87,blur,&CrtBlurShader);
            if(SUCCEEDED(hr))hr=D(87,composite,&CrtCompositeShader);
            if(SUCCEEDED(hr))hr=Texture(plan.SourceAllocationWidth,plan.SourceAllocationHeight,format,CrtBrightTexture,CrtBrightSurface);
            if(SUCCEEDED(hr))hr=Texture(plan.SourceAllocationWidth,plan.SourceAllocationHeight,format,CrtBlurTexture,CrtBlurSurface);
            if(FAILED(hr))return hr;
            hr=D(20,UINT(4),UINT(32),UINT(1),DWORD(0),DWORD(21),DWORD(1),&CrtMask.Value);
            if(FAILED(hr))return hr;
            a::LockedRect locked{};
            hr=a::Call<HRESULT>(CrtMask.Value,16,UINT(0),&locked,static_cast<const RECT*>(nullptr),DWORD(0));
            if(FAILED(hr))return hr;
            constexpr float phosphors[4][3]{{1.f,.988f,.988f},{.988f,1.f,.988f},{.988f,.988f,1.f},{.995f,.995f,.995f}};
            for(unsigned y=0;y<32;++y)for(unsigned x=0;x<4;++x)
            {
                const float beam=.85f+.15f*std::cos(6.28318530718f*static_cast<float>(y)/32.f);
                DWORD pixel=0xff000000;
                for(unsigned channel=0;channel<3;++channel)
                    pixel|=static_cast<DWORD>(255.f*beam*phosphors[x][2-channel]+.5f)<<(channel*8);
                reinterpret_cast<DWORD*>(static_cast<unsigned char*>(locked.Bits)+y*locked.Pitch)[x]=pixel;
            }
            return a::Call<HRESULT>(CrtMask.Value,17,UINT(0));
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

        HRESULT DrawCrtPass(void* target,UINT outputW,UINT outputH,DWORD shader,
            const std::array<CrtSample,4>& samples,const std::array<float,8>& constants)
        {
            HRESULT hr=S_OK;
            for(DWORD stage=0;stage<samples.size();++stage)
            {hr=D(61,stage,static_cast<void*>(nullptr));if(FAILED(hr))return hr;}
            hr=D(31,target,static_cast<void*>(nullptr));if(FAILED(hr))return hr;
            const a::Viewport viewport{0,0,outputW,outputH,0.f,1.f};
            hr=D(40,&viewport);if(FAILED(hr))return hr;
            hr=D(76,DWORD(0x404));if(FAILED(hr))return hr; // XYZRHW | TEX4
            hr=D(88,shader);if(FAILED(hr))return hr;
            hr=D(91,DWORD(0),constants.data(),DWORD(2));if(FAILED(hr))return hr;
            for(DWORD stage=0;stage<samples.size();++stage)
            {
                const auto& sample=samples[stage];
                hr=D(61,stage,sample.Texture);if(FAILED(hr))return hr;
                const DWORD address=sample.ScreenMask?1U:3U;
                const DWORD filter=sample.Linear?2U:1U;
                for(const auto state:std::array<std::pair<DWORD,DWORD>,8>{{
                    {11,stage},{13,address},{14,address},{16,filter},{17,filter},{18,0},{24,0},{28,1}}})
                {hr=D(63,stage,state.first,state.second);if(FAILED(hr))return hr;}
            }

            // Split at every sample's clamp boundary. Each interpolated UV then
            // clamps to valid pixels even when the render target has POT padding.
            const float width=float(outputW),height=float(outputH);
            std::array<float,10> xs{0,width},ys{0,height};
            std::size_t columns=2,rows=2;
            const auto add=[](auto& edges,std::size_t& count,float value,float limit)
            {
                if(value>0 && value<limit)edges[count++]=value;
            };
            for(const auto& sample:samples)
            {
                if(!sample.Texture || sample.ScreenMask)continue;
                add(xs,columns,(.5f-sample.OffsetX)*width/sample.Width,width);
                add(xs,columns,(float(sample.Width)-.5f-sample.OffsetX)*width/sample.Width,width);
                add(ys,rows,.5f*height/sample.Height,height);
                add(ys,rows,(float(sample.Height)-.5f)*height/sample.Height,height);
            }
            std::sort(xs.begin(),xs.begin()+columns);
            std::sort(ys.begin(),ys.begin()+rows);
            columns=std::size_t(std::unique(xs.begin(),xs.begin()+columns)-xs.begin());
            rows=std::size_t(std::unique(ys.begin(),ys.begin()+rows)-ys.begin());
            const auto vertex=[&](float x,float y)
            {
                CrtVertex result{x-.5f,y-.5f,0,1,{}};
                for(std::size_t stage=0;stage<samples.size();++stage)
                {
                    const auto& sample=samples[stage];
                    if(!sample.Texture)continue;
                    if(sample.ScreenMask)
                    {
                        result.Coordinates[stage*2]=x/4.f;
                        result.Coordinates[stage*2+1]=y/2.f-.25f;
                        continue;
                    }
                    const float bias=sample.Linear?0.f:1.f/1024.f;
                    result.Coordinates[stage*2]=(std::clamp(x*sample.Width/width+sample.OffsetX,
                        .5f,float(sample.Width)-.5f)+bias)/sample.AllocationWidth;
                    result.Coordinates[stage*2+1]=(std::clamp(y*sample.Height/height,
                        .5f,float(sample.Height)-.5f)+bias)/sample.AllocationHeight;
                }
                return result;
            };
            std::array<CrtVertex,486> vertices{};
            std::size_t count=0;
            for(std::size_t y=0;y+1<rows;++y)for(std::size_t x=0;x+1<columns;++x)
            {
                const auto tl=vertex(xs[x],ys[y]),tr=vertex(xs[x+1],ys[y]);
                const auto bl=vertex(xs[x],ys[y+1]),br=vertex(xs[x+1],ys[y+1]);
                for(const auto& point:{tl,tr,bl,tr,br,bl})vertices[count++]=point;
            }
            return D(72,DWORD(4),UINT(count/3),vertices.data(),UINT(sizeof(CrtVertex)));
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
                hr=D(72,DWORD(4),UINT(2),vertices,UINT(sizeof(Vertex)));
                if(FAILED(hr))return hr;
            }
            return S_OK;
        }

        RenderResult Render(void* source,void* target,const presentation::Plan& plan,presentation::PostProcess effect)
        {
            if(!source||!target||!plan.Supported)return {E_INVALIDARG,S_OK,"arguments"};
            a::SurfaceDescription input{},output{};
            HRESULT hr=a::Call<HRESULT>(source,8,&input);if(FAILED(hr))return {hr,S_OK,"source-description"};
            hr=a::Call<HRESULT>(target,8,&output);if(FAILED(hr))return {hr,S_OK,"target-description"};
            if(input.Width!=640||input.Height!=480||output.Width!=plan.Width||output.Height!=plan.Height||input.Format!=output.Format)
                return {a::InvalidCall,S_OK,"surface-layout"};
            if(ActiveEffect!=effect){Clear();ActiveEffect=effect;}
            const bool crt=effect==presentation::PostProcess::PhosphorLine;
            const bool xbrz=effect==presentation::PostProcess::Xbrz;
            const int ntscPreset = presentation::NtscPreset(effect);
            void* scalingSource = nullptr;
            ProcessingTimes times{};
            if(xbrz)
            {
#ifdef DFL_CHAPTER_TWO_XBRZ
                hr=PrepareXbrz(source,input.Format,plan.Width<=1280?2U:3U,times);
                if(FAILED(hr)){Clear();return {hr,S_OK,"xbrz-prepare",times};}
#else
                return {E_NOTIMPL,S_OK,"xbrz-unavailable"};
#endif
            }
            else
            {
                hr=Ensure(plan,input.Format);if(FAILED(hr))return {hr,S_OK,"allocate"};
                if(crt){hr=EnsureCrt(plan,input.Format);if(FAILED(hr)){Clear();return {hr,S_OK,"crt-allocate"};}}
                if (ntscPreset >= 0)
                {
#ifdef DFL_CHAPTER_TWO_NTSC
                    hr = PrepareNtsc(source, plan, input.Format, ntscPreset);
                    if (FAILED(hr)) { Clear(); return {hr, S_OK, "ntsc-prepare"}; }
                    scalingSource = NtscTexture.Value;
#else
                    return {E_NOTIMPL, S_OK, "ntsc-unavailable"};
#endif
                }
                else
                {
                    const RECT rect{0,0,640,480};const POINT point{0,0};
                    hr=D(28,source,&rect,UINT(1),SourceSurface.Value,&point);if(FAILED(hr))return {hr,S_OK,"copy"};
                    scalingSource = SourceTexture.Value;
                }
            }
            Saved saved(*this);hr=saved.Capture();if(FAILED(hr))return {hr,S_OK,"capture"};
            hr=D(34);if(FAILED(hr))return {hr,S_OK,"begin-scene"};
            hr=Configure();const char* stage="configure";
            if(SUCCEEDED(hr)&&plan.UseIntermediate&&!xbrz&&!crt)
            {
                stage="integer-pass";hr=Draw(IntermediateSurface.Value,scalingSource,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,
                    plan.IntermediateWidth,plan.IntermediateHeight,false);
            }
            if(SUCCEEDED(hr)&&crt)
            {
                const auto sample=[&](void* texture,float offset)
                {
                    return CrtSample{texture,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,offset};
                };
                constexpr std::array<float,8> threshold{.55f,.55f,.55f,0.f,
                    1.f/1.8f,1.f/1.8f,1.f/1.8f,.25f};
                constexpr std::array<float,8> weights{.125f,.125f,.125f,.125f,.375f,.375f,.375f,.375f};
                stage="crt-bright-pass";
                hr=DrawCrtPass(CrtBrightSurface.Value,640,480,CrtBrightShader,
                    {sample(SourceTexture.Value,0),{},{},{}},threshold);
                if(SUCCEEDED(hr))
                {
                    stage="crt-near-blur";
                    hr=DrawCrtPass(CrtBlurSurface.Value,640,480,CrtBlurShader,
                        {sample(CrtBrightTexture.Value,-1.5f),sample(CrtBrightTexture.Value,-.5f),
                         sample(CrtBrightTexture.Value,.5f),sample(CrtBrightTexture.Value,1.5f)},weights);
                }
                if(SUCCEEDED(hr))
                {
                    stage="crt-wide-blur";
                    hr=DrawCrtPass(CrtBrightSurface.Value,640,480,CrtBlurShader,
                        {sample(CrtBlurTexture.Value,-3.f),sample(CrtBlurTexture.Value,-1.f),
                         sample(CrtBlurTexture.Value,1.f),sample(CrtBlurTexture.Value,3.f)},weights);
                }
                if(SUCCEEDED(hr))
                {
                    // Reuse the near-blur target after its contribution reached
                    // the wide bloom. This narrow kernel softens horizontal
                    // pixel shoulders while keeping the original row structure.
                    stage="crt-base-reconstruction";
                    hr=DrawCrtPass(CrtBlurSurface.Value,640,480,CrtBlurShader,
                        {sample(SourceTexture.Value,-.65f),sample(SourceTexture.Value,-.15f),
                         sample(SourceTexture.Value,.15f),sample(SourceTexture.Value,.65f)},weights);
                }
                if(SUCCEEDED(hr)&&plan.UseIntermediate)
                {
                    stage="crt-integer-pass";
                    hr=Configure();
                    if(SUCCEEDED(hr))hr=Draw(IntermediateSurface.Value,CrtBlurTexture.Value,640,480,
                        plan.SourceAllocationWidth,plan.SourceAllocationHeight,plan.IntermediateWidth,plan.IntermediateHeight,false);
                }
            }
            if(SUCCEEDED(hr))
            {
                stage="output-pass";
                if(crt)
                {
                    stage="crt-composite";
                    const CrtSample base=plan.UseIntermediate
                        ?CrtSample{IntermediateTexture.Value,plan.IntermediateWidth,plan.IntermediateHeight,
                            plan.IntermediateAllocationWidth,plan.IntermediateAllocationHeight}
                        :CrtSample{CrtBlurTexture.Value,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,
                            0,plan.Filter==presentation::Mode::Linear};
                    const CrtSample bloom{CrtBrightTexture.Value,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight};
                    const CrtSample mask{CrtMask.Value,4,32,4,32,0,false,true};
                    constexpr std::array<float,8> strength{.6f,.6f,.6f,0,1,1,1,1};
                    hr=DrawCrtPass(target,plan.Width,plan.Height,CrtCompositeShader,{base,bloom,mask,{}},strength);
                }
                else
                {
#ifdef DFL_CHAPTER_TWO_XBRZ
                    if(xbrz)hr=Draw(target,XbrzTexture.Value,640*XbrzFactor,480*XbrzFactor,XbrzAllocationWidth,XbrzAllocationHeight,plan.Width,plan.Height,true);
                    else
#endif
                    hr=plan.UseIntermediate?Draw(target,IntermediateTexture.Value,plan.IntermediateWidth,plan.IntermediateHeight,
                        plan.IntermediateAllocationWidth,plan.IntermediateAllocationHeight,plan.Width,plan.Height,true)
                        :Draw(target,scalingSource,640,480,plan.SourceAllocationWidth,plan.SourceAllocationHeight,plan.Width,plan.Height,plan.Filter==presentation::Mode::Linear);
                }
            }
            const HRESULT ended=D(35);
            if(FAILED(ended)&&(SUCCEEDED(hr)||(!Lost(hr)&&Lost(ended)))){hr=ended;stage="end-scene";}
            const HRESULT restored=saved.Restore();
#ifdef DFL_CHAPTER_TWO_XBRZ
            if(xbrz){if(SUCCEEDED(hr)&&SUCCEEDED(restored))Xbrz->Commit();else Xbrz->Discard();}
#endif
            if(FAILED(restored))return {Lost(hr)?hr:restored,restored,Lost(hr)?stage:"restore"};
            return {hr,S_OK,SUCCEEDED(hr)?"ok":stage,times};
        }
    };

    Presenter::Presenter(void* device,void** originalTable) noexcept : implementation_(new(std::nothrow) Implementation(device,originalTable)) {}
    Presenter::~Presenter() {Clear();}
    void Presenter::Clear() noexcept {if(implementation_)implementation_->Clear();}
    RenderResult Presenter::Render(void* source,void* target,const presentation::Plan& plan,presentation::PostProcess effect) noexcept
    {
        if(!implementation_)return {E_OUTOFMEMORY,S_OK,"context"};
        try {return implementation_->Render(source,target,plan,effect);}
        catch(const std::bad_alloc&){implementation_->Clear();return {E_OUTOFMEMORY,S_OK,"allocate"};}
        catch(...){implementation_->Clear();return {E_FAIL,S_OK,"prepare"};}
    }
}
