#pragma once
#include "client_state.h"
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::client_navigation
{
    // File ABI: little endian, exactly 96 bytes. Worker IO only.
#pragma pack(push, 1)
    struct Request
    {
        char magic[4] = {'D','L','N','V'};
        std::uint16_t version = 1, size = 96;
        std::uint32_t pid = 0, uid = 0;
        std::uint64_t processStartedFileTime = 0, requestId = 0, issuedTickMs = 0;
        std::uint64_t snapshotTickMs = 0, generation = 0, snapshotSequence = 0;
        std::uint32_t actorToken = 0, mapToken = 0, userToken = 0, contextToken = 0;
        std::int32_t townId = -1, areaId = -1, x = 0, y = 0;
    };
#pragma pack(pop)
    static_assert(sizeof(Request) == 96, "navigation file ABI");
    struct Rect { std::int32_t x = 0, y = 0, width = 0, height = 0; };
    struct RegionRect { std::int32_t town = -1, area = -1; Rect rect{}; };
    constexpr std::size_t MaximumRegions = 256;
    const char* ValidateRequest(const Request&, const client_state::Snapshot&, std::uint64_t now, std::uint64_t consumedId) noexcept;
    bool InverseProject(const Rect& project, const Rect& real, std::int32_t px, std::int32_t py,
                        std::int32_t& x, std::int32_t& y) noexcept;
    bool ReadRegions(const client_state::Reader&, std::uint32_t hash,
                     std::array<RegionRect, MaximumRegions>&, std::size_t& count) noexcept;
    bool BlankMarkers(const client_state::Reader&, std::uint32_t plane, std::int32_t px, std::int32_t py) noexcept;
    bool ReadCanvasTarget(const client_state::Reader&, const client_state::Snapshot&,
                          std::uint32_t minimap, std::int32_t& x, std::int32_t& y) noexcept;
    using PatchOperation = bool (*)(const unsigned char* expected, const unsigned char* replacement) noexcept;
    bool RestoreInstalledHook(bool& installed, const std::array<unsigned char,5>& patched, PatchOperation) noexcept;
    void Initialize(const wchar_t* settingsPath, const wchar_t* directory, client_state::Reader) noexcept;
    void OnFrame() noexcept;
    void OnFrame(const client_state::Snapshot&) noexcept;
    void PumpWorker() noexcept;
    bool Shutdown() noexcept;
}
