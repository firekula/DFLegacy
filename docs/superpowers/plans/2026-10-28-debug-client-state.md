# Debug Client State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox syntax for tracking.

**Goal:** 让后续实机自动操作直接读取新鲜原生状态，减少截图、反复跨进程采集和冗长输出。

**Architecture:** debug.dll在DebugRender的旧场景返回之前每200ms有界只读采集，经非阻塞发布交给既有worker原子输出。ABI v2及服务端v1/v2箱怪协议保持，客户端JSON另用独立schema。Python消费端做身份与时效校验，提供紧凑摘要和条件等待。

**Tech Stack:** C++17/Win32、既有MSVC Release `/W4 /WX`、Python标准库。

**Spec:** `docs/superpowers/specs/2026-10-28-debug-client-state-design.md`

## Global Constraints

- 新导出默认关闭，`[DebugOverlay] ExportState=1`启用；DLL加载沿用EnableDebugOverlay，隐藏面板不停止导出。
- 不调用游戏业务函数，不写游戏状态，不发送游戏协议；通过原PE身份和关键签名才解引用硬编码布局。
- 上限4队员、256敌人、216技能、4门；截断与未知必须可见，不能推断全体集结／清场。
- 采集与写盘时间分离，序号只随新采集更新；游戏线程TryLock忙则跳过，IO/序列化不持发布锁。
- 服务端context独立注明matched=false，不用作客户端已换房依据。CD／MP等未证字段显式null/unsupported。

## Review Focus

- 停渲染、UTC跳变或旧JSON重写不得恢复新鲜度。
- 死亡actor.map0不等于Town；位置未知而死亡/身份可独立有效。
- 错误EXE、保护整数损坏、未知provider/职业均不能触发业务调用或异常外溢。
- TryLock争用与截断不能拖住帧线程或生成错误集结判断。
- 多客户端、PID复用、换角/换图不能使用上次状态继续输入。

## JSON接口

文件为配置SnapshotDirectory下`dfdebug-client-<pid>.json`，schema=`dfl-client-state/v1`。头字段`pid`、`process_started_filetime`、`sequence`、`captured_filetime`、`captured_tick_ms`、`exported_filetime`、`layout_supported`、`valid`、`scene`、`generation`。单调时钟采用同机GetTickCount64语义，Python用对应Win32来源；平台无此时钟时可注入测试clock，不猜UTC偏移。

`room`含原生stage/map token、boss_dead、assembly_phase、map_clear_state原始保护整数及4方向门（enabled与rect；enabled仅拓扑存在）；`me`及`players`含uid、actor token、hp、max_hp、dead、same_map、detached、position；`enemies`含uid、monster_id、hp、dead、active、position、action、queued_action、authority_token；`skills`仅已学id/level，`cooldown_ms`与`usable`默认null。未知标量为null，不以0冒充；顶层`truncated`、`errors`和`capture_duration_us`明确完整性及开销。

`server_context`可附既有新鲜箱怪数据并标matched=false。紧凑摘要默认仅场景/原生房间token/本人HP死亡/敌人数/可用门/技能能力标记，不输出指针原始内存块、完整字符串或全部调试表。

### Task 1: 原生采集与导出

**Files:** 新建`src/DFLegacy.Debug/src/client_state.h/.cpp`，必要拆分`client_state_json.cpp`；修改`debug_overlay.cpp`、Debug项目及tests项目；新增`src/DFLegacy.Debug/tests/client_state_tests.cpp`。

**Interfaces:** `dfl::client_state::Initialize(settingsPath,directory)`、`CaptureFrame() noexcept`、`ExportLatest() noexcept`、`Shutdown() noexcept`。具体固定结构/可注入Reader置于client_state.h，worker消费结构，不调用游戏getter。

- [x] 测试先RED：假内存验证身份/保护整数、HP/死标、两种坐标getter、4人/敌人上限、门、技能等级；未知/损坏/null/provider激活、重复ID、dead detached、错误布局及截断。
- [x] 实现有界原生只读采集、稳定性复核、布局门禁；从独立状态读取scene，不依赖旧Frame.scene。
- [x] 测试先RED：采集/写盘时间不混用、TryLock跳过、worker原子文件、无新样本不续期、关闭及隐藏面板仍导出。
- [x] 集成既有渲染和worker循环，默认关闭，序列化在锁外；focused与完整debug测试GREEN，Release构建通过。

### Task 2: 紧凑消费和条件等待

**Files:** 新建`tools/debug/client_state.py`、`tools/debug/test_client_state.py`；更新`tools/debug/README.md`。

**Interfaces:** `load_snapshot(path, *, pid, process_started_filetime, now_tick_ms, max_age_ms=750)`严格校验并返回结构；`summarize(snapshot)`输出小JSON。CLI `--directory --pid --process-started-filetime [--uid] [--wait dotted.path=value --timeout-seconds N]`，默认once；不读游戏内存、不写输入。

- [x] RED：错误schema/PID/start/UID/未来时间/过期/旧样本重写/UTC跳变/partialJSON/未知状态/截断；字段路径只解析数据，不eval。
- [x] 实现一次读取、变化摘要和有界条件等待；等待仅输出最终匹配或明确timeout，避免每个frame打印。
- [x] GREEN：标准库unittest，四PID独立数据隔离和场景代次变化案例。

### Task 3: 双端交叉验收与交付

**Files:** 新建`knowledge/decisions/2026-10-28-debug-client-state-verification.md`与精选原始图片/有界JSON；更新README/STATUS/backlog。

- [x] 最终代码独立复核；修复Critical/Important，完成必要构建与现有debug回归。
- [ ] 原配置/原档与新DLL保护备份，隔离存档启动两端。记录实际模块身份，以数据完成Town→副本→换房→死亡→复活→回城，并短窗对比既有只读probe；截图限关键视觉节点。
- [ ] 记录capture开销/大小/刷新与等待响应实测，明确CD未知或新闭合结果；故意停渲染/过期和两端隔离不能误动作。
- [ ] 同一实测DLL交付、恢复临时配置/输入、原档哈希核验、图文记录和本地提交。随后接U01/U03/U04与怪物同步，不把本接口当四人掉落已验。
