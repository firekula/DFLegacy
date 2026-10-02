# Store 写事务边界决策

普通写入口通过 SaveLockedAsync 保存正式 JSON；奖励提交与 operator discard 通过 _rewardWriter 特权快照路径写正式 JSON，二者是明确的两条例外。核查方式：grep JsonGameStore 的 File.Move/File.Create/WriteAllBytes 与 SaveSnapshot 调用。未来新增绕过路径会使证明失效。

quarantine 漏斗守卫阻止普通写盘；高风险背包/交易/邮件/密码等入口另有改内存前守卫。其他写入口仅靠漏斗，可能在异常前产生临时内存变化；只读查询不保存。

Initialize 隔离时允许内存迁移但跳过保存并告警；Recover 成功安装正式根状态不重跑迁移，恢复后需重启或重新执行迁移。

Operator 出口 ForceClearRewardQuarantineAsync 要求非空 reason：匹配正式 Committed 时恢复，不发奖；无匹配时 Unknown 转 Rejected/operator-discarded 并显式落盘解锁。审计包含 reason/keys/count/decision。`.tmp` 不能作为成功证据。

风险：进程内隔离不是掉电 durability，更不构成 durable exactly-once；恢复路径仍需端到端故障和操作员审计验证。此决策未接入翻牌与协议。
