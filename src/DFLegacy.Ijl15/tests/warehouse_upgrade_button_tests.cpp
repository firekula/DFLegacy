// Owned synthetic image only. Original bytes are independently transcribed
// from SHA256 6b4850be...47de0d5; never open or modify a game process.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "warehouse_upgrade_button.h"
#include "memory_patch.h"

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace
{
    constexpr std::size_t ImageSize = 0x682000;
    constexpr std::uintptr_t ImmediateOffset = 0x424C4E;
    constexpr unsigned char RenderEntry[]{
        0x55,0x8B,0xEC,0x81,0xEC,0xB4,0x00,0x00,0x00,0x8B,0x45,0x08,0x53,0x8B,0xD9};
    constexpr unsigned char RenderSlot[]{0xD0,0x41,0x82,0x00};
    constexpr unsigned char GradeBranch[]{
        0x83,0xFE,0x07,0x7C,0x0F,0x8B,0x4B,0x38,0xC6,0x81,0x20,0x01,0x00,0x00,0x00,
        0xE9,0xB4,0x00,0x00,0x00,0x8B,0x53,0x38,0xC6,0x82,0x20,0x01,0x00,0x00,0x01};
    constexpr unsigned char ButtonStore[]{
        0x8B,0x4B,0x38,0xC6,0x81,0x21,0x01,0x00,0x00,0x00};
    constexpr unsigned char EventGate[]{
        0x8B,0x46,0x10,0x8B,0x88,0x24,0x0F,0x00,0x00,0x39,0x59,0x10,0x75,0x5A,
        0x8B,0x46,0x38,0x8A,0x88,0x21,0x01,0x00,0x00,0x05,0x10,0x01,0x00,0x00,
        0x84,0xC9,0x74,0x48,0x8B,0x15,0x84,0x51,0xB5,0x00,0x3B,0x50,0x0C,0x75,0x3D};
    constexpr unsigned char BusyGate[]{
        0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8A,0x86,0xD0,0x25,0x00,0x00,0x84,0xC0,
        0x0F,0x85,0x1E,0x02,0x00,0x00};
    struct Fragment { std::uintptr_t Offset; const unsigned char* Bytes; std::size_t Size; };
    constexpr Fragment Fragments[]{
        {0x4241D0, RenderEntry, sizeof(RenderEntry)},
        {0x681E74, RenderSlot, sizeof(RenderSlot)},
        {0x424B87, GradeBranch, sizeof(GradeBranch)},
        {0x424C45, ButtonStore, sizeof(ButtonStore)},
        {0x423D36, EventGate, sizeof(EventGate)},
        {0x464B60, BusyGate, sizeof(BusyGate)}};
    std::uintptr_t Image = 0;
    unsigned Writes = 0;
    int Failures = 0;
    enum class Failure { None, BeforeWrite, AfterWrite };
    Failure WriteFailure = Failure::None;

    void Check(bool condition, const char* message)
    {
        if (!condition) { ++Failures; std::fprintf(stderr, "FAIL warehouse Upgrade: %s\n", message); }
    }

    void Reset()
    {
        std::memset(reinterpret_cast<void*>(Image), 0xCC, ImageSize);
        for (const auto& f : Fragments)
            std::memcpy(reinterpret_cast<void*>(Image + f.Offset), f.Bytes, f.Size);
        // Join only the original grade branch and store fragments. The omitted
        // positioning/protected-grade code has no bearing on the enabled store.
        auto* join = reinterpret_cast<unsigned char*>(Image + 0x424BA5);
        join[0] = 0xE9;
        const auto displacement = static_cast<std::int32_t>(0x424C45 - 0x424BA5 - 5);
        std::memcpy(join + 1, &displacement, sizeof(displacement));
        *reinterpret_cast<unsigned char*>(Image + 0x424C4F) = 0xC3;
        Writes = 0;
        WriteFailure = Failure::None;
    }

    bool Write(std::uintptr_t address, const void* bytes, std::size_t size)
    {
        ++Writes;
        Check(address == Image + ImmediateOffset && size == 1,
            "writer must touch only the enabled immediate");
        if (WriteFailure == Failure::BeforeWrite && Writes == 1) return false;
        const bool result = dfl::memory_patch::WriteBytes(address, bytes, size);
        return result && !(WriteFailure == Failure::AfterWrite && Writes == 1);
    }

    void RunGrade(unsigned grade, unsigned char shown, unsigned char enabled)
    {
        alignas(4) unsigned char storage[0x3C]{};
        alignas(4) unsigned char buttons[0x198]{};
        void* buttonPointer = buttons;
        std::memcpy(storage + 0x38, &buttonPointer, sizeof(buttonPointer));
        buttons[0x120] = 1;
        buttons[0x121] = 0;
        void* storagePointer = storage;
        void* entry = reinterpret_cast<void*>(Image + 0x424B87);
        __asm
        {
            push ebx
            push esi
            mov ebx, storagePointer
            mov esi, grade
            call entry
            pop esi
            pop ebx
        }
        Check(buttons[0x120] == shown, "original max-grade visibility branch must remain effective");
        Check(buttons[0x121] == enabled, "native enabled store must restore input below max grade");
    }
}

int main(int argc, char** argv)
{
    Image = reinterpret_cast<std::uintptr_t>(VirtualAlloc(nullptr, ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    if (!Image) return 2;
    Reset();
    if (argc == 2 && std::strcmp(argv[1], "--original-control") == 0)
    {
        RunGrade(0, 1, 0);
        RunGrade(6, 1, 0);
        RunGrade(7, 0, 0);
    }
    else
    {
        const std::vector<unsigned char> before(reinterpret_cast<unsigned char*>(Image),
            reinterpret_cast<unsigned char*>(Image) + ImageSize);
        const bool applied = dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write);
        Check(applied, "supported original client must accept the Upgrade patch");
        if (applied)
        {
            Check(Writes == 1, "initial install writes once");
            for (std::size_t i = 0; i < before.size(); ++i)
                if (before[i] != *reinterpret_cast<unsigned char*>(Image + i))
                    Check(i == ImmediateOffset && *reinterpret_cast<unsigned char*>(Image + i) == 1,
                        "entire image must differ only at the one-byte immediate");
            RunGrade(0, 1, 1);
            RunGrade(6, 1, 1);
            RunGrade(7, 0, 0);
            RunGrade(8, 0, 0);
            Check(dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write) && Writes == 1,
                "repeated install must succeed without another write");
        }

        // Corrupt every byte of every independent native signature. Both a
        // pristine image and an already-enabled image must reject the mismatch.
        for (const auto& f : Fragments)
        {
            for (std::size_t i = 0; i < f.Size; ++i)
            {
                if (f.Offset + i == ImmediateOffset) continue;
                for (unsigned alreadyInstalled = 0; alreadyInstalled < 2; ++alreadyInstalled)
                {
                    Reset();
                    *reinterpret_cast<unsigned char*>(Image + ImmediateOffset) =
                        static_cast<unsigned char>(alreadyInstalled);
                    auto* byte = reinterpret_cast<unsigned char*>(Image + f.Offset + i);
                    *byte ^= 0x80;
                    Check(!dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write) && Writes == 0,
                        "signature mismatch must reject without writing");
                }
            }
        }
        Reset();
        *reinterpret_cast<unsigned char*>(Image + ImmediateOffset) = 2;
        Check(!dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write) && Writes == 0,
            "unknown immediate must reject without writing");
        for (Failure failure : {Failure::BeforeWrite, Failure::AfterWrite})
        {
            Reset(); WriteFailure = failure;
            Check(!dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write),
                "write failure must be reported");
            Check(*reinterpret_cast<unsigned char*>(Image + ImmediateOffset) == 0,
                "failed write must restore the original disabled policy");
            WriteFailure = Failure::None;
            Check(dfl::warehouse_upgrade_button::detail::InstallImage(Image, Write),
                "safe retry after rollback must succeed");
        }
        Check(!dfl::warehouse_upgrade_button::detail::InstallImage(0, Write), "reject null image");
        Check(!dfl::warehouse_upgrade_button::detail::InstallImage(Image, nullptr), "reject null writer");
        Check(!dfl::warehouse_upgrade_button::detail::InstallImage(UINT32_MAX, Write), "reject overflow");
        Check(!dfl::warehouse_upgrade_button::detail::InstallImage(0x10000, Write), "reject unreadable image");
    }
    VirtualFree(reinterpret_cast<void*>(Image), 0, MEM_RELEASE);
    if (!Failures) std::printf("PASS warehouse Upgrade native policy\n");
    return Failures ? 1 : 0;
}
