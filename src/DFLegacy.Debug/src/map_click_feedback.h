#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::map_click_feedback
{
    struct Binding
    {
        std::uint32_t pid=0, uid=0, userToken=0, contextToken=0, minimapToken=0, planeToken=0;
        std::uint64_t processStartedFileTime=0;
    };
    struct Point { std::int32_t x=0, y=0; };
    struct RingState { bool visible=false; float scale=0; std::uint32_t color=0; };
    constexpr std::size_t RingPixelWidth=44, RingPixelHeight=44;
    using RingPixels=std::array<std::uint16_t,RingPixelWidth*RingPixelHeight>;
    std::uint16_t Tint1555(std::uint16_t source) noexcept;
    const RingPixels& GetYellowPixels() noexcept;

    class Feedback
    {
    public:
        void Start(const Binding&,Point,std::uint64_t now) noexcept;
        void Update(const Binding&,bool visible,std::uint64_t now) noexcept;
        void Reset() noexcept;
        bool Active() const noexcept;
        Point GetPoint() const noexcept;
        std::uint64_t StartedTick() const noexcept;
        RingState Ring(std::size_t index,std::uint64_t now) const noexcept;
    private:
        Binding binding_{};
        Point point_{};
        std::uint64_t started_=0, lastTick_=0;
        bool active_=false;
    };
}
