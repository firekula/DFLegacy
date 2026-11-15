#include "town_route_controller.h"
#include <algorithm>
#include <cmath>
#include <utility>
#include <vector>

namespace dfl::town_route
{
    namespace
    {
        constexpr std::uint64_t SampleAge=750, SendAge=200, DataWait=5000;
        constexpr std::uint64_t RouteTime=120000, LegTime=30000;
        constexpr std::size_t MaximumSteps=1024, MaximumEscapeCandidates=4096;
        bool Supported(const Area* area) noexcept
        { return area && (area->kind=="normal" || area->kind=="gate" || area->kind=="pub"); }
        bool SupportedTarget(const Area* area,Key final) noexcept
        { return Supported(area) || (area && area->key==final && area->kind=="dungeon_gate"); }
        bool Coordinate(double x) noexcept { return std::isfinite(x) && x>=-1000000 && x<=1000000; }
        bool Near(const Portal& p,Point position) noexcept
        {
            return position.x>=static_cast<double>(p.rect.x)-PortalBlockMargin
                && position.x<static_cast<double>(p.rect.x)+p.rect.width+PortalBlockMargin
                && position.y>=static_cast<double>(p.rect.y)-PortalBlockMargin
                && position.y<static_cast<double>(p.rect.y)+p.rect.height+PortalBlockMargin;
        }
        bool SamePortal(const Portal& a,const Portal& b) noexcept
        {
            return a.source==b.source && a.target==b.target && a.point==b.point
                && a.rect.x==b.rect.x && a.rect.y==b.rect.y && a.rect.width==b.rect.width && a.rect.height==b.rect.height;
        }
        bool AtPoint(const Observation& o,Waypoint point) noexcept
        { return std::fabs(o.position.x-point.x)<=3 && std::fabs(o.position.y-point.y)<=3 && o.directionX==4 && o.directionY==4; }
        bool Moved(Point a,Point b) noexcept { return std::fabs(a.x-b.x)>=.5 || std::fabs(a.y-b.y)>=.5; }
        bool MonotonicExit(const Portal& portal,Point start,Waypoint target) noexcept
        {
            const Point rounded{std::round(start.x),std::round(start.y)};
            for (const auto origin : {start,rounded})
            {
                if (portal.rect.Contains(origin.x,origin.y)) continue;
                const double left=std::fmin(origin.x,target.x),right=std::fmax(origin.x,target.x);
                const double top=std::fmin(origin.y,target.y),bottom=std::fmax(origin.y,target.y);
                if (left<static_cast<double>(portal.rect.x)+portal.rect.width && right>=portal.rect.x
                    && top<static_cast<double>(portal.rect.y)+portal.rect.height && bottom>=portal.rect.y) return false;
            }
            return true;
        }
        int EvenRound(double value) noexcept
        {
            const auto lower=std::floor(value),fraction=value-lower;
            if (fraction<.5) return static_cast<int>(lower);
            if (fraction>.5) return static_cast<int>(lower+1);
            const auto integral=static_cast<int>(lower);
            return integral%2==0?integral:integral+1;
        }
        bool Initial(const Observation& o,std::uint64_t now,std::string& error)
        {
            const char* reason=nullptr;
            if (!o.valid || o.scene!=1) reason="town_unavailable";
            else if (!o.pid || !o.started || !o.uid || o.uid>65535 || !o.user || !o.actor || !o.context || !o.map
                     || !o.sequence || !o.generation) reason="identity_unknown";
            else if (!Coordinate(o.position.x) || !Coordinate(o.position.y)) reason="position_unknown";
            else if (o.capturedTick>now) reason="future_capture";
            else if (now-o.capturedTick>SampleAge) reason="stale_capture";
            else if (o.busy) reason="ui_busy";
            else if (!o.autoKnown) reason="native_auto_move_unknown";
            if (reason) { error=reason;return false; }
            return true;
        }
    }
    bool Controller::Initialize(const Catalog& catalog,const Observation& o,Goal goal,std::uint64_t now,std::string& error)
    {
        error.clear();
        if (!Initial(o,now,error)) return false;
        if (!Coordinate(goal.point.x) || !Coordinate(goal.point.y)) {error="bad_target";return false;}
        const auto* area=catalog.Find(o.area);const auto* final=catalog.Find(goal.area);
        if (!area || !final) {error="unknown_area";return false;}
        const bool dungeonSource=area->kind=="dungeon_gate";
        if ((!Supported(area) && !dungeonSource) || !SupportedTarget(final,goal.area)) {error="unsupported_area";return false;}
        for (const auto& portal:final->portals)
            if (Near(portal,{static_cast<double>(goal.point.x),static_cast<double>(goal.point.y)})) {error="goal_inside_portal";return false;}
        std::vector<Waypoint> points;std::vector<Portal> route;
        if (!catalog.Waypoints(goal.area,{static_cast<double>(goal.point.x),static_cast<double>(goal.point.y)},goal.point,final->portals,points,error)
            || !catalog.Route(o.area,goal.area,route,error)) return false;
        if (route.size()>256) {error="route_step_limit";return false;}
        auto expected=o.area;
        for (const auto& portal:route)
        {
            if (portal.source!=expected) {error="invalid_route";return false;}
            if (!SupportedTarget(catalog.Find(portal.target),goal.area)) {error="unsupported_area";return false;}
            expected=portal.target;
        }
        if (expected!=goal.area) {error="invalid_route";return false;}
        active_=true;birthCheck_=true;target_=goal;binding_=current_=source_=o;began_=lastNow_=now;
        initialDungeonGate_=dungeonSource;
        haveIngress_=true;ingress_=o;
        catalogHash_=catalog.pvfSha256;status_="queued";reason_="goal_queued";
        // Probe only the first plan in a discarded candidate. No action escapes,
        // and Tick still replans from its later actual start. An unreachable new
        // goal must not cancel the existing route merely because its end is valid.
        Controller probe=*this;
        const auto plan=probe.Prepare(catalog,o,o.capturedTick);
        if (plan.kind==Action::Kind::Interrupted) {error=plan.reason;return false;}
        return true;
    }
    bool Controller::Start(const Catalog& catalog,const Observation& o,Goal goal,std::uint64_t now,std::string& error)
    {
        Controller next;
        if (!next.Initialize(catalog,o,goal,now,error)) return false;
        *this=std::move(next);return true;
    }
    bool Controller::KnownIdentityChanged(const Observation& o) const noexcept
    {
        return (o.pid && o.pid!=binding_.pid) || (o.started && o.started!=binding_.started)
            || (o.uid && o.uid!=binding_.uid) || (o.user && o.user!=binding_.user)
            || (o.actor && o.actor!=binding_.actor) || (o.context && o.context!=binding_.context);
    }
    bool Controller::BindingMatches(const Observation& o) const noexcept
    {
        return o.pid==binding_.pid && o.started==binding_.started && o.uid==binding_.uid
            && o.user==binding_.user && o.actor==binding_.actor && o.context==binding_.context;
    }
    bool Controller::Monotonic(const Observation& o) const noexcept
    {
        return o.sequence>=current_.sequence && o.generation>=current_.generation && o.capturedTick>=current_.capturedTick
            && (o.sequence!=current_.sequence || (o.capturedTick==current_.capturedTick && o.generation==current_.generation));
    }
    void Controller::Cancel(const char* reason)
    {
        active_=pending_=leg_=false;status_="interrupted";reason_=reason?reason:"cancelled";
    }
    Action Controller::Stop(const char* reason)
    { Cancel(reason);return {Action::Kind::Interrupted,issued_,reason_}; }
    bool Controller::Escape(const Catalog& catalog,const Area& area,Point start,Waypoint& target,std::string& error) const
    {
        std::vector<const Portal*> origins;
        for (const auto& portal:area.portals) if (Near(portal,start)) origins.push_back(&portal);
        if (origins.size()!=1) {error=origins.empty()?"portal_rearm_unavailable":"ambiguous_birth_portal";return false;}
        const auto& origin=*origins[0];std::vector<Waypoint> candidates;
        const double gap=PortalBlockMargin+1;
        const auto add=[&](double x,double y)
        {
            if (!Coordinate(x) || !Coordinate(y)) return;
            Waypoint p{static_cast<int>(x),static_cast<int>(y)};
            if (std::none_of(area.walkable.begin(),area.walkable.end(),[&](const Rect& r){return r.Contains(p.x,p.y);})
                || std::any_of(area.portals.begin(),area.portals.end(),[&](const Portal& door){return Near(door,{x,y});})) return;
            if (std::find(candidates.begin(),candidates.end(),p)==candidates.end() && candidates.size()<MaximumEscapeCandidates) candidates.push_back(p);
        };
        for (const auto& floor:area.walkable)
        {
            if (floor.width<=0 || floor.height<=0) continue;
            const double right=static_cast<double>(floor.x)+floor.width-1,bottom=static_cast<double>(floor.y)+floor.height-1;
            const auto clamp=[](int v,double low,double high){return std::fmax(low,std::fmin(v,high));};
            const double xs[]={clamp(EvenRound(start.x),floor.x,right),clamp(origin.point.x,floor.x,right),clamp(EvenRound(floor.x+floor.width/2.),floor.x,right)};
            const double ys[]={clamp(EvenRound(start.y),floor.y,bottom),clamp(origin.point.y,floor.y,bottom),clamp(EvenRound(floor.y+floor.height/2.),floor.y,bottom)};
            for (double x:xs) {add(x,origin.rect.y-gap);add(x,static_cast<double>(origin.rect.y)+origin.rect.height+gap);}
            for (double y:ys) {add(origin.rect.x-gap,y);add(static_cast<double>(origin.rect.x)+origin.rect.width+gap,y);}
        }
        const auto distance=[&](Waypoint p){const auto dx=p.x-start.x,dy=p.y-start.y;return dx*dx+dy*dy;};
        std::sort(candidates.begin(),candidates.end(),[&](Waypoint a,Waypoint b)
        {const auto da=distance(a),db=distance(b);return da<db || (da==db && (a.x<b.x || (a.x==b.x && a.y<b.y)));});
        std::vector<Portal> blocked;
        for (const auto& portal:area.portals) if (!Near(portal,start)) blocked.push_back(portal);
        for (const auto candidate:candidates)
        {
            if (!MonotonicExit(origin,start,candidate)) continue;
            std::vector<Waypoint> points;std::string why;
            if (catalog.Waypoints(area.key,start,candidate,blocked,points,why)
                && points.size()==1 && points.back()==candidate) {target=candidate;error.clear();return true;}
        }
        error="portal_rearm_unavailable";return false;
    }
    Action Controller::Prepare(const Catalog& catalog,const Observation& o,std::uint64_t now)
    {
        // A queued goal owns no native movement yet. Preserve newer manual
        // intent across the caller's first-frame delay and send-freshness wait.
        // A portal birth gets its own baseline from the new map, never the old
        // map's destination. Natural previous movement may settle to idle, but
        // once idle it may not silently start moving again before our leaf.
        if (haveIngress_)
        {
            if (!(o.destination==ingress_.destination)) return Stop("destination_changed");
            if (!o.autoKnown) return Stop("native_auto_move_unknown");
            if (o.autoActive!=ingress_.autoActive)
                return Stop(o.autoActive?"native_auto_move_changed":"native_auto_move_stopped");
            if ((o.directionX!=ingress_.directionX && o.directionX!=4)
                || (o.directionY!=ingress_.directionY && o.directionY!=4)) return Stop("manual_movement");
            ingress_.directionX=o.directionX;ingress_.directionY=o.directionY;
        }
        if (haveContinuation_)
        {
            if (!(o.destination==completed_.destination)) return Stop("destination_changed");
            if (!o.autoKnown) return Stop("native_auto_move_unknown");
            if (completed_.autoActive && !o.autoActive) return Stop("native_auto_move_stopped");
            if (o.directionX!=4 || o.directionY!=4) return Stop("manual_movement");
        }
        if (!o.autoKnown) return Stop("native_auto_move_unknown");
        if (now-o.capturedTick>SendAge)
        {
            if (!freshnessWait_) {freshnessWait_=true;freshWaitAt_=now;}
            if (now-freshWaitAt_>=DataWait) return Stop("fresh_sample_unavailable");
            status_="waiting_fresh_sample";reason_="send_sample_too_old";return {};
        }
        freshnessWait_=false;
        const auto* area=catalog.Find(o.area);
        // Returning from a dungeon can leave the actor in an entrance area.
        // Permit departure from that exact initial observation, or walking
        // within the requested final entrance. Never allow entrance transit.
        const bool exitingInitialGate=initialDungeonGate_ && area && area->kind=="dungeon_gate"
            && o.area==binding_.area && o.map==binding_.map && o.generation==binding_.generation;
        if (!SupportedTarget(area,target_.area) && !exitingInitialGate) return Stop("unsupported_area");
        if (area->portals.size()>256 || area->walkable.size()>256) return Stop("invalid_catalog");
        if (birthCheck_ && !escaping_)
        {
            const bool near=std::any_of(area->portals.begin(),area->portals.end(),[&](const Portal& p){return Near(p,o.position);});
            if (near)
            {
                std::string error;
                if (!Escape(catalog,*area,o.position,escape_,error)) return Stop(error.c_str());
                escaping_=true;
            }
            else birthCheck_=false;
        }
        if (!escaping_ && !selected_ && o.area!=target_.area)
        {
            std::vector<Portal> path;std::string error;
            if (!catalog.Route(o.area,target_.area,path,error)) return Stop(error.c_str());
            if (path.empty() || path.size()>256 || path.front().source!=o.area) return Stop("invalid_route");
            for (const auto& p:path) if (!SupportedTarget(catalog.Find(p.target),target_.area)) return Stop("unsupported_area");
            portal_=path.front();selected_=true;
        }
        const auto end=escaping_?escape_:(selected_?portal_.point:target_.point);
        std::vector<Portal> blocked;
        for (const auto& p:area->portals)
            if (!(selected_ && !escaping_ && SamePortal(p,portal_)) && !(escaping_ && Near(p,o.position))) blocked.push_back(p);
        std::vector<Waypoint> points;std::string error;
        if (!catalog.Waypoints(o.area,o.position,end,blocked,points,error)) return Stop(error.c_str());
        if (points.empty() || !(points.back()==end)) return Stop("invalid_route");
        if (escaping_)
        {
            if (points.size()!=1) return Stop("portal_rearm_unavailable");
            for (const auto& p:area->portals) if (Near(p,o.position) && !MonotonicExit(p,o.position,end)) return Stop("portal_rearm_unavailable");
        }
        const auto point=points.front();
        if (havePlan_ && point==lastPlan_ && !Moved(o.position,lastPlanPosition_)) ++repeats_;
        else repeats_=0;
        if (repeats_>=3) return Stop("native_no_progress");
        if (steps_>=MaximumSteps) return Stop("route_step_limit");
        ++steps_;havePlan_=true;lastPlan_=point;lastPlanPosition_=o.position;
        source_=o;issued_=point;pending_=true;activeEscape_=escaping_;pendingAt_=now;portalWait_=false;haveIngress_=false;
        status_="awaiting_confirmation";reason_="native_destination_pending";
        return {Action::Kind::SetDestination,point,reason_};
    }
    void Controller::ConfirmDestination(const Observation& o,Waypoint point,std::uint64_t now,bool accepted)
    {
        if (!active_ || !pending_) return;
        if (now<lastNow_) {Cancel("clock_regressed");return;}
        lastNow_=now;
        if (!(point==issued_) || !o.valid || o.scene!=1 || !BindingMatches(o) || o.busy
            || o.area!=source_.area || o.map!=source_.map || o.generation!=source_.generation
            || o.sequence<source_.sequence || o.capturedTick<source_.capturedTick || o.capturedTick>now || now-o.capturedTick>SampleAge)
        {Cancel("confirmation_context_changed");return;}
        if (!accepted) {Cancel("native_destination_rejected");return;}
        pending_=false;leg_=true;acceptedAt_=now;acceptedSequence_=o.sequence;
        progressAt_=now;progressPosition_=o.position;status_="moving";reason_="native_destination_set";
    }
    Action Controller::Tick(const Catalog& catalog,const Observation& o,std::uint64_t now)
    {
        if (!active_) return {};
        if (now<lastNow_) return Stop("clock_regressed");
        lastNow_=now;
        if (now-began_>=RouteTime) return Stop("route_timeout");
        if (KnownIdentityChanged(o)) return Stop("identity_changed");
        if ((o.scene!=-1 && o.scene!=1) || (o.valid && o.scene!=1)) return Stop("unexpected_scene");
        if (catalog.pvfSha256!=catalogHash_) return Stop("catalog_changed");
        if (o.valid && o.capturedTick>now) return Stop("future_capture");
        if (!o.valid || now-o.capturedTick>SampleAge)
        {
            if (!missing_) {missing_=true;missingAt_=now;}
            if (now-missingAt_>=DataWait) return Stop("capture_unavailable");
            status_="waiting_data";reason_="capture_unavailable";return {};
        }
        missing_=false;
        if (!BindingMatches(o) || !o.map || !o.sequence || !o.generation) return Stop("identity_changed");
        if (!Coordinate(o.position.x) || !Coordinate(o.position.y)) return Stop("position_unknown");
        if (!Monotonic(o)) return Stop("snapshot_regressed");
        if (o.busy) return Stop("ui_busy");
        if (o.area!=current_.area)
        {
            if (!leg_ || !selected_ || o.area!=portal_.target) return Stop("unexpected_area");
            if (o.generation<=current_.generation || o.map==current_.map) return Stop("map_transition_unproven");
            leg_=pending_=selected_=escaping_=activeEscape_=haveContinuation_=havePlan_=portalWait_=false;
            birthCheck_=true;repeats_=0;current_=o;source_=o;
            if (!o.autoKnown) return Stop("native_auto_move_unknown");
            ingress_=o;haveIngress_=true;
            status_="queued";reason_="area_entered";
            return {};
        }
        else
        {
            if (o.generation!=current_.generation || o.map!=current_.map) return Stop("unexpected_context");
            current_=o;
        }
        if (pending_)
        {
            if (now-pendingAt_>=DataWait) return Stop("native_confirmation_timeout");
            return {};
        }
        if (leg_)
        {
            if (o.sequence<=acceptedSequence_ || o.capturedTick<=acceptedAt_) return {};
            if (!o.autoKnown) return Stop("native_auto_move_unknown");
            if (!o.autoActive) return Stop("native_auto_move_stopped");
            if (!(o.destination==issued_)) return Stop("destination_changed");
            if (Moved(o.position,progressPosition_)) {progressPosition_=o.position;progressAt_=now;}
            if (AtPoint(o,issued_))
            {
                if (selected_ && !activeEscape_ && issued_==portal_.point)
                {
                    if (!portalWait_) {portalWait_=true;portalWaitAt_=now;}
                    if (now-portalWaitAt_>=DataWait) return Stop("portal_not_crossed");
                    status_="waiting_transition";reason_="portal_transition_pending";return {};
                }
                leg_=false;completed_=o;haveContinuation_=true;
                if (activeEscape_)
                {
                    const auto* area=catalog.Find(o.area);
                    if (!area || std::any_of(area->portals.begin(),area->portals.end(),[&](const Portal& p){return Near(p,o.position);})) return Stop("portal_rearm_incomplete");
                    escaping_=activeEscape_=birthCheck_=false;
                }
                else if (!selected_ && o.area==target_.area && issued_==target_.point)
                {active_=false;status_="arrived";reason_="final_position_observed";return {Action::Kind::Arrived,target_.point,reason_};}
            }
            else
            {
                if (now-progressAt_>=DataWait) return Stop("native_no_progress");
                if (now-acceptedAt_>=LegTime) return Stop("native_leg_timeout");
                status_="moving";reason_="native_destination_set";return {};
            }
        }
        return Prepare(catalog,o,now);
    }
}
