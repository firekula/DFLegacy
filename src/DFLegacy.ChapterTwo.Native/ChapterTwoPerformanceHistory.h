#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two::performance
{
    inline constexpr std::size_t FrameCapacity = 600;
    inline constexpr float SlowFrameMs = 1000.0f / 30.0f;

    template<class T, std::size_t Capacity>
    class History
    {
    public:
        void Add(T value) noexcept
        {
            samples_[next_] = value;
            next_ = (next_ + 1) % Capacity;
            size_ = (std::min)(size_ + 1, Capacity);
        }

        void Clear() noexcept { next_ = size_ = 0; }
        std::size_t Size() const noexcept { return size_; }
        const T& operator[](std::size_t index) const noexcept
        {
            return samples_[(next_ + Capacity - size_ + index) % Capacity];
        }

    private:
        std::array<T, Capacity> samples_{};
        std::size_t next_ = 0, size_ = 0;
    };

    struct FrameStatistics
    {
        double average = 0, fps = 0;
        float minimum = 0, maximum = 0, p95 = 0, p99 = 0;
        std::size_t slow = 0;
    };

    inline FrameStatistics Summarize(const History<float, FrameCapacity>& history) noexcept
    {
        const auto count = history.Size();
        if (count == 0) return {};
        std::array<float, FrameCapacity> sorted{};
        FrameStatistics stats;
        for (std::size_t i = 0; i < count; ++i)
        {
            const float value = history[i];
            sorted[i] = value;
            stats.average += value;
            stats.slow += value >= SlowFrameMs;
        }
        stats.average /= static_cast<double>(count);
        if (stats.average > 0) stats.fps = 1000 / stats.average;
        std::sort(sorted.begin(), sorted.begin() + count);
        stats.minimum = sorted.front();
        stats.maximum = sorted[count - 1];
        stats.p95 = sorted[(count * 95 + 99) / 100 - 1];
        stats.p99 = sorted[(count * 99 + 99) / 100 - 1];
        return stats;
    }

    struct Measurements
    {
        std::uint64_t count = 0;
        double sum = 0, first = 0, last = 0, minimum = 0, maximum = 0;

        void Add(double value) noexcept
        {
            if (count == 0) first = minimum = maximum = value;
            ++count;
            sum += value;
            last = value;
            minimum = (std::min)(minimum, value);
            maximum = (std::max)(maximum, value);
        }

        void Merge(const Measurements& other) noexcept
        {
            if (other.count == 0) return;
            if (count == 0) { *this = other; return; }
            count += other.count;
            sum += other.sum;
            last = other.last;
            minimum = (std::min)(minimum, other.minimum);
            maximum = (std::max)(maximum, other.maximum);
        }

        double Average() const noexcept { return count ? sum / static_cast<double>(count) : 0; }
    };

    struct FrameMeasurements
    {
        static constexpr std::size_t LastBucket = 500;
        Measurements duration;
        // Each bucket covers one millisecond. The last bucket holds longer gaps.
        std::array<std::uint64_t, LastBucket + 1> histogram{};
        std::uint64_t slow33 = 0, slow50 = 0, slow90 = 0;

        void Add(double milliseconds) noexcept
        {
            duration.Add(milliseconds);
            const auto bucket = static_cast<std::size_t>((std::min)(milliseconds, static_cast<double>(LastBucket)));
            ++histogram[bucket];
            slow33 += milliseconds >= SlowFrameMs;
            slow50 += milliseconds >= 50;
            slow90 += milliseconds >= 90;
        }

        void Merge(const FrameMeasurements& other) noexcept
        {
            duration.Merge(other.duration);
            for (std::size_t i = 0; i < histogram.size(); ++i) histogram[i] += other.histogram[i];
            slow33 += other.slow33;
            slow50 += other.slow50;
            slow90 += other.slow90;
        }

        double PercentileUpper(unsigned percent) const noexcept
        {
            if (duration.count == 0) return 0;
            const auto target = (duration.count * percent + 99) / 100;
            std::uint64_t seen = 0;
            for (std::size_t i = 0; i < LastBucket; ++i)
            {
                seen += histogram[i];
                if (seen >= target) return static_cast<double>(i + 1);
            }
            return duration.maximum;
        }
    };

    struct PeriodStatistics
    {
        FrameMeasurements frames;
        Measurements privateMiB, workingSetMiB;
        std::uint64_t droppedFrames = 0;

        void Merge(const PeriodStatistics& other) noexcept
        {
            frames.Merge(other.frames);
            privateMiB.Merge(other.privateMiB);
            workingSetMiB.Merge(other.workingSetMiB);
            droppedFrames += other.droppedFrames;
        }
    };
}
