"""Build the illustrated local-only settings / transition acceptance report."""
import base64
import hashlib
import html
import json
from pathlib import Path

run = Path('F:/codex-archives/c2-transition-retest-20261113')
out = run / 'report'
out.mkdir(exist_ok=True)


def load(name):
    return json.loads((run / 'logs' / name).read_text(encoding='utf-8-sig'))


def figure(name, caption):
    data = base64.b64encode((run / 'screenshots' / name).read_bytes()).decode()
    return f'<figure><img src="data:image/png;base64,{data}" alt="{html.escape(caption)}"><figcaption>{html.escape(caption)}</figcaption></figure>'


audits = [(name, load(name)) for name in [
    '02-rollback-audit.json', '03-locked-refusal-audit.json',
    '04-reupgrade-audit.json', '06-fixed-upgrade.json',
    '07-fixed-rollback-added-account.json', '08-fixed-interruption-recovered.json',
    '09-fixed-final-settings.json', '11-published-c2-upgrade.json',
    '12-published-c1-rollback.json']]
for name, audit in audits:
    assert not audit['mismatches'] and not audit['retiredPresent'], name
    assert all(audit['accountFieldsUnchanged'].values()), name
    assert not audit['pendingPresent'] and not audit['rollbackPending'], name
before = load('settings-before-restart-hashes.json')
after = load('settings-after-restart-hashes.json')
assert before == after
muted = load('audio-game-default-muted.json')
normal = load('audio-game-normal-mode.json')
assert muted['volumes'] == [0, 0, 0] and normal['volumes'] == [1, 1, 1]
ui_before = load('audio-ui-before.json')
ui_unchecked = load('audio-ui-unchecked.json')
ui_rechecked = load('audio-ui-rechecked.json')
assert ui_before['pid'] == ui_unchecked['pid'] == ui_rechecked['pid'] == 38284
assert ui_before['volumes'] == ui_rechecked['volumes'] == [0, 0, 0]
assert ui_unchecked['volumes'] == [1, 1, 1]
assert load('audio-ui-preference.json')['ChapterTwoTestMuteEnabled'] is False
assert load('audio-ui-preference-rechecked.json')['ChapterTwoTestMuteEnabled'] is True
assert load('audio-ui-settings-after.json')['Hash'] == before[0]['Hash']
assert '2297 assertions' in (run/'logs/launcher-full-after-race-fix.log').read_text(encoding='utf-8-sig')
assert '16 assertions, 0 failures' in (run/'logs/audio-bind-green.log').read_text(encoding='utf-8-sig')
build = run / 'builds/settings-launcher-final'
exe = build / 'package/DFLegacy.Chapter2.Launcher.exe'
exe_sha = hashlib.file_digest(exe.open('rb'), 'sha256').hexdigest()
zip_path = build / 'DFLegacy-Chapter2-Launcher-GUI-Windows-x64.zip'
zip_sha = hashlib.file_digest(zip_path.open('rb'), 'sha256').hexdigest()
result = dict(settingsBefore=before, settingsAfter=after, muted=muted,
              normal=normal, uiToggle=dict(before=ui_before, unchecked=ui_unchecked, rechecked=ui_rechecked,
                                          userAssisted=True, gameSettingsUnchanged=True),
              launcherSha256=exe_sha, zipSha256=zip_sha,
              audits=[dict(file=n, version=a['version'], files=a['files'], result='pass') for n,a in audits],
              limits=['Published native entry plus bridge tested; original v0.1.10 managed updater remote discovery not exercised.',
                      'Download cancel button not counted as real-client pass.',
                      'Previous multiplayer and trade gaps are outside this acceptance batch.'])
(out/'evidence-summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

body = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>第二章设置与跨章续测报告</title><style>
*{box-sizing:border-box}body{margin:0;background:#eef2f6;color:#203248;font:16px/1.75 "Microsoft YaHei",sans-serif}main{max-width:1120px;margin:auto;padding:32px 28px 70px}header{background:#173149;color:white;padding:30px;border-radius:14px}h1{margin:6px 0;font-size:31px}h2{margin-top:34px;font-size:24px}h3{font-size:19px}.sub{color:#bfd2e0}.notice{padding:18px 20px;background:#fff2d5;border-left:5px solid #c68c24;margin:22px 0}.cards,.pair{display:grid;gap:16px;grid-template-columns:1fr 1fr}.cards{grid-template-columns:repeat(3,1fr)}.card{background:white;padding:18px;border-radius:10px}.number{font-weight:bold;color:#176549;font-size:27px}table{width:100%;border-collapse:collapse;background:white;margin:18px 0}th,td{padding:12px 14px;text-align:left;vertical-align:top;border-bottom:1px solid #dbe3eb}th{background:#dfe9f1}td:first-child{width:27%}.ok{color:#176549;font-weight:bold}.pending{color:#95630c;font-weight:bold}figure{margin:16px 0;padding:12px;background:white;border:1px solid #d8e1ea;border-radius:9px}.pair figure{margin:0}img{display:block;max-width:100%;height:auto;margin:auto}figcaption{font-size:14px;color:#52687b;margin-top:10px}.path{font:13px/1.6 Consolas,monospace;overflow-wrap:anywhere;padding:14px;background:#e2eaf2}code{overflow-wrap:anywhere}footer{margin-top:32px;padding-top:20px;border-top:1px solid #cbd6e1;color:#52687b}a{color:#12649d}@media(max-width:740px){main{padding:18px 12px}.cards,.pair{grid-template-columns:1fr}h1{font-size:25px}table{font-size:14px}}@media print{body{background:white}figure,tr,.card{break-inside:avoid}}
</style><main><header><div class="sub">2026-11-13 · codex/chapter-two · 本机隔离验收</div><h1>设置保留、测试静音与跨章更新</h1><p>本轮修复、真机前后对照和固定入口补测。构建、截图与原始记录均已保留。</p></header>
<div class="notice"><b>结果：</b>游戏设置重开丢失已修复并真机验证；测试模式默认零音量、普通模式保留音量已实测，用户协助点击静音开关的往返切换也已通过。固定入口回退、新增账号保留和安装中断恢复通过；正式旧入口原件搭配过渡版的升级、回退通过。<b>原旧托管应用的远程更新发现尚未覆盖。</b></div>
<div class="cards"><div class="card"><div class="number">2297 + 16</div>启动器全量与设置/音频定向断言通过</div><div class="card"><div class="number">541 ⇄ 703</div>固定入口第一章、第二章逐文件核对</div><div class="card"><div class="number">0 → 1 → 0</div>同一游戏会话：静音、取消、重新开启</div></div>
<h2>1. 游戏内设置为什么会丢失</h2>
<p>旧版将声音、画面选项写进每次启动新建的临时目录。文件确实保存了，但重开时换了目录，因此看起来修改没有生效。新版把这些选项长期保存在当前隔离客户端内；首次自动采用本目录最近的有效设置，之后不覆盖。按键继续使用原来的账户按键文件。</p>
<table><tr><th>实际操作</th><th>结果</th><th>证据</th></tr>
<tr><td>自动采用旧会话</td><td>原音效 32、音乐 30、镜头关闭均保留；旧文件未修改。</td><td class="ok">真机 + 文件</td></tr>
<tr><td>修改声音、镜头并重开</td><td>配置值音效 44、音乐 52、镜头开启；正常退出后重新进城，UI 与文件一致。</td><td class="ok">真机 + 相同摘要</td></tr>
<tr><td>修改按键并重开</td><td>宠物攻击 V 改为 F8，重开仍显示 F8；完成后已改回 V。</td><td class="ok">真机 + 按键文件</td></tr>
<tr><td>旧、新实际保存逻辑对照</td><td>使用真实 Windows 配置读写和不同启动目录：旧版 3 项失败，新版 0 失败。</td><td class="ok">原生 RED/GREEN</td></tr></table>
<div class="pair">'''
body += figure('15-settings-changed.png','第一次会话：在游戏内调整音效、音乐并启用镜头选项。')
body += figure('18-settings-after-restart.png','完整退出、重新启动并进城后：两个滑块和镜头选项保持。')
body += '</div>' + figure('19-key-after-restart.png','重开后自定义 F8 保持；测试结束前已恢复原来的 V。')
body += '''<h2>2. 测试模式默认零音量</h2><p>Ctrl+F11 物品工具新增「测试静音（仅当前游戏）」，默认开启，无需 GM 登录。它调整本次游戏的音频会话，保留游戏内的声音滑块，也不改变系统总音量。关闭时恢复本次会话原音量；选择会保存，普通非调试模式不自动静音。</p>
<table><tr><th>检查</th><th>结果与范围</th></tr><tr><td>真实游戏、调试模式</td><td class="ok">PID 36152 的三个会话均为 [0, 0, 0]。</td></tr><tr><td>退出后，普通模式重开</td><td class="ok">PID 38696 的三个会话均为 [1, 1, 1]；游戏设置文件未变。</td></tr><tr><td>立即取消与退出恢复</td><td>真实 CoreAudio 定向测试通过；同时覆盖游戏先退出、快速启动失败两种情况。</td></tr><tr><td>面板复选框真机点击</td><td class="ok">用户手动打开面板并取消、重新勾选；同一游戏 PID 38284 的三个会话从 [0, 0, 0] → [1, 1, 1] → [0, 0, 0]。两次选择均保存，游戏设置摘要不变。</td></tr></table>
<p>该项为用户协助的真机验收：自动短按未打开面板，工具也未单独列出此附属窗口，因此由用户实际点击，随后用只读观察器核对真实音量，并核对保存选项及日志。</p>
<p>审阅发现的音频生命周期问题也已修复：恢复时保留精确的原音频会话；遇到已结束的启动尝试安全放弃绑定。完整测试另修正了测试程序读取结果文件过早的时序问题，未改变产品安装逻辑。</p>
<h2>3. 原目录升级与回退续测</h2><table><tr><th>场景</th><th>实际结果</th></tr>
<tr><td>原单 EXE 布局</td><td class="ok">回退 538 文件，重开停留第一章，再升级 700 文件；文件占用时在写入前拒绝。</td></tr>
<tr><td>当前固定入口布局</td><td class="ok">541→703 文件升级、进城；回退时保留升级期间新增的本机账号记录及原账号。</td></tr>
<tr><td>安装中断恢复</td><td class="ok">完整安装已写入文件、尚未提交收据时终止本次安装子进程。重新启动固定入口后恢复 541 文件；账号、回退点一致，无未完成事务。</td></tr>
<tr><td>正式旧入口原件</td><td class="ok">采用 v0.1.10 正式包的原入口，搭配当前第一章过渡版托管组件，实际 UI 升级 703 文件、回退 541 文件；原入口摘要全程不变。</td></tr>
<tr><td>原旧托管应用的远程分发</td><td class="pending">未发布正式更新，因此未实测原 v0.1.10 托管应用从正式来源获取过渡版。本轮通过的是原入口兼容性。</td></tr>
</table><div class="pair">'''
body += figure('16-published-entry-c2.png','正式旧入口原件启动完成升级后的第二章过渡环境。')
body += figure('17-published-entry-rollback.png','同一入口回退并重开，保持第一章。')
body += '</div>' + figure('13-interruption-restored.png','安装中断后，固定入口自动恢复到第一章。')
body += '''<p>中断测试终止的是本次独立安装子进程，不是断电或每个安装阶段的穷举。下载阶段「取消」按钮尚未计为真机通过。之前已确认的普通右键数量框、Shift 右键双向整组仓库存取沿用既有验收；本轮没有重新覆盖实体交易、多人非金币掉落和四人翻牌组合。</p>
<h2>4. 构建与复测入口</h2><p>本轮最新、已通过设置实测的完整隔离客户端：</p><div class="path">F:/codex-archives/c2-transition-retest-20261113/fixed-client/DFLegacy.Launcher.exe</div>
<p>该副本连接原隔离服务 127.0.0.2，最终验收后保持打开供用户继续测试。调试模式和测试静音均已开启；旧单 EXE 验收副本保持原样，没有把它误写成已替换。服务端本轮未重启或替换。</p>
<p>可复用启动器 ZIP 和 EXE：</p><div class="path">F:/codex-archives/c2-transition-retest-20261113/builds/settings-launcher-final/<br>DFLegacy-Chapter2-Launcher-GUI-Windows-x64.zip<br>package/DFLegacy.Chapter2.Launcher.exe</div>'''
body += f'<p>ZIP SHA-256：</p><div class="path">{zip_sha}</div><p>EXE SHA-256：</p><div class="path">{exe_sha}</div>'
body += '''<p>分支基线为 3c3086b，本轮修复及测试记录按用户要求归入 codex/chapter-two。完整原生检查、启动器 2297 项、设置/音频 16 项最终通过。构建输入、源码快照和检查日志保存在本轮 builds、source-snapshot、logs 目录；设置和账号私有备份仅在 private 目录，不放入分发包或报告。</p>
<h3>可核对记录</h3><table><tr><th>记录</th><th>用途</th></tr>'''
for name,audit in audits:
    body += f'<tr><td><a href="../logs/{html.escape(name)}">{html.escape(name)}</a></td><td>{html.escape(audit["version"])} · {audit["files"]} 文件一致，账号一致</td></tr>'
body += '''</table><p><a href="evidence-summary.json">汇总摘要</a> · <a href="../logs/profile-routing-red-green.log">旧/新保存逻辑对照</a> · <a href="../logs/audio-bind-green.log">设置与音频回归</a> · <a href="../logs/launcher-full-after-race-fix.log">启动器完整检查</a> · <a href="../logs/audio-ui-unchecked.json">真机取消静音</a> · <a href="../logs/audio-ui-rechecked.json">真机重新静音</a> · <a href="../logs/audio-ui-settings-after.json">切换后设置摘要</a></p>
<footer>范围：codex/chapter-two 和独立副本。主线仍为 209a1da，线上与日常客户端未修改；不发布更新。本报告分别标明真机、定向测试和未覆盖项，不表示全部历史系统测试已完成。</footer></main></html>'''
target=out/'第二章设置与跨章续测报告.html'
target.write_text(body,encoding='utf-8')
print(target)
