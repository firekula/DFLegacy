# 离线覆盖最终边界（本轮）

## 新增 [抓包]+[静态]

`verify-noti561.py` 对三条 VM S流真实统计出每条19个 opcode=561 帧（总57帧），长度/offset/body SHA256均落盘。容器30011样本没有561，不能混用。`Inter_Antibot_SendBufferClient @0x084e43bc` 复制payload、选择内部0x230/0x231、put_binary后发送；其输入生产者仍未找到。早期“容器432B序列就是NOTI561”已[作废]。

`CreateAntiServerObj @0x082a6539` 静态拼接并dlopen `libantisvrimport.so`，dlsym `GetAntiBotInterfaceAll`。该库不在只读工作区/目标ELF中，因此NOTI561 payload算法不能从当前离线材料闭合；这不是本轮扩大反外挂本体范围的理由。

## 新增 [静态]

Restriction policy：SQL实际为：
`seLect category, restrict_code, restrict_value from dnf_restrict_state where server_group=%d`。
`setPolicyApplyValue`将字符串逐字符`'0'/'1'`转成ServicePolicy vector short；非法字符记0并记录日志。`AuthMobileRestrictPolicy::beginPolicy`调用该解析器，`isApply(index)`检查0..33。`loadPolicy`从DB结果逐行取category/code/char后set，再按已有policy或EOF发送策略。实际行值未在本轮读取。

## [回放]

14算法目标ELF roundtrip及三VM C/S流已通过。最新输出：[all-cipher-algorithm-roundtrip.txt](<all-cipher-algorithm-roundtrip.txt>)。C流覆盖11算法、S普通包覆盖14算法；NOTI1专用、NOTI561/空正文不计普通算法正文解密。该回放是同一目标ELF自洽，不是独立密码已知答案。

## 离线到头的项目

在当前工作区材料范围内，以下已达到“继续无需新证据会重复同一假设”的离线边界：

1. [作废：材料更新] 原结论“NOTI561缺少libantisvrimport.so”已由用户提供的外部目录解除。已做身份及候选ABI核对，见[SO接入报告](<antibot-so-intake.md>)；现在可继续离线分析，尚未核对与VM加载版本的同一性。
2. restriction实际值：必须读取DB策略行或运行stream应答；静态只能恢复查询/解析/默认逻辑。
3. G_Cipher singleton具体值：需要同一运行实例内存或完全确定性初始化重放；历史pool[0]不能代替。
4. LOGIN最终接受/拒绝：需要服务端状态机/真实会话；离线只能确定输入消费与密码运算。

## 仍可做但价值有限的离线项

- SIG_LOGIN_DATA尾部12B零区的生产者来源；
- `DataManager+0x3c0`/version常量业务命名；
- 其它非登录opcode的字段语义；这些需要对应已解密样本或独立答案，当前材料没有足够交叉来源。

本轮没有真机、没有动态附加、没有连接VM、没有发送帧。
