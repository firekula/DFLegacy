#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_navigation.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
namespace dfl::client_navigation
{
    namespace
    {
        enum class MarkerPolicy { RequireBlank, AllowQuestIcons };
        constexpr std::uint32_t QuestIconVtable=0xA84758,QuestIconHit=0x882E40;
        constexpr std::uint32_t TownNpcVtable=0xA70BD0,TownNpcHit=0x882B40;

        struct Reads
        {
            const client_state::Reader& r;
            template<class T> bool At(std::uint32_t base, std::uint32_t offset, T& value) const noexcept
            {
                const auto address=static_cast<std::uint64_t>(base)+offset;
                return base>=0x10000 && address+sizeof(T)<=0x100000000ULL && r.read
                    && r.read(r.context,static_cast<std::uint32_t>(address),&value,sizeof(T));
            }
        };
        bool GoodRect(const Rect& r) noexcept
        {
            return r.width>0 && r.height>0 && r.width<=1000000 && r.height<=1000000
                && static_cast<std::int64_t>(r.x)+r.width<=INT32_MAX
                && static_cast<std::int64_t>(r.y)+r.height<=INT32_MAX;
        }
        bool Inside(const Rect& r, std::int32_t x, std::int32_t y) noexcept
        {
            return GoodRect(r) && x>=r.x && y>=r.y && static_cast<std::int64_t>(x)<static_cast<std::int64_t>(r.x)+r.width
                && static_cast<std::int64_t>(y)<static_cast<std::int64_t>(r.y)+r.height;
        }
        bool InsideMarker(std::int32_t px, std::int32_t py, std::int32_t mx, std::int32_t my,
                          std::int64_t half, std::int64_t above, std::int64_t below) noexcept
        {
            const auto x=static_cast<std::int64_t>(px)-mx,y=static_cast<std::int64_t>(py)-my;
            return x>=-half && x<=half && y>=-above && y<=below;
        }
        bool ReadQuestSpriteSize(const Reads& r, std::uint32_t quest, std::uint32_t& width, std::uint32_t& height) noexcept
        {
            std::uint32_t sprite=0;
            return r.At(quest,0x1C,sprite) && r.At(sprite,0x24,width) && r.At(sprite,0x28,height)
                && width<=8192 && height<=8192;
        }
        bool OwnedQuestOverlayContains(const Reads& r, std::uint32_t npc, std::uint32_t begin, std::uint32_t end,
                                       std::int32_t px, std::int32_t py) noexcept
        {
            std::uint32_t quest=0,table=0,hit=0,owner=0,image=0,width=0,height=0;
            std::int32_t mx=0,my=0;unsigned char visible=0;
            // The NPC owns the quest marker and the quest marker links back to it.
            if (!r.At(npc,0x28,quest) || !r.At(quest,0,table) || table!=QuestIconVtable || !r.At(table,4,hit) || hit!=QuestIconHit
                || !r.At(quest,0x3C,owner) || owner!=npc || !r.At(quest,0x41,visible) || visible!=1
                || !r.At(quest,0x10,image) || !image || !r.At(quest,8,mx) || !r.At(quest,12,my)
                || !ReadQuestSpriteSize(r,quest,width,height) || !width || !height
                || !InsideMarker(px,py,mx,my,width/5,height*2/5,0)) return false;
            for (auto slot=begin;slot<end;slot+=4)
            {
                std::uint32_t marker=0;
                if (!r.At(slot,0,marker)) return false;
                if (marker==quest) return true;
            }
            return false;
        }
    }
    const char* ValidateRequest(const Request& r, const client_state::Snapshot& s, std::uint64_t now, std::uint64_t consumed) noexcept
    {
        if (std::memcmp(r.magic,"DLNV",4)!=0 || r.version!=1 || r.size!=sizeof(Request)) return "invalid_request";
        if (!r.requestId || r.requestId<=consumed) return "replayed_request";
        if (!s.valid || !s.layoutSupported || s.truncated || !s.haveMe || !s.scene.known || s.scene.value!=1
            || !s.town.present || !s.me.uid.known || !s.town.position.known) return "town_unavailable";
        if (!r.issuedTickMs || r.issuedTickMs>now || now-r.issuedTickMs>1000
            || !r.snapshotTickMs || r.snapshotTickMs>now || now-r.snapshotTickMs>750
            || s.capturedTickMs>now || now-s.capturedTickMs>750) return "expired_request";
        if (r.snapshotTickMs>s.capturedTickMs || !r.snapshotSequence || r.snapshotSequence>s.sequence
            || s.sequence-r.snapshotSequence>5) return "snapshot_mismatch";
        if (r.pid!=s.pid || r.processStartedFileTime!=s.processStartedFileTime || r.uid!=s.me.uid.value
            || r.generation!=s.generation || r.actorToken!=s.me.actorToken || r.userToken!=s.me.userToken
            || r.mapToken!=s.me.mapToken || r.mapToken!=s.town.mapToken || r.contextToken!=s.town.contextToken) return "identity_mismatch";
        if (r.townId!=s.town.townId || r.areaId!=s.town.areaId) return "cross_area_unsupported";
        if (r.townId<0 || r.townId>255 || r.areaId<0 || r.areaId>255
            || r.x < -1000000 || r.x>1000000 || r.y < -1000000 || r.y>1000000) return "invalid_target";
        return nullptr;
    }
    bool InverseProject(const Rect& project, const Rect& real, std::int32_t px, std::int32_t py, std::int32_t& x, std::int32_t& y) noexcept
    {
        if (!Inside(project,px,py) || !GoodRect(real)) return false;
        const double a=real.x+static_cast<double>(static_cast<std::int64_t>(px)-project.x)*real.width/project.width;
        const double b=real.y+static_cast<double>(static_cast<std::int64_t>(py)-project.y)*real.height/project.height;
        if (!std::isfinite(a) || !std::isfinite(b) || a<INT32_MIN || a>INT32_MAX || b<INT32_MIN || b>INT32_MAX) return false;
        const auto rx=static_cast<std::int32_t>(std::round(a)), ry=static_cast<std::int32_t>(std::round(b));
        if (!Inside(real,rx,ry)) return false;
        x=rx;y=ry;return true;
    }
    bool ReadRegions(const client_state::Reader& reader, std::uint32_t hash, std::array<RegionRect,MaximumRegions>& output, std::size_t& count) noexcept
    {
        count=0;Reads r{reader};std::uint32_t sentinel=0,total=0,node=0,last=0;
        if (!r.At(hash,8,sentinel) || sentinel<0x10000 || !r.At(hash,12,total) || total>MaximumRegions
            || !r.At(sentinel,0,node) || !r.At(sentinel,4,last)) return false;
        std::array<std::uint32_t,MaximumRegions> seen{};std::uint32_t previous=sentinel;
        while (node!=sentinel)
        {
            if (count>=total || count>=MaximumRegions || std::find(seen.begin(),seen.begin()+count,node)!=seen.begin()+count) return false;
            seen[count]=node;std::uint32_t next=0,prev=0,record=0,rect=0;std::int32_t key=0;RegionRect row;
            if (!r.At(node,0,next) || !r.At(node,4,prev) || prev!=previous || !r.At(node,8,key)
                || !r.At(node,12,record) || !r.At(record,0,row.town) || !r.At(record,4,row.area)
                || row.town<0 || row.town>255 || row.area<0 || row.area>255 || key!=(row.town<<10)+row.area
                || !r.At(record,8,rect) || !r.At(rect,0,row.rect) || !GoodRect(row.rect)) return false;
            for (std::size_t i=0;i<count;++i) if (output[i].town==row.town && output[i].area==row.area) return false;
            output[count++]=row;previous=node;node=next;
        }
        std::uint32_t finalSentinel=0,finalTotal=0,first=0,finalLast=0;
        return count==total && previous==last && r.At(hash,8,finalSentinel) && finalSentinel==sentinel
            && r.At(hash,12,finalTotal) && finalTotal==total && r.At(sentinel,0,first)
            && first==(count?seen[0]:sentinel) && r.At(sentinel,4,finalLast) && finalLast==last;
    }
    static bool MarkersAllowPoint(const client_state::Reader& reader, std::uint32_t plane,
                                  std::int32_t px, std::int32_t py, MarkerPolicy policy) noexcept
    {
        Reads r{reader};std::uint32_t begin=0,end=0;
        if (!r.At(plane,0x14,begin) || !r.At(plane,0x18,end) || end<begin || (end-begin)%4 || (end-begin)/4>256) return false;
        for (std::uint32_t i=0;i<(end-begin)/4;++i)
        {
            std::uint32_t marker=0,table=0,hit=0,image=0,width=0,height=0;std::int32_t mx=0,my=0;
            if (!r.At(begin,i*4,marker)) return false;
            if (!marker) continue;
            if (!r.At(marker,0,table) || !r.At(table,4,hit)
                || (hit!=0x882A40 && hit!=TownNpcHit && hit!=0x882BB0 && hit!=0x882C80 && hit!=QuestIconHit)
                || !r.At(marker,0x10,image)) return false;
            if (!image) continue; // All supported native hit functions return false without image.
            if (!r.At(marker,8,mx) || !r.At(marker,12,my)) return false;
            std::int64_t half=0,above=0,below=0;
            if (hit==0x882BB0) half=above=below=23;
            else if (hit==QuestIconHit)
            {
                if (!ReadQuestSpriteSize(r,marker,width,height)) return false;
                // ACT1's town_mini_map::QuestIcon is a plane marker with no UI capture ID.
                if (policy==MarkerPolicy::AllowQuestIcons && table==QuestIconVtable) continue;
                // The legacy exclusion rounds outward from the native 0.2 width and 0.4 height.
                half=static_cast<std::int64_t>(std::ceil(width*0.4000000059604645*0.5));
                above=static_cast<std::int64_t>(std::ceil(height*0.4000000059604645));
            }
            else
            {
                if (!r.At(image,0x14,width) || !r.At(image,0x18,height) || !width || !height || width>8192 || height>8192) return false;
                half=width/2;above=hit==0x882A40?height/2:height;below=hit==0x882A40?height/2:0;
            }
            if (InsideMarker(px,py,mx,my,half,above,below)
                && (policy!=MarkerPolicy::AllowQuestIcons || table!=TownNpcVtable || hit!=TownNpcHit
                    || !OwnedQuestOverlayContains(r,marker,begin,end,px,py))) return false;
        }
        std::uint32_t finalBegin=0,finalEnd=0;
        return r.At(plane,0x14,finalBegin) && finalBegin==begin && r.At(plane,0x18,finalEnd) && finalEnd==end;
    }
    bool BlankMarkers(const client_state::Reader& reader, std::uint32_t plane, std::int32_t px, std::int32_t py) noexcept
    {
        return MarkersAllowPoint(reader,plane,px,py,MarkerPolicy::RequireBlank);
    }
    bool ReadCanvasTarget(const client_state::Reader& reader, const client_state::Snapshot& s, std::uint32_t minimap, std::int32_t& x, std::int32_t& y) noexcept
    {
        if (!s.valid || !s.haveMe || !s.town.present || !s.scene.known || s.scene.value!=1) return false;
        Reads r{reader};std::uint32_t im=0,owner=0,table=0,panel=0,id=0,capture=0,plane=0;unsigned char visible=0,enabled=0,prior=0,now=0,mode=0;
        std::int32_t dx=0,dy=0,mx=0,my=0,cx=0,cy=0;
        if (!r.At(0xB5DCE8,0,im) || !r.At(im,0xFF0,owner) || owner!=minimap || !r.At(minimap,0,table) || table!=0xA7CF3C
            || !r.At(minimap,4,panel) || !r.At(panel,0x10,visible) || visible!=1 || !r.At(panel,0x11,enabled) || enabled!=1
            || !r.At(panel,12,id) || !r.At(0xB55184,0,capture) || capture!=id || !r.At(im,0xFD8,prior) || prior!=1
            || !r.At(0xCFD200,0,now) || now!=0 || !r.At(0xCFD20A,0,mode) || mode!=0
            || !r.At(minimap,0xC,plane) || !plane || !r.At(0xBEEFC4,0,dx) || !r.At(0xB53C28,0,dy)
            || dx < -4096 || dx>4096 || dy < -4096 || dy>4096 || !r.At(plane,4,cx) || !r.At(plane,8,cy) || cx!=-dx || cy!=-dy
            || !r.At(0xCFD1EC,0,mx) || !r.At(0xCFD1F0,0,my)) return false;
        const auto wideX=static_cast<std::int64_t>(mx)-dx,wideY=static_cast<std::int64_t>(my)-dy;
        if (wideX<INT32_MIN || wideX>INT32_MAX || wideY<INT32_MIN || wideY>INT32_MAX) return false;
        const auto px=static_cast<std::int32_t>(wideX),py=static_cast<std::int32_t>(wideY);
        std::array<RegionRect,MaximumRegions> projects{},reals{};std::size_t pc=0,rc=0;
        if (plane>UINT32_MAX-0x20 || !ReadRegions(reader,plane+0x20,projects,pc) || !ReadRegions(reader,0xBEF024,reals,rc)) return false;
        const RegionRect* project=nullptr;const RegionRect* real=nullptr;std::size_t hits=0;
        for (std::size_t i=0;i<pc;++i) if (Inside(projects[i].rect,px,py)) { ++hits;project=&projects[i]; }
        if (hits!=1 || !project || project->town!=s.town.townId || project->area!=s.town.areaId) return false;
        for (std::size_t i=0;i<rc;++i) if (reals[i].town==s.town.townId && reals[i].area==s.town.areaId) real=&reals[i];
        std::uint32_t finalOwner=0,finalPlane=0;unsigned char finalVisible=0;
        return real && BlankMarkers(reader,plane,px,py) && InverseProject(project->rect,real->rect,px,py,x,y)
            && r.At(im,0xFF0,finalOwner) && finalOwner==minimap && r.At(minimap,0xC,finalPlane) && finalPlane==plane
            && r.At(panel,0x10,finalVisible) && finalVisible==1;
    }
    bool ReadRouteCanvasTarget(const client_state::Reader& reader, const client_state::Snapshot& s,
                               std::uint32_t minimap, CanvasGoal& output, std::uint32_t eventBefore) noexcept
    {
        if (eventBefore!=16 || !s.valid || !s.layoutSupported || s.truncated || !s.haveMe || !s.town.present
            || !s.scene.known || s.scene.value!=1) return false;
        constexpr std::uint32_t captureAddress=0xB55188,eventAddress=0xCFD1F8,buttonAddress=0xCFD202;
        Reads r{reader};
        std::uint32_t im=0,owner=0,table=0,panel=0,id=0,capture=0,plane=0,regionsMenu=0,npcsMenu=0,event=0;
        unsigned char visible=0,enabled=0,now=0,mode=0,regionsOpen=0,npcsOpen=0;
        std::int32_t dx=0,dy=0,mx=0,my=0,cx=0,cy=0;
        // Read after native8F6C30 consumes this call's selected release event
        // and writes its capture. The update phase may already clear FD8/FD9.
        if (!r.At(0xB5DCE8,0,im) || !r.At(im,0xFF0,owner) || owner!=minimap
            || !r.At(minimap,0,table) || table!=0xA7CF3C || !r.At(minimap,4,panel)
            || !r.At(panel,0x10,visible) || visible!=1 || !r.At(panel,0x11,enabled) || enabled!=1
            || !r.At(panel,12,id) || !r.At(captureAddress,0,capture) || capture!=id
            || !r.At(eventAddress,0,event) || event!=0 || !r.At(buttonAddress,0,now) || now!=0
            || !r.At(0xCFD20A,0,mode) || mode!=0
            || !r.At(minimap,0x1C,regionsMenu) || !r.At(regionsMenu,0x10,regionsOpen) || regionsOpen!=0
            || !r.At(minimap,0x64,npcsMenu) || !r.At(npcsMenu,0x10,npcsOpen) || npcsOpen!=0
            || !r.At(minimap,0xC,plane) || !plane || !r.At(0xBEEFC4,0,dx) || !r.At(0xB53C28,0,dy)
            || dx < -4096 || dx>4096 || dy < -4096 || dy>4096
            || !r.At(plane,4,cx) || !r.At(plane,8,cy) || cx!=-dx || cy!=-dy
            || !r.At(0xCFD1EC,0,mx) || !r.At(0xCFD1F0,0,my)) return false;
        const auto wideX=static_cast<std::int64_t>(mx)-dx,wideY=static_cast<std::int64_t>(my)-dy;
        if (wideX<INT32_MIN || wideX>INT32_MAX || wideY<INT32_MIN || wideY>INT32_MAX) return false;
        const auto px=static_cast<std::int32_t>(wideX),py=static_cast<std::int32_t>(wideY);
        std::array<RegionRect,MaximumRegions> projects{},reals{};std::size_t pc=0,rc=0;
        if (plane>UINT32_MAX-0x20 || !ReadRegions(reader,plane+0x20,projects,pc)
            || !ReadRegions(reader,0xBEF024,reals,rc)) return false;
        const RegionRect* project=nullptr;const RegionRect* real=nullptr;std::size_t hits=0;
        for (std::size_t i=0;i<pc;++i) if (Inside(projects[i].rect,px,py)) { ++hits;project=&projects[i]; }
        if (hits!=1 || !project) return false;
        for (std::size_t i=0;i<rc;++i)
            if (reals[i].town==project->town && reals[i].area==project->area) real=&reals[i];
        CanvasGoal goal;
        if (!real || !MarkersAllowPoint(reader,plane,px,py,MarkerPolicy::AllowQuestIcons)
            || !InverseProject(project->rect,real->rect,px,py,goal.x,goal.y)) return false;
        goal.town=project->town;goal.area=project->area;
        goal.mapX=px;goal.mapY=py;goal.minimapToken=minimap;goal.planeToken=plane;
        // A reader can fail or observe a replaced/closed map. Publish only a complete,
        // still-owned gesture; never call native input, UI or destination routines here.
        std::uint32_t finalIm=0,finalOwner=0,finalTable=0,finalPanel=0,finalId=0,finalCapture=0,finalPlane=0,finalRegions=0,finalNpcs=0,finalEvent=0;
        unsigned char finalVisible=0,finalEnabled=0,finalNow=0,finalMode=0,finalRegionsOpen=0,finalNpcsOpen=0;
        std::int32_t finalDx=0,finalDy=0,finalMx=0,finalMy=0,finalCx=0,finalCy=0;
        if (!r.At(0xB5DCE8,0,finalIm) || finalIm!=im || !r.At(im,0xFF0,finalOwner) || finalOwner!=minimap
            || !r.At(minimap,0,finalTable) || finalTable!=table || !r.At(minimap,4,finalPanel) || finalPanel!=panel
            || !r.At(panel,12,finalId) || finalId!=id || !r.At(captureAddress,0,finalCapture) || finalCapture!=id
            || !r.At(panel,0x10,finalVisible) || finalVisible!=1 || !r.At(panel,0x11,finalEnabled) || finalEnabled!=1
            || !r.At(eventAddress,0,finalEvent) || finalEvent!=0 || !r.At(buttonAddress,0,finalNow) || finalNow!=0
            || !r.At(0xCFD20A,0,finalMode) || finalMode!=0
            || !r.At(minimap,0x1C,finalRegions) || finalRegions!=regionsMenu
            || !r.At(regionsMenu,0x10,finalRegionsOpen) || finalRegionsOpen!=0
            || !r.At(minimap,0x64,finalNpcs) || finalNpcs!=npcsMenu || !r.At(npcsMenu,0x10,finalNpcsOpen) || finalNpcsOpen!=0
            || !r.At(minimap,0xC,finalPlane) || finalPlane!=plane
            || !r.At(0xBEEFC4,0,finalDx) || finalDx!=dx || !r.At(0xB53C28,0,finalDy) || finalDy!=dy
            || !r.At(plane,4,finalCx) || finalCx!=cx || !r.At(plane,8,finalCy) || finalCy!=cy
            || !r.At(0xCFD1EC,0,finalMx) || finalMx!=mx || !r.At(0xCFD1F0,0,finalMy) || finalMy!=my) return false;
        output=goal;return true;
    }
    bool RestoreInstalledHook(bool& installed,const std::array<unsigned char,5>& patched,PatchOperation replace) noexcept
    {
        constexpr unsigned char original[]={0xE8,0xD9,0x3B,0x0E,0x00};
        if (!installed) return true;
        if (!replace || !replace(patched.data(),original)) return false;
        installed=false;return true;
    }
    bool SetAndActivate(const Request& request,const DestinationOperations& operations) noexcept
    {
        return operations.setAndVerify && operations.activate
            && operations.setAndVerify(operations.context,request) && operations.activate(operations.context,request);
    }
    bool RestoreInstalledHitHook(bool& installed,const std::array<unsigned char,5>& patched,PatchOperation replace) noexcept
    {
        constexpr unsigned char original[]={0xE8,0x7C,0xFC,0xFF,0xFF};
        if (!installed) return true;
        if (!replace || !replace(patched.data(),original)) return false;
        installed=false;return true;
    }
    bool InstallCanvasHooks(bool& pollInstalled,bool& hitInstalled,const std::array<unsigned char,5>& pollPatch,
                            const std::array<unsigned char,5>& hitPatch,PatchOperation poll,PatchOperation hit) noexcept
    {
        constexpr unsigned char originalPoll[]={0xE8,0xD9,0x3B,0x0E,0x00},originalHit[]={0xE8,0x7C,0xFC,0xFF,0xFF};
        if (pollInstalled || hitInstalled || !poll || !hit || !poll(originalPoll,pollPatch.data())) return false;
        pollInstalled=true;
        if (hit(originalHit,hitPatch.data())) {hitInstalled=true;return true;}
        // A failed rollback deliberately leaves ownership set, blocking unload.
        RestoreInstalledHook(pollInstalled,pollPatch,poll);return false;
    }
    std::int32_t PollCanvasRelease(void* context,std::uint32_t eventBefore,CanvasHitOriginal original,CanvasHitAfter after) noexcept
    {
        if (!original) return 0;
        const auto result=original(context);
        if (eventBefore==16 && after) after(context,eventBefore);
        return result;
    }
    bool SetLiveAndActivate(const Request& request,const DestinationOperations& operations,LiveCheck live,std::uint64_t now) noexcept
    {
        return request.issuedTickMs&&request.issuedTickMs<=now&&now-request.issuedTickMs<=1000
            &&request.snapshotTickMs&&request.snapshotTickMs<=now&&now-request.snapshotTickMs<=750
            &&live&&live(request)&&SetAndActivate(request,operations);
    }
}
