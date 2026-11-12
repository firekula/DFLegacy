#pragma once
#include <windows.h>
#include <clocale>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "ChapterTwoFrameStages.h"
#include "ChapterTwoPerformanceHistory.h"

namespace dfl::chapter_two::performance
{
    class CsvLog
    {
    public:
        explicit CsvLog(const wchar_t* variable, bool slowFrames = false) noexcept
        {
            wchar_t text[24]{};
            const auto length = GetEnvironmentVariableW(variable, text, _countof(text));
            if (length == 0 || length >= _countof(text)) return;
            wchar_t* end = nullptr;
            const auto number = std::wcstoull(text, &end, 10);
            if (*end || number == 0 || number > UINTPTR_MAX) return;
            const auto file = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(number));
            if (GetFileType(file) != FILE_TYPE_DISK) return;
            file_ = file;
            SetHandleInformation(file_, HANDLE_FLAG_INHERIT, 0);
            static constexpr char Header[] =
                "utc_start,utc_end,elapsed_ms,frames,avg_fps,avg_ms,min_ms,max_ms,p95_upper_ms,p99_upper_ms,"
                "frames_ge_33_3ms,frames_ge_50ms,frames_ge_90ms,skipped_frames,memory_samples,"
                "private_first_mib,private_last_mib,private_min_mib,private_avg_mib,private_peak_mib,"
                "working_set_first_mib,working_set_last_mib,working_set_min_mib,working_set_avg_mib,working_set_peak_mib\r\n";
            static constexpr char SlowHeader[] =
                "utc_start,utc_end,frame_ms,trace_complete,resource_ms,effect_ms,present_ms,unmeasured_ms,"
                "animation_lookup_ms,animation_load_ms,image_load_ms,script_load_ms,projectile_effect_ms,hit_effect_ms,"
                "animation_lookup_calls,animation_load_calls,image_load_calls,script_load_calls,projectile_effect_calls,"
                "hit_effect_calls,present_calls,message_queue_ms,key_translate_ms,key_dispatch_ms,text_dispatch_ms,"
                "keyboard_poll_ms,keyboard_merge_ms,shop_ui_ms,end_scene_ms,letter_downs,arrow_downs,text_dispatch_calls,shop_ui_calls,"
                "shop_page,shop_draw_ms,shop_preview_ms,shop_icon_ms,texture_queue_work_ms,texture_queue_wait_ms,"
                "shop_draw_calls,shop_preview_calls,shop_icon_calls,texture_queue_calls,texture_queue_wait_calls,"
                "shop_notice_ms,shop_notice_skip_ms,shop_notice_draws,shop_notice_skips,window_dispatch_ms,main_loop_sleep_ms,"
                "avatar_tooltip_background_ms,avatar_tooltip_fill_ms,avatar_tooltip_background_calls,avatar_tooltip_fill_calls\r\n";
            const char* header = slowFrames ? SlowHeader : Header;
            Write(header, static_cast<DWORD>(std::strlen(header)));
        }

        ~CsvLog() { if (file_) CloseHandle(file_); }
        CsvLog(const CsvLog&) = delete;
        CsvLog& operator=(const CsvLog&) = delete;

        void Append(const SYSTEMTIME& start, const SYSTEMTIME& end, ULONGLONG elapsedMs,
            const PeriodStatistics& stats) noexcept
        {
            if (!file_) return;
            char from[26]{}, to[26]{};
            FormatTime(from, start);
            FormatTime(to, end);
            const auto& frame = stats.frames.duration;
            const double averageMs = frame.Average();
            const auto& priv = stats.privateMiB;
            const auto& working = stats.workingSetMiB;
            char row[768]{};
            const auto length = std::snprintf(row, sizeof(row),
                "%s,%s,%llu,%llu,%.2f,%.3f,%.3f,%.3f,%.3f,%.3f,%llu,%llu,%llu,%llu,%llu,"
                "%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f\r\n",
                from, to, elapsedMs, frame.count, averageMs > 0 ? 1000.0 / averageMs : 0,
                averageMs, frame.minimum, frame.maximum, stats.frames.PercentileUpper(95),
                stats.frames.PercentileUpper(99), stats.frames.slow33, stats.frames.slow50,
                stats.frames.slow90, stats.droppedFrames, priv.count, priv.first, priv.last,
                priv.minimum, priv.Average(), priv.maximum, working.first, working.last,
                working.minimum, working.Average(), working.maximum);
            if (length > 0 && length < static_cast<int>(sizeof(row))) Write(row, static_cast<DWORD>(length));
        }

        void AppendSlowFrame(const SlowFrame& sample, std::int64_t frequency) noexcept
        {
            if (!file_ || frequency <= 0) return;
            const auto toMs = [frequency](std::int64_t ticks) { return static_cast<double>(ticks) * 1000 / static_cast<double>(frequency); };
            const double frame = toMs(sample.elapsedTicks);
            const auto duration = static_cast<std::uint64_t>(frame * 10000 + 0.5);
            if (sample.endFileTime < duration) return;
            SYSTEMTIME start{}, end{};
            if (!DecodeTime(sample.endFileTime - duration, start) || !DecodeTime(sample.endFileTime, end)) return;
            char from[26]{}, to[26]{};
            FormatTime(from, start);
            FormatTime(to, end);
            const auto stage = [&](FrameStage part) { return toMs(sample.stages.ticks[StageIndex(part)]); };
            const double lookup = stage(FrameStage::AnimationLookup), animation = stage(FrameStage::AnimationLoad);
            const double image = stage(FrameStage::ImageLoad), script = stage(FrameStage::ScriptLoad);
            const double projectile = stage(FrameStage::ProjectileEffect), hit = stage(FrameStage::HitEffect);
            const double present = stage(FrameStage::Present);
            const double keyDispatch = stage(FrameStage::KeyDispatch) + stage(FrameStage::LetterDown) + stage(FrameStage::ArrowDown);
            const auto measured = sample.stages.MeasuredTicks();
            const auto complete = sample.stages.complete && measured <= sample.elapsedTicks;
            const auto& calls = sample.stages.calls;
            char row[768]{};
            const auto length = std::snprintf(row, sizeof(row),
                "%s,%s,%.3f,%u,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%u,%u,%u,%u,%u,%u,%u,"
                "%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%u,%u,%u,%u,"
                "%s,%.3f,%.3f,%.3f,%.3f,%.3f,%u,%u,%u,%u,%u,%.3f,%.3f,%u,%u,%.3f,%.3f,%.3f,%.3f,%u,%u\r\n",
                from, to, frame, complete ? 1u : 0u, lookup + animation + image + script, projectile + hit,
                present, toMs((std::max)(sample.elapsedTicks - measured, std::int64_t{0})),
                lookup, animation, image, script, projectile, hit,
                calls[StageIndex(FrameStage::AnimationLookup)], calls[StageIndex(FrameStage::AnimationLoad)],
                calls[StageIndex(FrameStage::ImageLoad)], calls[StageIndex(FrameStage::ScriptLoad)],
                calls[StageIndex(FrameStage::ProjectileEffect)], calls[StageIndex(FrameStage::HitEffect)],
                calls[StageIndex(FrameStage::Present)], stage(FrameStage::MessageQueue), stage(FrameStage::KeyTranslate),
                keyDispatch, stage(FrameStage::TextDispatch), stage(FrameStage::KeyboardPoll), stage(FrameStage::KeyboardMerge),
                stage(FrameStage::ShopUi), stage(FrameStage::EndScene), calls[StageIndex(FrameStage::LetterDown)],
                calls[StageIndex(FrameStage::ArrowDown)], calls[StageIndex(FrameStage::TextDispatch)], calls[StageIndex(FrameStage::ShopUi)],
                ShopPageName(sample.stages.shopPage), stage(FrameStage::ShopDraw), stage(FrameStage::ShopPreview),
                stage(FrameStage::ShopIcon), stage(FrameStage::TextureQueue), stage(FrameStage::TextureQueueWait),
                calls[StageIndex(FrameStage::ShopDraw)], calls[StageIndex(FrameStage::ShopPreview)],
                calls[StageIndex(FrameStage::ShopIcon)], calls[StageIndex(FrameStage::TextureQueue)],
                calls[StageIndex(FrameStage::TextureQueueWait)],
                stage(FrameStage::ShopNotice), stage(FrameStage::ShopNoticeSkipped), calls[StageIndex(FrameStage::ShopNotice)],
                calls[StageIndex(FrameStage::ShopNoticeSkipped)], stage(FrameStage::WindowDispatch), stage(FrameStage::MainLoopSleep),
                stage(FrameStage::AvatarTooltipBackground), stage(FrameStage::AvatarTooltipFill),
                calls[StageIndex(FrameStage::AvatarTooltipBackground)], calls[StageIndex(FrameStage::AvatarTooltipFill)]);
            if (length > 0 && length < static_cast<int>(sizeof(row))) Write(row, static_cast<DWORD>(length));
        }

    private:
        static bool DecodeTime(std::uint64_t timestamp, SYSTEMTIME& time) noexcept
        {
            const FILETIME value{ static_cast<DWORD>(timestamp), static_cast<DWORD>(timestamp >> 32) };
            return FileTimeToSystemTime(&value, &time) != FALSE;
        }
        static void FormatTime(char (&text)[26], const SYSTEMTIME& time) noexcept
        {
            std::snprintf(text, sizeof(text), "%04hu-%02hu-%02huT%02hu:%02hu:%02hu.%03huZ",
                time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond, time.wMilliseconds);
        }

        void Write(const char* text, DWORD bytes) noexcept
        {
            DWORD written = 0;
            if (!WriteFile(file_, text, bytes, &written, nullptr) || written != bytes)
            {
                CloseHandle(file_);
                file_ = nullptr;
                OutputDebugStringA("DFLegacy Chapter Two: performance CSV write failed.\n");
            }
        }

        HANDLE file_ = nullptr;
    };
}
