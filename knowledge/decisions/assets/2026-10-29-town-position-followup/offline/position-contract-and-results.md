# U07 位置专用离线过滤及六窗结果（2026-10-29）

本目录只保存位置体、哈希、限定元数据及离线原始程序取证。没有输出登录/认证或其他协议body，没有主动socket、GUI或进程访问，没有修改产品。最新最终输入是停止后副本，不再依赖live尾变化：

- A: P/closed-right-pulse-v2/A-netdump.log，905425字节，SHA256 E6BA771108883A9C108DB7F04B5DC04B7A22EAE3DB9B117B27E9FDCA4C2D6967。
- B: P/closed-right-pulse-v2/B-netdump.log，911087字节，SHA256 7CACC21A99F8A776E99EC524CA7F2FCF766E4179E00B14B2D039C60D63DAD724。
- P=G:/Game/DNF_ACT1/DFLegacy/.tmp/map-right-pulse-20261029；N=G:/Game/DNF_ACT1/DFLegacy/.tmp/town-position-followup-20261029。
- A UID1/PID8820，B UID2/PID8568，绑定由根代理runtime/SDK证据提供，过滤器不从登录载荷猜身份。

## 过滤契约与使用

PositionFilter.csproj引用既有DFLegacy.Protocol.dll，使用其GameClientCipherState/PacketFrame/Crc32/GamePayloadCipher；对应源码的帧契约已逐项核对。客户端CMD37：10B头(type1/id37/length/CRC32)，body9=sequence:u16、x:i16、y:i16、direction:u8、movement:u16。sequence明文，后7B按每帧rolling cipher推进，CRC覆盖全部9B明文；所有该TCP流客户端帧按序解码来推进状态，但只落CMD37。服务端NOTI22：6B头(type0/id22/length)，9B服务载荷用固定ROL6/XORB5逆变换；第一u16是UID，而非客户端sequence。匹配时比较body[2:9]的7B并校验发送端UID身份，不拿整9B哈希作转发匹配。

按(local endpoint,remote endpoint,direction)组流，7001为本phase实际channel端口。支持TCP分/合包，保留首末贡献socket时间、行号和流字节范围。拒绝截断记录、错误长度和时间倒退；失败不扫描重同步。运行中的半行/半帧只标pending，不当作完整消息。此phase端点未复用；未实现同一四元组重连后的cipher重置，后续复用时需加连接代次，不能直接接着解析。

```powershell
dotnet run --project 'G:\Game\DNF_ACT1\DFLegacy\.tmp\town-position-followup-20261029\offline\PositionFilter.csproj' -c Release -- --input 'G:/Game/DNF_ACT1/DFLegacy/.tmp/map-right-pulse-20261029/closed-right-pulse-v2/A-netdump.log' --output 'G:/Game/DNF_ACT1/DFLegacy/.tmp/town-position-followup-20261029/offline/A-recheck.json' --client A --port 7001 --self-uid 1
python 'G:/Game/DNF_ACT1/DFLegacy/.tmp/town-position-followup-20261029/offline/test_filter.py'
```

六项合成自测全部通过：分/合包、帧首末时间跨度、截断拒绝、错误头不重同步、不完整帧pending、末半行pending；插入的非位置敏感哨兵没有泄入输出。它们验证过滤器，不是产品同步修复测试。最终A/B选中26/31条位置消息；帧、位置CRC、cipher解码失败均0，所有7001方向流pending=0。无原始netdump复制到offline；读取快照的长度和全快照哈希记录在各JSON。

## 六窗明确结果

以每trial utc_start及最后sample.t界定11秒窗口，socket日志时间按+08转换；只配同UID及7B完整一致且窗口内的候选，不按最近时间强配。完整结果：six-window-position-match-closed-final.json。

| 窗口 | CMD37/对端NOTI22 | 末消息坐标/dir/value | 唯一匹配socket日志差 | 8秒至窗口末持续native差(本人-对端) |
|---|---:|---|---:|---|
| A-ground | 0/0 | 无新位置消息 | 不适用 | dx41.03186,dy0 |
| A-direction | 1/1 | seq41,(973,372),5,100 | +4ms | dx-25.42236,dy0 |
| A-destination | 1/1 | seq42,(1083,372),5,100 | +5ms | dx0.29126,dy0 |
| B-ground | 0/0 | 无新位置消息 | 不适用 | dx-0.39648,dy-6.59641 |
| B-direction | 1/1 | seq46,(1191,372),5,100 | +5ms | dx-0.61938,dy-6.59641 |
| B-destination | 1/1 | seq47,(1301,372),5,100 | +3ms | dx0.71863,dy-6.59641 |

四条末消息均只有一个完全一致的接收候选。ground两窗没有产生新位置消息，不能以此称服务丢包，也不能由过滤器判断地面点击是否造成有效移动。六窗不支持此前默认10/s丢尾包候选：实际四个新CMD已全转发且收到。日志时间不是原生应用时间或物理网络延迟。

## 原版接收容差：与六窗逐轴结果相符

原版DNF.exe SHA256重新实算为6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5，和S01相同。native-position-apply.asm保留如下原始指令链：

- 41944B读取协议ID，419474用42A081表按原始ID索引；NOTI22表项=41E878。读出UID、x、y、direction、movement后，先排除本机UID，查user并核actor仍在当前map；41E9B0调用actor虚表+170。
- Town9988DC+170=478F00。可能被actor+3C8所指对象的+1A0标志拒绝；正常路径将direction-1除3来解码两轴方向。direction5对应两轴4/4，调用虚表+174=4798A0。
- 4798A0处理movement值及移动方向预测；4799D6和4799DA只在两轴方向均4时进入停步容差检查。4799F4和479A1A分别cmp逐轴绝对差,0x32（50），条件是严格小于50；小差轴由479A03/479A29将收到目标替换成当前坐标。达到/超过50的轴保留新目标。随后479A31调用+178=478F70写目的地，再进入原生运动控制器。
- 因而它是**逐轴停步容差**，不是收到位置就无条件复制坐标。不能把阈值偷偷改成0或每包强写坐标后宣称恢复原版。

receipt-neighbor-samples.json保留每条接收前后邻近的原生成对采样：A方向消息973/372到达前后，peer目的地保持998/372；A大位移1083/372到达附近，peer目的地由998变1083、x方向由4变1。B方向消息1191/372到达附近，peer目标由1125/379变1191/379；B大位移1301/372到达附近，目标由1191/379变1301/379。横向大差更新目标，纵向7像素小差保留379，与原PE逐轴50规则相符。采样跨度和QPC都保留；这里仍不是hook观察实际branch，不能把相关性说成逐指令执行证明。

## 原版发送缓存：只定位，不强套ground结果

native-position-sender.asm保存479A40..479B65完整函数：479A51比较当前X getter与actor+254缓存；若相等再479A63比较Y与actor+258；两者相同则479A69直接到返回，不构建CMD37。不同才在479AC1构造37，发送后479B48/479B59刷新两缓存。入口虚表9988DC+17C=479A40，X/Y整数getter+84/+88分别绑定407460/407470。故方向改变、浮点微小变化未改变整数坐标时，本函数确有抑制发送机制。但本轮未读+254/+258、未观察函数实际调用，不能把ground无CMD直接归因于该缓存。

## 官方服务端合同

official-position.asm/json从原始官方ELF按符号完整导出，ELF SHA256 8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261：

- DisPatcher_SetPos::read@081C97A6读取short X/short Y/byte方向/short movement；check_error@081C9994只在CUser state=3时放行。
- process@081C98C8经过check_error，再081C9983调用GameWorld::move_position@086C5706。
- move_position排除空user/ghost，调用CUser::set_position@082F0E2A保存x/y/方向；随后构建header(0,22)，按unique UID、x、y、方向、movement原样写2/2/2/1/2字节。做实际area/visibility门控后走Area::send_to_all或CUser::Send。选定的完整处理链没有10/s limiter、定时尾包flush或更改收到的坐标。
- 官方坐标缓存使用uint16，而本地服务和native NOTI消费按i16读同一位宽；本轮坐标均正且小于32768，不构成此差异原因。未外推其他坐标域。

当前可报告的最强结论：本六窗末位置已完整到达，持续小差与原版接收端的逐轴停步容差相符；没有服务端位置丢包根因。若需要最终实际branch确认，应针对41E9B0→478F00→4798A0记录只读执行事件及参数/目标，不修改50阈值，不扩大为所有怪物同步已归因。
