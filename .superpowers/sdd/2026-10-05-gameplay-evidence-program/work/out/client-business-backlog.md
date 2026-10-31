# 客户端业务协议边界清单（本轮冻结）

状态：T5 已验收并冻结；不新增逆向任务。

来源：`noti561-inner-coverage.tsv`、`opcode-map.tsv`、`client-packet-coverage.md`。本轮复核命令：

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $O="$B/work/out"; $rows=Get-Content "$O/noti561-inner-coverage.tsv" | Select-Object -Skip 1; "TSV_DATA_ROWS=$($rows.Count)"; $map=Import-Csv -Delimiter "`t" "$O/opcode-map.tsv"; $map | Where-Object { ($_.kind -eq 'CMD') -and ($_.id -in @('2','4')) } | Select-Object kind,id,name
```

[回放] 复核输出：TSV_DATA_ROWS=54；9组直方图为 `0/2=15, 0/5=3, 0/9=3, 0/13=15, 0/19=6, 0/21=3, 0/127=3, 1/2=3, 1/4=3`。[静态] CMD 2=`SET_UDP_IP_PORT`、CMD 4=`SELECT_CHARACTER`。

| category/opcode | 样本数 | 当前证据 | 处置 |
|---|---:|---|---|
| 0/2 | 15 | inner frame/CRC/解压已回放；业务字段未读 | 后续玩法单元 |
| 0/5 | 3 | inner frame/CRC 已回放；handler/字段需单元确认 | 后续玩法单元 |
| 0/9 | 3 | inner frame/CRC/解压已回放；业务字段未读 | 后续玩法单元 |
| 0/13 | 15 | inner frame/CRC 已回放；业务字段未读 | 后续玩法单元 |
| 0/19 | 6 | inner frame/CRC 已回放；业务字段未读 | 后续玩法单元 |
| 0/21 | 3 | inner frame/CRC 已回放；业务字段未读 | 后续玩法单元 |
| 0/127 | 3 | inner frame/CRC 已回放；业务字段未读 | 后续玩法单元 |
| 1/2 | 3 | category=1 按 CMD；opcode map 名为 SET_UDP_IP_PORT | 后续玩法单元 |
| 1/4 | 3 | category=1 按 CMD；opcode map 名为 SELECT_CHARACTER | 后续玩法单元 |

## 范围冻结

- [静态] 所有1171条 opcode 行为属于后续 M1–M10 单元，不作为本收尾未完成分母。
- dbmw、bridge、辅助服务协议是独立后续计划；不阻塞客户端密码/封装收尾。
- type19 的 BinCount=5 作用、type17 业务字段、handler 语义均属于后续玩法单元或需要新抓包；本文件不自动开启任务。
- `opcode-map.tsv` 是命名来源；category=1 禁止套 NOTI 名称。
