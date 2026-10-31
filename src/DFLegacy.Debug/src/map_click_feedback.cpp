#include "map_click_feedback.h"

namespace dfl::map_click_feedback
{
    namespace
    {
        constexpr std::uint64_t PeriodMs=1750, SecondDelayMs=200, LifetimeMs=PeriodMs+SecondDelayMs;
        constexpr std::uint16_t Yellow(std::uint16_t source) noexcept
        {
            const auto value=static_cast<std::uint32_t>(source);
            const auto red=(value>>10)&31u, green=(value>>5)&31u, blue=value&31u;
            const auto rg=red>green?red:green;
            const auto intensity=rg>blue?rg:blue;
            return static_cast<std::uint16_t>((value&0x8000u)|(intensity<<10)|(intensity<<5));
        }
        constexpr RingPixels SourcePixels={
#include "map_click_ring_pixels.inc"
        };
        constexpr RingPixels YellowPixels() noexcept
        {
            RingPixels result{};
            for(std::size_t i=0;i<result.size();++i)result[i]=Yellow(SourcePixels[i]);
            return result;
        }
        constexpr auto Pixels=YellowPixels();
        bool Same(const Binding& a,const Binding& b) noexcept
        {
            return a.pid==b.pid && a.uid==b.uid && a.userToken==b.userToken
                && a.contextToken==b.contextToken && a.minimapToken==b.minimapToken
                && a.planeToken==b.planeToken && a.processStartedFileTime==b.processStartedFileTime;
        }
        std::uint32_t Channel(float start,float end,float fraction) noexcept
        { return static_cast<std::uint32_t>(start+(end-start)*fraction); }
        RingState Interpolate(float scaleStart,float scaleEnd,float grayStart,float grayEnd,
                              float alphaStart,float alphaEnd,std::uint64_t elapsed,std::uint64_t duration) noexcept
        {
            const auto fraction=static_cast<float>(elapsed)/static_cast<float>(duration);
            const auto gray=Channel(grayStart,grayEnd,fraction), alpha=Channel(alphaStart,alphaEnd,fraction);
            return {gray!=0 && alpha!=0,scaleStart+(scaleEnd-scaleStart)*fraction,
                (alpha<<24)|(gray<<16)|(gray<<8)|gray};
        }
    }

    std::uint16_t Tint1555(std::uint16_t source) noexcept { return Yellow(source); }
    const RingPixels& GetYellowPixels() noexcept { return Pixels; }
    void Feedback::Start(const Binding& binding,Point point,std::uint64_t now) noexcept
    { binding_=binding;point_=point;started_=lastTick_=now;active_=true; }
    void Feedback::Update(const Binding& binding,bool visible,std::uint64_t now) noexcept
    {
        if(!active_)return;
        if(!visible || !Same(binding_,binding) || now<lastTick_ || now-started_>=LifetimeMs)
        {Reset();return;}
        lastTick_=now;
    }
    void Feedback::Reset() noexcept
    { binding_={};point_={};started_=lastTick_=0;active_=false; }
    bool Feedback::Active() const noexcept { return active_; }
    Point Feedback::GetPoint() const noexcept { return point_; }
    std::uint64_t Feedback::StartedTick() const noexcept { return started_; }
    RingState Feedback::Ring(std::size_t index,std::uint64_t now) const noexcept
    {
        if(!active_ || index>=2 || now<started_)return {};
        const auto elapsed=now-started_;
        const std::uint64_t delay=index==1?SecondDelayMs:0;
        if(elapsed<delay || elapsed-delay>=PeriodMs)return {};
        const auto tick=elapsed-delay;
        // Original minimap_effect_dodge.ani: 50/500/700/400/100ms,
        // interpolation flags 0/1/1/1/0. RGB and alpha both fade.
        if(tick<50)return {true,.5f,0xFFFFFFFF};
        if(tick<550)return Interpolate(.5f,1.f,255.f,150.f,255.f,120.f,tick-50,500);
        if(tick<1250)return Interpolate(1.f,1.f,150.f,10.f,120.f,10.f,tick-550,700);
        if(tick<1650)return Interpolate(1.f,1.f,10.f,0.f,10.f,0.f,tick-1250,400);
        return {false,1.f,0};
    }
}
