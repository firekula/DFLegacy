#include "../src/frame_profile.h"
#include <cstdio>

int RunFrameProfileTests()
{
    using namespace dfl::frame_profile;
    int failures=0;auto check=[&](bool b,const char* text){if(!b){++failures;std::printf("FAIL frame profile: %s\n",text);}};
    Sample a;a.beginUs=100000;a.endUs=101000;a.tickMs=1000;a.scene=1;
    a.foregroundKnown=a.foregroundBegin=a.foregroundEnd=true;a.parts={100,200,100,300,300};
    Recorder r;check(r.Add(a)&&r.value.total.frames==1,"complete valid callback is counted");
    check(r.value.total.maxHookUs==1000&&r.value.total.maximumParts[3]==300,"total callback and component timings are separate");
    auto b=a;b.beginUs+=70000;b.endUs=b.beginUs+20000;b.tickMs+=70;b.parts={100,200,100,300,19300};r.Add(b);
    check(r.value.total.maxForegroundGapUs==70000&&r.value.total.gapsOver50ms==1,"foreground same-scene interval can reveal work outside debug callback");
    check(r.value.total.maxForegroundHookUs==20000&&r.value.total.over15ms==1&&r.value.eventCount==1,"slow callback is retained with components");
    b.beginUs+=200000;b.endUs=b.beginUs+1000;b.tickMs+=200;b.foregroundBegin=b.foregroundEnd=false;b.parts=a.parts;r.Add(b);
    b.beginUs+=200000;b.endUs=b.beginUs+1000;b.tickMs+=200;b.foregroundBegin=b.foregroundEnd=true;r.Add(b);
    check(r.value.total.gapsOver50ms==1,"background samples split foreground interval continuity");
    b.beginUs+=200000;b.endUs=b.beginUs+1000;b.tickMs+=200;b.scene=3;r.Add(b);
    check(r.value.total.gapsOver50ms==1,"scene changes do not become steady-scene intervals");
    auto bad=b;bad.endUs=bad.beginUs-1;check(!r.Add(bad)&&r.value.total.clockErrors==1,"rollback is unknown rather than a huge unsigned duration");
    bad=b;bad.parts[0]=999999;check(!r.Add(bad)&&r.value.total.clockErrors==2,"component overflow cannot masquerade as measured hook time");
    Recorder bounded;
    for(unsigned i=0;i<80;++i)
    {auto s=a;s.beginUs+=i*1000000ULL;s.endUs=s.beginUs+60000;s.parts={0,0,0,0,60000};s.tickMs=1000+i*1000;bounded.Add(s);}
    check(bounded.value.windowCount==Windows&&bounded.value.windows[0].startTickMs==72000&&bounded.value.windows[7].startTickMs==79000,"completed windows retain only the bounded newest history in order");
    check(bounded.value.eventCount==Events&&bounded.value.events[0].serial==49&&bounded.value.events[31].serial==80,"slow event ring is bounded and ordered");
    Recorder missing;missing.Add(a);auto later=a;later.beginUs+=3000000;later.endUs=later.beginUs+1000;later.tickMs+=3000;missing.Add(later);
    check(missing.value.missingBuckets==2,"unobserved buckets are counted explicitly");
    Recorder coverage;auto unknown=a;unknown.foregroundKnown=false;coverage.Add(unknown);
    auto background=a;background.beginUs+=10000;background.endUs+=10000;background.tickMs+=10;
    background.foregroundBegin=background.foregroundEnd=false;coverage.Add(background);
    check(coverage.value.total.unknownForegroundFrames==1&&coverage.value.total.backgroundFrames==1,"unknown foreground remains distinct from known background");
    check(bounded.value.events[31].intervalKnown&&!bounded.value.events[0].sample.parts.empty(),"eligible slow-event intervals have explicit measurement knowledge");
    Recorder first;auto slow=a;slow.endUs=slow.beginUs+60000;slow.parts={0,0,0,0,60000};first.Add(slow);
    check(first.value.eventCount==1&&!first.value.events[0].intervalKnown,"first slow callback does not invent an observed interval");
    Recorder attribution;attribution.Add(a);auto failed=a;failed.tickMs=5000;failed.endUs=failed.beginUs-1;attribution.Add(failed);
    auto recovery=a;recovery.tickMs=5010;recovery.beginUs+=4000000;recovery.endUs=recovery.beginUs+1000;attribution.Add(recovery);
    check(attribution.value.windows[0].stats.clockErrors==0&&attribution.value.current.startTickMs==5000
        &&attribution.value.current.stats.clockErrors==1,"clock failure belongs to its trustworthy tick bucket across phase boundaries");
    std::printf("Frame profile tests: %d failures\n",failures);return failures;
}
