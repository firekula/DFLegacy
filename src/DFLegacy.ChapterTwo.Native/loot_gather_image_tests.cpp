#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoLootGather.h"
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
namespace {
constexpr std::uintptr_t Base=0x400000;
constexpr std::size_t ImageSize=0xe57000;
unsigned Failures=0,Writes=0,FailureMode=0;
void Check(bool ok,const char* text) {if(!ok){++Failures;std::fprintf(stderr,"FAIL: %s\n",text);}}
bool Write(std::uintptr_t address,const void* value,std::size_t size) noexcept {
 ++Writes;
 if(FailureMode==1&&Writes==1)return false;
 std::memcpy(reinterpret_cast<void*>(address),value,size);
 return !(FailureMode==2&&Writes==1);
}
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

}
int wmain(int argc,wchar_t** argv) {
 if(argc!=2)return 2;
 const auto original=ReadImage(argv[1]);if(original.empty())return 2;
 using dfl::chapter_two::loot_gather_detail::InstallImage;
 auto image=original;
 Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid input must not write");
 for(const std::size_t offset:{0x5272d0,0x527326,0x527381,0x527358,0x83f30,0x6ae0,0x2f4500}) {
  image=original;image[offset]^=1;const auto before=image;Writes=0;
  Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"mutated original signature fails closed");
 }
 for(unsigned mode:{1U,2U}) {
  image=original;Writes=0;FailureMode=mode;
  Check(!InstallImage(image.data(),image.size(),Write)&&Writes==2&&image==original,"failed write restores original prologue");
 }
 image=original;Writes=FailureMode=0;
 Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"verified C2 image accepts chat hook");
 const auto patched=image;
 Check(InstallImage(image.data(),image.size(),Write)&&Writes==1&&image==patched,"install is idempotent");
 image[0x527381]^=1;const auto bad=image;
 Check(!InstallImage(image.data(),image.size(),Write)&&Writes==1&&image==bad,"reinstall rechecks remaining signatures");
 std::printf("loot_gather_image_tests: %u failures\n",Failures);
 return Failures?1:0;
}
