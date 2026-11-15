#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "frame_profile.h"
#include <cstdio>
#include <cwchar>
#include <locale>
#include <sstream>

namespace dfl::frame_profile
{
    namespace
    {
        volatile LONG Enabled=0,Thread=0;HWND WindowHandle=nullptr;
        client_state::Reader Native{};Recorder Model{};Snapshot Published{};
        Recorder PipelineModel{};Snapshot PipelinePublished{};
        std::uint64_t PipelinePublication=0,PipelineExported=0,PipelineSkipped=0,PipelinePublishedSkipped=0;
        bool HavePipeline=false;
        SRWLOCK Lock=SRWLOCK_INIT;bool Have=false,DirectoryReady=false;
        std::uint64_t LastPublication=0,Skipped=0,PublishedSkipped=0,LastExport=0,ExportedSerial=0;
        wchar_t Directory[MAX_PATH]{};
        bool Active() noexcept{return InterlockedCompareExchange(&Enabled,0,0)!=0;}
        std::uint64_t Counter() noexcept{return Native.counterUs?Native.counterUs(Native.context):0;}
        bool Foreground() noexcept{return WindowHandle&&GetForegroundWindow()==WindowHandle;}
        void Stats(std::ostream& o,const Statistics& s)
        {
            o<<"{\"frames\":"<<s.frames<<",\"foreground_frames\":"<<s.foregroundFrames
                <<",\"unknown_foreground_frames\":"<<s.unknownForegroundFrames<<",\"background_frames\":"<<s.backgroundFrames
                <<",\"foreground_transition_frames\":"<<s.foregroundTransitionFrames
                <<",\"foreground_intervals\":"<<s.foregroundIntervals<<",\"clock_errors\":"<<s.clockErrors
                <<",\"max_hook_us\":";
            if(s.frames)o<<s.maxHookUs;else o<<"null";o<<",\"max_foreground_hook_us\":";
            if(s.foregroundFrames)o<<s.maxForegroundHookUs;else o<<"null";o<<",\"max_foreground_interval_us\":";
            if(s.foregroundIntervals)o<<s.maxForegroundGapUs;else o<<"null";
            o<<",\"foreground_hook_over15ms\":"<<s.over15ms<<",\"foreground_hook_over50ms\":"<<s.over50ms
                <<",\"foreground_intervals_over50ms\":"<<s.gapsOver50ms<<",\"maximum_parts_us\":[";
            for(std::size_t i=0;i<Parts;++i){if(i)o<<',';if(s.frames)o<<s.maximumParts[i];else o<<"null";}o<<"]}";
        }
        void Bucket(std::ostream& o,const Window& w)
        {o<<"{\"start_tick_ms\":"<<w.startTickMs<<",\"last_tick_ms\":"<<w.lastTickMs<<",\"statistics\":";Stats(o,w.stats);o<<'}';}
    }
    void Initialize(const wchar_t* settings,const wchar_t* directory,void* window,client_state::Reader reader) noexcept
    {
        InterlockedExchange(&Enabled,0);
        if(!settings||!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64
            ||GetPrivateProfileIntW(L"DebugOverlay",L"ExportFrameProfile",0,settings)!=1
            ||!reader.counterUs||!reader.pid)return;
        if(!reader.processStartedFileTime)
        {
            FILETIME created{},exited{},kernel{},user{};
            if(reader.pid!=GetCurrentProcessId()||!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return;
            reader.processStartedFileTime=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        }
        Native=reader;WindowHandle=static_cast<HWND>(window);wcscpy_s(Directory,directory);
        Model={};Published={};Have=false;DirectoryReady=false;InterlockedExchange(&Thread,0);
        PipelineModel={};PipelinePublished={};HavePipeline=false;
        PipelinePublication=PipelineExported=PipelineSkipped=PipelinePublishedSkipped=0;
        LastPublication=Skipped=PublishedSkipped=LastExport=ExportedSerial=0;
        InterlockedExchange(&Enabled,1);
    }
    Frame::Frame(std::uint32_t scene) noexcept
    {
        if(!Active())return;
        const auto current=static_cast<LONG>(GetCurrentThreadId());const auto owner=InterlockedCompareExchange(&Thread,current,0);
        if(owner&&owner!=current)return;
        active=true;sample.scene=scene;sample.beginUs=last=Counter();sample.tickMs=GetTickCount64();
        sample.foregroundKnown=WindowHandle&&IsWindow(WindowHandle);sample.foregroundBegin=Foreground();
    }
    Frame::Frame(std::uint32_t scene,std::uint64_t captureUs,std::uint64_t navigationUs) noexcept : Frame(scene)
    {
        if(!active)return;
        if(captureUs>sample.beginUs||navigationUs>sample.beginUs-captureUs){clockValid=false;return;}
        sample.beginUs-=captureUs+navigationUs;sample.parts[0]=captureUs;sample.parts[1]=navigationUs;next=2;
    }
    void Frame::Mark() noexcept
    {
        if(!active||next>=Parts-1)return;
        const auto now=Counter();
        if(!last||now<last)clockValid=false;else sample.parts[next]=now-last;
        last=now;++next;
    }
    Frame::~Frame() noexcept
    {
        if(!active||!Active())return;
        sample.endUs=Counter();sample.foregroundEnd=Foreground();
        sample.foregroundKnown=sample.foregroundKnown&&WindowHandle&&IsWindow(WindowHandle);
        if(!last||sample.endUs<last||next!=Parts-1)clockValid=false;
        else sample.parts[Parts-1]=sample.endUs-last;
        if(!clockValid)sample.endUs=0;
        Model.Add(sample);
        if(LastPublication&&sample.tickMs>=LastPublication&&sample.tickMs-LastPublication<200)return;
        if(!TryAcquireSRWLockExclusive(&Lock)){++Skipped;return;}
        Published=Model.value;PublishedSkipped=Skipped;Have=true;
        ReleaseSRWLockExclusive(&Lock);LastPublication=sample.tickMs;
    }
    namespace
    {
    void ExportOne(const Snapshot& s,std::uint64_t skipped,bool pipeline,std::uint64_t& exported)
    {
        try
        {
            const auto tick=GetTickCount64();if(s.serial==exported)return;
            // Creation belongs to the worker, including standalone profiling
            // when SDK state export is disabled.
            if(!DirectoryReady)
            {
                if(!CreateDirectoryW(Directory,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
                const auto attributes=GetFileAttributesW(Directory);
                if(attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY))return;
                DirectoryReady=true;
            }
            std::ostringstream o;o.imbue(std::locale::classic());o<<std::boolalpha;
            o<<"{\"schema\":\""<<(pipeline?"dfl-client-frame-pipeline/v1":"dfl-client-frame-profile/v1")<<"\",\"pid\":"<<Native.pid
                <<",\"process_started_filetime\":"<<Native.processStartedFileTime<<",\"serial\":"<<s.serial
                <<",\"captured_tick_ms\":"<<s.lastTickMs<<",\"exported_tick_ms\":"<<tick
                <<",\"skipped_publications\":"<<skipped<<",\"missing_buckets\":"<<s.missingBuckets
                <<",\"unattributed_clock_errors\":"<<s.unattributedClockErrors
                <<",\"parts\":"<<(pipeline?
                    "[\"input_and_original_overlay\",\"text_setup\",\"debug_module\",\"fps_and_memory_format\",\"text_draw\"]":
                    "[\"state_capture\",\"navigation\",\"party\",\"test_control\",\"overlay_and_content_publish\"]")
                <<",\"total\":";Stats(o,s.total);o<<",\"current_window\":";Bucket(o,s.current);
            o<<",\"completed_windows\":[";
            for(std::size_t i=0;i<s.windowCount;++i){if(i)o<<',';Bucket(o,s.windows[i]);}
            o<<"],\"slow_events\":[";
            for(std::size_t i=0;i<s.eventCount;++i)
            {
                if(i)o<<',';const auto& e=s.events[i];const auto& v=e.sample;
                o<<"{\"serial\":"<<e.serial<<",\"tick_ms\":"<<v.tickMs<<",\"scene\":"<<v.scene
                    <<",\"hook_us\":"<<v.endUs-v.beginUs<<",\"foreground_interval_us\":";
                if(e.intervalKnown)o<<e.gapUs;else o<<"null";
                o<<",\"interval_known\":"<<e.intervalKnown<<",\"foreground_known\":"<<v.foregroundKnown
                    <<",\"foreground_begin\":"<<v.foregroundBegin<<",\"foreground_end\":"<<v.foregroundEnd<<",\"parts_us\":[";
                for(std::size_t j=0;j<Parts;++j){if(j)o<<',';o<<v.parts[j];}o<<"]}";
            }
            o<<"],\"scope\":\""<<(pipeline?
                "IJL performance-overlay call through text draw, including DebugRender and its bookkeeping. Native work outside this call, producer and recorder overhead, GPU Present and continuous foreground are not measured.":
                "Core state/navigation durations plus DebugRender workload through content publication; intervening renderer setup excluded; profiler bookkeeping/export excluded. Invocation intervals are not GPU Present or continuous foreground proof. Native input/network/render outside this hook are not timed.")<<"\"}";
            const auto json=o.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-frame-%s-%lu.json",Directory,pipeline?L"pipeline":L"profile",Native.pid)<0||swprintf_s(temporary,L"%s.tmp",path)<0)return;
            const auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return;
            DWORD written=0;const bool good=WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)&&written==json.size();CloseHandle(file);
            if(good&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING))exported=s.serial;else DeleteFileW(temporary);
        }
        catch(...){/* Diagnostic IO must not escape the shared worker. */}
    }
    }
    void Export() noexcept
    {
        if(!Active())return;
        const auto tick=GetTickCount64();if(LastExport&&tick>=LastExport&&tick-LastExport<200)return;LastExport=tick;
        Snapshot s,p;std::uint64_t skipped=0,pipelineSkipped=0;
        if(!TryAcquireSRWLockShared(&Lock))return;
        const bool have=Have,havePipeline=HavePipeline;
        if(have){s=Published;skipped=PublishedSkipped;}
        if(havePipeline){p=PipelinePublished;pipelineSkipped=PipelinePublishedSkipped;}
        ReleaseSRWLockShared(&Lock);
        if(have)ExportOne(s,skipped,false,ExportedSerial);
        if(havePipeline)ExportOne(p,pipelineSkipped,true,PipelineExported);
    }
    void Shutdown() noexcept{InterlockedExchange(&Enabled,0);}
    bool PipelineEnabled() noexcept{return Active();}
    void RecordPipeline(const Sample& sample) noexcept
    {
        if(!Active())return;
        const auto current=GetCurrentThreadId();
        if(InterlockedCompareExchange(&Thread,current,0)!=0&&Thread!=static_cast<LONG>(current))return;
        PipelineModel.Add(sample);
        if(PipelinePublication&&sample.tickMs>=PipelinePublication&&sample.tickMs-PipelinePublication<200)return;
        if(!TryAcquireSRWLockExclusive(&Lock)){++PipelineSkipped;return;}
        PipelinePublished=PipelineModel.value;PipelinePublishedSkipped=PipelineSkipped;HavePipeline=true;
        ReleaseSRWLockExclusive(&Lock);PipelinePublication=sample.tickMs;
    }
}
