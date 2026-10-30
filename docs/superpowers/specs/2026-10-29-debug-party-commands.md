# ACT1 debug 组队接口

用户明确要求通过 debug 直接请求组队并接受/拒绝邀请，以减少截图和鼠标操作。保留原版服务端流程及 ACT1 客户端编码器；调试 API 只替代 UI 决策。

本地原版 `DNF.exe` SHA256 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5` 为调用依据。`4FE9B0` 是 cdecl 请求函数，玩家菜单action1直接调用。`821DA0` 是邀请窗口thiscall接受回调，调用`4FEBC0`再排队关闭。原拒绝分支位于`821FB2..82202F`内部，不能当成独立函数跳入；本实现通过原始`8B2650`、`8B2450`、`8B24C0`按原顺序构建CMD11的UIDu16/type0/u32零/error100u16，成功后调用原始`7BDC50`关闭。

`CNRDResponsePeerWindow` vtable `A7E2F4`，构造`7C00F0`传class`0x25`到`7BDB30`，后者覆盖构造初值`0x71`。活跃数组在UI manager`+328/32C`，池化数组为另一bank，待删除队列`+EF8/EFC`。窗口`+8`为root、`+10`为manager、`+38`为UID、`+3C`为peer type、`+48`为计时器。只读计时是`8F3F60/40DF60`的镜像值解码，原窗口期限10000毫秒。未知布局、多个同时出现的peer窗口和非party type均不代答。

接口默认关闭，启用须`ExportState=1`及`EnablePartyCommands=1`并重启。文件命令108字节，小端`DLPT/v1`；绑定PID、UID、进程创建时间、request ID、issued/snapshot tick、generation/sequence、actor/map/user/Town context、town/area，以及operation、target UID、window token和invitation serial。最近750毫秒的状态才可发命令；原生执行前再次读真实绑定、目标用户/actor及邀请计时。回执`dispatched`只表示原函数已调用，不代表服务端组队成功。

游戏线程不做磁盘IO或等待锁。工作线程读命令、写状态和结果，固定mailbox用TryAcquireSRWLock；原生调用在现有DebugRender游戏线程。镜像哈希gate之外还验证原函数签名。对原Socket检查初始化/连接、`+15E1E8`发送阻塞标志、builder为空、ring write/read相等且最大21字节包连续，以免重用原编码器时覆盖残留字节。

邀请serial仅在实际窗口/来源/type/计时起点改变时递增，未知或空帧不清除上次指纹。已消费的邀请不会因瞬时读失败获得第二次回复资格。同一DLL对象重新Initialize保留同一进程的消费状态并设置issued tick下界；新的消费者从导出的`consumed_request_id`之后发命令。当前IJL每进程加载一次，不提供活跃邀请期间卸载/重载debug.dll的流程。

验证分层：策略与布局fixture无原生调用；writer核对ACT1黄金字节；runtime测试直接检查生产Socket gate、非支持镜像、错误线程、重初始化；Python检查文件API、进程/窗口身份、过期、重复键、并发锁。实机需先拒绝并确认双方未成队，再重新邀请接受并确认双方名单同为两个UID。之后继续同组完整副本验收，关键画面使用原生PrintScreen原图。
