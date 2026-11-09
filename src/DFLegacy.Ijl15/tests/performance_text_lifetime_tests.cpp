// Run the production overlay against the ACT1 renderer's real BeginText/EndText
// instructions. DNF.exe SHA256: 6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5.
#include "../src/performance_overlay.cpp"
#include <stdexcept>

namespace {
    dfl::ijl15_config::Settings Settings{};
    unsigned Renders{}, Draws{};
    void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
    struct Renderer { void* table; unsigned char pad[0x80 - sizeof(void*)]; };
    static_assert(sizeof(Renderer) == 0x80);
    unsigned char& Active(Renderer& value) { return reinterpret_cast<unsigned char*>(&value)[0x2D]; }
    void*& State(Renderer& value) { return *reinterpret_cast<void**>(reinterpret_cast<unsigned char*>(&value) + 0x40); }
    int __fastcall Draw(void* value, void*, int, int, int, char*) {
        auto& renderer = *static_cast<Renderer*>(value);
        Check(Active(renderer) && State(renderer), "draw has a live text batch");
        ++Draws; return 17;
    }
    struct Region {
        void* memory;
        explicit Region(std::uintptr_t address) : memory(VirtualAlloc(reinterpret_cast<void*>(address), 0x10000,
            MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE)) {
            if (!memory || (address && memory != reinterpret_cast<void*>(address)))
                std::fprintf(stderr, "fixture allocation %08X returned %p error=%lu\n", static_cast<unsigned>(address), memory, GetLastError());
            Check(memory && (!address || memory == reinterpret_cast<void*>(address)), "fixed fixture address unavailable");
        }
        ~Region() { if (memory) VirtualFree(memory, 0, MEM_RELEASE); }
    };
    void Run() {
        Region globals(0xBF0000), fonts(0xB50000), native(0);
        // Original 0x8D5210 stores the caller's pointer only if no batch is active.
        const unsigned char begin[]{0x55,0x8B,0xEC,0x8A,0x41,0x2D,0x84,0xC0,0x75,0x0A,
            0x8B,0x45,0x08,0x89,0x41,0x40,0xC6,0x41,0x2D,0x01,0x5D,0xC2,0x04,0x00};
        const unsigned char end[]{0x8A,0x51,0x2D,0x33,0xC0,0x3A,0xD0,0x74,0x06,
            0x89,0x41,0x40,0x88,0x41,0x2D,0xC3};
        auto* code = static_cast<unsigned char*>(native.memory);
        std::memcpy(code, begin, sizeof(begin));
        std::memcpy(code + 32, end, sizeof(end));
        FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
        void* table[24]{};
        table[0x4C / 4] = code;
        table[0x50 / 4] = code + 32;
        table[0x54 / 4] = reinterpret_cast<void*>(&Draw);
        Renderer renderer{}; renderer.table = table;
        *reinterpret_cast<void**>(0xBF1648) = &renderer;
        *reinterpret_cast<std::uint32_t*>(0xB5B384) = 0x12345678;

        // The normal player configuration hides FPS but still renders plugins.
        Settings.EnablePerformanceOverlay = false; Renders = Draws = 0;
        DrawFpsAndMemory(nullptr, nullptr);
        Check(Renders == 1 && Draws == 0, "plugin drawing retained while FPS is hidden");
        Check(!Active(renderer) && !State(renderer), "overlay returned with dangling stack text state");
        TextState next{}; next.mode = 1; next.font = 0x87654321;
        reinterpret_cast<BeginTextFunction>(table[0x4C / 4])(&renderer, &next);
        Check(State(renderer) == &next, "subsequent native message selects its own text state");
        reinterpret_cast<void(__thiscall*)(void*)>(table[0x50 / 4])(&renderer);
        // Different dungeon overlays and room prompts share this same callback.
        // Exercise repeated frames and immediately-following native messages.
        for (int frame = 0; frame < 256; ++frame) {
            Renders = Draws = 0;
            const auto result = DrawFpsAndMemory(nullptr, nullptr);
            Check(Renders == 1 && Draws == 0, "hidden overlay retains plugin drawing behavior");
            Check(result == 0, "normal overlay return preserved");
            Check(!Active(renderer) && !State(renderer), "repeated overlay left expired text state");
            reinterpret_cast<BeginTextFunction>(table[0x4C / 4])(&renderer, &next);
            Check(State(renderer) == &next, "next room message uses its own state after each overlay frame");
            reinterpret_cast<void(__thiscall*)(void*)>(table[0x50 / 4])(&renderer);
        }
        TextState borrowed{}; borrowed.mode = 1;
        reinterpret_cast<BeginTextFunction>(table[0x4C / 4])(&renderer, &borrowed);
        DrawFpsAndMemory(nullptr, nullptr);
        Check(Active(renderer) && State(renderer) == &borrowed, "preexisting caller batch preserved");
        reinterpret_cast<void(__thiscall*)(void*)>(table[0x50 / 4])(&renderer);
        table[0x50 / 4] = nullptr; Renders = 0;
        DrawFpsAndMemory(nullptr, nullptr);
        Check(!Active(renderer) && !State(renderer) && Renders == 0, "missing cleanup cannot leave a batch active");
    }
}
namespace dfl::ijl15_config { const Settings& Get() noexcept { return ::Settings; } }
namespace dfl::auto_input { bool IsEnabled() noexcept { return false; } void Pump() noexcept {} }
namespace dfl::player_runtime { void OnFrame() noexcept {} }
namespace dfl::debug_module {
    Pipeline::Pipeline() noexcept = default;
    Pipeline::~Pipeline() noexcept = default;
    void Pipeline::Mark() noexcept {}
    void Render() noexcept { ++Renders; }
}
int main() {
    try { Run(); std::puts("performance_text_lifetime PASS (hidden overlay, 256 frames, native follow-up, borrowed, missing cleanup)"); return 0; }
    catch (const std::exception& error) { std::fprintf(stderr, "FAIL: %s\n", error.what()); return 1; }
}
