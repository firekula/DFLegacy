// Execute C2's real BeginText/EndText against the production overlay. The fixture
// never enters the game or creates a D3D device/window. Input SHA256:
// 9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb.
#include "ChapterTwoPerformance.cpp"
#include "ChapterTwoPerformanceHistory.h"
#include "ChapterTwoFrameStages.h"
#include "ChapterTwoKeyboardTiming.h"
#include <fstream>
#include <iterator>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char textFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    unsigned Checks = 0, Failures = 0, Draws = 0, OriginalCalls = 0, NoticeDraws = 0;
    bool ChangeRenderer = false, OriginalChangesRenderer = false;
    struct Renderer { void** methods; unsigned char storage[0x80 - sizeof(void*)]; };
    static_assert(sizeof(Renderer) == 0x80);
    Renderer First{}, Second{};
    void* Methods[24]{};
    TextState NativeState{0, 1, -1, 1, 0, 0, 0x12345678};
    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL C2 text: %s\n", message); }
    }

    void CheckFrameHistory()
    {
        using namespace dfl::chapter_two::performance;
        History<float, FrameCapacity> frames;
        Check(Summarize(frames).fps == 0, "an empty frame history has no FPS reading");
        frames.Add(120);
        for (unsigned i = 0; i < 594; ++i) frames.Add(5);
        for (unsigned i = 0; i < 6; ++i) frames.Add(40);
        const auto stats = Summarize(frames);
        Check(frames.Size() == 600 && frames[0] == 5 && frames[599] == 40,
            "history keeps the latest 600 samples in display order");
        Check(stats.minimum == 5 && stats.maximum == 40 && stats.p95 == 5 && stats.p99 == 5,
            "nearest-rank percentiles exclude the slowest one percent");
        Check(stats.slow == 6 && stats.average > 5.34 && stats.average < 5.36 && stats.fps > 186 && stats.fps < 188,
            "slow spikes and the corresponding mean FPS are reported separately");
        frames.Add(70);
        Check(Summarize(frames).p99 == 40, "a spike above the one-percent boundary changes P99");
        frames.Clear();
        frames.Add(6);
        Check(frames.Size() == 1 && frames[0] == 6, "a new recording starts with fresh samples");
    }

    void CheckPerformanceIntervals()
    {
        using namespace dfl::chapter_two::performance;
        PeriodStatistics first, second;
        first.frames.Add(5.25);
        first.frames.Add(50.0);
        first.privateMiB.Add(100);
        first.workingSetMiB.Add(70);
        second.frames.Add(6.5);
        second.frames.Add(92.5);
        second.privateMiB.Add(180);
        second.privateMiB.Add(160);
        second.workingSetMiB.Add(110);
        second.workingSetMiB.Add(105);
        second.droppedFrames = 1;
        first.Merge(second);
        Check(first.frames.duration.count == 4 && first.frames.duration.sum == 154.25
            && first.frames.duration.minimum == 5.25 && first.frames.duration.maximum == 92.5,
            "the total combines durations across periods without averaging their averages");
        Check(first.frames.slow33 == 2 && first.frames.slow50 == 2 && first.frames.slow90 == 1
            && first.frames.PercentileUpper(99) == 93 && first.droppedFrames == 1,
            "the total retains slow frame counts and the upper percentile bucket");
        Check(first.privateMiB.count == 3 && first.privateMiB.first == 100 && first.privateMiB.last == 160
            && first.privateMiB.maximum == 180 && first.workingSetMiB.sum == 285,
            "the total retains the first, latest and peak values in the independent memory series");
        Check(second.frames.duration.count == 2 && second.frames.slow90 == 1 && second.privateMiB.first == 180,
            "merging the total preserves the standalone period");
    }

    void CheckSlowFrameStages()
    {
        using namespace dfl::chapter_two::performance;
        StageAccumulator stages;
        StageAccumulator::Token hit, lookup, load, present;
        stages.Begin(hit, FrameStage::HitEffect, 100);
        stages.Begin(lookup, FrameStage::AnimationLookup, 110);
        stages.Begin(load, FrameStage::AnimationLoad, 112);
        stages.End(load, 125);
        stages.End(lookup, 130);
        stages.End(hit, 145);
        stages.Begin(present, FrameStage::Present, 148);
        stages.End(present, 153);
        const auto frame = stages.Drain();
        Check(frame.complete && frame.ticks[StageIndex(FrameStage::HitEffect)] == 25
            && frame.ticks[StageIndex(FrameStage::AnimationLookup)] == 7
            && frame.ticks[StageIndex(FrameStage::AnimationLoad)] == 13
            && frame.ticks[StageIndex(FrameStage::Present)] == 5 && frame.MeasuredTicks() == 50,
            "nested resource work is charged once and separately from hit creation");
        Check(frame.calls[StageIndex(FrameStage::AnimationLoad)] == 1 && stages.Drain().MeasuredTicks() == 0,
            "stage counts and times are scoped to one frame");
        stages.Begin(present, FrameStage::Present, 155);
        Check(!stages.Drain().complete, "a stage spanning a frame boundary marks attribution incomplete");
        stages.End(present, 160);
        Check(!stages.Drain().complete && stages.Drain().complete, "a crossing stage does not contaminate later frames");

        StageAccumulator::Token draw, preview, icon, queue, wait;
        stages.Begin(draw, FrameStage::ShopDraw, 200, ShopPageFromMode(0));
        stages.Begin(preview, FrameStage::ShopPreview, 205);
        stages.End(preview, 210);
        stages.Begin(icon, FrameStage::ShopIcon, 212);
        stages.End(icon, 219);
        stages.End(draw, 230);
        stages.Begin(queue, FrameStage::TextureQueue, 232);
        stages.Begin(wait, FrameStage::TextureQueueWait, 234);
        stages.End(wait, 250);
        stages.End(queue, 255);
        const auto shop = stages.Drain();
        Check(shop.shopPage == ShopPage::Avatar && shop.MeasuredTicks() == 53
            && shop.ticks[StageIndex(FrameStage::ShopDraw)] == 18
            && shop.ticks[StageIndex(FrameStage::ShopPreview)] == 5
            && shop.ticks[StageIndex(FrameStage::ShopIcon)] == 7
            && shop.ticks[StageIndex(FrameStage::TextureQueueWait)] == 16
            && shop.ticks[StageIndex(FrameStage::TextureQueue)] == 7,
            "a shop frame separates preview, icon lookup and queue wait without double counting");
        stages.Begin(draw, FrameStage::ShopDraw, 260, ShopPageFromMode(2));
        stages.End(draw, 265);
        Check(stages.Drain().shopPage == ShopPage::Items && stages.Drain().shopPage == ShopPage::None,
            "the items page replaces the previous frame's tag and clears on the next frame");
        stages.Begin(draw, FrameStage::ShopDraw, 270, ShopPageFromMode(2));
        stages.End(draw, 272);
        stages.Begin(draw, FrameStage::ShopDraw, 273, ShopPageFromMode(0));
        stages.End(draw, 276);
        Check(stages.Drain().shopPage == ShopPage::Transition,
            "mixed page draws are excluded from either steady page");
    }

    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
    unsigned char& Active(Renderer& value) { return reinterpret_cast<unsigned char*>(&value)[0x2d]; }
    const TextState*& Saved(Renderer& value)
    {
        return *reinterpret_cast<const TextState**>(reinterpret_cast<unsigned char*>(&value) + 0x40);
    }
    void Begin(Renderer& renderer, const TextState& state)
    {
        reinterpret_cast<void(__thiscall*)(void*, const TextState*)>(Base + 0x614f20)(&renderer, &state);
    }
    void End(Renderer& renderer)
    {
        reinterpret_cast<void(__thiscall*)(void*)>(Base + 0x614f40)(&renderer);
    }
    int __fastcall Draw(void* object, void*, int x, int y, int color, char* text)
    {
        auto& renderer = *static_cast<Renderer*>(object);
        ++Draws;
        Check(Active(renderer) == 1 && Saved(renderer) != nullptr,
            "both overlay lines have a live native text state during Draw");
        if (Saved(renderer))
            Check(Saved(renderer)->mode == 1 && Saved(renderer)->font == 0x12345678,
                "the native consumer sees the correct mode and font through its retained pointer");
        Check(x == 440 && (y == 35 || y == 48) && color == static_cast<int>(0xff99c6e3u),
            "overlay positions and color remain unchanged");
        Check(text && (y == 35 ? std::strncmp(text, "FPS : ", 6) == 0 :
            std::strncmp(text, "Committed : ", 12) == 0), "both existing performance labels remain intact");
        if (ChangeRenderer && Draws == 1) At<void*>(Base + 0xa01fd4) = &Second;
        return y == 48 ? 19 : 17;
    }
    // Only the unrelated full native diagnostic drawing is stubbed: its real
    // entry needs a D3D device. Native Begin and End still execute C2 instructions.
    int __fastcall Original(void*, void*)
    {
        ++OriginalCalls;
        Begin(First, NativeState);
        End(First);
        if (OriginalChangesRenderer) At<void*>(Base + 0xa01fd4) = &Second;
        return 23;
    }
    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t a, std::size_t n, std::size_t limit) { return a <= limit && n <= limit - a; };
        if (!fits(0, sizeof(IMAGE_DOS_HEADER), file.size())) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                nt->FileHeader.NumberOfSections * sizeof(*sections), file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, ImageSize)) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, image.size()) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress, file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
        return image;
    }
    void CheckInstaller(const std::vector<unsigned char>& original)
    {
        Check(!dfl::chapter_two::InstallPerformanceOverlay(nullptr, ImageSize), "null image is rejected");
        auto image = original;
        Check(!dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size() - 1) && image == original,
            "wrong image extent is rejected without writes");
        // Existing frame/FPS guards plus the newly relied-on C2 text contract.
        const std::array<std::pair<std::size_t, std::size_t>, 6> guards{
            std::pair<std::size_t, std::size_t>{0x51e2c0, 16}, {0x3c080, 7}, {0x512e00, 9},
            {0x614f20, 24}, {0x614f40, 16}, {0x82ff60, 12}};
        for (const auto& range : guards)
            for (std::size_t i = 0; i < range.second; ++i)
            {
                image = original; image[range.first + i] ^= 1;
                const auto before = image;
                Check(!dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size()) && image == before,
                    "every frame/FPS/text signature byte is checked before any image write");
            }
        image = original;
        Check(dfl::chapter_two::InstallPerformanceOverlay(image.data(), image.size()), "the actual C2 image installs");
        unsigned changed = 0;
        for (std::size_t i = 0; i < image.size(); ++i)
            if (image[i] != original[i])
            {
                ++changed;
                Check(i == 0x51e2c7 || i == 0x51e2c8 || (i >= 0x51e2cc && i < 0x51e2d0),
                    "only the existing frame gate and relative call operand change");
            }
        Check(changed > 2 && changed <= 6, "the existing six-byte maximum patch extent is preserved");
    }

    void CheckTimingInstaller(const std::vector<unsigned char>& original)
    {
        auto candidate = original;
        for (const auto address : {0x9cd93au, 0x483bfbu, 0xc1dd18u, 0xc1dd34u, 0xc1dd6cu, 0xa2699du,
            0x483ba5u, 0x868b90u, 0x947ba5u, 0xa26955u, 0xc11c34u, 0x94248fu,
            0x941651u, 0x94156cu, 0x9408d2u, 0xa269efu, 0xa2d438u, 0x9424c3u, 0x93f0a4u,
            0x8bbdaeu, 0x88bafau, 0x88b901u, 0x9b4316u, 0x9b431bu})
        {
            candidate = original;
            candidate[address - Base] ^= 1;
            const auto corrupted = candidate;
            Check(!dfl::chapter_two::InstallSlowFrameTiming(candidate.data(), candidate.size()) && candidate == corrupted,
                "a mismatched timing site leaves all original code unchanged");
        }
    }

    void CheckKeyboardTimingInstaller(const std::vector<unsigned char>& original)
    {
        using namespace dfl::chapter_two::performance;
        Check(ClassifyKeyMessage(WM_KEYDOWN, 'X', 0) == FrameStage::LetterDown &&
            ClassifyKeyMessage(WM_KEYDOWN, 'X', 0x40000000) == FrameStage::KeyDispatch &&
            ClassifyKeyMessage(WM_KEYDOWN, VK_LEFT, 0) == FrameStage::ArrowDown &&
            ClassifyKeyMessage(WM_KEYUP, VK_LEFT, 0) == FrameStage::KeyDispatch,
            "only the initial letter and arrow presses increment their counters");
        Check(ClassifyKeyMessage(WM_IME_COMPOSITION, 0, 0) == FrameStage::TextDispatch &&
            ClassifyKeyMessage(WM_CHAR, 'X', 0) == FrameStage::TextDispatch &&
            ClassifyKeyMessage(WM_MOUSEMOVE, 0, 0) == FrameStage::Count,
            "text processing is measured and mouse movement is not classified as keyboard");
        constexpr LPARAM zScan = 0x002c0001;
        Check(IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, 1, 0x0804, 'Z') &&
            IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, zScan, 0x0804, 'Z'),
            "the original virtual key identifies a letter even without the physical scan code");
        Check(!IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, 1, 0x0804, VK_SHIFT) &&
            !IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, 1, 0x0804, VK_RETURN) &&
            !IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, zScan, 0x0804, VK_PROCESSKEY) &&
            !IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, zScan | 0x20000000, 0x0804, 'Z') &&
            !IsChineseImeLetterDown(WM_KEYUP, VK_PROCESSKEY, zScan, 0x0804, 'Z') &&
            !IsChineseImeLetterDown(WM_KEYDOWN, 'Z', zScan, 0x0804, 'Z') &&
            !IsChineseImeLetterDown(WM_KEYDOWN, VK_PROCESSKEY, zScan, 0x0411, 'Z'),
            "IME switches, non-letter keys, ordinary Latin keys, and other layouts keep the native path");
        Check(ChatFallbackHasFocus(0, 130, 130, 133) &&
            !ChatFallbackHasFocus(1, 130, 130, 133) &&
            !ChatFallbackHasFocus(0, 133, 130, 133) &&
            !ChatFallbackHasFocus(0, 211, 130, 133) &&
            !ChatFallbackHasFocus(0, 130, 130, 130),
            "only the inactive chat fallback qualifies, with an unambiguous distinct editor");
        const auto gameWindow = reinterpret_cast<HWND>(0x1234);
        ImeGameplayKey pending{gameWindow, 1, 'I'};
        Check(pending.Take(nullptr, 1) == 0 && pending.Take(gameWindow, zScan) == 0 &&
            pending.Take(gameWindow, 1) == 'I' && pending.Take(gameWindow, 1) == 0,
            "only the matching dispatch receives the recovered letter, once");
        auto image = original;
        constexpr std::size_t queue = 0x6ea3a4, translate = 0x6ea420;
        const auto user32 = GetModuleHandleW(L"user32.dll");
        const auto peek = GetProcAddress(user32, "PeekMessageA");
        const auto translateMessage = GetProcAddress(user32, "TranslateMessage");
        std::memcpy(image.data() + queue, &peek, sizeof(peek));
        std::memcpy(image.data() + translate, &translateMessage, sizeof(translateMessage));
        auto corrupted = image;
        corrupted[translate] ^= 1;
        const auto before = corrupted;
        Check(!dfl::chapter_two::InstallKeyboardMessageTiming(corrupted.data(), corrupted.size()) && corrupted == before,
            "an unexpected keyboard import prevents both IAT writes");
        Check(dfl::chapter_two::InstallKeyboardMessageTiming(image.data(), image.size()) &&
            std::memcmp(image.data() + queue, &peek, sizeof(peek)) != 0 &&
            std::memcmp(image.data() + translate, &translateMessage, sizeof(translateMessage)) != 0,
            "the two verified original input imports redirect to timing wrappers");
        std::array<unsigned char, 0x64> manager{};
        std::array<unsigned char, 0x1c5> chat{};
        std::array<unsigned char, 0x10> fallback{}, editor{};
        const auto write = [](unsigned char* target, std::uintptr_t value)
        { std::memcpy(target, &value, sizeof(value)); };
        write(image.data() + 0x956d24, reinterpret_cast<std::uintptr_t>(manager.data()));
        write(manager.data(), 0xaedb24);
        write(manager.data() + 0x60, reinterpret_cast<std::uintptr_t>(chat.data()));
        write(chat.data(), 0xc11c04);
        write(chat.data() + 0x9c, reinterpret_cast<std::uintptr_t>(fallback.data()));
        write(chat.data() + 0xa8, reinterpret_cast<std::uintptr_t>(editor.data()));
        write(fallback.data(), 0xc123ac); write(fallback.data() + 0xc, 130);
        write(editor.data(), 0xc123ac); write(editor.data() + 0xc, 133);
        write(image.data() + 0x94dfdc, 130);
        Check(!IsKeyboardTextEntryActive(), "inactive native chat permits gameplay sampling");
        chat[0x1c4] = 1;
        Check(IsKeyboardTextEntryActive(), "open native chat suppresses cached gameplay input");
        chat[0x1c4] = 0; write(image.data() + 0x94dfdc, 133);
        Check(IsKeyboardTextEntryActive(), "native editor focus suppresses cached gameplay input");
        write(manager.data(), 0);
        Check(!IsKeyboardTextEntryActive(), "unrecognized native UI memory keeps the existing input path");
    }

    void* ExpectedDevice = nullptr;
    void* ExpectedShopWidget = nullptr;
    LPCRITICAL_SECTION ExpectedQueueLock = nullptr;
    HRESULT WINAPI FixturePresent(void* device, const RECT* source, const RECT* target, HWND window, const RGNDATA* dirty)
    {
        Check(device == ExpectedDevice && !source && !target && !window && !dirty,
            "the native submission wrapper forwards the device and four original null arguments");
        return static_cast<HRESULT>(0x88760868);
    }

    HRESULT WINAPI FixtureEndScene(void* device)
    {
        Check(device == ExpectedDevice, "EndScene forwards the original device");
        return E_FAIL;
    }

    std::uintptr_t __fastcall FixtureHit(void* self, void*, std::uintptr_t first, std::uintptr_t second,
        std::uintptr_t third, std::uintptr_t fourth, std::uintptr_t fifth)
    {
        Check(self == ExpectedDevice && first == 11 && second == 22 && third == 33 && fourth == 44 && fifth == 55,
            "the native hit wrapper forwards all five stack arguments and the instance");
        return 777;
    }

    std::uintptr_t __fastcall FixtureShopDraw(void* shop, void*, void* widget)
    {
        Check(shop == ExpectedDevice && widget == ExpectedShopWidget,
            "the shop draw wrapper preserves the object and widget");
        return 456;
    }

    void WINAPI FixtureEnterQueue(LPCRITICAL_SECTION lock)
    {
        Check(lock == ExpectedQueueLock, "the texture queue wrapper calls the imported lock with the original pointer");
        EnterCriticalSection(lock);
    }

    using NoticeCallback = void (__fastcall*)(void*, void*);
    NoticeCallback CallNotice = nullptr;
    using NoticeLabel = void (__cdecl*)(int, int, int, int, int, const char*, int, int, int, int, int, int);
    NoticeLabel CallNoticeLabel = nullptr;
    int NoticeOpacity = 0;
    void __cdecl FixtureNoticeLabel(int x, int y, int, int, int, const char*, int opacity, int, int, int, int, int)
    {
        Check(x == 363 && y == 80, "the shop notice label preserves its original position");
        NoticeOpacity = opacity;
    }
    void __fastcall FixtureNoticeDraw(void*, void*)
    {
        ++NoticeDraws;
        CallNoticeLabel(363, 80, -1, 0xff000000, 2, "test notice", 13, 0, 0, 75, 0, 1);
    }

    bool ChangeReviveFlag = false;
    int __fastcall FixtureShopTree(void* shop, void*)
    {
        const auto previous = NoticeDraws;
        for (unsigned child = 0; child < 38; ++child) CallNotice(shop, nullptr);
        Check(NoticeDraws == previous, "the notice waits until the native shop tree finishes drawing");
        if (ChangeReviveFlag)
        {
            (*reinterpret_cast<unsigned char**>(static_cast<unsigned char*>(shop) + 0x1f4))[0x11] = 1;
            CallNotice(shop, nullptr);
            Check(NoticeDraws == previous + 1, "an out-of-date revive widget flag invokes the native state update immediately");
        }
        return 1;
    }

    void CheckTimingWrappers(unsigned char* mapped)
    {
        if (!dfl::chapter_two::InstallSlowFrameTiming(mapped, ImageSize))
        {
            Check(false, "timing hooks install on the verified C2 image");
            return;
        }
        using Present = HRESULT (__fastcall*)(void*, void*);
        using Hit = std::uintptr_t (__fastcall*)(void*, void*, std::uintptr_t, std::uintptr_t,
            std::uintptr_t, std::uintptr_t, std::uintptr_t);
        const auto presentCall = Base + (0xa26996 - Base);
        const auto hitCall = Base + (0x483bfb - Base);
        const auto target = [](std::uintptr_t call) { return call + 5 + At<std::uint32_t>(call + 1); };
        std::array<void*, 36> deviceMethods{};
        deviceMethods[15] = reinterpret_cast<void*>(&FixturePresent);
        deviceMethods[35] = reinterpret_cast<void*>(&FixtureEndScene);
        auto* device = deviceMethods.data();
        ExpectedDevice = &device;
        Check(At<std::uint16_t>(0xa26994) == 0xc88b && At<unsigned char>(0xa2699f) == 0x90
            && At<unsigned char>(0xa269a0) == 0x6a,
            "submission replacement preserves the first following native instruction");
        Check(reinterpret_cast<Present>(target(presentCall))(&device, nullptr) == static_cast<HRESULT>(0x88760868),
            "submission preserves the original device-lost HRESULT");
        Check(At<std::uint16_t>(0xa26954) == 0xc88b && At<unsigned char>(0xa2695b) == 0x6a &&
            reinterpret_cast<Present>(target(0xa26956))(&device, nullptr) == E_FAIL,
            "EndScene preserves the following instruction and the device HRESULT");
        At<unsigned char>(0x483530) = 0xe9;
        At<std::uint32_t>(0x483531) = reinterpret_cast<std::uintptr_t>(&FixtureHit) - 0x483535;
        At<unsigned char>(0x942480) = 0xe9;
        At<std::uint32_t>(0x942481) = reinterpret_cast<std::uintptr_t>(&FixtureShopDraw) - 0x942485;
        At<unsigned char>(0x93f040) = 0xe9;
        At<std::uint32_t>(0x93f041) = reinterpret_cast<std::uintptr_t>(&FixtureNoticeDraw) - 0x93f045;
        At<unsigned char>(0xa3cbf0) = 0xe9;
        At<std::uint32_t>(0xa3cbf1) = reinterpret_cast<std::uintptr_t>(&FixtureShopTree) - 0xa3cbf5;
        At<unsigned char>(0x91a710) = 0xe9;
        At<std::uint32_t>(0x91a711) = reinterpret_cast<std::uintptr_t>(&FixtureNoticeLabel) - 0x91a715;
        CallNoticeLabel = reinterpret_cast<NoticeLabel>(target(0x93f0a4));
        At<decltype(&FixtureEnterQueue)>(0xaea274) = &FixtureEnterQueue;
        FlushInstructionCache(GetCurrentProcess(), mapped, ImageSize);
        Check(reinterpret_cast<Hit>(target(hitCall))(&device, nullptr, 11, 22, 33, 44, 55) == 777,
            "hit effect preserves the original return value");
        using ShopDraw = std::uintptr_t (__fastcall*)(void*, void*, void*);
        alignas(void*) unsigned char shop[0x1fc]{}, widget[0x12]{};
        widget[0x10] = 1;
        *reinterpret_cast<void**>(shop + 0x68) = widget;
        ExpectedDevice = shop;
        ExpectedShopWidget = widget;
        Check(At<ShopDraw>(0xc11c34)(shop, nullptr, widget) == 456,
            "the shop vtable wrapper preserves the return from the original draw");

        *reinterpret_cast<const char**>(shop + 8) = "test notice";
        *reinterpret_cast<void**>(shop + 0x1f4) = widget;
        At<void*>(0xe01fd4) = &First;
        At<unsigned>(0xdfac74) = 0;
        CallNotice = reinterpret_cast<NoticeCallback>(target(0x9424c3));
        const auto drawTree = reinterpret_cast<int(__fastcall*)(void*, void*)>(target(0x9b4316));
        for (const auto tab : {0u, 1u, 2u})
        {
            *reinterpret_cast<unsigned*>(shop + 0x60) = tab;
            const auto previous = NoticeDraws;
            Check(drawTree(shop, nullptr) == 1, "the draw tree preserves the native return value");
            Check(NoticeDraws == previous + 1, "the installed hook draws the notice once after 38 child callbacks");
            Check(NoticeOpacity == 220, "one draw preserves the blended opacity of 38 native notice backgrounds");
        }
        *reinterpret_cast<unsigned*>(shop + 0x60) = 1;
        const auto previous = NoticeDraws;
        ChangeReviveFlag = true;
        drawTree(shop, nullptr);
        Check(NoticeDraws == previous + 2, "a revive flag update retains the final late draw");
        CallNotice(shop, nullptr);
        Check(NoticeDraws == previous + 3, "calls outside the verified tree use the original draw");
        Check(NoticeOpacity == 13, "calls outside the verified tree keep the native notice opacity");

        CRITICAL_SECTION queueLock{};
        InitializeCriticalSection(&queueLock);
        ExpectedQueueLock = &queueLock;
        Check(At<unsigned char>(0xa2d43d) == 0x90 && At<unsigned char>(0xa2d43e) == 0x8b,
            "the texture queue wrapper preserves the instruction after EnterCriticalSection");
        reinterpret_cast<decltype(&FixtureEnterQueue)>(target(0xa2d438))(&queueLock);
        Check(queueLock.RecursionCount == 1, "the queue wrapper returns with the critical section held");
        LeaveCriticalSection(&queueLock);
        DeleteCriticalSection(&queueLock);
    }
    void Reset()
    {
        First = {}; Second = {};
        Methods[0x4c / 4] = reinterpret_cast<void*>(Base + 0x614f20);
        Methods[0x50 / 4] = reinterpret_cast<void*>(Base + 0x614f40);
        Methods[0x54 / 4] = reinterpret_cast<void*>(&Draw);
        First.methods = Second.methods = Methods;
        At<void*>(Base + 0xa01fd4) = &First;
        At<std::uint32_t>(Base + 0x954144) = 0x12345678;
        At<float>(0xd56d84) = 60;
        Draws = OriginalCalls = 0;
        ChangeRenderer = OriginalChangesRenderer = false;
    }
    void CheckOwnedAndFollowup()
    {
        Reset();
        Check(DrawPerformance(nullptr, nullptr) == 19 && Draws == 2,
            "overlay draws both lines and preserves the final draw return value");
        Check(!Active(First) && !Saved(First), "owned overlay batch ends before its stack TextState expires");
        TextState next = NativeState;
        Begin(First, next);
        Check(Active(First) && Saved(First) == &next, "the following native message selects its own state");
        End(First);
        for (unsigned i = 0; i < 256; ++i)
        {
            DrawPerformance(nullptr, nullptr);
            Check(!Active(First) && !Saved(First), "repeated frames do not retain an expired stack TextState");
            Begin(First, next);
            Check(Saved(First) == &next, "native messages immediately after every frame use their own state");
            End(First);
        }
    }
    void CheckBorrowedAndRendererChange()
    {
        Reset(); Begin(First, NativeState);
        DrawPerformance(nullptr, nullptr);
        Check(Active(First) && Saved(First) == &NativeState,
            "an already active native batch retains its caller's state and ownership");
        End(First);
        Reset(); ChangeRenderer = true;
        Begin(Second, NativeState);
        DrawPerformance(nullptr, nullptr);
        Check(!Active(First) && !Saved(First), "cleanup follows the captured renderer after the global root changes");
        Check(Active(Second) && Saved(Second) == &NativeState,
            "a changed global root cannot end another renderer's batch");
        End(Second);
    }
    void CheckMissingMethods()
    {
        for (const auto index : {0x4c / 4, 0x50 / 4, 0x54 / 4})
        {
            Reset(); Methods[index] = nullptr;
            Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0 && !Active(First) && !Saved(First),
                "a missing Begin, End or Draw cannot create an incomplete batch");
        }
        Reset(); First.methods = nullptr;
        Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0, "missing renderer vtable is safe");
        Reset(); At<void*>(Base + 0xa01fd4) = nullptr;
        Check(DrawPerformance(nullptr, nullptr) == 0 && Draws == 0, "missing renderer root is safe");
    }
    void CheckOriginalFlag()
    {
        std::array<unsigned char, 0x300> state{};
        Reset(); state[0x2f0] = 1;
        Check(DrawPerformance(state.data(), nullptr) == 19 && OriginalCalls == 1 && Draws == 2 && !Active(First),
            "the original diagnostic flag still invokes the native entry before overlay drawing");
        Reset(); state[0x2f0] = 0;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 0, "a disabled original diagnostic flag does not invoke the native entry");
        Reset(); state[0x2f0] = 2;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 0, "the original diagnostic predicate remains exactly flag one");
        Reset(); state[0x2f0] = 1; OriginalChangesRenderer = true;
        DrawPerformance(state.data(), nullptr);
        Check(OriginalCalls == 1 && Draws == 2 && !Active(First) && !Active(Second),
            "overlay uses the renderer selected after the original diagnostic callback");
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    CheckFrameHistory();
    CheckPerformanceIntervals();
    CheckSlowFrameStages();
    const auto image = ReadImage(argv[1]);
    Check(image.size() == ImageSize, "fixture reads the actual original C2 PE");
    if (image.empty()) return 2;
    CheckInstaller(image);
    CheckTimingInstaller(image);
    CheckKeyboardTimingInstaller(image);
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        DWORD previous = 0;
        const auto reserve = reinterpret_cast<std::uintptr_t>(textFixtureReserve);
        if (reserve <= Base && Base + ImageSize <= reserve + sizeof(textFixtureReserve) &&
            VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &previous))
            mapped = reinterpret_cast<unsigned char*>(Base);
    }
    Check(mapped == reinterpret_cast<void*>(Base), "fixture maps real C2 instructions at their original base");
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
    Check(dfl::chapter_two::InstallPerformanceOverlay(mapped, ImageSize), "production overlay installs on executable fixture");
    // Preserve the real C2 Begin/End and FPS reader. Only the original full D3D
    // diagnostic entry is replaced; its flag dispatch remains production code.
    mapped[0x512e00] = 0xe9;
    At<std::uint32_t>(Base + 0x512e01) = static_cast<std::uint32_t>(
        reinterpret_cast<std::uintptr_t>(&Original) - (Base + 0x512e00) - 5);
    FlushInstructionCache(GetCurrentProcess(), mapped, ImageSize);
    CheckOwnedAndFollowup();
    CheckBorrowedAndRendererChange();
    CheckMissingMethods();
    CheckOriginalFlag();
    CheckTimingWrappers(mapped);
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two performance text lifetime: %u checks, %u failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
