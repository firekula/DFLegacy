# C→S 入站 PI/校验离线复核

## [静态]

`CUser::Parsing @0x08647890`：

- 收包复制13B头到本地 `header`；
- 从 `header+1` 读取 uint16 opcode；
- 若普通路径，调用 `NS_PI_2ND_RecvHash_CS`，它使用 `buffer+0x0d`、`length-0x0d` 交给 `PIReceiverManager::CheckHashReceived`；
- 调 `G_Cipher()->Decrypt(opcode, buffer+0x0d, length-0x0d, ...)`；
- 随后另以 `buffer+0x0b`、`length-0x0b` 调 `Encryption::crc32NE`，再用 `MakeChecksumTo1Byte2` 把两个值降到低字节比较。

`CPacketVerifier::UpdatePacket @0x08696e58` 只在 opcode/category/头校验相符时更新状态；连续序号读取 accessor 另在 `GetSequence @0x08696e48`。当前静态函数未显示“必须序号+1”的比较，不能把抓包序号递增推广为服务端拒绝规则。

`CheckHashReceived @0x0808bf7e`：对非空正文调用 `MakeHash(data,length,seed_byte)`，将收到的16位字段解交错为 hash 与下一随机字节，再比较 hash；空/特殊分支不走相同计算。`RecvHash_CS @0x08274f73` 还要求 `(category,opcode)` 在用户的 `CSHashSet` 中，否则进入 hack 计数/集合插入路径。该 PI 样本与反外挂进程本体不同，只记录客户端兼容边界。

来源：[inbound-verifier-static.txt](<inbound-verifier-static.txt>)、[static-cipher-details.txt](<static-cipher-details.txt#L1016-L1259>)。

## [回放]

`verify-inbound-pi.py` 对已有三份 C→S 21帧流：

- 21帧均可按13B头切分；
- 尾部 sequence 字段为0..20递增；
- 直接假设“header[7:11] 低字节=SuperFastHash(解密正文)与header[9:11]交错字段”会在三份样本全部失败，证明该简化假设不成立；
- 当前脚本保留失败输出，不将序号递增误写为校验规则。

CMD1 单帧另有独立成功的 `verify-static-password.py`：候选key[8:24]解密、token/RSA和密文往返均通过；这不能替代全部 C→S PI 验证。

## 当前边界

- [已证实] C→S 收包分13B头、opcode为uint16、Cipher输入从+13开始；
- [已证实] PI检查使用独立的 `MakeHash`/随机seed/CSHashSet，不能由普通CRC或公开抓包字段简单替代；
- [未验证] `MakeHash` 对每个实际 C→S 帧的完整输入（是解密前/后、哪个头字段、随机seed来源）和 `MakeChecksumTo1Byte2` 的两个实参对应；
- [未验证] 服务端是否把 sequence 递增作为拒绝条件；
- [未验证] 非登录 opcode 的完整 C→S PI解码。

结论：入站 PI 仍是离线可继续的静态/回放任务，不是本轮真机阻塞；但当前失败简化模型必须保留，不能用它构造任意账号帧。
