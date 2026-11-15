// Owned synthetic image only: execute compatible inventory calls and selector.
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
    std::uintptr_t TailSite = 0;
    constexpr unsigned char Tail[]{0x83,0xFA,0xFF,0x74,0x03,0x89,0x56,0x3C,0x5E,0x5D,0xC2,0x04,0x00};
    constexpr unsigned char Original[]{
        0x6A,0x01,0x8B,0x8D,0x98,0xEF,0xFF,0xFF,0x51,
        0x0F,0xB7,0x95,0xAC,0xEF,0xFF,0xFF,0x52,
        0x8B,0x8D,0xB8,0xEF,0xFF,0xFF,0xE8,0x69,0x10,0x04,0x00};
    static_assert(sizeof(Original) == 28);
    constexpr unsigned char NewItemCall[]{
        0x6A,0x01,0x8B,0x85,0x50,0xEF,0xFF,0xFF,0x50,
        0x0F,0xB7,0x8D,0x70,0xEF,0xFF,0xFF,0x51,
        0x8B,0x8D,0x7C,0xEF,0xFF,0xFF,0xE8,0x78,0x0A,0x04,0x00};
    constexpr unsigned char IncreaseCall[]{
        0xA1,0xE8,0xDC,0xB5,0x00,0x8B,0x88,0x80,0x00,0x00,0x00,
        0x57,0xE8,0xA0,0xDE,0x34,0x00};
    // Bounded original 7AAAF0..7AAB6F fragment (function and switch table).
    // Only absolute manager/table addresses are relocated in this fixture.
    constexpr unsigned char Selector[]{
        0x55,0x8B,0xEC,0x8B,0x45,0x08,0x56,0x8B,0xF1,0x8B,0x0D,0xEC,
        0xDC,0xB5,0x00,0x50,0xE8,0x5B,0x2F,0xCB,0xFF,0xBA,0x03,0x00,
        0x00,0x00,0x8D,0x9B,0x00,0x00,0x00,0x00,0x83,0xFA,0x04,0x77,
        0x2A,0xFF,0x24,0x95,0x5C,0xAB,0x7A,0x00,0xB9,0x09,0x00,0x00,
        0x00,0xEB,0x1E,0xB9,0x29,0x00,0x00,0x00,0xEB,0x17,0xB9,0x49,
        0x00,0x00,0x00,0xEB,0x10,0xB9,0x69,0x00,0x00,0x00,0xEB,0x09,
        0xB9,0x89,0x00,0x00,0x00,0xEB,0x02,0x33,0xC9,0x3B,0xC1,0x7D,
        0x08,0x4A,0x79,0xC8,0x5E,0x5D,0xC2,0x04,0x00,0x83,0xFA,0xFF,
        0x74,0x03,0x89,0x56,0x3C,0x5E,0x5D,0xC2,0x04,0x00,0x8B,0xFF,
        0x1C,0xAB,0x7A,0x00,0x23,0xAB,0x7A,0x00,0x2A,0xAB,0x7A,0x00,
        0x31,0xAB,0x7A,0x00,0x38,0xAB,0x7A,0x00};
    static_assert(sizeof(Selector) == 128);
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
    struct Item
    {
        unsigned Id;
        unsigned Category;
        Model* Owner = nullptr;
        unsigned Amount = 1;
        int LookupSlot = INT32_MIN;
    };
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
    void* SelectorEntry = nullptr;
    void* SelectorWindow = nullptr;
    Item* SelectorItem = nullptr;
    unsigned HarnessEdi = 0x33445566;
    unsigned LookupCalls = 0, NewItemCalls = 0, IncreaseCalls = 0;
    unsigned AfterEax = 0, AfterEcx = 0, AfterEdx = 0, AfterFlags = 0;
    unsigned char LookupManager{};
    alignas(4) unsigned char UiManager[0x84]{};

    int __fastcall Lookup(void* manager, void*, Item* item)
    {
        ++LookupCalls;
        Check(manager == &LookupManager && item == ExpectedItem,
            "actual selector passes the manager and item to deterministic slot lookup");
        return item->LookupSlot == INT32_MIN ? 9 + static_cast<int>(item->Category) * 32 : item->LookupSlot;
    }
    int Select(Window* window, Item* item)
    {
        using Function = int (__thiscall*)(Window*, Item*);
        return reinterpret_cast<Function>(SelectorEntry)(window, item);
    }

    // Compatible 45E280 boundary: ECX=this, stack=(slot,item,autoSelect), ret12.
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
            Check(Select(model->Ui, item) == 9 + static_cast<int>(item->Category) * 32,
                "native selector still returns the item slot");
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
            mov edi, HarnessEdi
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
    __declspec(naked) void RunSelector()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp, esp
            mov ecx, SelectorWindow
            mov ebx, 11223344h
            mov esi, 22334455h
            mov edi, 33445566h
            mov ebp, 44556677h
            push 247h
            popfd
            push SelectorItem
            call SelectorEntry
            mov AfterEsp, esp
            mov AfterEax, eax
            mov AfterEcx, ecx
            mov AfterEdx, edx
            mov AfterEbp, ebp
            mov AfterEbx, ebx
            mov AfterEsi, esi
            mov AfterEdi, edi
            pushfd
            pop AfterFlags
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
        Entry = reinterpret_cast<void*>(Site);
        HarnessEdi = 0x33445566;
        const auto before = Calls;
        RunCall();
        Check(Calls == before + 1 && !BadArguments, "ECX and three stack arguments reach the callee once");
        Check(AfterEsp == BeforeEsp && AfterEbp == reinterpret_cast<unsigned>(FrameTop)
            && AfterEbx == 0x11223344 && AfterEsi == 0x22334455 && AfterEdi == 0x33445566,
            "x86 ret12 balances ESP and preserves EBP/EBX/ESI/EDI");
        Check(CalleeFlags == BeforeFlags, "push/mov/movzx/call retains caller flags up to the callee");
    }
    void ExecuteNewItem(Model& model, unsigned slot, Item* item)
    {
        ExpectedModel = &model; ExpectedItem = item; ExpectedSlot = slot;
        Put(Frame + sizeof(Frame) - 0x10B0, item);
        Put(Frame + sizeof(Frame) - 0x1090, 0xBEEF0000u | slot);
        Put(Frame + sizeof(Frame) - 0x1084, &model);
        Entry = reinterpret_cast<void*>(Image + 0x1D7EC);
        HarnessEdi = 0x33445566;
        const auto before = Calls;
        RunCall(); ++NewItemCalls;
        Check(Calls == before + 1 && !BadArguments && LastFlag == 1,
            "actual NOTI14 call retains receiver, item, zero-extended slot and flag1");
        Check(AfterEsp == BeforeEsp && AfterEbp == reinterpret_cast<unsigned>(FrameTop)
            && AfterEbx == 0x11223344 && AfterEsi == 0x22334455 && AfterEdi == 0x33445566
            && CalleeFlags == BeforeFlags,
            "actual NOTI14 call preserves x86 stack, saved registers and incoming flags");
    }
    void ExecuteIncrease(Model& model, Item& item, unsigned amount)
    {
        ExpectedItem = &item;
        const auto oldOwner = item.Owner;
        const auto slots = model.Slots;
        if (amount > item.Amount && model.Space == 0)
        {
            Put(UiManager + 0x80, model.Ui);
            HarnessEdi = reinterpret_cast<unsigned>(&item);
            Entry = reinterpret_cast<void*>(Image + 0x5CC3F);
            const auto lookups = LookupCalls;
            RunCall(); ++IncreaseCalls;
            Check(LookupCalls == lookups + 1 && AfterEsp == BeforeEsp
                && AfterEbp == reinterpret_cast<unsigned>(FrameTop) && AfterEbx == 0x11223344
                && AfterEsi == 0x22334455 && AfterEdi == HarnessEdi,
                "actual amount-increase caller executes selector once with balanced stack and saved registers");
        }
        item.Amount = amount; // The native amount mirror update follows 45CC4B.
        Check(item.Amount == amount && item.Owner == oldOwner && model.Slots == slots,
            "amount update survives and retains item ownership and inventory slots");
    }
    void PartialUpdates(bool originalControl)
    {
        Window window;
        Model model{0, &window};
        Item material{303, 2}, quest{404, 3};
        for (bool visible : {false, true})
            for (unsigned tab = 0; tab < 4; ++tab)
                for (Item* item : {&material, &quest})
                {
                    window.Click(tab); window.Visible = visible;
                    ExecuteNewItem(model, 8 + item->Category, item);
                    window.Visible = true;
                    Check(window.Selected == (originalControl ? item->Category : tab),
                        "new-item sync visible/hidden followed by reopen retains the chosen tab");
                    Check(model.Slots[8 + item->Category] == item && item->Owner == &model,
                        "partial new-item insertion and owner update survive");
                    window.Click(tab); window.Visible = visible;
                    item->Amount = 5;
                    ExecuteIncrease(model, *item, 11);
                    window.Visible = true;
                    Check(window.Selected == (originalControl ? item->Category : tab),
                        "stack-increase sync visible/hidden followed by reopen retains the chosen tab");
                    window.DragTo((tab + 1) % 4);
                    ExecuteIncrease(model, *item, 12);
                    Check(window.Selected == (originalControl ? item->Category : (tab + 1) % 4),
                        "user drag remains authoritative across the next automatic amount update");
                }
        for (unsigned space = 1; space <= 4; ++space)
        {
            Window otherWindow; otherWindow.Selected = 77;
            Model other{space, &otherWindow};
            const auto lookups = LookupCalls;
            ExecuteNewItem(other, 9, &quest);
            ExecuteIncrease(other, quest, quest.Amount + 1);
            Check(otherWindow.Selected == 77 && LookupCalls == lookups
                && other.Slots[9] == &quest && quest.Owner == &other,
                "partial updates in other inventory spaces retain their UI behavior and model updates");
        }
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
        Check(at == TailSite + 5 && size == 3, "installer writes only the automatic selector's final tab store");
        const bool result = dfl::memory_patch::WriteBytes(at, bytes, size);
        if (FailWrite) { FailWrite = false; return false; }
        return result;
    }
    void ResetSite()
    {
        Check(dfl::memory_patch::WriteBytes(Site, Original, sizeof(Original)), "restore owned original signature");
        Check(dfl::memory_patch::WriteBytes(TailSite, Tail, sizeof(Tail)), "restore owned original selector tail");
        Writes = 0;
    }
    void Safety()
    {
        using dfl::inventory_tab::detail::InstallImage;
        Check(!dfl::inventory_tab::Install(), "runtime wrapper rejects this relocated fixture executable");
        for (std::size_t i = 0; i < sizeof(Tail); ++i)
        {
            ResetSite();
            auto changed = std::array<unsigned char, sizeof(Tail)>{};
            std::memcpy(changed.data(), Tail, sizeof(Tail));
            changed[i] ^= 0x80;
            dfl::memory_patch::WriteBytes(TailSite, changed.data(), changed.size());
            Check(!InstallImage(Image, &Write) && Writes == 0
                && std::memcmp(reinterpret_cast<void*>(TailSite), changed.data(), changed.size()) == 0,
                "unknown branch/store/return context byte is rejected without partial writes");
        }
        for (unsigned mask = 1; mask < 7; ++mask)
        {
            ResetSite();
            auto partial = std::array<unsigned char, sizeof(Tail)>{};
            std::memcpy(partial.data(), Tail, sizeof(Tail));
            for (unsigned i = 0; i < 3; ++i) if (mask & (1u << i)) partial[5 + i] = 0x90;
            dfl::memory_patch::WriteBytes(TailSite, partial.data(), partial.size());
            Check(!InstallImage(Image, &Write) && Writes == 0
                && std::memcmp(reinterpret_cast<void*>(TailSite), partial.data(), partial.size()) == 0,
                "partially modified store is rejected without further writes");
        }
        ResetSite();
        Check(!InstallImage(0, &Write) && !InstallImage(UINTPTR_MAX, &Write)
            && !InstallImage(1, &Write) && !InstallImage(Image, nullptr) && Writes == 0,
            "invalid, overflow, unreadable image or missing writer fails closed");
        FailWrite = true;
        Check(!InstallImage(Image, &Write) && Writes == 2
            && std::memcmp(reinterpret_cast<void*>(TailSite), Tail, sizeof(Tail)) == 0,
            "failure after writing restores the original three bytes");
        ResetSite();
        Check(InstallImage(Image, &Write) && Writes == 1, "supported original selector is installed once");
        Check(std::memcmp(reinterpret_cast<void*>(Site), Original, sizeof(Original)) == 0
            && std::memcmp(reinterpret_cast<void*>(TailSite + 5), "\x90\x90\x90", 3) == 0,
            "selector store is suppressed and redundant NOTI13 immediate patch is absent");
        MEMORY_BASIC_INFORMATION protection{};
        VirtualQuery(reinterpret_cast<void*>(TailSite), &protection, sizeof(protection));
        Check(protection.Protect == PAGE_EXECUTE_READ, "real memory writer restores executable read-only protection");
        Check(InstallImage(Image, &Write) && Writes == 1, "exact installed signature is idempotent");
        unsigned char bad = 0x80;
        dfl::memory_patch::WriteBytes(TailSite + 12, &bad, 1);
        Check(!InstallImage(Image, &Write) && Writes == 1,
            "patched store with a foreign return context is rejected");
        ResetSite();
    }
    void SelectorAbi()
    {
        constexpr int slots[]{-9,-1,0,8,9,40,41,72,73,104,105,136,137,500};
        using Registers = std::array<unsigned, 8>;
        std::array<Registers, sizeof(slots) / sizeof(slots[0])> original{};
        Window window;
        Item item{505, 0};
        SelectorWindow = &window; SelectorItem = &item; ExpectedItem = &item;
        for (unsigned pass = 0; pass < 2; ++pass)
        {
            if (pass == 0) ResetSite();
            else Check(dfl::inventory_tab::detail::InstallImage(Image, &Write), "install selector for ABI comparison");
            for (unsigned i = 0; i < sizeof(slots) / sizeof(slots[0]); ++i)
            {
                item.LookupSlot = slots[i]; window.Selected = 77;
                const auto calls = LookupCalls;
                RunSelector();
                Check(LookupCalls == calls + 1 && AfterEax == static_cast<unsigned>(slots[i]),
                    "actual original and patched selector retain exactly one lookup and EAX slot result");
                Check(AfterEsp == BeforeEsp && AfterEbp == 0x44556677 && AfterEbx == 0x11223344
                    && AfterEsi == 0x22334455 && AfterEdi == 0x33445566,
                    "actual selector ret4 balances stack and preserves all callee-saved registers");
                const Registers observed{AfterEax,AfterEcx,AfterEdx,AfterFlags,AfterEbp,AfterEbx,AfterEsi,AfterEdi};
                const auto category = slots[i] >= 105 ? 3u : slots[i] >= 73 ? 2u
                    : slots[i] >= 41 ? 1u : slots[i] >= 9 ? 0u : 77u;
                if (pass == 0)
                {
                    original[i] = observed;
                    Check(window.Selected == category, "unpatched actual selector computes original category boundaries");
                }
                else
                {
                    Check(observed == original[i], "patch preserves original EAX/ECX/EDX, EFLAGS and saved-register results");
                    Check(window.Selected == 77, "all selector return paths retain the selected tab after patch");
                }
            }
        }
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
                Check(mainWindow.Selected == tab && LastFlag == 1, "repeated mixed Sort retains chosen tab without changing insertion flag");
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
        // All automatic synchronization callers now retain the user selection.
        ExpectedModel = &model; ExpectedItem = &quest; ExpectedSlot = 8;
        mainWindow.Click(1);
        Insert(&model, nullptr, 8, &quest, 1);
        Check(mainWindow.Selected == 1 && model.Slots[8] == &quest,
            "non-full-refresh automatic caller retains the user tab and updates the model");
    }
}

int main(int argc, char** argv)
{
    const bool originalControl = argc == 2 && std::strcmp(argv[1], "--original-control") == 0;
    const bool partialOnly = argc == 2 && std::strcmp(argv[1], "--partial-updates-only") == 0;
    if (argc > 2 || (argc == 2 && !originalControl && !partialOnly)) return 2;
    constexpr std::size_t ImageSize = 0x3B0000;
    auto* owned = VirtualAlloc(nullptr, ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!owned)
    {
        std::fprintf(stderr, "FAIL inventory tab: owned synthetic image allocation error=%lu\n", GetLastError());
        return 1;
    }
    Image = reinterpret_cast<std::uintptr_t>(owned);
    Site = Image + 0x1D1FB;
    TailSite = Image + 0x3AAB4D;
    Entry = reinterpret_cast<void*>(Site);
    std::memcpy(reinterpret_cast<void*>(Site), Original, sizeof(Original));
    *reinterpret_cast<unsigned char*>(Site + sizeof(Original)) = 0xC3;
    auto* newItem = reinterpret_cast<unsigned char*>(Image + 0x1D7EC);
    std::memcpy(newItem, NewItemCall, sizeof(NewItemCall));
    newItem[sizeof(NewItemCall)] = 0xC3;
    auto* increase = reinterpret_cast<unsigned char*>(Image + 0x5CC3F);
    std::memcpy(increase, IncreaseCall, sizeof(IncreaseCall));
    increase[sizeof(IncreaseCall)] = 0xC3;
    Put(increase + 1, Image + 0x1010);
    Put(reinterpret_cast<void*>(Image + 0x1010), UiManager);
    auto* selector = reinterpret_cast<unsigned char*>(Image + 0x3AAAF0);
    SelectorEntry = selector;
    std::memcpy(selector, Selector, sizeof(Selector));
    Put(selector + 11, Image + 0x1000);
    Put(reinterpret_cast<void*>(Image + 0x1000), &LookupManager);
    Put(selector + 40, Image + 0x3AAB5C);
    for (unsigned i = 0; i < 5; ++i)
    {
        unsigned target{};
        std::memcpy(&target, Selector + 108 + i * 4, sizeof(target));
        Put(selector + 108 + i * 4, target - 0x400000 + Image);
    }
    unsigned char lookup[]{0xE9,0,0,0,0};
    Put(lookup + 1, static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(&Lookup) - (Image + 0x5DA60) - sizeof(lookup)));
    std::memcpy(reinterpret_cast<void*>(Image + 0x5DA60), lookup, sizeof(lookup));
    // At the original call target, capture flags without changing them and
    // tail-call the synthetic insertion model, which invokes the selector above.
    unsigned char callee[]{0x9C,0x8F,0x05,0,0,0,0,0xE9,0,0,0,0};
    Put(callee + 3, reinterpret_cast<unsigned>(&CalleeFlags));
    Put(callee + 8, static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(&Insert) - (Image + 0x5E280) - sizeof(callee)));
    std::memcpy(reinterpret_cast<void*>(Image + 0x5E280), callee, sizeof(callee));
    DWORD previous{};
    if (!VirtualProtect(owned, ImageSize, PAGE_EXECUTE_READ, &previous)) return 1;
    FlushInstructionCache(GetCurrentProcess(), owned, ImageSize);
    if (partialOnly)
    {
        Check(dfl::inventory_tab::detail::InstallImage(Image, &Write), "production installer accepts compatible image");
        PartialUpdates(false);
    }
    else
    {
        if (!originalControl) { Safety(); SelectorAbi(); }
        Behavior(originalControl);
        PartialUpdates(originalControl);
    }
    VirtualFree(owned, 0, MEM_RELEASE);
    std::printf("inventory tab %s: %d failure(s), %u insertion calls (%u NOTI14), %u amount-increase calls, %u native selector lookups\n",
        originalControl ? "original control" : (partialOnly ? "partial updates" : "preservation"),
        Failures, Calls, NewItemCalls, IncreaseCalls, LookupCalls);
    return Failures ? 1 : 0;
}
