#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoBackgroundPolicy.h"
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    unsigned Checks = 0, Failures = 0, Writes = 0, FailAt = 0;
    bool FailAfterCopy = false;
    using Installer = bool (*)(unsigned char*, std::size_t,
        dfl::chapter_two::background_policy_detail::PatchWriter) noexcept;

    void Check(bool value, const char* message)
    {
        ++Checks;
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL background policy: %s\n", message); }
    }

    bool Write(std::uintptr_t address, const void* bytes, std::size_t count)
    {
        ++Writes;
        if (Writes == FailAt && !FailAfterCopy) return false;
        std::memcpy(reinterpret_cast<void*>(address), bytes, count);
        return Writes != FailAt;
    }

    std::vector<unsigned char> ReadImage(const char* path)
    {
        std::ifstream input(path, std::ios::binary);
        const std::vector<unsigned char> file((std::istreambuf_iterator<char>(input)), {});
        const auto fits = [](std::size_t at, std::size_t size, std::size_t limit)
        { return at <= limit && size <= limit - at; };
        if (file.size() < sizeof(IMAGE_DOS_HEADER)) return {};
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
            !fits(dos->e_lfanew, sizeof(IMAGE_NT_HEADERS32), file.size())) return {};
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data() + dos->e_lfanew);
        const auto* sections = IMAGE_FIRST_SECTION(nt);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt->OptionalHeader.ImageBase != 0x400000 || nt->OptionalHeader.SizeOfImage != ImageSize ||
            !fits(reinterpret_cast<const unsigned char*>(sections) - file.data(),
                sizeof(*sections) * nt->FileHeader.NumberOfSections, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, file.size()) ||
            !fits(0, nt->OptionalHeader.SizeOfHeaders, ImageSize)) return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(), file.data(), nt->OptionalHeader.SizeOfHeaders);
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i)
        {
            const auto& section = sections[i];
            if (!fits(section.VirtualAddress, section.SizeOfRawData, image.size()) ||
                !fits(section.PointerToRawData, section.SizeOfRawData, file.size())) return {};
            std::memcpy(image.data() + section.VirtualAddress,
                file.data() + section.PointerToRawData, section.SizeOfRawData);
        }
        return image;
    }

    void VerifyInstaller(Installer install, const std::vector<unsigned char>& original,
        std::size_t first, std::size_t second, std::initializer_list<std::size_t> contexts)
    {
        auto image = original;
        Writes = FailAt = 0;
        Check(!install(nullptr, ImageSize, Write) && !install(image.data(), ImageSize - 1, Write) &&
            !install(image.data(), ImageSize, nullptr) && Writes == 0, "invalid inputs make no writes");
        for (auto offset : contexts)
        {
            image = original;
            image[offset] ^= 1;
            const auto before = image;
            Writes = 0;
            Check(!install(image.data(), image.size(), Write) && Writes == 0 && image == before,
                "mismatched native context fails before any write");
        }
        for (unsigned fail = 1; fail <= 2; ++fail)
            for (bool after : {false, true})
            {
                image = original;
                Writes = 0; FailAt = fail; FailAfterCopy = after;
                Check(!install(image.data(), image.size(), Write) && image == original,
                    "failure before or after either write restores both original spans");
            }
        image = original;
        Writes = FailAt = 0;
        Check(install(image.data(), image.size(), Write) && Writes == 2,
            "verified policy installs two small edits");
        unsigned differences = 0;
        bool onlyPolicyEdits = true;
        for (std::size_t i = 0; i < image.size(); ++i)
            if (image[i] != original[i])
            {
                ++differences;
                if (i != first && i != second && i != second + 1) onlyPolicyEdits = false;
            }
        Check(onlyPolicyEdits && differences == 3, "only three policy bytes change");
        Writes = 0;
        Check(install(image.data(), image.size(), Write) && Writes == 0, "reinstall is a no-write success");
        image[first] = original[first];
        const auto partial = image;
        Check(!install(image.data(), image.size(), Write) && Writes == 0 && image == partial,
            "partially installed policy is rejected without edits");
    }

    void Save(const std::string& path, const std::vector<unsigned char>& image)
    {
        std::ofstream output(path, std::ios::binary);
        output.write(reinterpret_cast<const char*>(image.data()), image.size());
        Check(output.good(), "mapped image exported for independent x86 execution");
    }
}

int main(int argc, char** argv)
{
    if (argc < 2) return 2;
    const auto original = ReadImage(argv[1]);
    Check(original.size() == ImageSize, "original Chapter Two PE maps successfully");
    if (original.empty()) return 2;
    using namespace dfl::chapter_two::background_policy_detail;
    VerifyInstaller(InstallBackground, original, 0x63ceed, 0x63d38e,
        {0x63cee8,0x63ceed,0x63ceef,0x63cef3,0x63cef9,0x63cf21,
         0x63d386,0x63d38b,0x63d38e,0x63d390,0x63d3bd,0x63d3c4});
    VerifyInstaller(InstallStartup, original, 0xd47e, 0x6e0f80,
        {0xd470,0xd47e,0xd481,0xd486,0xd4b2,0x6e0f80,0x6e0f87,0x6e0f8c,
         0x6e6d90,0x6eb86c,0x8b2088});
    auto image = original;
    Check(dfl::chapter_two::InstallBackgroundPolicy(image.data(), image.size()),
        "production protected-memory writer installs background policy");
    if (argc >= 3) Save(std::string(argv[2]) + "-background.bin", image);
    Check(dfl::chapter_two::InstallStartupWindowPolicy(image.data(), image.size()),
        "production protected-memory writer installs independent startup policy");
    if (argc >= 3) Save(std::string(argv[2]) + "-all.bin", image);
    auto startupOnly = original;
    Check(dfl::chapter_two::InstallStartupWindowPolicy(startupOnly.data(), startupOnly.size()),
        "startup can install without background policy");
    if (argc >= 3) Save(std::string(argv[2]) + "-startup.bin", startupOnly);
    std::printf("Chapter Two background/startup: %u checks, %u failures\n", Checks, Failures);
    return Failures ? 1 : 0;
}
