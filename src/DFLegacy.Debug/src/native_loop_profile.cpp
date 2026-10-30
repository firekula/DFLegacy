#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "native_loop_profile.h"
#include <sstream>
#include <locale>
#include <cwchar>

namespace dfl::native_loop_profile
{
    namespace
    {
        volatile LONG Active=0,Drops=0;SRWLOCK Lock=SRWLOCK_INIT;
        Model Recorder{};client_state::Reader Native{};wchar_t Directory[MAX_PATH]{};
        std::uint64_t LastExport=0;
        void Number(std::ostream& o,bool known,std::uint64_t n){if(known)o<<n;else o<<"null";}
        template<class A> void Array(std::ostream& o,const A& a,bool known=true)
        {o<<'[';for(std::size_t i=0;i<a.size();++i){if(i)o<<',';Number(o,known,a[i]);}o<<']';}
        std::array<std::uint64_t,Parts> PartsOf(const std::uint64_t* a)
        {std::array<std::uint64_t,Parts> r{};for(unsigned i=0;i<Parts;++i)r[i]=a[i+1]-a[i];return r;}
        void Stats(std::ostream& o,const Statistics& s)
        {
            o<<"{\"frames\":"<<s.frames<<",\"intervals\":"<<s.intervals<<",\"cpu_frames\":"<<s.cpuFrames<<",\"cpu_intervals\":"<<s.cpuIntervals
                <<",\"foreground_frames\":"<<s.foregroundFrames<<",\"background_frames\":"<<s.backgroundFrames
                <<",\"unknown_foreground_frames\":"<<s.unknownForegroundFrames<<",\"foreground_transition_frames\":"<<s.transitionFrames
                <<",\"loops_over15ms\":"<<s.over15ms<<",\"outside_over50ms\":"<<s.betweenOver50ms<<",\"max_loop_us\":";
            Number(o,s.frames!=0,s.maxLoop);o<<",\"max_interval_us\":";Number(o,s.intervals!=0,s.maxInterval);
            o<<",\"max_outside_us\":";Number(o,s.intervals!=0,s.maxBetween);o<<",\"max_cpu_loop_us\":";Number(o,s.cpuFrames!=0,s.maxCpuLoop);
            o<<",\"max_cpu_outside_us\":";Number(o,s.cpuIntervals!=0,s.maxCpuBetween);
            o<<",\"maximum_sections_us\":";Array(o,s.parts,s.frames!=0);o<<",\"maximum_cpu_sections_us\":";Array(o,s.cpuParts,s.cpuFrames!=0);o<<'}';
        }
        void Bucket(std::ostream& o,const Window& w)
        {o<<"{\"start_tick_ms\":"<<w.start<<",\"last_tick_ms\":"<<w.last<<",\"statistics\":";Stats(o,w.stats);o<<'}';}
        void EventJson(std::ostream& o,const Event& e)
        {
            const auto& s=e.sample;const auto& p=e.previous;
            o<<"{\"event_id\":"<<e.id<<",\"sequence\":"<<s.sequence<<",\"thread\":"<<s.thread<<",\"begin_tick_ms\":"<<s.tickMs
                <<",\"tick_ms\":"<<s.endTickMs<<",\"scene_begin\":"<<s.sceneBegin<<",\"scene_end\":"<<s.sceneEnd
                <<",\"foreground_known\":"<<(s.foregroundKnown!=0)<<",\"foreground_begin\":"<<(s.foregroundBegin!=0)
                <<",\"foreground_end\":"<<(s.foregroundEnd!=0)<<",\"loop_us\":"<<s.wallUs[7]-s.wallUs[0]<<",\"sections_us\":";
            Array(o,PartsOf(s.wallUs));o<<",\"cpu_known\":"<<e.cpu<<",\"cpu_loop_us\":";Number(o,e.cpu,s.cpuUs[7]-s.cpuUs[0]);
            o<<",\"cpu_sections_us\":";if(e.cpu)Array(o,PartsOf(s.cpuUs));else o<<"null";
            o<<",\"interval_known\":"<<e.interval<<",\"interval_us\":";Number(o,e.interval,s.wallUs[0]-p.wallUs[0]);
            o<<",\"outside_us\":";Number(o,e.interval,s.wallUs[0]-p.wallUs[7]);
            o<<",\"cpu_outside_known\":"<<e.cpuInterval<<",\"cpu_outside_us\":";Number(o,e.cpuInterval,s.cpuUs[0]-p.cpuUs[7]);
            o<<",\"previous_sequence\":";Number(o,e.interval,p.sequence);o<<",\"previous_loop_us\":";Number(o,e.interval,p.wallUs[7]-p.wallUs[0]);
            o<<",\"previous_sections_us\":";if(e.interval)Array(o,PartsOf(p.wallUs));else o<<"null";
            const bool previousCpu=e.interval&&CpuKnown(p);o<<",\"previous_cpu_loop_us\":";Number(o,previousCpu,p.cpuUs[7]-p.cpuUs[0]);o<<'}';
        }
    }
    bool Enabled() noexcept{return InterlockedCompareExchange(&Active,0,0)==1;}
    void Initialize(const wchar_t* ini,const wchar_t* directory,client_state::Reader reader) noexcept
    {
        InterlockedExchange(&Active,0);
        if(!ini||!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64||!reader.pid
            ||GetPrivateProfileIntW(L"DebugOverlay",L"ExportNativeLoopProfile",0,ini)!=1)return;
        if(!reader.processStartedFileTime)
        {
            FILETIME created{},exited{},kernel{},user{};
            if(reader.pid!=GetCurrentProcessId()||!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return;
            reader.processStartedFileTime=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        }
        Native=reader;Recorder={};Drops=0;LastExport=0;wcscpy_s(Directory,directory);InterlockedExchange(&Active,1);
    }
    void Record(const debug_api::NativeLoopSample& sample) noexcept
    {
        if(!Enabled())return;
        if(!TryAcquireSRWLockExclusive(&Lock)){InterlockedIncrement(&Drops);return;}
        Recorder.Add(sample);ReleaseSRWLockExclusive(&Lock);
    }
    void Export() noexcept
    {
        if(!Enabled())return;
        try
        {
            const auto throttle=GetTickCount64();if(LastExport&&throttle-LastExport<200)return;LastExport=throttle;
            Snapshot snapshot;
            if(!TryAcquireSRWLockShared(&Lock))return;snapshot=Recorder.value;ReleaseSRWLockShared(&Lock);
            const auto tick=GetTickCount64();
            std::ostringstream o;o.imbue(std::locale::classic());o<<std::boolalpha;
            o<<"{\"schema\":\"dfl-native-loop-profile/v1\",\"pid\":"<<Native.pid<<",\"process_started_filetime\":"<<Native.processStartedFileTime
                <<",\"exported_tick_ms\":"<<tick<<",\"captured_tick_ms\":"<<snapshot.lastTick<<",\"received\":"<<snapshot.received
                <<",\"invalid_records\":"<<snapshot.invalid<<",\"nested_records\":"<<snapshot.nested
                <<",\"sequence_discontinuities\":"<<snapshot.discontinuities<<",\"lock_drops\":"<<InterlockedCompareExchange(&Drops,0,0)
                <<",\"slow_events_total\":"<<snapshot.slowTotal<<",\"capacity\":32,\"parts\":[\"critical_sections_and_pre_update\",\"network_dispatch\",\"ui_78C9E0\",\"scene_79CE00\",\"post_scene\",\"pacing_433660\",\"tail_792A70\"],\"total\":";
            Stats(o,snapshot.total);o<<",\"current_window\":";Bucket(o,snapshot.current);o<<",\"completed_windows\":[";
            for(std::size_t i=0;i<snapshot.windowCount;++i){if(i)o<<',';Bucket(o,snapshot.windows[i]);}
            o<<"],\"last_sample\":";if(snapshot.have)EventJson(o,snapshot.last);else o<<"null";o<<",\"slow_events\":[";
            for(std::size_t i=0;i<snapshot.eventCount;++i){if(i)o<<',';EventJson(o,snapshot.events[i]);}
            o<<"],\"scope\":\"Original ACT1 433B00 through original tail return. Between-call intervals include message/wait work outside this function and observer return overhead. Segment boundaries include marker overhead; CPU is quantized user+kernel thread time, not GPU time. Scene tags are the last DebugRender observation, not direct scene-edge events. Foreground endpoints are not continuous foreground. Nested/incomplete records and sequence gaps are not attributed as outside-loop time.\"}";
            if(!CreateDirectoryW(Directory,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
            wchar_t path[MAX_PATH]{},temp[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-native-loop-%lu.json",Directory,Native.pid)<0||swprintf_s(temp,L"%s.tmp",path)<0)return;
            const auto json=o.str();const auto f=CreateFileW(temp,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(f==INVALID_HANDLE_VALUE)return;
            DWORD written=0;const bool ok=WriteFile(f,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)&&written==json.size();CloseHandle(f);
            if(!ok||!MoveFileExW(temp,path,MOVEFILE_REPLACE_EXISTING))DeleteFileW(temp);
        }
        catch(...){/* Diagnostic worker IO never escapes to gameplay. */}
    }
    void Shutdown() noexcept{InterlockedExchange(&Active,0);}
}
