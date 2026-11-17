# GM 后台客户端故障页重做与 ZIP 导出（2026-11-17 夜）

目标环境：本机 GM 口 `http://localhost:48093`（页面可打开，账号 root 换 token 后 API 可用）。
线上数据快照：74 个故障组 / 363 份报告（导出前查询，2026-11-17 23:0x）。

## 问题

- 每组只显示一行超长文本，末尾三个按钮（新问题/处理中/已处理）没有标签说明。
- 没有统一下载：只能逐份「下载脱敏 JSON」。
- 详情是整段原始 JSON。
- 没有给自动化/agent 直接读取和操作报告的入口。

## 改动

### 服务端

- 新增 `GET /api/admin/diagnostics/export`（仅 GM 口 + Admin），沿用
  `kind/version/status/system/gpu/from/to` 筛选，返回一个 ZIP：
  - `index.json`：导出时间、筛选条件、组列表与每份报告的文件名；
  - `reports/<组指纹前 12 位>/<receipt>.json`：原始脱敏报告。
  - 单次上限 5,000 份 / 128 MiB（解压后），超限返回 413。
- [ClientDiagnosticStore.ExportAsync](../../src/DFLegacy.Server/ClientDiagnosticStore.cs) 在同一把存储锁内
  取组与正文快照；`ListAsync` 与它共用 `SnapshotGroups`，筛选逻辑只有一份。
- 端点实现见 [ClientDiagnosticEndpoints.cs](../../src/DFLegacy.Server/ClientDiagnosticEndpoints.cs)。

### GM 页面

- 组列表改成 `.data-table` 表格：类型 | 模块 | 版本 | 合并事故 | 报告 | 处理状态 | 最近发生 | 操作。
- 三个状态按钮改成带 `aria-label="处理状态"` 的下拉，选中即 PATCH 并 toast，不再需要猜。
- 工具栏新增「下载当前筛选（ZIP）」；每行「展开报告」列出短回执号，点开看详情。
- 详情改成字段化摘要（回执/状态/类型/证据/阶段/版本/模块+偏移/异常码/时间/系统/说明）+ 可展开原始 JSON；
  单份下载与删除保留。
- 改动文件：[index.html](../../src/DFLegacy.Server/wwwroot/admin/index.html)、
  [admin-diagnostics.js](../../src/DFLegacy.Server/wwwroot/admin/admin-diagnostics.js)；脚本查询串升到
  `?v=20261117-diagnostics`。

### 给 agent 的 CLI

[tools/admin/diagnostics.py](../../tools/admin/diagnostics.py)（纯标准库）：

```powershell
$env:DFLEGACY_GM_URL='http://localhost:48093'
$env:DFLEGACY_GM_USER='<admin>'; $env:DFLEGACY_GM_PASSWORD='<password>'
# 或已有 token：$env:DFLEGACY_GM_TOKEN='<token>'
python -B tools/admin/diagnostics.py groups --kind crash --status new
python -B tools/admin/diagnostics.py export --out diag.zip --kind crash
python -B tools/admin/diagnostics.py report <receipt>
python -B tools/admin/diagnostics.py status <group-id> resolved
python -B tools/admin/diagnostics.py delete <receipt>
```

`export` 对**现有** API 逐页取组、逐份下载正文并在本地打 ZIP，所以线上服务端不升级也能用；
服务端 ZIP 端点用于页面的一键下载。凭据只从环境变量读，不写仓库。

## 验证

| 项目 | 结果 |
| --- | --- |
| CLI 对线上 48093 | `groups` 列出 crash/new；`export --kind crash` 得到 30 组 / 54 份 / 56,235 字节 ZIP（55 条目，含 index.json）；`status <group> new` 返回 updated |
| 本地 ZIP 端点 | 本地服务（Admin 48081 / GM 48095）播种 3 份报告；`?kind=crash` 导出 2 组 2 份 + index，error 组被过滤 |
| 本地页面 | `http://127.0.0.1:48095/` 返回 200，HTML 含 `diagnosticExport`、`data-table`、`diagnosticGroups`、`处理状态`；脚本含 `saveDiagnosticStatus` |
| 诊断冒烟 | `dotnet run ... -- --client-diagnostics` → 0 failures（含新增：ZIP 仅 Admin、仅 GM 口、index+正文、筛选） |
| 浏览器逻辑 | `node --test tests/DFLegacy.Manager.Tests/gm-mail-browser.test.cjs` → 19/19，新增表格/状态下拉/导出按钮用例 |
| 全量冒烟 | `dotnet run ... --no-build` 退出码 0，无 FAIL |

本地验证用的服务是仓库构建的 `DFLegacy.Server.exe` + `.tmp/gm-ui-check/server.json`（临时数据目录，
验证后已停止并保留目录供复查）；没有动 48093 的线上服务与数据。

## 未验证 / 边界

- [未验证] 线上 48093 仍是旧页面：页面与 ZIP 端点要重新部署服务端（或替换 `wwwroot` 并重启）才出现。
- [未验证] 真实浏览器里的布局/缩放下拉与长版本号换行；本机 GUI 自动化在无人值守会话被自动拒绝，
  以页面 HTML/脚本 + Node DOM 测试 + 本地 HTTP 200 作为本轮证据。
- 导出内存上限 128 MiB；超过时页面提示收窄筛选。
- 删除仍是单份；批量删除未做。
