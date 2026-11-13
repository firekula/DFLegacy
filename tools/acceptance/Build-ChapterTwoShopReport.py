"""Render the retained shop/container evidence as an offline illustrated report."""
import datetime
import hashlib
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = root / 'dist/c2-shop-restoration-20261112'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
resources = read(out / 'combined-resource-green.json')
installed = read(out / 'installed.json')
names = ['土罐','火罐','木罐','水罐','金罐']
jars = {j['id']: j for j in resources['jars']}
rows = []
for name, shop in zip(names, resources['shops']):
    prices = sorted({int(jars[i]['price']) for i in shop['items']})
    rows.append(f'<tr><td>{name}</td><td>0 → {len(shop["items"])}</td><td>{", ".join(f"{p:,}" for p in prices)} 金币</td><td>第一章商品顺序、价格一致</td></tr>')
jar_rows = ''.join(f'<tr><td>{j["id"]}</td><td>{html.escape(j["name"])}</td><td>{int(j["price"]):,}</td><td>使用类型、奖励、价格已核对；真机待测</td></tr>' for j in resources['jars'])
hash_rows = ''.join(f'<tr><td>{html.escape(str(Path(m["Path"]).relative_to(root)))}</td><td class="hash">{m["After"]}</td></tr>' for m in installed if not m['Path'].endswith('state.json'))
stamp = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
content = f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>第二章商店与礼包恢复 · 修复和验证报告</title>
<style>body{{margin:0;background:#eff3f7;color:#172638;font:16px/1.7 "Microsoft YaHei",sans-serif}}main{{max-width:1100px;margin:auto;padding:36px 24px}}h1{{font-size:30px;line-height:1.35}}h2{{margin:0 0 16px;font-size:22px}}h3{{font-size:18px}}section{{background:white;border:1px solid #dce4ec;border-radius:12px;padding:24px;margin:20px 0}}.muted{{color:#576879}}.pending{{background:#fff4d5;border-left:5px solid #dc9c15;padding:16px 20px}}.good{{color:#087345}}.metrics{{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}}.metric{{background:#eaf4ef;padding:16px;border-radius:9px}}.metric b{{font-size:28px;display:block}}table{{border-collapse:collapse;width:100%;font-size:14px}}th,td{{text-align:left;vertical-align:top;padding:10px;border-bottom:1px solid #dde4ec}}th{{background:#eef3f7}}figure{{margin:20px 0}}img{{display:block;width:100%;height:auto;border:1px solid #dce4ec}}figcaption{{color:#576879;font-size:14px;padding-top:8px}}.hash,code{{font-family:Consolas,monospace;overflow-wrap:anywhere;font-size:12px}}a{{color:#12649c}}summary{{cursor:pointer;font-weight:bold}}.split{{display:grid;grid-template-columns:1fr 1fr;gap:22px}}@media(max-width:750px){{.metrics,.split{{grid-template-columns:1fr 1fr}}main{{padding:18px 12px}}section{{padding:16px}}table{{font-size:12px}}}}@media print{{body{{background:white}}section{{break-inside:avoid}}details{{display:block}}}}</style>
<main><p class="muted">codex/chapter-two · 127.0.0.2 · {stamp}</p>
<h1>第二章五个罐子商店与旧礼包恢复</h1>
<div class="pending"><b>当前状态：已备份、构建、安装并启动；离线验证通过，真机验收未完成。</b><br>Windows 为冒烟测试弹出的防火墙询问遮挡游戏，控制工具拒绝向游戏派发点击。需要用户手动点“取消”后继续。未接受网络权限、未修改系统安全设置；本报告不把启动成功或离线通过当作购买、开罐的真机通过。</div>
<section><h2>已完成的修复</h2><div class="metrics"><div class="metric"><b>5</b>商店补齐，共53个商品位</div><div class="metric"><b>107</b>袖珍罐核对，101个恢复使用</div><div class="metric"><b>10</b>礼包恢复商城与背包使用</div><div class="metric"><b>13</b>旧礼包仍缺完整权益依据</div></div>
<p>五店空白来自原第二章资源自身的空商品表。按第一章恢复商品顺序，并恢复101个被设为普通物品的袖珍罐使用类型；另外6个已可用。奖励表、售卖价、开启成本、等级条件和图标保持来源值。107种包含活动罐，不等于全部上架；五店仍只销售第一章对应的53个商品位。</p>
<table><tr><th>商店</th><th>商品位</th><th>所售罐子价格</th><th>资源结果</th></tr>{''.join(rows)}</table>
<figure><img src="screenshots/01-empty-danjin-before.jpg"><figcaption>修复前的真实土罐商店：左侧商品区域为空。修复后页面尚未完成真机截图，不能用资源表代替。</figcaption></figure></section>
<section><h2>礼包恢复范围与价格</h2><p>按照用户同条消息后半句“把礼包也恢复”，已恢复下列有完整奖励依据的10种。商城独立编号避免与现有商品冲突；保留原价格和资格条件。</p>
<table><tr><th>物品ID</th><th>内容</th><th>点券</th><th>处理</th></tr><tr><td>38–41</td><td>四职业福包，共4种</td><td>每种29,800</td><td>恢复商城拆奖与背包开启</td></tr><tr><td>52–56</td><td>野生力量套装，共5种</td><td>每种22,200</td><td>恢复商城拆奖与背包开启</td></tr><tr><td>49</td><td>达人之契约15天＋遗忘河之水1件</td><td>13,550</td><td>契约直接写入账号权益</td></tr><tr><td>9、11、27、63–67、69–73</td><td>冒险家、挑战者、海之勇者系列，共13种</td><td>未上架</td><td>缺少完整权益/期限依据，继续停用</td></tr></table>
<p>剩余13种涉及仅有文字说明的疲劳、每日复活币或“不限疲劳”权益；海之勇者的两章原奖励行也有差异。没有编造期限、删掉权益后按完整礼包价格出售，也没有删除迁移存档中持有的旧礼包。第二章原本4个无效礼包定义继续隔离。</p>
<p>本地补丁只为受支持且类型匹配的10件道具启用原生使用入口，其余物品走原有路径。购买和背包开启共用奖励规则，装扮、宠物、复活币和契约进入各自对应位置。礼包沿用溢出邮件；袖珍罐仍执行真正满包和超重拒绝，两者规则分别验证。</p></section>
<section><h2>结算与失败重试</h2><p>故障注入发现：原有礼包购买在写盘失败时，内存余额与权益可能已经变化。已改为同一原子保存，并在提交失败时恢复内存快照；过期背包计划拒绝重复使用。实际验证了失败后重试、重新加载存档、邮件只能领取一次、满邮箱拒绝，以及扣款与奖励同进同退。</p>
<p>全量冒烟另发现一个测试日期问题：周四运行时，前一项把日期推进到周五并消耗3点，后一项仍以同一个周五当作新一天。实测返回为3/156/0，产品疲劳上限正确。测试日期已安排到更晚的周五，并增加“确实进入新一天”的断言；原上限、已用值断言未放宽。独立复审枚举七个起始星期均通过。</p></section>
<section><h2>验证结果：离线与真机分开记录</h2><table><tr><th>验证</th><th>结果</th><th>证据</th></tr>
<tr><td>双端资源、五店、罐子、商城映射</td><td class="good">826项，0失败；69,399条资源校验</td><td><a href="combined-resource-green.json">资源检查</a></td></tr>
<tr><td>107种袖珍罐奖励放置</td><td class="good">204,527项，25,392次放置规划，0失败</td><td><a href="all-jars-final.log">完整奖励与数量日志</a></td></tr>
<tr><td>10种礼包实际资源</td><td class="good">910条奖励；908个正权重行分别强制命中购买/开启，0失败</td><td><a href="server-work/combined-green02.log">礼包专项</a></td></tr>
<tr><td>原生使用入口与既有修复</td><td class="good">新增313项及完整宿主回归通过</td><td><a href="native-final-build02.log">原生构建与验证</a></td></tr>
<tr><td>完整服务端冒烟、最终PVF迁移</td><td class="good">两项均0失败</td><td><a href="full-smoke02.log">全量冒烟</a> · <a href="final-pvf-migration-smoke02.log">最终资源回归</a></td></tr>
<tr><td>独立复审</td><td class="good">无阻断发现；重复执行专项通过</td><td><a href="containers-review.txt">结算/原生审查</a> · <a href="jar-resource-review.txt">资源审查</a></td></tr>
<tr><td>真实新版启动</td><td>服务就绪，游戏到频道选择；原生入口日志尚在缓冲</td><td><a href="runtime-logs/">本次运行日志快照</a></td></tr>
<tr><td>五店购买、开罐、满格、超重、礼包界面</td><td><b>未完成真机验收</b>，被系统询问阻挡</td><td>下方真实截图</td></tr></table>
<figure><img src="screenshots/03-server-started.jpg"><figcaption>候选服务经管理器正常启动；地址127.0.0.2，进程43228。管理器历史版本说明文字未用作文件版本依据，实际文件哈希见下表。</figcaption></figure>
<figure><img src="screenshots/04-ui-blocked-firewall.jpg"><figcaption>新版游戏已到频道选择；Windows安全询问覆盖前台。一次频道点击被工具判定为安全窗口覆盖而拒绝，未执行购买或选择角色。</figcaption></figure></section>
<section><h2>保留存档、构建与复测入口</h2><p>安装前完整备份位于 <code>before-install/</code>，仅排除管理器正在持有的锁文件；每个复制文件均有哈希记录。安装只追加验收账号shopqa和8个测试角色。服务器启动后再次比较，原有三个账号完整记录保持一致。</p>
<p>隔离客户端：<code>{root / 'dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DFLegacy.Chapter2.Launcher.exe'}</code>。启动器已有“商店与礼包验收”配置，不在报告公开测试密码。服务管理器仍在 <code>http://127.0.0.2:48092/</code>。</p>
<table><tr><th>构建/资源</th><th>SHA-256</th></tr>{hash_rows}</table>
<p><a href="launcher/DFLegacy-Chapter2-Launcher-GUI-Windows-x64.zip">启动器ZIP</a> · <a href="server-build/">服务端构建</a> · <a href="installed.json">安装前后哈希</a> · <a href="baseline.json">基线</a> · <a href="before-install/hashes.json">备份哈希</a></p>
<p>主线仍为209a1da，第二章HEAD仍为de4f5a8；未提交、推送或发布，未改日常客户端。服务所有TCP/UDP监听均核对为127.0.0.2。</p></section>
<section><h2>待接续的真机清单</h2><ol><li>手动取消系统询问后，在五个“商店验收”角色逐店确认商品、价格、购买扣款与入包；每店购买并开罐，重登核对。</li><li>“满格验收”：装备栏与快捷栏满，开武器罐应明确提示空位不足且不扣罐。腾出一个正确格位后重试。</li><li>“超重验收”：装备栏有空位但重量超限，应明确提示负重且不扣罐；存仓减重后重试。</li><li>“礼包验收”：十种礼包各开启一次，核对奖励数量、装扮/宠物位置；49核对15天契约。商城购买检查价格、拆奖、余额、溢出邮件及重新登录。</li><li>历史边界继续保留：双人同物品掉落、宠物跨角色赠送、仓库占格交换与整理、摆摊第二排/失败重试、实体交易，以及其他队伍/技能/频道对照缺项。这次全量自动检查不代表这些场景已在真实客户端完成。</li></ol>
<p>此前用户已确认过上一版开罐、满格与超重提示；本轮因恢复了更多资源，仍需对候选版重新验收，不能沿用旧反馈替代。</p></section>
<section><details><summary>107种袖珍罐核对明细（点击展开）</summary><table><tr><th>ID</th><th>名称</th><th>金币售价</th><th>状态</th></tr>{jar_rows}</table></details></section>
<p class="muted">当前报告明确保留未完成项；没有“全部真机通过”的结论。</p></main></html>'''
(out / 'REPORT.html').write_text(content, encoding='utf-8')
print(out / 'REPORT.html')
