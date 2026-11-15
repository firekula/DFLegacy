#pragma once
#include "../src/d3d8_presenter.h"
#include <array>
#include <cstdio>
#include <vector>
namespace crt_headless
{
namespace
{
namespace a = dfl::d3d8;
struct Resource
{
    void **Table = nullptr;
    unsigned References = 1;
    unsigned Width = 640, Height = 480;
    bool Owned = false;
    std::vector<unsigned char> Pixels;
};
struct Device
{
    void **Table = nullptr;
    Resource *Target = nullptr;
    a::Viewport View{0, 0, 640, 480, 0, 1};
    unsigned ShaderCreates = 0, ShaderDeletes = 0, TextureCreates = 0;
    DWORD Shader = 0;
    unsigned FailDraw = 0, Restores = 0;
    std::vector<DWORD> DrawShaders;
};
std::array<void *, 18> Resources{};
ULONG WINAPI Add(void *p)
{
    return ++static_cast<Resource *>(p)->References;
}
ULONG WINAPI Release(void *p)
{
    auto *r = static_cast<Resource *>(p);
    auto count = --r->References;
    if (!count && r->Owned)
        delete r;
    return count;
}
HRESULT WINAPI Description(void *p, a::SurfaceDescription *d)
{
    auto &r = *static_cast<Resource *>(p);
    *d = {21, 1, 0, 0, 0, 0, r.Width, r.Height};
    return S_OK;
}
HRESULT WINAPI Surface(void *p, UINT, void **out)
{
    *out = p;
    Add(p);
    return S_OK;
}
HRESULT WINAPI Lock(void *p, UINT, a::LockedRect *lock, const RECT *, DWORD)
{
    auto &r = *static_cast<Resource *>(p);
    r.Pixels.resize(r.Width * r.Height * 4);
    lock->Pitch = r.Width * 4;
    lock->Bits = r.Pixels.data();
    return S_OK;
}
HRESULT WINAPI Unlock(void *, UINT)
{
    return S_OK;
}
HRESULT WINAPI Texture(void *p, UINT w, UINT h, UINT, DWORD, DWORD, DWORD, void **out)
{
    auto *r = new Resource;
    r->Table = Resources.data();
    r->Width = w;
    r->Height = h;
    r->Owned = true;
    *out = r;
    ++static_cast<Device *>(p)->TextureCreates;
    return S_OK;
}
HRESULT WINAPI Copy(void *, void *, const RECT *, UINT, void *, const POINT *)
{
    return S_OK;
}
HRESULT WINAPI SetTarget(void *p, void *target, void *)
{
    static_cast<Device *>(p)->Target = static_cast<Resource *>(target);
    return S_OK;
}
HRESULT WINAPI GetTarget(void *p, void **out)
{
    *out = static_cast<Device *>(p)->Target;
    Add(*out);
    return S_OK;
}
HRESULT WINAPI GetDepth(void *, void **out)
{
    *out = nullptr;
    return a::NotFound;
}
HRESULT WINAPI SetView(void *p, const a::Viewport *view)
{
    static_cast<Device *>(p)->View = *view;
    return S_OK;
}
HRESULT WINAPI GetView(void *p, a::Viewport *view)
{
    *view = static_cast<Device *>(p)->View;
    return S_OK;
}
HRESULT WINAPI Simple(void *)
{
    return S_OK;
}
HRESULT WINAPI Pair(void *, DWORD, DWORD)
{
    return S_OK;
}
HRESULT WINAPI Triple(void *, DWORD, DWORD, DWORD)
{
    return S_OK;
}
HRESULT WINAPI Single(void *, DWORD)
{
    return S_OK;
}
HRESULT WINAPI SetTexture(void *, DWORD, void *)
{
    return S_OK;
}
HRESULT WINAPI GetStream(void *, UINT, void **out, UINT *stride)
{
    *out = nullptr;
    *stride = 0;
    return S_OK;
}
HRESULT WINAPI GetIndices(void *, void **out, UINT *base)
{
    *out = nullptr;
    *base = 0;
    return S_OK;
}
HRESULT WINAPI SetStream(void *, UINT, void *, UINT)
{
    return S_OK;
}
HRESULT WINAPI SetIndices(void *, void *, UINT)
{
    return S_OK;
}
HRESULT WINAPI CreateBlock(void *, DWORD, DWORD *out)
{
    *out = 1;
    return S_OK;
}
HRESULT WINAPI RestoreBlock(void *p, DWORD)
{
    ++static_cast<Device *>(p)->Restores;
    return S_OK;
}
HRESULT WINAPI CreateShader(void *p, const DWORD *, DWORD *out)
{
    *out = ++static_cast<Device *>(p)->ShaderCreates;
    return S_OK;
}
HRESULT WINAPI DeleteShader(void *p, DWORD)
{
    ++static_cast<Device *>(p)->ShaderDeletes;
    return S_OK;
}
HRESULT WINAPI SetShader(void *p, DWORD shader)
{
    static_cast<Device *>(p)->Shader = shader;
    return S_OK;
}
HRESULT WINAPI Constants(void *, DWORD, const void *, DWORD)
{
    return S_OK;
}
HRESULT WINAPI Draw(void *p, DWORD, UINT, const void *, UINT)
{
    auto &d = *static_cast<Device *>(p);
    d.DrawShaders.push_back(d.Shader);
    return d.FailDraw && d.DrawShaders.size() == d.FailDraw ? E_FAIL : S_OK;
}
unsigned Checks = 0, Failures = 0;
void Check(bool value, const char *label)
{
    ++Checks;
    if (!value)
    {
        ++Failures;
        std::fprintf(stderr, "FAIL CRT headless: %s\n", label);
    }
}
} // namespace
inline int Run()
{
    Resources[1] = reinterpret_cast<void *>(&Add);
    Resources[2] = reinterpret_cast<void *>(&Release);
    Resources[8] = reinterpret_cast<void *>(&Description);
    Resources[15] = reinterpret_cast<void *>(&Surface);
    Resources[16] = reinterpret_cast<void *>(&Lock);
    Resources[17] = reinterpret_cast<void *>(&Unlock);
    std::array<void *, a::DeviceMethods> table{};
    table[20] = reinterpret_cast<void *>(&Texture);
    table[28] = reinterpret_cast<void *>(&Copy);
    table[31] = reinterpret_cast<void *>(&SetTarget);
    table[32] = reinterpret_cast<void *>(&GetTarget);
    table[33] = reinterpret_cast<void *>(&GetDepth);
    table[34] = reinterpret_cast<void *>(&Simple);
    table[35] = reinterpret_cast<void *>(&Simple);
    table[40] = reinterpret_cast<void *>(&SetView);
    table[41] = reinterpret_cast<void *>(&GetView);
    table[50] = reinterpret_cast<void *>(&Pair);
    table[54] = reinterpret_cast<void *>(&RestoreBlock);
    table[56] = reinterpret_cast<void *>(&Single);
    table[57] = reinterpret_cast<void *>(&CreateBlock);
    table[61] = reinterpret_cast<void *>(&SetTexture);
    table[63] = reinterpret_cast<void *>(&Triple);
    table[72] = reinterpret_cast<void *>(&Draw);
    table[76] = reinterpret_cast<void *>(&Single);
    table[83] = reinterpret_cast<void *>(&SetStream);
    table[84] = reinterpret_cast<void *>(&GetStream);
    table[85] = reinterpret_cast<void *>(&SetIndices);
    table[86] = reinterpret_cast<void *>(&GetIndices);
    table[87] = reinterpret_cast<void *>(&CreateShader);
    table[88] = reinterpret_cast<void *>(&SetShader);
    table[90] = reinterpret_cast<void *>(&DeleteShader);
    table[91] = reinterpret_cast<void *>(&Constants);
    Resource input, target;
    input.Table = target.Table = Resources.data();
    Device device;
    device.Table = table.data();
    device.Target = &target;
    auto plan =
        dfl::presentation::MakePlan(640, 480, dfl::presentation::Mode::Point, {4096, 4096, false, false});
    dfl::d3d8_presenter::Presenter renderer(&device, table.data());
    const auto first = renderer.Render(&input, &target, plan, dfl::presentation::PostProcess::PhosphorLine);
    Check(SUCCEEDED(first.Status) && SUCCEEDED(first.RestoreStatus), "CRT frame succeeds");
    Check(device.ShaderCreates == 3, "bright-pass, horizontal blur and composite use separate shaders");
    Check(device.DrawShaders == std::vector<DWORD>{1, 2, 2, 2, 3},
          "bright-pass, bloom and narrow base reconstruction precede the composite");
    Check(device.TextureCreates == 4, "source, two bloom render targets and mask are allocated once");
    Check(device.Restores == 1, "all passes share one captured state restoration");
    auto textures = device.TextureCreates;
    auto shaders = device.ShaderCreates;
    device.DrawShaders.clear();
    Check(SUCCEEDED(
              renderer.Render(&input, &target, plan, dfl::presentation::PostProcess::PhosphorLine).Status) &&
              device.TextureCreates == textures && device.ShaderCreates == shaders,
          "steady frames reuse shaders and targets");
    renderer.Clear();
    Check(device.ShaderDeletes == 3, "Clear releases every bloom shader");
    device.DrawShaders.clear();
    device.FailDraw = 2;
    auto failed = renderer.Render(&input, &target, plan, dfl::presentation::PostProcess::PhosphorLine);
    Check(FAILED(failed.Status) && SUCCEEDED(failed.RestoreStatus),
          "a failed bloom pass restores the incoming state");
    device.FailDraw = 0;
    Check(SUCCEEDED(renderer.Render(&input, &target, plan).Status),
          "plain rendering remains available after a failed bloom pass");
    std::printf("CRT headless: %u checks, %u failures\n", Checks, Failures);
    return Failures ? 1 : 0;
}
} // namespace crt_headless
