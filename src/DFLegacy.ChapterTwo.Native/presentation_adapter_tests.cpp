#include "ChapterTwoPresentation.h"
#include "ChapterTwoPerformance.h"
#include "ChapterTwoNpcDialogue.h"
#include "ChapterTwoWarehouseUpgrade.h"
#include "ChapterTwoPaymentViewport.h"
#include "../DFLegacy.Ijl15/src/pe_import.h"
#include <cstdio>
#include <fstream>
#include <iterator>
#include <vector>

namespace
{
    constexpr unsigned ImageSize = 0xe57000;
    constexpr unsigned OuterHeight = 0xb62d, OuterWidth = 0xb632;
    constexpr unsigned LogicalHeight = 0xb656, LogicalWidth = 0xb65b;

    std::vector<unsigned char> MapImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        if (file.size() < sizeof(IMAGE_NT_HEADERS32)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            static_cast<std::size_t>(dos->e_lfanew) > file.size() - sizeof(IMAGE_NT_HEADERS32)) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.NumberOfSections != 16 ||
            nt->OptionalHeader.SizeOfImage != ImageSize) return {};
        std::vector<unsigned char> image(ImageSize);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        const auto fits = [](std::size_t offset, std::size_t length, std::size_t limit) {
            return offset <= limit && length <= limit - offset;
        };
        if (!fits(reinterpret_cast<const unsigned char*>(sections) - file.data(),
                sizeof(*sections) * nt->FileHeader.NumberOfSections, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, image.size())) return {};
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned index = 0; index < nt->FileHeader.NumberOfSections; ++index)
        {
            const auto& section = sections[index];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, image.size()) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress,
                file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
        return image;
    }

    unsigned Read(const std::vector<unsigned char>& image, unsigned offset)
    {
        unsigned result{};
        std::memcpy(&result, image.data() + offset, sizeof(result));
        return result;
    }

    struct FixtureFont
    {
        void** methods = nullptr;
        unsigned char initialized = 0;
    };
    FixtureFont bodyFixture;
    unsigned fontCreates = 0;
    int measuredFontHeight = 20;
    int definedFontSize = 0;

    int __fastcall CreateFixtureFont(void*, void*, int kind, void** result)
    {
        if (kind != 4) return -1;
        ++fontCreates;
        bodyFixture.initialized = 0;
        *result = &bodyFixture;
        return 0;
    }

    int __fastcall PrepareFixtureFont(void*, void*) { return 1; }
    int __fastcall MeasureFixtureFont(void*, void*) { return measuredFontHeight; }
    int __fastcall DefineFixtureFont(void*, void*, int index, const char* face, int size, float x, float y)
    {
        if (index != 0 || std::strcmp(face, "SimSun") != 0 || x != 1.0f || y != 1.0f) return -1;
        definedFontSize = size;
        return 0;
    }

    bool ExerciseDialogueFont(const std::vector<unsigned char>& image)
    {
        auto* mapped = static_cast<unsigned char*>(VirtualAlloc(nullptr, image.size(),
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        if (!mapped) return false;
        std::memcpy(mapped, image.data(), image.size());
        bool passed = false;
        if (dfl::chapter_two::InstallNpcDialogueLayout(mapped, image.size()))
        {
            void* fontMethods[14]{};
            fontMethods[2] = reinterpret_cast<void*>(&DefineFixtureFont);
            fontMethods[5] = reinterpret_cast<void*>(&MeasureFixtureFont);
            fontMethods[13] = reinterpret_cast<void*>(&PrepareFixtureFont);
            bodyFixture.methods = fontMethods;
            FixtureFont compact{fontMethods};
            void* factoryMethods[]{nullptr, reinterpret_cast<void*>(&CreateFixtureFont)};
            void** factory = factoryMethods;
            void* factoryObject = &factory;
            std::memcpy(mapped + 0xa01fd0, &factoryObject, sizeof(factoryObject));

            void* widget[27]{};
            void* body[19]{};
            widget[26] = body;
            constexpr unsigned PrepareCall = 0xb893, BodyCall = 0x4f129c;
            using Prepare = int (__fastcall*)(void*, void*);
            using SetBody = void (__fastcall*)(void*, void*, void*);
            const auto destination = [mapped](unsigned offset) {
                std::uintptr_t displacement{};
                std::memcpy(&displacement, mapped + offset + 1, sizeof(displacement));
                return reinterpret_cast<std::uintptr_t>(mapped + offset + 5) + displacement;
            };
            const auto prepare = reinterpret_cast<Prepare>(destination(PrepareCall));
            const auto setBody = reinterpret_cast<SetBody>(destination(BodyCall));
            if (prepare(&compact, nullptr) == 1)
            {
                setBody(widget, nullptr, &compact);
                setBody(widget, nullptr, &compact);
                passed = fontCreates == 1 && definedFontSize == 18 && body[18] == &bodyFixture;
                measuredFontHeight = 21;
                compact.initialized = 0;
                passed = prepare(&compact, nullptr) == 1 && passed;
                setBody(widget, nullptr, &compact);
                passed = passed && fontCreates == 2 && body[18] == &compact;
            }
        }
        VirtualFree(mapped, 0, MEM_RELEASE);
        return passed;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    auto image = MapImage(argv[1]);
    if (image.empty()) return 2;
    struct Import { const char* library; const char* name; unsigned slot; };
    constexpr Import imports[] = {
        {"d3d8.dll", "Direct3DCreate8", 0x6ea5c8},
        {"user32.dll", "DispatchMessageA", 0x6ea3d8},
        {"user32.dll", "ClientToScreen", 0x6ea46c},
    };
    for (const auto& item : imports)
    {
        const auto module = LoadLibraryA(item.library);
        const auto original = module ? GetProcAddress(module, item.name) : nullptr;
        const auto slot = dfl::pe_import::FindNamed(image.data(), image.size(), item.library, item.name);
        if (!original || reinterpret_cast<unsigned char*>(slot) != image.data() + item.slot) return 2;
        std::memcpy(slot, &original, sizeof(original));
    }
    using dfl::presentation::Mode;
    const auto original = image;
    {
        constexpr unsigned EnableOperand = 0x50334b;
        auto warehouse = original;
        if (warehouse[EnableOperand] != 0 ||
            !dfl::chapter_two::InstallWarehouseUpgradeButton(warehouse.data(), warehouse.size()) ||
            warehouse[EnableOperand] != 1 ||
            !dfl::chapter_two::InstallWarehouseUpgradeButton(warehouse.data(), warehouse.size()))
        {
            std::puts("FAIL: the warehouse Upgrade enable operand was not restored");
            return 1;
        }
        warehouse[EnableOperand] = 0;
        if (warehouse != original)
        {
            std::puts("FAIL: the warehouse Upgrade edit changed more than one byte");
            return 1;
        }
        for (const auto offset : {0x502830u, 0x816d30u, 0x50328cu, 0x5023d5u, 0x545460u, EnableOperand - 1})
        {
            warehouse[offset] ^= 1;
            if (dfl::chapter_two::InstallWarehouseUpgradeButton(warehouse.data(), warehouse.size()) ||
                warehouse[EnableOperand] != 0)
            {
                std::puts("FAIL: warehouse Upgrade accepted a mismatched native signature");
                return 1;
            }
            warehouse[offset] ^= 1;
        }
        std::puts("PASS: warehouse Upgrade modifies only the verified enable operand");
    }
    {
        constexpr unsigned BrowserCreateOperand = 0x4fb005;
        constexpr unsigned BackgroundYOffset = 0x4fae25;
        auto payment = original;
        payment[BackgroundYOffset] ^= 1;
        const auto mismatchedPayment = payment;
        if (dfl::chapter_two::InstallPaymentViewport(payment.data(), payment.size()) ||
            payment != mismatchedPayment)
        {
            std::puts("FAIL: a mismatched payment background allowed a browser viewport edit");
            return 1;
        }
        payment = original;
        if (!dfl::chapter_two::InstallPaymentViewport(payment.data(), payment.size()) ||
            Read(payment, BrowserCreateOperand) == Read(original, BrowserCreateOperand))
        {
            std::puts("FAIL: the verified Chapter Two payment window did not install the scaled browser viewport");
            return 1;
        }
        using BrowserCreate = HWND (WINAPI*)(HWND, HINSTANCE, RECT*, BOOL);
        const auto viewport = reinterpret_cast<BrowserCreate>(
            reinterpret_cast<std::uintptr_t>(payment.data() + BrowserCreateOperand + sizeof(unsigned))
            + Read(payment, BrowserCreateOperand));
        if (viewport(nullptr, nullptr, nullptr, FALSE))
        {
            std::puts("FAIL: the payment viewport accepted a missing native rectangle");
            return 1;
        }
        std::memcpy(payment.data() + BrowserCreateOperand, original.data() + BrowserCreateOperand, sizeof(unsigned));
        if (payment != original)
        {
            std::puts("FAIL: the Chapter Two viewport edit changed more than the payment browser call operand");
            return 1;
        }
    }
    {
        auto dialogue = original;
        constexpr unsigned BodyFontOperand = 0x4f1291;
        constexpr unsigned BodyGapOperand = 0x4f126e;
        constexpr unsigned BodyFontCall = 0x4f129c;
        constexpr unsigned CompactFontSize = 0xb8db;
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_SIZE", nullptr);
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_GAP", nullptr);
        dialogue[CompactFontSize] ^= 1;
        const auto mismatchedDialogue = dialogue;
        if (dfl::chapter_two::InstallNpcDialogueLayout(dialogue.data(), dialogue.size()) ||
            dialogue != mismatchedDialogue)
        {
            std::puts("FAIL: a mismatched compact NPC font initializer allowed an image edit");
            return 1;
        }
        dialogue = original;
        if (Read(dialogue, BodyFontOperand) != 0xd54148 ||
            !dfl::chapter_two::InstallNpcDialogueLayout(dialogue.data(), dialogue.size()) ||
            Read(dialogue, BodyFontOperand) != 0xd54144)
        {
            std::puts("FAIL: the verified Chapter Two NPC body did not select the compact Chinese font");
            return 1;
        }
        dialogue[BodyFontOperand] = original[BodyFontOperand];
        if (dialogue != original)
        {
            std::puts("FAIL: the Chapter Two NPC layout edit changed more than the body font selector");
            return 1;
        }
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_SIZE", "18");
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_GAP", "4");
        dialogue = original;
        if (!dfl::chapter_two::InstallNpcDialogueLayout(dialogue.data(), dialogue.size()) ||
            dialogue[BodyGapOperand] != 4 ||
            Read(dialogue, BodyFontCall + 1) == Read(original, BodyFontCall + 1))
        {
            std::puts("FAIL: the requested NPC body size and spacing did not reach the native widget");
            return 1;
        }
        if (!ExerciseDialogueFont(original))
        {
            std::puts("FAIL: the real native widget did not receive the cached font and the fallback");
            return 1;
        }
        dialogue = original;
        dialogue[BodyGapOperand] ^= 1;
        const auto mismatchedGap = dialogue;
        if (dfl::chapter_two::InstallNpcDialogueLayout(dialogue.data(), dialogue.size()) ||
            dialogue != mismatchedGap)
        {
            std::puts("FAIL: an unrecognized NPC gap setter was modified");
            return 1;
        }
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_SIZE", "999");
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_GAP", "0");
        dialogue = original;
        if (!dfl::chapter_two::InstallNpcDialogueLayout(dialogue.data(), dialogue.size()) ||
            dialogue[BodyGapOperand] != 0 ||
            Read(dialogue, BodyFontCall + 1) != Read(original, BodyFontCall + 1))
        {
            std::puts("FAIL: an invalid font size should fall back without discarding a valid gap");
            return 1;
        }
        dialogue[BodyGapOperand] = original[BodyGapOperand];
        dialogue[BodyFontOperand] = original[BodyFontOperand];
        if (dialogue != original)
        {
            std::puts("FAIL: an NPC spacing change modified unrelated text");
            return 1;
        }
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_SIZE", nullptr);
        SetEnvironmentVariableA("DFL_S1C2_DIALOGUE_GAP", nullptr);
        std::puts("PASS: verified default, custom NPC body size and gap, and invalid settings");
    }
    auto hud = original;
    hud[0x51e2cc] ^= 1;
    const auto mismatchedHud = hud;
    if (dfl::chapter_two::InstallPerformanceOverlay(hud.data(), hud.size()) || hud != mismatchedHud)
    {
        std::puts("FAIL: a mismatched HUD call site allowed an image edit");
        return 1;
    }
    hud = original;
    if (!dfl::chapter_two::InstallPerformanceOverlay(hud.data(), hud.size()) ||
        hud[0x51e2c7] != 0x90 || hud[0x51e2c8] != 0x90 ||
        hud[0x51e2cb] != 0xe8 || Read(hud, 0x51e2cc) == Read(original, 0x51e2cc))
    {
        std::puts("FAIL: verified Chapter Two frame call did not install the HUD");
        return 1;
    }
    if (dfl::chapter_two::InstallPresentation(image.data(), image.size(), Mode::Legacy, 1280, 960)
        || dfl::chapter_two::InstallPresentation(image.data(), image.size(), Mode::Sharp, 1280, 900)
        || image != original)
    {
        std::puts("FAIL: disabled or unsupported display configuration touched the image");
        return 1;
    }
    image[0x64227f] ^= 1;
    const auto mismatched = image;
    if (dfl::chapter_two::InstallPresentation(image.data(), image.size(), Mode::Sharp, 1280, 960) || image != mismatched)
    {
        std::puts("FAIL: one incompatible cursor call did not prevent every write");
        return 1;
    }
    image = original;
    if (!dfl::chapter_two::InstallPresentation(image.data(), image.size(), Mode::Sharp, 1280, 960))
    {
        std::puts("FAIL: compatible Chapter Two image did not install the display adapter");
        return 1;
    }
    if (Read(image, OuterWidth) != 1280 || Read(image, OuterHeight) != 960 ||
        Read(image, 0x63d7a3) != 1280 || Read(image, 0x63d7af) != 960 ||
        Read(image, LogicalWidth) != 640 || Read(image, LogicalHeight) != 480)
    {
        std::puts("FAIL: adapter did not keep the 640x480 game frame separate from the outer window");
        return 1;
    }
    for (const auto& item : imports)
    {
        if (Read(image, item.slot) == Read(original, item.slot))
        {
            std::puts("FAIL: one verified display adapter import did not get redirected");
            return 1;
        }
    }
    std::puts("PASS: fail-closed image validation, payment viewport, NPC dialogue, outer window size, fixed frame and input import adapters");
    return 0;
}
