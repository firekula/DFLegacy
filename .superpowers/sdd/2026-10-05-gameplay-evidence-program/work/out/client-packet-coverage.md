# 客户端面密码/封装协议覆盖报告

范围裁决见 [client-crypto-scope.md](<client-crypto-scope.md>)：优先客户端可见加密；纯服务端模块间加密暂缓。

## [回放] NOTI561 解密与内层客户端包

三条VM S流共57个opcode561 wrapper：

- 类型17：54帧；全部使用wrapper头中的seed/choice/crc，目标SO Cipher解密成功，CRC32闭合，再加密逐字节恢复；
- 解密明文中随机元数据长度为4..15，随后可读取内层游戏帧；所有54个类型17 inner frame都满足 `u32@+3 == frame length`；内层正文CRC与解压（opcode 2/9/87）已通过；category/opcode/inner与解压正文SHA256及54条分母见 [noti561-inner-coverage.tsv](<noti561-inner-coverage.tsv>)；
- 类型19：3帧通过Cipher/CRC/再加密，并由原SO DPInterData::unpack完整消费7B，解析为长度7、kind5、flag0、DPBinCount u16=5。结构已确定，BinCount后续业务作用仍未验证。

证据：[decrypt-noti561-fixed.txt](<decrypt-noti561-fixed.txt>)、[verify-noti561-envelope.txt](<verify-noti561-envelope.txt>)。

## 已闭合的客户端面密码层

- 游戏面普通CMD/NOTI：目标df_game_r 14算法选择、key切片、CRC/头、已有多流回放；
- LOGIN：Rijndael CMD1、base64、OpenSSL/Python RSA、mouse CTEA/XTEA、LOGIN尾游标；
- Antibot客户端可见下行：官方容器SO `libantisvrimport.so` 的19路Cipher、GenKey、NOTI561外层和57帧回放；
- 类型17内层：已确认回到正常游戏头格式，具体业务opcode待映射；
- 类型19：密码/CRC闭合，但语义仍未验证。

## 仍未确定的协议

1. 类型19解密后7B结构具体字段；
2. 类型17内层游戏 opcode 对应的业务字段（已知头/长度，未完成全部业务语义）；
3. `DataManager+0x3c0`、version、server group、尾12B零区的业务命名/生产者；
4. restriction policy实际DB行值和各虚表策略业务含义；
5. `channel↔bridge`内部协议；
6. dbmw packet/query字段级协议及登录握手；
7. 其他辅助服务行为协议；
8. 当前运行实例G_Cipher singleton逐字节交叉；
9. LOGIN真实接受/拒绝状态机。

纯服务端模块间加密暂缓，不影响客户端面密码层清单，但模块间明文/字段契约仍不等于已完成。
