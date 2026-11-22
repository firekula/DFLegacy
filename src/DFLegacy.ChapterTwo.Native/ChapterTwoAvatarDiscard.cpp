#include "ChapterTwoAvatarDiscard.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstdint>
#include <cstring>

namespace {
constexpr std::uintptr_t ImageBase = 0x400000;
constexpr std::size_t ImageSize = 0xe57000;
constexpr std::uintptr_t DropBranch = 0x866ee8;
const std::uintptr_t CreatureDropPath = 0x866eee;
const std::uintptr_t FinishDrop = 0x8674ae;
constexpr int DestroyConfirmation = 0x1f;

template<typename T>
T Read(const void* object, std::size_t offset) noexcept {
    return *reinterpret_cast<const T*>(static_cast<const unsigned char*>(object) + offset);
}

void __stdcall ConfirmTownAvatarDiscard(void* manager) noexcept {
    // Tag 7 originates only in the avatar-bag picker. The worn picker emits
    // tag 6, even when both pickers belong to the same visible avatar window.
    const auto* game = *reinterpret_cast<void* const*>(0xd564ac);
    const auto scene = reinterpret_cast<int(__thiscall*)(const void*)>(0xa46ab0);
    if (!game || scene(static_cast<const unsigned char*>(game) + 0x10) != 1) return;

    auto* avatarBag = *reinterpret_cast<void* const*>(0xd56d30);
    const auto slot = Read<int>(manager, 0x1334);
    const auto findSlot = reinterpret_cast<void*(__thiscall*)(void*, int)>(0x404100);
    auto* item = avatarBag ? findSlot(avatarBag, slot) : nullptr;
    if (!item) return;

    const auto* vtable = Read<const void*>(item, 0);
    const auto record = Read<const void*(__thiscall*)(void*)>(vtable, 0x14)(item);
    if (!record || Read<unsigned>(record, 8) != Read<unsigned>(manager, 0x1330)) return;

    // Use the same dialog as ordinary inventory discard. Its confirmation
    // resolves the item's current bag and slot before sending CMD18.
    const auto show = reinterpret_cast<void*(__thiscall*)(void*, int, void*, int)>(0x8657d0);
    show(manager, DestroyConfirmation, item, 1);
}

__declspec(naked) void AvatarDropBranch() {
    __asm {
        je creature
        cmp eax, 7
        jne finish
        push esi
        call ConfirmTownAvatarDiscard
    finish:
        jmp dword ptr [FinishDrop]
    creature:
        jmp dword ptr [CreatureDropPath]
    }
}

template<std::size_t N>
bool Matches(std::uintptr_t address, const std::array<unsigned char, N>& expected) noexcept {
    return std::memcmp(reinterpret_cast<const void*>(address), expected.data(), N) == 0;
}
}

bool dfl::chapter_two::InstallAvatarDiscard(unsigned char* image, std::size_t bytes) noexcept {
    static_assert(sizeof(void*) == 4, "Chapter Two avatar discard requires x86.");
    if (image != reinterpret_cast<unsigned char*>(ImageBase) || bytes != ImageSize) return false;

    constexpr std::array<unsigned char, 3> precedingCompare{0x83, 0xf8, 0x0d};
    constexpr std::array<unsigned char, 9> avatarPicker{0x6a, 0x07, 0x6a, 0x02, 0xe8, 0x64, 0x49, 0xff, 0xff};
    constexpr std::array<unsigned char, 7> nativeConfirm{0x6a, 0x01, 0x57, 0x6a, 0x1f, 0x8b, 0xce};
    constexpr std::array<unsigned char, 4> dropEpilogue{0x5f, 0x5e, 0x5b, 0xc3};
    constexpr std::array<unsigned char, 6> original{0x0f, 0x85, 0xc0, 0x05, 0x00, 0x00};
    if (!Matches(0x866ee5, precedingCompare) || !Matches(0x8701f3, avatarPicker)
        || !Matches(0x867051, nativeConfirm) || !Matches(FinishDrop, dropEpilogue)) return false;

    std::array<unsigned char, 6> replacement{0xe9, 0, 0, 0, 0, 0x90};
    const auto distance = reinterpret_cast<std::uintptr_t>(&AvatarDropBranch) - DropBranch - 5;
    std::memcpy(replacement.data() + 1, &distance, sizeof(distance));
    if (Matches(DropBranch, replacement)) return true;
    if (!Matches(DropBranch, original)) return false;
    if (memory_patch::WriteBytes(DropBranch, replacement.data(), replacement.size())
        && Matches(DropBranch, replacement)) return true;

    memory_patch::WriteBytes(DropBranch, original.data(), original.size());
    return false;
}
