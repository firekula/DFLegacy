# DFLegacy 官方文档站 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一个公开玩家文档站和一套内部技术/运维/运营离线文档，两者共用 Markdown 源但分别构建、分别发布。

**Architecture:** 在受跟踪的 `documentation/` 下建立 Docusaurus 3 工程；`content/public` 与 `content/internal` 分别构建。现有 `knowledge/handbook` 和根文档通过 `content-manifest.json` 生成内部站页面；玩家文档和运营 SOP 新写。公开站发布到 GitHub Pages，并可镜像到 Gitea 的静态分支；内部站打包成离线 ZIP。

**Tech Stack:** Node 24、pnpm 11、Docusaurus 3（TypeScript）、`@easyops-cn/docusaurus-search-local`、Node 内置 `node:test`、GitHub Actions、Gitea Actions。

**Spec:** `knowledge/decisions/2026-11-17-official-docs-site-design.md`

本计划放在 `knowledge/plans/` 而不是默认的 `docs/superpowers/plans/`，因为根 `.gitignore` 忽略 `/docs`；这里能保证计划随仓库受 Git 管理。

## Global Constraints

- 只改文档站相关文件；不改游戏客户端、服务端、PVF/NPK、发布包或线上部署。
- 新站点目录是 `documentation/`；不要把站点源放进被 `.gitignore` 忽略的 `docs/`。
- 公开站只加载 `content/public`；内部站只加载 `content/internal`。公开构建不得读取或复制内部内容。
- 现有技术文档仍以 `knowledge/handbook`、`ADMIN_API.md`、`DEPLOYING.md`、`BUILDING.md` 等为源；`content/internal/_generated` 是生成物，永不手改。
- 中文为默认语言；第一期不做多版本、英文、评论、登录或 SaaS 编辑器。
- Node 24、pnpm 11；提交 `pnpm-lock.yaml`，CI 使用 `--frozen-lockfile`。
- 每个任务结束运行该任务的验证命令并提交；提交信息使用 `docs:` 前缀。
- 公开站不得出现 `knowledge/`、`src/`、`G:/`、`D:/`、`C:/`、`internal` 等内部路径；发现即失败。
- 内部页面的源码链接通过 `REPO_WEB_URL` 配置；未配置时降级为代码文本，不生成失效链接。
- 本轮不补写监控/灾备/安全/发布台账/兼容矩阵的完整内容；只保留结构和第二期入口。
- 所有命令从仓库根目录执行，除非步骤明确 `cd documentation`。

---

### Task 1: 搭建 Docusaurus 双构建骨架

**Files:**
- Create: `documentation/package.json`
- Create: `documentation/docusaurus.config.shared.ts`
- Create: `documentation/docusaurus.config.public.ts`
- Create: `documentation/docusaurus.config.internal.ts`
- Create: `documentation/sidebars.public.ts`
- Create: `documentation/sidebars.internal.ts`
- Create: `documentation/content/public/intro.md`
- Create: `documentation/content/internal/intro.md`
- Create: `documentation/src/public/pages/index.tsx`
- Create: `documentation/src/internal/pages/index.tsx`
- Create: `documentation/src/css/custom.css`
- Create: `documentation/static/img/logo.svg`
- Create: `documentation/.gitignore`
- Create: `documentation/README.md`
- Test: `documentation/build/public/index.html`、`documentation/build/internal/index.html`

**Interfaces:**
- Produces: `pnpm --dir documentation build:public` 和 `pnpm --dir documentation build:internal`，分别输出 `documentation/build/public`、`documentation/build/internal`。
- Produces: `createConfig('public' | 'internal'): Config`，供两个配置入口复用。

- [ ] **Step 1: 生成 Docusaurus 基础工程**

Run:

```powershell
pnpm create docusaurus@latest documentation classic --typescript --package-manager pnpm --skip-install
cd documentation
pnpm install
pnpm add @easyops-cn/docusaurus-search-local
```

Expected:
- `documentation/package.json`、`docusaurus.config.ts`、`sidebars.ts`、`src/`、`static/` 生成。
- `pnpm install` 退出码 0。

- [ ] **Step 2: 清理脚手架默认内容**

删除脚手架默认的 `documentation/docs/`、`documentation/blog/`、`documentation/src/pages/`、`documentation/src/components/`、`docusaurus.config.ts`、`sidebars.ts`。保留 `src/css/custom.css`、`static/img/`。

Run:

```powershell
Remove-Item -Recurse -Force documentation/docs, documentation/blog
Remove-Item -Recurse -Force documentation/src/pages, documentation/src/components
Remove-Item -Force documentation/docusaurus.config.ts, documentation/sidebars.ts
```

- [ ] **Step 3: 写 package scripts**

把 `documentation/package.json` 的 `scripts` 替换为：

```json
{
  "scripts": {
    "start:public": "docusaurus start --config docusaurus.config.public.ts --port 3001",
    "start:internal": "docusaurus start --config docusaurus.config.internal.ts --port 3002",
    "prepare:internal": "node scripts/prepare-content.mjs --audience internal",
    "build:public": "docusaurus build --config docusaurus.config.public.ts --out-dir build/public",
    "build:internal": "pnpm prepare:internal && docusaurus build --config docusaurus.config.internal.ts --out-dir build/internal",
    "check:content:public": "node scripts/check-content.mjs --audience public",
    "check:content:internal": "node scripts/check-content.mjs --audience internal",
    "check:content": "pnpm check:content:public && pnpm check:content:internal",
    "check:links": "node scripts/check-links.mjs",
    "test:scripts": "node --test scripts/lib/links.test.mjs",
    "serve:internal": "node scripts/serve-internal.mjs build/internal"
  },
  "packageManager": "pnpm@11.8.0"
}
```

- [ ] **Step 4: 写共享配置与两个入口**

`documentation/docusaurus.config.shared.ts`：

```ts
import type {Config} from '@docusaurus/types';
import type {Options as PresetOptions} from '@docusaurus/preset-classic';

export type DocsAudience = 'public' | 'internal';

export function createConfig(audience: DocsAudience): Config {
  const isPublic = audience === 'public';
  const siteUrl = process.env.DOCS_SITE_URL ?? 'https://firekula.github.io/DFLegacy';
  const baseUrl = process.env.DOCS_BASE_URL ?? '/DFLegacy/';
  const repoUrl = process.env.DOCS_REPO_URL ?? 'https://github.com/firekula/DFLegacy';
  const docsPath = isPublic ? 'content/public' : 'content/internal';
  const sidebarPath = isPublic ? './sidebars.public.ts' : './sidebars.internal.ts';

  return {
    title: isPublic ? 'DFLegacy 文档' : 'DFLegacy 内部文档',
    tagline: isPublic ? '第一章 ACT1 / 第二章 Chapter Two' : '技术、运维与运营手册',
    url: siteUrl,
    baseUrl,
    srcDir: isPublic ? 'src/public' : 'src/internal',
    onBrokenLinks: isPublic ? 'throw' : 'warn',
    onBrokenMarkdownLinks: isPublic ? 'throw' : 'warn',
    onBrokenAnchors: isPublic ? 'throw' : 'warn',
    favicon: 'img/favicon.ico',
    i18n: {
      defaultLocale: 'zh-Hans',
      locales: ['zh-Hans'],
    },
    presets: [
      [
        'classic',
        {
          docs: {
            path: docsPath,
            routeBasePath: 'docs',
            sidebarPath,
            editUrl: `${repoUrl}/edit/main/documentation/`,
            showLastUpdateTime: false,
          },
          blog: false,
          theme: {
            customCss: './src/css/custom.css',
          },
          sitemap: isPublic ? {changefreq: 'weekly', priority: 0.5} : false,
        } satisfies PresetOptions,
      ],
    ],
    plugins: [
      [
        '@easyops-cn/docusaurus-search-local',
        {
          hashed: true,
          language: ['zh'],
          docsRouteBasePath: 'docs',
          indexBlog: false,
        },
      ],
    ],
    headTags: isPublic
      ? []
      : [
          {
            tagName: 'meta',
            attributes: {name: 'robots', content: 'noindex,nofollow'},
          },
        ],
    themeConfig: {
      navbar: {
        title: isPublic ? 'DFLegacy 文档' : 'DFLegacy 内部文档',
        logo: {alt: 'DFLegacy', src: 'img/logo.svg'},
        items: isPublic
          ? [{type: 'doc', docId: 'intro', label: '文档', position: 'left'}]
          : [
              {type: 'doc', docId: 'intro', label: '内部首页', position: 'left'},
              {to: '/docs/technical/home', label: '技术', position: 'left'},
              {to: '/docs/ops/operations', label: '运维', position: 'left'},
            ],
      },
      footer: {
        style: 'dark',
        links: isPublic
          ? [
              {
                title: '文档',
                items: [{label: '文档首页', to: '/docs/intro'}],
              },
              {
                title: '项目',
                items: [{label: 'GitHub', href: repoUrl}],
              },
            ]
          : [
              {
                title: '内部',
                items: [
                  {label: '技术', to: '/docs/technical/home'},
                  {label: '运维', to: '/docs/ops/operations'},
                ],
              },
            ],
        copyright: `DFLegacy ${new Date().getFullYear()} · 内部资料禁止外传`,
      },
      prism: {
        theme: require('prism-react-renderer').themes.github,
        darkTheme: require('prism-react-renderer').themes.dracula,
      },
    },
  };
}
```

`documentation/docusaurus.config.public.ts`：

```ts
import {createConfig} from './docusaurus.config.shared';

export default createConfig('public');
```

`documentation/docusaurus.config.internal.ts`：

```ts
import {createConfig} from './docusaurus.config.shared';

export default createConfig('internal');
```

- [ ] **Step 5: 写首页、示例页和基础样式**

`documentation/content/public/intro.md`：

```md
---
title: DFLegacy 文档
description: DFLegacy 第一章与第二章的玩家文档入口
audience: user
status: active
chapter: shared
last_verified: 2026-11-17
owner: docs
---

# DFLegacy 文档

这里提供安装、账号、启动、更新、玩法与常见问题说明。第一次进入请从 [下载与安装](start/install.md) 开始。

- 第一章 ACT1：保留独立客户端与原生补丁链。
- 第二章 Chapter Two：使用第二章专用启动器和资源。
- 遇到启动/更新问题：先看 [启动问题](faq/startup.md) 与 [更新问题](faq/update.md)。
```

`documentation/content/internal/intro.md`：

```md
---
title: DFLegacy 内部文档
description: 技术、运维与运营内部手册
audience: technical
status: active
chapter: shared
last_verified: 2026-11-17
owner: docs
---

# DFLegacy 内部文档

内部资料，禁止外传。按角色进入：

- [技术手册](technical/home.md)
- [运维手册](ops/operations.md)
- [参考](reference/current-state.md)
```

`documentation/src/public/pages/index.tsx`：

```tsx
import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';

export default function Home(): React.JSX.Element {
  return (
    <Layout title="DFLegacy 文档" description="安装、账号、玩法与常见问题">
      <main className="container margin-vert--lg">
        <h1>DFLegacy 文档</h1>
        <p>第一章 ACT1 与第二章 Chapter Two 的玩家文档。</p>
        <p>
          <Link className="button button--primary" to="/docs/intro">
            进入文档
          </Link>
        </p>
      </main>
    </Layout>
  );
}
```

`documentation/src/internal/pages/index.tsx`：

```tsx
import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';

export default function InternalHome(): React.JSX.Element {
  return (
    <Layout title="DFLegacy 内部文档" description="技术、运维与运营手册">
      <main className="container margin-vert--lg">
        <div className="internal-banner">内部资料，禁止外传</div>
        <h1>DFLegacy 内部文档</h1>
        <ul>
          <li><Link to="/docs/technical/home">技术手册</Link></li>
          <li><Link to="/docs/ops/operations">运维手册</Link></li>
          <li><Link to="/docs/reference/current-state">当前基线</Link></li>
        </ul>
      </main>
    </Layout>
  );
}
```

`documentation/static/img/logo.svg`：

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="DFLegacy">
  <rect width="64" height="64" rx="12" fill="#2b1d12"/>
  <path d="M16 18h20c9 0 14 5 14 14s-5 14-14 14H16V18zm10 8v12h9c4 0 6-2 6-6s-2-6-6-6h-9z" fill="#d7a76a"/>
</svg>
```

`documentation/src/css/custom.css`：

```css
:root {
  --ifm-color-primary: #8a5a2b;
  --ifm-color-primary-dark: #7c5026;
  --ifm-color-primary-darker: #754b24;
  --ifm-color-primary-darkest: #613e1e;
  --ifm-color-primary-light: #986532;
  --ifm-color-primary-lighter: #9f6b35;
  --ifm-color-primary-lightest: #b07a42;
  --ifm-code-font-size: 95%;
}

[data-theme='dark'] {
  --ifm-color-primary: #d7a76a;
  --ifm-color-primary-dark: #cf9955;
  --ifm-color-primary-darker: #cb914a;
  --ifm-color-primary-darkest: #b37835;
  --ifm-color-primary-light: #dfb57f;
  --ifm-color-primary-lighter: #e3bd8b;
  --ifm-color-primary-lightest: #efd3ae;
}

.internal-banner {
  padding: 0.5rem 1rem;
  background: #7a2e2e;
  color: #fff;
  text-align: center;
  font-weight: 600;
}
```

- [ ] **Step 6: 写 sidebars 最小版**

`documentation/sidebars.public.ts`：

```ts
import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  public: ['intro'],
};

export default sidebars;
```

`documentation/sidebars.internal.ts`：

```ts
import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  internal: ['intro'],
};

export default sidebars;
```

- [ ] **Step 7: 加 `.gitignore` 与 README**

`documentation/.gitignore`：

```text
node_modules/
.docusaurus/
build/
content/internal/_generated/
```

`documentation/README.md`：

```md
# DFLegacy 文档站

- 公开站：`pnpm build:public` → `build/public`
- 内部站：`pnpm build:internal` → `build/internal`
- 本地预览：`pnpm start:public` / `pnpm start:internal`
- 内容边界：`content/public` 只放玩家文档；`content/internal` 放技术/运维/运营。
- 技术文档由 `content-manifest.json` 生成到 `content/internal/_generated`，不要手改生成目录。
```

- [ ] **Step 8: 构建验证**

Run:

```powershell
cd documentation
pnpm build:public
if ($LASTEXITCODE) { throw 'public build failed' }
pnpm build:internal
if ($LASTEXITCODE) { throw 'internal build failed' }
```

Expected: 两条命令退出码 0；`documentation/build/public/index.html` 与 `documentation/build/internal/index.html` 存在。

- [ ] **Step 9: Commit**

```powershell
git add documentation
git commit -m "docs: scaffold public and internal Docusaurus sites"
```

---

### Task 2: 内容清单、生成与边界检查脚本

**Files:**
- Create: `documentation/content-manifest.json`
- Create: `documentation/scripts/lib/frontmatter.mjs`
- Create: `documentation/scripts/lib/links.mjs`
- Create: `documentation/scripts/lib/links.test.mjs`
- Create: `documentation/scripts/prepare-content.mjs`
- Create: `documentation/scripts/check-content.mjs`
- Create: `documentation/scripts/check-links.mjs`
- Create: `documentation/scripts/serve-internal.mjs`
- Modify: `documentation/package.json`

**Interfaces:**
- `prepare-content.mjs --audience internal` 读取 `content-manifest.json`，清空/生成 `content/internal/_generated`，写 `_generated/manifest.json`。
- `check-content.mjs --audience public|internal` 返回 0/1；公开构建发现内部路径即失败。
- `check-links.mjs` 返回 0/1；检查 `build/public` 与 `build/internal` 的本地链接。
- `serve-internal.mjs <dir> [port]` 用 Node HTTP 服务静态目录，默认端口 4173。

- [ ] **Step 1: 写链接重写失败测试**

`documentation/scripts/lib/links.test.mjs`：

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import {rewriteLinks} from './links.mjs';

const repoRoot = path.resolve('/repo');
const sourceFile = path.resolve('/repo/knowledge/handbook/server-architecture.md');
const targetFile = path.resolve('/docs/content/internal/_generated/technical/architecture.md');
const sourceToTarget = new Map([
  [sourceFile, targetFile],
  [path.resolve('/repo/knowledge/handbook/protocol-and-data.md'),
   path.resolve('/docs/content/internal/_generated/technical/protocol.md')],
]);

test('rewrites included doc links and keeps anchors', () => {
  const input = '见 [协议](../handbook/protocol-and-data.md#存档与事务)。';
  const out = rewriteLinks(input, {sourceFile, targetFile, sourceToTarget, repoRoot, repoWebUrl: ''});
  assert.match(out, /\]\(protocol\.md#存档与事务\)/);
});

test('rewrites repo source links to configured web URL', () => {
  const input = '见 [代码](../../src/DFLegacy.Server/Program.cs)。';
  const out = rewriteLinks(input, {
    sourceFile, targetFile, sourceToTarget, repoRoot,
    repoWebUrl: 'https://example.test/repo/blob/main',
  });
  assert.match(out, /https:\/\/example\.test\/repo\/blob\/main\/src\/DFLegacy\.Server\/Program\.cs/);
});

test('turns external absolute paths into code text', () => {
  const input = '见 [旧图](G:/archive/shot.png)。';
  const out = rewriteLinks(input, {sourceFile, targetFile, sourceToTarget, repoRoot, repoWebUrl: ''});
  assert.match(out, /`G:\/archive\/shot\.png`/);
  assert.doesNotMatch(out, /\]\(G:/);
});
```

Run:

```powershell
cd documentation
node --test scripts/lib/links.test.mjs
```

Expected: FAIL（`links.mjs` 不存在）。

- [ ] **Step 2: 实现 `frontmatter.mjs` 与 `links.mjs`**

`documentation/scripts/lib/frontmatter.mjs`：

```js
export function buildFrontmatter({title, description, order, docAudience, source}) {
  return [
    '---',
    `title: ${JSON.stringify(title)}`,
    `description: ${JSON.stringify(description ?? title)}`,
    `sidebar_position: ${order ?? 999}`,
    `audience: ${JSON.stringify(docAudience ?? 'technical')}`,
    'status: active',
    `source: ${JSON.stringify(source)}`,
    'last_verified: 2026-11-17',
    '---',
    '',
  ].join('\n');
}

export function hasFrontmatter(text) {
  return text.startsWith('---\n') || text.startsWith('---\r\n');
}
```

`documentation/scripts/lib/links.mjs`：

```js
import path from 'node:path';

function toPosix(value) {
  return value.split(path.sep).join('/');
}

function codeText(value) {
  return '`' + value.replaceAll('`', '\\`') + '`';
}

export function rewriteLinks(text, {sourceFile, targetFile, sourceToTarget, repoRoot, repoWebUrl}) {
  const sourceDir = path.dirname(sourceFile);
  return text.replace(/\]\(([^)]+)\)/g, (match, raw) => {
    const target = raw.trim();
    if (/^(https?:|mailto:|tel:|#)/i.test(target)) return match;

    const hashIndex = target.indexOf('#');
    const rawPath = (hashIndex >= 0 ? target.slice(0, hashIndex) : target).replace(/^<|>$/g, '');
    const anchor = hashIndex >= 0 ? target.slice(hashIndex) : '';
    if (!rawPath) return match;

    const absolute = path.resolve(sourceDir, rawPath);
    if (sourceToTarget.has(absolute)) {
      const relative = toPosix(path.relative(path.dirname(targetFile), sourceToTarget.get(absolute)));
      return `](${relative}${anchor})`;
    }

    const repoRelative = toPosix(path.relative(repoRoot, absolute));
    const isImage = /\.(png|jpe?g|gif|svg|webp)$/i.test(rawPath);
    if (!repoRelative.startsWith('..') && !path.isAbsolute(repoRelative) && !isImage) {
      if (repoWebUrl) return `](${repoWebUrl.replace(/\/+$/, '')}/${repoRelative}${anchor})`;
      return `](${codeText(repoRelative + anchor)})`;
    }

    if (/^[A-Za-z]:[\\/]/.test(rawPath)) {
      return `](${codeText(rawPath)}（附件不在仓库）)`;
    }

    return match;
  });
}
```

- [ ] **Step 3: 运行链接重写测试**

Run:

```powershell
cd documentation
node --test scripts/lib/links.test.mjs
```

Expected: PASS，3 tests / 0 failures.

- [ ] **Step 4: 写 `content-manifest.json` 初始两项**

```json
{
  "version": 1,
  "entries": [
    {
      "source": "knowledge/handbook/server-architecture.md",
      "target": "technical/architecture.md",
      "title": "服务端架构与玩法定位",
      "order": 10,
      "scope": "internal",
      "docAudience": "technical"
    },
    {
      "source": "knowledge/handbook/operations.md",
      "target": "ops/operations.md",
      "title": "部署、备份、回滚与诊断",
      "order": 10,
      "scope": "internal",
      "docAudience": "ops"
    }
  ]
}
```

- [ ] **Step 5: 写 `prepare-content.mjs`**

```js
import {readFile, writeFile, mkdir, rm} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {buildFrontmatter, hasFrontmatter} from './lib/frontmatter.mjs';
import {rewriteLinks} from './lib/links.mjs';

const documentationRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(documentationRoot, '..');
const args = process.argv.slice(2);
const audienceIndex = args.indexOf('--audience');
const audience = audienceIndex >= 0 ? args[audienceIndex + 1] : 'internal';
if (!['public', 'internal'].includes(audience)) throw new Error(`unknown audience: ${audience}`);

const manifest = JSON.parse(await readFile(path.join(documentationRoot, 'content-manifest.json'), 'utf8'));
const entries = manifest.entries.filter(entry => entry.scope === audience);
const contentRoot = path.join(documentationRoot, 'content', audience);
const generatedRoot = path.join(contentRoot, '_generated');
const repoWebUrl = (process.env.REPO_WEB_URL ?? '').replace(/\/+$/, '');

await rm(generatedRoot, {recursive: true, force: true});
await mkdir(generatedRoot, {recursive: true});

const sourceToTarget = new Map();
for (const entry of entries) {
  sourceToTarget.set(path.resolve(repoRoot, entry.source), path.resolve(generatedRoot, entry.target));
}

for (const entry of entries) {
  const sourceFile = path.resolve(repoRoot, entry.source);
  const targetFile = path.resolve(generatedRoot, entry.target);
  let text = await readFile(sourceFile, 'utf8');
  if (!hasFrontmatter(text)) {
    text = buildFrontmatter({
      title: entry.title,
      description: entry.title,
      order: entry.order,
      docAudience: entry.docAudience,
      source: entry.source,
    }) + text;
  }
  text = rewriteLinks(text, {sourceFile, targetFile, sourceToTarget, repoRoot, repoWebUrl});
  await mkdir(path.dirname(targetFile), {recursive: true});
  await writeFile(targetFile, text, 'utf8');
}

await writeFile(path.join(generatedRoot, 'manifest.json'), JSON.stringify({audience, entries}, null, 2));
console.log(`prepared ${entries.length} ${audience} doc(s)`);
```

- [ ] **Step 6: 写 `check-content.mjs`**

```js
import {readdir, readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const documentationRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const audienceIndex = args.indexOf('--audience');
const audience = audienceIndex >= 0 ? args[audienceIndex + 1] : 'public';
const root = path.join(documentationRoot, 'content', audience);
const forbidden = [
  /knowledge\//,
  /\bsrc\//,
  /\btests\//,
  /\btools\//,
  /\bG:[\\/]/,
  /\bD:[\\/]/,
  /\bC:[\\/]/,
  /\/internal\//i,
];

async function walk(dir) {
  const out = [];
  for (const entry of await readdir(dir, {withFileTypes: true})) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...await walk(full));
    else if (entry.name.endsWith('.md')) out.push(full);
  }
  return out;
}

let failed = false;
for (const file of await walk(root)) {
  if (file.includes(`${path.sep}_generated${path.sep}`) && audience === 'public') {
    console.error(`public generated content is not allowed: ${file}`);
    failed = true;
    continue;
  }
  const text = await readFile(file, 'utf8');
  if (!text.startsWith('---\n') && !text.startsWith('---\r\n')) {
    console.error(`missing frontmatter: ${path.relative(documentationRoot, file)}`);
    failed = true;
  }
  if (audience === 'public') {
    for (const pattern of forbidden) {
      if (pattern.test(text)) {
        console.error(`internal path/pattern ${pattern} in ${path.relative(documentationRoot, file)}`);
        failed = true;
      }
    }
  }
}
console.log(`checked ${audience}: ${failed ? 'FAIL' : 'ok'}`);
process.exit(failed ? 1 : 0);
```

- [ ] **Step 7: 写 `check-links.mjs` 与 `serve-internal.mjs`**

`documentation/scripts/check-links.mjs`：

```js
import {readdir, readFile, stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const documentationRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const baseUrl = (process.env.DOCS_BASE_URL ?? '/DFLegacy/').replace(/\/?$/, '/');

async function walk(dir) {
  const out = [];
  for (const entry of await readdir(dir, {withFileTypes: true})) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...await walk(full));
    else if (entry.name.endsWith('.html')) out.push(full);
  }
  return out;
}

async function existsFileOrIndex(target) {
  if (await stat(target).then(() => true, () => false)) return true;
  return stat(path.join(target, 'index.html')).then(() => true, () => false);
}

let checked = 0;
const missing = [];
for (const file of await walk(path.join(documentationRoot, 'build'))) {
  const text = await readFile(file, 'utf8');
  for (const match of text.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const raw = match[1];
    if (/^(https?:|mailto:|tel:|#|data:)/i.test(raw)) continue;
    let target = raw.split('#')[0].split('?')[0];
    if (!target) continue;
    if (target.startsWith(baseUrl)) target = target.slice(baseUrl.length);
    else if (target.startsWith('/')) continue;
    else target = path.join(path.dirname(file), target);
    const absolute = path.resolve(documentationRoot, 'build', target.replace(/^\/+/, ''));
    checked++;
    if (!await existsFileOrIndex(absolute)) missing.push(`${path.relative(documentationRoot, file)} -> ${raw}`);
  }
}
if (missing.length) {
  console.error(missing.slice(0, 30).join('\n'));
  console.error(`missing local targets: ${missing.length}`);
  process.exit(1);
}
console.log(`checked ${checked} local links: ok`);
```

`documentation/scripts/serve-internal.mjs`：

```js
import http from 'node:http';
import {createReadStream, existsSync, statSync} from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] ?? 'build/internal');
const port = Number(process.argv[3] ?? 4173);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.woff2': 'font/woff2',
};

http.createServer((request, response) => {
  const urlPath = decodeURIComponent((request.url ?? '/').split('?')[0]);
  let file = path.join(root, urlPath);
  if (!file.startsWith(root)) {
    response.writeHead(403);
    response.end('forbidden');
    return;
  }
  if (existsSync(file) && statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!existsSync(file) && existsSync(path.join(root, '404.html'))) file = path.join(root, '404.html');
  if (!existsSync(file)) {
    response.writeHead(404);
    response.end('not found');
    return;
  }
  response.writeHead(200, {'Content-Type': types[path.extname(file)] ?? 'application/octet-stream'});
  createReadStream(file).pipe(response);
}).listen(port, '127.0.0.1', () => {
  console.log(`internal docs: http://127.0.0.1:${port}/`);
});
```

- [ ] **Step 8: 验证生成与检查**

Run:

```powershell
cd documentation
node scripts/prepare-content.mjs --audience internal
Test-Path content/internal/_generated/technical/architecture.md
Test-Path content/internal/_generated/ops/operations.md
node scripts/check-content.mjs --audience internal
node scripts/check-content.mjs --audience public
```

Expected:
- 两个 `Test-Path` 为 `True`。
- `check-content` 内部/公开均 `ok`，退出码 0。

- [ ] **Step 9: 构建后在公开产物中检查内部路径**

Run:

```powershell
pnpm build:public
$hits = Get-ChildItem build/public -Recurse -File -Include *.html | Select-String -Pattern 'knowledge/|/internal/|D:/|G:/'
if ($hits) { $hits | Select-Object -First 5; throw 'internal content leaked into public build' }
```

Expected: 无命中，退出码 0。

- [ ] **Step 10: Commit**

```powershell
git add documentation
git commit -m "docs: add content manifest and public/internal guards"
```

---

### Task 3: 主题、搜索与公开/内部首页

**Files:**
- Modify: `documentation/src/css/custom.css`
- Modify: `documentation/src/internal/pages/index.tsx`
- Create: `documentation/static/img/logo.svg`
- Modify: `documentation/sidebars.public.ts`
- Modify: `documentation/sidebars.internal.ts`

**Interfaces:**
- Produces: 公开站 `/` 首页、`/docs/intro`；内部站 `/` 首页、`/docs/intro`。
- Produces: 中文本地搜索入口；公开/内部构建都生成可搜索索引。

- [ ] **Step 1: 扩展内部首页**

把 Task 1 的 `documentation/src/internal/pages/index.tsx` 改为：

```tsx
import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';

export default function InternalHome(): React.JSX.Element {
  return (
    <Layout title="DFLegacy 内部文档" description="技术、运维与运营手册">
      <main className="container margin-vert--lg">
        <div className="internal-banner">内部资料，禁止外传</div>
        <h1>DFLegacy 内部文档</h1>
        <ul>
          <li><Link to="/docs/technical/home">技术手册</Link></li>
          <li><Link to="/docs/ops/operations">运维手册</Link></li>
          <li><Link to="/docs/reference/current-state">当前基线</Link></li>
        </ul>
      </main>
    </Layout>
  );
}
```

- [ ] **Step 2: 加导航 logo 尺寸样式**

在 `documentation/src/css/custom.css` 末尾追加：

```css
.navbar__logo img {
  height: 2rem;
}
```

- [ ] **Step 3: 更新公开/内部 sidebars**

`documentation/sidebars.public.ts`（Task 3 只引用已存在的 `intro`；Task 5/6 再加分类）：

```ts
import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  public: ['intro'],
};

export default sidebars;
```

`documentation/sidebars.internal.ts`（只引用 Task 2 manifest 已生成的 `technical/architecture` 与 `ops/operations`；Task 4 再扩全）：

```ts
import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  internal: [
    'intro',
    {
      type: 'category',
      label: '技术',
      items: ['technical/architecture'],
    },
    {
      type: 'category',
      label: '运维',
      items: ['ops/operations'],
    },
  ],
};

export default sidebars;
```

- [ ] **Step 4: 构建并验证搜索**

Run:

```powershell
cd documentation
node scripts/prepare-content.mjs --audience internal
pnpm build:public
pnpm build:internal
Select-String -Path build/public/index.html -Pattern 'navbar__search|search' | Select-Object -First 3
Select-String -Path build/internal/index.html -Pattern 'noindex' | Select-Object -First 3
```

Expected: 两个 build 退出码 0；公开 HTML 有搜索入口；内部 HTML 有 `noindex`。内部导航可能因后续页面尚未生成而出现 warning，不阻断本任务。

- [ ] **Step 5: Commit**

```powershell
git add documentation
git commit -m "docs: add docs theme and local search"
```

---

### Task 4: 迁移全部 handbook 与根文档到内部站

**Files:**
- Modify: `documentation/content-manifest.json`
- Modify: `documentation/sidebars.internal.ts`
- Generated: `documentation/content/internal/_generated/**`（gitignored）
- Modify: `documentation/content/internal/intro.md`

**Interfaces:**
- Produces: 27 个内部技术/运维/参考页面；`prepare-content` 输出数量必须等于 manifest 条目数。
- Consumes: Task 2 的 `prepare-content.mjs`、`check-content.mjs`。

- [ ] **Step 1: 补全 manifest**

把 `content-manifest.json` 的 `entries` 替换为以下 27 项（保持 JSON 合法）：

```json
[
  {"source":"knowledge/handbook/server-architecture.md","target":"technical/architecture.md","title":"服务端架构与玩法定位","order":10,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/protocol-and-data.md","target":"technical/protocol.md","title":"协议、资源与存档契约","order":20,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/client-and-native.md","target":"technical/client-native.md","title":"客户端与原生层","order":30,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/desktop-apps.md","target":"technical/desktop.md","title":"桌面应用与更新","order":40,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/resources-and-pvf.md","target":"technical/resources.md","title":"资源与 PVF","order":50,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/resource-subprojects.md","target":"technical/subprojects.md","title":"PVFStudio/NPKStudio 协作","order":60,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/build-test-release.md","target":"technical/build-test.md","title":"构建、测试与发行","order":70,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/testing-and-evidence.md","target":"technical/testing.md","title":"测试与证据","order":80,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/chapter-adaptation.md","target":"technical/chapter-adaptation.md","title":"第一章与第二章适配","order":90,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/social-world-and-bots.md","target":"technical/social-bots.md","title":"区域频道与假人","order":100,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/agent-workflow.md","target":"technical/onboarding.md","title":"agent/开发接手步骤","order":110,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/configuration.md","target":"technical/configuration.md","title":"配置来源与优先级","order":120,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/repository-map.md","target":"technical/repository-map.md","title":"目录与模块地图","order":130,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/pvf-dump.md","target":"technical/pvf-dump.md","title":"pvf-dump 工具","order":140,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/tools-reference.md","target":"technical/tools.md","title":"工具与脚本","order":150,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/README.md","target":"technical/home.md","title":"技术手册首页","order":160,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/operations.md","target":"ops/operations.md","title":"部署、备份、回滚与诊断","order":10,"scope":"internal","docAudience":"ops"},
  {"source":"knowledge/handbook/web-and-admin.md","target":"ops/admin-web.md","title":"Web 页面与管理 API","order":20,"scope":"internal","docAudience":"ops"},
  {"source":"ADMIN_API.md","target":"ops/admin-api.md","title":"后台管理 API","order":30,"scope":"internal","docAudience":"ops"},
  {"source":"DEPLOYING.md","target":"ops/deployment.md","title":"部署指南","order":40,"scope":"internal","docAudience":"ops"},
  {"source":"DEPLOYMENT_MANAGER.md","target":"ops/manager.md","title":"部署管理器","order":50,"scope":"internal","docAudience":"ops"},
  {"source":"CLIENT_UPDATES_V4.md","target":"ops/client-updates.md","title":"V4 客户端更新","order":60,"scope":"internal","docAudience":"ops"},
  {"source":"PARTY_NETWORK_DIAGNOSTICS.md","target":"ops/party-network.md","title":"组队网络诊断","order":70,"scope":"internal","docAudience":"ops"},
  {"source":"BUILDING.md","target":"technical/building.md","title":"构建与发行","order":170,"scope":"internal","docAudience":"technical"},
  {"source":"knowledge/handbook/current-state.md","target":"reference/current-state.md","title":"当前基线与未完成项","order":10,"scope":"internal","docAudience":"reference"},
  {"source":"knowledge/handbook/glossary.md","target":"reference/glossary.md","title":"术语表","order":20,"scope":"internal","docAudience":"reference"},
  {"source":"knowledge/handbook/reference/README.md","target":"reference/reference-tables.md","title":"参考表范围与重建","order":30,"scope":"internal","docAudience":"reference"}
]
```

- [ ] **Step 2: 更新内部 sidebars**

把 `documentation/sidebars.internal.ts` 替换为：

```ts
import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  internal: [
    'intro',
    {
      type: 'category',
      label: '技术',
      items: [
        'technical/home',
        'technical/architecture',
        'technical/protocol',
        'technical/client-native',
        'technical/desktop',
        'technical/resources',
        'technical/subprojects',
        'technical/configuration',
        'technical/build-test',
        'technical/building',
        'technical/testing',
        'technical/chapter-adaptation',
        'technical/social-bots',
        'technical/onboarding',
        'technical/repository-map',
        'technical/pvf-dump',
        'technical/tools',
      ],
    },
    {
      type: 'category',
      label: '运维',
      items: [
        'ops/operations',
        'ops/admin-web',
        'ops/admin-api',
        'ops/deployment',
        'ops/manager',
        'ops/client-updates',
        'ops/party-network',
      ],
    },
    {
      type: 'category',
      label: '参考',
      items: [
        'reference/current-state',
        'reference/glossary',
        'reference/reference-tables',
      ],
    },
  ],
};

export default sidebars;
```

- [ ] **Step 3: 运行生成并检查数量**

Run:

```powershell
cd documentation
$env:REPO_WEB_URL = 'https://github.com/firekula/DFLegacy/blob/main'
node scripts/prepare-content.mjs --audience internal
$count = (Get-ChildItem content/internal/_generated -Recurse -Filter *.md).Count
if ($count -ne 27) { throw "expected 27 generated docs, got $count" }
```

Expected: `prepared 27 internal doc(s)`，count 为 27。

- [ ] **Step 4: 构建内部站并检查失效路径**

Run:

```powershell
pnpm build:internal
pnpm check:content:internal
$hits = Get-ChildItem build/internal -Recurse -File -Include *.html | Select-String -Pattern 'G:/|D:/|C:/'
if ($hits) { $hits | Select-Object -First 10; throw 'absolute local paths leaked into internal build' }
```

Expected: build 退出码 0；`check-content` 内部 `ok`；无绝对本地路径命中。

- [ ] **Step 5: 检查内部链接报告并修复**

Run:

```powershell
pnpm check:links
```

Expected: 0 missing local targets。若失败，按报告修复：
- 生成页指向未包含的 decision/acceptance -> manifest 外链接改由 `REPO_WEB_URL` 重写。
- 图片缺失 -> 把脱敏图复制到 `content/internal/assets/` 并在 manifest 中加 `assets` 字段；或在源页改成代码文本并标注“附件不在仓库”。

- [ ] **Step 6: Commit**

```powershell
git add documentation
git commit -m "docs: migrate handbook and root runbooks to internal site"
```

---

### Task 5: 公开站“开始游戏”和“玩法指南”

**Files:**
- Create: `documentation/content/public/start/requirements.md`
- Create: `documentation/content/public/start/install.md`
- Create: `documentation/content/public/start/account.md`
- Create: `documentation/content/public/start/first-launch.md`
- Create: `documentation/content/public/start/update.md`
- Create: `documentation/content/public/gameplay/basics.md`
- Create: `documentation/content/public/gameplay/town.md`
- Create: `documentation/content/public/gameplay/dungeon-party.md`
- Create: `documentation/content/public/gameplay/trade-shop.md`
- Create: `documentation/content/public/gameplay/cash-shop.md`
- Modify: `documentation/sidebars.public.ts`

**Interfaces:**
- Produces: 公开站 `/docs/start/*` 与 `/docs/gameplay/*`；所有页面 frontmatter `audience: user`、`status: active`。
- Consumes: `content/public/intro.md` 的链接。

- [ ] **Step 1: 写五篇开始游戏页面**

每页使用 frontmatter：

```yaml
---
title: 标题
description: 一句话摘要
audience: user
status: active
chapter: act1
last_verified: 2026-11-17
owner: docs
---
```

内容要求：

| 文件 | 必须包含 | 事实来源 |
| --- | --- | --- |
| `start/requirements.md` | Windows 版本、x86/x64 启动器、图形桌面/D3D9、磁盘/网络；第二章宿主位数 | `README.md` 平台支持、`client-and-native.md` 第二章 |
| `start/install.md` | 从官方发布页下载；第一章/第二章分开；不要混用 DLL/PVF；安装到新目录；校验/启动 | `README.md`、`build-test-release.md` 版本/固定入口 |
| `start/account.md` | 注册、登录、角色创建、密码长度限制、测试/普通账号区别（不暴露管理员操作） | `desktop-apps.md` 账号段落、`README.md` |
| `start/first-launch.md` | 启动器路径、首次校验、选择章节/频道、选区、进城镇；黑屏/窗口模式先看 FAQ | `client-and-native.md`、`operations.md` 诊断 |
| `start/update.md` | 登录器更新、V4 事务、不要手删 `.dflegacy`、修复入口、旧版兼容 | `desktop-apps.md` 更新、`CLIENT_UPDATES_V4.md`、`operations.md` |

每页至少包含：
- 一句“本文适用于第一章/第二章/两章共用”的说明。
- 3–6 个步骤或检查项。
- 2–4 个“常见错误/不要这样做”。
- 相关链接（FAQ、更新、支持）。

- [ ] **Step 2: 写五篇玩法页面**

同样 frontmatter；`chapter` 按页面实际填写 `act1`、`chapter-two` 或 `shared`。

| 文件 | 必须包含 |
| --- | --- |
| `gameplay/basics.md` | 移动/攻击/技能/物品栏/菜单/键位说明；不写未实现功能 |
| `gameplay/town.md` | 城镇、NPC、地图、移动、频道切换；说明同镇同区可见 |
| `gameplay/dungeon-party.md` | 建队/邀请/进图/翻牌/回城；人数和组队限制按当前实现 |
| `gameplay/trade-shop.md` | 玩家交易、摆摊、金币/物品确认；禁止现金交易/诈骗提醒 |
| `gameplay/cash-shop.md` | 商城、点券、黑钻、测试充值仅测试环境；不承诺未实现退款 |

事实来源：`README.md` 当前实现/已知缺口、`server-architecture.md`、`web-and-admin.md`、`ADMIN_API.md`、`DEPLOYMENT_MANAGER.md` 的测试充值说明。

- [ ] **Step 3: 更新公开 sidebars**

在 `documentation/sidebars.public.ts` 的 `开始游戏` 和 `玩法指南` 分类中加入所有页面 ID，例如：

```ts
{
  type: 'category',
  label: '开始游戏',
  items: [
    'start/requirements',
    'start/install',
    'start/account',
    'start/first-launch',
    'start/update',
  ],
},
{
  type: 'category',
  label: '玩法指南',
  items: [
    'gameplay/basics',
    'gameplay/town',
    'gameplay/dungeon-party',
    'gameplay/trade-shop',
    'gameplay/cash-shop',
  ],
},
```

- [ ] **Step 4: 更新公开 navbar 与首页**

在 `documentation/docusaurus.config.shared.ts` 的公开 navbar 中加入：

```ts
{to: '/docs/start/install', label: '开始游戏', position: 'left'},
{to: '/docs/gameplay/basics', label: '玩法', position: 'left'},
```

把 `documentation/src/public/pages/index.tsx` 的主按钮改为：

```tsx
<Link className="button button--primary" to="/docs/start/install">
  下载与安装
</Link>
```

- [ ] **Step 5: 构建与检查**

Run:

```powershell
cd documentation
pnpm build:public
pnpm check:content:public
$internal = Get-ChildItem build/public -Recurse -File -Include *.html | Select-String -Pattern 'knowledge/|/internal/|src/'
if ($internal) { $internal | Select-Object -First 5; throw 'public build contains internal paths' }
```

Expected: build 0；`check-content` 公开 `ok`；无内部路径命中。

- [ ] **Step 6: Commit**

```powershell
git add documentation
git commit -m "docs: add player start and gameplay guides"
```

---

### Task 6: 公开站 FAQ、更新、规则与支持

**Files:**
- Create: `documentation/content/public/faq/startup.md`
- Create: `documentation/content/public/faq/update.md`
- Create: `documentation/content/public/faq/network.md`
- Create: `documentation/content/public/faq/account.md`
- Create: `documentation/content/public/faq/performance.md`
- Create: `documentation/content/public/updates/act1.md`
- Create: `documentation/content/public/updates/chapter-two.md`
- Create: `documentation/content/public/updates/known-issues.md`
- Create: `documentation/content/public/rules/terms.md`
- Create: `documentation/content/public/rules/fair-play.md`
- Create: `documentation/content/public/support/contact.md`
- Modify: `documentation/sidebars.public.ts`

**Interfaces:**
- Produces: 公开站 `/docs/faq/*`、`/docs/updates/*`、`/docs/rules/*`、`/docs/support/contact`。

- [ ] **Step 1: 写 FAQ 五页**

每页 frontmatter `audience: user`、`status: active`、`chapter: shared`。

| 文件 | 必须覆盖 |
| --- | --- |
| `faq/startup.md` | 启动器无响应、黑屏、D3D9 失败、路径含中文/空格、杀软拦截、日志位置 |
| `faq/update.md` | 更新卡住、校验失败、`.dflegacy` 未决事务、修复入口、不要手删状态文件 |
| `faq/network.md` | 连接超时、频道/选区、组队掉线、Relay/防火墙提示、IPv4/IPv6 边界 |
| `faq/account.md` | 注册失败、密码规则、账号锁定、角色不显示、找客服前准备什么 |
| `faq/performance.md` | 帧率、后台限速、第二章 perf 日志、输入法/光标、不要用第三方注入 |

事实来源：`operations.md` 诊断表、`client-and-native.md` 日志、`desktop-apps.md` 更新/账号、`PARTY_NETWORK_DIAGNOSTICS.md`。不要写具体内部端口/密钥。

- [ ] **Step 2: 写更新与已知问题**

| 文件 | 内容 |
| --- | --- |
| `updates/act1.md` | 第一章最近版本摘要、更新方式、旧版升级注意事项；基于 `docs/releases/v0.1.8`–`v0.1.11` 与 `CHANGELOG` |
| `updates/chapter-two.md` | 第二章 v1.0.13 摘要、完整启动包/新目录、旧版兼容；基于 `docs/releases/v1.0.13.md` |
| `updates/known-issues.md` | 面向玩家的已知问题：首次进副本掉线需日志、墓地侧门限制、组队/性能边界；不暴露内部实现 |

中文表述要区分“已修复/待确认/已知限制”，不得把历史验收写成当前保证。

- [ ] **Step 3: 写规则与支持**

| 文件 | 内容 |
| --- | --- |
| `rules/terms.md` | 使用规则、账号责任、禁止外挂/诈骗/现金交易、内容变更权；标明“需运营/法务复核，status: draft”直到用户确认 |
| `rules/fair-play.md` | 公平游戏、举报、处罚、申诉入口；不虚构处罚时限 |
| `support/contact.md` | 官方支持入口：启动器内客服/公告、GitHub Issues；不虚构 QQ/Discord/邮箱；说明反馈需附日志 |

`rules/terms.md` 在用户确认前保持 `status: draft`，并在页首显示“草案，待正式发布”。

- [ ] **Step 4: 更新公开 sidebars**

加入 FAQ、更新、规则、支持分类：

```ts
{
  type: 'category',
  label: '常见问题',
  items: ['faq/startup', 'faq/update', 'faq/network', 'faq/account', 'faq/performance'],
},
{
  type: 'category',
  label: '更新与公告',
  items: ['updates/act1', 'updates/chapter-two', 'updates/known-issues'],
},
{
  type: 'category',
  label: '规则与支持',
  items: ['rules/terms', 'rules/fair-play', 'support/contact'],
},
```

- [ ] **Step 5: 更新公开 navbar、首页与 footer**

在公开 navbar 中加入：

```ts
{to: '/docs/faq/startup', label: '常见问题', position: 'left'},
{to: '/docs/updates/act1', label: '更新', position: 'left'},
```

在公开 footer 的 `items` 中加入：

```ts
{label: '常见问题', to: '/docs/faq/startup'},
{label: '更新记录', to: '/docs/updates/act1'},
```

在 `documentation/src/public/pages/index.tsx` 的按钮下加入：

```tsx
<p>
  <Link to="/docs/faq/startup">启动问题</Link> ·{' '}
  <Link to="/docs/updates/act1">更新记录</Link>
</p>
```

- [ ] **Step 6: 构建与检查**

Run:

```powershell
cd documentation
pnpm build:public
pnpm check:content:public
pnpm check:links
```

Expected: 全部退出码 0；公开站无内部路径。

- [ ] **Step 7: Commit**

```powershell
git add documentation
git commit -m "docs: add player FAQ, updates, rules and support"
```

---

### Task 7: 内部运营 SOP 首批

**Files:**
- Create: `documentation/content/internal/business/gm-console.md`
- Create: `documentation/content/internal/business/account-support.md`
- Create: `documentation/content/internal/business/rewards-mail.md`
- Create: `documentation/content/internal/business/moderation-appeals.md`
- Create: `documentation/content/internal/business/payments-refunds.md`
- Create: `documentation/content/internal/business/events-announcements.md`
- Create: `documentation/content/internal/business/customer-service.md`
- Create: `documentation/content/internal/business/shift-handover.md`
- Modify: `documentation/sidebars.internal.ts`

**Interfaces:**
- Produces: 内部站 `/docs/business/*`；每页 `audience: business`、`status: active|draft`、`owner: business`。

- [ ] **Step 1: 写 GM 与账号支持**

| 文件 | 必须包含 | 事实来源 |
| --- | --- | --- |
| `gm-console.md` | 入口/端口、登录身份、角色/账号查询、邮件/奖励入口、权限边界、禁止直接改 JSON | `ADMIN_API.md` §2–§5、`web-and-admin.md` |
| `account-support.md` | 账号/角色定位、密码重置、测试/普通账号转换、封禁/申诉前置检查、隐私 | `ADMIN_API.md` §4、`DEPLOYMENT_MANAGER.md` 账号类型 |

- [ ] **Step 2: 写发奖/邮件与充值边界**

| 文件 | 必须包含 | 事实来源 |
| --- | --- | --- |
| `rewards-mail.md` | 预览、批次 ID、发送/补发、重试去重、`Unknown` 处理、隔离存档验证 | `ADMIN_API.md` §5、`protocol-and-data.md` 奖励事务 |
| `payments-refunds.md` | 区分测试充值/真实支付；测试充值开关风险；当前无真实退款流程；禁止手改余额；对账/客服升级 | `ADMIN_API.md` §6、`DEPLOYMENT_MANAGER.md` 测试充值 |
| `moderation-appeals.md` | 只写当前存在的封禁/申诉能力；没有独立封禁 API 时明确“当前不支持/需开发”，不做手工改档 | 搜索 `Admin`/封禁实现；无则写缺口 |

- [ ] **Step 3: 写活动/客服/交接**

| 文件 | 必须包含 |
| --- | --- |
| `events-announcements.md` | 公告/邮件入口、排期、预览、回滚/补发注意；未实现的活动的明确标注 |
| `customer-service.md` | 受理范围、需要收集的证据（时间/角色/章节/日志）、隐私、升级路径、常见回复边界 |
| `shift-handover.md` | 交班检查表：服务状态、维护窗口、未结奖励/退款、异常日志、待跟进玩家、变更记录 |

- [ ] **Step 4: 更新内部 sidebars 与内部首页**

在 `documentation/sidebars.internal.ts` 的“技术/运维/参考”之间加入：

```ts
{
  type: 'category',
  label: '运营',
  items: [
    'business/gm-console',
    'business/account-support',
    'business/rewards-mail',
    'business/moderation-appeals',
    'business/payments-refunds',
    'business/events-announcements',
    'business/customer-service',
    'business/shift-handover',
  ],
},
```

在 `documentation/src/internal/pages/index.tsx` 的列表中加入：

```tsx
<li><Link to="/docs/business/gm-console">运营 SOP</Link></li>
```

在 `documentation/docusaurus.config.shared.ts` 的内部 navbar 中加入：

```ts
{to: '/docs/business/gm-console', label: '运营', position: 'left'}
```

在内部 footer 的 `items` 中加入：

```ts
{label: '运营', to: '/docs/business/gm-console'}
```

- [ ] **Step 5: 构建与检查**

Run:

```powershell
cd documentation
pnpm build:internal
pnpm check:content:internal
pnpm check:links
```

Expected: 全部退出码 0；内部页面 `audience: business`。

- [ ] **Step 6: Commit**

```powershell
git add documentation
git commit -m "docs: add internal operations SOPs"
```

---

### Task 8: 发布脚本、GitHub Pages 与 Gitea 镜像

**Files:**
- Create: `documentation/scripts/publish-static.sh`
- Create: `.github/workflows/docs-pages.yml`
- Create: `.gitea/workflows/docs.yml`
- Create: `documentation/scripts/package-internal.ps1`
- Modify: `documentation/README.md`

**Interfaces:**
- `publish-static.sh <build-dir> <remote> <branch>`：把静态目录提交到指定分支；不包含凭据。
- GitHub Actions：构建公开站并部署 GitHub Pages。
- Gitea Actions：构建公开/内部站，上传内部 ZIP，推送公开站到 `pages` 分支。
- `package-internal.ps1`：生成 `artifacts/docs-internal-<commit>.zip`。

- [ ] **Step 1: 写发布脚本**

`documentation/scripts/publish-static.sh`：

```bash
#!/usr/bin/env bash
set -euo pipefail
BUILD_DIR="${1:?build dir}"
REMOTE="${2:?remote}"
BRANCH="${3:?branch}"
REMOTE_URL="$(git remote get-url "$REMOTE")"
WORKTREE="$(mktemp -d)"
trap 'rm -rf "$WORKTREE"' EXIT

git -C "$WORKTREE" init -q
cp -a "$BUILD_DIR"/. "$WORKTREE"/
git -C "$WORKTREE" add -A
git -C "$WORKTREE" -c user.name="docs-bot" -c user.email="docs-bot@localhost" \
  commit -m "docs: publish $(date -u +%Y-%m-%dT%H:%M:%SZ)"
git -C "$WORKTREE" push --force "$REMOTE_URL" "HEAD:${BRANCH}"
```

说明：静态分支是生成物，使用独立临时仓库和 `--force` 覆盖；不要把这个脚本用于源码分支。

Run:

```powershell
bash documentation/scripts/publish-static.sh documentation/build/public origin pages
```

Expected: 本地有推送权限时成功；无权限时失败并保留工作树输出。不要把 token 写进脚本。

- [ ] **Step 2: 写内部打包脚本**

`documentation/scripts/package-internal.ps1`：

```powershell
param(
  [string]$BuildDir = "",
  [string]$OutputDir = ""
)
$ErrorActionPreference = "Stop"
$repoRoot = (git rev-parse --show-toplevel).Trim()
if (-not $BuildDir) { $BuildDir = Join-Path $repoRoot "documentation/build/internal" }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "artifacts" }
$commit = (git rev-parse --short HEAD).Trim()
$zip = Join-Path $OutputDir "docs-internal-$commit.zip"
if (Test-Path $zip) { throw "output exists: $zip" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $repoRoot "documentation/scripts/serve-internal.mjs") $BuildDir
@"
DFLegacy 内部文档
================
本包为内部资料，禁止外传。
启动：node serve-internal.mjs . 4173
然后打开 http://127.0.0.1:4173/
"@ | Set-Content -Encoding UTF8 (Join-Path $BuildDir "README.txt")
Compress-Archive -Path (Join-Path $BuildDir "*") -DestinationPath $zip
Write-Host "wrote $zip"
```

- [ ] **Step 3: 写 GitHub Pages workflow**

`.github/workflows/docs-pages.yml`：

```yaml
name: docs-pages
on:
  push:
    branches: [main]
    paths: ['documentation/**', '.github/workflows/docs-pages.yml']
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: true

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
        with: {version: 11}
      - uses: actions/setup-node@v4
        with: {node-version: 24, cache: pnpm, cache-dependency-path: documentation/pnpm-lock.yaml}
      - run: pnpm --dir documentation install --frozen-lockfile
      - run: pnpm --dir documentation check:content
      - run: pnpm --dir documentation build:public
        env:
          DOCS_SITE_URL: https://firekula.github.io/DFLegacy
          DOCS_BASE_URL: /DFLegacy/
      - run: pnpm --dir documentation check:links
      - uses: actions/upload-pages-artifact@v3
        with: {path: documentation/build/public}
  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

- [ ] **Step 4: 写 Gitea workflow**

`.gitea/workflows/docs.yml`：

```yaml
name: docs
on:
  push:
    branches: [main]
    paths:
      - 'documentation/**'
      - 'knowledge/**'
      - 'ADMIN_API.md'
      - 'DEPLOYING.md'
      - 'DEPLOYMENT_MANAGER.md'
      - 'BUILDING.md'
      - 'CLIENT_UPDATES_V4.md'
      - 'PARTY_NETWORK_DIAGNOSTICS.md'
      - '.gitea/workflows/docs.yml'
  workflow_dispatch:

jobs:
  build:
    runs-on: dflegacy-linux-x64
    steps:
      - uses: actions/checkout@v4
      - run: corepack enable
      - run: pnpm --dir documentation install --frozen-lockfile
      - run: pnpm --dir documentation check:content
      - run: pnpm --dir documentation build:internal
        env:
          REPO_WEB_URL: http://firekula.site:3000/firekula/DFLegacy/src/branch/main
      - run: pnpm --dir documentation build:public
        env:
          DOCS_SITE_URL: http://firekula.site:3000/firekula/DFLegacy
          DOCS_BASE_URL: /firekula/DFLegacy/
      - run: pnpm --dir documentation check:links
      - run: pwsh -NoProfile -File documentation/scripts/package-internal.ps1
      - uses: actions/upload-artifact@v4
        with:
          name: docs-internal
          path: artifacts/docs-internal-*.zip
      - run: bash documentation/scripts/publish-static.sh documentation/build/public origin pages
```

Expected: workflow 语法可被 Gitea Actions 读取；首次运行前确认 runner 有 Node/pnpm 和 `pwsh`。若 runner 没有 `pwsh`，把打包步骤换成 bash + `zip`，并在 `documentation/README.md` 记录。

- [ ] **Step 5: 本地验证**

Run:

```powershell
cd documentation
pnpm build:public
pnpm build:internal
pwsh -NoProfile -File scripts/package-internal.ps1
Test-Path ../artifacts/docs-internal-*.zip
```

Expected: build 0；ZIP 生成；ZIP 含 `index.html`、`README.txt`、`serve-internal.mjs`。

- [ ] **Step 6: Commit**

```powershell
git add documentation .github .gitea
git commit -m "docs: add Pages publishing and internal package"
```

---

### Task 9: 公开/内部隔离、链接检查与交付验收

**Files:**
- Modify: `documentation/README.md`
- Create: `documentation/scripts/check-isolation.mjs`
- Modify: `documentation/package.json`
- Test: `documentation/build/public`、`documentation/build/internal`、`artifacts/docs-internal-*.zip`

**Interfaces:**
- `check-isolation.mjs`：扫描公开构建 HTML，发现内部路径/标题即失败；扫描内部构建，确认包含 `noindex`。
- `pnpm verify`：运行 `test:scripts`、`check:content`、`build:public`、`build:internal`、`check:links`、`check-isolation`。

- [ ] **Step 1: 写隔离检查脚本**

`documentation/scripts/check-isolation.mjs`：

```js
import {readdir, readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const documentationRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const publicRoot = path.join(documentationRoot, 'build/public');
const internalRoot = path.join(documentationRoot, 'build/internal');
const forbidden = [/knowledge\//, /\/internal\//i, /\bsrc\//, /\bG:[\\/]/, /\bD:[\\/]/];

async function walk(dir) {
  const out = [];
  for (const entry of await readdir(dir, {withFileTypes: true})) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...await walk(full));
    else if (entry.name.endsWith('.html')) out.push(full);
  }
  return out;
}

let failed = false;
for (const file of await walk(publicRoot)) {
  const text = await readFile(file, 'utf8');
  for (const pattern of forbidden) {
    if (pattern.test(text)) {
      console.error(`public leak ${pattern} in ${path.relative(documentationRoot, file)}`);
      failed = true;
    }
  }
}
const internalIndex = await readFile(path.join(internalRoot, 'index.html'), 'utf8');
if (!/noindex/i.test(internalIndex)) {
  console.error('internal index.html missing noindex');
  failed = true;
}
console.log(`isolation: ${failed ? 'FAIL' : 'ok'}`);
process.exit(failed ? 1 : 0);
```

- [ ] **Step 2: 加 verify 脚本**

`documentation/package.json` scripts 追加：

```json
"check:isolation": "node scripts/check-isolation.mjs",
"verify": "pnpm test:scripts && pnpm check:content && pnpm build:public && pnpm build:internal && pnpm check:links && pnpm check:isolation"
```

- [ ] **Step 3: 运行完整验收**

Run:

```powershell
cd documentation
pnpm verify
```

Expected: 所有步骤退出码 0；`isolation: ok`。

- [ ] **Step 4: 人工检查**

启动公开站与内部站：

```powershell
cd documentation
pnpm start:public
# 另开终端
pnpm start:internal
```

检查：
- 公开站：首页、开始游戏、玩法、FAQ、更新、规则、支持均可点；移动端宽度下导航可用；搜索“安装”“更新”能命中。
- 内部站：技术/运维/运营/参考四区；生成页源码链接指向 `REPO_WEB_URL`；内部首页显示“内部资料，禁止外传”。
- 公开站源码中无 `GM`、`协议`、`knowledge/`、`src/`、内部地址。

- [ ] **Step 5: 更新 README 与交付说明**

`documentation/README.md` 补充：

```md
## 发布

- GitHub Pages：`.github/workflows/docs-pages.yml`，需在仓库 Settings → Pages 选择 GitHub Actions。
- Gitea 镜像：`.gitea/workflows/docs.yml` 推送 `pages` 分支；Gitea 1.26 不能依赖内置 Pages，需 Nginx/第三方 pages server。
- 内部包：`pwsh -File scripts/package-internal.ps1` → `artifacts/docs-internal-<commit>.zip`，不上传公开位置。

## 内容边界

- `content/public` 只放玩家文档；`content/internal` 放技术/运维/运营。
- 技术文档源在 `knowledge/handbook`；生成目录 `content/internal/_generated` 不手改。
- 公开构建失败于内部路径；内部构建允许源码链接，但不含密钥/账号/存档。
```

- [ ] **Step 6: Commit**

```powershell
git add documentation
git commit -m "docs: add isolation checks and final verification"
```

---

## Self-Review

- [x] 规格覆盖：公开站、内部站、双构建、内容清单、链接重写、GitHub Pages、Gitea 镜像、内部 ZIP、首批玩家/运营内容、隔离检查均有任务。
- [x] 无 TBD/TODO/占位实现；Task 3 只引用已存在的页面，Task 4–7 再逐步扩展分类和页面。
- [x] 类型/字段一致：`scope` 用于构建过滤，`docAudience` 写入 frontmatter；`content-manifest.json` 与 `prepare-content.mjs` 一致。
- [x] 链接重写、绝对路径降级、公开边界检查有可执行代码和失败测试。
- [x] 部署命令、baseUrl、内部 serve、ZIP 打包有明确入口；Gitea Pages 限制已记录。

## Execution Handoff

Plan complete and saved to `knowledge/plans/2026-11-17-official-docs-site.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — 每个任务派一个新 subagent，任务间审查，适合这个多文件、分阶段项目。
2. **Inline Execution** — 在当前会话按 executing-plans 分批执行，带检查点。

Which approach?
