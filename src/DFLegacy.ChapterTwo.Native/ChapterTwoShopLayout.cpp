#include "ChapterTwoShopLayout.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <cstring>
#include <limits>

namespace {
constexpr std::size_t ImageSize=0xe57000,Table=0x818c10,ItemX=16;
// Original six (x,y) entries, including the undefined group4 sentinel.
// Chinese item frame12 extends 59 pixels from x+8, overlapping the creature
// frame at x448+7. Move only the item origin from 393 to 383.
constexpr unsigned char Positions[]={
    0xdd,0x00,0x00,0x00,0x08,0x00,0x00,0x00,
    0x33,0x01,0x00,0x00,0x08,0x00,0x00,0x00,
    0x89,0x01,0x00,0x00,0x08,0x00,0x00,0x00,
    0xc0,0x01,0x00,0x00,0x08,0x00,0x00,0x00,
    0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
    0x29,0x02,0x00,0x00,0x08,0x00,0x00,0x00
};
// Native constructor 946F8E..946FB3 reads this table and passes (x,y) to
// A3C600, the common position setter used by rendering and input.
constexpr unsigned char Constructor[]={
    0x8b,0x84,0xbe,0x18,0x25,0x00,0x00,0x89,0x70,0x44,
    0x8b,0x04,0xfd,0x14,0x8c,0xc1,0x00,0x8b,0x0c,0xfd,
    0x10,0x8c,0xc1,0x00,0x50,0x51,0x8b,0x8c,0xbe,0x18,
    0x25,0x00,0x00,0xe8,0x4c,0x56,0x0f,0x00
};
}

bool dfl::chapter_two::shop_layout_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept
{
    if(!image||bytes!=ImageSize||!write||reinterpret_cast<std::uintptr_t>(image)>std::numeric_limits<std::uintptr_t>::max()-ImageSize)return false;
    if(std::memcmp(image+0x546f8e,Constructor,sizeof(Constructor))||
        std::memcmp(image+Table,Positions,ItemX)||
        std::memcmp(image+Table+ItemX+1,Positions+ItemX+1,sizeof(Positions)-ItemX-1))return false;
    constexpr unsigned char shifted=0x7f;
    if(image[Table+ItemX]==shifted)return true;
    if(image[Table+ItemX]!=Positions[ItemX])return false;
    const auto address=reinterpret_cast<std::uintptr_t>(image+Table+ItemX);
    if(write(address,&shifted,1))return true;
    // WriteBytes may fail after copying when cache/protection restoration fails.
    write(address,Positions+ItemX,1);
    return false;
}

bool dfl::chapter_two::InstallShopLayout(unsigned char* image,std::size_t bytes) noexcept
{
    return image==reinterpret_cast<unsigned char*>(0x400000)&&shop_layout_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
}
