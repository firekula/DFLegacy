# 离线取证继续推进（本轮）

## [静态] Mouse CTEA key

`GlobalData::Init` 的 CTEA 初始化参数来自 `0x08c179e7`，16 B静态ASCII为 `tjsghTlWkdajtwla`。它是mouse password CTEA材料，不是334B游戏面Cipher key；见 [ctea-key-static.md](<ctea-key-static.md>)。

## [静态] LOGIN restriction policy

`AuthMobileRestrictPolicy::C2 @0x0816d6ce` 构造category=1 policy，vector中索引0..33全部初始化为short 1。`isApply(int)` 仅允许category index 0..33并返回对应short是否为1；`getRestrictValue`将policy自身的code和vector值写入传入的 `SigLoadRestrictPolicy`，这是导出，不是导入；其循环从索引1开始。`ServiceRestrictManager::isRestricted(CUser,cat,value)` 先取GarenaAuthData.mobileAuth，true时直接返回0；否则查policy map，缺项返回1，存在项调用虚方法。

`loadPolicy @0x0816e7d6` 创建内部stream(type=1,id=-1)，初始化SigLoadRestrictPolicy，入队QUEUE_IDX=2。这是发起加载请求，函数自身没有把策略数据导入policy。持久化来源和应答处理仍需追消费者，不能据函数名断言来自文件或DB。

来源：[login-restriction-static.txt](<login-restriction-static.txt>)、[restrict-policy-static.txt](<restrict-policy-static.txt>)、[restrict-policy-predicates.txt](<restrict-policy-predicates.txt>)、[mobile-policy-binding.txt](<mobile-policy-binding.txt>)。

## [回放] 目标ELF指令级一致性

`emulate-cipher-boundaries.py` 在无网络、无进程情况下，以固定ELF执行：SuperFastHash 259长度、NOTI包装全部字节、三条PI流、mouse CTEA 8B解密；并用独立Python XTEA形式完成256个固定块逐块对照和加解密往返，`checkValiedPassword`跑7个边界样本。输出：[emulate-cipher-boundaries.txt](<emulate-cipher-boundaries.txt>)。

## 尚可离线但未完成

1. `ServicePolicy::getCode`/`SigLoadRestrictPolicy::set`完整字段布局与实际策略输入；
2. 0x08c179e7 CTEA静态key的来源文件/配置语义；
3. LOGIN success分支中 `DataManager+0x3c0`、version、server-group 与 `SIG_LOGIN_DATA` 字段名映射；
4. 其余13种算法的独立实现回放/已知答案；
5. singleton具体值交叉仍需动态实例或完全确定性初始化重放。

本轮没有启动客户端、连接VM、发送帧或附加进程。
