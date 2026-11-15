#include "client_town_route_runtime.h"
#include <cmath>
#include <cstring>

namespace dfl::client_town_route
{
    town_route::Observation Observe(const client_state::Snapshot& s) noexcept
    {
        town_route::Observation o{};
        o.pid=s.pid;o.started=s.processStartedFileTime;o.uid=s.me.uid.value;
        o.user=s.me.userToken;o.actor=s.me.actorToken;o.map=s.me.mapToken;o.context=s.town.contextToken;
        o.generation=s.generation;o.sequence=s.sequence;o.capturedTick=s.capturedTickMs;
        o.scene=s.scene.known?static_cast<int>(s.scene.value):-1;
        o.area={s.town.townId,s.town.areaId};o.position={s.town.position.x,s.town.position.y};
        o.destination={s.town.destinationX.value,s.town.destinationY.value};
        o.directionX=s.town.directionX.value;o.directionY=s.town.directionY.value;
        o.autoKnown=s.town.autoMoveActive.known;o.autoActive=s.town.autoMoveActive.value;
        o.busy=s.ui.selectionMode.known&&s.ui.selectionMode.value!=0;
        o.valid=s.valid&&s.layoutSupported&&!s.truncated&&s.haveMe&&s.scene.known&&s.scene.value==1
            &&s.me.uid.known&&s.me.uid.value>0&&s.me.uid.value<=65535&&o.pid&&o.started
            &&o.user>=0x10000&&o.actor>=0x10000&&o.map>=0x10000&&o.context>=0x10000
            &&s.town.present&&s.town.mapToken==s.me.mapToken&&s.town.position.known
            &&std::isfinite(o.position.x)&&std::isfinite(o.position.y)
            &&s.town.destinationX.known&&s.town.destinationY.known
            &&s.town.directionX.known&&s.town.directionY.known&&s.ui.selectionMode.known;
        return o;
    }
    const char* ValidateGoalRequest(const client_navigation::Request& request,
        const client_state::Snapshot& snapshot,std::uint64_t now,std::uint64_t consumed) noexcept
    {
        if(std::memcmp(request.magic,"DLRT",4)!=0||request.version!=1||request.size!=sizeof(request))return "invalid_request";
        if(request.townId<0||request.townId>255||request.areaId<0||request.areaId>255)return "invalid_target";
        if(!Observe(snapshot).valid)return "town_unavailable";
        auto source=request;
        std::memcpy(source.magic,"DLNV",4);
        source.townId=snapshot.town.townId;source.areaId=snapshot.town.areaId;
        return client_navigation::ValidateRequest(source,snapshot,now,consumed);
    }
}
