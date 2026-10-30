# Pure map-click feedback core

Delivered only four new product/test files: map_click_feedback.h/.cpp, map_click_ring_pixels.inc, map_click_feedback_tests.cpp. No existing files, project registrations, runtime, game data, process or memory modified. No commit.

Namespace dfl::map_click_feedback. Binding has pid,uid,userToken,contextToken,minimapToken,planeToken (uint32) and processStartedFileTime(uint64). Exact member comparisons avoid padding-sensitive memcmp. No actor/map/generation fields exist, so normal cross-area identity continuation is supported.

Public signatures (all noexcept):
- Feedback::Start(const Binding&, Point, uint64_t now)
- Feedback::Update(const Binding&, bool visible, uint64_t now)
- Feedback::Reset()
- bool Feedback::Active() const
- Point Feedback::GetPoint() const
- uint64_t Feedback::StartedTick() const
- RingState Feedback::Ring(size_t index,uint64_t now) const
- uint16_t Tint1555(uint16_t source)
- const RingPixels& GetYellowPixels()

RingState={bool visible,float scale,uint32 color}; RingPixels=array<uint16_t,1936>. RingPixelWidth=RingPixelHeight=44. Source1936 pixels are constexpr originals from native resource, yellow pixels compile-time derived. Source decodedSHA c1da5f256eace3c660bd2d7abd9f7cde79664144c829ea2b29f3023d6e412484 verified byte-identical to frozen native-contract evidence. TInt preserves alpha and maps maximum RGB5bit intensity to R=G/B=0.

Original1750ms curve used twice, second delayed200ms; new Start replaces. Update cancels exact binding changes,hidden map,now<previousUpdateTick andelapsed>=1950. Tick0 valid and uint64 high-tick arithmetic tested without addition overflow. Query Ring is const,so renderer must call Update before querying frame; future/past query alone cannot alter active state. Ring returns invisible for absent/expired/index>=2. Fully transparent final100ms returns invisible,scale1,color0 while controller staysactiveuntil1950.

Tests were written before production.cpp. Initial independent RED compiled header+tests+temporary missing-behavior stub; runtime24 expected assertion failures (not compile/link failure). The first compiler attempt caught signed/unsigned comparison warnings in the new tests, corrected before valid RED. GREEN compiled actual new core + test + independent main usingMSVC14.51 x86 /W4 /WX /EHsc /std:c++17 /MT /utf-8 and passed exit0 without warnings. Green additionally checks all1750 shifted sample equality,1936source-derived outputs,804lit native pixels andall65536possible1555input colors.

Runner entry: int RunMapClickFeedbackTests(). Project registration intentionally left to root; no other repo suite run because root owns native integration and existing project files. Logs and commands are retained here; core-manifest.json records source hashes and original-pixel validation. This is logic/unit PASS only,not live native rendering acceptance.
