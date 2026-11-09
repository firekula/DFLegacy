# 客户端自动故障上报 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 无需玩家点击同意即可自动向现有 GM 后台发送脱敏故障与硬件摘要，原始转储仅保存在本机。

**Architecture:** 公共账号监听器接收有界 JSON 报告，独立持久化诊断仓库和 GM 页面查看；独立客户端助手读取当前用户的新会话事故、脱敏、采集硬件并有限重试。原生捕获通过有界交接保存最小现场，不在故障线程联网。

**Tech Stack:** C#/.NET 10、ASP.NET Core、现有原生 x86 C++/Win32、原有静态 GM JS/CSS、V4 发布工具。

**Spec:** docs/superpowers/specs/2026-11-09-client-diagnostic-reporting-design.md。开头的“2026-11-09 用户修订”优先；下文旧同意/转储上传方案不实施。

## Global Constraints

- 当前隔离工作区与 codex/client-diagnostic-reporting 分支；不改线上、第二章工作区或 G:/dnf_local/DNF_2008 原目录；不发布 GitHub/GHCR。
- 默认自动基本上报，无首次同意弹窗，设置可关闭。原始转储只落当前用户本地，不提供上传接口。
- 不上传账号、密码、令牌、聊天、截图、完整配置、原始内存、完整路径或全进程列表。硬件字段缺失为 unknown，不阻塞启动。
- 接收仅账号 API 端口；管理仅 GM 端口和 Admin 身份；玩家端口不接收也不能查看。兼容公网 HTTP 基本报告，显示未加密；HTTPS 不降级、不忽略证书、不跨站重定向。
- 基本报告压缩 256 KiB/展开 1 MiB 上限；客户端单并发，默认 64 KiB/s，3 次尝试，单次连接/响应均有界；服务端同时最多 2 个上传。
- 服务端 256 MiB/2000 条、14 天；删除后去重摘要 30 天/50000 条，满额拒新，不提前丢失幂等承诺；客户端自动队列 100 份/32 MiB/14 天。
- 每次事故冻结唯一事件 ID 和正文哈希；重复 POST 同正文返回原回执，不同正文 409，已删除记录 410。不用游戏存档锁或 state.json 存诊断。
- D 盘剩余空间较少，构建到 C:/codex-build/client-diagnostics-20261109，测试客户端和较大产物使用 G:/dnf_local 下隔离目录。保留全部现有未跟踪文件。

## Review Focus

- 被复制的整合包和共享游戏目录不得把其他用户的历史事故自动上传（Task 2）。
- 取消/正常 EOF/旧处理器 CONTINUE_EXECUTION 不得被自动记为 crash（Task 3）。
- 接收成功但响应丢失/重启/删除后重试不得重复计数或复活报告（Task 1/2）。
- HTTP、重定向、路径穿越、压缩炸弹及正文 XSS 不得越过边界（Task 1/2）。
- 原版异常处理可能在助手取得现场前退出或阻塞；原始现场、事后快照、未知退出必须区分（Task 3）。

## 共享协议

Wire format `DFLegacy-client-diagnostic-1`，JSON UTF-8，服务器可选接收 gzip，客户端先用普通 JSON（小报告无需压缩也必须有界）。稳定字段：`eventId`（N 格式 GUID），`sessionId`（N GUID），`kind`（error/networkerror/crash），`occurredAt`（ISO8601 UTC），`clientVersion`，`architecture`，`evidence`，`stage`，`exceptionCode`，`module`（仅叶名称），`moduleHash`，`moduleOffset`，`stack`（至多 32 个模块相对帧），`occurrences`，`system`（有界键值），`context`（有界已脱敏摘要）。无附件、路径、账号或自由任意 metadata。

服务返回 `{receiptId,eventId,status}`，创建 201，重传 200，冲突 409，已删除 410；未支持/关闭 404、限流 429、容量暂满 503。服务器计算原始展开 JSON 字节 SHA256，不以客户端声称摘要为准。客户端重试必须重用冻结字节。

### Task 1: 服务端接收、存储和 GM 查看

**Files:** 新增 src/DFLegacy.Server/ClientDiagnosticModels.cs、ClientDiagnosticStore.cs、ClientDiagnosticEndpoints.cs；修改 ServerOptions.cs、Program.cs、AdminAccessPolicy.cs、WebPanelHosting.cs、wwwroot/admin/index.html；新增 wwwroot/admin/admin-diagnostics.js；新增 tests/DFLegacy.SmokeTests/ClientDiagnosticSmokeTests.cs 并接入 Program.cs，扩展实际 WebPanelsSmokeTests。

**Interfaces:** `app.MapClientDiagnostics(options)` 注册端点，DI singleton `ClientDiagnosticStore` 使用 TimeProvider。报告 DTO 与共享协议一致。监听器限制由专门路由检查加集中权限策略共同执行。

- [ ] 写失败测试：实际 HTTP POST 在 8081 成功、18081/18082 拒绝；匿名和玩家管理读写拒绝、Admin 仅 GM 可读；幂等/409/410/重启；超限正文与 gzip、恶意字段/路径、容量、分页、删除、标记状态。
- [ ] 执行 `dotnet run --project tests/DFLegacy.SmokeTests -c Release --artifacts-path C:/codex-build/client-diagnostics-20261109 -- --client-diagnostics` 留存 RED。
- [ ] 实现只接受严格白名单的 DTO/入库校验、限流、独立目录原子入库及有界索引恢复；不在 API 请求内读取内存转储；接收开关默认启用，路径从 DataPath 推导。
- [ ] GM 页新增列表、过滤、详情、下载脱敏 JSON、状态和删除；所有正文用 textContent，不嵌入 HTML。新增脚本仅 GM 端口放行；UI 请求复用现有授权。
- [ ] 运行相同测试及真实页面资源回归；记录命令/退出码，提交该任务文件，独立审阅。

### Task 2: 当前用户自动报告助手与设置

**Files:** 新增 src/DFLegacy.Diagnostics.Core 项目（DTO/净化/本地队列/HTTP 上传/设置），新增 src/DFLegacy.Diagnostics 项目（独立助手）；修改 Launcher/Models/LauncherOptions.cs、Services/LaunchFlow.cs、Diagnostics/IncidentReporter.cs、Views/ConfigWindow.xaml(.cs)、启动/退出诊断交接；新增 tests/DFLegacy.Diagnostics.Tests，扩展 Launcher.Tests。

**Interfaces:** `DiagnosticOptions` 默认 `Enabled=true`；站点为空时使用所选服务器账号 API；`DiagnosticSession.Start(clientRoot, serviceUrl, sessionId)` 产生当前用户会话登记和子进程交接，助手只消费登记后该会话的事故。`DiagnosticQueue.Enqueue(report)` 冻结字节，`UploadPendingAsync` 单并发并返回状态，不进入游戏主线程。

- [ ] 写失败测试：无交互默认启用、关闭/重开不自动补传旧记录、换站点不串报、复制目录不能继承队列；脱敏假密码/路径/令牌；超大/符号链接文件不采集；并发互斥；429/5xx/响应丢失/404/410/证书失败/重定向；助手退出不关闭游戏。
- [ ] 执行新测试控制台项目并留 RED；实现 LocalAppData 用户隔离队列和开关、严格结构化投影、受限 CPU/OS/内存/GPU 查询、单并发节流上传及重试。
- [ ] 故障上下文只从登记会话的新独立记录读取，不能扫全部 launcher.log。敏感原始字段不进入 DTO。成功更新、正常取消不入队；错误和崩溃/断线用既有证据类型。
- [ ] 登录器增加诊断状态和关闭开关，不弹首次同意；独立助手使用结构化参数且不接收游戏密码。启动器故障也在可用的会话/配置范围内入队，站点不可用只留本地。
- [ ] 运行新测试及相关 Launcher.Tests，提交和审阅。上传使用冻结正文，必须与 Task 1 的实际服务端做兼容测试。

### Task 3: ACT1 原生现场与本地转储

**Files:** 修改 src/DFLegacy.Ijl15/src/native_incidents.cpp(.h) 与测试，tools/debug/crash_capture/main.cpp 和故障夹具，Launcher 的助手交接。新增原版入口研究证据文档，不把游戏二进制入仓。

**Interfaces:** 原生最小事件仍输出现有事件 ID/会话格式；新增 provisional/confirmed/cancelled 证据状态。当前用户随机命名 IPC 绑定 PID、创建时间、会话；助手仅对该会话的游戏操作，原始转储不进入 Task 2 队列。

- [ ] 前置只读研究当前 ACT1/BugTrap/DumpReport 哈希、导出、处理链与安装时序；给出已验证入口及未知项，不能盲目 Hook。不可验证的精确现场明确阻断，其他报告链路仍继续。
- [ ] 原生故障子进程写失败测试：访问冲突、栈溢出、之前过滤器继续执行/立即退出/阻塞/后注册覆盖、原生窗口/异常退出，多开与 PID 归属；正常退出与可处理异常不上传。
- [ ] 实现预分配现场、有界交接（基本 2s/含转储总 5s）、先临时保存后确认、保留旧处理链；CONTINUE_EXECUTION 撤销。观察到的故障弹窗只记窗口证据，不能伪称故障指令。
- [ ] 外部保存最多 16 MiB 小型转储、最多 2 份到当前用户本地；超限/超时清理不完整文件，不自动上传，不杀游戏，不默认调试附加。保留原显式详细调试工具。
- [ ] 构建 Win32 原生测试并运行已有/新增捕获测试；实现代码必须经过真实故障夹具验证。提交和审阅。

### Task 4: 打包、端到端与交付文档

**Files:** tools/release 中实际 client-assets/V4 打包路径及测试，离线打包器/Manager 的已有客户端配置导出接线，README.md、CHANGELOG.md、CLIENT_UPDATES_V4.md、DEPLOYING.md、图文验收报告。

**Interfaces:** 两架构包都含对应架构的自包含单文件 Diagnostics.exe 和 x86 捕获器（按 Task4 集成裁定放宽共享运行时优化，保持单文件修复且无需全局 .NET），受 V4 清单校验。LocalAppData 数据、旧预留诊断目录、报告、转储与本地开关不进入发布资源。

- [ ] 写失败测试：四变体包组件齐全、配置迁移不覆盖已关闭选择、任何运行时报告/凭证不进入 client-assets；故障助手缺失能经 V4 修复。
- [ ] 实现打包接线和服务端参数文档；本地配置保持同一 8081/18081 端口。
- [ ] 构建两架构，在隔离服务上跑真实报告入库、GM 查询/筛选/下载/删除、重启/断网/响应丢失和原生故障端到端；显示实际 UI 并保存无敏感信息截图。
- [ ] Gitea 隔离测试发布并校验四变体更新，确认正式 GitHub/GHCR、原客户端和线上配置哈希保持。若远端凭据不可用，记录实际阻断，不伪称成功。
- [ ] 完整相关测试和最终代码审阅，更新图文报告与所有相关文档；本地提交，不合并 main 或正式发布，汇报完成项与真实限制。

## 执行记录

- 用户修订：基本报告自动发送，不增加玩家同意步骤；原始转储只本地保存。
- 采用同一隔离工作区，保留当前未跟踪文件；实施过程中不再为已经明确的接入方式和自动基本上报重复询问。
