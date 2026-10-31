# 副本介绍超时残留修复验收

2026-10-29，U01已修复。原版会在选图超时回城后继续绘制副本介绍；现在A、B轮流担任队长，超时回城后介绍均正常消失。重新打开、鼠标移出、再次悬停、主动返回城镇，以及两端正常进入副本均通过。

## 原因与修复

ACT1从选图状态2返回城镇状态1时，原生界面切换漏掉了副本介绍窗口。原19C构建实机超时后，class3F仍登记1个窗口，缓存key为1，且没有加入删除队列；截图也仍显示介绍。

`6268903`只包装原生`42F830`的一处调用，先执行原界面切换，再对2→1补调原窗口关闭函数，继续使用客户端原有的延迟删除和缓存清理。已排队的窗口不会重复关闭。补丁核对原始PE身份及函数字节；独立审查复核了参数顺序、ret8及实际编译后的调用约定。

## 原图对照

以下均为客户端原生PrintScreen文件，未裁剪、重绘或合成。

修复前悬停、超时回城后仍残留：

![修复前悬停](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/01-red-hover.jpg)
![修复前超时残留](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/02-red-timeout.jpg)

A（mageco）作队长，修复后悬停、超时回城：

![A修复后悬停](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/03-A-hover.jpg)
![A超时已清理](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/04-A-timeout.jpg)

B（heal1024）作队长，同样通过：

![B修复后悬停](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/05-B-hover.jpg)
![B超时已清理](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-dungeon-tooltip/06-B-timeout.jpg)

## 数据与范围

| 检查 | 结果 |
|---|---|
| A、B分别作队长，悬停后等待30秒超时 | 回城后窗口数0、key=-1、删除队列0 |
| 两端分别重新打开、鼠标移出、重悬停、主动返回 | 介绍正常生成／消失，最终窗口数0、key=-1 |
| 正常从选图进入普通洛兰 | 两端scene3、队伍UID1/2，介绍窗口及删除队列均清空 |
| 测试 | 先RED9项失败，完成后tooltip及ijl15完整回归0失败；正确测试工作目录下再次运行通过 |
| 交付 | 四处ijl15.dll均为实测EAA7781B…；debug.dll保持已验收B31E9342…；服务端未改 |
| 收尾 | 隔离进程已停止，两端正常配置和8个输入文件恢复；原EXE、PVF、原存档校验通过 |

验收使用上组自然存档的逐字节继承副本，没有修改原存档。B作为被动队员时本轮未创建悬停介绍，因此改由B担任队长覆盖，不宣称复现了被动队员独有路径。正常进副本是入口回归，不代替前组已完成的完整普通洛兰战斗、结算和回城。

短按键尚未走到入口、界面刚打开就检查，以及一次测试工作目录错误的尝试都保留在私有证据中。最终以原生目的地到达和窗口状态确认就绪；没有把输入完成回执当成界面成功。

6张图片的原字节、[验收摘要](assets/2026-10-29-dungeon-tooltip/summary.json)和[SHA256清单](assets/2026-10-29-dungeon-tooltip/manifest.json)已保存。完整输入收据、只读采样、原始失败尝试及构建日志位于独立验收目录`G:/Game/DNF_ACT1/DFLegacy/.tmp/dungeon-tooltip-20261029`。
