#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

#ifndef CHAPTER_TWO_INVENTORY_BASELINE
#include "ChapterTwoInventoryTab.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char inventoryFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::uintptr_t Base = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t Selector = 0x477ef0, Store = 0x477f52;
    int Failures = 0;
    unsigned Checks = 0, Writes = 0, FailWrite = 0;

    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL Chapter Two inventory: %s\n", message); }
    }

    template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }

    std::vector<unsigned char> ReadImage(const wchar_t* path)
    {
        std::ifstream stream(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)), {});
        const auto fits = [](std::size_t offset, std::size_t size, std::size_t limit) {
            return offset <= limit && size <= limit - offset;
        };
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(static_cast<std::size_t>(dos->e_lfanew), sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != Base || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(static_cast<std::size_t>(reinterpret_cast<const unsigned char*>(sections) - file.data()),
                sizeof(*sections) * nt->FileHeader.NumberOfSections, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, ImageSize)) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, ImageSize) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress, file.data() + section.PointerToRawData,
                section.SizeOfRawData);
        }
        return image;
    }

#ifndef CHAPTER_TWO_INVENTORY_BASELINE
    bool Write(std::uintptr_t address, const void* bytes, std::size_t size)
    {
        ++Writes;
        if (FailWrite == 1 && Writes == 1) return false;
        std::memcpy(reinterpret_cast<void*>(address), bytes, size);
        return !(FailWrite == 2 && Writes == 1);
    }

    void CheckInstallation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::inventory_tab_detail::InstallImage;
        auto image = original;
        Writes = FailWrite = 0;
        Check(!InstallImage(nullptr, ImageSize, &Write) && !InstallImage(image.data(), ImageSize - 1, &Write)
            && !InstallImage(image.data(), ImageSize, nullptr) && Writes == 0,
            "invalid image, extent and writer are rejected without writes");
        for (const auto offset : { Selector, Selector + 0x25, Store, std::size_t{0x6e2df}, std::size_t{0x70179} })
        {
            image = original;
            image[offset] ^= 1;
            const auto before = image;
            Writes = 0;
            Check(!InstallImage(image.data(), image.size(), &Write) && Writes == 0 && image == before,
                "selector, jump table and automatic-caller mismatches make zero writes");
        }
        for (const auto mode : { 1u, 2u })
        {
            image = original;
            Writes = 0; FailWrite = mode;
            Check(!InstallImage(image.data(), image.size(), &Write) && image == original && Writes == 2,
                "writer failure before or after writing restores the original store");
        }
        image = original;
        Writes = FailWrite = 0;
        Check(InstallImage(image.data(), image.size(), &Write) && Writes == 1,
            "a verified image receives one three-byte selector patch");
        unsigned differences = 0;
        for (std::size_t i = 0; i < image.size(); ++i)
            if (image[i] != original[i])
            {
                ++differences;
                Check(i >= Store && i < Store + 3 && image[i] == 0x90,
                    "the only changed bytes are the automatic selector's tab store");
            }
        Check(differences == 3, "manual input, inventory synchronization and selector return stay byte-identical");
        Writes = 0;
        Check(InstallImage(image.data(), image.size(), &Write) && Writes == 0,
            "reinstalling the verified patch does not write again");
    }
#endif

    struct Window
    {
        unsigned char prefix[0x40]{};
        int selected = 4;
    };
    static_assert(offsetof(Window, selected) == 0x40);
    struct Model
    {
        unsigned char prefix[0x20]{};
        void** slots = nullptr;
        int space = 0, capacity = 16;
        unsigned updates = 0;
    };
    static_assert(offsetof(Model, slots) == 0x20);
    struct Item
    {
        unsigned char bytes[0xbc]{};
    };
    struct Button
    {
        unsigned char prefix[12]{};
        unsigned id = 0;
        unsigned char padding = 0, active = 1;
    };
    static_assert(offsetof(Button, active) == 0x11);
    Window Ui;
    Model Inventory;
    Item Loot;
    std::array<void*, 16> Slots{};
    std::array<unsigned char, 0x84> UiManager{};
    std::array<unsigned char, 0x4c> ProtectionManager{};
    std::array<std::uint32_t, 1> MirrorA{}, MirrorB{};
    unsigned LookupCalls = 0, OwnerCalls = 0, Sounds = 0;
    int CurrentSlot = 9;
    unsigned char LookupManager = 0;
    constexpr std::uint32_t Key = 0x12345678;

    int __fastcall Lookup(void* manager, void*, void* item)
    {
        ++LookupCalls;
        Check(manager == &LookupManager && item == &Loot, "native selector forwards its manager and item unchanged");
        return CurrentSlot;
    }
    int Amount()
    {
        return static_cast<int>(MirrorA[0] ^ static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(MirrorA.data())) ^ Key);
    }
    void SetAmount(int amount)
    {
        MirrorA[0] = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(MirrorA.data())) ^ Key ^ static_cast<unsigned>(amount);
        MirrorB[0] = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(MirrorB.data())) ^ Key ^ static_cast<unsigned>(amount);
    }
    int __fastcall ReadProtected(void* value, void*)
    {
        if (value == &Inventory) return Inventory.space;
        if (value == Inventory.prefix + 0x10) return Inventory.capacity;
        if (value == Loot.bytes + 0xb4) return Amount();
        Check(false, "native callers only read the expected model and protected amount");
        return -1;
    }
    void __fastcall Refresh(Model* model, void*)
    {
        Check(model == &Inventory, "native inventory refresh retains the model receiver");
        ++model->updates;
    }
    void __fastcall SetOwner(Item* item, void*, Model* model)
    {
        Check(item == &Loot && model == &Inventory, "native insertion retains item ownership arguments");
        At<Model*>(reinterpret_cast<std::uintptr_t>(item->bytes + 0xb0)) = model;
        ++OwnerCalls;
    }
    void __cdecl Sound(const char* name, int channel, int a, int b)
    {
        Check(reinterpret_cast<std::uintptr_t>(name) == 0xaf6c08 && channel == -1 && a == 0 && b == 0,
            "native manual button path retains its original sound request");
        ++Sounds;
    }
    void Jump(std::uintptr_t from, const void* to)
    {
        At<unsigned char>(from) = 0xe9;
        At<std::uint32_t>(from + 1) = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(to) - from - 5);
    }
    void PrepareDependencies()
    {
        Inventory.slots = Slots.data();
        At<void*>(Base + 0x956d28) = &LookupManager;
        At<void*>(Base + 0x956d24) = UiManager.data();
        At<Window*>(reinterpret_cast<std::uintptr_t>(UiManager.data() + 0x80)) = &Ui;
        At<void*>(Base + 0xb35af0) = ProtectionManager.data();
        At<std::uint32_t>(Base + 0xb35af4) = Key;
        At<void*>(reinterpret_cast<std::uintptr_t>(ProtectionManager.data() + 0x44)) = MirrorA.data();
        At<void*>(reinterpret_cast<std::uintptr_t>(ProtectionManager.data() + 0x48)) = MirrorB.data();
        static void* itemMethods[9]{};
        itemMethods[8] = reinterpret_cast<void*>(&SetOwner);
        At<void*>(reinterpret_cast<std::uintptr_t>(Loot.bytes)) = itemMethods;
        Jump(0x46f590, reinterpret_cast<void*>(&Lookup));
        Jump(0xa46ab0, reinterpret_cast<void*>(&ReadProtected));
        Jump(0x46fcd0, reinterpret_cast<void*>(&Refresh));
        Jump(0x43c160, reinterpret_cast<void*>(&Sound));
        // Stop after the real button loop, native store and sound call. The
        // caller's later UI painting is outside this bounded input fixture.
        At<unsigned char>(0x878b7b) = 0x5b;
        At<unsigned char>(0x878b7c) = 0xc3;
        FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(Base), ImageSize);
    }

    void* Entry = nullptr;
    unsigned BeforeEsp = 0, AfterEsp = 0, AfterEax = 0, AfterFlags = 0;
    unsigned AfterEbp = 0, AfterEbx = 0, AfterEsi = 0, AfterEdi = 0;
    void* WindowPointer = &Ui;
    void* ItemPointer = &Loot;
    __declspec(naked) void RunSelector()
    {
        __asm
        {
            pushfd
            pushad
            mov BeforeEsp, esp
            mov ecx, WindowPointer
            mov ebp, 11223344h
            mov ebx, 22334455h
            mov esi, 33445566h
            mov edi, 44556677h
            push ItemPointer
            call Entry
            mov AfterEsp, esp
            mov AfterEax, eax
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
    unsigned CapturedButton = 0;
    __declspec(naked) void RunManual()
    {
        __asm
        {
            pushad
            mov edi, WindowPointer
            mov esi, CapturedButton
            call Entry
            popad
            ret
        }
    }
    std::array<Button, 4> Buttons{};
    void CheckManualInput()
    {
        for (unsigned i = 0; i < Buttons.size(); ++i)
        {
            Buttons[i].id = 100 + i;
            At<Button*>(reinterpret_cast<std::uintptr_t>(Ui.prefix + 0x1c + i * 4)) = &Buttons[i];
        }
        Entry = reinterpret_cast<void*>(0x878b40);
        for (unsigned i = 0; i < Buttons.size(); ++i)
        {
            CapturedButton = Buttons[i].id;
            Ui.selected = 4;
            const auto before = Sounds;
            RunManual();
            Check(Ui.selected == static_cast<int>(i) && Sounds == before + 1,
                "the actual native clicked-button path still selects every backpack tab");
        }
        Ui.selected = 2;
        CapturedButton = 999;
        RunManual();
        Check(Ui.selected == 2, "an unrelated capture does not select another backpack tab");
    }

    void CheckNativeUpdates()
    {
        Entry = reinterpret_cast<void*>(Base + Selector);
        for (const auto slot : { -1, 0, 8, 9, 40, 41, 72, 73, 104, 105, 136, 137, 168 })
        {
            CurrentSlot = slot;
            Ui.selected = 4;
            RunSelector();
            Check(Ui.selected == 4, "actual automatic selector retains the user-selected tab at every slot boundary");
            Check(static_cast<int>(AfterEax) == slot && BeforeEsp == AfterEsp && AfterEbp == 0x11223344
                && AfterEbx == 0x22334455 && AfterEsi == 0x33445566 && AfterEdi == 0x44556677,
                "actual selector retains its slot return, ret4 stack balance and callee-saved registers");
        }
        using Insert = unsigned char (__thiscall*)(Model*, unsigned, Item*, unsigned);
        const auto insert = reinterpret_cast<Insert>(0x470140);
        for (const auto space : { 0, 1, 2, 7 })
        {
            Inventory.space = space;
            for (const auto automatic : { 0u, 1u })
            {
                Ui.selected = 2;
                CurrentSlot = 9;
                const auto before = Inventory.updates, owners = OwnerCalls, lookups = LookupCalls;
                Check(insert(&Inventory, 3, &Loot, automatic) == 1 && Slots[3] == &Loot
                    && At<Model*>(reinterpret_cast<std::uintptr_t>(Loot.bytes + 0xb0)) == &Inventory
                    && Inventory.updates == before + 1 && OwnerCalls == owners + 1,
                    "actual insertion preserves slot data, item ownership, refresh and success");
                Check(Ui.selected == 2 && LookupCalls == lookups + (space == 0 && automatic == 1 ? 1u : 0u),
                    "actual insertion retains tab selection and native automatic-selection gates");
            }
        }
        const auto beforeClear = Inventory.updates;
        Check(insert(&Inventory, 3, nullptr, 1) == 1 && Slots[3] == nullptr && Inventory.updates == beforeClear + 1,
            "actual empty-slot synchronization continues to clear and refresh the model");
        const auto beforeInvalid = Inventory.updates;
        Check(insert(&Inventory, 16, &Loot, 1) == 0 && Inventory.updates == beforeInvalid,
            "native out-of-range insertion still fails before changing the model");

        using ChangeAmount = void (__thiscall*)(Item*, unsigned);
        const auto changeAmount = reinterpret_cast<ChangeAmount>(0x46e2b0);
        for (const auto space : { 0, 1 })
        {
            Inventory.space = space;
            for (const auto amount : { 3, 5, 10 })
            {
                SetAmount(5);
                Ui.selected = 3;
                CurrentSlot = 9;
                At<Model*>(reinterpret_cast<std::uintptr_t>(Loot.bytes + 0xb0)) = &Inventory;
                At<unsigned>(reinterpret_cast<std::uintptr_t>(Loot.bytes + 0xb8)) = 0;
                const auto before = Inventory.updates, lookups = LookupCalls;
                changeAmount(&Loot, static_cast<unsigned>(amount));
                const auto second = MirrorB[0] ^ static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(MirrorB.data())) ^ Key;
                Check(Amount() == amount && second == static_cast<unsigned>(amount) && Inventory.updates == before + 1,
                    "actual amount update retains both protected mirrors and the model refresh");
                Check(Ui.selected == 3 && LookupCalls == lookups + (space == 0 && amount > 5 ? 1u : 0u),
                    "actual amount update retains the tab and native increase-only inventory gate");
            }
        }
        CheckManualInput();
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto image = ReadImage(argv[1]);
    Check(image.size() == ImageSize, "the supplied Chapter Two PE maps with its original extent and preferred base");
    if (image.empty()) return 2;
#ifndef CHAPTER_TWO_INVENTORY_BASELINE
    CheckInstallation(image);
#endif
    auto* mapped = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base), ImageSize,
        MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    const bool allocated = mapped != nullptr;
    if (!mapped)
    {
        const auto begin = reinterpret_cast<std::uintptr_t>(inventoryFixtureReserve);
        DWORD protection = 0;
        if (begin <= Base && Base + ImageSize <= begin + sizeof(inventoryFixtureReserve)
            && VirtualProtect(reinterpret_cast<void*>(Base), ImageSize, PAGE_EXECUTE_READWRITE, &protection))
            mapped = reinterpret_cast<unsigned char*>(Base);
    }
    Check(mapped == reinterpret_cast<void*>(Base), "actual native functions map at their required preferred base");
    if (!mapped) return 2;
    std::memcpy(mapped, image.data(), image.size());
#ifndef CHAPTER_TWO_INVENTORY_BASELINE
    Check(dfl::chapter_two::InstallInventoryTabPreservation(mapped, ImageSize),
        "production writer installs on the actual mapped Chapter Two image");
#endif
    PrepareDependencies();
    CheckNativeUpdates();
    if (allocated) VirtualFree(mapped, 0, MEM_RELEASE);
    std::printf("Chapter Two inventory: %u checks, %d failures\n", Checks, Failures);
    return Failures == 0 ? 0 : 1;
}
