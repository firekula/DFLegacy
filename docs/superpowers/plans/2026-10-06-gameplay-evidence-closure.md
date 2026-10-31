# 官服游戏面证据工程 · 收尾任务表

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans. Steps use checkbox syntax. 本表不是产品实现计划；只用于证据收尾、边界冻结和退出判定。

**Goal:** 在不开始产品代码重写、不进行真机登录前，完成客户端可见密码/封装协议的有限证据闭合，并以明确的“完成/降级/暂缓”状态停止无限取证。

**Architecture:** 以既有目标 ELF、官方容器提取的 `libantisvrimport.so` 和已存抓包为唯一分析输入。先做静态调用链，再做确定性离线回放；只有本表明确列为动态阻塞的项目才进入真机/运行实例阶段。服务器模块间加密实现暂缓，但其客户端可见边界和协议字段不因此消失。

**Tech Stack:** Python 3.12、Capstone、pyelftools、Unicorn、PyCryptodome、cryptography、Git Bash/PowerShell；只读 ELF/ELF32 SO 与本地抓包。

**Spec:** `docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`；接续依据：`.superpowers/sdd/2026-10-05-gameplay-evidence-program/RESUME.md`。

## Global Constraints

- 目标 ELF SHA256 必须为 `486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`，大小 `36,360,954 B`。
- 官方容器 SO 来源由所有者确认：`G:\Work\Server\dnf\build\dnf_data\home\template\neople\game\libantisvrimport.so`；SHA256 `8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7`，大小 `31,904,293 B`。
- 只修改 `.superpowers/sdd/2026-10-05-gameplay-evidence-program/` 下的分析脚本与报告；不改 `src/`、`tests/`、`STATUS.md`、`server.json`。
- 不启动客户端、不连接 VM、不重放账号、不附加运行进程；本表执行阶段只允许离线静态和已有字节回放。
- 反外挂进程本体 `gunnersvr`、`secagent`、`zergsvr` 排除；只记录客户端兼容所需的游戏服/SO边界。
- 服务器模块间加密实现暂缓；面向客户端的帧、Cipher、CRC、Antibot封装、LOGIN/RSA优先。
- 每条结论必须使用 `[静态]`、`[抓包]`、`[真机]`、`[回放]`、`[推断]`、`[未验证]`，被推翻的旧结论加 `[作废]`。
- 每个任务结束必须留下可重跑命令、原始输出、正例/负例或明确的“输入不存在/需要动态证据”说明。

---

## 执行模式、范围与完成含义（优先于通用skill模板）

- 本表由所有者要求用于换会话接续。选择 **executing-plans，同一代理逐项执行**；未经用户明确要求，不创建teammates、不使用Agent Teams、不另开worktree搬动证据。
- 工作区固定 `G:\Game\DNF_ACT1\DFLegacy`。记号 `$B=.superpowers/sdd/2026-10-05-gameplay-evidence-program`，`$W=$B/work`，`$O=$W/out`。本表Files中的短脚本名均位于`$W`，报告名均位于`$O`；不存在第二个work/out目录。
- 本表完成表示 **这轮客户端密码/封装证据包收尾**，不是全官服业务协议钉完、不是所有1171条命令已完成、不是可开始产品重写。
- 已闭合：判据满足且原始输出可复跑。已降级：结论仍不确定，但查证范围、失败原因和获取所需证据的具体条件写清；**不算技术问题解决**。动态阻塞：本表禁止获取的运行实例事实。范围暂缓：所有者已裁决的纯服务端间加密及后续业务单元。
- 不准把失败断言改成skip或删掉来获得PASS；不准把本可离线完成的检查改标动态阻塞。可以按下述硬预算形成有证据的未解决结论并停止。
- 硬预算：Task 1、2、4、5、6各最多一次主执行+一次纠错复跑；若仍失败，记录失败并将**本表签收状态置BLOCKED**，向所有者交付，不开启第三轮。Task 3生产者检索最多两条明确路径，详见该任务；耗尽即形成限定性结论，不保证恢复业务命名。
- 新线索只进入Task 5登记，不新增任务、不修改本表分母。所有者明确改范围才可另开任务。
- 提交只用明确文件列表：`git add -f -- <本任务文件>`、`git diff --cached --check -- <同一列表>`、`git commit -m <说明> -- <同一列表>`；禁止通配暂存、add -A、amend、重写历史。无关既有暂存保持原状。

## 退出判定（全部满足后停止本表，不再继续扩大）

以下条件全部满足时，本表视为“证据收尾完成”：

1. 任务1–6均已勾选并有提交记录。
2. `offline-closure-report.md` 对每一项标注：`已闭合`、`证据不足但已降级`、`动态阻塞` 或 `范围暂缓`。
3. 所有已存客户端面加密流都已被某个验证器处理，或明确列入“无配对/无法解释/需要动态”的清单。
4. NOTI561 类型17、类型19均有：外层结构、解密路径、CRC/再加密回放结果和未闭合字段清单。
5. LOGIN 已有字段消费表、RSA/CTEA验证、尾部生产者状态；不能再把“明文可读”写成“登录成功”。
6. `G_Cipher` singleton、真实LOGIN接受/拒绝、策略实际行值等动态问题被单独列为动态任务，不再以静态取证名义反复追加。
7. 非客户端模块间加密、未采集的dbmw/bridge/辅助服务行为不阻塞本表退出；它们另入后续子计划，不在本表无限扩展。

## 固定硬门（任何一项失败不得签收）

| ID | 必须满足的判据 | 原始依据/允许的有限结论 |
|---|---|---|
| H01 | ELF/SO大小与上述SHA256一致；记录输入流、脚本、解释器/依赖版本 | 来源是所有者确认的容器SO，非运行态VM库哈希 |
| H02 | 5个PLT映射与uint16全域%14通过；pool快照边界不混用 | pool[0]不是singleton |
| H03 | 登录配对11文件对/8唯一内容对；5个不完整前缀显式skip；部署公钥不混用 | 不完整样本不算PASS；分母变化停止核查 |
| H04 | 三VM C流63帧PI通过，三VM S流270帧分型计数，不能把扫描数写成解密数 | 空正文/NOTI1/NOTI561单列；63帧明文CRC通过 |
| H05 | 14算法目标ELF初始化/往返通过；RSA Python与OpenSSL逐字节一致；mouse Python轮函数与ELF一致 | 同二进制往返不算独立官服真值，不要求再写13套算法实现 |
| H06 | 57个NOTI561中54个type17通过SO CRC32+zlib CRC32+再加密；54内层长度、压缩前后CRC通过 | 3个type19嵌套解密与原SO unpack一致，内层7B=length7/kind5/flag0/BinCount5；作用未验证 |
| H07 | 54内层帧按(category,opcode)统计为9组，0走NOTI、1走CMD；S方向category1为CMD应答 | (1,2)=SET_UDP_IP_PORT，(1,4)=SELECT_CHARACTER；不是USERINFO/STAMINA |
| H08 | LOGIN消费表与静态读取序一致；400B消费388B、尾12B逐字节记录；所有未解决项都有有限结论 | 不把无生产端证明的零尾叫padding；不声称成功登录可构造 |

### 固定任务状态表（初始全部待执行验收，不沿用历史“完成”）

| 任务 | 依赖 | 状态 | 产物/退出凭据 |
|---|---|---|---|
| T1 账本纠错 | 无 | 待执行 | H01与所有旧错指向更正 |
| T2 封装收尾 | T1 | 待执行 | H06/H07 |
| T3 LOGIN限定结论 | T1 | 待执行 | H08与两条检索路径结果 |
| T4 总回归 | T2/T3 | 待执行 | H02–H06逐命令退出码 |
| T5 后续边界冻结 | T2/T3 | 待执行 | 不新增范围的业务/动态表 |
| T6 签收并停止 | T1–T5 | 待执行 | CLOSED_WITH_LIMITS 或 BLOCKED |

每行执行后填写实际提交hash和输出链接；不只勾选todo工具。BLOCKED是停止交付，不是完成证据硬门。

---

## Task 1：修正并冻结当前证据账本

**Files:**
- Create: `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/offline-closure-report.md`
- Modify: `.superpowers/sdd/2026-10-05-gameplay-evidence-program/RESUME.md`
- Reference: `offline-master-verification.md`、`client-packet-coverage.md`、`offline-coverage-final.md`

**Produces:** 一张单一状态表，逐项列出来源、命令、证据等级、当前结论和退出状态。

- [ ] Step 1: 从以下项目建立清单，不新增功能范围：目标文件哈希、NOTI1、普通Cipher、CMD1/RSA、LOGIN尾字段、CTEA、入站PI、NOTI561类型17、NOTI561类型19、singleton、restriction实际值、bridge、dbmw、辅助服务、反外挂本体。
- [ ] Step 2: 把历史滞后说法显式标 `[作废]`，尤其是“尚未解出CMD1”“NOTI561只有432B失败序列”“所有离线均已完成”。
- [ ] Step 3: 为每项写四种退出状态之一：`已闭合`、`已降级`、`动态阻塞`、`范围暂缓`；禁止使用“继续看看”作为状态。
- [ ] Step 4: 写入每项唯一复跑命令和原始输出路径。
- [ ] Step 5: `git diff --check` 后只提交本任务报告与RESUME。

**验收：** 新会话只读该报告即可知道哪些问题必须停止、哪些问题允许动态阶段。

---

## Task 2：完成NOTI561客户端封装收尾

**Files:**
- Modify/Create: `decrypt-noti561-fixed.py`、`verify-noti561-envelope.py`
- Create: `work/out/noti561-closure.md`、`work/out/noti561-inner-coverage.tsv`
- Reference: `libantisvrimport.so`、三条VM S流

**Produces:** 类型17/19的统一覆盖表和可复跑报告。

- [ ] Step 1: 固定外层字段：outer length、inner length、type、flag、尾部0/4B残余；给每帧流SHA256。
- [ ] Step 2: 类型17的54帧全部验证：GenKey、Cipher、CRC、再加密、内层frame length、正文CRC/解压；输出category/opcode和正文SHA256。
- [ ] Step 3: 类型19的3帧全部验证：原始SO `DPInterData::unpack` 成功；记录 `length=7`、`kind=5`、`flag=0`、`DPBinCount=5`；不替业务消费者命名。
- [ ] Step 4: 对类型17内层opcode按 CMD/NOTI命名空间分别映射；禁止把 category=1 的 CMD opcode套NOTI名称。
- [ ] Step 5: 将结果分成“密码/封装已闭合”和“业务字段未闭合”，并提交。

**退出：** 类型17和19不再作为“未解密”事项重复出现；剩余只允许列业务消费者语义或动态状态。

---

## Task 3：完成LOGIN离线字段与尾部收尾

**Files:**
- Create: `work/out/login-offline-closure.md`
- Reference: `static-cipher-details.txt`、`login-tail-static.txt`、`verify-login-tail.txt`

**Produces:** LOGIN字段表、消费者表、尾部状态。

- [ ] Step 1: 固定字段：patch、token length/token、两个空字符串、flag、mouse length/data、DataManager比较值、version值、server group、local IP、loginout state。
- [ ] Step 2: 为每个字段记录已有读取/消费地址，不保证找回源码字段名。373处比较值追 `G_CDataManager()->+0x3c0`：仅复核已生成的constructor/init报告与LOGIN比较分支；找不到写入者则保留“比较值”。377处u32追LOGIN内写入`SIG_LOGIN_DATA+0x3a34`；381/383/387处分别核对SetServerGroup、set_local_ip_address、SetLogInOutState的存储目标。最多一轮消费者定位+一轮复核；输出角色不明即可标已降级，不再因字段命名开启全量DB/玩法逆向。`20080418`、`30000401`、`28000201`不能仅凭形状命名版本/日期。
- [ ] Step 3: 12B尾零只做以下两条生产者检索路径，不反复全库猜测：(A) 复读 `CUser::Parsing @0x08647890`、`Cipher::Decrypt @0x0809751e`、`DisPatcher_Login @0x081e8c78` 的现有精确反汇编，列出末次正文读取、总密文长度及是否去填充；(B) 用glob盘点本地是否已有**对应抓包客户端**的发送端二进制/标注库，不能拿2008客户端替代另一代；有身份依据则追CMD1构包入口及其填充调用，未有身份依据则记录缺少对应生产端资产。每条路径最多一轮定位+一轮精确函数复核。终止输出只能是：`生产者证实的填充规则`，或`接收方消费388B、观测尾12B全零，生产者身份/入口未取得，来源未验证`。后者列已降级而非已解决，禁止仅凭388对齐400命名padding。
- [ ] Step 4: 固定mouse CTEA：key、XTEA轮函数、长度8、字符检查边界；输出仅hash，不输出密码明文。
- [ ] Step 5: 对“服务限制”只记录查询、解析和默认分支；DB实际行值标 `动态/DB输入阻塞`。

**退出：** LOGIN的离线字段消费完成；真实接受/拒绝单独标动态阻塞。

---

## Task 4：完成客户端加密总验收（不包含服务端间加密）

**Files:**
- Create: `work/out/client-crypto-closure.md`
- Reference: `offline-master-verification.md`、`all-verify-*.txt`、`all-emulate-boundaries.txt`

**Produces:** 客户端可见密码覆盖矩阵。

- [ ] Step 1: 执行下列冻结命令，每条单独检查退出码。所有脚本须先读，确认没有网络或进程附加。不要并行覆盖同名产物，不要用Select-Object -First截断生产者。

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'
$W="$B/work"; $O="$W/out"
$E="$env:TEMP/dfl-creds/vm-df_game_r"
$SO='G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'
# 每个产物由对应任务先确认可覆盖；按整个文件读取现值。
$jobs=@(
 @{s='audit-cipher-callchain.py'; a=@($E); o='closure-callchain.txt'},
 @{s='verify-cipher-audit.py'; a=@($E); o='closure-selector.txt'},
 @{s='verify-cipher-snapshot.py'; a=@("$O/peek-key2.txt"); o='closure-pool-snapshot.txt'},
 @{s='lock-noti1-evidence.py'; a=@($E); o='closure-noti1.txt'},
 @{s='verify-static-password.py'; a=@(); o='closure-login-rsa.txt'},
 @{s='verify-pi-boundary.py'; a=@(); o='closure-outbound-pi.txt'},
 @{s='verify-inbound-pi.py'; a=@(); o='closure-inbound-pi.txt'},
 @{s='verify-login-tail.py'; a=@(); o='closure-login-tail.txt'},
 @{s='emulate-cipher-boundaries.py'; a=@($E); o='closure-elf-boundaries.txt'},
 @{s='verify-all-cipher-algorithms.py'; a=@($E); o='closure-all-algorithms.txt'},
 @{s='verify-noti561.py'; a=@(); o='closure-noti561-inventory.txt'},
 @{s='verify-noti561-envelope.py'; a=@(); o='closure-noti561-envelope.txt'},
 @{s='decrypt-noti561-fixed.py'; a=@($SO); o='closure-noti561-decrypt.txt'}
)
foreach($j in $jobs){
 $argv=$j.a
 & python "$W/$($j.s)" @argv > "$O/$($j.o)" 2> "$O/$($j.o).stderr"
 $rc=$LASTEXITCODE
 Write-Output "$($j.s) exit=$rc"
 if($rc -ne 0){ throw "Stopped: $($j.s), exit=$rc; preserve stdout/stderr" }
}
```

合并最终验收必须读完整输出、检查覆盖计数；exit0不等于每种帧均解密。长任务用Harness job id收取，禁止用新进程Get-Job判断完成。不得自动新增脚本去测试该列表之外的业务。
- [ ] Step 2: 记录分母：配对流数、唯一内容对、C/S帧数、NOTI561 57帧、类型17 54帧、类型19 3帧、内层opcode组合。
- [ ] Step 3: 把14算法“同一目标ELF往返”与“独立官服已知答案”分开；前者可闭合自洽，后者标 `[未验证]`。
- [ ] Step 4: 明确服务器模块间加密为 `范围暂缓`，但客户端可见头/长度/CRC/密码层不能暂缓。
- [ ] Step 5: 输出错误输入/错误key/字段突变负例清单。

**退出：** 客户端面密码层有分母、有正例、有负例、有动态边界；不能再用“密码层未完成”泛化所有协议。

---

## Task 5：处理“仍可离线但不阻塞”的业务协议清单

**Files:**
- Create: `work/out/client-business-backlog.md`
- Reference: `noti561-inner-coverage.tsv`、`opcode-map.tsv`、`rewrite-decomposition.md`

**Produces:** 有限业务字段补证清单，不扩大为无限逆向。

- [ ] Step 1: 只登记已有样本实际出现的内层opcode：category/opcode、样本数、是否有handler、是否有结构读取器。
- [ ] Step 2: 为每项选择：`本表完成`、`后续玩法单元`、`需要新抓包`、`动态阻塞`。
- [ ] Step 3: 将“所有1171条opcode行为”明确放入后续M1–M10单元，不列为本表未完成。
- [ ] Step 4: 将dbmw、bridge、辅助服务协议列为独立后续计划，不阻塞客户端密码收尾。
- [ ] Step 5: 提交清单，不再自动开启新逆向任务。

**退出：** 本表不因新发现一个未分析系统而自动扩张。

---

## Task 6：最终收尾门

**Files:**
- Modify: `RESUME.md`
- Create: `work/out/evidence-closure-signoff.md`

- [ ] Step 1: 复跑Task 1–5所有命令并记录退出码。
- [ ] Step 2: 检查 `git status`，确认没有把产品代码、测试或服务配置纳入本轮。
- [ ] Step 3: 写三栏最终表：`已钉证据` / `降级或暂缓` / `动态阶段必须验证`。
- [ ] Step 4: 明确签收语句：

```text
本证据收尾表已执行完，结论为CLOSED_WITH_LIMITS。
硬门H01–H08均有原始验证输出；LOGIN生产者等未解决问题按限定性结论保留，不算已解决。
这是当前客户端密码/封装证据包的范围冻结签收，不是全官服协议完成、singleton验证完成或重写授权。
后续业务单元、范围暂缓、动态验证已有固定清单，不再以“继续离线”扩展本表。
```

- [ ] Step 5: 用明确 pathspec 提交，完成后停止本任务表。

**最终停止条件：** Task 6 提交后，不得继续以本表名义新增任务；需要新证据必须新建独立计划和新的范围/验收门。
