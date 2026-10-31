# B′ 真机执行手册：合成输入单键门（T2）与无人工 UI 验收门（T3）

> 2026-10-22 实测通过。本文是"照着做就能复现"的操作卡：前置、步骤、判据、证据位置、
> 已知坑位全部来自 2026-10-22 的两轮实测（T2 一轮 + T3 两轮，verdict 均为 green-with-human）。

## 0. 这套门验证什么

- **T2 真机单键**：客户端在真机收到 `auto-input.json` 时间线后，合成鼠标/键盘注入能引发
  **网络动作**（netdump 新封包）与**可观测 UI 变化**（截图 diff），客户端不崩溃。
- **T3 无人工 UI 验收**：`handshake-login-town` 场景全程无人工 UI 跑通——launcher 自动登录、
  频道选择/公告对话框/选角/进城全部由合成输入驱动，verdict 无 fail。

## 1. 前置状态（每一条都查过，缺一不可）

1. **构建产物**：`artifacts\native\ijl15\Release\ijl15.dll`（含 auto_input + net_dump）。
2. **部署点有两个**（只有一个是坑）：
   - `G:\Game\DNF_ACT1\DF\ijl15.dll`（客户端加载的那份）；
   - **`G:\Game\DNF_ACT1\DF\town-test\ijl15.dll`——launcher 每次启动都会把它安装到客户端目录**
     （launcher.log 的 `Installed x86 ijl15 replacement ... sha256=...` 行）。**只部署到 DF\ 会被
     launcher 用旧版覆盖**——这是 2026-10-22 实测踩过的坑（三处哈希必须一致：
     artifacts = town-test = DF）。
3. **客户端配置** `G:\Game\DNF_ACT1\DF\Config.ini`：
   - `[Features] EnableAutoInput=1`（默认关；关 = 客户端行为与旧版逐字节一致）；
   - `[Features] DumpNetwork=1`（netdump 证据）；
   - **`[Rendering] ScaleFilter=linear`（不能是 native！见 §4 坑位 2）**。
4. **端口空闲**：TCP/UDP 2311、TCP 7001、TCP 8081。
5. **干净计划目录**：DF\ 下无残留 `auto-input.json` / `auto-input.done.json`
   （残留 .done 会让第一次 `await_done` 立即误报 True）。

## 2. T2 单键门（十步）

工作目录 `tools\`，客户端自动登录后停在**频道选择**界面（launcher `--account test` 免掉了登录框）。

1. 起服务端：`G:\Game\DNF_ACT1\DF\town-test\DFLegacy.Server.exe`（后台），等 2311/7001/8081 监听；
2. 起客户端：`town-test\DFLegacy.Launcher.exe --headless --no-dialog --account test`，等 DNF 窗口；
3. 等诊断线 `G:\Game\DNF_ACT1\DF\auto-input-diag.log` 出现两行：
   `keyboard hook installed` → `pump first call`（第二条出现 = 帧泵活了，约在客户端起来后 4 秒）；
4. 截屏 A（无注入）；等 2 秒截屏 B —— diff(A,B) = 动画噪声基线；
5. 记录 netdump 字节数（`DF\netdump-<pid>.log`）；
6. **注入**（tools\ 下）：
   ```python
   from acceptance import auto_input
   steps = [
       {'t': 400,  'op': 'click', 'x': 127, 'y': 97},    # 频道行 chi01新手-洛兰
       {'t': 1400, 'op': 'click', 'x': 320, 'y': 448},   # 「开始」按钮
       {'t': 2600, 'op': 'key',  'vk': 72},              # H 键（无文本框，无效果属预期）
   ]
   auto_input.write_plan(r'G:\Game\DNF_ACT1\DF', steps)
   assert auto_input.wait_ready(r'G:\Game\DNF_ACT1\DF', timeout_s=15)   # 装载（旧 .done 被消费）
   assert auto_input.await_done(r'G:\Game\DNF_ACT1\DF', timeout_s=30)   # 执行完
   ```
7. 等 5 秒（频道加入 + 选角界面加载）；
8. 截屏 C —— 应显示**选择角色**界面（光标已被合成 move 带到点击处）；
9. **判据（全部满足才算过）**：
   - netdump 出现注入后的新封包（实测 +2610 字节 = 进频道帧）；
   - diff(C, B) 显著大于 diff(A, B)（实测是"整屏跳转"级）；
   - 客户端无崩溃无卡死（进程存活、画面可继续交互）；
10. 收尾：杀客户端与服务端。

## 3. T3 无人工 UI 验收门

一条命令（驱动脚本在 `.superpowers/sdd/2026-10-22-client-auto-input/work/t3_drive.py`，
它 monkeypatch `cli._confirm`，把两个场景步骤换成合成输入计划，复用 CLI 的存档隔离/
launcher/PID 归因/netdump 归集/判定全链）：

```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy
python .superpowers\sdd\2026-10-22-client-auto-input\work\t3_drive.py
```

**它自动做的事**（对应场景 `handshake-login-town` 的两个步骤）：
1. 建 runId（时间戳-短哈希-场景）+ 存档隔离（`DFLegacy__DataPath` 指 run 私有副本）；
2. 起服务端（等监听）→ launcher 起 1 个 actor（`--account test` 自动登录）；
3. 步骤 1「完成握手并登录」：**无需 UI**——等待 + 截图（握手帧已在 netdump）；
4. 步骤 2「选角并进入城镇」：写计划 A（点频道行 + 点开始）→ 等 taken/done → 等 7 秒（加载 +
   公告对话框）→ 截图 → 写计划 B（点确认 → 点角色槽 → 点开始）→ 等 taken/done → 等 15 秒（进城加载）→ 截图；
5. 归集 netdump + 判定 + 写 verdict + 提升 digest 到 `tools/acceptance/verdicts/`。

**判据**：`status: green-with-human`、`counts: pass=3, needs-human=1, fail=0, missing-evidence=0`
（两个 [存在] 帧 + 服务端日志 pass；[语义]「人工确认同屏」按设计留 needs-human；
**fail 必须为 0**）。**两次连续运行结果一致**（可复现性判据，2026-10-22 两轮实测一致）。

## 4. 已知坑位（每条都真踩过）

1. **launcher 会覆盖客户端 ijl15.dll**：启动时从 launcher 目录安装替换模块（日志有 sha256）。
   只换 DF\ 不换 town-test\ = 白换。核对：三处哈希一致 + launcher.log 最新 install 行的
   sha256 == artifacts 的。
2. **ScaleFilter=native 时 D3D8 scaler 链整体不安装**（dnf_patches 只在 `!= Native` 时装），
   而 **SetTextureStageState/性能调用点两处帧泵都会失效**：性能调用点（0x83F996）只在部分场景
   执行（频道选择界面实测不执行），纹理状态代理又没装 ⇒ auto_input 全链死。**解法：linear**。
   （最终帧泵锚 = **GetAsyncKeyState 钩子本体**——游戏每帧轮询键盘、全界面有效，且合成键态
   对当次查询立即可见。纹理代理 + 性能调用点仍保留为次级锚。）
3. **计划文件只在装载时刻被消费一次**：客户端启动后写入的计划靠**每帧轮询**拾取
   （spec §3.2），轮询有 50ms 节流。写计划后等 `wait_ready`（装载）→ `await_done`（完成）。
4. **残留 .done 会让 await_done 秒回 True**：上一条计划的完成标记还在。写新计划前清掉
   （或依赖装载即清标记的新语义——客户端装载新计划时会删除旧 .done）。
5. **截图要用 PrintWindow(PW_RENDERFULLCONTENT)**：CopyFromScreen 抓得到桌面壁纸却抓不到
   被遮挡/后台的 D3D 窗口。
6. **进频道的加载过渡期是黑屏**：截图全黑 ≠ 崩溃——netdump 有 10 秒心跳（UDP 7003）即存活。
7. **诊断线**：`DF\auto-input-diag.log` 记录 `keyboard hook installed` / `pump first call` /
   `plan armed (N steps)` 三个里程碑；OutputDebugStringA 需要 DBWin 监听器，真机上不可靠。

## 5. 证据索引（2026-10-22 实测）

- T2：基线 A/B + 注入后截图 + netdump 增量（35799→38409，+2610），
  `.superpowers/sdd/2026-10-22-client-auto-input/work/t2-final-*.png`；
- T3 两轮：`runs/20261022T152212Z-9276e97-handshake-login-town/` 与
  `runs/20261022T152435Z-9276e97-handshake-login-town/`（verdict.json / verdict.digest.json /
  steps.json / actor-A/netdump-<pid>.log / server 日志）；
- 城镇画面实拍：`work/t3-3-town.png`（洛兰城镇、Lv.52 召唤师、完整 HUD、FPS overlay 可见）；
- 诊断线：`DF\auto-input-diag.log`（hook → pump → plan armed ×2 时序与驱动日志互证）。
- 回归：MSBuild 原生自测零警告零错误 + pytest 126 passed + smoke-tests 全过 + protocol.s1 verify 5/5。

## 6. 边界与未覆盖

- [语义]「人工确认同屏」按设计仍是 needs-human：机器证明的是"进城镇的帧与画面发生了"，
  "同屏正确性"留给 Task 11 的双客户端验收。
- `text` 步骤在真机上未单独验证（验收路径的登录已被 launcher 免掉；ASCII 账号名场景留给
  需要它的后续任务，风险见 spec §6.5）。
- 帧泵锚依赖键盘轮询活跃：若未来客户端补丁改掉 GetAsyncKeyState 轮询（spec §6.1 同款风险），
  帧泵需要重新挂锚。
