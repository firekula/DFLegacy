# ACT1诊断命令适配验收

> 最终交付补记：本页的解析测试与双端正常登录回归已纳入[本轮交付索引](2026-10-28-overnight-delivery.md)。统一构建回归、正常启动及原档/配置核验由该索引绑定；下文的“待补”保留取证时点含义。CMD125仍没有自然触发记录。

本组修复ACT1错误上报被漏接和历史编号混用，不新增玩家玩法。源码提交 `294fdf3`。用户授权多组连续恢复；本记录当前已完成隔离测试和双端正常登录回归，最终统一部署/恢复记录将在本轮交付时补充。

## 官服与ACT1对照

完整官服 `Dispatcher_TraceError::dispatch_sig@08204B0C` 读取type；type2再读error并记录KILLBOSS2诊断，type3进入诊断分支，不回复、不改变业务状态。ACT1当前EXE的 `004AF42B/004AF4A6` 实际使用CMD125，发送type2/error。后期样本用118，不能直接沿用编号。

当前ACT1的CMD118已有独立历史样本和多个不同构包点；本组保留其旧code/flag解析和公开常量兼容，将日志准确标为历史客户端报告，不再以官服TRACE_ERROR解释完整语义。125与118都有界消费畸形包，不落入通用成功回复；非诊断命令继续原路。结构化日志只含命令号、类别/错误、长度及可选角色身份，不打印任意原始正文。客户端EXE、PVF与IJL15没有修改。

## 自动化

真实handler的确定性测试覆盖序列偏移、type2缺error、type3单字节、未知type、短包、未选角色、118旧样本、非命令/邻接编号排除及输入不变。24项预期失败后实现，定向为0 failures；完整.NET冒烟通过，Server和SmokeTests Release均0警告、0错误。

- [RED日志](assets/2026-10-28-client-diagnostics/red.log)
- [定向GREEN](assets/2026-10-28-client-diagnostics/green-direct.log)、[完整冒烟](assets/2026-10-28-client-diagnostics/full-smoke.log)
- [Server构建](assets/2026-10-28-client-diagnostics/server-release.log)、[Tests构建](assets/2026-10-28-client-diagnostics/smoke-release.log)

## 双原生客户端回归

隔离阶段 `diagnostics-294fdf3`，服务端PID48980，A PID75692、B PID52932。两端通过原启动器、原生频道和选角进入赛丽亚房间，截图均实际查看后原样复制：

![A在诊断修复构建上正常进城](assets/2026-10-28-client-diagnostics/007-diagnostics-A-town.jpg)

![B在同一构建上正常进城](assets/2026-10-28-client-diagnostics/008-diagnostics-B-town.jpg)

[专项检查](assets/2026-10-28-client-diagnostics/diagnostics-acceptance.json)31/31通过，核对冻结捕获的选角/进入世界包头、实测运行文件、原档保护和客户端文件哈希。两端本次均未自然发送125，也没有故意制造BOSS错误。因此图片证明正常操作未回归，125具体解析与日志由确定性测试覆盖；不把“未发生错误”写成“真实错误上报已触发”。包头解析不等于CRC/正文验证。

冻结前后的隔离状态与基线暂停状态相同；原生产存档SHA256仍为 `FB20572589A5CFB31D89E16EC6936DE96071630E432EDE787CCB0887930ABE52`。本阶段结束已停止记录中的测试进程，下一组使用独立发布阶段。原图、日志和检查JSON的来源与复制件哈希见[资产清单](assets/2026-10-28-client-diagnostics/sha256.json)。
