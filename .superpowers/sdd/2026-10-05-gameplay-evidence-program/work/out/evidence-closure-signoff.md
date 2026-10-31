# DFLegacy Evidence Closure Signoff

## 判定：CLOSED_WITH_LIMITS

本证据收尾表已执行完。它签收的是当前客户端密码/封装证据包的范围冻结，不是全官服协议完成、不是 singleton 验证完成、不是产品重写授权。

## T1–T6 提交记录

|任务|产物|提交|
|---|---|---|
|T1|`work/out/offline-closure-report.md`、`RESUME.md`|`c4a74467e7592fb0595c6cbc028c46e56f6f5cae`；状态修正 `8b323ba60bc71e7f9ec6563a525d4992568e0159`|
|T2|`work/out/noti561-closure.md`|`f06a8ceac3371c2cde4289059cdca3ec984635ee`|
|T3|`work/out/login-offline-closure.md`|`fe85231037ba16145df2a7e034c867b6d6a40b92`|
|T4|`work/out/client-crypto-closure.md`|`71f684a40a735ca2adb9e7b46ec2dce4ee746be3`|
|T5|`work/out/client-business-backlog.md`|`71eda227dfc194c615cbf892740b9b29ddb709d9`|
|T6|本报告、RESUME 状态更新|本次提交|

## 硬门与证据边界

- H01：目标 ELF 与官方 SO 的大小/SHA256符合计划；解释器/依赖版本已记录。
- H02：5个 PLT、uint16 全域 `%14`、pool[0]边界已有原始输出；pool[0]不冒充 singleton。
- H03：11 文件对/8唯一内容对；5个未配对前缀显式 skip；RSA/OpenSSL/Cipher/CMD1 正负例已有输出。
- H04：3条 C 流共63帧；3条 S 流共270帧并按普通/NOTI1/NOTI561/空正文限制记录，未把扫描数写成全部解密数。
- H05：14算法目标 ELF 初始化/往返、RSA双实现、mouse CTEA/XTEA与边界回放已有输出；独立官服已知答案仍[未验证]。
- H06：57个 NOTI561 中54 type17、3 type19；type17 CRC/解压/再加密与 type19 原 SO unpack 已有输出。
- H07：内层按 `(category,opcode)` 为9组；category=1按 CMD 命名，`(1,2)=SET_UDP_IP_PORT`、`(1,4)=SELECT_CHARACTER`。
- H08：LOGIN消费388B、观测尾12B全零；尾部生产者身份/入口未取得，严格标为已降级，不命名 padding。

## 已钉证据

[回放] NOTI561 客户端可见封装、54/3 分母、CRC、再加密、内层长度与限制性命名；[回放] LOGIN 的密码/RSA、mouse、字段消费游标；[回放] 普通客户端 Cipher/PI 的冻结样本、负例与目标 ELF 往返；[静态] LOGIN 消费者地址与字段存储目标。

## 降级或暂缓

- [已降级] LOGIN 尾12B生产者规则、type19 BinCount=5业务作用、type17内层业务字段、独立官服算法已知答案。
- [动态阻塞] 当前运行实例 `G_Cipher` singleton 逐字节值、restriction 实际 DB 行值、LOGIN 真实服务端接受/拒绝状态机。
- [范围暂缓] bridge、dbmw字段级协议、辅助服务协议、服务器模块间加密、反外挂进程本体；它们不阻塞本客户端密码/封装收尾。
- [后续玩法单元] 当前样本实际出现的9个 inner `(category,opcode)` 组合；所有1171条 opcode 行为放入 M1–M10，不扩大本表分母。

## 范围检查

本轮只提交计划指定的证据报告/接续文件；未将 `src/`、`tests/`、`STATUS.md`、`server.json` 纳入本轮提交。未启动客户端、未连接 VM、未附加运行进程；未创建 teammates；未修改产品代码。

**签收语句：**

本证据收尾表已执行完，结论为CLOSED_WITH_LIMITS。
硬门H01–H08均有原始验证输出；LOGIN生产者等未解决问题按限定性结论保留，不算已解决。
这是当前客户端密码/封装证据包的范围冻结签收，不是全官服协议完成、singleton验证完成或重写授权。
后续业务单元、范围暂缓、动态验证已有固定清单，不再以“继续离线”扩展本表。
