# 操作工具截图默认行为：独立离线审查

结论：所审源码及v4/v5记录一致，未发现阻断此次工具修改的P1/P2。此次只读源码、既有JSON/测试日志和原图哈希；未运行测试、启动或查询游戏、发输入、部署、修改产品、读取存档或netdump。此审核不是独立实机认证，也不证明全部卡顿已解决。

源码实际为 `tools/acceptance/native_control.py` 与 `tools/acceptance/tests/test_native_control.py`。普通调用默认screenshot=false，构造命令时不附加 `--screenshot`；显式选项才交给原builtin driver追加PrintScreen，`--no-shot`保持兼容且与显式截图互斥。用户在steps中明确写入vk44仍属于显式动作，不被此默认策略过滤。

守卫核对：运行目录须存在；角色/标签/phase白名单、running状态及唯一角色匹配先行；PID起始FILETIME保留第七位小数，路径比较及再次start查询防止错PID；驱动前后均verify_target。已有同标签证据拒绝，独占创建reservation防止同标签并发覆盖，driver日志用exclusive-create。操作后deployment原字节不得变化；驱动失败或无own_plan_armed不返回成功。依赖仍为run内既有builtin_driver，未引入其他输入通道；此工具不改DLL。

交付字节已核对controller-adoption：controller SHA256 `EF5F0966BCA95E5932706E0E63EB12E5898AA8F9AC3584C218526C18205F6478`；test `2981C0738B6B703489ECB1C9A2D74E4E8C638B509153659042A5C5396DABEF4F`；builtin_driver `BFEC410E346404E1C0B540FEA78A6CC8F012D14440B564BDE3A17881D070AFC3`。已读日志中RED为3failed/5passed，正确导入环境下GREEN为176passed/173subtests；另一次collection缺少acceptance模块的环境错误单独保留，不混为成功或产品失败。审查者未重跑这些测试。

run wrapper字节也匹配adoption；v4/v5的A端PID77704、路径和起始UTC与deployment记录一致。本次读取时deployment已标为stopped；没有据此查询当前进程或尝试验证输入。

v4连续轮询重算采用双字段B5DD20/B5DD34，只有相邻read间隔≤10ms、单次read≤5ms且pair相同才延续常量段；观察器空档不拼接。12694个poll、errors为空，三个阶段前台比例均为1。重算与 `screenshot-v4-poll-analysis.json` 相同：

| 阶段 | 时长 | 最大合格常量段 | >50ms段 |
|---|---:|---:|---:|
| idle |12s|7.5980ms|0|
| screenshots |20s|70.2358ms|6|
| post-shot-idle |12s|7.1577ms|0|

六个长段分别为59.3012、56.6915、57.1120、64.8042、66.3022、70.2358ms。输入计划明确有六次vk44且成功完成；六张原生JPG的源/复制/记录SHA均一致，记录文件完成时间分别在长段末端后约0.55–2.65ms。它支持本轮PrintScreen与约57–70ms原生时钟停顿的重复关联。不是GPU Present或渲染帧耗时，不能据此认定其他卡顿均同源。

v5使用新canonical controller，源码调用未传 `--no-shot` 或 `--screenshot`。完整40秒、11508个poll、errors为空；一次32方向键连续计划及16次短计划共48个key，无vk44，17条计划均有success和own_plan_armed。image_before/after各1129项，文件名、尺寸、mtime逐项相同，支持本轮没有新增或改写原生截图。重算最大连续常量段为continuous7.4215ms、short7.5577ms，均无>15ms合格段。short阶段62.2205ms观察器未调度空档单列，不能当作已证明的游戏停顿；不满足观测条件的时段也不构成“绝无卡顿”的证明。

独立显式 `--screenshot` 的完成记录仍有success/own_plan_armed，原图与证据复制图SHA均为 `2861FFB66BA14585DFE2C310EEB3B575E711442DD6BCE1EF39EB8C485F43DDBC`，符合保留关键验收截图的目标。以上计划完成仅证明输入处理；未把输入回执推广为其他游戏业务成功。

限制与更正：v1触及14000读取上限而中止，不能当完整无停顿基线；v2/v3只看delta1而漏掉reset，不能作完整无卡顿证明。v4 raw的scope残留“No screenshot in measured phases”与该文件的screenshots phase/events矛盾，应保留原字节、用更正说明澄清并以显式阶段、步骤和文件记录为准。v5的同句话与其实际无截图计划一致。短样本只覆盖当前A端/当前操作模式，不外推所有场景或长期性能。

主要证据哈希：v4 raw `7AFCBADA1D9B20384B7F2AAD014C3F84F5EE6E3CEA7B7F73975E5C1C97DF8388`，v4 analysis `E82CDA378948A9180A495B199DD651FE26C155BF8A2E5C3277BCB7AFD0FFAAE3`；v5 raw `920F9C08E446EAAC821109D2510A2020A779849A1B67F7F5160E9F5FA94237AB`，v5 analysis `DB05510582C3291841FD7285A8DAB9226287A89075451139652ADCE0F6846DD0`。
