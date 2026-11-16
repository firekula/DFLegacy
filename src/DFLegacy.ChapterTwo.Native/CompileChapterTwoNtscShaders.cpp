#include "ChapterTwoNtscShaders.h"
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    using Microsoft::WRL::ComPtr;
    using dfl::chapter_two::NtscShaderSource;
    constexpr struct { const char* Entry; const char* Profile; } passes[] = {
        {"VertexMain", "vs_4_0"}, {"EncodeMain", "ps_4_0"},
        {"DecodeMain", "ps_4_0"}, {"CrtMain", "ps_4_0"}
    };
    std::ostringstream header;
    header << "#pragma once\nnamespace dfl::chapter_two::ntsc\n{\n" << std::hex << std::setfill('0');
    for (const auto& pass : passes)
    {
        ComPtr<ID3DBlob> bytecode, diagnostics;
        const auto hr = D3DCompile(NtscShaderSource, sizeof(NtscShaderSource) - 1, "ChapterTwoNtscShaders",
            nullptr, nullptr, pass.Entry, pass.Profile, D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0, bytecode.GetAddressOf(), diagnostics.GetAddressOf());
        if (diagnostics)
            std::fwrite(diagnostics->GetBufferPointer(), 1, diagnostics->GetBufferSize(), stderr);
        if (FAILED(hr)) return 1;
        const auto* bytes = static_cast<const unsigned char*>(bytecode->GetBufferPointer());
        header << "    inline constexpr unsigned char " << pass.Entry << "[] = {";
        for (std::size_t i = 0; i < bytecode->GetBufferSize(); ++i)
        {
            if (i % 16 == 0) header << "\n        ";
            header << "0x" << std::setw(2) << static_cast<unsigned>(bytes[i]) << ',';
        }
        header << "\n    };\n";
    }
    header << "}\n";
    std::ofstream output(std::filesystem::path(argv[1]), std::ios::binary | std::ios::trunc);
    output << header.str();
    output.close();
    return output ? 0 : 1;
}
