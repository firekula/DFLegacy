#include "../../DFLegacy.Debug/include/debug_api.h"
#pragma comment(linker, "/EXPORT:DebugInitialize=_DebugInitialize@8")
#pragma comment(linker, "/EXPORT:DebugRender=_DebugRender@4")
extern "C" int __stdcall DebugInitialize(const dfl::debug_api::Host* host, const wchar_t*)
{ return host && host->version == dfl::debug_api::Version; }
extern "C" void __stdcall DebugRender(const dfl::debug_api::Frame*) {}
