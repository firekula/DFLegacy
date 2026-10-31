# NOTI561 客户端封装收尾

状态：T2 已验收；密码/封装判据闭合，业务语义仍有限。证据输入：三条已存 VM S 流与官方 `libantisvrimport.so`（身份见 `offline-closure-report.md`）。本轮 fresh 输出为 `t2-noti561-*.txt`；重跑：

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $SO='G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'
python "$B/work/verify-noti561.py"
python "$B/work/verify-noti561-envelope.py"
python "$B/work/decrypt-noti561-fixed.py" $SO
```

## 覆盖

- [抓包] `verify-noti561.py`：每条已存 VM S 流 90 帧、各 19 个 opcode=561，共 57 wrapper；另一个已存 `streams/30011-46386.bin` 样本为 5 帧、NOTI561_COUNT=0。这里不对样本代际做额外推断。
- [回放] 类型17共54帧：SO GenKey（19 choices 独立 hashlib 对照）、Cipher 解密、外层 CRC、再加密逐字节恢复、随机元数据长度、内层 frame length、正文 CRC 与 opcode 2/9/87 解压均通过。原始逐帧输出：`closure-noti561-decrypt.txt`；汇总：`noti561-inner-coverage.tsv`。
- [回放] 类型19共3帧：嵌套外层解密与原 SO `DPInterData::unpack` 通过，内层固定为 `length=7/kind=5/flag=0/DPBinCount=5`。`verify-noti561-envelope.py` 的残余为 0 或 4 字节，残余语义未赋值。
- [静态]+[回放] 内层按 `(category,opcode)` 统计 9 组：`(0,2),(0,5),(0,9),(0,13),(0,19),(0,21),(0,127),(1,2),(1,4)`。category=1 使用 CMD 命名空间；不得套 NOTI 名称。`(1,2)=SET_UDP_IP_PORT`、`(1,4)=SELECT_CHARACTER` 来自 `opcode-map.tsv`。

## 退出

T2：**已验收**。三条 fresh 验证命令均 exit=0；`t2-noti561-inventory.txt`（62行）完整记录57个opcode=561 wrapper及未配对样本；`t2-noti561-envelope.txt`（61行）完整记录三流19帧结构、type histogram与residual histogram；`t2-noti561-decrypt.txt`（121行）末行明确 `INNER_GAME_DUAL_CRC_AND_DECOMPRESSION=PASS count=54`，并给出9组 `(category,opcode)` 直方图。`noti561-inner-coverage.tsv` 为54行数据加表头。

退出分类：类型17密码/封装 **已闭合**；类型19密码/封装结构 **已闭合**，BinCount=5的业务作用 **已降级**；54个内层业务字段语义 **已降级**。category=1 仅按 `opcode-map.tsv` 的 CMD 命名，不套 NOTI 名称。
