# VM LOGIN/频道与本次 opcode 静态分析

状态：本轮离线静态/确定性回放分析完成；未连接 VM、未附加进程、未修改产品代码。

## 输入与重跑

输入 pcap SHA256：`968E1F9155EF60F02279A60B3363701953DCA4CAE1253E8A80BAF8DFF1D1FD8A`。

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'; $O="$B/work/out/vm-login-2026-10-06-streams"; $D="$B/work/driver"
python '.superpowers/sdd/2026-10-05-official-login-channel/work/pcap-streams.py' "$B/work/captures/dynamic/vm-login-10013-2026-10-06.pcap" $O --filter-port 7001
python '.superpowers/sdd/2026-10-05-official-login-channel/work/pcap-streams.py' "$B/work/captures/dynamic/vm-login-10013-2026-10-06.pcap" $O --filter-port 10013
```

## 7001 频道正文

[抓包] CHANNEL 11B切帧完整：C→S4帧/92B，S→C4帧/7450B。C→S帧为 `(124,12,47),(124,6,43),(124,10,7222),(124,3,138)`；S→C帧为 `(0,11,43),(0,5,27),(0,9,11),(0,1,11)`。

[回放] 使用 `work/driver/dnfdrive.py` 的 `session_key_from`，从 C→S id12 正文 `[4:20)` 取16B：

- key bytes = `20261007000011\0\0`
- key SHA256 = `a135ec22355e771f8c409091ccef8c6f437f5dc5da65d4d7f4894caea89ca94a`
- id10：正文7211B → zlib 13312B → AES明文13312B；明文 SHA256 `90f99c53ca975f95153f67e0f3443876b88d2cdc79528c446ff95ef5d23024e1`；首行 `[dungeon]`
- id3：正文127B → zlib128B → AES明文128B；明文 SHA256 `7a80822dd4008e1443a925350bb33bc4b15d1d885d9ba91aac22f0d7faaf8bab`；首4B `01 00 00 00`

[静态] 既有驱动器证据明确：id12 status位于正文前4B，密钥是 `[4:20)`；id10/id3采用 `zlib(AES(明文))`，不是旧计划所写的 `[5:21)`或固定 count 前缀。来源：`work/driver/dnfdrive.py` `session_key_from` 与 `decode_channel_blob`。

## 本次游戏面 opcode 静态映射

[抓包] 本次 C→S：CMD 1,174,8,186,38,93,91,2,91；S→C：NOTI 1,183,160,173,16,53,49,186,49,49。

[静态] `opcode-map.tsv` 映射：

|方向/ID|名称|dispatcher/status|
|---|---|---|
|CMD 1|LOGIN|Login/mapped|
|CMD 2|SET_UDP_IP_PORT|SetIP/mapped|
|CMD 8|GET_USERINFO|GetUserInfo/mapped|
|CMD 38|SET_USER_AREA|SetArea/mapped|
|CMD 91|MOVE_TO_GATE|MoveToGate/mapped|
|CMD 93|ENTER_WARROOM|EnterWarRoom/mapped|
|CMD 174|CLIENT_SPEC_STATISTIC|Client_Spec_Statistic/mapped|
|CMD 186|AUCTION_REGIST_ITEM|AuctionRegistItem/mapped|
|NOTI 1|CHANNELINFO|出站专用/no-dispatcher|
|NOTI 16|USERINFO|出站专用/no-dispatcher|
|NOTI 49|REQ_PVP_RANK|出站专用/no-dispatcher|
|NOTI 53|CERA|出站专用/no-dispatcher|
|NOTI 160|POWER_WAR_END|出站专用/no-dispatcher|
|NOTI 173|GAME_OPTION|出站专用/no-dispatcher|
|NOTI 183|AUCTION_NOTIFY_AUCTION_SERVICE|出站专用/no-dispatcher|

本次进入城镇的独立窗口还出现 CMD 4/37/66/70/82/100/142/146；其静态名称已在 `opcode-map.tsv`，其中 CMD142 `GUILD_MEMER_LIST_NEXT` 与 CMD100 `PEER_CONNECT_RESULT` 等为映射结果，不把名称当作字段语义证明。

## handler 静态交叉

[静态] 已有 `catalog_handlers_full.txt` 对部分 CMD dispatcher 给出读取器/调用点：

- `SetIP`：PacketBuf get_byte/get_int 等读取器
- `ChangeTutorialFlag`：LineFunc、get_int、get_byte
- `CallGuildMembers`：角色 guild key/character 信息读取器
- `MoveToGate`：私有商店管理器相关调用
- `PeerConnectResult`：get_byte/get_short
- `EPLPCommand`：状态检查/get_byte
- `TryEnchant`/`PutItemForEnchant`：packet读取器与安全检查
- `ChangeGuildName`：short/byte读取器
- `SDC_Damage_Check`、`SDC_ActiveStatus_Damage_Check`：byte/int读取器

这些是[静态]读取器/调用图证据，不是本次动态会话中的服务器决策原因，也不等价于完整业务字段规格。NOTI 没有服务端 dispatcher；它们是客户端可见出站命名空间。

## 与 singleton/restriction 的关系

- [静态] `G_Cipher` singleton 槽位与 pool[0] 已在既有报告中分离；本次频道 key 解密成功不能证明运行实例 singleton 的地址值相等。
- [静态] restriction 查询/解析/default 分支已有报告；本次成功 LOGIN 不能推出实际 DB 行值。
- [真机] 本次游戏面首序列出现 CMD1(413) 后服务端 CMD1(95)，支持 LOGIN 被接受；这不替代上述动态内存/DB证据。

## 结论

本次新抓包的频道密钥提取、id10/id3解密和本次出现 opcode 的静态映射已完成。剩余 singleton 实值、restriction 实际行值、失败边界与业务字段语义，不由本次静态分析闭合。
