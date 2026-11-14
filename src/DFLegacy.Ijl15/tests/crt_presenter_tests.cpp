#include "../src/d3d8_presenter.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <vector>

namespace
{
    namespace a = dfl::d3d8;
    namespace p = dfl::presentation;
    unsigned Checks = 0, Failures = 0;
    unsigned TextureCreates = 0, ShaderCreates = 0;
    void** DeviceTable = nullptr;
    HRESULT WINAPI CreateTexture(void* device,UINT width,UINT height,UINT levels,DWORD usage,DWORD format,DWORD pool,void** result)
    {
        ++TextureCreates;
        return a::CallTable<HRESULT>(device,DeviceTable,20,width,height,levels,usage,format,pool,result);
    }
    HRESULT WINAPI CreateShader(void* device,const DWORD* code,DWORD* result)
    {
        ++ShaderCreates;
        return a::CallTable<HRESULT>(device,DeviceTable,87,code,result);
    }
    void Check(bool value, const char* text)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL CRT: %s\n", text); }
    }
    struct Ref
    {
        void* Value = nullptr;
        ~Ref() { Reset(); }
        void Reset() { if (Value) { a::Call<ULONG>(Value, 2); Value = nullptr; } }
    };
    dfl::d3d8_presenter::RenderResult RenderCrt(dfl::d3d8_presenter::Presenter& presenter,
        void* source, void* target, const p::Plan& plan)
    {
        return presenter.Render(source, target, plan, p::PostProcess::PhosphorLine);
    }
    void Fill(void* surface, bool picture)
    {
        a::LockedRect lock{};
        Check(SUCCEEDED(a::Call<HRESULT>(surface, 9, &lock, static_cast<const RECT*>(nullptr), DWORD(0))), "source locks");
        if (!lock.Bits) return;
        constexpr DWORD bars[]{0xffff8080,0xffffff80,0xff80ff80,0xff80ffff,0xff8080ff,0xffff80ff,0xffdddddd,0xff303030};
        for (unsigned y=0;y<480;++y) for(unsigned x=0;x<640;++x)
        {
            DWORD pixel=0xff808080;
            if(picture)
            {
                pixel=bars[x/80];
                if(y>180) pixel=0xff102040|((x/16+y/16)%2 ? 0x182820 : 0);
                if(x>190&&x<450&&y>200&&y<420) pixel=(x<220||x>420||y<230||y>390)?0xffffb830:0xffcc3030;
                if(y>48&&y<128)pixel=((x/8+y/8)%2)?0xffeeeeee:0xff202020;
                if(x==0||y==0||x==639||y==479)pixel=0xffffffff;
            }
            reinterpret_cast<DWORD*>(static_cast<unsigned char*>(lock.Bits)+y*lock.Pitch)[x]=pixel;
        }
        Check(SUCCEEDED(a::Call<HRESULT>(surface,10)), "source unlocks");
    }
    std::vector<DWORD> Read(void* surface, unsigned width, unsigned height)
    {
        a::LockedRect lock{};
        if(FAILED(a::Call<HRESULT>(surface,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0x10)))) return {};
        std::vector<DWORD> pixels(width*height);
        for(unsigned y=0;y<height;++y)
            std::memcpy(pixels.data()+y*width,static_cast<unsigned char*>(lock.Bits)+y*lock.Pitch,width*4);
        a::Call<HRESULT>(surface,10);
        return pixels;
    }
    void Save(const std::filesystem::path& path,const std::vector<DWORD>& pixels,unsigned width,unsigned height)
    {
        BITMAPFILEHEADER file{};file.bfType=0x4d42;file.bfOffBits=sizeof(file)+sizeof(BITMAPINFOHEADER);
        file.bfSize=file.bfOffBits+width*height*4;
        BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=LONG(width);info.biHeight=-LONG(height);
        info.biPlanes=1;info.biBitCount=32;info.biSizeImage=width*height*4;
        std::ofstream stream(path,std::ios::binary);
        stream.write(reinterpret_cast<const char*>(&file),sizeof(file));
        stream.write(reinterpret_cast<const char*>(&info),sizeof(info));
        stream.write(reinterpret_cast<const char*>(pixels.data()),pixels.size()*4);
        Check(stream.good(),"actual GPU image is saved");
    }
}

int wmain(int argc,wchar_t** argv)
{
    const auto instance=GetModuleHandleW(nullptr);
    HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"CRT offscreen acceptance",
        WS_POPUP,0,0,1280,960,nullptr,nullptr,instance,nullptr);
    const auto library=LoadLibraryExW(L"d3d8.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    using Create=void*(WINAPI*)(UINT);
    const auto create=library?reinterpret_cast<Create>(GetProcAddress(library,"Direct3DCreate8")):nullptr;
    Ref factory;factory.Value=create?create(220):nullptr;
    if(!window||!factory.Value)return 2;
    a::DisplayMode display{};
    if(FAILED(a::Call<HRESULT>(factory.Value,8,UINT(0),&display)))return 2;
    a::PresentationParameters parameters{};
    parameters.BackBufferWidth=640;parameters.BackBufferHeight=480;parameters.BackBufferFormat=display.Format;
    parameters.BackBufferCount=1;parameters.SwapEffect=1;parameters.DeviceWindow=window;parameters.Windowed=TRUE;parameters.Flags=1;
    Ref device;
    const auto created=a::Call<HRESULT>(factory.Value,15,UINT(0),DWORD(1),window,DWORD(0x22),&parameters,&device.Value);
    if(FAILED(created)){std::fprintf(stderr,"GPU_UNAVAILABLE %08lx\n",static_cast<unsigned long>(created));return 2;}
    a::Caps caps{};a::Call<HRESULT>(device.Value,7,&caps);
    const p::Capabilities capabilities{caps.MaxTextureWidth,caps.MaxTextureHeight,(caps.TextureCaps&2)!=0,(caps.TextureCaps&32)!=0};
    DeviceTable=*static_cast<void***>(device.Value);
    std::array<void*,a::DeviceMethods> observed{};
    std::copy_n(DeviceTable,observed.size(),observed.begin());
    observed[20]=reinterpret_cast<void*>(&CreateTexture);observed[87]=reinterpret_cast<void*>(&CreateShader);
    for(unsigned cycle=0;cycle<3;++cycle)
    {
        {
            Ref source;a::Call<HRESULT>(device.Value,16,UINT(0),DWORD(0),&source.Value);
            Fill(source.Value,false);
            dfl::d3d8_presenter::Presenter presenter(device.Value,observed.data());
            Ref savedTexture;
            a::Call<HRESULT>(device.Value,20,UINT(4),UINT(4),UINT(1),DWORD(0),display.Format,DWORD(1),&savedTexture.Value);
            constexpr DWORD passThrough[]{0xffff0101,0x42,0xb00f0000,0x01,0x800f0000,0xb0e40000,0xffff};
            DWORD savedShader=0;
            Check(SUCCEEDED(a::Call<HRESULT>(device.Value,87,passThrough,&savedShader)),"incoming shader compiles");
            struct Case{unsigned Width,Height;p::Mode Mode;bool Pot;};
            const Case cases[]{{640,480,p::Mode::Point,false},{800,600,p::Mode::Sharp,false},
                {960,720,p::Mode::Sharp,false},{1024,768,p::Mode::Sharp,false},{1280,960,p::Mode::Point,false},
                {1600,1200,p::Mode::Linear,false},{800,600,p::Mode::Linear,true},{800,600,p::Mode::Sharp,true}};
            for(const auto& trial:cases)
            {
                const std::array<unsigned,2> size{trial.Width,trial.Height};
                Ref target;
                Check(SUCCEEDED(a::Call<HRESULT>(device.Value,25,size[0],size[1],display.Format,DWORD(0),BOOL(TRUE),&target.Value)),"output target allocates");
                if(!target.Value)continue;
                auto requested=capabilities;requested.PowerOfTwo=requested.PowerOfTwo||trial.Pot;
                const auto plan=p::MakePlan(size[0],size[1],trial.Mode,requested);
                const a::Viewport view{3,4,600,450,.2f,.8f};
                a::Call<HRESULT>(device.Value,40,&view);
                a::Call<HRESULT>(device.Value,50,DWORD(27),DWORD(1));
                a::Call<HRESULT>(device.Value,50,DWORD(60),DWORD(0x12345678));
                constexpr std::array<float,8> constants{.2f,.3f,.4f,.5f,.6f,.7f,.8f,.9f};
                a::Call<HRESULT>(device.Value,91,DWORD(0),constants.data(),DWORD(2));
                a::Call<HRESULT>(device.Value,88,savedShader);
                for(DWORD stage=0;stage<4;++stage)a::Call<HRESULT>(device.Value,61,stage,savedTexture.Value);
                const auto rendered=RenderCrt(presenter,source.Value,target.Value,plan);
                Check(SUCCEEDED(rendered.Status)&&SUCCEEDED(rendered.RestoreStatus),"CRT renders on the real D3D8 device");
                if(FAILED(rendered.Status)){std::fprintf(stderr,"CRT_STAGE %s %08lx\n",rendered.Stage,static_cast<unsigned long>(rendered.Status));continue;}
                a::Viewport restored{};a::Call<HRESULT>(device.Value,41,&restored);
                Check(std::memcmp(&view,&restored,sizeof(view))==0,"viewport is restored");
                DWORD state=0;a::Call<HRESULT>(device.Value,51,DWORD(27),&state);Check(state==1,"alpha blend is restored");
                a::Call<HRESULT>(device.Value,51,DWORD(60),&state);Check(state==0x12345678,"texture factor is restored");
                std::array<float,8> restoredConstants{};a::Call<HRESULT>(device.Value,92,DWORD(0),restoredConstants.data(),DWORD(2));
                Check(restoredConstants==constants,"pixel shader constants are restored");
                a::Call<HRESULT>(device.Value,89,&state);Check(state==savedShader,"incoming pixel shader is restored");
                for(DWORD stage=0;stage<4;++stage)
                {
                    Ref restoredTexture;a::Call<HRESULT>(device.Value,60,stage,&restoredTexture.Value);
                    Check(restoredTexture.Value==savedTexture.Value,"all four texture bindings are restored");
                }
                const auto pixels=Read(target.Value,size[0],size[1]);
                Check(!pixels.empty(),"GPU output can be read by the acceptance fixture");
                if(pixels.empty())continue;
                const auto x=size[0]/2,y=size[1]/2;
                Check((pixels[y*size[0]+x]&0xffffff)!=(pixels[(y+1)*size[0]+x]&0xffffff),"fine horizontal scanlines appear on flat grey");
                Check((pixels[y*size[0]+x]&0xffffff)!=(pixels[y*size[0]+x+1]&0xffffff),"RGB phosphor structure appears across columns");
                const auto textureCount=TextureCreates,shaderCount=ShaderCreates;
                Check(SUCCEEDED(RenderCrt(presenter,source.Value,target.Value,plan).Status)&&
                    TextureCreates==textureCount&&ShaderCreates==shaderCount,"steady CRT frames reuse textures and shader");
                const auto off=presenter.Render(source.Value,target.Value,plan);
                Check(SUCCEEDED(off.Status),"disabled CRT returns to the existing renderer");
                const auto plain=Read(target.Value,size[0],size[1]);
                Check(!plain.empty()&&std::all_of(plain.begin(),plain.end(),[](DWORD pixel){return (pixel&0xffffff)==0x808080;}),"default rendering retains every unfiltered grey pixel");
                if(argc==2&&cycle==0&&size[0]==1280)
                {
                    Fill(source.Value,true);
                    presenter.Render(source.Value,target.Value,plan);
                    const auto baseline=Read(target.Value,size[0],size[1]);
                    RenderCrt(presenter,source.Value,target.Value,plan);
                    const auto crt=Read(target.Value,size[0],size[1]);
                    std::filesystem::create_directories(argv[1]);
                    Save(std::filesystem::path(argv[1])/L"crt-off.bmp",baseline,size[0],size[1]);
                    Save(std::filesystem::path(argv[1])/L"phosphor-line-2.bmp",crt,size[0],size[1]);
                    Fill(source.Value,false);
                }
            }
            a::Call<HRESULT>(device.Value,88,DWORD(0));
            for(DWORD stage=0;stage<4;++stage)a::Call<HRESULT>(device.Value,61,stage,static_cast<void*>(nullptr));
            a::Call<HRESULT>(device.Value,90,savedShader);
            const auto original=Read(source.Value,640,480);
            Check(!original.empty()&&std::all_of(original.begin(),original.end(),[](DWORD pixel){return (pixel&0xffffff)==0x808080;}),"CRT keeps the game's source pixels intact");
        }
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,14,&parameters)),"device resets after all CRT resources are released");
    }
    device.Reset();factory.Reset();FreeLibrary(library);DestroyWindow(window);
    std::printf("CRT GPU: %u checks, %u failures\n",Checks,Failures);
    return Failures?1:0;
}
