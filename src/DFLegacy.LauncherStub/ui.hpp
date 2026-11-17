#pragma once
#include <windows.h>
#include <cstddef>
#include <string>

namespace dfo {
bool Confirm(HINSTANCE instance, const wchar_t* message);
void Error(HINSTANCE instance, const wchar_t* message);

class ProgressWindow {
public:
    explicit ProgressWindow(HINSTANCE instance);
    ~ProgressWindow();
    ProgressWindow(const ProgressWindow&) = delete;
    ProgressWindow& operator=(const ProgressWindow&) = delete;
    void Update(std::size_t done, std::size_t total, const std::wstring& name);
    void Close();

private:
    struct Impl;
    Impl* impl_;
};
}
