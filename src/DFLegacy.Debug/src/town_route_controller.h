#pragma once
#include "town_route_catalog.h"
#include <cstddef>
#include <cstdint>
#include <string>

namespace dfl::town_route
{
    struct Observation
    {
        bool valid = false, busy = false;
        int scene = -1; // -1 unknown; known nonTown values always interrupt.
        std::uint32_t pid = 0, uid = 0, user = 0, actor = 0, context = 0, map = 0;
        std::uint64_t started = 0, generation = 0, sequence = 0, capturedTick = 0;
        Key area{};
        Point position{};
        Waypoint destination{};
        int directionX = 4, directionY = 4;
        bool autoKnown = false, autoActive = false;
    };
    struct Goal { Key area{}; Waypoint point{}; };
    struct Action
    {
        enum class Kind { None, SetDestination, Arrived, Interrupted };
        Kind kind = Kind::None;
        Waypoint point{};
        std::string reason;
    };
    // Pure lifecycle: no process/file access, native calls, locks or time reads.
    // Only successful Start takes over; Tick emits at most one pending action.
    // A dungeon gate is permitted as initial departure or final walking area,
    // never as transit; final points must stay outside every portal margin.
    // ConfirmDestination is synchronous confirmation of the caller's native leaf.
    class Controller
    {
    public:
        bool Start(const Catalog&, const Observation&, Goal, std::uint64_t now, std::string& error);
        Action Tick(const Catalog&, const Observation&, std::uint64_t now);
        void ConfirmDestination(const Observation&, Waypoint, std::uint64_t now, bool accepted);
        void Cancel(const char* reason);
        bool Active() const noexcept { return active_; }
        Goal Target() const { return target_; }
        std::string Status() const { return status_; }
        std::string Reason() const { return reason_; }
        std::size_t Steps() const noexcept { return steps_; }
    private:
        bool Initialize(const Catalog&, const Observation&, Goal, std::uint64_t, std::string&);
        bool KnownIdentityChanged(const Observation&) const noexcept;
        bool BindingMatches(const Observation&) const noexcept;
        bool Monotonic(const Observation&) const noexcept;
        Action Stop(const char* reason);
        Action Prepare(const Catalog&, const Observation&, std::uint64_t);
        bool Escape(const Catalog&, const Area&, Point, Waypoint&, std::string&) const;
        bool active_ = false, pending_ = false, leg_ = false;
        bool birthCheck_ = false, escaping_ = false, activeEscape_ = false;
        bool selected_ = false, haveContinuation_ = false, havePlan_ = false;
        bool haveIngress_ = false;
        bool initialDungeonGate_ = false;
        bool missing_ = false, freshnessWait_ = false, portalWait_ = false;
        Goal target_{};
        Observation binding_{}, current_{}, source_{}, completed_{}, ingress_{};
        Portal portal_{};
        Waypoint escape_{}, issued_{}, lastPlan_{};
        Point progressPosition_{}, lastPlanPosition_{};
        std::uint64_t began_ = 0, lastNow_ = 0, pendingAt_ = 0, acceptedAt_ = 0;
        std::uint64_t acceptedSequence_ = 0, progressAt_ = 0, missingAt_ = 0;
        std::uint64_t freshWaitAt_ = 0, portalWaitAt_ = 0;
        std::size_t steps_ = 0, repeats_ = 0;
        std::string catalogHash_, status_ = "idle", reason_;
    };
}
