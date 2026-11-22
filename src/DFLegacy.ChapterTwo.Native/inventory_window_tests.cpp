#include "ChapterTwoInventoryWindow.h"
#include <algorithm>
#include <array>
#include <cassert>
#include <cstdio>
#include <cstdlib>

using namespace dfl::chapter_two::inventory_window;

static void Check(bool ok)
{
    if (!ok) std::abort();
}

int main()
{
    Request request{Operation::Read, Section::Avatar, 999971, 7};
    const auto moveBody = EncodeArguments(request);
    constexpr std::array<unsigned char, 18> expected{
        255, 255, 255, 'D', 'F', 'I', 'V', 1, 1, 4, 0x23, 0x42, 0x0f, 0, 7, 0, 0, 0};
    Check(moveBody == expected);
    std::array<unsigned char, 64> payload{255,'D','F','I','V',1,1,7,0,0,0,4,0x23,0x42,0x0f,0,
        0x3f,0x42,0x0f,0,1,0,0,0,0,0,0,0,1,27,0x59,0x98,1};
    Page page{};
    Check(DecodePage(payload.data(), payload.size(), request, page));
    Check(page.count == 1 && page.cells[0].itemId == 39001 && page.cells[0].offset == 27 && page.revision == 1);
    Check(!DecodePage(payload.data(), payload.size()-1, request, page));
    auto stale = request;
    ++stale.id;
    Check(!DecodePage(payload.data(), payload.size(), stale, page));
    payload[29] = 28;
    Check(!DecodePage(payload.data(), payload.size(), request, page));
    std::puts("inventory window native request, page and stale-response checks passed");
}
