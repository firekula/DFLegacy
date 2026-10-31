#pragma once
#include <cstdint>

namespace dfl::debug_api
{
    constexpr std::uint32_t Version = 2;
    using DrawText = void (__stdcall*)(int x, int y, std::uint32_t color, const char* text);
    struct Host
    {
        std::uint32_t size;
        std::uint32_t version;
        DrawText drawText;
        void* gameWindow;
        const wchar_t* settingsPath;
    };
    struct Frame
    {
        std::uint32_t size;
        std::uint32_t scene;
        std::uint32_t userId;
        std::uint32_t bossDead;
        std::uint64_t enteredFileTime;
    };
    using Initialize = int (__stdcall*)(const Host*, const wchar_t* directory);
    using Render = void (__stdcall*)(const Frame*);
    using Shutdown = int (__stdcall*)();
    // Optional exports; Host/Frame v2 ABI remains unchanged. Same QPC clock
    // domain as the original callback profiler, fixed five-section payload.
    struct PipelineSample
    {
        std::uint32_t size,version,scene,foregroundKnown,foregroundBegin,foregroundEnd;
        std::uint64_t beginUs,endUs,tickMs,partsUs[5];
    };
    using PipelineEnabled = int (__stdcall*)();
    using RecordPipeline = void (__stdcall*)(const PipelineSample*);
    using ObserveGameplay = void (__stdcall*)(std::uint32_t context,const void*,std::uint32_t length,std::uint32_t provider);
    using ObserveGameplayV2 = void (__stdcall*)(std::uint32_t context,const void*,std::uint32_t length,std::uint32_t provider,std::uint32_t frame);
    struct MovementGate
    {
        std::uint32_t size,version,context,frame,actor,controller,flags;
        std::uint32_t objectKind,objectUid,playerKind,playerUid,roomX,roomY;
    };
    using ObserveMovementGate = void (__stdcall*)(const MovementGate*);
    // Optional, independent schema. Eight native boundaries delimit seven
    // sections; CPU times are user+kernel thread time, not QPC or GPU time.
    struct NativeLoopSample
    {
        std::uint32_t size,version,thread,flags;
        std::uint32_t sceneBegin,sceneEnd,foregroundKnown,foregroundBegin,foregroundEnd,pointMask,cpuMask,reserved;
        std::uint64_t tickMs,endTickMs,sequence,wallUs[8],cpuUs[8];
    };
    static_assert(sizeof(NativeLoopSample)==200,"Native loop observer ABI");
    using RecordNativeLoop = void (__stdcall*)(const NativeLoopSample*);
}
