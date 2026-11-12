#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two::performance
{
    enum class FrameStage
    {
        AnimationLookup, AnimationLoad, ImageLoad, ScriptLoad, ProjectileEffect, HitEffect, Present,
        MessageQueue, KeyTranslate, KeyDispatch, LetterDown, ArrowDown, TextDispatch,
        KeyboardPoll, KeyboardMerge, ShopUi, EndScene,
        ShopDraw, ShopPreview, ShopIcon, TextureQueue, TextureQueueWait,
        ShopNotice, ShopNoticeSkipped, WindowDispatch, MainLoopSleep,
        AvatarTooltipBackground, AvatarTooltipFill, Count
    };
    constexpr std::size_t StageIndex(FrameStage stage) noexcept { return static_cast<std::size_t>(stage); }
    constexpr std::size_t StageCount = StageIndex(FrameStage::Count);

    enum class ShopPage { None, Avatar, Items, Revive, Other, Transition };

    constexpr ShopPage ShopPageFromMode(std::uint32_t mode) noexcept
    {
        switch (mode)
        {
        case 0: return ShopPage::Avatar;
        case 1: return ShopPage::Revive;
        case 2: return ShopPage::Items;
        default: return ShopPage::Other;
        }
    }

    constexpr const char* ShopPageName(ShopPage page) noexcept
    {
        switch (page)
        {
        case ShopPage::Avatar: return "avatar";
        case ShopPage::Items: return "items";
        case ShopPage::Revive: return "revive";
        case ShopPage::Other: return "other";
        case ShopPage::Transition: return "transition";
        default: return "none";
        }
    }

    struct StageTimes
    {
        std::array<std::int64_t, StageCount> ticks{};
        std::array<unsigned, StageCount> calls{};
        bool complete = true;
        ShopPage shopPage = ShopPage::None;

        std::int64_t MeasuredTicks() const noexcept
        {
            std::int64_t sum = 0;
            for (const auto tick : ticks) sum += tick;
            return sum;
        }
    };

    class StageAccumulator
    {
    public:
        struct Token
        {
            FrameStage stage{};
            std::int64_t start = 0, nested = 0;
            Token* parent = nullptr;
        };

        void Begin(Token& token, FrameStage stage, std::int64_t now, ShopPage page = ShopPage::None) noexcept
        {
            token = { stage, now, 0, current_ };
            current_ = &token;
            if (page != ShopPage::None)
                frame_.shopPage = frame_.shopPage == ShopPage::None || frame_.shopPage == page
                    ? page : ShopPage::Transition;
        }

        void End(Token& token, std::int64_t now) noexcept
        {
            current_ = token.parent;
            if (!frame_.complete) return;
            const auto elapsed = now - token.start;
            if (elapsed < token.nested)
            {
                frame_.complete = false;
                return;
            }
            frame_.ticks[StageIndex(token.stage)] += elapsed - token.nested;
            ++frame_.calls[StageIndex(token.stage)];
            if (current_) current_->nested += elapsed;
        }

        StageTimes Drain() noexcept
        {
            auto finished = frame_;
            if (current_) finished.complete = false;
            frame_ = {};
            frame_.complete = current_ == nullptr;
            return finished;
        }

    private:
        StageTimes frame_;
        Token* current_ = nullptr;
    };

    class FrameStageScope
    {
    public:
        explicit FrameStageScope(FrameStage stage, ShopPage page = ShopPage::None) noexcept;
        ~FrameStageScope() noexcept;
        FrameStageScope(const FrameStageScope&) = delete;
        FrameStageScope& operator=(const FrameStageScope&) = delete;

    private:
        StageAccumulator::Token token_;
        bool active_ = false;
    };

    struct SlowFrame
    {
        std::uint64_t endFileTime = 0;
        std::int64_t elapsedTicks = 0;
        StageTimes stages;
    };
}
