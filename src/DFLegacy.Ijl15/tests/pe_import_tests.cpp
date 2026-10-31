#include "../src/pe_import.h"
#include <array>
#include <cstdio>
#include <cstring>

int main()
{
    std::array<unsigned char,0x4000> bytes{};auto* image=bytes.data();
    auto* dos=reinterpret_cast<IMAGE_DOS_HEADER*>(image);dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=0x100;
    auto* nt=reinterpret_cast<IMAGE_NT_HEADERS32*>(image+0x100);nt->Signature=IMAGE_NT_SIGNATURE;nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR32_MAGIC;
    nt->OptionalHeader.NumberOfRvaAndSizes=16;nt->OptionalHeader.SizeOfImage=bytes.size();
    auto& directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];directory={0x400,40};
    auto* descriptor=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image+0x400);descriptor->Name=0x500;descriptor->OriginalFirstThunk=0x700;descriptor->FirstThunk=0x600;
    std::memcpy(image+0x500,"D3D8.DLL",9);std::memcpy(image+0x802,"Direct3DCreate8",15);
    auto* named=reinterpret_cast<DWORD*>(image+0x700);*named=0x800;
    auto** slot=reinterpret_cast<void**>(image+0x600);*slot=reinterpret_cast<void*>(0x12345678);
    // Reconstructed ACT1 imports moved, leaving an unused old slot with a plausible function value.
    *reinterpret_cast<DWORD*>(image+0x1200)=0x12345678;
    int failed=0;const auto check=[&](bool value,const char* why){if(!value){++failed;std::fprintf(stderr,"FAIL: %s\n",why);}};
    const auto find=[&](){return dfl::pe_import::FindNamed(image,bytes.size(),"d3d8.dll","Direct3DCreate8");};
    check(find()==slot,"resolve actual named import rather than stale historical IAT address");
    *named=IMAGE_ORDINAL_FLAG32|1;check(find()==nullptr,"unresolved ordinal import is rejected");*named=0x800;
    descriptor->OriginalFirstThunk=0;check(find()==nullptr,"resolved unnamed thunk is not guessed");descriptor->OriginalFirstThunk=0x700;
    descriptor->OriginalFirstThunk=0x5000;check(find()==nullptr,"thunk RVA outside mapped image rejected");descriptor->OriginalFirstThunk=0x700;
    *named=0x3ffe;std::memcpy(image+0x3ffe,"xx",2);check(find()==nullptr,"unterminated import name cannot overread image");*named=0x800;
    directory.Size=0xffffffff;check(find()==nullptr,"directory extent overflow rejected");directory.Size=40;
    descriptor->FirstThunk=0x3fff;check(find()==nullptr,"partial pointer at image boundary rejected");descriptor->FirstThunk=0x600;
    nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR64_MAGIC;check(find()==nullptr,"64-bit image rejected by x86 adapter");nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR32_MAGIC;
    dos->e_lfanew=-1;check(find()==nullptr,"negative NT header offset rejected");dos->e_lfanew=0x100;
    check(dfl::pe_import::FindNamed(image,bytes.size(),"d3d8.dll","OtherFunction")==nullptr,"unrelated symbol never patched");
    std::printf("Bounded named PE import checks: %d failures\n",failed);return failed?1:0;
}
