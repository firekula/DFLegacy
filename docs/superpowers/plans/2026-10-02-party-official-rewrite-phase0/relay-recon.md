# ⑤ 跨端 UDP 中继勘察（官服 `df_relay_r` / `df_stun_r`）

对象：`official/template/neople/relay/df_relay_r`（ELF32、**418,098 B**、**含完整 `.symtab`**：2,733 个符号 / 2,207 个 FUNC）、
`official/template/neople/stun/df_stun_r`（131,126 B）、配置 `relay/cfg/relay_200.cfg`、`stun/cfg/udp_server.cfg`、启动脚本 `init/run/start_relay.sh`。
本报告只记**静态**结论；动态（真机）部分明确标 `待真机`。

## 1. 结论速览

| 问题 | 结论 | 类别 |
| --- | --- | --- |
| relay 是"纯字节管道"吗？ | **不是**。它取包后经 handler 表分派，并按 `getACCID()` 索引用户 | `[静态]` |
| relay 的配对结构 | 两张 `hash_map<uint32, TCPUser*>` / `hash_map<uint32, UDPUser*>`（键 = uint32，几乎必然是 ACCID） | `[静态]` |
| 有没有服务端到服务端的转发 | 有：`RelayServiceApp::UDPHandlerS2S::dispatch(char*, int, int)` | `[静态]` |
| 网络模型 | `EpollReactor<TCPUser, TCPSocket, TCPUser>::handleEvents` + Accept/TCP/UDP 三个 `*Thread::loop` | `[静态]` |
| 端口/容量/认证 | `max_client=1000`、`this_tcp_port`/`this_udp_port`（部署模板替换）、`this_pvp_channel=1`；`[aradauth]` 段指向监控进程 ⇒ `RelayService::setAuthenticated(uint)` | `[静态]` |
| 客户端的游戏数据报是否走它 | **未证明**（真机 UDP 审计至今 `mt=0`） | `待真机` |

## 2. 关键符号（全部可用 `official_surface.py --elf <relay> func <名字>` 复核）

```
0x0805d02e  1334  RelayServiceApp::TCPUser::onPacketParse
0x0805e406   144  RelayServiceApp::UDPHandlerS2S::dispatch(char*, int, int)
0x0805c158  1487  EpollReactor<RelayServiceApp::TCPUser,TCPSocket,TCPUser>::handleEvents(uint, TCPUser, uint)
0x08051b2c    74  RelayServiceApp::RelayService::setAuthenticated(uint)
0x08049f5c    13  RelayServiceApp::PortInfo::setTCPPort(int)
0x0805b920    42  RelayServiceApp::TCPHandlerRelay::TCPHandlerRelay()
0x08050d94    25  RelayServiceApp::UserPools::createTCPSocket()
```

`TCPUser::onPacketParse` 的内部调用序列（`calls_in.py`，**注意 `+size` 是十六进制**）显示它按顺序做：

```
TDoubleCircularQueueBuffer（取包/组帧）
get_ms_tickv / TManager
RelayServiceApp::Handlers::getTCPHandlerRelay      ← handler 表分派
RelayServiceApp::TCPUser::getACCID                 ← 取 ACCID 作配对键
RelayServiceApp::Users::setDispatchTime
RelayServiceApp::TCPUser::postDisconnected         ← 断线收尾
```

四条线程入口也在符号里：`TCPAcceptThread::loop`、`TCPThread::loop`、`UDPThread::loop`（`RelayServiceApp::*`）。

## 3. 配置与启动

```ini
# relay/cfg/relay_200.cfg
[server]
max_client=1000
this_ip = 127.0.0.1
this_tcp_port=__RELAY_TCP_PORT__
this_udp_port=__RELAY_TCP_PORT__
this_pvp_channel=1
[aradauth]
udp_port_to_monitor=__RELAY_TCP_PORT__
monitor_ip=127.0.0.1
monitor_port=__MONITOR_TCP_PORT__
relay_index=200
```

```bash
# init/run/start_relay.sh
exec env LD_PRELOAD=/usr/lib/libjemalloc32.so.2 ./df_relay_r relay_200 run
```

⇒ 端口是**部署时替换**的（模板占位符），所以"官服 relay 用 8101-8300"这个区间说法来自容器编排，不在这份 cfg 里
（本报告不采信那个区间，标 `[未验证]`）。

## 4. 仍未取证（S2 的后续任务）

1. **包格式**：`onPacketParse` 读的是自定义帧（首部字段/长度/opcode 落点）——需要逐指令读，或在 `Handlers::getTCPHandlerRelay` 的 handler 表里反推 opcode 集合。
2. **配对键语义**：`getACCID()` 是"账号 id"还是"角色 id"；UDP 侧如何用同一键找到对端。
3. **S2S 的方向**：`UDPHandlerS2S::dispatch` 是"relay→relay"还是"relay→game"；与 `[aradauth]` 监控进程的关系。
4. **与客户端的关系**：客户端到底把什么发给谁（PD-5006）——只能真机判定。
5. **stun**：`df_stun_r`（131 KB）只读到 `udp_server.cfg` 里三条 `2__SERVER_GROUP__1x`；用途（打洞服务）未取证。

## 5. 对我们的意义（写给 S8）

- 若 PD-5006 的真机结论是"客户端把 `mt` 发给服务端"，那么 S8 要做的不是"实现一个 relay 端口"，而是**在 game 服务端内做转发**
  （把一端的 `mt` 数据报按 ACCID/角色映射投给同实例的另一端）——工作量与风险都远小于复刻 relay 进程。
- 只有当结论指向"客户端期望连 relay 的独立端口"时，才需要按本报告的符号清单去复刻那个进程。
