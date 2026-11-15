#include "../src/map_click_feedback.h"
#include <cmath>
#include <cstdio>
#include <limits>

namespace
{
    using namespace dfl::map_click_feedback;
    int Failures=0;
    void Check(bool okay,const char* name)
    { if(!okay){++Failures;std::fprintf(stderr,"FAIL map click feedback: %s\n",name);} }
    Binding Identity() noexcept { return {7,23,0x100000,0x200000,0x300000,0x400000,900}; }
    void Sample(const Feedback& feedback,std::size_t index,std::uint64_t now,
                bool visible,float scale,std::uint32_t color,const char* label)
    {
        const auto ring=feedback.Ring(index,now);
        Check(ring.visible==visible && std::fabs(ring.scale-scale)<0.00001f && ring.color==color,label);
    }
    void Timing()
    {
        Feedback feedback;
        Check(!feedback.Active() && !feedback.Ring(0,1000).visible,"initially inactive");
        feedback.Start(Identity(),{80,-35},1000);
        Check(feedback.Active() && feedback.StartedTick()==1000,"start activates exact clock");
        Check(feedback.GetPoint().x==80 && feedback.GetPoint().y==-35,"map local point preserved");
        Sample(feedback,0,1000,true,.5f,0xFFFFFFFF,"first frame");
        Sample(feedback,0,1049,true,.5f,0xFFFFFFFF,"initial 50ms hold");
        Sample(feedback,0,1050,true,.5f,0xFFFFFFFF,"growth begins at 50ms");
        Sample(feedback,0,1300,true,.75f,0xBBCACACA,"mid growth fades RGB and alpha");
        Sample(feedback,0,1550,true,1.f,0x78969696,"second keyframe");
        Sample(feedback,0,1900,true,1.f,0x41505050,"mid long fade");
        Sample(feedback,0,2250,true,1.f,0x0A0A0A0A,"third keyframe");
        Sample(feedback,0,2450,true,1.f,0x05050505,"mid final fade");
        Sample(feedback,0,2650,false,1.f,0,"transparent final frame");
        Check(!feedback.Ring(0,2750).visible,"first ring never loops");
        Check(!feedback.Ring(1,1199).visible,"second ring delayed");
        Sample(feedback,1,1200,true,.5f,0xFFFFFFFF,"second starts at 200ms");
        Sample(feedback,1,1500,true,.75f,0xBBCACACA,"second owns shifted original curve");
        Check(!feedback.Ring(1,2950).visible && !feedback.Ring(2,1300).visible,"exactly two finite rings");
        bool shifted=true;
        for(std::uint64_t tick=0;tick<1750;++tick)
        {
            const auto first=feedback.Ring(0,1000+tick),second=feedback.Ring(1,1200+tick);
            shifted &= first.visible==second.visible && first.scale==second.scale && first.color==second.color;
        }
        Check(shifted,"both rings have the same original 1750ms curve");
        feedback.Update(Identity(),true,2949);
        Check(feedback.Active(),"lifetime remains active through 1949ms");
        feedback.Update(Identity(),true,2950);
        Check(!feedback.Active(),"lifetime expires at 1950ms");
        feedback.Update(Identity(),true,4000);
        Check(!feedback.Active(),"expiry never restarts");
    }
    void IdentityAndClock()
    {
        const auto original=Identity();
        for(unsigned field=0;field<7;++field)
        {
            auto changed=original;
            switch(field)
            {
            case 0:++changed.pid;break;
            case 1:++changed.uid;break;
            case 2:++changed.userToken;break;
            case 3:++changed.contextToken;break;
            case 4:++changed.minimapToken;break;
            case 5:++changed.planeToken;break;
            default:++changed.processStartedFileTime;break;
            }
            Feedback feedback;feedback.Start(original,{1,2},1000);feedback.Update(changed,true,1001);
            Check(!feedback.Active(),"each bound identity field cancels");
        }
        Feedback feedback;feedback.Start(original,{1,2},1000);
        feedback.Update(original,true,1100);
        Check(feedback.Active(),"same binding survives normal cross area");
        feedback.Update(original,true,1099);
        Check(!feedback.Active(),"clock regression after valid update cancels");
        feedback.Start(original,{1,2},1000);feedback.Update(original,true,999);
        Check(!feedback.Active(),"clock regression before start cancels");
        feedback.Start(original,{1,2},1000);feedback.Update(original,false,1100);
        Check(!feedback.Active(),"hidden map cancels");
        feedback.Start(original,{1,2},1000);feedback.Start(original,{-200,500},1200);
        Check(feedback.StartedTick()==1200 && feedback.GetPoint().x==-200 && feedback.GetPoint().y==500,"new click replaces point and clock");
        Check(!feedback.Ring(1,1399).visible && feedback.Ring(1,1400).visible,"replacement resets delayed ring");
        Check(!feedback.Ring(0,1199).visible,"ring query cannot underflow start");
        feedback.Reset();Check(!feedback.Active() && !feedback.Ring(0,1200).visible,"explicit reset clears drawing");
        const auto high=std::numeric_limits<std::uint64_t>::max()-2000;
        feedback.Start(original,{1,2},high);feedback.Update(original,true,high+1949);
        Check(feedback.Active(),"high tick lifetime does not overflow");
        feedback.Update(original,true,high+1950);Check(!feedback.Active(),"high tick expires safely");
        feedback.Start(original,{1,2},0);Check(feedback.Active() && feedback.Ring(0,0).visible,"zero tick is valid");
    }
    void Pixels()
    {
        Check(Tint1555(0x801F)==0xFFE0,"blue source intensity maps to yellow");
        Check(Tint1555(0x83E0)==0xFFE0 && Tint1555(0xFC00)==0xFFE0,"max RGB includes every channel");
        Check(Tint1555(0x8011)==0xC620 && Tint1555(0x0011)==0x4620,"five bit intensity and alpha preserved");
        bool exhaustive=true;
        for(std::uint32_t value=0;value<=0xFFFF;++value)
        {
            const auto result=static_cast<std::uint32_t>(Tint1555(static_cast<std::uint16_t>(value)));
            const auto red=(value>>10)&31u,green=(value>>5)&31u,blue=value&31u;
            const auto maxRG=red>green?red:green;const auto expected=maxRG>blue?maxRG:blue;
            exhaustive &= (result&0x8000)==(value&0x8000) && (result&31)==0
                && ((result>>5)&31)==expected && ((result>>10)&31)==expected;
        }
        Check(exhaustive,"all 65536 source colors preserve alpha and maximum intensity");
        const auto& pixels=GetYellowPixels();
        constexpr RingPixels source={
#include "../src/map_click_ring_pixels.inc"
        };
        Check(pixels.size()==1936 && &pixels==&GetYellowPixels(),"44x44 immutable persistent pixels");
        unsigned lit=0;bool allYellow=true,sourceShape=true;
        for(std::size_t i=0;i<pixels.size();++i)
        {
            const auto pixel=pixels[i];
            allYellow &= (pixel&31)==0 && ((pixel>>5)&31)==((pixel>>10)&31);
            sourceShape &= pixel==Tint1555(source[i]) && (pixel&0x8000)==(source[i]&0x8000);
            if((pixel&0x8000) && (pixel&0x7FFF))++lit;
        }
        Check(sourceShape && lit==804,"all 1936 original pixels preserve native shape and alpha");
        Check(allYellow && lit>200 && lit<1000,"native ring is colored and keeps sparse outline");
        Check((pixels[22*44+22]&0x7FFF)==0,"native ring interior stays black");
    }
}

int RunMapClickFeedbackTests()
{
    Failures=0;Timing();IdentityAndClock();Pixels();
    if(!Failures)std::puts("PASS map click feedback: finite native-curve rings, identity, clock, 1555 pixels");
    return Failures;
}
