# C→S 入站 PI / CRC：更正后的离线结果

## [静态] 精确边界

来源：[inbound-verifier-static.txt](<inbound-verifier-static.txt>)、[inbound-pi-receiver-static.txt](<inbound-pi-receiver-static.txt>)、[static-cipher-details.txt](<static-cipher-details.txt#L1197-L1259>)。

- `Parsing @0x08647890` 在0x08647bb9调用RecvHash，之后0x08647c25才解密。因此PI覆盖**密文** frame[13:]。
- `RecvHash_CS` 先读u32@7并右移8；`CheckHashReceived`再次右移8，所以实际交错hash/next-seed为frame[9:11]。frame[8]为另一个随机字节。
- CMD1使receiver old-seed置0x27；其余包使用前包解出的next-seed。非空密文：MakeHash=SuperFastHash四字节XOR再XOR old-seed；接收器先保存new-seed再比较。空正文直接返回true。
- `CSHashSet`记录已计数类别：只有hash不符且集合中尚无(category,opcode)才增加计数并插入。**不是必须已在集合才能通过**。
- 解密后CRC覆盖frame[11:13]（两字节sequence）+解密正文，比较`crc32NE(...) &255`与头frame[7]；0x0808cc4b将两个实参都原地降为低8位。
- `UpdatePacket`先过时间检查，再对特定opcode做频率限制，并统计连续相同category/opcode/u32@7。三字段不同时更新缓存，相同时增加重复计数。该函数没有读取头[11:13]；不能声称它检验sequence+1。

## [回放] 当前结果

命令：`python .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/verify-inbound-pi.py`。
输出：[verify-inbound-pi.txt](<verify-inbound-pi.txt>)。

三条VM C流各21帧，合计63帧：PI密文链全部通过；观察sequence=0..20（不等于服务端强制规则）。三条CMD1均通过`sequence+400B明文`的低字节CRC。改变sequence首位或明文末字节首位的两个负例均不匹配原CRC。

## [作废] 上轮报告/答复纠错

旧报告声称“当前脚本保留失败输出，完整入站PI尚不知覆盖明密文”。这与当时最终脚本和PASS产物矛盾：失败的是临时版本（选错[7:11]位域、未正确折叠32位hash、未沿前包seed递推、未截断32位运算）；最后脚本已经修正并通过。旧报告把临时失败误当最终结果，作废，以以上源码实参和复跑输出为准。

旧报告的“CSHashSet必须包含才能通过”“只有三字段相符才更新状态”也作废，正确分支见上。

## 未完成边界

PI密文验证不要求解其它算法；本轮没有声称其它62帧明文或所有算法完成。尚需其它算法的离线答案、完整时间/重复阈值回放、鼠标密码及LOGIN下游行为。无动态附加或登录重放。
