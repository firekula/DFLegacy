# 第二章同步主线与继续测试（2026-11-10）

隔离分支 codex/chapter-two 从421a903同步本地主线/远端共同的209a1da。主线仅作读取和合并来源，不修改主线工作区或线上/日常客户端，不发布。

暂停的五个翻牌文件先按哈希备份，并保存到专用ref refs/codex/chapter-two-paused-free-card-20261110（stash42ead8b3）。合并期间已按该ref恢复为未暂存的独立适配工作，合并提交不把未验收实现冒称完成。

两处内容冲突：MainWindow.Updates保留C2 IsBundled保护，同时采用主线后台发现与非模态错误提示；SmokeTests.Program保留C2专项入口及主线禁地图检查。独立合并审查未发现兼容问题。多附件GM发件先拆成多个原生单附件邮件，整束一次提交；旧记录和C2单附件输出保持兼容。

完整服务端冒烟、更新核心717、Manager175、页面37及共享native51项通过。全Launcher首轮完成2317项，仅两个旧UpdateUiTests的PixelText类型断言失败：主线实际已改TextBlock（左对齐/单行省略/Tooltip全文），且main本身仍有旧断言。已只修测试并加强行为验证，未回退生产控件，完整重跑2317项通过。环境初次缺CMake/原生资产/旧版夹具、错误DLL入口调用的失败日志均保留，与产品行为失败分别记载。

实际C2 paired PVF保持FB0EEF35/001899B2，69399项及body一致。禁地worldmap原本已有27 452，不执行ACT1专用资源修改；任务译名采用C2自身。16组实际冰心房间（两图×四难度×单/双人参数）271项通过，验证按monster行映射阵营、保留心、不晋升心、只由敌对boss触发清图。尚不等同C2真机进入冰心。

客户端字体生命周期补丁依据C2实际Begin/End/Draw：原性能显示把栈状态留在renderer，现只结束自己建立的batch、保留借用者。实际映像RED570失败到GREEN2728项0失败，独立复审通过；完整宿主集成/真机待验。

新副本缺loginInfo.inf会在原生fopen失败后进入空文件指针路径。启动helper现在CreateNew预置空文件、保留已有字节，不复制旧登录信息，不扩大ACL；同时验证目标EXIT而不是仅看broker返回值。root专项74项通过，真实新副本复验待执行。

构建、审计及原始记录在 dist/chapter-two-main-sync-20261110。普通交付资源与五品质DO-NOT-SHIP资源分开；后者仅供专门隔离副本测试。
