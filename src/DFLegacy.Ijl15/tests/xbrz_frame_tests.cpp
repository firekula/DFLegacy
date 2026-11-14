#include "../src/xbrz_frame.h"
#include <algorithm>
#include <cstdio>
#include <stdexcept>

namespace
{
    unsigned Checks=0,Failures=0;
    void Check(bool value,const char* message)
    {
        ++Checks;
        if(!value){++Failures;std::fprintf(stderr,"FAIL xBRZ: %s\n",message);}
    }
    constexpr unsigned Width=40,Height=48;
    std::vector<std::uint32_t> Reference(const std::vector<std::uint32_t>& source,unsigned factor,xbrz::ColorFormat format)
    {
        auto opaque=source;
        for(auto& pixel:opaque)pixel|=0xff000000;
        std::vector<std::uint32_t> result(Width*Height*factor*factor);
        xbrz::scale(factor,opaque.data(),result.data(),Width,Height,format);
        return result;
    }
    void Throw(std::size_t,const std::uint32_t*,std::uint32_t*,int,int,xbrz::ColorFormat,const xbrz::ScalerCfg&,int,int)
    {throw std::runtime_error("synthetic scaler failure");}
}

int main()
{
    using namespace dfl::presentation;
    for(auto format:{xbrz::ColorFormat::rgb,xbrz::ColorFormat::argbUnbuffered})
    for(unsigned workers:{1u,2u,8u})for(unsigned factor:{2u,3u})
    {
        std::vector<std::uint32_t> source(Width*Height);
        for(unsigned y=0;y<Height;++y)for(unsigned x=0;x<Width;++x)
            source[y*Width+x]=((x/3+y/4)%3==0)?0x00c04020:((x/4+y/3)%3==0?0x8090c060:0xff102038);
        XbrzFrame frame(Width,Height,workers);XbrzWork work;
        Check(frame.Prepare(source.data(),Width*4,factor,format,work),"initial frame prepares");
        Check(frame.Output()==Reference(source,factor,format),"threaded initial frame equals the complete upstream scaler");
        Check(work.ScaledRows==Height&&work.Workers>=1&&work.Workers<=8,"initial work is bounded by source rows and eight workers");
        frame.Commit();
        Check(frame.Prepare(source.data(),Width*4,factor,format,work)&&work.ScaledRows==0&&work.ChangedRows==0,
            "a static frame reuses the committed result");
        frame.Commit();
        source[10*Width+3]^=0x003f6d89;
        Check(frame.Prepare(source.data(),Width*4,factor,format,work)&&work.ChangedRows==1&&work.ScaledRows==5,
            "one changed row expands by exactly two rows above and below");
        Check(frame.Output()==Reference(source,factor,format),"partial row update equals a full upstream frame");
        frame.Commit();
        source[3]^=0x00123456;source[(Height-1)*Width+Width-1]^=0x00432165;
        Check(frame.Prepare(source.data(),Width*4,factor,format,work)&&work.ScaledRows==6,
            "first and last row updates clip safely to the image");
        Check(frame.Output()==Reference(source,factor,format),"border updates equal a full upstream frame");
        frame.Commit();
        source[20*Width+2]^=0x003f6d89;
        frame.Prepare(source.data(),Width*4,factor,format,work);frame.Discard();
        source[20*Width+2]^=0x003f6d89;
        Check(frame.Prepare(source.data(),Width*4,factor,format,work)&&work.ScaledRows==Height,
            "a failed presentation invalidates the uncommitted row cache");
        Check(frame.Output()==Reference(source,factor,format),"rollback cannot expose pixels from an uncommitted frame");
        frame.Commit();
        std::vector<std::uint32_t> padded((Width+3)*Height,0xdeadbeef);
        for(unsigned row=0;row<Height;++row)std::copy_n(source.data()+row*Width,Width,padded.data()+row*(Width+3));
        Check(frame.Prepare(padded.data(),(Width+3)*4,factor,format,work)&&work.ScaledRows==0,
            "row padding is excluded from comparison and normalization");
        frame.Commit();
        Check(!frame.Prepare(source.data(),Width*4-1,factor,format,work),"short source pitch is rejected");
    }
    XbrzFrame broken(Width,Height,4,&Throw);XbrzWork work;
    std::vector<std::uint32_t> source(Width*Height,0xff405060);
    Check(!broken.Prepare(source.data(),Width*4,2,xbrz::ColorFormat::argbUnbuffered,work),
        "worker exceptions return to the render thread without escaping");
    std::printf("xBRZ frame: %u checks, %u failures\n",Checks,Failures);
    return Failures?1:0;
}
