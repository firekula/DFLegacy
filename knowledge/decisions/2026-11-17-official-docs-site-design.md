# 官方文档站设计：公开玩家文档 + 内部技术/运维/运营文档

状态：设计稿，待用户评审。日期：2026-11-17。源码基线：`eda4f6f`。范围：文档平台与内容组织，不修改游戏代码、服务端行为或现有运行数据。

本设计放在 `knowledge/decisions/` 而不是 `docs/superpowers/specs/`，因为根 `.gitignore` 忽略 `/docs`，新增长期文档必须放入受 Git 管理的 `knowledge/`；现有 `docs/` 文件是历史显式跟踪例外。

## 1. 目标与非目标

### 目标

1. 建立一个正式的公开文档站，面向玩家，包含安装、账号、启动、更新、玩法、FAQ、规则和支持。
2. 建立一套内部文档，面向开发、运维和运营，包含技术、部署/回滚/备份/故障、GM/客服/发奖/封禁/充值/活动/交接。
3. 公开站与内部站共用同一套 Markdown 源和构建工具，但**分开构建、分开产出**，避免内部内容被误发布。
4. 技术文档不复制维护：从现有 `knowledge/handbook` 和根文档按清单生成内部站页面，保留“源文件 -> 站点路径”的可追溯关系。
5. 公开站可发布到 GitHub Pages；同一产物可镜像到 Gitea 侧；内部站产出离线 ZIP，供内网/本地使用。
6. 为后续更新迭代预留版本化、英文、权限和搜索扩展空间，但第一期不做多版本、不做 SaaS 编辑器。

### 非目标

- 不引入 GitBook.com 等 SaaS 作为唯一编辑/托管平台；若以后需要网页编辑，再单独评估 CMS 或 GitBook 私有空间。
- 不把 115 篇 decisions、135 篇 `docs/superpowers` 历史文档整批发布到公开站。
- 不公开技术细节、协议、内部地址、GM/运维操作或账号数据。
- 不做多版本切换、英文站、评论、用户登录、在线分析；这些留到后续。
- 不改动游戏客户端、服务端、PVF/NPK、发布包或线上部署。

## 2. 决策摘要

| 项目 | 决策 |
| --- | --- |
| 生成器 | Docusaurus 3 + pnpm 11 + Node 24；TypeScript 配置 |
| 编辑方式 | Markdown + Git；开发/agent 为主，不做网页所见即所得 |
| 站点数量 | 两个独立构建：`public` 与 `internal` |
| 公开站托管 | GitHub Pages 为主；同一静态产物可镜像到 Gitea 侧 |
| 内部站托管 | 本地/内网静态 ZIP + 本地 serve 脚本；不上公网 |
| 搜索 | Docusaurus 本地中文搜索插件；不依赖 Algolia |
| 版本 | 第一期只保留当前版；按 ACT1 / 第二章章节分区，发行说明单独归档 |
| 语言 | 中文默认；不做英文 |
| 技术文档来源 | `knowledge/handbook` + 根 `ADMIN_API`/`DEPLOYING`/`BUILDING` 等，按 `content-manifest.json` 生成 |
| 玩家/运营内容 | 新写在 `documentation/content/public` 与 `documentation/content/internal` |
| 目录 | 新增受跟踪的 `documentation/`；不使用被忽略的 `docs/` 作为站点源 |

## 3. 信息架构

### 3.1 公开站（玩家）

```text
/                         首页：项目介绍、两章入口、下载/启动、快捷链接
/start/                   开始游戏
  requirements            系统需求
  install                 下载与安装
  account                 注册、登录、找回
  first-launch            首次启动与基本设置
  update                  客户端更新
/gameplay/                玩法指南
  basics                  基础操作与界面
  town                    城镇、NPC、移动
  dungeon-party           副本与组队
  trade-shop              交易与摆摊
  cash-shop               商城与充值
  events                  活动
/faq/                     常见问题
  startup                 启动失败/黑屏/闪退
  update                  更新失败/修复
  network                 连接、掉线、组队
  account                 账号、密码、角色
  performance             卡顿、帧率、内存
/updates/                 更新与公告
  act1                    第一章更新
  chapter-two             第二章更新
  known-issues            已知问题与限制
/rules/                   规则与支持
  terms                   用户协议/使用规则
  fair-play               公平游戏与处罚说明
/support/                 支持与联系方式
```

公开站只允许出现玩家可见内容。技术路径、源码链接、内部地址、GM/运维操作、决策记录不进入公开构建。

### 3.2 内部站（技术/运维/运营）

```text
/                         内部首页：角色快捷入口、当前版本、常用链接
/technical/               技术
  architecture            服务端架构与会话/状态
  protocol                协议、存档、资源契约
  client-native           客户端/原生层/第二章启动链
  desktop                 登录器、更新器、诊断
  resources               资源与 PVF/NPK
  subprojects             PVFStudio/NPKStudio 协作
  build-test              构建、测试、发行
  chapter-adaptation      第一章/第二章适配
  social-bots             区域频道与假人
  onboarding              agent/开发接手步骤
/ops/                     运维
  deployment              安装、首次启动、端口/网络
  upgrade-rollback        升级、回滚、维护窗口
  backup-restore          备份、恢复、隔离演练
  monitoring              监控、告警、日志（第二期补写）
  security-secrets        安全、密钥、权限边界（第二期补写）
  incident-response       事故响应与复盘（第二期补写）
  troubleshooting         诊断顺序与常见故障
  client-updates          客户端更新/修复
/business/                运营
  gm-console              GM 后台使用
  account-support        账号/角色客服
  rewards-mail           发奖、全服邮件、补发
  moderation-appeals     封禁、申诉、处罚
  payments-refunds       充值、退款、对账
  events-announcements   活动、公告、排期
  customer-service       客服话术与升级路径
  shift-handover         值班与交接班
/reference/               参考
  current-state           当前基线/未完成项
  glossary                术语
  decision-index          决策索引（指向 Git/摘要）
  release-ledger          发布台账（第二期补写）
  compatibility-matrix    兼容矩阵（第二期补写）
```

“监控/安全/灾备/发布台账/兼容矩阵”在当前仓库本来缺失或只有原则，第一期只留结构和入口，第二期按 2026-11-17 文档缺口审查的优先级补写；不在第一期伪装成已完成。

## 4. 目录与构建架构

```text
documentation/
  package.json
  pnpm-lock.yaml
  docusaurus.config.shared.ts     # 主题、搜索、公共插件配置
  docusaurus.config.public.ts     # 公开站：只读 content/public
  docusaurus.config.internal.ts   # 内部站：只读 content/internal
  sidebars.public.ts
  sidebars.internal.ts
  content-manifest.json            # 现有文档 -> 内部站路径/标题/排序
  content/
    public/                        # 玩家文档，手写，唯一公开来源
      intro.md
      start/...
      gameplay/...
      faq/...
      updates/...
      rules/...
      support/...
      assets/...
    internal/                      # 运维/运营 SOP，手写
      intro.md
      ops/...
      business/...
      reference/...
    internal/_generated/           # 构建生成，gitignored
  scripts/
    prepare-content.mjs            # 按 manifest 复制/重写内部技术文档
    check-content.mjs              # 公共/内部边界、元数据、绝对路径检查
    check-links.mjs                # 构建后链接检查
    serve-internal.ps1             # 本地静态服务
    serve-internal.sh
  src/
    pages/index.tsx                # 公开首页
    pages/internal/index.tsx       # 内部首页（仅内部构建）
    css/custom.css
  static/
    img/logo.svg
    img/...                        # 公开站图片/图标
  README.md                        # 编辑、构建、部署、内容边界
```

`documentation/` 必须加入 Git。`documentation/build/`、`.docusaurus/`、`node_modules/`、`content/internal/_generated/` 加入 `documentation/.gitignore`；不从被忽略的 `docs/` 读取站点源。

### 4.1 两个构建

```powershell
pnpm install --frozen-lockfile
pnpm build:public    # 输出 documentation/build/public
pnpm build:internal  # 输出 documentation/build/internal
```

- 公开构建的 Docusaurus 配置只加载 `content/public`；`onBrokenLinks: 'throw'`。
- 内部构建只加载 `content/internal`（含 `_generated`）；`onBrokenLinks: 'warn'` 并输出报告，最终由 `check-links` 决定是否阻断。
- 内部构建设置 `noIndex`、不生成 sitemap；公开构建生成 sitemap/robots。
- 两个配置共享主题、搜索、代码高亮、MDX 组件和 CSS，避免双份维护。

## 5. 内容来源与迁移

### 5.1 手写内容

- `content/public/**`：玩家文档唯一来源，直接编辑 Markdown。
- `content/internal/ops/**`、`content/internal/business/**`、`content/internal/reference/**`：内部 SOP 唯一来源，直接编辑 Markdown。
- 公开更新公告由 `docs/releases` 和 `CHANGELOG` 摘要改写后手写；不把发行说明原样复制进公开站。
- 每篇页面使用统一 frontmatter：

```yaml
---
title: 页面标题
description: 一句话摘要
audience: user | technical | ops | business
status: draft | active | deprecated
chapter: act1 | chapter-two | shared
last_verified: 2026-11-17
owner: docs | tech | ops | business
---
```

生成页面的 frontmatter 由 manifest 注入，不手改 `_generated`。

### 5.2 内部技术文档生成

`content-manifest.json` 是唯一映射表，至少包含：

```json
{
  "version": 1,
  "entries": [
    {
      "source": "knowledge/handbook/server-architecture.md",
      "target": "technical/architecture.md",
      "title": "服务端架构与玩法定位",
      "order": 10,
      "audience": "technical"
    }
  ]
}
```

第一批映射建议：

| 来源 | 内部站目标 | 分区 |
| --- | --- | --- |
| `knowledge/handbook/server-architecture.md` | `technical/architecture.md` | 技术 |
| `knowledge/handbook/protocol-and-data.md` | `technical/protocol.md` | 技术 |
| `knowledge/handbook/client-and-native.md` | `technical/client-native.md` | 技术 |
| `knowledge/handbook/desktop-apps.md` | `technical/desktop.md` | 技术 |
| `knowledge/handbook/resources-and-pvf.md` | `technical/resources.md` | 技术 |
| `knowledge/handbook/resource-subprojects.md` | `technical/subprojects.md` | 技术 |
| `knowledge/handbook/build-test-release.md` | `technical/build-test.md` | 技术 |
| `knowledge/handbook/testing-and-evidence.md` | `technical/testing.md` | 技术 |
| `knowledge/handbook/chapter-adaptation.md` | `technical/chapter-adaptation.md` | 技术 |
| `knowledge/handbook/social-world-and-bots.md` | `technical/social-bots.md` | 技术 |
| `knowledge/handbook/agent-workflow.md` | `technical/onboarding.md` | 技术 |
| `knowledge/handbook/operations.md` | `ops/operations.md` | 运维 |
| `knowledge/handbook/web-and-admin.md` | `ops/admin-web.md` | 运维 |
| `knowledge/handbook/configuration.md` | `technical/configuration.md` | 技术 |
| `knowledge/handbook/repository-map.md` | `technical/repository-map.md` | 技术 |
| `knowledge/handbook/current-state.md` | `reference/current-state.md` | 参考 |
| `knowledge/handbook/glossary.md` | `reference/glossary.md` | 参考 |
| `knowledge/handbook/pvf-dump.md` | `technical/pvf-dump.md` | 技术 |
| `knowledge/handbook/tools-reference.md` | `technical/tools.md` | 技术 |
| `knowledge/handbook/reference/README.md` | `reference/reference-tables.md` | 参考 |
| `knowledge/handbook/README.md` | `technical/home.md` | 技术 |
| `ADMIN_API.md` | `ops/admin-api.md` | 运维/运营 |
| `DEPLOYING.md` | `ops/deployment.md` | 运维 |
| `DEPLOYMENT_MANAGER.md` | `ops/manager.md` | 运维/运营 |
| `BUILDING.md` | `technical/building.md` | 技术 |
| `CLIENT_UPDATES_V4.md` | `ops/client-updates.md` | 运维 |
| `PARTY_NETWORK_DIAGNOSTICS.md` | `ops/party-network.md` | 运维 |

`knowledge/handbook/README.md` 生成后需检查其“手册目录”是否适合作为内部技术首页；如不适合，改用 `content/internal/intro.md` 手写首页，并在 manifest 中移除该条。不能手改 `_generated`。

### 5.3 不进入公开站的内容

- `knowledge/handbook/**`、`knowledge/decisions/**`、`docs/superpowers/**`、`docs/acceptance/**`。
- 源码链接、协议细节、内部端口、管理地址、GM 命令、账号/存档操作。
- 任何含 `G:/`、`D:/`、`C:/` 外部路径或未脱敏截图的页面。

## 6. 链接、图片与路径处理

`prepare-content.mjs` 的职责：

1. 清空 `content/internal/_generated`，避免旧页面残留。
2. 读取 manifest，复制源 Markdown 到目标路径，注入 frontmatter（标题、audience、status、source、last_verified）。
3. 建立源路径 -> 目标路径的映射，重写 Markdown 相对链接：
   - 指向同一内部站的 `.md` -> 目标站内路径，保留锚点。
   - 指向 `src/`、`tests/`、`tools/`、`protocol/`、`third_party/` -> `REPO_WEB_URL/<path>`；未配置 `REPO_WEB_URL` 时降级为不可点击的代码文本。
   - 指向 `knowledge/decisions/`、`docs/acceptance/` -> 内部决策索引/Git URL，不批量复制。
   - 指向 `G:/`、`D:/`、`C:/` 的绝对路径 -> 转为代码文本并加“附件不在仓库”的提示，不生成失效链接。
4. 复制被引用的本地图片到 `_generated/assets/<相对路径>`，重写图片路径；缺失图片写入 `_generated/missing-assets.json`，由检查脚本报告。
5. 不修改源文件；所有转换只在生成目录进行。

公开站：

- 只允许站内相对链接、公开外链和 `content/public/assets` 图片。
- 发现 `src/`、`knowledge/`、`G:/`、`D:/`、`internal` 等内部路径时，`check-content` 直接失败。
- 首发图片使用脱敏后的客户端截图/示意图；缺图页面默认 `status: draft`，不进入公开导航的“已完成”分组。

## 7. 主题、搜索与元数据

- 采用 Docusaurus classic 主题；自定义 CSS 使用 DFLegacy 金棕色点缀，正文保持高可读性，不把启动器像素字体用于大段文字。
- 顶栏：文档、更新、支持；右侧：搜索、GitHub/Gitea 仓库链接（公开站只链公开仓库，不链内部地址）。
- 中文搜索：`@easyops-cn/docusaurus-search-local`，配置 `language: ['zh']`、`hashed: true`；构建后人工验证中文词、英文符号和代码片段搜索。
- 公开站设置 `title`、`description`、`og:image`（有品牌图后再加）、sitemap、robots。
- 内部站设置 `noIndex`、`robots: noindex, nofollow`，页面顶部显示“内部资料，禁止外传”的提示。
- 代码块、表格、提示块使用 Docusaurus 内置能力；不引入重型 UI 组件。

## 8. 部署与 CI

### 8.1 公开站

构建产物是纯静态目录，可发布到任意 Pages/Nginx。第一阶段支持：

1. **GitHub Pages**：发布到 `gh-pages` 分支；`DOCS_SITE_URL` 和 `DOCS_BASE_URL` 由环境变量提供。项目页默认 `baseUrl: /DFLegacy/`；自定义域名时改为 `/`。
2. **Gitea 侧镜像**：发布到 `pages` 分支。Gitea 1.26 本身不能假设内置 Pages；由现有 Nginx 或第三方 pages server 提供静态服务。若该服务不可用，Gitea 侧只保留分支产物，不作为上线阻塞项。

同一静态产物要发两个目标时，`baseUrl` 必须一致；如果两个目标路径不同，分别构建并记录 baseUrl，不能手工改 HTML。

### 8.2 内部站

- `pnpm build:internal` 产出 `documentation/build/internal`。
- CI 打包为 `artifacts/docs-internal-<commit>.zip`，包含 `serve-internal.ps1`/`.sh` 和 `README.txt`。
- 内部包不发布到 Pages、不创建公开 Release、不放入公开仓库附件；通过内网共享/受控文件分发。
- 因 Docusaurus 使用绝对 baseUrl，不能直接双击 `index.html`；必须用本地 HTTP 服务打开。

### 8.3 CI

第一阶段在 `.gitea/workflows/docs.yml` 增加文档任务；实现计划需先验证 runner 的 Node/pnpm 可用性：

```text
install: corepack enable && pnpm install --frozen-lockfile
check:   pnpm check:content && pnpm build:public && pnpm build:internal && pnpm check:links
package: 上传 docs-internal-<commit>.zip
publish: 把 build/public 推到 pages 分支；配置 GH_PAGES_TOKEN 时再推 gh-pages
```

- 公开站发布凭据只从 CI secret 读取；不写入仓库、日志、构建产物。
- 内部构建与公开构建在同一 job 中分开输出；公开 job 不允许读取 `content/internal`。
- 文档 CI 失败不自动部署；修复后重新运行。
- 第二阶段再加：链接/锚点全量检查、frontmatter 必填检查、参考表重建、过期检查。

## 9. 内容计划与阶段

### 第一阶段（本设计对应的实施范围）

1. 建立 `documentation/` 工程、双构建、主题、导航、搜索、内容检查、内部 serve 脚本。
2. 建立 `content-manifest.json`，迁移 21 页 handbook + 6 份根文档到内部站；修复链接/图片/绝对路径。
3. 公开站首批页面：
   - 首页、系统需求、下载安装、账号、首次启动、更新；
   - 基础操作、城镇、副本与组队、交易与摆摊、商城与充值；
   - 启动/更新/网络/账号/性能 FAQ；
   - ACT1/第二章更新摘要、已知问题、规则与支持。
4. 内部站首批运营 SOP：
   - GM 后台、账号/角色客服、发奖/全服邮件、封禁/申诉、充值/退款、活动/公告、值班交接。
5. 产出公开静态站与内部 ZIP，完成构建、链接、移动端、公开/内部隔离检查。
6. 部署 GitHub Pages；Gitea 侧按可用 Pages 服务镜像或记录为待部署。

### 第二阶段（不在本设计实施）

- 监控告警、灾难恢复、安全/密钥、事故复盘、发布台账、兼容矩阵。
- decisions 主题索引与状态标记、文档 CI 强化、参考表自动重建。
- 视需要启用 Docusaurus 版本化（`docs:version`）、英文、GitBook/CMS 编辑器。

## 10. 验收标准

### 构建与隔离

- `pnpm install --frozen-lockfile` 成功。
- `pnpm build:public` 退出码 0，无 broken link；产物中不出现 `content/internal`、`knowledge/`、`src/`、`G:/`、`D:/` 内部路径。
- `pnpm build:internal` 退出码 0；生成页面数量与 manifest 条目一致；缺失图片/绝对路径有报告。
- `pnpm check:content` 对公开站内部路径、缺失 frontmatter、无效 audience/status 失败。
- 内部 ZIP 可在无网络环境下用自带 serve 脚本打开，搜索、导航、代码块、表格正常。

### 站点体验

- 公开站：首页、移动端、搜索、404、更新公告、下载/支持链接可用；中文搜索能命中中文标题和正文。
- 内部站：技术/运维/运营/参考四区导航完整；生成页面的源码链接指向配置的 Git 网页地址；不出现失效的 `D:/` 链接。
- 公开站无内部页面、内部地址、协议细节、GM/运维操作。
- 页面 frontmatter 完整，`status: draft` 页面不进入公开导航的“已完成”分组。

### 部署

- GitHub Pages 可访问，HTTPS 正常，`baseUrl` 与站点路径一致。
- Gitea 镜像：若 Pages 服务可用，地址可访问；若不可用，记录“静态分支已产出、Pages 服务待部署”，不宣称已完成。
- 内部包未上传到任何公开位置。

## 11. 风险与约束

| 风险 | 处理 |
| --- | --- |
| Gitea 1.26 不能依赖内置 Pages | 先保证 GitHub Pages；Gitea 侧用 Nginx/第三方 pages server，作为独立部署验证项 |
| 现有文档相对链接/绝对路径多 | 用 manifest + prepare/check 脚本；先迁移 21 页 handbook，不整批搬 430 篇 |
| 公开/内部内容泄漏 | 两次独立构建；公开配置只加载 `content/public`；check-content 扫描内部路径 |
| 技术文档复制后漂移 | 生成页只读；源仍是 `knowledge/handbook`；不手改 `_generated` |
| 玩家文档内容不准 | 首批从现有代码/验收记录改写；用户评审后才从 draft 转 active |
| 截图/图片缺失或含隐私 | 先用脱敏图/示意图；缺图标 draft；不使用玩家账号、IP、存档截图 |
| Docusaurus/插件升级 | 锁定 pnpm lock 和主版本；升级单独验证构建/搜索/移动端 |
| Gitea/GitHub baseUrl 不一致 | 用环境变量分别构建并记录；不手改 HTML |

## 12. 实施顺序（交给 writing-plans 细化）

1. 建 `documentation/` 工程与双配置，能本地 `build:public`/`build:internal`。
2. 建内容检查/链接重写脚本，先用 3 页 handbook 做迁移验证。
3. 全量迁移 21 页 handbook + 6 份根文档，修复链接/图片。
4. 建公开站信息架构与首批玩家文档。
5. 建内部运营 SOP 首批页面。
6. 主题、搜索、移动端、公开/内部隔离检查。
7. GitHub Pages 发布；Gitea 镜像/内部 ZIP 产出。
8. 内容评审与状态从 draft 转 active；记录未覆盖项。

## 13. 明确未决的部署配置（不是设计缺口）

以下值在实施时通过环境变量或仓库设置确定，不在本设计写死：

- `DOCS_SITE_URL`：公开站 canonical URL。
- `DOCS_BASE_URL`：GitHub Pages/ Gitea 镜像的路径前缀；项目页与自定义域名不同。
- `REPO_WEB_URL`：内部站源码链接使用的 Git 网页地址；未设置时源链接降级为代码文本。
- GitHub Pages/Gitea pages server 的实际仓库、分支和凭据。

这些是部署参数，不改变站点结构或内容边界。
