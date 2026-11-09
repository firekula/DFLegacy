# v0.1.8 赛丽亚材料商店修改来源

此目录保存生成已发布 PVF 的原始工具及材料清单，避免修改方法仅留在本机发布目录。
不包含游戏资源二进制；已发布的 `Script.pvf` 随 v0.1.8 客户端资源分发。

- 输入：已完成决斗频道、偷学、掉落及任务修复的 PVF，SHA256
  `c2321f05953ae4484dc6d0b10db50f83c3251c4967cdd0859376d4f8acdf27f4`。
- 输出：v0.1.8 正式资源，SHA256
  `69aa1a0338408fbcc3eff381924311ba42119a2cd2db4e8ffd2fab369bb9b34b`。
- 唯一改动条目：`itemshop/equipmentshop7.shp`。
- 保留原 98 个商店位置，追加 201 种具有正原价的材料；填充两个空位后共 301 格。
- 63 种零价或缺价材料不出售，记录于 `pending-prices.json`，不擅自设价。

`ExportSeriaMaterials.java` 使用 PVFStudio 的 `studio.pvf.core.PvfArchive` 和
`PvfWriter`。将提供这些类的 PVFStudio 构建产物加入 Java 编译及运行 classpath，
编译后以 `ExportSeriaMaterials <source.pvf> <new-output.pvf>` 运行。
输出必须是不存在的新文件；工具会检查输入摘要，拒绝覆盖原文件，并重新读取输出，
比较全部 48,327 个条目，确认只有商店条目改变。

`InspectMaterials.java` 以 `InspectMaterials <source.pvf> <existing-output-directory>`
运行，用于导出材料类型、价格及堆叠上限等核对信息。
`materials.json` 是本次材料盘点；`release-v0.1.8.json` 保存发布时的验证结果。
`inGameVerified: false` 只表示该导出步骤没有完成游戏内商店验收，不能据此宣称实机已验证。
