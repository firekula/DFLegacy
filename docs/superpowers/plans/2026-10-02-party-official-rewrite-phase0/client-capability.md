# 客户端协议面能力表（DNF.exe 1.0.1.9）

生成物（本目录，可直接复核）：

| 文件 | 内容 | 生成命令 |
| --- | --- | --- |
| `client-noti-table.txt` | 152 行：`NOTI <id>: handler=0x… default=yes/no` | `python -c` 循环跑 `work/client_surface.py noti <id>`（见本节末） |
| `client-cmds-run42.tsv` | 608 个官服命令 × 5 列（官服名/派发器/真机 CMD 次数/真机 NOTI 次数/我们的常量） | `python work/client_surface.py cmds --log ../2026-10-01-party-loot-card-alignment/real-client-run-42-server.log` |

## 1. 通知分发表（可达层的根）

- 分发器：`0x41944E` 读包内 `[eax+1]` 作 id，`cmp …,0x97; ja default` ⇒ **有效 id 只有 0..151**；
  跳转表 `jpt_419474` @ `0x42A081`，default = `0x42A069`（公共收尾 = 什么都不做）。
  证据：`.superpowers/sdd/2026-09-27-town-multiplayer-visibility/client-re-town-actors.md` §0.1、
  `friends-protocol-recon/work/{jt.txt,client_switch.txt}`。
- 实测统计（`client-noti-table.txt`）：**in-table 130 / default 22**。
- default 名单（与 IDA 注释逐字一致，互为交叉验证）：
  `25, 52, 62, 63, 67, 108-119, 129, 133, 135, 146, 147`
- **超出 0..151 的官方通知在本客户端不可达**（除非另有第二张表，尚未查证）：
  `NOTI 153`（队伍实时信息）、`NOTI 281/282`（官服 routing）—— 相关条目已按"无法实现"裁决。

## 2. 上行命令表（我们真正需要的部分）

完整表见 `client-cmds-run42.tsv`。判读要点：

- `seen_as_cmd` 与 `seen_as_noti` **必须分开看**：CMD 与 NOTI 是两套命名空间（`CMD 39 = FISHING`，
  `NOTI 39 = GET_ITEM`）。
- run #42 实测（`seen_as_cmd`）：`2`=4、`37`=32、`40`=32、`46`=23、`48`=12、`49`=4、`73`=8、`74`=11、`13`=0、`45`=0。
- **客户端从不发**（run #42 = 0，且与既有记录一致）：`135 BACK_2_VILLAGE`、`169 CALL_PARTY_MEMBER_REALTIME_INFO`、
  `100/108/329/336/479/599`（P2P 统计族）、`301 BIDDING_ROUTING_ITEM`。
- **已知偏差**：`CMD 142` 官服名是 `GUILD_MEMER_LIST_NEXT`，而我们的常量是 `142 → BackToVillage`
  （客户端在选择界面用它当"返回城镇"，见 `STATUS.md` §2.7 第 13 条）。

## 3. 字段级读取序列（关键通知）

`work/client_surface.py noti-fields <id>` 给出**线性**读取序列（分支合并、循环体只出现一次；首个读取常是计数）：

| NOTI | 处理器 | 线性序列 | 备注 |
| --- | --- | --- | --- |
| 11 `USER_UDP_IP_PORT` | `0x41C7E7` | `byte, short, int, int, short, int, byte, int` | 首 byte = 成员数；每成员 7 字段（无第 8 个） |
| 39 `GET_ITEM` | `0x421EA7` | `short, short, int, byte, int, byte, int, byte, short, short` | 前两个是 groundId/uid；item 分支再读 4×u8 + u16 + u16 |
| 40 `DROP_ITEM` | `0x422CC3` | 见 `client_surface.py noti-fields 40` | 掉落建物路径 |
| 86 `SERVER_BROADCAST` | `0x427104` | 只读 1 字节子类型 | 只实现 1/2；官服发的 **5 不处理** |

## 4. 删地面物的唯一路径（P4 的根因）

`0x417c40`（→ `0x46ed60`）是删地面 actor 的调用；它的**全部**调用点只有两个：

- `0x4224CB`：NOTI 39 处理器内，门 = `payload u16@8 == myUid`（全局 `0xb75a40`）；
- `0x4229BD`：NOTI 93（`WARROOM_GET_ITEM`）处理器内，同样以"载荷 uid == 我的 uid"为门。

⇒ 非拾取者没有任何路径能删掉别人的地面物。`NOTI 39` 那条在删除后还会把物品**塞进本地背包**，
所以也不能借它去"清掉队友那件"。

## 5. 复核命令

```powershell
# 生成通知表（152 行）
cd G:/Game/DNF_ACT1/DFLegacy/.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite
python -c "import subprocess,sys;[print(subprocess.run([sys.executable,'work/client_surface.py','noti',str(i)],capture_output=True,text=True,encoding='utf-8').stdout.strip().splitlines()[0]) for i in range(152)]" > client-noti-table.txt
# 生成上行命令表
python work/client_surface.py cmds --log ../2026-10-01-party-loot-card-alignment/real-client-run-42-server.log > client-cmds-run42.tsv
# 自证
python work/client_surface.py --selftest
```
