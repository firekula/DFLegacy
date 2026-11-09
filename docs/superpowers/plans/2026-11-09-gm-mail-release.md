# GM 多物品发信与热修复发布 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保存冰心少年修复、完成跨副本排查及 GM 多物品发信，发布可兼容升级的 v0.1.10。v0.1.9 仅为已构建候选：GHCR 标签保留，GitHub 客户端草稿不公开。

**Architecture:** 保留旧客户端单物品邮件协议，后台多格请求按角色原子拆信。保留现网固定入口，托管更新流程处理旧版首次校验记录迁移。

**Tech Stack:** .NET 10、原生 x86 C++、原生 JavaScript、Python 发布工具。

**Spec:** docs/superpowers/specs/2026-11-09-gm-mail-attachments-design.md

## Global Constraints

- 最多 10 个后台物品格，金币只在首封。
- 保持旧单附件请求、群发指纹与单角色完整投递。
- 不操作线上服务或奖励；不修改第二章工作区。
- 发布沿用 v0.1.8 稳定原生入口；原生新入口实验不混入 V4 正式更新。

## Review Focus

- 群发旧预览在升级后仍能确认，重复请求不发重复奖励。
- 中间一个附件无效或收件箱差一格，不投递任何部分。
- 零金币／纯金币／相同物品多格正确处理，金币总额不翻倍。
- UI 刷新恢复所有格，发出预览后不能变更待确认内容。
- 旧客户端首次升级没有校验记录时可安全完整校验并进入界面。

### Task 1: 固定与排查

- [x] 独立提交冰心文字状态和友方目标修复（9c23733）。
- [x] 对当前实际 PVF 扫描全部地图及四难度，区分不可进入 A 格与有效地图。
- [x] 补齐 256 帧隐藏 FPS 的共用文字入口回归，保存排查报告；开启 FPS 的夹具受固定地址冲突限制，不计为通过。

### Task 2: 多附件服务端

Files: AdminApiModels.cs、AdminRewardModels.cs、JsonGameStore.AdminRewards.cs、Program.cs、相关 smoke。

- [x] 先写原子拆信、兼容与边界失败测试并确认失败。
- [x] 增加可选 Attachments 与 TryComposeMany；新单角色原子方法和群发复用。
- [x] 跑针对性和完整 smoke，记录既有 PVF 基线差异。

### Task 3: 后台多格

Files: wwwroot/admin/index.html、admin.js、admin-rewards.js、admin.css、网页测试。

- [x] 先测试多格提交、增删、最多十格及群发恢复。
- [x] 实现重复字段组、可视化格号、独立 ID／数量、明确拆信说明。
- [x] 本地网页验收，核对响应及持久化结果。

### Task 4: 启动兼容

- [x] 为 v0.1.8 更新后没有 validated 记录的首次进入添加失败回归。
- [x] 安全完整校验后记账，保留篡改／来源不符拒绝；跑两架构测试。
- [x] 普通 GUI 快路不先联网、不创建验证窗口；命令行自动登录保留更新检查。
- [x] 后台 Ready 自动交回固定入口安装，保留占用确认和失败处理；联合 x86/x64 各 2290 项通过。
- [x] 四路产物使用旧稳定入口并验证生产符号来源；54 个正式包验收命令通过。

### Task 5: 发布

- [x] 汇总报告，更新 README、CHANGELOG、ADMIN_API、部署及发布文档。
- [x] 代码审阅、提交推送 Gitea，净化快照推 GitHub。
- [x] 构建 GHCR 和四路客户端、验证摘要清单、迁移、符号；发布 v0.1.10。
- [x] 核实 30 个公开附件、四路生产更新发现、镜像 16 层完整匿名摘要；更新命令和验证边界记入报告。
