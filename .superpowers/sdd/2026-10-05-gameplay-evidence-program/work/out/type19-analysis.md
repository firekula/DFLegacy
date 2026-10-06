# NOTI561 类型19：离线解密结果与语义边界

## [回放]

使用与类型17相同的外层 `GenKey(choice,seed)`、SO `Cipher`、CRC32和逐字节再加密闭环，三条 VM 流各1个类型19帧均能通过外层解密与再加密。

类型19解密后内层候选结构为 7 B；当前样本代表：

```text
00 07 00 05 00 00 05
```

三条流的类型19明文由脚本输出保留，不把7B中的字段按名称猜测。

## [静态]

`DPInterDataBody::unpackEx @0x1616a0` 的union discriminator分支包括 0x1、0x2、0x3、0x4、0x5、0x6、0x9..0xf、0x11、0x14；不同值转入不同结构 unpack。类型19的封装头值不是直接等于body discriminator，必须区分外层type与内层body。

`DPBinCount::pack/unpack` (`0x158400/0x159f50`) 只读写一个BE u16，不能把全部7B候选直接命名为BinCount。该7B已通过目标SO `DPInterData::unpack @0x1619e0` 完整消费验证：BE u16长度7、BE u16 discriminator=5、flag=0、BE u16 DPBinCount值5。3/3样本通过，输出结构字节与Python解码逐字节一致。故不是“整个7B直接为BinCount”，而是5B InterData头+2B BinCount。

## [未验证]

- DPBinCount=5的后续业务含义（具体指哪些bin、数量如何影响客户端状态），不以类名代替消费者证明；
- 其余4B尾部是否属于外层/内层长度、校验或随机字段。

类型19已经完成密码层回放闭环，但尚未完成协议语义闭环。
