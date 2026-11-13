"""Build a standalone illustrated progress report from retained local evidence."""
import base64
import html
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
warehouse = repo / 'dist/c2-warehouse-quick-store-20261113'
transition = Path('F:/codex-archives/c2-chapter-transition-20261113')
output = repo / 'dist/c2-warehouse-update-prep-20261113/report'
output.mkdir(exist_ok=True)


def figure(path, caption):
    mime = 'image/jpeg' if path.suffix.lower() == '.jpg' else 'image/png'
    return '<figure><img src="data:' + mime + ';base64,' + base64.b64encode(path.read_bytes()).decode() + '"><figcaption>' + html.escape(caption) + '</figcaption></figure>'


body = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>第二章仓库快捷存入与跨章更新 · 隔离验收报告</title><style>
*{box-sizing:border-box}body{margin:0;background:#eff3f7;color:#233449;font:16px/1.8 "Microsoft YaHei",sans-serif}main{max-width:1100px;margin:auto;padding:38px 28px 80px}header{background:#132b44;color:white;padding:32px;border-radius:14px}h1{font-size:30px;margin:8px 0}h2{font-size:24px;margin-top:38px}h3{font-size:19px}p{margin:12px 0}.sub{color:#b6cce0}.notice{background:#fff4dc;border-left:5px solid #c18412;padding:18px;margin:22px 0;border-radius:5px}.good{color:#167145;font-weight:bold}.pending{color:#9e6410;font-weight:bold}table{width:100%;border-collapse:collapse;background:white;margin:18px 0}th,td{padding:12px 14px;border-bottom:1px solid #dce4ec;text-align:left;vertical-align:top}th{background:#e2ebf3}td:first-child{width:27%}figure{margin:20px 0;padding:14px;background:white;border:1px solid #dae3ec;border-radius:10px}img{max-width:100%;display:block;margin:auto;height:auto}figcaption{margin-top:10px;color:#4f6074;font-size:14px}.pairs{display:grid;grid-template-columns:1fr 1fr;gap:14px}.pairs figure{margin:0}.cards{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin:20px 0}.card{background:white;padding:18px;border-radius:10px}.n{font-size:27px;font-weight:bold;color:#1a547c}.path{font:13px/1.7 Consolas,monospace;overflow-wrap:anywhere;background:#e6edf3;padding:12px}footer{border-top:1px solid #cdd9e5;margin-top:30px;padding-top:18px;color:#586d81}@media(max-width:700px){main{padding:18px 14px}.pairs,.cards{grid-template-columns:1fr}h1{font-size:24px}td:first-child{width:33%}}@media print{body{background:white}main{padding:0}figure,table{break-inside:avoid}.notice{border:1px solid #c18412}}
</style><main><header><div class="sub">2026-11-13 · codex/chapter-two · 本机隔离验收</div><h1>仓库快捷存入与原目录跨章更新</h1><p>实施、修复与图文记录。此报告为阶段结果，未把待测项目标成通过。</p></header>
<div class="notice"><b>本轮结果：</b>仓库右键整组存入、满仓拒绝和可并堆场景已真机通过。完整第一章副本已从原入口升级第二章、回退第一章进城、再次升级；第二章后续增量更新和真实损坏文件修复均通过。中文拼音选词上屏、商城和仓库也已在升级后的目录复测。Shift 组合鼠标仍缺物理输入验收，旧固定入口布局未认证支持，不能宣称所有旧客户端均已覆盖。所有操作只在本机隔离副本，没有发布更新。</div>
<div class="cards"><div class="card"><div class="n">97 + 69</div>仓库服务端及原生定向断言通过</div><div class="card"><div class="n">538 / 538</div>登记后的第一章文件独立校验一致</div><div class="card"><div class="n">700 个文件</div>第二章安装、修复后逐文件一致</div></div>
<h2>1. 仓库快捷存入</h2><p>仓库打开时，右键背包物品整组存入；优先完整并入同类堆叠，再找空格。Shift 右键堆叠物品使用原有数量框，默认最大数量。不能完整存放时保持原物品，不执行使用，也不与仓库物品交换。</p>
<table><tr><th>场景</th><th>实际结果</th><th>证据等级</th></tr>
<tr><td>装备、单件材料</td><td>装备 27301 和材料 3016×1 正常存入；装备实例、耐久 35 保留。</td><td class="good">真机 + 保存后核对</td></tr>
<tr><td>整组材料、消耗品</td><td>3037×496、8×20 完整入仓；消耗品没有被使用。金币 999989 不变。</td><td class="good">真机 + 保存后核对</td></tr>
<tr><td>重复点击 / 重开仓库</td><td>重复空格点击无变化，关仓重开正常，退出保存后 4/120 格一致。</td><td class="good">真机</td></tr>
<tr><td>满仓且无法并堆</td><td>120 格满时，装备和没有同类堆叠的材料留在背包。没有明确错误弹窗，服务记录给出拒绝原因。</td><td class="good">真机</td></tr>
<tr><td>满仓但同类可并堆</td><td>496 个材料完整并入 1000 的已有堆叠，成为 1496；仍占 120 格。1000 为夹具数量，不是上限。</td><td class="good">真机</td></tr>
<tr><td>失败后继续操作</td><td>装备拒绝后，紧接着材料存入成功，没有卡在等待状态。</td><td class="good">真机</td></tr>
<tr><td>Shift 默认 / 部分 / 取消 / 关仓</td><td>原生回归覆盖。当前控制工具不能持续按住 Shift 再点击，物理输入尚需复测。</td><td class="pending">仅离线验证</td></tr>
<tr><td>关闭仓库的原使用、不可存物品</td><td>代码分流与回归保留；本轮未逐一真机覆盖。</td><td class="pending">仍需真机补测</td></tr></table>
'''
body += '<div class="pairs">' + figure(warehouse/'screenshots/01-before-equipment.jpg','存入前：装备仍在背包。') + figure(warehouse/'screenshots/02-equipment-stored.jpg','右键后：装备进入仓库，属性与实例继续保留。') + '</div>'
body += figure(warehouse/'screenshots/06-consumable-all-stored.jpg','装备、材料和消耗品已依次完整存入；退出后的状态文件再次核对数量。')
body += '<div class="pairs">' + figure(warehouse/'screenshots/09-full-warehouse-rejected-equipment.jpg','120 格已占用：装备拒绝后仍在背包。') + figure(warehouse/'screenshots/10-full-warehouse-stack-merged.jpg','同一满仓状态：材料可以完整并入同类堆叠。') + '</div>'
body += '''<p>满仓测试使用独立备份和专用夹具；测试完成后已恢复正常存档，人工填满的仓库没有留在用户接着测试的存档里。</p>
<h2>2. 原目录升级与回退</h2><p>保留同一个 DFLegacy.Launcher.exe 入口。第一章先桥接，再登记完整资源，随后主动升级第二章；长期回退点与普通更新备份分开保存。回退保留当前账号记录，并停留第一章，需再次主动选择升级。</p>
<table><tr><th>阶段</th><th>结果</th><th>状态</th></tr>
<tr><td>旧独立启动器 → 桥接版</td><td>真实 v0.1.11 原入口通过自己的更新助手完成更新和自动重开。</td><td class="good">实际安装通过</td></tr>
<tr><td>第一章完整资源登记</td><td>538 文件、1,020,586,070 字节逐文件一致。账号列表、当前账号和凭据与原副本相同。</td><td class="good">实际安装与审计通过</td></tr>
<tr><td>第一章游戏基线</td><td>用户取消系统提示后，原账号选择角色并正常进城。第一章测试副本的显示配置修正为正确的清晰模式键名；日常目录未修改。</td><td class="good">真机通过</td></tr>
<tr><td>完整第二章包</td><td>b4 共 700 文件、1,256,866,170 字节，包括本体与图像、音效、PVF。安装后逐文件摘要全部一致。</td><td class="good">实际安装通过</td></tr>
<tr><td>第二章进城及功能回归</td><td>同一账号、角色进城；仓库四组物品保留，3022×9 右键整组存入后为 5/120 格；道具、宠物商城正常加载；拼音 ni 选词后“你”正常上屏，未发送。</td><td class="good">真机通过</td></tr>
<tr><td>回退第一章 / 再次升级</td><td>同目录恢复 C1b3 的 538 文件并进城。重开仍停留第一章；主动选择才再次升级到 C2b4。账号记录保留，1600×1200 清晰设置迁移。</td><td class="good">真机与全文件审计通过</td></tr>
<tr><td>第二章普通增量</td><td>b4→b5 仅更改章节描述文件的排版，语义相同，另外 699 文件不变。真实启动器自动更新、重开，700 文件全部一致；原回退点摘要不变。</td><td class="good">实际更新通过</td></tr>
<tr><td>真实损坏修复</td><td>退出后先备份 Music/result.ogg，再改动一个字节。独立修复入口退出码 0，恢复原始 SHA-256，700 文件全量复核通过；回退点仍保留，修复后同角色再次进城。</td><td class="good">真实文件修复通过</td></tr>
<tr><td>旧固定入口布局</td><td>不能直接套用本次旧独立启动器桥接方法；需单独兼容验证。</td><td class="pending">未认证支持</td></tr></table>
<h3>真实链路发现并修复的三处遗漏</h3><p><b>旧包诊断组件遗漏：</b>旧安装助手会校验原有诊断组件，但最初桥接包没有携带它们，导致备份校验在写入前拒绝。现已要求桥接包保留旧运行组件；补包后真实更新成功。</p><p><b>清单与配置校验规则不一致：</b>配置迁移已支持新入口，清单读取还只认旧规则，首次资源登记被拒绝。现统一校验，并增加实际清单解析回归；登记及账号保留通过。</p><p><b>交接后诊断助手被重新启动：</b>跨章界面在成功交接安装后仍恢复诊断助手，造成旧组件占用。第一次升级在写入前拒绝，原 538 文件和账号复核无变化。现成功交接后保持诊断暂停；失败或取消时仍恢复诊断。14 项定向回归、独立审阅及整套真实升级/回退复测通过。</p><p>第二章第一次启动遇到三次地址布局不满足，已有启动器重试机制在第四次成功启动。此项日志保留，不把短暂启动重试描述成新增修复。</p>
'''
body += figure(transition/'screenshots/05-c1-managed-restarted.png','桥接、完整资源登记完成后自动重开；界面显示当前受管理第一章版本。')
body += '<div class="pairs">' + figure(transition/'screenshots/07-c1-town-baseline.png','升级前：第一章原副本正常进城。') + figure(transition/'screenshots/11-c2-town-after-upgrade.png','原目录升级后：同一角色进入第二章。') + '</div>'
body += figure(transition/'screenshots/13-c2-quick-store-after-upgrade.png','更新后的第二章再次右键存入 9 个材料，仓库从 4/120 增至 5/120，金币不变。')
body += '<div class="pairs">' + figure(transition/'screenshots/14-c2-shop-after-upgrade.png','更新后的道具商城，物品图标和文字正常加载。') + figure(transition/'screenshots/15-c2-pet-shop-after-upgrade.png','宠物商城页正常显示；本轮跨章冒烟未重复购买全商品。') + '</div>'
body += '<div class="pairs">' + figure(transition/'screenshots/16-c2-chinese-composition.png','聊天框中输入 ni：拼音与候选显示正常。') + figure(transition/'screenshots/17-c2-chinese-committed-unsent.png','空格选择“你”后正常上屏，随后取消，未发送聊天消息。') + '</div>'
body += figure(transition/'screenshots/20-c1-town-after-rollback.png','通过启动器回退第一章后，同一角色再次进城。此后已主动再次升级第二章。')
body += figure(transition/'screenshots/22-c2-incremental-b5-restarted.png','第二章增量更新完成并自动重开。版本 b5，全量文件审计通过。')
body += figure(transition/'screenshots/23-c2-town-after-repair.png','真实文件损坏修复后，第二章再次正常进城。测试结束后已正常退出游戏，隔离服务继续保留。')
body += '''<h2>3. 回归、构建与可追溯资料</h2><table><tr><th>检查</th><th>本轮最终结果</th></tr><tr><td>仓库定向 / 原生定向 / 服务全量冒烟</td><td>97 项、69 项通过；完整服务冒烟通过。</td></tr><tr><td>UpdateCore</td><td>796 项断言及章节事务回归通过。</td></tr><tr><td>启动器更新 / 就绪交接 / 修复</td><td>71 / 53 / 22 项通过。53 与 22 项须运行测试 EXE；最初直接以 dotnet DLL 运行不能正确自启动测试子进程，已纠正运行方式。</td></tr><tr><td>本地打包与桥接</td><td>7 项通过；覆盖必需资源、输入私有文件保护、版本边界、分卷和文件摘要。</td></tr></table>
<p>第二章手工测试目录仍为：</p><div class="path">F:/codex-archives/c2-shop-restoration-20261113/manual-restore/DNFS1C2</div>
<p>本轮跨章完整副本与最终构建：</p><div class="path">F:/codex-archives/c2-chapter-transition-20261113/client/DFLegacy.Launcher.exe<br>F:/codex-archives/c2-chapter-transition-20261113/c1-final-r3<br>F:/codex-archives/c2-chapter-transition-20261113/c2-final-r3</div>
<p>仓库构建、截图和状态核对：</p><div class="path">D:/codex_worktree/dnf-s1c2-overnight/DFLegacy/dist/c2-warehouse-quick-store-20261113</div>
<p>跨章截图与日志位于跨章归档的 screenshots、logs；标记 private 的本机配置备份没有放入本报告或更新包。</p>
<h2>4. 继续手测与边界</h2><p>从上面的 client/DFLegacy.Launcher.exe 启动完整验收副本；仓库右键整组存入已通过。请补测 Shift+右键：数量框应默认整组最大数量，改成部分数量后只存入所选数量，取消时物品保持。当前电脑控制工具不能持续按住 Shift 再点击，因此这几项只完成离线回归，未标成真机通过。</p><p>旧固定入口/应用分离布局仍未认证支持；本轮完成的是原 v0.1.11 独立入口到受管理完整资源的真实迁移。正式远程分发不在本轮范围。空间不足采用预检回归，没有写满真实磁盘。本轮商城检查属于更新后的资源和操作冒烟，不等同于重新覆盖全部商品与既往四人玩法测试。</p><p>证据索引：c1-b3-audit.json、c2-first-upgrade-audit.json、c1-rollback-hold-audit.json、c2-reupgrade-audit.json、c2-incremental-b5-audit.json、c2-repaired-audit.json；修复记录为 repair-injected-damage.json、c2-repair-live-incident.log、c2-repair-live-exit.txt。均在跨章归档 logs 目录。检查点摘要在各审计结果中一致。</p><footer>范围：只修改 codex/chapter-two 与隔离副本；主线、线上、日常客户端保持原状。未提交混合工作树、未推送、未发布。此报告记录实际覆盖，待测项目继续保留。</footer></main></html>'''
target = output / '第二章仓库与跨章更新-验收报告.html'
target.write_text(body, encoding='utf-8')
print(target)
