# 游戏资源与 PVF 维护

用途：修改物品、任务、商店、地图、技能和客户端资源。业务代码读 [协议与数据](protocol-and-data.md)，产物分发读 [构建发布](build-test-release.md)。

需要修改编辑器本身的解析、脚本编译、PNG/帧数据或包写回逻辑时，读 [PVFStudio/NPKStudio 子项目协作](resource-subprojects.md)；两仓库均有独立接手入口、格式说明和编辑保存手册。

## 先确认三种身份

1. **章别**：ACT1 还是第二章，不能用名称相同认定内容相同。跨章资源适配的完整检查见 [章别适配](chapter-adaptation.md#4-资源适配)。
2. **角色**：客户端原生 PVF 还是服务端可读取的封装；第二章二者可有相同条目内容但不同文件头。
3. **基线**：输入完整 SHA256、条目数、格式版本和导出工具；不能拿一份不明来源的 `Script.pvf` 跳过工具的固定摘要检查。

第二章把服务端封装直接交给客户端曾导致 `std::bad_alloc`；[原始记录](../../docs/acceptance/2026-11-14-c2-native-pvf-release.md)明确说明原因与验证边界。仅把启动器允许摘要改成新值不能修复资源格式。

## 资源类别

| 类型 | 作用 | 修改后验证 |
| --- | --- | --- |
| `Script.pvf` 中脚本/列表 | 任务、技能、商店、物品、地图等定义；服务端由 Catalog 和 ScriptFileSystem 消费 | 解析、ID/路径、字段值、关联列表、真实功能 |
| 字符串表、外部文字 | 显示名称和说明；有共享键及语言映射 | 共享引用、编码、原文指纹、实际字形/换行 |
| NPK/IMG、ImagePacks | 客户端图像 | 索引范围、命名、选择性覆盖、游戏内贴图和点击范围 |
| 登录器像素资源 | WPF 原版风格图片/字体 | 构建嵌入、尺寸、透明、DPI、按钮状态 |
| 渲染 DLL/着色器 | 原生显示链 | 章别、位数、构建来源、许可和真实加载 |

原始客户端和多数游戏资源不随源码仓库提供。缺失时记下所需格式/摘要和获取方式，由用户提供合法持有的副本；不要拿仓库中的 PNG 或服务器 PVF 伪造完整客户端依赖。

## 现有工具与输入约束

| 工具 | 用途和依赖 |
| --- | --- |
| [ExportPvpChannels.java](../../tools/pvf/ExportPvpChannels.java) | ACT1 频道资源导出；读源码中的固定前置条件 |
| [FixBehemothForbiddenUnlock.java](../../tools/pvf/FixBehemothForbiddenUnlock.java) | 天帷禁地任务/入口修复；参数为输入、新输出和证据目录 |
| [seria-materials](../../tools/pvf/seria-materials/README.md) | ACT1 赛丽亚材料商店；依赖提供 `studio.pvf.core` 的 PVFStudio classpath；JSON 保存材料与缺价证据 |
| [chapter-two/port.py](../../tools/pvf/chapter-two/port.py) | 第二章语义迁移；Python 标准库；固定第二章输入及 ACT1 证据摘要；产生原生/服务端两份 PVF 和验证报告 |
| [quest-recovery.json](../../tools/pvf/chapter-two/quest-recovery.json) | 任务文字来源指纹和语言映射证据，不能任意替换缺失键 |
| [client_resources.py](../../tools/pvf/chapter-two/client_resources.py) | 第二章洛兰布局及选择性客户端资源处理，校验地图与图像依赖 |
| [restore_shops.py](../../tools/pvf/chapter-two/restore_shops.py) | 用两章输入对照恢复商店；参数 `c1 c2 output` |
| [restore_migrated_jars.py](../../tools/pvf/chapter-two/restore_migrated_jars.py) | 迁移后罐子数据处理；参数 `c1 c2 output` |
| [restore_cash_packages.py](../../tools/pvf/chapter-two/restore_cash_packages.py) | 点券礼包脚本处理；参数 `source output` |
| [restore_avatar_compound_shop.py](../../tools/pvf/chapter-two/restore_avatar_compound_shop.py) | 时装合成商店处理；参数 `source output` |
| [restore_pet_shop.py](../../tools/pvf/chapter-two/restore_pet_shop.py) | 宠物商店资源处理；参数 `--client --server --output` |
| [build_classic_launcher_assets.py](../../tools/build_classic_launcher_assets.py) | 登录器原版风格资源生成，先读脚本的输入/输出规则 |

这些脚本是具体修复工具，不构成一个任意顺序都能执行的通用补丁链。输入版本与已应用修改不匹配时应停下核对来源，不能削弱 `require` 或摘要检查来强行通过。Java 工具的外部依赖没有随源码完整提供，不要编造可直接运行的 classpath。

## 标准修改步骤

1. 保存输入文件身份和当前客户端/服务端使用位置；不覆盖原始文件。
2. 确认脚本真实依赖：需要原生 PVF、服务端 PVF、ACT1 证据还是 NPK 根目录。
3. 选择新的输出目录。运行前读参数解析和固定摘要/字段守卫。已有导出说明见 [第二章工具 README](../../tools/pvf/chapter-two/README.md)。
4. 对输出重新打开解析，比较条目 ID、路径、数量、CRC/密钥、修改白名单和全部未变条目；输出两种封装时验证解码内容一致。
5. 保存输入、输出、工具版本、实际差异和失败项。摘要不同本身不说明业务变化正确。
6. 在独立运行目录配对加载服务端与客户端资源，检查实际目标功能，至少覆盖打开、操作、取消、重登和关联任务/物品的情况。
7. 把经过验证的资源加入正确章别包及清单；服务端挂载也需要对应更新，镜像升级不自动等于 PVF 已替换。

## 可先运行的独立检查

```powershell
python -B tools/pvf/chapter-two/test_port.py
python -B tools/pvf/chapter-two/test_client_resources.py
```

测试里的合成样例通过，不代表完整外部资源通过校验。需要真实输入的导出和原生 UI 验收另记。检查商业功能时特别确认价格、容量、重复领取、邮件/仓库持久化和既有物品可用性。

## 避免常见误判

- 缺价材料不自动填一个猜测价格；现有 `pending-prices.json` 就是明确保留未证条目的例子。
- 新章同 ID 的任务可能换了语义；文字替换应同时检查任务脚本和原语言文本。
- 停止提供旧任务不等于删除旧进度、奖励定义和完成映射。
- 只测试导出后的条目数不能证明客户端正常解码或 UI 命中范围正确。
- 发布通知和第三方许可随包保留，来源记录见 [模块地图](repository-map.md)。
