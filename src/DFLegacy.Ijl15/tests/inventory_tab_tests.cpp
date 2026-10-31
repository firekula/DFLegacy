// Owned synthetic image only: execute the compatible NOTI13 x86 call contract.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "inventory_tab.h"
#include "memory_patch.h"

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace
{
    std::uintptr_t Image = 0;
    std::uintptr_t Site = 0;
    constexpr unsigned char Original[]{
        0x6A,0x01,0x8B,0x8D,0x98,0xEF,0xFF,0xFF,0x51,
        0x0F,0xB7,0x95,0xAC,0xEF,0xFF,0xFF,0x52,
        0x8B,0x8D,0xB8,0xEF,0xFF,0xFF,0xE8,0x69,0x10,0x04,0x00};
    static_assert(sizeof(Original) == 28);
    int Failures = 0;
    unsigned Writes = 0;
    bool FailWrite = false;
    void Check(bool ok, const char* message)
    {
        if (!ok) { ++Failures; std::fprintf(stderr, "FAIL inventory tab: %s\n", message); }
    }
    template<class T> void Put(void* at, T value) { std::memcpy(at, &value, sizeof(value)); }

    // The native window keeps the selected main tab at +3C. Show/hide retains it.
    struct Window
    {
        unsigned char Prefix[0x3C]{};
        unsigned Selected = 0;
        bool Visible = false;
        void Click(unsigned tab) { Selected = tab; }
        void DragTo(unsigned tab) { Selected = tab; }
    };
    static_assert(offsetof(Window, Selected) == 0x3C);
    struct Model;
    struct Item { unsigned Id; unsigned Category; Model* Owner = nullptr; };
    struct Model
    {
        unsigned Space;
        Window* Ui;
        std::array<Item*, 16> Slots{};
        unsigned Updates = 0;
    };
    unsigned Calls = 0, LastFlag = 99;
    bool BadArguments = false;
    Model* ExpectedModel = nullptr;
    Item* ExpectedItem = nullptr;
    unsigned ExpectedSlot = 0;
    unsigned CalleeFlags = 0, BeforeFlags = 0, BeforeEsp = 0, AfterEsp = 0;
    unsigned AfterEbp = 0, AfterEbx = 0, AfterEsi = 0, AfterEdi = 0;
    alignas(4) unsigned char Frame[0x1100]{};
    void* FrameTop = Frame + sizeof(Frame);
    void* Entry = nullptr;

    // Compatible45E280 boundary: ECX=this, stack=(slot,item,autoSelect), ret12.
    // Only its external item/UI effects are synthetic; the call bytes execute.
    unsigned char __fastcall Insert(Model* model, void*, unsigned slot, Item* item, unsigned autoSelect)
    {
        ++Calls; LastFlag = autoSelect;
        if (model != ExpectedModel || item != ExpectedItem || slot != ExpectedSlot
            || autoSelect > 1 || slot >= model->Slots.size())
        { BadArguments = true; return 0; }
        model->Slots[slot] = item;
        if (item) item->Owner = model;
        ++model->Updates;
        if (autoSelect == 1 && model->Space == 0 && item)
            model->Ui->Selected = item->Category;
        return 1;
    }
    __declspec(naked) void RunCall()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp, esp
            mov ebp, FrameTop
            mov ebx, 11223344h
            mov esi, 22334455h
            mov edi, 33445566h
            push 247h
            popfd
            pushfd
            pop BeforeFlags
            call Entry
            mov AfterEsp, esp
            mov AfterEbp, ebp
            mov AfterEbx, ebx
            mov AfterEsi, esi
            mov AfterEdi, edi
            popad
            popfd
            ret
        }
    }
    void Execute(Model& model, unsigned slot, Item* item)
    {
        ExpectedModel = &model; ExpectedItem = item; ExpectedSlot = slot;
        Put(Frame + sizeof(Frame) - 0x1068, item);
        // Upper16 bits must not leak through native movzx word[ebp-1054].
        Put(Frame + sizeof(Frame) - 0x1054, 0xBEEF0000u | slot);
        Put(Frame + sizeof(Frame) - 0x1048, &model);
        const auto before = Calls;
        RunCall();
        Check(Calls == before + 1 && !BadArguments, "ECX and three stack arguments reach the callee once");
        Check(AfterEsp == BeforeEsp && AfterEbp == reinterpret_cast<unsigned>(FrameTop)
            && AfterEbx == 0x11223344 && AfterEsi == 0x22334455 && AfterEdi == 0x33445566,
            "x86 ret12 balances ESP and preserves EBP/EBX/ESI/EDI");
        Check(CalleeFlags == BeforeFlags, "push/mov/movzx/call retains caller flags up to the callee");
    }
    void Refresh(Model& model, Item* const* items, std::size_t size)
    {
        model.Slots.fill(nullptr); // NOTI13 still clears and rebuilds the model.
        const auto updates = model.Updates;
        for (std::size_t i = 0; i < size; ++i) Execute(model, static_cast<unsigned>(i), items[i]);
        Check(model.Updates == updates + size, "all original item update notifications remain");
        for (std::size_t i = 0; i < model.Slots.size(); ++i)
            Check(model.Slots[i] == (i < size ? items[i] : nullptr), "refresh retains server slot order and clears stale slots");
        for (std::size_t i = 0; i < size; ++i)
            Check(!items[i] || items[i]->Owner == &model, "inserted item owner remains the actual model");
    }
    bool Write(std::uintptr_t at, const void* bytes, std::size_t size)
    {
        ++Writes;
        Check(at == Site + 1 && size == 1, "installer writes only the auto-select immediate");
        const bool result = dfl::memory_patch::WriteBytes(at, bytes, size);
        if (FailWrite) { FailWrite = false; return false; }
        return result;
    }
    void ResetSite()
    {
        Check(dfl::memory_patch::WriteBytes(Site, Original, sizeof(Original)), "restore owned original signature");
        Writes = 0;
    }
    void Safety()
    {
        using dfl::inventory_tab::detail::InstallImage;
        Check(!dfl::inventory_tab::Install(), "runtime wrapper rejects this relocated fixture executable");
        for (std::size_t i = 0; i < sizeof(Original); ++i)
        {
            ResetSite();
            auto changed = std::array<unsigned char, sizeof(Original)>{};
            std::memcpy(changed.data(), Original, sizeof(Original));
            changed[i] ^= 0x80;
            dfl::memory_patch::WriteBytes(Site, changed.data(), changed.size());
            Check(!InstallImage(Image, &Write) && Writes == 0
                && std::memcmp(reinterpret_cast<void*>(Site), changed.data(), changed.size()) == 0,
                "unknown opcode/operand/call byte is rejected without partial writes");
        }
        ResetSite();
        Check(!InstallImage(0, &Write) && !InstallImage(UINTPTR_MAX, &Write)
            && !InstallImage(1, &Write) && !InstallImage(Image, nullptr) && Writes == 0,
            "invalid, overflow, unreadable image or missing writer fails closed");
        FailWrite = true;
        Check(!InstallImage(Image, &Write) && Writes == 2
            && std::memcmp(reinterpret_cast<void*>(Site), Original, sizeof(Original)) == 0,
            "failure after writing restores the original byte");
        ResetSite();
        Check(InstallImage(Image, &Write) && Writes == 1, "supported original call is installed once");
        MEMORY_BASIC_INFORMATION protection{};
        VirtualQuery(reinterpret_cast<void*>(Site), &protection, sizeof(protection));
        Check(protection.Protect == PAGE_EXECUTE_READ, "real memory writer restores executable read-only protection");
        Check(InstallImage(Image, &Write) && Writes == 1, "exact installed signature is idempotent");
        unsigned char bad = 0x80;
        dfl::memory_patch::WriteBytes(Site + 27, &bad, 1);
        Check(!InstallImage(Image, &Write) && Writes == 1,
            "patched flag with a foreign call target is rejected");
        ResetSite();
    }
    void Behavior(bool originalControl)
    {
        Window mainWindow;
        Model model{0, &mainWindow};
        Item equipment{101, 0}, consumable{202, 1}, material{303, 2}, quest{404, 3};
        Item* endingMaterials[]{&equipment, &consumable, &quest, &material};
        Item* endingQuest[]{&equipment, &material, &consumable, &quest};
        if (originalControl)
        {
            mainWindow.Click(1);
            Refresh(model, endingMaterials, 4);
            Check(mainWindow.Selected == 2 && LastFlag == 1, "original full refresh reproduces the Materials jump");
            mainWindow.Visible = false;
            mainWindow.Click(0);
            Refresh(model, endingQuest, 4);
            mainWindow.Visible = true;
            Check(mainWindow.Selected == 3, "original hidden refresh reproduces the reopen jump");
            return;
        }
        Check(dfl::inventory_tab::detail::InstallImage(Image, &Write), "production installer accepts compatible call");
        Refresh(model, endingMaterials, 4);
        mainWindow.Visible = true;
        Check(mainWindow.Selected == 0, "first open keeps constructor equipment default after full refresh");
        for (unsigned tab = 0; tab < 4; ++tab)
        {
            mainWindow.Click(tab);
            Check(mainWindow.Selected == tab, "manual tab switch remains effective");
            mainWindow.Visible = false;
            mainWindow.Visible = true;
            Check(mainWindow.Selected == tab, "close/reopen without a refresh retains chosen tab");
            for (unsigned repeat = 0; repeat < 3; ++repeat)
            {
                Refresh(model, endingMaterials, 4);
                Check(mainWindow.Selected == tab && LastFlag == 0, "repeated mixed Sort ending Materials retains chosen tab");
                Refresh(model, endingQuest, 4);
                Check(mainWindow.Selected == tab, "mixed refresh ending Quest retains chosen tab");
                Refresh(model, nullptr, 0);
                Check(mainWindow.Selected == tab, "empty refresh retains chosen tab");
                mainWindow.Visible = false;
                Refresh(model, endingMaterials, 4);
                Refresh(model, endingQuest, 4);
                Refresh(model, nullptr, 0);
                mainWindow.Visible = true;
                Check(mainWindow.Selected == tab, "close/hidden populated and empty refresh/reopen retains chosen tab");
            }
            mainWindow.DragTo((tab + 1) % 4);
            Check(mainWindow.Selected == (tab + 1) % 4, "drag-specific selection remains effective");
        }
        Item* nullItem[]{nullptr};
        Refresh(model, nullItem, 1);
        for (unsigned space = 1; space <= 4; ++space)
        {
            Window otherWindow; otherWindow.Selected = 77;
            Model other{space, &otherWindow};
            Refresh(other, endingQuest, 4);
            Check(otherWindow.Selected == 77, "other inventory spaces retain original UI behavior and item updates");
        }
        // Acquisition/move callers still request stock auto-selection directly.
        ExpectedModel = &model; ExpectedItem = &quest; ExpectedSlot = 8;
        Insert(&model, nullptr, 8, &quest, 1);
        Check(mainWindow.Selected == 3 && model.Slots[8] == &quest,
            "non-full-refresh caller can still auto-select; selector is not globally disabled");
    }
}

int main(int argc, char** argv)
{
    const bool originalControl = argc == 2 && std::strcmp(argv[1], "--original-control") == 0;
    if (argc > 2 || (argc == 2 && !originalControl)) return 2;
    auto* owned = VirtualAlloc(nullptr, 0x60000,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!owned)
    {
        std::fprintf(stderr, "FAIL inventory tab: owned synthetic image allocation error=%lu\n", GetLastError());
        return 1;
    }
    Image = reinterpret_cast<std::uintptr_t>(owned);
    Site = Image + 0x1D1FB;
    Entry = reinterpret_cast<void*>(Site);
    std::memcpy(reinterpret_cast<void*>(Site), Original, sizeof(Original));
    *reinterpret_cast<unsigned char*>(Site + sizeof(Original)) = 0xC3;
    // At the original call target, capture flags without changing them and
    // tail-call the compatible synthetic callee. No game bytes beyond the site.
    unsigned char callee[]{0x9C,0x8F,0x05,0,0,0,0,0xE9,0,0,0,0};
    Put(callee + 3, reinterpret_cast<unsigned>(&CalleeFlags));
    Put(callee + 8, static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(&Insert) - (Image + 0x5E280) - sizeof(callee)));
    std::memcpy(reinterpret_cast<void*>(Image + 0x5E280), callee, sizeof(callee));
    DWORD previous{};
    if (!VirtualProtect(owned, 0x60000, PAGE_EXECUTE_READ, &previous)) return 1;
    FlushInstructionCache(GetCurrentProcess(), owned, 0x60000);
    if (!originalControl) Safety();
    Behavior(originalControl);
    VirtualFree(owned, 0, MEM_RELEASE);
    std::printf("inventory tab %s: %d failure(s), %u executed x86 insertion calls\n",
        originalControl ? "original control" : "preservation", Failures, Calls);
    return Failures ? 1 : 0;
}
