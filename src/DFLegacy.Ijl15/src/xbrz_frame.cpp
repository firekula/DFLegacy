#include "xbrz_frame.h"
#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <thread>
#include <utility>

namespace dfl::presentation
{
    struct XbrzFrame::Implementation
    {
        const unsigned Width, Height, RequestedWorkers;
        const ScaleRoutine Scale;
        std::vector<std::uint32_t> Current, Previous, Pixels;
        std::vector<std::pair<int, int>> Ranges, Jobs;
        std::vector<std::thread> Threads;
        std::mutex Mutex;
        std::condition_variable Ready, Done;
        std::atomic<std::size_t> Next{0};
        std::atomic<bool> Failed{false};
        std::size_t Generation = 0;
        unsigned Pending = 0, Workers = 1, Factor = 0;
        xbrz::ColorFormat Format = xbrz::ColorFormat::argbUnbuffered;
        bool Stopping = false, PoolReady = false, Valid = false, Prepared = false;

        Implementation(unsigned width, unsigned height, unsigned workers, ScaleRoutine scale)
            : Width(width), Height(height), RequestedWorkers(workers), Scale(scale)
        {
        }
        ~Implementation()
        {
            Stop();
        }

        void Stop() noexcept
        {
            {
                std::lock_guard<std::mutex> lock(Mutex);
                Stopping = true;
            }
            Ready.notify_all();
            for (auto &thread : Threads)
                if (thread.joinable())
                    thread.join();
            Threads.clear();
        }

        void RunJobs() noexcept
        {
            for (auto index = Next.fetch_add(1); index < Jobs.size(); index = Next.fetch_add(1))
            {
                const auto range = Jobs[index];
                try
                {
                    Scale(Factor, Current.data(), Pixels.data(), int(Width), int(Height), Format, xbrz::ScalerCfg{},
                          range.first, range.second);
                }
                catch (...)
                {
                    Failed.store(true);
                }
            }
        }

        void Worker() noexcept
        {
            std::size_t observed = 0;
            for (;;)
            {
                std::unique_lock<std::mutex> lock(Mutex);
                Ready.wait(lock, [&] { return Stopping || Generation != observed; });
                if (Stopping)
                    return;
                observed = Generation;
                lock.unlock();
                RunJobs();
                lock.lock();
                if (--Pending == 0)
                    Done.notify_one();
            }
        }

        void EnsurePool()
        {
            if (PoolReady)
                return;
            const auto hardware = std::thread::hardware_concurrency();
            Workers = std::clamp(RequestedWorkers ? RequestedWorkers : (hardware > 1 ? hardware - 1 : 1), 1u, 8u);
            try
            {
                Threads.reserve(Workers - 1);
                for (unsigned worker = 1; worker < Workers; ++worker)
                    Threads.emplace_back([this] { Worker(); });
            }
            catch (...)
            {
                Stop();
                Stopping = false;
                Workers = 1;
            }
            PoolReady = true;
        }

        bool Prepare(const void *pixels, std::size_t pitch, unsigned factor, xbrz::ColorFormat format, XbrzWork &work)
        {
            work = {};
            if (Prepared)
                Valid = false;
            Prepared = false;
            if (!pixels || !Scale || !Width || !Height || Width > 4096 || Height > 4096 ||
                pitch < std::size_t(Width) * 4 || (factor != 2 && factor != 3) ||
                (format != xbrz::ColorFormat::rgb && format != xbrz::ColorFormat::argbUnbuffered))
                return false;
            const auto count = std::size_t(Width) * Height;
            if (Factor != factor || Format != format || Pixels.size() != count * factor * factor)
            {
                Current.resize(count);
                Previous.resize(count);
                Pixels.resize(count * factor * factor);
                Ranges.reserve(Height);
                Jobs.reserve(Height);
                Factor = factor;
                Format = format;
                Valid = false;
            }
            Ranges.clear();
            Jobs.clear();
            for (unsigned row = 0; row < Height; ++row)
            {
                auto *target = Current.data() + std::size_t(row) * Width;
                std::memcpy(target, static_cast<const unsigned char *>(pixels) + std::size_t(row) * pitch,
                            std::size_t(Width) * 4);
                for (unsigned column = 0; column < Width; ++column)
                    target[column] |= 0xff000000;
                if (Valid &&
                    std::memcmp(target, Previous.data() + std::size_t(row) * Width, std::size_t(Width) * 4) == 0)
                    continue;
                ++work.ChangedRows;
                const int first = std::max(0, int(row) - 2), last = std::min(int(Height), int(row) + 3);
                if (!Ranges.empty() && first <= Ranges.back().second)
                    Ranges.back().second = std::max(last, Ranges.back().second);
                else
                    Ranges.emplace_back(first, last);
            }
            for (const auto range : Ranges)
                work.ScaledRows += unsigned(range.second - range.first);
            if (work.ScaledRows)
            {
                EnsurePool();
                work.Workers = Workers;
                const auto grain = std::max(16u, (work.ScaledRows + Workers - 1) / Workers);
                for (const auto range : Ranges)
                    for (int first = range.first; first < range.second; first += int(grain))
                        Jobs.emplace_back(first, std::min(range.second, first + int(grain)));
                Next.store(0);
                Failed.store(false);
                {
                    std::lock_guard<std::mutex> lock(Mutex);
                    Pending = unsigned(Threads.size());
                    ++Generation;
                }
                Ready.notify_all();
                RunJobs();
                {
                    std::unique_lock<std::mutex> lock(Mutex);
                    Done.wait(lock, [&] { return Pending == 0; });
                }
                if (Failed.load())
                {
                    Valid = false;
                    return false;
                }
            }
            work.Workers = Workers;
            Prepared = true;
            return true;
        }
    };
    XbrzFrame::XbrzFrame(unsigned width, unsigned height, unsigned workers, ScaleRoutine scale)
        : implementation_(new Implementation(width, height, workers, scale))
    {
    }
    XbrzFrame::~XbrzFrame() = default;
    bool XbrzFrame::Prepare(const void *pixels, std::size_t pitch, unsigned factor, xbrz::ColorFormat format,
                            XbrzWork &work) noexcept
    {
        try
        {
            return implementation_->Prepare(pixels, pitch, factor, format, work);
        }
        catch (...)
        {
            Discard();
            return false;
        }
    }
    void XbrzFrame::Commit() noexcept
    {
        if (!implementation_->Prepared)
            return;
        implementation_->Current.swap(implementation_->Previous);
        implementation_->Valid = true;
        implementation_->Prepared = false;
    }
    void XbrzFrame::Discard() noexcept
    {
        implementation_->Valid = false;
        implementation_->Prepared = false;
    }
    const std::vector<std::uint32_t> &XbrzFrame::Output() const noexcept
    {
        return implementation_->Pixels;
    }
} // namespace dfl::presentation
