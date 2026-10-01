# 最后一轮静态闭合：普通拾取、NOTI39 与 PVF 路由边界

## 证据范围

官服 `df_game_r.bin` SHA-256 `8859c99d8290dc5fb58151444138cf2697b2bfff12d47d6159596a03f20b5261`；官方客户端 `G:/Game/DNF_ACT1/DF/DNF.exe`；PVF `G:/Game/DNF_ACT1/DF/Script.pvf`。方法为 ELF/PE 原始反汇编、符号、客户端跳转表和既有 PVF 解密脚本，均为 `[静态]`。

## 1. 普通拾取 NOTI39 布局（已闭合字段宽度）

官服 `CParty::get_item @0x085A3B98` 的成功广播 `0x085A6228–0x085A632D`：

```text
class=0, cmd=39
u16 field_item_or_pickup_id      // put_short(函数参数 +0x10)
u16 picker_uid                    // CUser::get_unique_id
u8  score[4]                      // 每个 party slot 的分数/骰子结果
u16 winner_uid                    // 选择出的成员 uid
u16 extra_short                   // 由局部 +0xD4 得出
u8  extra_byte                    // 由局部 +0xE4 得出
```

来源：`0x085A6246` put_header(0,39)、`0x085A625B` first short、`0x085A627B` picker uid、`0x085A62A0` 4 个 put_byte、`0x085A62BD` winner uid、`0x085A62EA` extra short、`0x085A6302` extra byte、`send_to_party @0x085A632D`。

客户端 `NOTI39` handler `0x00421EA7`：先读两个 `u16`（`0x00421EBF/0x00421ECE`），再按对象的固定 4 成员循环读 `u8` 分数（`0x004222AB–0x00422329`），以已选/胜者 uid 驱动物品显示和地面对象状态。

尚不能仅凭静态命名两个 u16 的业务名字：第一个在官服来自 `+0x10` 形参，客户端先用于对象查找；第二个是 picker uid/winner uid 的客户端对象索引输入。两者的字节宽度和发送顺序闭合，精确字段名保持 `[未验证]`。

## 2. 普通拾取计分（结构已闭合，输入语义部分未闭合）

普通路径对每个有效队员：

- 基分计数数组在局部 `ebp-0x234`；
- 初始/参与条件读取 `party+0x380+slot`；
- 成功插入物品后基分 +1（`0x085A5A03–0x085A5A13`）；
- 某些非叠加/物品虚函数条件与 `CItem::check_job_type` 可追加 30（`0x085A5A4B–0x085A5AA8`）；
- 调 `get_rand_int(100-base)`，把结果加回该成员分数（`0x085A5AE1–0x085A5AF4`）；
- 第二遍只看有效且 `party+0x380+slot` 的成员，选最高分；同分正分会减一以打破平局（`0x085A5B77–0x085A5C36`）；
- 成功后 `NOTI39` 广播包含 4 个 score byte 与 winner uid。

因此“服务端先算分/随机，再广播完整结果”是 `[静态]`；分数字段在客户端 UI 上的文字语义仍 `[未验证]`。

## 3. NOTI39 客户端状态路径（字段用途部分闭合）

客户端 `0x00421EA7`：

- `u16` 字段读入局部 `-0x1DCC/-0x1DC4`；
- 取本地地面对象/地图管理对象；
- 每个非零成员 score 通过 `0x404320`/`0x433550` 取成员模型，更新其状态；
- 若有胜者/匹配对象，调用 `0x434560`；
- 对本地 uid匹配时进入本地物品对象清理/显示；
- 物品显示使用第二组对象字段和结果值。

这足以证明当前客户端期望“全队一份完整 NOTI39”，不是每个会话各发一份本地获奖包；但 UI 中最后 short/byte 的精确名称仍需动态抓包或继续跟入虚函数，保持 `[未验证]`。

## 4. PVF 隐藏 routing 检查结果

扫描当前 ACT1 PVF 所有 `.equ/.stk` 文件：

```text
直接 [epic routing] 命中：0
直接 [routing priority] 命中：0
```

同时未发现 include 行含 routing 关键字。已有字段统计仍是 `[rarity]` 与 `[usable job]` 大量存在。

官服格式支持字段并且复制链已闭合：

```text
ImportEquipmentScript [rarity]              → STEquipmentScript +0x10
CEquipItem::set_item                       → CItem +0x38
ImportEquipmentScript [epic routing]        → STEquipmentScript +0x7BC
CEquipItem::set_item                       → CItem +0x79
ImportEquipmentScript [routing priority]    → STEquipmentScript +0x110 pair vector
CEquipItem::set_item                       → CItem +0xC0 pair vector
ImportEquipmentScript [usable job]          → STEquipmentScript +0x38..+0x42
CEquipItem::set_item                       → CItem +0x3C..+0x47
```

来源：`ImportEquipmentScript` `.rodata` 标签和 `.text` 引用；`CEquipItem::set_item @0x0850FD84` 的 `0x08510150–0x08510484`；PVF扫描命令使用既有 `d-pvf.py`。

结论：当前 PVF 没有明文 routing 物品样本；不能静态证明 ACT1 实际会生成 `CItem+0xBD=1`。Special Item Routing 的服务端机制保留为第二层 `[未验证/未启用]`，不应强行加入第一层普通掉落。

## 5. 本轮最终边界

已闭合，可先处理：

1. 普通 party loot：单一实例内统一计分/随机/胜者，向全队发一份 NOTI39；
2. PVF rarity/usability 读取及物品对象字段映射；
3. 普通 NOTI39 的协议字段宽度与客户端四槽分数消费；
4. 翻牌 party-level 自动补全及客户端 owner 首写不覆盖（前述 `STATIC-FINDINGS`/`ida_cardflow` 证据）。

暂不阻塞、继续标记未验证：

1. NOTI39 两个额外字段的业务命名和最终 UI 文案；
2. 当前 ACT1 是否存在运行时生成 routing item；
3. NOTI281/282/CMD301 在当前客户端是否有扩展入口；
4. Special Item Routing 的动态实际行为；
5. 普通 score 的 UI 文字含义和所有特殊物主调整条件。

这轮没有修改生产代码。
