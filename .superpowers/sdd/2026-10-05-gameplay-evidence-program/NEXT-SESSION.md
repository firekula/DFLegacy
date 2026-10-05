# 接续提示词（复制下面整段发给新会话即可）

---

读这三个文件（按序，别跳过）：

1. `docs/superpowers/plans/2026-10-05-gameplay-evidence-program-m0-m1.md`（实施计划，Task 1–11）
2. `docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`（spec）
3. `.superpowers/sdd/2026-10-05-gameplay-evidence-program/RESUME.md`（**接续块**：现状 / 产物清单 / 可重跑命令 / 环境事实 / 已知答案）

**当前进度**：M0（Task 1–7）✅ 完成；评审门 ✅ 已裁决（待评估队列**全部纳入**、范围不缩、判据 3 已补全）；
B0（Task 8）✅ 完成；**Task 9 ✅ 完成（7001 自检 PASS，spec 的硬门 S3 未触发）**。
**待做：Task 10（`token.py`，纯离线）→ Task 11（游戏面自检）。** 从 Task 10 开始，按计划逐步做。

**提交纪律**：沿用本工程既有做法——每个任务末尾按计划的提交点提交一次，
**只提交该任务 paths 里列的文件**（`.superpowers/` 被 gitignore，需 `git add -f`；
工作区有大量无关未提交改动，**绝不** `git add -A`，一律用 pathspec）。
证据纪律见 `.recon/onboarding.md` §4（六类标记 + `[作废]` + 每条结论附可重跑命令）。

---

## 已实测的纠正（**别重新踩，也别重新推导**）

1. **游戏面帧头 = 10 字节**：`[0]cat(0x01=CMD/0x00=NOTI) [1]id [2] [3..6]size(LE,含头) [7..9]3字节`，
   **与 7001 的 11 字节头不同**（`[0]cat [1]id [2..5]size [6..9]reserved [10]ackOk`）。
   已实现在 `work/driver/dnfdrive.py`（`CHANNEL` / `GAME` 两个**显式**模型，不做自动嗅探），
   并用 B0 的七条真实流回归通过（全部"恰好铺满"）。
2. **会话密钥 = `id=12` 正文 `[4:20)`**（计划的 `[5:21)` 实测为错）。
   定案证据：拿候选密钥解 `id=10` 的 13312 B 密文——`[4:20)` 可打印率 97.75% 且解出
   `[dungeon]`/`channel_info_dname_*` 频道表；`[5:21)` 与 `[0:16)` 均为垃圾。
3. **`id=3`/`id=10` 正文 = `zlib(AES(明文))`，没有 `count(1)` 前缀**（跳过首字节反而 zlib 失败）。
4. **7001 自检的判据① 不能是"整帧长度恒定"**：会话密钥按登录时刻生成 ⇒ 密文不同 ⇒
   `zlib(密文)` 长度差几字节（实测 replay `[47,43,7240,139]` vs capture `[47,43,7235,140]`）。
   正确判据 = **结构性不变**（帧数 + `(cat,id)` 序列 + 解密后明文长度一致，外加 `id=10` 明文逐字节相同）。
   见 `work/driver/selftest-7001.py` 的 docstring（证据写在里面，评审可核）。
5. **docker 容器的游戏面进不去**（`err_code = 15` 在该 build 里无 handler → `Abnormal LoginError` →
   `DisConnSig(14)`）。根因是上游 **Rust 重写的 gate** 与官服 `df_game_r` 的 `err_code` 枚举不一致；
   **不是**我们的客户端、**也不是** IP 白名单（该假设已用单变量实验**否证**）。
   ⇒ **Task 11 的游戏面"成功"自检要用 VM 那套**（`192.168.200.131`，游戏面端口 **`10013`**）；
   docker 的 5 帧序列（末帧 `NOTI 199 DISCONNECT_REASON`）可作**拒绝路径**自检。
6. **两条 L2a 前置条件**（每次打官服靶子前都要做）：① 清掉本次账号在 `d_taiwan.member_join_info`
   的行（PK 冲突会让登录异常断连，症状只有客户端一句"连接失败"）；② 客户端 IP 需在
   `d_taiwan.geo_allow` 里（容器看到的通常是 `172.17.0.1`，**默认不在**名单）。
7. **环境坑**：① `--network container:dnf` 的抓包 sidecar **熬不过 `docker restart dnf`**（必须重建，
   重建后用 `cat /proc/net/dev` 或 `ip -o addr show` 验有 `eth0`）；② **容器 publish 绑失败不会自动重试**
   （`docker inspect` 里显示 `[]`，即使占用者后来走了也要再 `restart` 一次）；③ `netsh`/`pktmon` **需要管理员权限**。

## 环境事实（2026-10-05 23:2x）

* `DFLegacy.Server` 在跑（pid **43904**），占宿主 **7001/2311**；容器 `dnf` 的 `7001/tcp` 是 `[]`（= 基线形态）。
* 容器 `dnf`、`dfl-recon` 均在跑；`dfl-recon` 提供 `c++filt`（demangle 工具依赖它）。
* VM `192.168.200.131`：SSH 需**老算法**（`-o KexAlgorithms=+diffie-hellman-group14-sha1
  -o MACs=+hmac-sha1 -o HostKeyAlgorithms=+ssh-rsa`）；本机私钥 `<用户>\.ssh\id_rsa_b0`。
  ⚠️ 公钥已装入 VM 的 `authorized_keys`（指纹对得上）但**认证仍被拒**（疑似 SELinux/StrictModes，**未解决**）
  —— 所以要用 VM 时，可能仍需让所有者在 VM 里代跑命令。
* 证据产物：`work/captures/` 下 `e2e-official-to-game.pcap`（docker 失败路径）、
  `b0-vm-success.pcap`（VM 成功路径）、`internal-during-retry.pcap`（全 TCP 内部流量）、
  单向流目录 `streams/`（docker）与 `streams-vm/`（VM）。

## Task 10 要做的（计划 Task 10）

`work/driver/token.py`：`token.sign(uid:int, key_path:str) -> bytes`（256 B）、
`token.verify(sig, uid, pub_path) -> bool`。实现依据（已实证）：
**裸 PKCS#1 v1.5 type-1 块**（`rsa` crate 的 `Pkcs1v15Sign::new_unprefixed()`），
被签内容 **46 字节** = `00 00 00 <uid 大端>` + `01 ×32` + `55 91 45 10` + `01 04 03 03 01 01`；
密钥用 `$W/container-privatekey.pem`（其模数等于 `$W/container-game-publickey.pem`）。
判据：① 签名 **256 字节**；② 用公钥**验签通过**；③ 模幂后前 4 字节 `00 01`、其后含 uid。
额外要求：与抓到的三条令牌**同形**（`[推断]` 标注：46 字节里除 uid 外的 42 字节语义仍未解）。
产物 `work/out/token-offline-check.txt`，按计划提交。

## Task 11 要做的（计划 Task 11 + 上面的靶子修正）

`work/driver/selftest-30011.py` + `work/out/driver-30011-selftest.txt`。
期望值**必须从 B0 抓包抽**（`work/captures/streams-vm/` 的 `13681-10013.bin` 21 帧 /
`10013-13681.bin` 90 帧；或 docker 的 `streams/46386-30011.bin` 1 帧 ↔ `30011-46386.bin` 5 帧），
**不许写死在代码里**，抽不到就报错退出。
若 VM 靶子不可达（SSH 未解决），退化为用 docker 的**拒绝序列**做自检，并**显式记录**"成功路径未在本机复现"。
收尾时按计划更新 `RESUME.md`。

## 建议的第一步动作

1. `recon_status` 确认 `.recon` 无配置问题（应报 8 工具）；
2. 读上面三个文件；
3. 直接开始 Task 10（不需要动环境），跑完提交，再做 Task 11。
