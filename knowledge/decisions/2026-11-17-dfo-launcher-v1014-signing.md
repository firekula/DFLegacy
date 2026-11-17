# v1.0.14 调试证书签名记录

日期：2026-11-17
范围：B2（自签名调试证书签名，不声称杀软/SmartScreen 已解决）。
状态：6 个自有 EXE 已用调试证书签名；签名后清单/序列、桥接/目标、入口迁移对（B3）和发行（B4）仍待完成。

## 证书

- Subject：`CN=FireKula`，自签名，Code Signing EKU。
- Thumbprint：`7506662DAB26A538B1CCAD01A08F3450A8F7D67B`；NotAfter：2029-11-17。
- PFX：`G:\tmp\dflegacy-debug-signing\FireKula-debug.pfx`，密码在本地 `password.txt`，均不入仓库。
- **该证书没有公共 CA 信任链**。玩家 Windows 会显示未知发布者/不受信任；它不能降低 SmartScreen 或杀软告警，可能比未签名更显眼。发行说明必须明确标注“调试签名，不受信任”。

## 已签名文件（签名后哈希）

| 文件 | 大小 | SHA-256 | 签名状态 |
| --- | --- | --- | --- |
| `DFLegacy.Launcher.exe` | 413,032 | `470E3AD7567B971C2D5DFDE554F221F7FBF262245AB0B8532A98CF9BC8883497` | UnknownError（未受信任） |
| `DFLegacy.Launcher.App.exe` | 89,102,312 | `9EB3CD76A1EE566AF26EB6124E7AFBFF8A1943829D36426FD772CB8993AC719E` | UnknownError |
| `DFLegacy.Updater.exe` | 89,102,336 | `B755816A5765A079F4CAF00A1F656BF90E1BDEABBD3FC68EE480564F91EDAE6C` | UnknownError |
| `DFLegacy.Diagnostics.exe` | 37,851,096 | `4A9C862601E84B3AA089C0E21326549DE81CD5CC6499D6FC57C6046EC8B7BBEB` | UnknownError |
| `DFOLauncher.exe` | 442,216 | `8643B2198B7934E2E6FF92307DC063F62168B677B2E4D832C3AD5DE4674B55AF` | UnknownError |
| `DFLegacy.CrashCapture.exe` | 231,272 | `16038BA6B9CC321986A302DA6BB25119B0B1BECE59043D197A24067B3754B869` | UnknownError |

- 签名主体均为 `CN=FireKula`；文件版本资源仍是 B1 的 1.0.14/FireKula 元数据。
- `DFLegacy.CrashCapture.exe` 签名前无任何版本信息，签名不会补版本属性；这是已知缺口。
- 证据：`G:\Game\DNF_ACT1\DFLegacy\.tmp\full-starter-upgrade\v1014-signed\signing-evidence.json`（未入仓库）。

## 工具

- `tools/release/sign_launcher.ps1` 新增 `-DebugCertificate`：跳过 `/pa` 信任链验证和时间戳，仅检查签名存在并输出 `status`；受信任证书模式仍保持 `/pa /all` 硬门禁。
- 调试证书模式会显式打印警告；发行说明不得声称签名解决了误报。

## 下一步（B3）

- 用签名后的 EXE 生成 v1.0.14 客户端清单（新序列），重新生成 `DFLegacy.Bootstrap.ini` 描述符；
- 生成 `v1.0.14-bridge`（保留 v1.0.13-r2 入口 `cf16fedc…`）和 `v1.0.14` 目标（签名后的入口），扩展入口迁移对和测试；
- B4 更新 DFOLauncher 启动包（`DFOLauncher.exe` + `client\`）、发行说明（标注调试签名）并完成本地验收。
