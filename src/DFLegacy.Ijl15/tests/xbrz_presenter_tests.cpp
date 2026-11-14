#include "../src/d3d8_presenter.h"
#include "xbrz.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <vector>

namespace
{
    namespace a=dfl::d3d8;
    namespace p=dfl::presentation;
    unsigned Checks=0,Failures=0,Readbacks=0;
    void** Original=nullptr;
    bool ForcePot=false,FailAllocation=false,FailDraw=false;
    void Check(bool value,const char* text){++Checks;if(!value){++Failures;std::fprintf(stderr,"FAIL xBRZ GPU: %s\n",text);}}
    struct Ref {void* Value=nullptr;~Ref(){if(Value)a::Call<ULONG>(Value,2);}};
    HRESULT WINAPI Caps(void* device,a::Caps* caps)
    {const auto hr=a::CallTable<HRESULT>(device,Original,7,caps);if(ForcePot)caps->TextureCaps|=34;return hr;}
    HRESULT WINAPI Image(void* device,UINT w,UINT h,DWORD format,void** target)
    {return FailAllocation?E_OUTOFMEMORY:a::CallTable<HRESULT>(device,Original,27,w,h,format,target);}
    HRESULT WINAPI Copy(void* device,void* from,const RECT* rect,UINT count,void* to,const POINT* point)
    {if(!rect&&!count)++Readbacks;return a::CallTable<HRESULT>(device,Original,28,from,rect,count,to,point);}
    HRESULT WINAPI Draw(void* device,DWORD type,UINT count,const void* vertices,UINT stride)
    {if(FailDraw){FailDraw=false;return E_FAIL;}return a::CallTable<HRESULT>(device,Original,72,type,count,vertices,stride);}
    std::vector<std::uint32_t> Pixels(const wchar_t* path)
    {
        std::vector<std::uint32_t> pixels(640*480);
        if(path)
        {
            std::ifstream stream(std::filesystem::path(path),std::ios::binary);
            if(!stream.read(reinterpret_cast<char*>(pixels.data()),std::streamsize(pixels.size()*4)))return {};
            for(auto& pixel:pixels)pixel|=0xff000000;
            return pixels;
        }
        for(unsigned y=0;y<480;++y)for(unsigned x=0;x<640;++x)
            pixels[y*640+x]=0xff000000|(((x/5+y/7)%3==0)?0xe0c040:((x/7+y/5)%3==0?0x104070:0xc04020));
        return pixels;
    }
    void Upload(void* surface,const std::vector<std::uint32_t>& pixels)
    {
        a::LockedRect lock{};Check(SUCCEEDED(a::Call<HRESULT>(surface,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0))),"source locks");
        if(!lock.Bits)return;
        for(unsigned y=0;y<480;++y)std::memcpy(static_cast<unsigned char*>(lock.Bits)+y*lock.Pitch,pixels.data()+y*640,640*4);
        a::Call<HRESULT>(surface,10);
    }
    std::vector<std::uint32_t> Read(void* surface,unsigned w,unsigned h)
    {
        a::LockedRect lock{};if(FAILED(a::Call<HRESULT>(surface,9,&lock,static_cast<const RECT*>(nullptr),DWORD(0x10))))return {};
        std::vector<std::uint32_t> pixels(w*h);
        for(unsigned y=0;y<h;++y)std::memcpy(pixels.data()+y*w,static_cast<unsigned char*>(lock.Bits)+y*lock.Pitch,w*4);
        a::Call<HRESULT>(surface,10);return pixels;
    }
    void Save(const std::filesystem::path& path,const std::vector<std::uint32_t>& pixels,unsigned width,unsigned height)
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
    bool Matches(const std::vector<std::uint32_t>& gpu,unsigned w,unsigned h,const std::vector<std::uint32_t>& source,unsigned factor)
    {
        const unsigned sw=640*factor,sh=480*factor;
        std::vector<std::uint32_t> scaled(sw*sh);
        xbrz::scale(factor,source.data(),scaled.data(),640,480,xbrz::ColorFormat::argbUnbuffered);
        if(gpu.size()!=std::size_t(w)*h)return false;
        for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x)
        {
            const double sx=std::clamp((x+.5)*sw/w-.5,0.0,double(sw-1));
            const double sy=std::clamp((y+.5)*sh/h-.5,0.0,double(sh-1));
            const auto left=unsigned(sx),top=unsigned(sy),right=std::min(left+1,sw-1),bottom=std::min(top+1,sh-1);
            const double fx=sx-left,fy=sy-top;
            for(unsigned channel=0;channel<3;++channel)
            {
                const auto value=[&](unsigned xx,unsigned yy){return double((scaled[yy*sw+xx]>>(channel*8))&255);};
                const auto expected=(1-fy)*((1-fx)*value(left,top)+fx*value(right,top))+fy*((1-fx)*value(left,bottom)+fx*value(right,bottom));
                const auto actual=double((gpu[y*w+x]>>(channel*8))&255);
                if(std::abs(actual-expected)>2)return false;
            }
        }
        return true;
    }
}

int wmain(int argc,wchar_t** argv)
{
    const auto instance=GetModuleHandleW(nullptr);
    const auto window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"xBRZ offscreen acceptance",WS_POPUP,0,0,1280,960,nullptr,nullptr,instance,nullptr);
    const auto library=LoadLibraryExW(L"d3d8.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    using Create=void*(WINAPI*)(UINT);const auto create=library?reinterpret_cast<Create>(GetProcAddress(library,"Direct3DCreate8")):nullptr;
    Ref factory;factory.Value=create?create(220):nullptr;if(!window||!factory.Value)return 2;
    a::DisplayMode display{};a::Call<HRESULT>(factory.Value,8,UINT(0),&display);
    a::PresentationParameters pp{};pp.BackBufferWidth=640;pp.BackBufferHeight=480;pp.BackBufferFormat=display.Format;pp.BackBufferCount=1;
    pp.SwapEffect=1;pp.DeviceWindow=window;pp.Windowed=TRUE;pp.Flags=1;
    Ref device;if(FAILED(a::Call<HRESULT>(factory.Value,15,UINT(0),DWORD(1),window,DWORD(0x22),&pp,&device.Value)))return 2;
    Original=*static_cast<void***>(device.Value);
    std::array<void*,a::DeviceMethods> table{};std::copy_n(Original,table.size(),table.begin());
    table[7]=reinterpret_cast<void*>(&Caps);table[27]=reinterpret_cast<void*>(&Image);table[28]=reinterpret_cast<void*>(&Copy);table[72]=reinterpret_cast<void*>(&Draw);
    for(unsigned cycle=0;cycle<2;++cycle)
    {
        {
            const DWORD format=cycle?21:display.Format;
            Ref source;
            if(cycle)a::Call<HRESULT>(device.Value,25,UINT(640),UINT(480),format,DWORD(0),BOOL(TRUE),&source.Value);
            else a::Call<HRESULT>(device.Value,16,UINT(0),DWORD(0),&source.Value);
            auto pixels=Pixels(argc>=2?argv[1]:nullptr);if(pixels.empty()||!source.Value)return 2;
            auto stored=pixels;if(cycle)for(auto& pixel:stored)pixel&=0xffffff;
            Upload(source.Value,stored);
            dfl::d3d8_presenter::Presenter renderer(device.Value,table.data());
            for(unsigned width:{1280u,1600u})
            {
                ForcePot=cycle!=0;
                const unsigned height=width*3/4,factor=width<=1280?2:3;
                const auto plan=p::MakePlan(width,height,p::Mode::Point,{4096,4096,ForcePot,ForcePot});
                Ref target;a::Call<HRESULT>(device.Value,25,width,height,format,DWORD(0),BOOL(TRUE),&target.Value);
                renderer.Clear();const auto previous=Readbacks;
                const auto disabled=renderer.Render(source.Value,target.Value,plan);
                Check(SUCCEEDED(disabled.Status)&&Readbacks==previous&&disabled.Times.Workers==0,"disabled xBRZ performs no CPU readback or worker processing");
                if(argc>=3&&!cycle&&width==1280)Save(std::filesystem::path(argv[2])/L"xbrz-off.bmp",Read(target.Value,width,height),width,height);
                auto first=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
                Check(SUCCEEDED(first.Status)&&first.Times.ScaledRows==480&&first.Times.Workers>=1&&first.Times.Workers<=8,"first xBRZ frame uses a bounded worker pool");
                Check(Matches(Read(target.Value,width,height),width,height,pixels,factor),"actual GPU output matches the complete upstream xBRZ scaler and bilinear output");
                if(argc>=3&&!cycle&&width==1280)Save(std::filesystem::path(argv[2])/L"xbrz-on.bmp",Read(target.Value,width,height),width,height);
                const auto repeated=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
                Check(SUCCEEDED(repeated.Status)&&repeated.Times.ScaledRows==0&&repeated.Times.UploadUs==0,"unchanged frames reuse CPU result and uploaded texture");
                pixels[10*640+4]^=0x00182534;Upload(source.Value,pixels);
                const auto changed=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
                Check(SUCCEEDED(changed.Status)&&changed.Times.ScaledRows==5,"a changed GPU source row recomputes its two-row halo");
                Check(Matches(Read(target.Value,width,height),width,height,pixels,factor),"partial GPU update is pixel-equivalent to complete upstream scaling");
                const auto stationary=pixels;
                for(unsigned row=50;row<385;++row)for(unsigned x=0;x<640;++x)
                    pixels[row*640+x]=stationary[row*640+(x+1)%640];
                Upload(source.Value,pixels);
                const auto moving=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
                Check(SUCCEEDED(moving.Status)&&moving.Times.ScaledRows<=339,"moving middle rows keep a bounded two-row halo");
                Check(Matches(Read(target.Value,width,height),width,height,pixels,factor),"moving frame equals complete upstream scaling");
                pixels[20*640+9]^=0x00395418;Upload(source.Value,pixels);FailDraw=true;
                Check(FAILED(renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz).Status),"failed output draw returns failure");
                pixels[20*640+9]^=0x00395418;Upload(source.Value,pixels);
                const auto retried=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
                Check(SUCCEEDED(retried.Status)&&retried.Times.ScaledRows==480,"failed rendering leaves no committed row cache");
                Check(Matches(Read(target.Value,width,height),width,height,pixels,factor),"retry never displays uncommitted scaled pixels");
                renderer.Clear();FailAllocation=true;
                Check(renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz).Status==E_OUTOFMEMORY,"readback allocation failure remains recoverable");
                FailAllocation=false;
                Check(SUCCEEDED(renderer.Render(source.Value,target.Value,plan).Status),"original scaling remains usable after allocation failure");
                std::printf("xBRZ GPU %ux%u pot=%u readback=%.3fus scale=%.3fus upload=%.3fus staticRows=%u changedRows=%u\n",
                    width,height,ForcePot?1u:0u,first.Times.ReadbackUs,first.Times.ScaleUs,first.Times.UploadUs,repeated.Times.ScaledRows,changed.Times.ScaledRows);
                std::printf("xBRZ moving %ux%u readback=%.3fus scale=%.3fus upload=%.3fus rows=%u workers=%u\n",
                    width,height,moving.Times.ReadbackUs,moving.Times.ScaleUs,moving.Times.UploadUs,moving.Times.ScaledRows,moving.Times.Workers);
            }
        }
        Check(SUCCEEDED(a::Call<HRESULT>(device.Value,14,&pp)),"device resets after xBRZ resources and workers are released");
    }
    {
        Ref source,target;
        const auto input=a::Call<HRESULT>(device.Value,25,UINT(640),UINT(480),DWORD(23),DWORD(0),BOOL(FALSE),&source.Value);
        const auto output=a::Call<HRESULT>(device.Value,25,UINT(1280),UINT(960),DWORD(23),DWORD(0),BOOL(FALSE),&target.Value);
        Check(SUCCEEDED(input)&&SUCCEEDED(output),"16-bit fallback fixture is available");
        if(SUCCEEDED(input)&&SUCCEEDED(output))
        {
            const auto plan=p::MakePlan(1280,960,p::Mode::Point,{4096,4096,false,false});
            dfl::d3d8_presenter::Presenter renderer(device.Value,table.data());
            const auto previous=Readbacks;
            const auto unsupported=renderer.Render(source.Value,target.Value,plan,p::PostProcess::Xbrz);
            Check(FAILED(unsupported.Status)&&Readbacks==previous&&unsupported.Times.Workers==0,
                "unsupported pixel formats return to the fallback without readback or workers");
            Check(SUCCEEDED(renderer.Render(source.Value,target.Value,plan).Status),"16-bit format keeps the original scaling path");
        }
    }
    DestroyWindow(window);
    std::printf("xBRZ GPU: %u checks, %u failures\n",Checks,Failures);
    return Failures?1:0;
}
