#pragma once
#include <windows.h>

namespace dflegacy::startup {
void ShowStartupPrompt(HINSTANCE instance, const wchar_t* message) noexcept;
}
