# 第二章背包宠物页点击接入（2026-11-12）

本轮root用实际C2客户端确认：宠物标签可见，但从物品栏或装扮页点击宠物都没有切换。原截图在 `dist/c2-parity-next-20261112/baseline/06-pet-tab-native.png`、`07-pet-click-from-avatar.png`；第一张文件名不代表通过。历史截图只能证明标签绘制，不能证明可用。

## 原因与适用性

这不是第一章两处“保持visible=1”补丁。C2的对应构造指令 `870EA8`（装扮）、`879020`（物品栏）原本就是visible=1。通用按钮构造 `A3C4E0` 也将enabled设为1。

真正差异是点击分支缺失。第一章装扮handler在 `7A3076–7A30A7` 检查宠物按钮并调用原生切页2；C2 handler `86FEB0` 处理物品栏按钮后，`86FF26` 直接进入后续业务。物品栏handler `878AE0` 同样只保留切装扮1（`878B7B–878BAE`），没有宠物按钮到2的分支。

C2的目标页面仍有完整原生对象：`8679EB–867A0E` 设置vtable `C115AC`、调用初始化 `873560` 及资源构造 `875730`，保存到UI manager+88，即page2。资源构造读取 `Interface/Creature.img` 和 `Interface/InventoryTab.img`。准入 `8736D0` 检查当前玩家，通用切页 `866D40` 对page2使用原注册表和vtable。宠物页输入 `874FC0` 保留返回装扮1、物品栏0（`87505A–8750C1`）。

原始指令与两映像SHA256在 `dist/c2-parity-next-20261112/creature-tabs/native-comparison.asm`、`images.json`。真实会话中page2资源对象是否构造完成仍由集成验收检查；本模块不会创建或替代资源窗口。

## 最小实现

`ChapterTwoCreatureTabs.cpp` 只替换 `C1153C`、`C11578` 两个vtable输入回调槽。先执行完整原回调，再补宠物按钮事件：

- 源对象仍注册到相同manager的page0/1，且还是当前页；
- 目标page2已注册、vtable正确、owner一致并有原生窗口；
- 宠物按钮visible、enabled，非零控件ID匹配当前原生capture；
- 调用原生 `866D40(manager,2,false)`，继续使用原准入、关旧页、挂载窗口、更新页号与音效。

不修改 `ChapterTwoInventoryTab` 的 `877F52` 自动分类保持补丁，也不修改任何原有可执行指令。源输入行为、目标宠物页和返回按钮仍执行原生代码。两槽和关联原指令完整校验后才写入；失败回滚本次变更，重复安装无额外写入。

实际manager输入循环 `868AE1–868B0A` 依次取得当前页、以ECX传入self并调用vtable+8；调用后只推进循环，不使用EAX。两个原回调所有出口均为无参数的RET。因此 `void __fastcall(self, unused EDX)` 包装适配原 `void __thiscall(self)`；该调用方原指令也纳入安装签名校验。独立复核见 `creature-tabs/caller-review.txt`。

## 验证边界

`creature-tabs/red-full-source.txt`：原始C2映像9检查中仅两项失败，即两个来源无法进入page2。原生宠物页返回两个来源本来正常。

`creature-tabs/green-full-source.txt`：38检查通过，并由另一agent独立复跑确认。夹具映射真实原始C2映像，执行完整源页输入函数、真实 `866D40` 和真实宠物返回按钮分派切片；只隔离宠物页返回后的无关交互，以及窗口挂载、关闭、音效、滚动控件读取等外部依赖。验证了两来源进入2、返回原页、原物品分类字段+40保持、完整装扮回调仍更新滚动字段+38、非匹配capture、隐藏/禁用按钮、未构造目标、已脱离源页、签名不符、两次写入前后故障回滚和幂等。

2026-11-12 13:11，对生产候选PID42136仅申请进程查询/只读内存权限，验证其完整EXE路径、创建时间和本会话trusted START。`live-page-and-logs-42136-131127.json` 证明page2实例 `04C80180`、vtable `C115AC`、manager归属一致、group=1，原生窗口 `0E8620D0` 已构造且可见/启用；两个源宠物按钮均可见且启用。资源构造前提已满足，但该候选尚未接入本模块，因此不作为点击成功证明。

这不是资源渲染或宠物装备/孵化真机验收。应在最终接线宿主确认原生page2和窗口实例存在，再逐项点击物品→宠物→装扮→宠物→物品、关闭重开，并检查分类不被重置。实际宠物物品业务及商城宠物页另列，不由空页切换通过替代。
