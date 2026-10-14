# S0 真机验收 RUNBOOK（一条命令）

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
3. **自解释**：只看 `runs/<runId>/` 能回答"跑的是什么／谁参与／T0 依据／哪几项要人看"。

> **T0 依据**：参考点是 `manifest.created_utc`（控制端时钟）；服务端 server-accept T0 需从日志/人工时间确认。`offsets.json` 的数值是**时间对齐辅助**，不是已证明的跨机时钟偏移。