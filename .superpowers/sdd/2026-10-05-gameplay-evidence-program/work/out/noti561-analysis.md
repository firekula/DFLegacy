# NOTI561 / Antibot_SendBufferClient 离线分析

## [抓包]

三条 VM 游戏面 S→C 流各发现 19 个 `category=0, opcode=561` 帧（总计57帧）：

- streams-live：帧长 521、71、92、286、930、2854、1753、109、183、238、227、401、505、439、91、242、80、421、519 等；
- streams-live2：对应帧长不同但帧序位置相同；
- streams-vm：同样19个位置。

逐帧 payload SHA256 与流哈希见 [verify-noti561-current.txt](<verify-noti561-current.txt>)。容器 `streams/30011-46386.bin` 没有 opcode561，不能与 VM 的 NOTI561 直接混为同一部署事实。

## [静态]

`Inter_Antibot_SendBufferClient::dispatch_sig @0x084e43bc`：

- 输入 `ParamBase` 偏移+8取 uint16 payload length；+12为 payload pointer；
- payload复制到本地 buffer；
- 根据对象状态及 packet category 选择内部 header opcode `0x230` 或 `0x231`；
- `put_header(0, opcode)`；
- `put_binary(payload, length)`；
- `finalize` 后经 `CUser::Send`。

这说明 opcode561帧在此处是 antibot send-buffer client 专用分支；不能按普通14算法直接推断正文。`CUser::AntibotSend @0x0864809c` 也有独立的 antibot key/状态分支。

`PacketBuf::isEncryptRequired @0x0858da28` 仅返回内部 byte `this+0x17330`；`encFinalize` 依据内部分类及长度/CRC写头。NOTI561是否已经在前级完成其它变换，需继续追 `Inter_Antibot_SendBufferClient` 的输入生产者，不可用普通Cipher回放替代。

## [未验证]

- 561 payload 的明文结构、随机/状态字段及 `0x230/0x231` 选择条件；
- 反外挂客户端/游戏服边界协议；本项目只允许记录客户端兼容所需边界，不扩展到 gunnersvr/secagent/zergsvr 本体；
- 容器 opcode561缺失不代表部署不存在，只代表当前样本未捕获。

[作废] 早期报告把“NOTI561已固定为432B NOTI1/31B序列”当作该 opcode 的结论；该序列其实是容器失败路径的普通帧序列，且 VM样本真实包含19个561帧。两者不可混用。
