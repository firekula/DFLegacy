# 官方容器 SO：NOTI561 静态推进

## [静态] 来源与接口

所有者确认 `libantisvrimport.so` 来自对应官方 Docker 容器。文件SHA256 `8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7`，ELF32/i386。

游戏服 `AntibotSend` 的虚表槽与SO `AntibotInterface` relocation对应：+0x28=`RecvBufferForEncrypt`、+0x2c=`RecvBufferForDecrypt`、+0x34=`IsCmdInDownList`。候选SO的运行时虚表不可直接读raw 0值，必须按 relocation还原。

## [静态] 下行加密调用链

```text
RecvBufferForEncrypt
→ DPInterface::recv_buffer_for_encrypt
→ DPMsgHandler::recv_buffer_for_encrypt
→ DPVMBaseEngine::encrypt
→ dynamic_encrypt 或 fix_encrypt
```

`DPVMBaseEngine::encrypt @0xbe820` 将输出指针前移4，并从输出容量引用扣4；不是缩短输入。读取全局对象+8的u16作为处理前缀上限；超过上限的输入尾部保存长度，成功后原样追加。末参数bool为0先试dynamic_encrypt；返回非0才回退fix_encrypt，bool非0则直接走fix_encrypt。成功后输出长度加4，用EncodeInt写到原输出地址。UserInfo+0xb4/+0xb8分别在固定/动态路径成功时递增，不能称为错误计数。来源：antibot-engine-deep-static.txt L12–151。

## [静态] fix_encrypt

`fix_encrypt @0xbd4c0` 首先要求输出容量至少为输入长度+0x11a（不是要求输入>=0x11a）：

- 第一处rand结果按指令取模19再加1，即候选选择号1..19（不是1..10）。用户存在时更新user+0xac为旧值+rand()/256+1；用户为空时用两次rand组合seed；
- `init_encrypt_data @0xbd130` 生成 `DPAlgorithmData`：若UserInfo+0x64!=1，则取UserInfo+0x1c为基础值，rand()%16写入object+8，随后rand两次组成每个4B algorithm key word，数量=(object+8 >>2)+1；object+4指向调用者seed/metadata。该方法使运行时rand进入算法数据，固定模式无法仅由抓包头唯一反推。
- `DPAlgorithmData::pack` 以两个4B大端字段加一个对象+8的1B字段写算法头，随后复制变长正文；`DPInterDataHead::pack @0x158550` 再写两个大端u16（object+0、object+2）和一个u8（object+4）。Body pack按 data type 分支到 DPClientCode/DPDynamicAlgorithmHead/DPBinFile/DPCmdList/DPBinCount 等具体序列化器。
- `Crc32` 对封装数据计算校验；
- 调 `Cipheri` 完成实际变换；
- 结果前置 `DPInterData::pack`，其长度是内部data长度+0x11；
- 失败时用 `DPInterData`/错误路径返回-1。

该固定路径调用SO自身的Cipher(int,...)，不是df_game_r的Cipher::Encrypt(opcode,...)。不能据此排除其内部使用AES；算法映射仍需读SO的Cipher实现。

## [静态] TDR/外层字段

`DPInterData::pack` 先写5B的DPInterDataHead，再以word@object+2作为**union discriminator**（不是body长度），将object+5传给对应body序列化器。来源：antibot-inter-head-body-static.txt 0x15bacc..0x15bb93。

`DPDynamicAlgorithmHead::pack` 以大端方式写4B整数到TDR buffer，并在空间不足时返回 `0xffffffff`。因此外层至少包含可识别的4B大端头字段和后续变长数据；完整字段语义需继续追各结构head/body pack。

## [抓包]+[回放]

三条VM S流各19个opcode561帧。新实验 [verify-noti561-envelope.txt](<verify-noti561-envelope.txt>) 对57帧全部验证：正文offset0为BE u32外层长度，offset4为BE u16内部长度，二者满足outer=inner+4；offset6为BE u16类型，offset8为flag。每流18个类型17、1个类型19，flag均0。outer之后剩余0或4B；尚不命名这4B。这里已能读取封装头，不能说模式/seed必然不可从线路恢复；具体body字段仍需追下游序列化器。

复跑：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/verify-noti561-envelope.py`。

[作废] 本稿初写“先扣输入4B”“错误计数”“rand选择1..10”“object+2是body长度”均为整理错误；上述精确指令分别证明输出容量扣4、成功路径计数、选择1..19及union discriminator。保留更正，不沿用这些旧说法。

## [回放] 固定路径实际解密

`decrypt-noti561-fixed.py` 在不加载原生SO、只在有界Unicorn执行SO代码和独立CRC验证的条件下，从NOTI561类型17头读取 `seed`（BE u32@body+9）、`choice`（BE u32@body+13）、`crc`（BE u32@body+17），执行 `GenKey(choice,seed)` 后调用SO `Cipher(choice, body_cipher, seed, flag=0)`。三条VM流的54个type17帧全部：`Cipher`成功、SO `Crc32(plaintext)==header crc`、再加密逐字节恢复原cipher。输出：[decrypt-noti561-fixed.txt](<decrypt-noti561-fixed.txt>)。

这一步把NOTI561 type17从“候选封装”提升为 `[回放]` 已解密；每帧choice/明文长度/SHA256已落盘。type19一帧/流走不同body serializer，尚未套固定路径。

## [未验证]

- 当前SO与VM运行时加载SO的运行实例状态/构建差异；文件来源已由所有者确认，哈希是容器资产哈希；
- `dynamic_encrypt` 和 `fix_encrypt` 中间对象的用户状态、rand序列与算法参数；
- 57个NOTI561 payload的具体解密明文及字段；
- 反外挂进程本体不在本次范围。

来源：
- [antibot-so-survey.txt](<antibot-so-survey.txt>)
- [antibot-downstream-resolved.txt](<antibot-downstream-resolved.txt>)
- [antibot-engine-deep-static.txt](<antibot-engine-deep-static.txt>)
- [antibot-wire-fields.txt](<antibot-wire-fields.txt>)
