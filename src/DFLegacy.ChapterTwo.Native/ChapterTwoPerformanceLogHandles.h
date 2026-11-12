#pragma once
#include <windows.h>
#include <cstdio>
#include <string>

// The broker owns the filenames. The confined game receives only
// append-only file handles in the explicit child handle list.
class ChapterTwoPerformanceLogHandles
{
public:
    ChapterTwoPerformanceLogHandles() = default;
    ChapterTwoPerformanceLogHandles(const ChapterTwoPerformanceLogHandles&) = delete;
    ChapterTwoPerformanceLogHandles& operator=(const ChapterTwoPerformanceLogHandles&) = delete;
    ~ChapterTwoPerformanceLogHandles()
    {
        for (std::size_t i = 0; i < count_; ++i) CloseHandle(files_[i]);
    }

    std::size_t Open(HANDLE* inherited)
    {
        SetEnvironmentVariableW(L"DFL_S1C2_PERF_INTERVAL_HANDLE", nullptr);
        SetEnvironmentVariableW(L"DFL_S1C2_PERF_TOTAL_HANDLE", nullptr);
        SetEnvironmentVariableW(L"DFL_S1C2_PERF_SLOW_HANDLE", nullptr);
        wchar_t prefix[32768]{};
        const auto length = GetEnvironmentVariableW(L"DFL_S1C2_PERF_LOG_PREFIX", prefix, _countof(prefix));
        if (length && length < _countof(prefix))
        {
            OpenFile(std::wstring(prefix) + L"-interval.csv", L"DFL_S1C2_PERF_INTERVAL_HANDLE");
            OpenFile(std::wstring(prefix) + L"-total.csv", L"DFL_S1C2_PERF_TOTAL_HANDLE");
            OpenFile(std::wstring(prefix) + L"-slowframes.csv", L"DFL_S1C2_PERF_SLOW_HANDLE");
        }
        SetEnvironmentVariableW(L"DFL_S1C2_PERF_LOG_PREFIX", nullptr);
        for (std::size_t i = 0; i < count_; ++i) inherited[i] = files_[i];
        return count_;
    }

private:
    void OpenFile(const std::wstring& path, const wchar_t* variable)
    {
        SECURITY_ATTRIBUTES attributes{ sizeof(attributes), nullptr, TRUE };
        const auto file = CreateFileW(path.c_str(), FILE_APPEND_DATA | SYNCHRONIZE,
            FILE_SHARE_READ, &attributes, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            std::printf("PERFORMANCE_LOG_OPEN_FAILED error=%lu\n", GetLastError());
            return;
        }
        const auto value = std::to_wstring(reinterpret_cast<UINT_PTR>(file));
        if (!SetEnvironmentVariableW(variable, value.c_str()))
        {
            CloseHandle(file);
            return;
        }
        files_[count_++] = file;
    }

    HANDLE files_[3]{};
    std::size_t count_ = 0;
};
