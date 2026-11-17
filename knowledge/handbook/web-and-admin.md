# Web 页面与管理 API

用途：修改部署管理器、GM、玩家中心及游戏内充值页面。API 参数详情先读 [ADMIN_API](../../ADMIN_API.md)，运维读 [operations](operations.md)，路由定位查 [http-routes.tsv](reference/http-routes.tsv)。

## 四类界面

| 界面 | 入口代码/资源 | 身份与职责 |
| --- | --- | --- |
| 部署管理器，通常 18080 | [ManagerApplication](../../src/DFLegacy.Manager/ManagerApplication.cs)、[manager.js](../../src/DFLegacy.Manager/wwwroot/manager.js)、[manager.css](../../src/DFLegacy.Manager/wwwroot/manager.css)、[index](../../src/DFLegacy.Manager/wwwroot/index.html) | 管理器独立账号；配置、启停、PVF、日志、导出包、首次游戏管理员 |
| GM，通常 18081 | [admin/index.html](../../src/DFLegacy.Server/wwwroot/admin/index.html)、[admin.js](../../src/DFLegacy.Server/wwwroot/admin/admin.js) | 游戏管理员；角色、账号、邮件、奖励、诊断 |
| 玩家中心，通常 18082 | [player.html](../../src/DFLegacy.Server/wwwroot/admin/player.html) | 游戏账号；自己的角色和允许的玩家操作 |
| 游戏内充值/黑钻 | [wwwroot/client](../../src/DFLegacy.Server/wwwroot/client) | 老客户端内嵌浏览器；布局和认证兼容性要求独立于现代管理网页 |

上述端口是默认部署口径，实际启用值由配置决定。管理器账号与游戏 GM 账号不是同一存储。管理器 `/health` 存活不代表游戏服务已启动；其 `/ready` 在游戏未运行时返回 503 属正常区分。

## 服务端路由和权限

[Server Program](../../src/DFLegacy.Server/Program.cs) 注册大部分账号、角色、商城、GM API；专门功能在 [AdminRewardEndpoints](../../src/DFLegacy.Server/AdminRewardEndpoints.cs)、[ClientDiagnosticEndpoints](../../src/DFLegacy.Server/ClientDiagnosticEndpoints.cs)、[ClientGuildEndpoints](../../src/DFLegacy.Server/ClientGuildEndpoints.cs)。新增 HTTP 功能需同时检查：

1. 请求/响应类型：[AdminApiModels](../../src/DFLegacy.Server/AdminApiModels.cs)及对应功能模型。
2. 身份和所有权：[AdminAuthService](../../src/DFLegacy.Server/AdminAuthService.cs)、[AdminAccessPolicy](../../src/DFLegacy.Server/AdminAccessPolicy.cs)。不能只隐藏前端按钮。
3. 端口分流：[WebPanelHosting](../../src/DFLegacy.Server/WebPanelHosting.cs)。同一个路径可能在一个端口允许，在另一个端口 404。
4. 状态写入：通过业务服务/`JsonGameStore`，不能从网页处理器直接改快照 JSON。
5. 在线效果：持久化与当前游戏会话通知是否一致，重试是否重复发奖。

诊断报告接收是账号 API 口上的指定 POST 路径，诊断查询/下载/删除属于 GM 口。测试充值受显式测试开关约束。路径名中带 `admin` 不是完整安全检查，匿名/普通玩家/管理员三种身份都要验证。

Manager 路由由 [ManagerApplication](../../src/DFLegacy.Manager/ManagerApplication.cs) 的 `/api` 组注册。首次设置使用独立设置凭据，登录后使用 cookie 和 CSRF 检查；程序入口不复用游戏 token。先读 [ManagerIdentity](../../src/DFLegacy.Manager/ManagerIdentity.cs)，再改登录/跨源/反向代理逻辑。

## 前端维护

这是随 ASP.NET 发布的静态 HTML/CSS/JavaScript，现有页面无需 npm bundler。先沿 HTML 的 script/link 引用查依赖；不要为一个按钮引入新框架或把静态资源遗漏在发布中。

- GM 邮件/奖励使用 [admin-rewards.js](../../src/DFLegacy.Server/wwwroot/admin/admin-rewards.js)，故障报告使用 [admin-diagnostics.js](../../src/DFLegacy.Server/wwwroot/admin/admin-diagnostics.js)。新增脚本还要检查独立 GM 监听的静态资源白名单。
- 故障报告的批量导出是 GM 口的 `GET /api/admin/diagnostics/export`（同筛选参数，ZIP：`index.json` + `reports/<组>/<回执>.json`，上限 5,000 份 / 128 MiB）。命令行/agent 用 [tools/admin/diagnostics.py](../../tools/admin/diagnostics.py)，凭据只从 `DFLEGACY_GM_*` 环境变量读；它的 `export` 逐份拉取本地打 ZIP，可不升级服务端使用。改动页面或端点后按 [决策记录](../decisions/2026-11-17-gm-diagnostics-redesign.md) 的表逐项复核。
- 内嵌页面使用 [client-auth.js](../../src/DFLegacy.Server/wwwroot/client/client-auth.js)、[client-layout.js](../../src/DFLegacy.Server/wwwroot/client/client-layout.js) 与 `css/commen.css`。保留老浏览器兼容代码，检查实际客户端中的尺寸、滚动和点击，不只在桌面 Chrome 看效果。
- 项目自有桌面弹窗适用根 AGENTS 的原版 DNF 风格；普通管理网页沿现有网页样式，不把 WPF 控件名强行套入 HTML。

## 典型修改链

**新增一个 GM 操作**：请求模型 → 权限/端口策略 → 业务事务与重试规则 → API → GM JS/HTML → 后端权限测试 → 浏览器逻辑测试 → 独立 GM 端口实测 → API 文档。

**修复“管理页可用，但独立 GM 口按钮不工作”**：浏览器网络面板确认请求/静态脚本状态 → 核对 `WebPanelHosting` → 核对登录身份与 API 基址 → 检查处理器日志。不能通过把所有端口全部放行来修复。

**发全体邮件**：先理解预览、批次身份、重试/补发和已领取状态；不能为了 UI 方便改成每次点击重新生成一个没有去重的批次。真实发奖属于数据修改，验证用隔离存档。

## 验证入口

```powershell
node --test tests/DFLegacy.Manager.Tests/manager-browser.test.cjs tests/DFLegacy.Manager.Tests/client-pay-browser.test.cjs tests/DFLegacy.Manager.Tests/gm-management-browser.test.cjs tests/DFLegacy.Manager.Tests/gm-mail-browser.test.cjs
```

后端还需执行受影响托管测试，命令见 [测试手册](testing-and-evidence.md)。Node 测试覆盖的是断言中的浏览器逻辑，不能代替长文本布局、真实登录、内嵌浏览器或桌面缩放验收。
