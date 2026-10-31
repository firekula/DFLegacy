# 官服普通拾取／骰子隐藏规则完整核对

研究范围：本地 `df_game_r.bin`（既有 SHA256 `8859c99d8290dc5fb58151444138cf2697b2bfff12d47d6159596a03f20b5261`）的普通 `CParty::get_item@085A3B98`。未运行 IDA、构建、客户端或服务端；本目录仅新增分析脚本、汇编和报告。父任务提供的新反编译 `../official_full.txt`、`../official_helpers.txt` 与原始 Capstone 指令交叉核对。旧决策文档仅用于找缺口，没有作为官服证据。

**本轮最重要勘误：[静态] 普通物品骰子排除尸体，包括当前房间尸体。`party+896` 是活人标志；`party+900` 才是金币的本房进入资格。** 现有“活人和本房尸体参与普通物品”的规则与此样本不符。

## 1. 可直接落地的完整规则表

| 规则 | 精确行为 | 证据／级别 |
|---|---|---|
| 成员有效性 | 槽位 user 指针非空、当前 `GetUID()` 等于成员记录 UID、`getCurCharacR()` 非空。UID／角色无效分支还会断线并 Init 成员记录。不是单纯指针非空。 | `_checkValidUser@085B4D12`，成功条件 `085B4D33–085B4DAB`；[静态] |
| 普通候选生命条件 | 每个槽必须 `party[896+slot] == 1`。当前房尸体、上房尸体均不参加；不能把 `900` 代入。 | `085A5904–085A5927`；第二遍 `085A5B97–085A5BBA`；[静态] |
| 背包预筛 | 对完整 `Inven_Item` 调 `CInventory::tryInsertItemIntoInventory`，只有返回非 `-1` 的槽才成为候选。 | `085A5947–085A59FD`；[静态] |
| 基础分 | 每个候选从 0 增至 1。 | `085A5A03–085A5A13`；[静态] |
| 轮转目标 | `target = lastOrdinal+1; if target >= totalMemberCount: target=0`。分母是总成员数，不是活人数或背包候选数。 | `085A5896–085A58BA`；[静态] |
| 轮转序号 | 从槽0到槽3扫描，仅对通过 valid／alive／背包全部筛选的候选递增 `candidateOrdinal`。是压缩候选序号，不是 party slot。 | `085A5A1A–085A5A20`, `085A5B57`；[静态] |
| 轮转加分与提交 | `candidateOrdinal == target` 者 +40，并立即将其 ordinal 写回 `party+324`。未命中时不写回。写回发生在真正入包／摘地面之前，最终入包失败也不回退。 | `085A5A22–085A5A3F`；[静态] |
| 轮转初值 | `game_start` 时 `lastOrdinal = get_rand_int(totalMemberCount)`，不是固定0或-1。 | `0859D7A0/0859D7A8/0859D7B0`；[静态] |
| 职业+30 | 只有 `is_stackable != 1` 且 `GetItemType()==10` 的物品才检查 job byte；10已闭合为 `[weapon]`。`check_job_type(item, charac_job)!=0` 则 +30。护甲、首饰没有此加分。 | `085A5A4B–085A5AC3`；类型链见§4；[静态] |
| 普通随机 | `score = base + get_rand_int(100-base)`。base 只可能1、31、41、71。实际范围分别1..99、31..99、41..99、71..99，上界99。 | `085A5ACA–085A5AF7`；边界见§6；[静态＋有限枚举] |
| 幸运物主分 | map_item+0x50 非空时先抽 `ownerScore=89+get_rand_int(11)`，即89..99；候选若指针等于该物主，覆盖其正常分数；其他候选若 `score>=ownerScore`，压为 `ownerScore-1`。 | `085A58CF–085A58E8`, `085A5AFE–085A5B50`；[静态] |
| 物主资格 | 物主不绕过有效性、活人或背包预筛；该物主未成为候选时，其他人仍受上述分数上限压制，随后正常选最高者。 | owner改分在预筛之后；[静态] |
| 随机调用顺序 | 若有物主，先抽一次89..99；随后按槽顺序每个候选都调用普通随机，包括最终被覆盖的物主本人。 | 同上；[静态] |
| 赢家 | 第二遍槽0..3扫描，`score>runningMax` 才替换赢家。同最高正分时先槽获奖，后槽显示分减1。 | `085A5B77–085A5C3B`；[静态] |
| 并列调整范围 | 只处理与**当时 running maximum**相等的正分，不保证四个显示分两两不同。例如99,99,99会变99,98,98。 | `085A5BF7–085A5C21`；[静态] |
| 未参与分 | 无效、死亡、背包失败的槽保留0；非骰子的直接拾取四槽也全0。 | 函数开头 memset；候选++位置；[静态] |
| 无候选 | winner保持null，向全队广播 CMD46失败 `(0,22)`，返回false；不转给拾取者、不入包、不摘地面。没有候选也不会写回轮转。 | `085A5C62–085A5CE5`；[静态] |
| 最终入包失败 | 已选得主调用 `insertItemIntoInventory`／`addAvatarItem`，返回-1则只对原拾取者报 CMD46 `(0,4)`；不改选第二名。 | `085A5EFB–085A60C5`；[静态] |
| 成功提交 | 最终入包后调用地面 `pickup_item`；成功后 NOTI39 全队广播 groundId、pickerUid、四槽分、winnerUid、目标背包槽及原样本尾byte。 | `085A6041`, `085A611C`, `085A6228–085A632D`；[静态] |

**轮转缺候选的实际后果：[静态推导]** 总成员4、仅2个候选、lastOrdinal=1时，下一目标为2；本轮无人获得+40，last保持1。以后候选数仍为2时会一直无人获得+40，而不是按2人取模继续。这是样本行为；是否兼容保留须产品裁决，不能“修正”后仍称逐字复刻。

## 2. 进入普通骰子与直取的完整门控

物品id为0的金币及id1199的按成员生成金币分支先于普通物品查表，不进入下列物品分配。

1. 总成员数<=1：得主直接设为拾取者，分数全0。
2. 总成员数>1：`routeEnabled` 初始为1。
3. `map_item[0] != 1`：先将 `routeEnabled=0`。
4. `rarity>3`：将 `routeEnabled=requestBool`，可以覆盖步骤3。
5. `map_item[0] != 0 && isEpicRoutingItem`：再次以 `requestBool` 覆盖。
6. 只有 `routeEnabled && (check_item_routing || IsAvatarItem)` 才进入骰子；否则直取。
7. `check_item_routing = isEpicRoutingItem || rarity>0`。

地址：`085A57CF–085A5890`；`check_item_routing@0850D756`；`isEpicRoutingItem@0846996A`；`CEquipItem` vtable `08C96E20` 的+16指向 `IsAvatarItem@08514D46`。

这里存在两个不能简化掉的例外：

- `[静态]` rarity0头像也可经 `IsAvatarItem` 骰子门控。
- `[静态]` `map_item[0]!=1` 并非一票否决；rarity>3时requestBool=true可重新打开。来源枚举必须保留原始byte再执行原顺序；仅存“可路由bool”会丢信息。

样本 `085A56D1` 的 `EB16` 已跳过普通入口特殊 `IsRoutingItem→StartRouting` 分支。此报告只描述持有样本的普通路径，不把不可达分支恢复成猜测的发行版行为。

`get_item` 入口对死亡拾取者（896!=1）会记反作弊，宠物id63025例外，但不会立刻return。**“死亡者不能当骰子候选”不等于该函数拒绝其所有拾取请求**；直取路径仍直接指定picker，不能据候选筛选推导入口授权。证据 `085A3C14–085A3CA1`。

## 3. 活人、当前房金币资格、死亡和复活

| 字段／动作 | 静态含义及改变 | 地址 |
|---|---|---|
| `party+896+slot` | 当前活人标志。`get_live_count`按该字段统计；普通骰子用它。 | `get_live_count@0859A8BC`，读取0859A8F1 |
| `party+900+slot` | 进入本房时具备分金币资格；死在当前房间后仍保留。`get_live_count_enter_map`按它统计。 | `get_live_count_enter_map@0859A918`，读取0859A94D |
| 开局 | 有效成员896=1、900=1、904=0。 | game_start 0859D98F/0859D99D/0859D9AB |
| `set_charac_live(user,0)` | 只清896，不清900或904；发NOTI32 uid/state0。 | 085B2A81–085B2A87 |
| `set_charac_live(user,1或2)` | 896=900=904=1；state1另增加成员记录计数。 | 085B2A1D–085B2A7D |
| 正常死亡 | 先要求当前896==1，已死重报返回错误18；随后set_charac_live(...,0)。 | die_user@085A7828，085A7942/085A79A3 |
| 自动复活时间窗 | 若GetRevivalTime>当前秒数，死亡后立即set_charac_live(...,2)，再清RevivalTime。 | 085A79AE–085A79F2 |
| 用币复活 | ReviveUser先寻找目标uid，要求其896==0；检查复活条件/币后set_charac_live(target,1)。 | ReviveUser@085B27C8，085B2863/085B29A5 |
| 真正推进房间时 | changePartyMemberInfo对死者：若900或904仍为1则900=0；若两者原本均非1只增加另一个计数；总会904=0。活人900保持1。没有按房号恢复旧尸体资格的路径。 | 085A8D1E，085A8D4C–085A8DBF |
| 不是每次move都清 | changePartyMemberInfoOnMoveMap的bool参数为0时才处理；IsEnterEachMap地下城只处理请求中给出的成员槽，否则遍历四槽。 | 085A8E3C–085A8EDC |

金币分支（itemId0）：分母 `N=get_live_count_enter_map()`；N<=0报错误21。基础每人 `gold/N`，拾取者再得 `gold%N`；循环仍先检查有效成员，然后对900!=1者将本次基础所得清0。当前房尸体900仍1，因此能分金币；换房后900清0，不能继续分。宠物／装备／管理奖金、税和金币上限处理在此基础上追加。普通物品候选从未读取900。

金币证据：`get_item`原反编译 `ida_s5_loot.txt` 268–448行；原始汇编 085A3F80（进入人数）、085A42CC（900检查）。itemId1199的另一金币生成分支同样按900决定所得是否清0（085A4E18），没有普通骰子。

## 4. 武器类型和 usable job 映射

`CEquipItem` vtable首指针实际指向08C96E28；其+12位于08C96E34，值08514D26，即 `CEquipItem::GetItemType`，返回equip+0x234。

加载链完全闭合：

```text
ImportEquipmentScript@08991AEB
  [equipment type] → GetEquipmentType@08990F1F
  089938D9 call / 089938E1 写 STEquipmentScript+0x1B4
  CEquipItem::set_item@0850FD84 的0850FFAE复制→equip+0x234
  GetItemType@08514D26读取→普通计分085A5A69比较10
```

`GetEquipmentType`用`characterEquipmentTypeTags@093F4FC0`逐项strcmp。索引10的指针093F4FE8指向08E15F13字符串`[weapon]`。因此+30只针对武器；不是“所有非叠加装备职业可用就+30”。原始表与相关映射函数见`type_mapping_asm.txt`，复制见`set_item_asm.txt`。

职业表在093F4D40，由`getCharacterJob@088BE57E`及`getCharacterJobName@088BE60E`使用：

| job | 标签 | 字符串地址 |
|---:|---|---|
| 0 | `[swordman]` | 08E0879F |
| 1 | `[fighter]` | 08E087AA |
| 2 | `[gunner]` | 08E087B4 |
| 3 | `[mage]` | 08E087BD |
| 4 | `[priest]` | 08E087C4 |
| 5 | `[at gunner]` | 08E087CD |
| 6 | `[thief]` | 08E087D9 |
| 7 | `[at fighter]` | 08E087E1 |
| 8 | `[at mage]` | 08E087EE |
| 9 | `[demonic swordman]` | 08E087F8 |
| 10 | `[creator mage]` | 08E0880B |
| 11 | `[all]`（解析器未匹配前11项时的返回值） | 08E0881A |

`[usable job]`具体加载语义：

- 缺省清零11个job byte：STItemScript::Clear@0898D718，0898D7BC循环清script+0x38+0..10；Import入口08991D17调用equipment Clear再调用此base Clear。缺省不是all。
- 每个字符串经08993381的getCharacterJob；返回11时0899339E循环将0..10全部置1；否则089933C4只置对应byte。
- 注意解析器只对job0..10逐项strcmp，**任何未匹配字符串**都返回11，原生会按all处理；这包含`[all]`，也包含未知token。是否兼容此宽松行为须明示。
- set_item在0850FF66–0850FF88将script+0x38..0x42复制到item+0x3C..0x46，共11byte。
- `check_job_type@0850D728`仅对signed char job>10返回0，否则直接取item+0x3C+job。负数未显式保护；正常角色job0..10无此问题。
- 加分不检查grow、等级、武器子类、精通、`check_able_To_equip`或actual装备权限，只查基础职业byte。不要把高品质routing priority列表合并进该规则。

## 5. 背包预筛的准确含义

入口 `tryInsertItemIntoInventory@08501ECA`为const试算，没有实际插物品。接受完整61byte `Inven_Item`：id为0时数量>=0返回0，否则-1。非0物品按Inven_Item的类型byte分流：

| item类型byte | 检查顺序 |
|---:|---|
| 8 | `tryInsertAvatarIntoInventory@085035F4`，头像包0..104找首个itemId==0的槽，无则-1。 |
| 1 | 不做叠加；先对应栏空槽，再快捷栏3..8空槽。 |
| 2 | 先快捷栏叠加、再对应栏叠加；仍未成功则先快捷栏空槽、再对应栏空槽。 |
| 其他非8 | 先快捷栏叠加、再对应栏叠加；再对应栏空槽、快捷栏空槽。 |

普通空槽试算助手 `tryInsertInvenItemIntoQuickSlot@08501B70`、`tryInsertInvenItemIntoCoresspondingSpace@08501E12`：

- 非equipable item先检查数量是否合法（`checkStackableLimit@08501A79`）；失败返回-10，主函数将指定分支的-10终止为-1。
- 检查的是slot.itemId==0，不检查容量总件数的抽象计数。
- 对应栏界限由getStartEndPos@0850185E→StartEndPos@08501886→CSlotBoundChecker::get_slot_bound@080F1096获取，key包含类型byte及inventory+0x18的扩展值；找不到边界时StartEndPos给0..0。
- 样本CSlotBoundChecker::init@08373674的类型1/2/3/4/10基础区间分别9..56、57..104、105..152、153..200、201..248；末槽按扩展值0/8/16减16/8/0。快捷栏固定3..8。此为官服样本布局，不能直接假设ACT1兼容层也采用这些绝对槽号。

叠加助手 `tryStackInvenItemIntoQuickSlot@08501BF6`、`tryStackInvenItemIntoCorrespondingSpace@08501CEA`：

- 类型1返回-1（不叠加）。其他类型按槽序找相同itemId；只比较id，没有在这些助手中比较附魔、有效期等元数据。
- 首个相同id若existing>=INT_MAX-incoming，返回-2；若combined数量超过物品getStackableLimit或为负，返回-10；否则返回该槽号。
- 超堆叠上限的-10会令主试算直接失败，不会拆堆，也不会继续寻找空格建第二堆。整数上溢检查返回-2则可继续主函数的下一组尝试。
- `checkStackableLimit`对非stackable／找不到item定义返回true；对stackable要求0<=quantity<=limit，边界等号允许。

**明确的缺项：[静态] 整个候选预筛调用树没有负重、角色等级、职业、装备需求检查。** 它是栏位／叠加试算。把最终发奖的负重失败提前移进候选筛选会改变官服骰子参与者；如果产品需要该差异，应单列兼容裁决。最终插入失败也不会重掷。

### stack limit 与 max having count 不能混用

`[stack limit]@08E191CC`：ImportStackableScript@089AEEC8的089B3A01取整数、089B3A09写STStackableScript+0x364；CStackableItem::set_item@0850D81E的0850DE48复制到item+0x28C；getStackableLimit@0822C9FC的0822CA02读取它。STStackableScript::Clear@0899087A的08990A44将缺省设为**9,999,999 (0x98967F)**。明写0就保留0，明写负数就保留负数，没有现planner的`<=0即int.MaxValue`回退。

`[max having count]@08E162D9`是独立字段：Import089B27A9/089B27B1→script+0x60→set_item0850DE32→CItem+0x4C。试算调用树未读取+0x4C或计算背包同id总数，因此**普通骰子预筛不做“最大拥有总数”过滤**。未查全最终insert或别的入口是否另用它，不能扩大为整个游戏均不用。

### 与当前 CharacterInventoryPlanner 的适配裁决输入

这部分是对生产源码的比较，**不作为官服证据**。当前源码读取位置：`src/DFLegacy.Server/CharacterInventoryPlanner.cs`、`CharacterAvatarInventoryPlanner.cs`、`CharacterCreatureInventoryPlanner.cs`、`ItemCatalog.cs`。

| 当前可用部分 | 能否直接用于native候选预筛 | 原因／建议 |
|---|---|---|
| ItemCatalog的类型分类、TypeTag、StackLimit、MaximumHavingCount解析值 | 分类和已显式设置的正StackLimit可复用 | 不能把MaximumHavingCount当StackLimit；缺省StackLimit需独立定义，现GetStackLimit给int.MaxValue而官服9,999,999。 |
| CharacterInventoryLayout快捷栏3..8 | 可复用 | 与native快捷栏绝对槽号吻合。 |
| CharacterInventoryLayout各ACT1分类区间 | 应复用ACT1布局而非生搬官服新版槽号 | 当前装备[9,41)、消耗[41,73)、材料[73,105)、任务[105,137)；native对应分类起点9/57/105/153且预留扩展位，再加职业副业栏201..248等。两个布局不同。 |
| CharacterInventoryPlanner.TryPlace整个结果 | 不可视为等价候选筛选 | 180–186行提前计算负重，native候选试算没有；199–250行允许跨现堆/空槽拆分incoming；native首个匹配超limit会直接失败，不拆堆。 |
| EnumerateCandidateSlots | 不能原样照用 | 当前一律对应栏→快捷栏；native叠加总是快捷栏→对应栏，type2空格也是快捷栏优先，其他空格对应栏优先。 |
| CanStack | 不能称逐字等价 | 当前额外要求State、Durability、SealState一致；native候选助手仅比较itemId。保留更严格匹配若必要须明示兼容差异。 |
| 当前“满堆跳过继续下一格” | 与native不同 | native找到首个同id后，combined>limit返回-10终止候选，即使后面有空格。 |
| 权重计算、计划复制、唯一实例ID、实际入包提交 | 可继续用于最终产品发奖保护 | 它们不应该被静默投射成官服骰子的候选规则。候选试算与最终事务可分别设计，最终失败native不会自动重掷。 |
| Avatar容量105及扫描空格 | 典型单个头像的容量检测可复用 | native扫描0..104，与当前CharacterAvatarInventoryLayout.Capacity105一致。当前planner可多件跨格，native预筛只寻找一个空格。 |
| Creature的分类范围助手 | 仅在ACT1既有适配逻辑下复用 | 当前宠物独立字典／装备槽映射是兼容实现；不能把native CInventory::tryInsert的主栏位绝对数直接套到独立字典。 |

最低改动的行为复刻应新增只读“native普通候选容量试算”，复用ACT1分类区间与物品数据，明确不调用重量拒绝、不拆堆，按native搜索与失败顺序返回可参与/失败。最终发奖仍用现有事务/唯一入账保护。这样不会为了一次骰子规则重写全游戏库存；其中元数据匹配、max having count及零/负stack limit的选择仍须记录差异。

### 可直接编码的候选预筛伪码

下列逻辑直接对应native主函数及四个助手，范围参数由ACT1布局提供；所有范围按**末槽包含**书写。普通主包的ACT1对应范围应为装备9..40、消耗41..72、材料73..104、任务105..136；快捷栏3..8。native新版范围见上，严禁将其起点57/105/153直接写入ACT1。

```text
FitsStackLimit(itemId, signedCount):
    definition = FindItem(itemId)
    if definition == null or !definition.IsStackable:
        return true
    limit = definition.explicitStackLimit ?? 9_999_999
    return signedCount >= 0 && signedCount <= limit

TryStack(range, item):
    if item.typeByte == 1:
        return -1
    for slot in range in ascending order:
        old = inventory[slot]
        if old.itemId != item.itemId:
            continue
        // 找到首个同id后必定立即返回，不看后续同id堆。
        if old.signedCount >= INT_MAX - item.signedCount:
            return -2
        combined = old.signedCount + item.signedCount
        if !FitsStackLimit(item.itemId, combined):
            return -10
        return slot
    return -4

TryEmpty(range, item):
    // 原生Inven_Item::isEquipableItemType@08150812为{1,5,6,8}。
    if item.typeByte not in {1,5,6,8} &&
       !FitsStackLimit(item.itemId, item.signedCount):
        return -10
    for slot in range in ascending order:
        if inventory[slot].itemId == 0:
            return slot
    return -2

TryInsertForNativeCandidate(item):
    if item.itemId == 0:
        return item.signedCount >= 0 ? 0 : -1
    if item.typeByte == 8:
        return first empty avatar slot in 0..104, else -1

    quick = 3..8
    main = corresponding category range for item.typeByte
    if item.typeByte != 1:
        r = TryStack(quick, item)
        if r >= 0: return r
        if r == -10: return -1
        r = TryStack(main, item)
        if r >= 0: return r
        if r == -10: return -1

    if item.typeByte == 2:
        r = TryEmpty(quick, item)
        if r >= 0: return r
        if r == -10: return -1
        r = TryEmpty(main, item)
        if r >= 0: return r
        if r == -10: return -1
    else:
        r = TryEmpty(main, item)
        if r >= 0: return r
        r = TryEmpty(quick, item)
        if r >= 0: return r
    return -1

Eligible(candidate, item):
    return valid member UID/role && candidate.IsAlive &&
           TryInsertForNativeCandidate(item) != -1
```

关键验收例：快捷栏首个同id堆数量90、limit100、incoming20、对应栏和快捷栏均有空格，native仍因combined110返回-10，候选失败。快捷栏没有同id、对应栏首个同id90时同样失败。两个同id堆分别90/1也不会改找第二堆。如果快捷栏首个同id可合并，立刻成功，不检查对应栏有没有其他更理想的堆。**一次只找一个堆，绝不拆incoming到多堆／多空位；不做负重或maxHavingCount筛选。** 数量边界以native signed32处理；产品正常正数掉落可直接执行该伪码，非正常负数/溢出输入另需保持明确拒绝策略。

## 6. 随机边界：不是只凭函数名猜测

普通实际调用为全局 `get_rand_int@086B1B87`，而不是另外两个同名namespace版本。函数：n<0返回0，n==0返回rand；n>0先取rand r，r>n则再取rand()%n，否则对r做三次32bit有符号LCG并拼成31bit w，仅当w>n才取w%n，否则直接返回w。

因此不能对所有n未经证明地宣称严格排除n。本轮对**所有普通用到的正参数**穷举else分支允许的全部r=0..n：n=1,2,3,4（开局成员数）以及11,29,59,69,99；生成w最小值都是1013509，严格大于n，故这些调用全部最终取模，确实是0..n-1。

证据：`core_asm.txt`全局函数；`random_bounds.py`精确模拟32bit溢出及有符号除法朝0截断；`random_bounds.txt`为本轮运行结果。结论只关于值域；未声称原生PRNG完全均匀或与当前随机源有相同序列。

## 7. 幸运物主来源、扣点及稀疏map怪癖

原反编译v131（新反编译v124）不是请求里的物主字段：它是完整map_item的末尾 `CUser*`，偏移0x50。map_item构造@081512CC在081512E3将其清0；get_field_item@0830D9C0按21个dword复制84byte整个结构，带入该字段。

生成链：[静态]

```text
CBattle_Field::_MakeDropItems@0830ADF6
  → CParty::GetLuckPoint@085B61BE(...,true)：有效且896==1成员，按槽顺序AddUser
  → 怪物生成器把物品顺序放入stGenerateResult的vector<Inven_Item>
  → CLuckPoint::UseLuckPoint(this,itemType,rarity,grade,itemOrdinal)@08550B14
      取_GetMaxLuckPointUserIndex@08550954
      静态UseLuckPoint(user,itemType,rarity,grade)@08550A84
      成功后 owners[itemOrdinal]=maxLuckUserIndex
  → 对生成vector依次i=0..count-1：GetUserGainItem(i)@08550E50
  → 0830B781取得user，0830B786写map_item+0x50
```

已核的调用：普通怪物 `_generateRandomItem@08535ED2` 在0853683B；特殊怪物@08536882在08536D5D；Hell固定掉落@08535726在0853591B。调用四个业务参数依次来自Inven_Item+1、CItem::get_rarity、CItem::get_grade、当前生成物品vector.size。

静态UseLuckPoint只在user非空、itemType==1、rarity==2时扣点并返回true；扣点率从ServerParameterScript::getLuckPointDeductionRate(level,abs(grade-level))取得。**IDA误把静态函数第一个user参数标成CLuckPoint::this，并把数字1标成CUser*，应以mangled符号和汇编栈参数为准。** `_GetMaxLuckPointUserIndex`最大值初始0、索引初始0，严格大于才替换；同点优先vector前序。全为0也选索引0，没有“幸运点必须>0才能绑定”的检查。

`GetUserGainItem(i)`还有必须如实记录的原生实现细节：

```text
if i<0 or i>=owners.size(): return null
userIndex = owners[i]   // std::map::operator[]，缺key会插入0
if userIndex<0 or userIndex>=users.size(): return null
return users[userIndex]
```

所以“只有rarity2物品才可能带物主”并不完全成立。**[静态推导]** 若生成map初始仅 `{2:B的索引1}`，物品0满足0<map.size1，owners[0]插0并绑定A；物品1此时满足1<size2，继续插0并绑定A；物品2绑定B；物品3返回null。若map完全空则所有物品均无物主。`owner_map_model.py/txt`有该C++语义的可重演模型，但不是运行原生服务器的动态证明。

这类怪癖应在兼容裁决单列：可以实现明确的幸运物主语义，但若不照搬稀疏map插入副作用，就不能称“所有隐藏细节逐字一致”。本轮未修改产品代码。

## 8. 已闭合、剩余未知与建议验收

本任务指定的活人/尸体、死亡/复活/换房链、候选背包预筛、武器+30、职业编号、usable job加载缺省、物主来源和压分、轮转总人数/候选序号、无候选、随机边界均已闭合。

尚未由本任务证明：样本EB16来自何种发行/补丁来源；原始官方PRNG运行时种子/序列；当前ACT1 PVF实际生成稀有幸运物主的频次；多客户端真实画面；任意第三方函数是否还能通过间接指针修改map_item物主。此报告对map_item构造及所有直接构造调用者的本地写入扫描发现的非零物主赋值链为_MakeDropItems；不将这一有界扫描扩大为所有外部插件/动态修改均不存在。

无需启动服务也可用定向回放验收产品差异：当前房尸体应0分但可分金币；活着却背包不容纳则0分；非武器职业匹配不加30；武器all给所有基础职业30；缺usable job不给30；总4人候选2人且轮转目标2时无+40且last不变；最大并列先槽获奖、后槽减1；物主背包满后其他人仍受89..99阈值压制；无人候选报22且地面保留；最终入包失败报4且不重掷；高rarity请求bool按原覆盖次序处理来源byte。

主要可重演证据文件：`probe.py`、`get_item_asm.txt`、`party_flags.txt`、`life_asm.txt`、`core_asm.txt`、`inventory_helpers_asm.txt`、`job_and_ranges_asm.txt`、`import_equ_asm.txt`、`set_item_asm.txt`、`type_mapping_asm.txt`、`luck_asm.txt`、`owner_offsets.txt`、`generation_and_slot_insert_asm.txt`、`rotation_index.txt`、`random_bounds.py/txt`、`owner_map_model.py/txt`、`item_clear_asm.txt`、`equipment_clear_asm.txt`、`stack_loading_asm.txt`、`stack_clear_asm.txt`、`stack_limit_getter_asm.txt`。
