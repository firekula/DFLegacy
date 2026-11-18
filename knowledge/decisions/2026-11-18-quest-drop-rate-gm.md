# GM 后台任务道具爆率调整（2026-11-18）

用户要求：在管理后台加任务道具爆率调整，本机自动测试，确认生效后**经用户同意再发布镜像**。
本轮没有构建/发布镜像，也没有改 `server.json`。

## 语义

- 只缩放任务道具的两类条目：`[monster reward item]`（击杀）与
  `[clear reward item]`（通关）；普通掉落仍走 `Drop.RatePercent`，互不影响。
- 倍率 100 = 保留 PVF 原始百分比；0 = 关闭任务道具掉落；上限 1000（10 倍）。
- 换算：`round(chance * rate / 100)`（.5 远离零），结果夹到 0–100。
  例：2% × 250% = 5%；2% × 25% = 1%；2% × 10% = 0%（小概率在强降倍率下会归零）。
- 保存后立即生效（`EntranceService` 每次掷骰都读当前值），并持久化到
  `<数据目录>/settings/quest-drop.json`；不写 operator 维护的 `server.json`。
  `server.json` 的 `Drop.QuestItemRatePercent` 只作为首次启动的初始值（默认 100）。

## 组件

| 位置 | 作用 |
| --- | --- |
| [QuestDropSettings.cs](../../src/DFLegacy.Server/QuestDropSettings.cs) | 读取/校验/换算/原子持久化；`ScaleChance` |
| [QuestDropEndpoints.cs](../../src/DFLegacy.Server/QuestDropEndpoints.cs) | `GET/PUT /api/admin/drop/quest-rate`（仅 GM 口 + Admin） |
| [EntranceService.cs](../../src/DFLegacy.Server/EntranceService.cs) | 击杀/通关发放改为 `RollQuestDrop`，套用倍率 |
| [admin-settings.js](../../src/DFLegacy.Server/wwwroot/admin/admin-settings.js) | 「掉落设置」视图：读取当前倍率、保存 |
| [index.html](../../src/DFLegacy.Server/wwwroot/admin/index.html) / [admin.js](../../src/DFLegacy.Server/wwwroot/admin/admin.js) | 导航、视图标题、脚本版本 |
| [WebPanelHosting.cs](../../src/DFLegacy.Server/WebPanelHosting.cs) | GM 口静态白名单加入 `admin-settings.js` |

API：

```text
GET /api/admin/drop/quest-rate        -> { ratePercent, minimum, maximum, persistent }
PUT /api/admin/drop/quest-rate  {"ratePercent":250}
```

只允许正好一个 `ratePercent` 整数属性；越界 400；非 Admin 403；非 GM 口 404。

## 本机自动测试（全部通过）

| 测试 | 结果 |
| --- | --- |
| `--quest-drop-api`（真实子进程服务端 + HTTP） | 9 断言 0 失败：初始 100、改 250、越界 400、多属性 400、普通用户 403、匿名 401、非 GM 口 404、重启后仍 250、设置文件在数据目录 |
| `QuestDropSettingsSmokeTests`（全量冒烟内） | 100 恒等；250% 把 2%→5%、40%→100%；0% 关闭；越界不改值；新实例恢复 50%；无文件时用 `server.json` 的 300% |
| 浏览器逻辑 `gm-mail-browser.test.cjs` | 20/20，新增「掉落设置」视图加载 150、提交 200 并发出 PUT |
| 本机实例页面 | `http://127.0.0.1:48095/` 含 `data-view="settings"`、`questDropForm`、`admin-settings.js`；脚本 200 |
| 本机实例 API | GET 100 → PUT 250 → 文件 `{"RatePercent":250}`；PUT 1010 → 400 |
| 全量冒烟 | 退出码 0，无 FAIL（`.tmp/smoke-full5.log`） |

## 未验证 / 边界

- [未验证] 真实客户端里改倍率后的实际出率体感（需要部署后开客户端；本轮只验证到 API、持久化、
  换算与页面逻辑）。
- 倍率对所有在线会话立即生效，没有按角色/副本的细分。
- 没有审计日志页面；改倍率只写服务端日志（actor + 新值）。
- 发布镜像未做，等待用户确认。
