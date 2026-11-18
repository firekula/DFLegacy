# 2026-11-19：商城 +10 强化券、调试传送/无敌与正式构建边界

## 背景

第二章本地测试暴露四个问题：副本无敌不生效、F11 面板右键传送无效、强化券使用时提示材料不足、商城充值页打不开。用户逐项实机确认后，传送、充值、无敌和商城 +10 强化券均已通过；本轮同时完成正式构建和发布准备。

## 1. 副本无敌

根因：`ChapterTwoDebugControl.cpp` 的 `LocateActor()` 先判断城镇角色，副本里 `0xd7a8c0` 仍可能指向城镇角色，`Tick()` 因此直接按城镇分支返回，副本角色永远拿不到 `CombatOverride`。

修复：

- 用游戏自身的场景字段 `Secure(game+0x10, scene)` 判断：`1` 城镇、`3` 普通副本、`10` 塔；城镇只处理传送并返回，副本返回 `0xd7a8bc` 的副本角色。
- `SetHpGuard` 在副本内拦截当前副本角色的 HP 损失，即使战斗属性银行不存在，也能保证“受伤为 0”。
- 战斗属性银行继续按原 `CombatOverride` 写入物理/魔法攻击、防御和移速/攻速/施放；城镇不写入。

边界：这是**调试客户端**功能，正式 host 不定义 `DFL_CHAPTER_TWO_DEBUG_CONTROL`，正式包不应出现 F11 调试入口。

## 2. 右键传送

根因：对 `0x490e60` 的入口 detour 从未被真实右键路径调用；地图窗口钩子在无地图时又因 `plane=0` 拒绝。

修复：

- 保留 `TryTeleport` 的同城镇校验、坐标校验、控制器/表/全局/地图区校验。
- 在 `Tick()` 每帧读取 `townActor+0x278/+0x27c` 的右键自动寻路目标；与当前位置不同时直接调用 `TryTeleport`，从而用传送替代客户端原本的自动寻路。
- 城镇角色不是 `0x489bc0` 的 authority 路径，`TryTeleport` 对已确认的本地城镇角色（`0xd7a8c0` 且 vtable `0xaea99c`）放行；其他角色仍要求 authority。
- 目标与当前位置相同、失败目标未变化时不会重复尝试，避免日志和调用风暴。

边界：只在同城镇；跨城镇、副本内不走这条路径。正式包不启用。

## 3. 商城 +10 强化券

根因有两层：

1. 强化券 `7274` 只有道具模板，没有服务端使用入口；客户端右键后请求协议 83，服务端按普通强化规则找不到材料，返回“材料不足”。
2. 商城目录 `etc/newcashshop.etc` 的 `[item]` 段没有该商品；第一次插入时又误插到文件开头注释 `// [item] : ...` 之后，服务端/客户端都按段外内容忽略。

修复：

- `EquipmentReinforcementPlanner.TryCreate` 识别 `7273/7274/7275`：目标必须在装备列表或背包中、物品 ID 匹配；券数量至少 1；只扣 1 张；把目标 `State` 的低 5 位直接设为 `7/10/12`；`ResultCode=0`、`Failed=false`、不扣金币/材料、不掷失败判定。
- 商城目录在 `[item]` 段第一行写入 `100001 7274 1 0 0 100` `+10 Ticket` `0 0`；服务端配套 PVF 和客户端 `Script.pvf` 使用同一行。
- 客户端 `Script.pvf` 新 SHA-256：`1c7f01da1aaf982760ea3908fa403aefd088e6b7116c0ca5ddce7f33e77ea84d`；登录器加入该哈希，同时保留旧哈希兼容。
- 服务端 `CeraShopCatalog.LoadProducts` 按真实 `[item]` 段头解析；测试账号 Cera 余额只用于本地测试。

实机证据：用户在调试/本地测试构建中确认商城“道具”页出现 +10 券并完成购买流程；该构建同时使用调试视图补丁。正式包改用基础 `Script.pvf` 加同一行商城目录，避免依赖调试视图。

## 4. 商城充值

充值页布局/导航修复已在前序构建中完成；本轮用户实机确认商城购买页点“充值”可以正常弹出充值面板。本轮没有修改支付代码。

## 正式构建与发布边界

- 版本统一为 `1.0.14`；正式 host 由不带 `DFL_CHAPTER_TWO_DEBUG_CONTROL` 的 CMake 配置构建。
- 正式 `launcher.chapter2.json`：`ChapterTwoServerHost=games.firekula.site`、`ChapterTwoDebugEnabled=false`、`ChapterTwoTestMuteEnabled=false`、`StartServer=false`、更新源 GitHub/GitCode 二选一。
- 完整整合包包含客户端、正式登录器、配套 `Script.pvf` 和正式配置；服务端镜像不内置 PVF/存档。
- 按用户要求，**在确认万无一失前不执行真实上传**：本轮只生成本地产物、校验和、发布说明与上传命令，不推送源码、不创建 Release、不推镜像。

## 复现/验证要点

- 服务端：`dotnet run --project tests/DFLegacy.SmokeTests -c Release`，新增券测试覆盖 +0/+5/+10 目标、券不足、材料/金币不扣、结果码。
- 原生：正式 host 构建的 Release 测试；调试功能只在带 `DFL_CHAPTER_TWO_DEBUG_CONTROL` 的构建中验证。
- PVF：`tools/pvf/chapter-two` 的 `verify_views` 逐条目确认只改 `etc/newcashshop.etc`，native/server 内容一致。
- 真机：用户完成传送、充值、无敌、+10 券流程；正式发布包仍需用户再做一次启动/商城/强化冒烟。
