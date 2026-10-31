# S0 真机验收 RUNBOOK（一条命令）

2026-10-30门前终点增量验收见[修复记录](../../knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)。延续当前手测进度时只重启所属客户端，不重建私有档或停掉独立服务；旧／新PID和DLL分别绑定证据，更新恢复期望哈希而不覆盖原始备份。门内目标拒绝须由新鲜SDK位置保持佐证，不为测试点击副本选择门。当前会话保留供用户手测，清理由该私有目录 `Stop-ManualTest.ps1` 执行。

2026-10-30当前接续以[交接](../../HANDOFF-2026-10-30.md)和[debug操作说明](../debug/README.md)为准：先核对已有验收范围，只补新场景；四端PID／创建时间与DLL哈希须绑定同轮journal，窗口按田字形排列。启动前建立新鲜替换文件备份／保护哈希，拒绝残留`auto-input.json`和`.focus`，使用显式私有档来源，关闭所属进程后恢复并复核。保留关键原生截图；普通步骤从SDK／CLI读状态。下方Task6“UI无法自动驱动”与单账号提示是早期操作卡，不覆盖当前能力和已验证四账号环境。

2026-10-30 存档来源补充：默认从服务端`server.json`的`DFLegacy.DataPath`读取（相对路径以服务端可执行目录解析，缺省`data/state.json`）；已有`DFLegacy__DataPath`覆盖同样计入来源。每轮仍只复制到私有存档，不回写模板。需要延续上一轮的技能栏时，显式传`--seed-state <上一轮关闭后保存的state.json>`，避免从旧模板重建；来源、所选字节及副本SHA256写入`server/state-seed.json`。保持同档重登／服务端重启时直接复用该轮的DataPath，不再次种子复制。下方旧Task6人工UI说明是早期场景；目前具备独立native/debug控制器，关键原图显式请求。

> **Task 6 需要人**：客户端闭源，UI 动作无法自动驱动。本文件是给真人照做的"一条命令"操作卡。
> **party15 崩溃不在范围**：若队员收到自己的 CMD 73 后约 15 秒被 RST，**记录现象即可，不要修**。
> **失败留档**：若验收失败，`runs/<runId>/` 与命令 stdout **都要留档**（两者都是证据）。

## 前置检查

1. **两个不同账号**已写入 `G:\Game\DNF_ACT1\DF\town-test\launcher.json`。
   > 目前只配置了 1 个账号，**需用户自行补第二个**（A、B 两个账号不同）。
2. 客户端 `Config.ini`（`G:\Game\DNF_ACT1\DF\Config.ini`）有 `DumpNetwork=1`，且 `ijl15.dll` 是带 netdump 的那份。
3. `DNF.exe` 目录 = `G:\Game\DNF_ACT1\DF`（netdump-*.log 就写在这里）。
4. 端口未被占（TCP 7001、TCP/UDP 2311）：
   ```powershell
   Get-NetTCPConnection -State Listen -LocalPort 7001,2311 -ErrorAction SilentlyContinue
   Get-NetUDPEndpoint -LocalPort 2311 -ErrorAction SilentlyContinue
   ```
   若被占，**先停占用者**（本项目服务端或 `docker stop dnf`），不要硬起。

## 一条命令

工作目录固定 `G:\Game\DNF_ACT1\DFLegacy\tools`：

```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m acceptance.cli run --scenario town-two-clients --actors 2 --actor-account A=<账号1>,B=<账号2> --client-dir G:\Game\DNF_ACT1\DF --runs-dir ..\runs
```

## 这条命令会自动做完

- 新建 **全自动 runId**（时间戳-短哈希-场景，禁人工编号）并建 `runs/<runId>/`；
- 把活存档复制成 **run 私有副本**，服务端用 `DFLegacy__DataPath` 指向副本写 —— **活存档不再被 run 写**；
- 复制 `server.json` 作 provenance（原排版与 `//` 注释键原样保留，不重写）；
- 把每步墙上时间记进 `runs/<runId>/steps.json`；
- 从 `launcher.log` **新增行**自动归因真实客户端 PID（不猜、不外泄密码）。

## 真人只需做

按 CLI 提示逐步完成 UI，每步完成按一次回车：

1. A：登录 → 选角 → 进城，回车；
2. B：登录 → 选角 → 进城，回车；
3. A 走到 B 附近，回车；
4. 最后一次回车让 CLI 收尾（归集 netdump/offsets、判定、写出 verdict）。

> CLI 会把 `netdump-<pid>.log` 从 `--client-dir` 归集进 `runs/<runId>/actor-<名>/`，写 `offsets.json`、`verdict.json`、`verdict.digest.json`，并把归一化判定提升到 `tools/acceptance/verdicts/town-two-clients.json`。

## 验收判据（三条）

1. **两次一致**：对同一次 run 再判一次（`python -m acceptance.cli judge --run-dir ..\runs\<runId>`）⇒ 两次 `status` 与 `counts` 必须一致。
2. **能变红**：把 `town-two-clients.yaml` 里某个 `[存在]` 期望的 `pattern` 改成不存在的串（例如 `c2s:no-such-frame`），再 `judge` ⇒ 必须出现 `fail` 且 `status == "red"`。**改回**后回到非红。
3. **自解释**：只看 `runs/<runId>/` 能回答"跑的是什么／谁参与／T0 依据／哪几项要人看"
   （`manifest.json` = 跑什么+谁参与+`created_utc`；`steps.json` = 每步墙钟；
   `offsets.json` = 时间对齐辅助；`verdict.json` = 三分结果与命中行号；
   两项限制见下方"T0 依据"与"词表"）。

> **T0 依据（如实）**：run 目录里能回答的 T0 依据**只有** `manifest.created_utc`（**控制端时钟**，
> 即 CLI 建 run 的时刻）。**服务端的 server-accept 时间没有被记录**——`offsets.json` 的数值是
> "控制端时钟 − 该 actor 首帧时间"，因此含真实经过时间（实测 A −6.985s / B −8.222s，
> 其中 7–8 秒是"起服务端→客户端首帧"的真实间隔），**不是**已证明的跨机时钟偏移，
> 更不能当作服务端 accept 时刻。需要 server-accept T0 时，从 `server/server.log` 的
> `Entrance listener ready` / `connected` 行或人工计时另行确认。

> **判据词表（防止把绿读成"验收完全通过"）**：
> - `pass`：声明的**字节/帧**在证据里出现了。`[存在]` 档只证明「该帧在场」，
>   **不等于**登录成功、**更不等于**进城。
> - `needs-human`：`[语义]` 档，机器不判，必须人看（例如"A 能看到 B 同屏"）。
> - `missing-evidence`：该类证据**根本没产物**（不是"不符"）。
> - `fail`：证据在、但不符（这才叫红）。
> - `green-with-human` 只表示"没有 fail"，**不代表**语义项已通过；`pass:0` 的 run
>   也可能是 `green-with-human`，务必看 `counts` 而不是只看 `status`。
## 副本内战斗键验收（B′ 键盘控制，业主裁定：战斗只能在副本里验）

**为什么单独一条**：城镇不接受攻击/技能动作（`[真机]` t11：`X` 3.04%、`Z` 1.51~2.72%，
而同轮待机对照 3.15%，同量级）。所以战斗是否生效只能在副本里判，且必须带**同轮待机对照**。

**前置**：两个客户端已组队，队长已进入副本（截图确认加载完成）；
relay+stun 容器必须已起（组队副本必需，`docker ps` 应能看到 `dnf`）。

**一条命令**（工作目录 `G:\Game\DNF_ACT1\DFLegacy\tools`）：

```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m acceptance.combat_gate `
    --client-dir G:\Game\DNF_ACT1\DF `
    --server-log <run_dir>\server\server-A.log `
    --netdump G:\Game\DNF_ACT1\DF\netdump-<PID>.log `
    --out-dir <run_dir>
```

它按客户端自带「键盘设置」的绑定注入 `X`（普通攻击）/`Z`（技能）/`C`（跳跃，对照项），
采三类证据：① 服务端伤害/HP 事件；② netdump 里**待机窗口没有的上行帧类**；
③ 动作帧像素差 vs 同轮待机对照。产出 `<run_dir>\combat-gate.json`，退出码 0 = pass。

**自检（不需要游戏，CI/提交前跑）**：

```powershell
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m acceptance.combat_gate --self-check   # 10 条判定向量
python -m pytest acceptance\tests\test_combat_gate.py -q
```

**判据**（`acceptance/combat_gate.py` 的 `Thresholds`/`judge`，纯函数）：
- 服务端伤害事件 > 0 ⇒ pass（最强证据，像素无关）；
- netdump 新增上行帧类 > 0 ⇒ pass；
- 否则像素差必须 ≥ `max(8.0%, 3.0×待机, 待机+4.0%)`，否则 **fail**；
- 计划未被客户端取走 ⇒ `missing-evidence`（基础设施问题，不算产品失败）；
- 缺服务端日志/netdump ⇒ `missing-evidence`；
- 对照项（跳跃）**永不**单独构成 pass。
- 红线回归用例：`test_t11_reverse_case_must_fail`（3.04% vs 3.15% 必须 fail）。

### 双人组队进本的前置（2026-10-23 实测补充）

1. **服务端必须带中继开关**：`$env:DFLEGACY_UDP_RELAY='1'`，再起 `town-test\DFLegacy.Server.exe`
   （启动日志应出现 `Gameplay UDP relay enabled (DFLEGACY_UDP_RELAY=1)`）。
   不带这个开关时组队会卡在「连接中」、队员互动走不通（STATUS §3.6 已定因：
   组队互动走客户端↔客户端通道，由服务端扮演 relay）。
2. **两个账号**：`test` 与 `shizuku`（`launcher.json` 的 `Accounts` 里现成两个）。
   两次 `DFLegacy.Launcher.exe --headless --no-dialog --account <账号>`，间隔 ≥5 秒。
3. **共用客户端目录靠"前台闸"隔离**：同一份 `auto-input.json` 只有**拥有前台窗口**的客户端会推进。
   所以驱动两个客户端时必须用**真实点击聚焦**（`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/work/focus.ps1`，
   `tier3_dual_drive.py` 的 `drive_plan_for` 就是这套：写计划 → 每 10 s 重新聚焦直到 `.done`）；
   仅 `SetForegroundWindow`/`AttachThreadInput` 不够——**真实点击才能唤醒 DNF 的 DirectInput 轮询**。
4. **进本门与站位**：`[dungeon gate]` 区的 `dest=-1` 门（不是 `type 1`）；必须走进**门矩形**
   （`D_StormPass2`：`x 14..33, y 354..479`）。弹出选图界面（`state=2`）后才点「前往地下城」(447,395)；
   城镇里点同一坐标会打开赛丽亚 NPC 菜单，误点「进入商城」会让客户端**干净退出**。
