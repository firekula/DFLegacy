# 大地图右键寻路与黄色双扩散反馈

用户已经明确：大地图只由右键设置寻路目标，左键不触发；目标点复用玩家位置扩散效果，改黄色并连续出两次，随后继续待办。沿既有授权完成实现、测试、原图验收，不增加设计确认停点。

仅原生右键release16经过当前地图root命中和consume/capture/plane复核后提交目标；左键只执行原处理，不分发目标或反馈。有效目标受理后建立plane-local点击反馈，第二圈延后200ms，复用原1750ms五帧scale/RGB/alpha节奏，总1950ms，不无限loop。新目标替换，关闭地图、角色/owner/plane变化或时钟回退清理；同角色正常跨区域允许继续显示。

原资源是MiniMap_minimap_town.img index7的44×44 ARGB1555青色圈，普通乘色无法变黄。复用原像素形状与alpha，亮度映射R=G=max(original RGB5), B=0，生成编译期私有黄色像素。新建未登记到共享管理链的原生私有texture1555，从当前玩家动画读取58h绘制描述符模板，本地副本替换texture、重编码地址保护scale并使用原82C160绘制。存储/restore RS19/20，保持现有Sprite Begin/End；原玩家marker/ani/texture不写，不改EXE/PVF/NPK/原始存档。

新draw CALL hook8F6FB6仅在原导航两hook均已就绪后安装，原调用先且一次，self必须是当前N地图root。任何还原失败保留所有权阻止卸载；有绘制资源的Shutdown先请求下一游戏帧清理，Nav保持enabled直到成功。工作线程才导出轻量诊断，无主线程文件IO或等待锁。

验证：右键/左键回归RED→GREEN，原圈像素和五帧曲线及两次/替换/关闭测试；Native ABI、unsupported failclosed、full EAX、状态还原、纹理pitch与失败、hook所有权与延迟清理测试；严格Win32Release及各Native套件；A/B右键同/跨区域、左键不动作、两圈原图/数据、新目标和关闭保护。普通操作默认不截图，关键验收显式截图。旧冻结命名空间只读；地图验收后接续U07最小已证候选，怪物位置同步仍单列。
