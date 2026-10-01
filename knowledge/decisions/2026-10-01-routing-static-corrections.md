# 静态取证勘误：普通拾取骰子不等于 Special Item Routing

## 样本与方法

官服样本：`.superpowers/sdd/2026-09-29-dungeon-party/work/official/df_game_r.bin`；SHA-256 `8859c99d8290dc5fb58151444138cf2697b2bfff12d47d6159596a03f20b5261`。同目录 `template/init/df_game_r` 在上轮独立读取中具有相同字节和哈希，因此这只是副本一致性，不代表未修改发行版的独立验证。

方法：既有 `calls_in.py::Binary` 解析 ELF 节和符号，Capstone 从完整 `CParty::get_item` 函数入口解码；脚本标签通过 `.rodata` 字符串扫描和 `.text` 立即数引用扫描定位。PVF 使用既有 `party-protocol-recon/work/d-pvf.py`，并与 `PvfArchiveReader.cs` 的树/解密实现核对。所有本轮结论为静态证据，不是真机行为证明。

## 1. [作废] 普通拾取必然调用 StartRouting

旧结论：拾取入口按 IsRoutingItem 分流到 SpecialItemRoutingManager，且可由 rarity>3 得出蓝装阈值。

[静态] 反证：`CParty::get_item` 的 `0x085A56D1` 字节为 `EB 16`，无条件跳转至 `0x085A56E9`，把结果置为 0；此控制流绕过 `0x085A56D9` 的 `CItem::IsRoutingItem` 和后续 StartRouting 分支。该分支在本样本这条入口上不可达。是否来自发行镜像补丁尚未验证，不应擅自恢复成猜测的条件跳转。

[静态] 后续 `rarity>3` 位于普通分配路径，不能等同为 SpecialItemRoutingManager 的入口。`get_rarity` 的 +0x38 是稀有度，不是未知额外 routing 值。

## 2. [静态] 普通骰子路径与特殊候选路径不同

普通路径 `0x085A5896–0x085A5C3B`：

- 按 party+0x144 的上次索引加一选取轮转目标；
- 有效成员、party+0x380+slot 标志、背包可插入检查共同限制参与者；
- 候选基分增加 1，轮转目标增加 40；
- 特定非叠加类型和职业匹配条件下增加 30；
- 调用 get_rand_int(100-基分)，所得值与基分相加；
- 第二遍遍历按最高分选收件人；相同正分时后遇到者的显示分减一；
- 另有特殊物主强制调整分数路径，尚需追踪来源。

不能把上述分数规则推广为 ACT1 已验证行为：样本版本、输入标志、随机函数范围和当前客户端显示还需交叉核对。

特殊路径 `GetRoutingDiceMemberList @0x085BA294` 使用选择状态==1，优先职业/成长列表，再职业可用表，再所有参与者。它不是普通路径的同一个候选算法。

## 3. [静态] 职业辅助函数

`CItem::check_job_type @0x0850D728`：读取 item+0x3C+job 的 byte；对 job>10 返回 0。负数入口未被该函数显式保护，不能概括成完整范围校验。

`CItem::IsPriorityJob @0x085BEEC8`：遍历 item+0xC0 的 pair(job,grow) 列表。grow 1/17→1，2/18→2，3/19→3，4/20→4，其他→0；职业和标准化 grow 均匹配才返回 true。

## 4. [静态] 官服加载器确实有 PVF 路由标签

字符串：

- `[rarity]` @0x08E16243
- `[usable job]` @0x08E162BD
- `[epic routing]` @0x08E16AD0
- `[routing priority]` @0x08E16CB6
- `[/routing priority]` @0x08E16CC9

`ImportEquipmentScript`：

- `[rarity]` 分支 `0x08992EB7–0x08992EC8` 调 ScanInt 并写 STEquipmentScript+0x10；
- `[epic routing]` 分支 `0x0899A49E–0x0899A4AC` 读取整数，以 ==1 写 STEquipmentScript+0x7BC 的 byte；
- `[routing priority]` 分支 `0x0899B805` 置 STEquipmentScript+0x10F=1；解析职业字符串和 grow 整数0..4，写入 +0x110 的 pair 列表（`0x0899B840–0x0899B8E8`）。

这些是脚本中间结构偏移，不是 CItem 偏移。尚未追踪 STEquipmentScript→CItem 的复制/构造路径，不能声称 +0x10F 已映射到 CItem+0xBD。

## 5. [静态] 当前 ACT1 PVF 标签统计

扫描对象 `G:/Game/DNF_ACT1/DF/Script.pvf` 的 `.equ`/`.stk` 定义：`[epic routing]` 与 `[routing priority]` 直接命中均为 0。这只证明该范围没有明文标签，不证明默认值、include 继承或代码派生均不存在。

usable job：fighter单职业1529，mage1121，gunner1530，all4633，swordman1524，priest659；另有任务物品多职业与 sample 定义，不可按全量计数直接推断运行物品库。

rarity：equ 0=1027、1=1476、2=2055、3=300、4=230、缺省4477；stk 0=21、1=359、2=1029、3=95、缺省209。包含 sample，尚未按装备列表有效ID过滤。rarity数值与颜色须另查客户端，不应把 >3 叫蓝装。

## 6. 协议边界与前轮错误

[静态] ACT1 命令分发 `0x0040F2B6` 从 pkt+1 读 byte，id-1必须<=0x9E；通知 `0x0041944E` 同样读 byte，id<=0x97。因此不能直接给它套用新官服 NOTI281/282、CMD301。

[作废] 上轮对越界的281/282/301直接索引跳转表所得“处理器”是表外内存，不是有效协议证据；工具随后报 PEFormatError。已按分发边界重新核对。

没有全量证明不存在其他扩展入口；此项仍[未验证]。低字节截断也不能当协议支持，可能误投其他合法命令。

## 后续最窄取证范围

1. 追踪 ordinary get_item 的分配开关参数、物主标志、rarity条件和 NOTI39字段。
2. 官服脚本中间结构到 CItem +0x38/+0x79/+0xBD/+0xC0 的构造复制链。
3. ACT1 客户端 NOTI39 的骰子/获奖UID解析与 rarity颜色映射。
4. 镜像生成/补丁记录解释 EB16，寻找未修改同版本样本，禁止凭推测还原。
5. 翻牌统一核对 CMD74 byte/short/int宽度、卡类型0/1与自动分配；不提前改代码。
