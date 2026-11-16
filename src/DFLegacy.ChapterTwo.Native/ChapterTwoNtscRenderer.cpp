#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "ChapterTwoNtscRenderer.h"
#include "ChapterTwoNtscBytecode.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <cstring>
#include <cwchar>
#include <limits>
#include <new>
#include <utility>

namespace
{
    using Microsoft::WRL::ComPtr;
    constexpr UINT Width = 640;
    constexpr UINT Height = 480;
    constexpr std::size_t RowBytes = Width * 4;
    constexpr DXGI_FORMAT Bgra = DXGI_FORMAT_B8G8R8A8_UNORM;

    struct SystemLibrary
    {
        HMODULE Handle = nullptr;
        SystemLibrary() = default;
        SystemLibrary(const SystemLibrary&) = delete;
        SystemLibrary& operator=(const SystemLibrary&) = delete;

        ~SystemLibrary()
        {
            if (Handle) FreeLibrary(Handle);
        }

        HRESULT Load(const wchar_t* name) noexcept
        {
            wchar_t path[MAX_PATH]{};
            const auto length = GetSystemDirectoryW(path, MAX_PATH);
            if (!length) return HRESULT_FROM_WIN32(GetLastError());
            const auto nameLength = std::wcslen(name);
            if (length >= MAX_PATH || length + 1 + nameLength >= MAX_PATH)
                return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
            path[length] = L'\\';
            std::memcpy(path + length + 1, name, (nameLength + 1) * sizeof(wchar_t));
            Handle = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            return Handle ? S_OK : HRESULT_FROM_WIN32(GetLastError());
        }
    };

    bool ValidPitch(std::size_t pitch) noexcept
    {
        return pitch >= RowBytes && pitch <= (std::numeric_limits<std::size_t>::max() - RowBytes) / (Height - 1);
    }

    void CopyRows(void* destination, std::size_t destinationPitch, const void* source, std::size_t sourcePitch) noexcept
    {
        auto* target = static_cast<unsigned char*>(destination);
        const auto* input = static_cast<const unsigned char*>(source);
        for (UINT y = 0; y < Height; ++y)
            std::memcpy(target + y * destinationPitch, input + y * sourcePitch, RowBytes);
    }

    D3D11_TEXTURE2D_DESC TextureDescription(UINT width, DXGI_FORMAT format, D3D11_USAGE usage,
        UINT bindings, UINT access = 0) noexcept
    {
        D3D11_TEXTURE2D_DESC description{};
        description.Width = width;
        description.Height = Height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = format;
        description.SampleDesc.Count = 1;
        description.Usage = usage;
        description.BindFlags = bindings;
        description.CPUAccessFlags = access;
        return description;
    }

    struct RenderTarget
    {
        ComPtr<ID3D11Texture2D> Texture;
        ComPtr<ID3D11RenderTargetView> View;
        ComPtr<ID3D11ShaderResourceView> Sample;
    };

}

namespace dfl::chapter_two
{
    struct NtscRenderer::Implementation
    {
        // Keep the device library alive until all of its COM interfaces are released.
        SystemLibrary Runtime;
        ComPtr<ID3D11Device> Device;
        ComPtr<ID3D11DeviceContext> Context;
        ComPtr<ID3D11VertexShader> Vertex;
        ComPtr<ID3D11PixelShader> Encode, Decode, Crt;
        ComPtr<ID3D11RasterizerState> Rasterizer;
        ComPtr<ID3D11Buffer> Parameters;
        ComPtr<ID3D11Texture2D> Input, Staging;
        ComPtr<ID3D11ShaderResourceView> InputView;
        RenderTarget Encoded, Decoded, Output;

        ~Implementation()
        {
            if (!Context) return;
            Context->ClearState();
            Context->Flush();
        }

        HRESULT CreateShaders() noexcept
        {
            auto hr = Device->CreateVertexShader(ntsc::VertexMain, sizeof(ntsc::VertexMain), nullptr, Vertex.GetAddressOf());
            if (FAILED(hr)) return hr;

            const struct { const unsigned char* Bytecode; std::size_t Size; ComPtr<ID3D11PixelShader>& Shader; } passes[] = {
                {ntsc::EncodeMain, sizeof(ntsc::EncodeMain), Encode},
                {ntsc::DecodeMain, sizeof(ntsc::DecodeMain), Decode},
                {ntsc::CrtMain, sizeof(ntsc::CrtMain), Crt}
            };
            for (const auto& pass : passes)
            {
                hr = Device->CreatePixelShader(pass.Bytecode, pass.Size, nullptr, pass.Shader.GetAddressOf());
                if (FAILED(hr)) return hr;
            }
            return S_OK;
        }

        HRESULT CreateTarget(UINT width, DXGI_FORMAT format, bool sampled, RenderTarget& target) noexcept
        {
            UINT bindings = D3D11_BIND_RENDER_TARGET;
            if (sampled) bindings |= D3D11_BIND_SHADER_RESOURCE;
            const auto description = TextureDescription(width, format, D3D11_USAGE_DEFAULT, bindings);
            auto hr = Device->CreateTexture2D(&description, nullptr, target.Texture.GetAddressOf());
            if (FAILED(hr)) return hr;
            hr = Device->CreateRenderTargetView(target.Texture.Get(), nullptr, target.View.GetAddressOf());
            if (FAILED(hr) || !sampled) return hr;
            return Device->CreateShaderResourceView(target.Texture.Get(), nullptr, target.Sample.GetAddressOf());
        }

        HRESULT Initialize() noexcept
        {
            auto hr = Runtime.Load(L"d3d11.dll");
            if (FAILED(hr)) return hr;
            const auto create = reinterpret_cast<decltype(&D3D11CreateDevice)>(GetProcAddress(Runtime.Handle, "D3D11CreateDevice"));
            if (!create) return HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
            constexpr D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0};
            hr = create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                levels, ARRAYSIZE(levels), D3D11_SDK_VERSION, Device.GetAddressOf(), nullptr, Context.GetAddressOf());
            if (FAILED(hr)) return hr;

            hr = CreateShaders();
            if (FAILED(hr)) return hr;
            D3D11_RASTERIZER_DESC rasterizer{};
            rasterizer.FillMode = D3D11_FILL_SOLID;
            rasterizer.CullMode = D3D11_CULL_NONE;
            rasterizer.DepthClipEnable = TRUE;
            hr = Device->CreateRasterizerState(&rasterizer, Rasterizer.GetAddressOf());
            if (FAILED(hr)) return hr;
            D3D11_BUFFER_DESC parameters{};
            parameters.ByteWidth = 16;
            parameters.Usage = D3D11_USAGE_DEFAULT;
            parameters.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
            hr = Device->CreateBuffer(&parameters, nullptr, Parameters.GetAddressOf());
            if (FAILED(hr)) return hr;

            const auto input = TextureDescription(Width, Bgra, D3D11_USAGE_DYNAMIC,
                D3D11_BIND_SHADER_RESOURCE, D3D11_CPU_ACCESS_WRITE);
            hr = Device->CreateTexture2D(&input, nullptr, Input.GetAddressOf());
            if (FAILED(hr)) return hr;
            hr = Device->CreateShaderResourceView(Input.Get(), nullptr, InputView.GetAddressOf());
            if (FAILED(hr)) return hr;
            hr = CreateTarget(Width * 4, DXGI_FORMAT_R32G32B32A32_FLOAT, true, Encoded);
            if (FAILED(hr)) return hr;
            hr = CreateTarget(Width * 2, DXGI_FORMAT_R8G8B8A8_UNORM, true, Decoded);
            if (FAILED(hr)) return hr;
            hr = CreateTarget(Width, Bgra, false, Output);
            if (FAILED(hr)) return hr;
            const auto staging = TextureDescription(Width, Bgra, D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_READ);
            hr = Device->CreateTexture2D(&staging, nullptr, Staging.GetAddressOf());
            if (FAILED(hr)) return hr;

            Context->IASetInputLayout(nullptr);
            Context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            Context->VSSetShader(Vertex.Get(), nullptr, 0);
            Context->RSSetState(Rasterizer.Get());
            ID3D11Buffer* buffer = Parameters.Get();
            Context->PSSetConstantBuffers(0, 1, &buffer);
            return DeviceStatus();
        }

        HRESULT DeviceStatus() const noexcept
        {
            return Device ? Device->GetDeviceRemovedReason() : S_OK;
        }

        void Draw(UINT width, ID3D11ShaderResourceView* input, ID3D11PixelShader* shader,
            const RenderTarget& target) noexcept
        {
            const D3D11_VIEWPORT viewport{0, 0, static_cast<float>(width), static_cast<float>(Height), 0, 1};
            Context->RSSetViewports(1, &viewport);
            ID3D11RenderTargetView* output = target.View.Get();
            Context->OMSetRenderTargets(1, &output, nullptr);
            Context->PSSetShader(shader, nullptr, 0);
            Context->PSSetShaderResources(0, 1, &input);
            Context->Draw(3, 0);

            ID3D11ShaderResourceView* unbound = nullptr;
            Context->PSSetShaderResources(0, 1, &unbound);
            Context->OMSetRenderTargets(0, nullptr, nullptr);
        }

        HRESULT Render(const void* bgra, std::size_t sourcePitch, int mode,
            void* outputBgra, std::size_t outputPitch) noexcept
        {
            D3D11_MAPPED_SUBRESOURCE mapped{};
            auto hr = Context->Map(Input.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
            if (FAILED(hr)) return hr;
            const bool upload = mapped.pData && ValidPitch(mapped.RowPitch);
            if (upload) CopyRows(mapped.pData, mapped.RowPitch, bgra, sourcePitch);
            Context->Unmap(Input.Get(), 0);
            if (!upload) return E_UNEXPECTED;

            const int settings[4]{mode, 0, 0, 0};
            Context->UpdateSubresource(Parameters.Get(), 0, nullptr, settings, 0, 0);
            ID3D11ShaderResourceView* crtInput = InputView.Get();
            if (mode != 3)
            {
                Draw(Width * 4, InputView.Get(), Encode.Get(), Encoded);
                Draw(Width * 2, Encoded.Sample.Get(), Decode.Get(), Decoded);
                crtInput = Decoded.Sample.Get();
            }
            Draw(Width, crtInput, Crt.Get(), Output);
            Context->CopyResource(Staging.Get(), Output.Texture.Get());
            hr = Context->Map(Staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
            if (FAILED(hr)) return hr;

            hr = DeviceStatus();
            if (SUCCEEDED(hr) && (!mapped.pData || !ValidPitch(mapped.RowPitch))) hr = E_UNEXPECTED;
            if (SUCCEEDED(hr)) CopyRows(outputBgra, outputPitch, mapped.pData, mapped.RowPitch);
            Context->Unmap(Staging.Get(), 0);
            return hr;
        }
    };

    NtscRenderer::NtscRenderer() noexcept = default;
    NtscRenderer::~NtscRenderer() = default;

    void NtscRenderer::Clear() noexcept
    {
        implementation_.reset();
    }

    HRESULT NtscRenderer::Render(const void* bgra, std::size_t sourcePitch, int mode,
        void* outputBgra, std::size_t outputPitch) noexcept
    {
        if (!bgra || !outputBgra || mode < 0 || mode > 3 || !ValidPitch(sourcePitch) || !ValidPitch(outputPitch))
            return E_INVALIDARG;
        if (!implementation_)
        {
            std::unique_ptr<Implementation> candidate(new (std::nothrow) Implementation);
            if (!candidate) return E_OUTOFMEMORY;
            const auto hr = candidate->Initialize();
            if (FAILED(hr))
            {
                const auto removed = candidate->DeviceStatus();
                return FAILED(removed) ? removed : hr;
            }
            implementation_ = std::move(candidate);
        }

        const auto hr = implementation_->Render(bgra, sourcePitch, mode, outputBgra, outputPitch);
        if (FAILED(hr))
        {
            const auto removed = implementation_->DeviceStatus();
            if (FAILED(removed))
            {
                Clear();
                return removed;
            }
        }
        return hr;
    }
}
