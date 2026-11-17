# v1.0.14 客户端角色重建（未签名）

日期：2026-11-17
范围：B1（FireKula 元数据 + 1.0.14 重建，不签名）；Phase A 的 stub/布局见 [验收记录](2026-11-17-dfo-launcher-layout-acceptance.md)。
状态：客户端三程序、诊断和 stub 已重建并核对元数据；签名（B2）、签名后清单/迁移对（B3）和发行（B4）仍待证书与决策。

## 源码

- dev：`771905f`（LauncherVersion 1.0.14、托管登录器/更新器 Company/Copyright FireKula、原生入口 rc FireKula、诊断 csproj 元数据）；`b5d4736`（诊断 Win32Resource + rc）。
- public：`3215cc2`（角色元数据与版本）、`a7f857c`（诊断版本资源）。
- 最终发行提交将包含 stub 源码、签名/打包工具和文档（B4），届时以该提交重新构建并签名；当前 public `a7f857c` 不含 stub 源码。

## 构建产物（未签名，最终哈希以签名后为准）

| 角色 | 文件 | 大小 | SHA-256 | 版本/说明 |
| --- | --- | --- | --- | --- |
| 固定入口 | `DFLegacy.Launcher.exe` | 411,648 | `8DBD987EF03C1D4077C52CD634F498D66EA4BAB0A07EB10D7B739052FC862EEE` | 1.0.14.0 / `1.0.14+a7f857c…` |
| 托管登录器 | `DFLegacy.Launcher.App.exe` | 89,100,928 | `3cf877ec2854b3dd23bdd49fc7bc7d2b3e75cf00a83d1619fe59167e15193c34` | 1.0.14.0 / `1.0.14+a7f857c…` |
| 更新器 | `DFLegacy.Updater.exe` | 89,100,951 | `34004d53ed37e5dc9c2b7a2195c506c7a70aa0df5e355a5b8c00d53061716a0b` | 1.0.14.0 / `1.0.14+a7f857c…` |
| 诊断 | `DFLegacy.Diagnostics.exe` | 37,849,705 | `6a76565670ce99d925e477921f1721b6405de4a82d6fe6e55bcc83c0cc40caa7` | 1.0.14.0 / `1.0.14+a7f857c…` |
| 启动器 | `DFOLauncher.exe` | 440,832 | `2EC8A464F95B2AAA0590AC7CFCC376C810F0F12C55ED7230C271A28FB7B81570` | 1.0.14.0 / `1.0.14+b5d4736…`（dev 源码，最终 public 提交后重建） |

所有产物已核对：FileDescription/OriginalFilename/InternalName/ProductName 与实际角色一致，`CompanyName = FireKula`，`LegalCopyright = Copyright (C) 2026 FireKula`，FileVersion `1.0.14.0`，ProductVersion `1.0.14+<40位提交>`。诊断的 OriginalFilename 已从 .NET 默认的 `.dll` 修正为实际的 `DFLegacy.Diagnostics.exe`。

## 待决策与阻塞

- **代码签名证书**：B2 需要受信任 OV/EV 证书（PFX + 密码 env，或硬件/云签名）和时间戳 URL；签名会改变上述所有哈希，B3 的清单、桥接/目标和入口迁移对必须基于签名后的最终文件。
- **`DFLegacy.CrashCapture.exe`**：仓库无源码，当前文件无任何版本信息；只能签名、不能补 FireKula/版本属性。需选择重做、仅签名或移除。
- **内部 2009 风格改名**：未确认；如执行会改变文件名，需要新的构建和迁移对，建议与 B3 一起定稿。
- **公司电脑实机迁移**：`DFOLauncher.exe`（Phase A 的 `be020178…` 构建）仍需用户实机确认登录和游戏。
- **最终发行提交**：B4 将把 stub 源码、签名/打包工具和文档提交到 public main，再以该提交重建全部角色并签名；当前 `a7f857c`/`b5d4736` 的哈希是签名前的验证值。

## 验证

- 原生入口/诊断/stub 的构建脚本各自通过；stub 的 CTest `1/1 Passed`（2.58s）。
- 元数据逐项断言在构建脚本内执行，任何一项不符即失败。
- 本记录不构成签名完成的声明；杀软误报的最终评估必须在受信任证书签名、修改后的清单/迁移和发行之后再验证。
