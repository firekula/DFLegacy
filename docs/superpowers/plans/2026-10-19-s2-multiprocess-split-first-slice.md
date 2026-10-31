# S2 第一刀（入口握手族拆分）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把握手族（TCP 2311/8080）从 `DFLegacy.Server` 拆成独立进程 `DFLegacy.Channel`，两进程之间跑一条官方形状的进程间链路，且客户端可见行为逐字节不变。

**Architecture:** 两个新项目 —— `DFLegacy.InterProcess`（链路帧与报文）、`DFLegacy.Channel`（新 exe，只依赖 `DFLegacy.Protocol` + `DFLegacy.InterProcess`，**不得引用 `DFLegacy.Server`**）、以及在既有 `DFLegacy.Protocol` 内新增的握手宿主 `EntranceHandshakeServer`（无 ASP.NET 依赖，靠 `IEntranceSessionSink` 记账）。`DFLegacy.Server` 保留频道玩法族，并在 `Processes.Channel.Enabled=false` 时退化为单体模式。

**Tech Stack:** .NET 10（`net10.0`）、`Microsoft.NET.Sdk.Web` 宿主、`System.Text.Json` 配置、自建 smoke 测试运行器（`tests/DFLegacy.SmokeTests`，`OutputType=Exe`，`Action<bool,string> check` 风格）、Python 3.12（`tools/acceptance`、`protocol/`）。

**Spec:** `docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md`

## Global Constraints

- **不改客户端面协议**：`PacketFrame`（10 B 头 + 载荷首字节=记录数）与 11 字节头同构模型**一行不动**（spec §1.3）。
- **落盘单写者**：`DFLegacy.Channel` 进程内**不得**出现 `JsonGameStore`、不得写 `state.json`、不得引用 `DFLegacy.Server`（spec §2 D4、§4.1）。
- **链路帧固定参数**：`category = 0x7b`、`recordCount = 1`、`includeCrc32 = false`、`size` 含 11 字节头 ⇒ 建帧必须走 `PacketFrame.Create(id, payload, type: 0x7b, recordCount: 1, includeCrc32: false)`（spec §5.1/§5.2）。
- **报文号**：只有 `NOTICE_CHANNEL_SERVER = 4` 复用官方 ID；其它一律 `>= 128`（`REGISTER=128`、`REGISTER_ACK=129`、`STATUS=130`）（spec §5.2）。
- **载荷口径**：字段从帧偏移 11 起 = `PacketFrame.Body[1..]`；`Body[0]` 是记录数（spec §5.2）。
- **端口归属**：`EntrancePorts = {Entrance.Port} ∪ AdditionalPorts \ {Channel.Port}` 归 channel 进程；`{Channel.Port}` 归 server 进程；链路默认 `7004`（spec §4.2/§5.6）。
- **每个任务结束时**：沿用 Task 4 已批准的 managed build gate（原生 vcxproj 不由 dotnet CLI 验证），构建受影响的 managed 工程零警告零错误；全量 smoke 与 `--s2-ipc` 分别通过（单组不包含另一组）；提交用**逐文件 pathspec**，**绝不 `git add -A`**。
- **Commit message 用中文**，一条任务一次提交（或按步骤里写明的小提交）。
- `docs/` 被 `.gitignore:15` 挡住 ⇒ 文档使用 `git add -f -- <明确文件路径>`；本轮普通 add 已跟踪文档虽入 index，仍因忽略父目录返回 1，不能据此猜测暂存结果，须检查 index。

---

> 恢复阅读：Task 1–4 的示例属于执行前历史，实际接口以当前源码和最新 handoff 为准；Task 5 使用本次修订契约，Task 6–11 草案需进入对应波时刷新。

## File Structure（先锁死分工）

| 文件 | 职责 | 新/改 |
|---|---|---|
| `src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj` | 链路库项目 | 新 |
| `src/DFLegacy.InterProcess/InterProcessMessageIds.cs` | 报文号 + category 常量 + 链路协议版本 | 新 |
| `src/DFLegacy.InterProcess/InterProcessPayloads.cs` | 4 种载荷的 record + 编解码 | 新 |
| `src/DFLegacy.InterProcess/InterProcessLink.cs` | 帧包装（建帧/校验 category） | 新 |
| `src/DFLegacy.InterProcess/InterProcessLinkClient.cs` | game 侧：拨号/注册/心跳/超时/重连 + 可读状态 | 新 |
| `src/DFLegacy.InterProcess/InterProcessLinkServer.cs` | channel 侧：接受/校验/ACK/收 STATUS + 可读状态 | 新 |
| `src/DFLegacy.Protocol/EntrancePortFamilies.cs` | 纯函数：端口按族切分 | 新 |
| `src/DFLegacy.Protocol/EntranceDialHost.cs` | 已提交的拨号策略；历史镜像来源含未提交补丁，见 Task 5 恢复交接 | 新 |
| `src/DFLegacy.Protocol/EntranceSessionSink.cs` | `IEntranceSessionSink` / `IEntranceSession` 抽象 | 新 |
| `src/DFLegacy.Protocol/EntranceHandshakeServer.cs` | 握手族监听 + 读循环 + 派发 + 记账（无 ASP.NET 依赖） | 新 |
| `src/DFLegacy.Channel/DFLegacy.Channel.csproj` | 新 exe | 新 |
| `src/DFLegacy.Channel/ChannelOptions.cs` | 窄配置模型（只读它需要的键，不与 `ServerOptions` 合并） | 新 |
| `src/DFLegacy.Channel/ChannelScriptSource.cs` | 读 `etc/channel_info.etc`（PVF 或目录） | 新 |
| `src/DFLegacy.Channel/Program.cs` | 组装：配置 → 内容 → 握手宿主 + 链路服务端 | 新 |
| `src/DFLegacy.Server/ProcessOptions.cs` | `Processes` 配置段 + 校验 | 新 |
| `src/DFLegacy.Server/ServerOptions.cs` | 增加 `Processes` 属性 | 改 |
| `src/DFLegacy.Server/EntranceHandshakeService.cs` | 把 `EntranceHandshakeServer` 托管进 game 进程（单体模式用） | 新 |
| `src/DFLegacy.Server/RuntimeEntranceSink.cs` | `IEntranceSessionSink` → `RuntimeState` 适配 | 新 |
| `src/DFLegacy.Server/EntranceService.cs` | Task 4 已改为只绑 `Channel.Port`；其旧 LoadContent 留存，不在 Task 5 改 | 改（小） |
| `src/DFLegacy.Server/Program.cs` | 注册 hosted service、`/api/status` 增字段、链路客户端 | 改 |
| `server.json` | 增加 `Processes` 段 | 改 |
| `tests/DFLegacy.SmokeTests/InterProcessCodecSmokeTests.cs` | 帧/载荷字节断言 | 新 |
| `tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs` | 端口分族 + 握手宿主行为 | 新 |
| `tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs` | Tier 1 链路断言（真 socket） | 新 |
| `tests/DFLegacy.SmokeTests/Program.cs` | 注册 `--s2-ipc` 组 | 改 |
| `protocol/map/s2s-map.tsv` | 进程间报文表（S1 契约扩展） | 新 |
| `protocol/importer.py`、`protocol/verify.py`、`protocol/manifest.json` | 支持 `kind=S2S` + 新文件 + 总数 | 改 |
| `tools/acceptance/model.py`、`runner.py`、`cli.py`、`verdict.py` | 多进程编排与归因 | 改 |
| `tools/acceptance/scenarios/handshake-login-town.yaml` | 本刀的门场景（握手→登录→进城） | 新 |

---

## Task 1: `DFLegacy.InterProcess` 库 + 帧与载荷编解码

**Files:**
- Create: `src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj`
- Create: `src/DFLegacy.InterProcess/InterProcessMessageIds.cs`
- Create: `src/DFLegacy.InterProcess/InterProcessPayloads.cs`
- Create: `src/DFLegacy.InterProcess/InterProcessLink.cs`
- Modify: `DFLegacy.Emulator.slnx`
- Modify: `tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj`
- Create: `tests/DFLegacy.SmokeTests/InterProcessCodecSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`

**Interfaces:**
- Consumes: `DFLegacy.Protocol.PacketFrame`（`Create(byte protocolId, ReadOnlySpan<byte> payload, byte type, byte recordCount, bool includeCrc32)`、`Encode()`、`Body`、`Type`、`ProtocolId`）。
- Produces:
  - `DFLegacy.InterProcess.InterProcessMessageIds`：`NoticeChannelServer=4`、`Register=128`、`RegisterAck=129`、`Status=130`、`LinkCategory=0x7b`、`LinkProtocolVersion=2`。
  - `RegisterPayload(ushort ProtocolVersion, uint GcNo, uint ChannelNo, uint MaximumUsers, uint CurrentUsers, string AdvertisedHost, uint GamePort)` + `Encode()/TryDecode`（38 B）。
  - `RegisterAckPayload(ushort ProtocolVersion, uint Accepted, string ChannelName, uint ChannelPort)`（30 B）。
  - `StatusPayload(ulong UptimeMs, uint ActiveSessions, uint LoggedInUsers, ulong TotalConnections, ulong TotalPackets)`（32 B）。
  - `NoticeChannelServerPayload(int ServerGroup, string AdvertisedHost, int Port)`（24 B）。
  - `InterProcessLink.Create(byte messageId, ReadOnlySpan<byte> payload)`、`InterProcessLink.IsLinkFrame(PacketFrame)`、`InterProcessLink.TryGetMessageId(PacketFrame, out byte)`。

- [ ] **Step 1: 建项目并挂进解决方案**

`src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../DFLegacy.Protocol/DFLegacy.Protocol.csproj" />
  </ItemGroup>
</Project>
```

`DFLegacy.Emulator.slnx` 在 `src/DFLegacy.Protocol/...` 那行之后插入：

```xml
  <Project Path="src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj" />
```

`tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj` 的 `ItemGroup` 内追加：

```xml
    <ProjectReference Include="../../src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj" />
```

- [ ] **Step 2: 写失败测试（字节级）**

`tests/DFLegacy.SmokeTests/InterProcessCodecSmokeTests.cs`：

```csharp
using DFLegacy.InterProcess;
using DFLegacy.Protocol;

internal static class InterProcessCodecSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var register = new RegisterPayload(
            ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion,
            GcNo: 3011,
            ChannelNo: 11,
            MaximumUsers: 100,
            CurrentUsers: 1,
            AdvertisedHost: "127.0.0.1",
            GamePort: 7001);
        var frame = InterProcessLink.Create(InterProcessMessageIds.Register, register.Encode());

        check(Convert.ToHexString(frame.Encode()) ==
              "7B803100000000000000" + "01" + "0200" + "C30B0000" + "0B000000" +
              "64000000" + "01000000" + "3132372E302E302E31" + "00000000000000" + "591B0000",
            "REGISTER frame is 49 bytes: category 0x7b, id 128, size 0x31, crc 0, ack 1");
        check(frame.Type == InterProcessMessageIds.LinkCategory
                && frame.ProtocolId == InterProcessMessageIds.Register
                && frame.TotalLength == 49
                && frame.Body.Length == 39
                && frame.RecordCount == 1,
            "REGISTER header fields match the 11-byte model (size includes header)");
        check(InterProcessLink.TryGetMessageId(frame, out var id) && id == InterProcessMessageIds.Register,
            "link frame decodes back to its message id");

        var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
        check(ack.Encode().Length == 30, "REGISTER_ACK payload is 30 bytes");
        var status = new StatusPayload(1, 2, 3, 4, 5);
        check(status.Encode().Length == 32, "STATUS payload is 32 bytes");
        var notice = new NoticeChannelServerPayload(1, "127.0.0.1", 2311);
        check(notice.Encode().Length == 24, "NOTICE_CHANNEL_SERVER payload is 24 bytes");

        var wrongCategory = new PacketFrame(0x00, InterProcessMessageIds.Register, 0, [1, 2, 3]);
        check(!InterProcessLink.IsLinkFrame(wrongCategory),
            "a frame from another link (category 0x00) is not accepted as a link frame");
    }
}
```

`tests/DFLegacy.SmokeTests/Program.cs` 在第一个 `if (args.Contains("--party-card"))` **之前**插入：

```csharp
if (args.Contains("--s2-ipc"))
{
    InterProcessCodecSmokeTests.Run(Check);
    foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
    Console.WriteLine($"S2 IPC smoke: {failures.Count} failures");
    Environment.Exit(failures.Count > 0 ? 1 : 0);
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`The type or namespace name 'InterProcess' does not exist`

- [ ] **Step 4: 写实现**

`src/DFLegacy.InterProcess/InterProcessMessageIds.cs`：

```csharp
namespace DFLegacy.InterProcess;

/// <summary>
/// 进程间链路的报文号与常量。只有 NoticeChannelServer 复用官方 ID（其载荷布局已有抓包双证）；
/// 其余走 >= 128 的私有号段，避免与官方 1..14 的通道服注册表相撞。
/// 证据：docs/superpowers/sdd/2026-10-05-official-login-channel/dual-evidence.md:88-91,229
/// </summary>
public static class InterProcessMessageIds
{
    /// <summary>官方 channel↔bridge 链路的 category 字节，实测 0x7b。</summary>
    public const byte LinkCategory = 0x7b;

    /// <summary>本链路自己的协议版本。任一侧不匹配时 REGISTER_ACK.accepted 回 0。</summary>
    public const ushort LinkProtocolVersion = 2;

    public const byte NoticeChannelServer = 4;
    public const byte Register = 128;
    public const byte RegisterAck = 129;
    public const byte Status = 130;
}
```

`src/DFLegacy.InterProcess/InterProcessPayloads.cs`：

```csharp
using System.Buffers.Binary;
using System.Text;

namespace DFLegacy.InterProcess;

/// <summary>REGISTER：game → channel。38 B。</summary>
public sealed record RegisterPayload(
    ushort ProtocolVersion,
    uint GcNo,
    uint ChannelNo,
    uint MaximumUsers,
    uint CurrentUsers,
    string AdvertisedHost,
    uint GamePort)
{
    public const int Length = 38;
    public const int HostWidth = 16;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(2, 4), GcNo);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(6, 4), ChannelNo);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(10, 4), MaximumUsers);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(14, 4), CurrentUsers);
        AsciiFixed.Write(buffer.AsSpan(18, HostWidth), AdvertisedHost);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(34, 4), GamePort);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RegisterPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new RegisterPayload(
            BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(6, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(10, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(14, 4)),
            AsciiFixed.Read(payload.Slice(18, HostWidth)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(34, 4)));
        return true;
    }
}

/// <summary>REGISTER_ACK：channel → game。30 B。</summary>
public sealed record RegisterAckPayload(
    ushort ProtocolVersion,
    uint Accepted,
    string ChannelName,
    uint ChannelPort)
{
    public const int Length = 30;
    public const int NameWidth = 20;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(2, 4), Accepted);
        AsciiFixed.Write(buffer.AsSpan(6, NameWidth), ChannelName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(26, 4), ChannelPort);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RegisterAckPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new RegisterAckPayload(
            BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4)),
            AsciiFixed.Read(payload.Slice(6, NameWidth)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(26, 4)));
        return true;
    }
}

/// <summary>STATUS：game → channel，兼心跳。32 B。</summary>
public sealed record StatusPayload(
    ulong UptimeMs,
    uint ActiveSessions,
    uint LoggedInUsers,
    ulong TotalConnections,
    ulong TotalPackets)
{
    public const int Length = 32;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), UptimeMs);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), ActiveSessions);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), LoggedInUsers);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, 8), TotalConnections);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24, 8), TotalPackets);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out StatusPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new StatusPayload(
            BinaryPrimitives.ReadUInt64LittleEndian(payload[..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(24, 8)));
        return true;
    }
}

/// <summary>NOTICE_CHANNEL_SERVER：channel → game。24 B，复用官方 ID 与官方载荷布局。</summary>
public sealed record NoticeChannelServerPayload(int ServerGroup, string AdvertisedHost, int Port)
{
    public const int Length = 24;
    public const int HostWidth = 16;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), ServerGroup);
        AsciiFixed.Write(buffer.AsSpan(4, HostWidth), AdvertisedHost);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), Port);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out NoticeChannelServerPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new NoticeChannelServerPayload(
            BinaryPrimitives.ReadInt32LittleEndian(payload[..4]),
            AsciiFixed.Read(payload.Slice(4, HostWidth)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(20, 4)));
        return true;
    }
}

/// <summary>定宽 ASCII 字段（与官方 tpServerInfo 的 char[N] 同口径：不足补 0，写入前校验长度）。</summary>
internal static class AsciiFixed
{
    public static void Write(Span<byte> destination, string value)
    {
        var encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length >= destination.Length)
        {
            throw new ArgumentException(
                $"ASCII value must be shorter than {destination.Length} bytes: {value}", nameof(value));
        }

        destination.Clear();
        encoded.CopyTo(destination);
    }

    public static string Read(ReadOnlySpan<byte> source)
    {
        var end = source.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? source : source[..end]);
    }
}
```

`src/DFLegacy.InterProcess/InterProcessLink.cs`：

```csharp
using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

/// <summary>
/// 进程间链路的帧包装。帧壳沿用 PacketFrame（10 B 头 + Body[0]=记录数），
/// 线上字节与官方 11 字节头同构：Body[0] 就落在官方 [10] 的位置。
/// 固定参数：category=0x7b，recordCount=1，不带 CRC（spec §5.1）。
/// </summary>
public static class InterProcessLink
{
    public static PacketFrame Create(byte messageId, ReadOnlySpan<byte> payload) =>
        PacketFrame.Create(
            messageId,
            payload,
            type: InterProcessMessageIds.LinkCategory,
            recordCount: 1,
            includeCrc32: false);

    public static bool IsLinkFrame(PacketFrame frame) =>
        frame.Type == InterProcessMessageIds.LinkCategory;

    public static bool TryGetMessageId(PacketFrame frame, out byte messageId)
    {
        messageId = frame.ProtocolId;
        return IsLinkFrame(frame);
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`

- [ ] **Step 6: 提交**

```bash
git add src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj src/DFLegacy.InterProcess/InterProcessMessageIds.cs src/DFLegacy.InterProcess/InterProcessPayloads.cs src/DFLegacy.InterProcess/InterProcessLink.cs DFLegacy.Emulator.slnx tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj tests/DFLegacy.SmokeTests/InterProcessCodecSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: 新增进程间链路库（帧壳 + 4 种载荷编解码）"
```

---

## Task 2: 端口分族纯函数 + `Processes` 配置段

**Files:**
- Create: `src/DFLegacy.Protocol/EntrancePortFamilies.cs`
- Create: `src/DFLegacy.Server/ProcessOptions.cs`
- Modify: `src/DFLegacy.Server/ServerOptions.cs`
- Modify: `server.json`
- Create: `tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`

**Interfaces:**
- Consumes: 无（纯函数 + POCO）。
- Produces:
  - `DFLegacy.Protocol.EntrancePortFamilies.Split(int entrancePort, IEnumerable<int> additionalPorts, int channelPort)` → `(int[] EntrancePorts, int[] ChannelPorts)`；丢弃 `<1 或 >65535`、去重、保序（Entrance 在前）。
  - `DFLegacy.Server.ProcessOptions`：`ChannelProcessOptions Channel`（`Enabled`、`LinkPort=7004`）、`GameProcessOptions Game`（`EntranceBinding=true`）；`ProcessOptions.Validate()` 在两者同时启用时抛 `InvalidOperationException`。

- [ ] **Step 1: 写失败测试**

`tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs`：

```csharp
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class EntranceSplitSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var (entrance, channel) = EntrancePortFamilies.Split(2311, [7001, 8080], 7001);
        check(entrance.SequenceEqual([2311, 8080]) && channel.SequenceEqual([7001]),
            "entrance family = Entrance.Port + AdditionalPorts minus Channel.Port");
        check(EntrancePortFamilies.Split(2311, [7001, 8080], 7001).EntrancePorts
                .All(port => port != 7001),
            "the channel port never lands in the entrance family");
        var (deduped, _) = EntrancePortFamilies.Split(2311, [2311, 0, 70000, 8080, 8080], 7001);
        check(deduped.SequenceEqual([2311, 8080]),
            "duplicates and out-of-range ports are dropped while order is preserved");
        var (single, singleChannel) = EntrancePortFamilies.Split(8080, [], 7001);
        check(single.SequenceEqual([8080]) && singleChannel.SequenceEqual([7001]),
            "defaults (Entrance.Port=8080, no AdditionalPorts) still yield both families");

        var monolithic = new ProcessOptions();
        monolithic.Validate();
        check(!monolithic.Channel.Enabled && monolithic.Game.EntranceBinding
                && monolithic.Channel.LinkPort == 7004,
            "defaults describe today's single-process mode with link port 7004");

        var both = new ProcessOptions();
        both.Channel.Enabled = true;
        both.Game.EntranceBinding = true;
        var threw = false;
        try { both.Validate(); }
        catch (InvalidOperationException) { threw = true; }
        check(threw,
            "enabling the channel process while the game process still binds the entrance ports is rejected");

        var split = new ProcessOptions();
        split.Channel.Enabled = true;
        split.Game.EntranceBinding = false;
        split.Validate();
        check(true, "channel-owned entrance ports with game.EntranceBinding=false is a valid split");
    }
}
```

`Program.cs` 的 `--s2-ipc` 组里加一行（放在 codec 之后）：

```csharp
    EntranceSplitSmokeTests.Run(Check);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`EntrancePortFamilies` / `ProcessOptions` 不存在

- [ ] **Step 3: 写实现**

`src/DFLegacy.Protocol/EntrancePortFamilies.cs`：

```csharp
namespace DFLegacy.Protocol;

/// <summary>
/// 把同一个 Entrance 监听器上的端口按协议族切开：
/// 入口握手族 = {Entrance.Port} ∪ AdditionalPorts 去掉 Channel.Port；频道玩法族 = {Channel.Port}。
/// 与 EntranceService 现行的 isChannelPort 判定（localPort == Channel.Port）**同义**，
/// 只是把隐含规则提成可测试的纯函数 —— 拆分进程时两族必须绑在不同进程里。
/// </summary>
public static class EntrancePortFamilies
{
    public static (int[] EntrancePorts, int[] ChannelPorts) Split(
        int entrancePort,
        IEnumerable<int> additionalPorts,
        int channelPort)
    {
        var all = new[] { entrancePort }.Concat(additionalPorts);
        var valid = all.Where(port => port is > 0 and <= 65535).Distinct().ToArray();
        var entrance = valid.Where(port => port != channelPort).ToArray();
        var channel = valid.Where(port => port == channelPort).ToArray();
        if (channel.Length == 0 && channelPort is > 0 and <= 65535)
        {
            channel = [channelPort];
        }

        return (entrance, channel);
    }
}
```

> 注意：`ChannelPorts` 即使不在监听列表里也要返回，因为频道族由 `Channel.Port` 独立决定（`EntranceService` 用自己的监听器绑它）。

`src/DFLegacy.Server/ProcessOptions.cs`：

```csharp
namespace DFLegacy.Server;

/// <summary>
/// 进程拓扑开关。默认值 = 今天的单体模式（一个进程绑全部端口）。
/// 拆分布局：Channel.Enabled=true 且 Game.EntranceBinding=false。
/// spec: docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md §5.6
/// </summary>
public sealed class ProcessOptions
{
    public ChannelProcessOptions Channel { get; set; } = new();
    public GameProcessOptions Game { get; set; } = new();

    public void Validate()
    {
        if (Channel.Enabled && Game.EntranceBinding)
        {
            throw new InvalidOperationException(
                "Processes.Channel.Enabled=true requires Processes.Game.EntranceBinding=false: " +
                "both processes cannot bind the same entrance ports.");
        }

        if (Channel.LinkPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Processes.Channel.LinkPort must be in 1..65535, got {Channel.LinkPort}.");
        }
    }
}

public sealed class ChannelProcessOptions
{
    public bool Enabled { get; set; }
    public int LinkPort { get; set; } = 7004;
}

public sealed class GameProcessOptions
{
    public bool EntranceBinding { get; set; } = true;
}
```

`src/DFLegacy.Server/ServerOptions.cs` 在 `public AdminOptions Admin { get; set; } = new();` 之后插入一行：

```csharp
    public ProcessOptions Processes { get; set; } = new();
```

`server.json` 在 `"DFLegacy": {` 之后、`"Admin": {` 之前插入：

```json
    "Processes": {
      "//": "进程拓扑开关。默认 = 单体模式（一个进程绑全部端口）。拆分布局：Channel.Enabled=true 且 Game.EntranceBinding=false。Channel.LinkPort 是两进程之间的链路端口。",
      "Channel": {
        "Enabled": false,
        "LinkPort": 7004
      },
      "Game": {
        "EntranceBinding": true
      }
    },
```

`Program.cs`（这是 `src/DFLegacy.Server/Program.cs`）在 `var options = builder.Configuration.GetSection("DFLegacy").Get<ServerOptions>() ?? new ServerOptions();` 之后插入：

```csharp
options.Processes.Validate();
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`

- [ ] **Step 5: 全量 smoke 不回归**

Run: `dotnet run --project tests/DFLegacy.SmokeTests`
Expected: 末尾 `failures` 为 0（若仓库既有失败，先记录基线再继续，不允许新增失败）

- [ ] **Step 6: 提交**

```bash
git add src/DFLegacy.Protocol/EntrancePortFamilies.cs src/DFLegacy.Server/ProcessOptions.cs src/DFLegacy.Server/ServerOptions.cs src/DFLegacy.Server/Program.cs server.json tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: 端口按协议族切分 + Processes 配置段与校验"
```

---

## Task 3: `EntranceHandshakeServer`（无 ASP.NET 依赖的握手宿主）

**Files:**
- Create: `src/DFLegacy.Protocol/EntranceSessionSink.cs`
- Create: `src/DFLegacy.Protocol/EntranceDialHost.cs`
- Create: `src/DFLegacy.Protocol/EntranceHandshakeServer.cs`
- Modify: `tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs`（追加断言）
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`（在 `--s2-ipc` 组里 `await` 一次异步跑）

**Interfaces:**
- Consumes: `EntranceProtocolEngine.Handle(PacketFrame, EntranceSessionState, EntranceContent)`、`EntranceContent(byte[]? ChannelScript, byte[] Metadata)`、`PacketFrame.ReadAsync/WriteAsync`。
- Produces:
  - `IEntranceSessionSink { IEntranceSession Begin(string service, string remote); }`
  - `IEntranceSession : IDisposable { void Received(PacketFrame frame); void Sent(PacketFrame frame); }`
  - `EntranceDialHost.Resolve(string advertisedHost, string entranceHost, string channelHost)` → `string`；`EntranceDialHost.IsDialable(string host)` → `bool`。
  - `EntranceHandshakeServer(IPAddress address, IReadOnlyList<int> ports, Func<EntranceContent> content, IEntranceSessionSink sink, Action<string> log, Action<string, Exception?> warn)`；`Task<int[]> StartAsync(CancellationToken)` 返回实际绑定端口；`Task RunAsync(CancellationToken)`；`void Stop()`。

- [ ] **Step 1: 写失败测试**

在 `EntranceSplitSmokeTests.Run` 末尾追加：

```csharp
        check(EntranceDialHost.Resolve("0.0.0.0", "127.0.0.1", "1.2.3.4") == "127.0.0.1",
            "a wildcard advertised host falls back to Entrance.Host");
        check(EntranceDialHost.Resolve("", "", "1.2.3.4") == "1.2.3.4",
            "with nothing advertised, a concrete Channel.Host is used as the dial target");
        check(EntranceDialHost.Resolve("192.168.1.5", "127.0.0.1", "0.0.0.0") == "192.168.1.5",
            "an advertised gameplay host wins over the entrance host");
```

再把 `EntranceSplitSmokeTests.Run` 的签名保持不变，另加一个异步入口：

```csharp
    public static async Task RunHandshakeServerAsync(Action<bool, string> check)
    {
        var content = new EntranceContent(null, [1, 2, 3, 4]);
        var sink = new RecordingSink();
        var server = new EntranceHandshakeServer(
            System.Net.IPAddress.Loopback,
            [0],
            () => content,
            sink,
            _ => { },
            (_, _) => { });
        var ports = await server.StartAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var run = server.RunAsync(stop.Token);
        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var stream = client.GetStream();
            var request = PacketFrame.Create(11, new byte[32]);
            await request.WriteAsync(stream);
            var reply = await PacketFrame.ReadAsync(stream);
            check(reply is not null && reply.ProtocolId == 12 && reply.Body.Length == 37,
                "protocol 11 (0x0b) is answered by protocol 12 with a 36-byte payload plus record count");
            check(sink.Last is { ReceivedCount: 1, SentCount: 1 },
                "the session sink sees one received and one sent frame");
        }

        stop.Cancel();
        await run;
        server.Stop();
    }

    private sealed class RecordingSink : IEntranceSessionSink
    {
        public RecordingSession? Last { get; private set; }

        public IEntranceSession Begin(string service, string remote) => Last = new RecordingSession();
    }

    private sealed class RecordingSession : IEntranceSession
    {
        public int ReceivedCount { get; private set; }
        public int SentCount { get; private set; }

        public void Received(PacketFrame frame) => ReceivedCount++;

        public void Sent(PacketFrame frame) => SentCount++;

        public void Dispose()
        {
        }
    }
```

`Program.cs` 的 `--s2-ipc` 组里、`EntranceSplitSmokeTests.Run(Check);` 之后插入：

```csharp
    await EntranceSplitSmokeTests.RunHandshakeServerAsync(Check);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`EntranceHandshakeServer` / `EntranceDialHost` / `IEntranceSessionSink` 不存在

- [ ] **Step 3: 写实现**

`src/DFLegacy.Protocol/EntranceSessionSink.cs`：

```csharp
namespace DFLegacy.Protocol;

/// <summary>
/// 握手宿主的记账出口。宿主本身不认识 RuntimeState（那是 server 进程的东西）；
/// 两个进程各自实现本接口：server 进程映射到 RuntimeState，channel 进程映射到自己的计数/日志。
/// </summary>
public interface IEntranceSessionSink
{
    IEntranceSession Begin(string service, string remote);
}

public interface IEntranceSession : IDisposable
{
    void Received(PacketFrame frame);

    void Sent(PacketFrame frame);
}
```

`src/DFLegacy.Protocol/EntranceDialHost.cs`：

```csharp
using System.Net;

namespace DFLegacy.Protocol;

/// <summary>
/// 决定"告诉客户端去拨哪个地址"。Channel.Host 可能是 0.0.0.0 这类绑定地址，绝不能当成拨号目标
/// （客户端拨 0.0.0.0 会失败）。优先级：可拨的广告地址 → Entrance.Host → 可拨的 Channel.Host。
/// 逻辑从 EntranceService.ResolveChannelDialHost 原样抽出，两个进程共用。
/// </summary>
public static class EntranceDialHost
{
    public static string Resolve(string advertisedHost, string entranceHost, string channelHost)
    {
        if (IsDialable(advertisedHost))
        {
            return advertisedHost;
        }

        if (IsDialable(entranceHost))
        {
            return entranceHost;
        }

        return channelHost;
    }

    public static bool IsDialable(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // 逐字镜像 EntranceService.IsDialableHost（EntranceService.cs:17680-17689）：
        // 不是 IP 的主机名算可拨；只有通配地址 0.0.0.0 / :: 不可拨。
        // 广播 255.255.255.255 现存实现**不排除**，这里也不排除 —— Task 4 要把调用点
        // 换成这个方法，语义必须完全一致，否则客户端拨号目标会悄悄变（违反 I1 验收③）。
        return !IPAddress.TryParse(host, out var address)
            || (!IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address));
    }
}
```

`src/DFLegacy.Protocol/EntranceHandshakeServer.cs`：

```csharp
using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Protocol;

/// <summary>
/// 入口握手族的监听与读循环（协议 11/5/9/1 → 12/6/10/3）。
/// 无 ASP.NET 依赖：日志与记账都通过构造参数注入，因此同一份实现可以跑在
/// DFLegacy.Server（单体模式）或 DFLegacy.Channel（拆分模式）里。
/// </summary>
public sealed class EntranceHandshakeServer(
    IPAddress address,
    IReadOnlyList<int> ports,
    Func<EntranceContent> content,
    IEntranceSessionSink sink,
    Action<string> log,
    Action<string, Exception?> warn)
{
    private readonly List<TcpListener> _listeners = [];

    public IReadOnlyList<int> BoundPorts { get; private set; } = [];

    public Task<int[]> StartAsync(CancellationToken cancellationToken)
    {
        foreach (var port in ports.Where(port => port is >= 0 and <= 65535).Distinct())
        {
            var listener = new TcpListener(address, port);
            listener.Start();
            _listeners.Add(listener);
            log($"Entrance listener ready at {address}:{((IPEndPoint)listener.LocalEndpoint).Port}");
        }

        BoundPorts = _listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
        return Task.FromResult(BoundPorts.ToArray());
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(_listeners.Select(listener =>
                AcceptLoopAsync(listener, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        _listeners.Clear();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using (client)
        {
            var session = sink.Begin("entrance", remote);
            try
            {
                using var stream = client.GetStream();
                var state = new EntranceSessionState();
                while (await PacketFrame.ReadAsync(stream, cancellationToken: cancellationToken) is { } request)
                {
                    session.Received(request);
                    var reply = EntranceProtocolEngine.Handle(request, state, content());
                    if (reply is null)
                    {
                        warn(
                            $"No entrance handler for packet type {request.Type}, protocol {request.ProtocolId}; frame retained in logs.",
                            null);
                        continue;
                    }

                    await reply.WriteAsync(stream, cancellationToken);
                    session.Sent(reply);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warn($"Entrance session {remote} ended.", exception);
            }
            finally
            {
                session.Dispose();
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`

- [ ] **Step 5: 提交**

```bash
git add src/DFLegacy.Protocol/EntranceSessionSink.cs src/DFLegacy.Protocol/EntranceDialHost.cs src/DFLegacy.Protocol/EntranceHandshakeServer.cs tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: 握手宿主抽成无 ASP.NET 依赖的 EntranceHandshakeServer"
```

---

## Task 4: 把 game 进程的握手族切到新宿主（I1 完成）

**Files:**
- Create: `src/DFLegacy.Server/RuntimeEntranceSink.cs`
- Create: `src/DFLegacy.Server/EntranceHandshakeService.cs`
- Create: `src/DFLegacy.Protocol/EntranceContentFactory.cs`
- Modify: `src/DFLegacy.Server/EntranceService.cs`（只绑频道族）
- Modify: `src/DFLegacy.Protocol/EntranceHandshakeServer.cs`（会话标签带本地端口；`Stop()` 并发竞态）
- Modify: `src/DFLegacy.Server/Program.cs`（注册 hosted service）

**Interfaces:**
- Consumes: Task 1–3 的全部产出；`RuntimeState.Add/CountPacket/Remove/TracePacket`；已提交的 `EntranceDialHost.Resolve`。
- Produces: `EntranceHandshakeService : BackgroundService`（在 `options.Processes.Channel.Enabled == false` 时启动握手宿主；否则记一条日志并退出）。
- **执行期修订（2026-10-21，所有者已批准）**：`RuntimeEntranceSink` 用 `public`（仓库无 `InternalsVisibleTo` 先例，测试需要可见）；`EntranceContentFactory` 只做组装+缓存，缺 `etc/channel_info.etc` 的 warn 留在 Server 侧取数 lambda（它需要 `ScriptFileSystem.SourceDescription`）；启动门用 `Processes.Channel.Enabled` 而不是 `Game.EntranceBinding`，以保留"两个开关都 false"下的今天行为；**不动 `EntranceService.LoadContent`** —— 拆分后入口分支不可达，且本工作区里它已被未提交的 `ResolveChannelDialHost` 改过，动它会与未提交改动冲突；宿主构造参数带上 `MaximumPacketLength` / `RejectBadCrc32` / `NoDelay`，与旧共用读循环（HEAD `EntranceService.cs:207,4201,4432`）逐项对齐 —— 复审 Important 要求（开 `RejectBadCrc32` 的部署下入口族必须仍然断会话）。

- [ ] **Step 1: 写失败的端口归属测试**

在 `EntranceSplitSmokeTests` 追加：

```csharp
    public static void RunPortOwnership(Action<bool, string> check)
    {
        var monolithic = new ServerOptions();
        var (entrance, channel) = EntrancePortFamilies.Split(
            monolithic.Entrance.Port,
            monolithic.Entrance.AdditionalPorts,
            monolithic.Channel.Port);
        check(entrance.Contains(monolithic.Entrance.Port) && channel.SequenceEqual([monolithic.Channel.Port]),
            "monolithic mode: the handshake host owns every entrance port and the server owns the channel port");
        check(!entrance.Contains(monolithic.Channel.Port),
            "monolithic mode: the channel port is never double-bound by the handshake host");
    }
```

`Program.cs` 的 `--s2-ipc` 组里插入：

```csharp
    EntranceSplitSmokeTests.RunPortOwnership(Check);
```

另外追加三组断言（执行期新增，所有者已批准；三组均已变异验证可红）：

- `EntranceSplitSmokeTests.RunRuntimeSinkAccounting`：`RuntimeEntranceSink.Received` 必须让
  `RuntimeState.TotalPackets` 加 1（换成 `Interlocked.Increment(ref session.ReceivedPackets)` 会让它变红），
  `Sent` 不得污染全局计数，`Dispose` 后 `Sessions()` 为空，`service` 标签原样保留。
- `EntranceSplitSmokeTests.RunContentFactory`：`EntranceContentFactory` 组装的 metadata 必须等于
  **真机录制明文**——652 字节，sha256 `8E059F3A8BEF0DBC34F446007C259A60EE90C4A5DEBCA3981D490B72A0EE3493`，
  录自 `runs/20261018T091951Z-2724238-town-two-clients/*/netdump-*.log`（该次配置：Name="Local Channel"、
  Port=7001、Number=1、Max=100、Cur=1、拨号地址=192.168.3.177；5 个 netdump / 4 个不同会话密钥复现同一明文）；
  脚本原样透传；第二次 `Get()` 返回同一实例且取数 lambda 只被调一次。
- `EntranceSplitSmokeTests.RunHandshakeReplayAsync`：把录制的客户端请求帧（11 → 5 → 9 → 1）喂给新宿主，
  用 12 回包里的 key 解密后比对：proto6 明文 = `00000000` + `dfls-local` + 零；proto10 明文 = 注入的脚本；
  proto3 解密块 656 字节（652 明文 + 4 字节 AES 块填充），sha256 `59FC373C8527F02F3392A88344B93F8EA160B47F309655D1C5B9871317DB08CD`。
  解密顺序是 `DecryptBlocks(Decompress(x))`（`EncryptCompressed = Compress(EncryptBlocks())`）；
  该基线可用 `EncryptCompressed(明文, 录制密钥) == 录制密文` 反证。

- [ ] **Step 2: 跑测试确认通过（这是回归护栏，先立住）**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`（此步只加测试，不需要新实现）

- [ ] **Step 3: 实现适配与托管**

`src/DFLegacy.Server/RuntimeEntranceSink.cs`：

```csharp
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>把握手宿主的记账接到 RuntimeState 上，保留 admin 的 /api/sessions 与 /api/status 口径。</summary>
internal sealed class RuntimeEntranceSink(
    RuntimeState runtime,
    bool tracePackets,
    int hexDumpLimit) : IEntranceSessionSink
{
    public IEntranceSession Begin(string service, string remote) =>
        new Session(runtime.Add(service, remote), runtime, tracePackets, hexDumpLimit);

    private sealed class Session(
        RuntimeSession session,
        RuntimeState runtime,
        bool tracePackets,
        int hexDumpLimit) : IEntranceSession
    {
        public void Received(PacketFrame frame)
        {
            // 必须走 runtime.CountPacket：它同时累加会话级与全局计数（RuntimeState.cs:71-75），
            // 手写 Interlocked.Increment 只会动会话字段，/api/status.TotalPackets 会丢掉全部握手包。
            runtime.CountPacket(session);
            session.LastProtocolId = frame.ProtocolId;
            if (tracePackets)
            {
                runtime.TracePacket(session, "RX", frame, hexDumpLimit);
            }
        }

        public void Sent(PacketFrame frame)
        {
            Interlocked.Increment(ref session.SentPackets);
            if (tracePackets)
            {
                runtime.TracePacket(session, "TX", frame, hexDumpLimit);
            }
        }

        public void Dispose() => runtime.Remove(session.Id);
    }
}
```

`src/DFLegacy.Server/EntranceHandshakeService.cs`：

```csharp
using System.Net;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 单体模式下由本进程托管握手族；拆分模式（Processes.Channel.Enabled=true）不再绑这些端口，
/// 由 DFLegacy.Channel 进程接管。两种模式共用同一个 EntranceHandshakeServer 实现。
/// </summary>
public sealed class EntranceHandshakeService(
    ServerOptions options,
    RuntimeState runtime,
    ScriptFileSystem scripts,
    ILogger<EntranceHandshakeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Processes.Channel.Enabled)
        {
            logger.LogInformation(
                "Entrance handshake ports are owned by the DFLegacy.Channel process; this process will not bind them.");
            return;
        }

        if (!options.Entrance.Enabled)
        {
            logger.LogWarning("Entrance listener is disabled.");
            return;
        }

        var (entrancePorts, _) = EntrancePortFamilies.Split(
            options.Entrance.Port,
            options.Entrance.AdditionalPorts,
            options.Channel.Port);
        var address = IPAddress.Parse(options.Entrance.Host);
        // 频道信息里"客户端该拨哪个地址"必须沿用既有口径（EntranceService.ResolveChannelDialHost 的
        // 优先级），否则拆分前后客户端会被指到不同地址。
        var channelContent = new EntranceChannel(
            options.Channel.Name,
            EntranceDialHost.Resolve(
                options.GameplayDatagram.ResolveAdvertisedHost(),
                options.Entrance.Host,
                options.Channel.Host),
            options.Channel.Port,
            options.Channel.ChannelNumber,
            options.Channel.MaximumUsers,
            options.Channel.CurrentUsers);
        var content = new EntranceContentFactory(
            () => scripts.FileExists("etc/channel_info.etc")
                ? scripts.ReadAllBytes("etc/channel_info.etc")
                : null,
            channelContent);
        var server = new EntranceHandshakeServer(
            address,
            entrancePorts,
            content.Get,
            new RuntimeEntranceSink(runtime, options.EnablePacketTracing, options.HexDumpLimit),
            logger.LogInformation,
            (message, error) => logger.LogWarning(error, "{Message}", message));
        await server.StartAsync(stoppingToken);
        await server.RunAsync(stoppingToken);
    }
}
```

`src/DFLegacy.Protocol/EntranceContentFactory.cs`（新文件，把 `EntranceService.LoadContent` 的内容组装抽出来，两个进程共用）：

```csharp
using System.Net;

namespace DFLegacy.Protocol;

/// <summary>握手内容（ChannelScript.pvf 与频道信息）的组装。缓存一份，与原 LoadContent 行为一致。</summary>
public sealed class EntranceContentFactory(
    Func<byte[]?> readChannelScript,
    EntranceChannel channel)
{
    private readonly object _gate = new();
    private EntranceContent? _content;

    public EntranceContent Get()
    {
        if (_content is not null)
        {
            return _content;
        }

        lock (_gate)
        {
            _content ??= new EntranceContent(readChannelScript(), EntranceMetadata.BuildSingleLocalChannel(channel));
            return _content;
        }
    }
}
```

`src/DFLegacy.Server/EntranceService.cs` 的 `ExecuteAsync` 端口列表改成只含频道族：

```csharp
        var (_, channelPorts) = EntrancePortFamilies.Split(
            options.Entrance.Port,
            options.Entrance.AdditionalPorts,
            options.Channel.Port);
        var ports = channelPorts;
```

`src/DFLegacy.Protocol/EntranceHandshakeServer.cs` 的 `HandleClientAsync`：会话标签从硬编码 `"entrance"`
改为带本地端口（`entrance:{localPort}`，镜像 `EntranceService.cs:221` 的既有口径；不改会让 admin
`/api/sessions` 的两个入口端口不可区分）。同文件 `Stop()` 改为「取 `_listeners` 快照 → 释放锁 → 逐个 Stop」，
消除 `RunAsync` 的 `finally` 与宿主 `Stop()` 并发枚举/清空 `List` 的竞态（实测 `--s2-ipc` 会随机红在 `Stop()`）。

**不要**改 `EntranceService.LoadContent`：拆分后入口分支不可达，且本工作区里它已被未提交的
`ResolveChannelDialHost` 改过，动它会与未提交改动冲突（见上面"执行期修订"）。

`src/DFLegacy.Server/Program.cs` 在 `builder.Services.AddHostedService<EntranceService>();` 之前插入：

```csharp
builder.Services.AddHostedService<EntranceHandshakeService>();
```

- [ ] **Step 4: 构建 + 跑全部 smoke，确认零回归**

Run: `dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj` 与 `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj`
Expected: 各 `0 个警告 / 0 个错误`

> 为什么不是 `dotnet build DFLegacy.Emulator.slnx`（执行期实测，2026-10-21）：原生工程在 dotnet CLI 下必失败
> （`MSB4278: $(VCTargetsPath)\Microsoft.Cpp.Default.props 不存在`，需要 MSBuild.exe），且 slnx 上有
> **他人未提交**的 `DFLegacy.Ijl15.Tests.vcxproj` 条目 —— 与本任务改动无关。

Run: `dotnet run --project tests/DFLegacy.SmokeTests`
Expected: `All DFLegacy protocol smoke tests passed.`（exit 0）

- [ ] **Step 5: 手工验证端口归属（真绑定）**

Run: `$env:DFLegacy__ScriptPvfPath='<repo>\DNF2008\Script.pvf'; dotnet src/DFLegacy.Server/bin/Debug/net10.0/DFLegacy.Server.dll`（另开一个 PowerShell）
（`server.json:58` 的 `ScriptPvfPath` 指向 `D:\DFLegacy\DF2008\Script.pvf`，本机不存在该路径；环境变量覆盖是既有支持的入口，`Program.cs:21-25`。）
Run: `Get-NetTCPConnection -State Listen | Where-Object LocalPort -in 2311,7001,8080 | Select-Object LocalAddress,LocalPort,OwningProcess`
Expected: 2311、7001、8080 全部在监听，且属于**同一个** pid（单体模式行为不变）

- [ ] **Step 6: 提交**

```bash
git add src/DFLegacy.Server/RuntimeEntranceSink.cs src/DFLegacy.Server/EntranceHandshakeService.cs src/DFLegacy.Protocol/EntranceContentFactory.cs src/DFLegacy.Protocol/EntranceHandshakeServer.cs src/DFLegacy.Server/EntranceService.cs src/DFLegacy.Server/Program.cs tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: 握手族改由 EntranceHandshakeService 托管（单体模式端口归属与握手字节不变）"
```

---

## Task 5: `DFLegacy.Channel` 新 exe（I2，待实现与验收）

> 2026-10-21 按 Recon 0.2.1 恢复核验修订。此段取代旧的可直接照抄示例。
> 同日实施并验收：实现与两档进程证据见 `docs/superpowers/handoff/2026-10-21-task-5-complete-handoff.md`；Step 1 的三处收窄见下方「本波 Ruling」。
> 代码基线 `fdde655`，相关工作区快照见交接 §1；下列引用均在该基线/快照核验。
> 只实施这一波。Task 6–11 的代码草案为 `needs-refresh`，不能直接照抄。

**Files:**

- Create: `src/DFLegacy.Channel/DFLegacy.Channel.csproj`
- Create: `src/DFLegacy.Channel/ChannelOptions.cs`
- Create: `src/DFLegacy.Channel/ChannelScriptSource.cs`
- Create: `src/DFLegacy.Channel/ChannelSessionSink.cs`
- Create: `src/DFLegacy.Channel/Program.cs`
- Modify: `DFLegacy.Emulator.slnx`（只加 Channel 工程，保留现有原生工程条目）
- Modify: `tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj`（补 Channel ProjectReference）
- Create: `tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`（只向 `--s2-ipc` 插入本组调用）
- Test-only, if needed: `tests/DFLegacy.SmokeTests/EntranceSplitSmokeTests.cs`（提取复用的握手客户端逻辑，保留既有断言）

### 消费契约（派发前以现场符号和摘要复核）

| ID / 版本 | Task 5 必须遵守 | 来源 |
|---|---|---|
| C5-DEP / 2 | Channel 只引用 Protocol + InterProcess；可直接用 `EntranceHandshakeServer`、`EntranceContentFactory`、`EntranceDialHost`、`EntrancePortFamilies`、`IEntranceSessionSink`、`PvfArchiveReader`。Server 的 `EntranceHandshakeService` / `RuntimeEntranceSink` / `ScriptFileSystem` 仅参考，不能直接引用 | Global Constraints；`EntranceSessionSink.cs:3`；Task 4 的 Server 适配器 |
| C5-READ / 2 | 显式传入 `maximumPacketLength=options.MaximumPacketLength`、`rejectBadCrc32=options.RejectBadCrc32`、`noDelay=true`。NoDelay 是旧行为常量，不发明新配置键；先记收包再拒坏 CRC | `EntranceHandshakeServer.cs:11,132,145`；`EntranceHandshakeService.cs:64`；`ServerOptions.cs:21` |
| C5-CONFIG / 2 | 从同一份 `server.json` 的 DFLegacy section 读角色所需配置，环境变量在 JSON 后覆盖；窄模型不引用 Server。`ChannelOptions.Read(IConfigurationSection)` 可作为映射入口，替代旧 `Read(JsonElement)` 和仅覆盖两个键的示例 | `Program.cs:17-28`；`ServerOptions.cs:3,120` |
| C5-HOST / 2 | 先按 `string.IsNullOrWhiteSpace(AdvertisedHost) ? GameplayDatagram.Host : AdvertisedHost` 取得广告地址，再调 `EntranceDialHost.Resolve(advertised, EntranceHost, ChannelHost)`；不能把缺省广告地址直接当空串跳过 GameplayDatagram.Host | `ServerOptions.cs:42`；`EntranceHandshakeService.cs:54`；已提交 `EntranceDialHost.cs:12` |
| C5-SCRIPT / 2 | `ChannelScriptSource.Read(string pvfPath, string unpackedScriptPath)`：有效 PVF 优先，缺 PVF 才看 `SkillScriptPath` 目录；相对路径按 `AppContext.BaseDirectory`。缺 `etc/channel_info.etc` 返回 null，由调用方 warn；已存在但损坏的 PVF 按既有异常语义处理 | `ScriptFileSystem.cs:29-57,304`；`PvfArchiveReader.cs:22,32`；`EntranceHandshakeService.cs:77` |
| C5-LIFE / 2 | 支持取消与 finally 清理：Start 和 Run 都放在清理范围内，多端口启动失败也 Stop 已建监听；当前共享宿主忽略 Start token 且存在 Start/Stop 窗口，调用方串行管理生命周期，不在启动中并发 Stop | `EntranceHandshakeServer.cs:27,73,120,166`；遗留 D5-START / D5-CANCEL |
| C5-SINK / 2 | 自建 ChannelSessionSink，保留 `ReceivedPackets / SentPackets / TotalSessions / ActiveSessions`；Begin 保留宿主的 `entrance:{port}` 标签，关闭后 active 归零。将来合并 TotalPackets 的口径以收包计数为准，Task 7/8 需刷新 | `EntranceSessionSink.cs:8`；`RuntimeEntranceSink.cs:24`；`EntranceHandshakeServer.cs:136` |
| C5-GATE / 2 | 配置/纯函数测试 + 真实宿主端口与请求路由 + 单体/拆分握手脚本内容对比；两组 smoke 都跑。端口存在或同 pid 本身不证明引擎接线 | spec §6 I2；`EntranceSplitSmokeTests.cs:85,189`；`tests/.../Program.cs:12` |

**窄配置至少覆盖：** Entrance.Enabled/Host/Port/AdditionalPorts；Channel.Name/Host/Port/ChannelNumber/MaximumUsers/CurrentUsers；GameplayDatagram.Host/AdvertisedHost；ScriptPvfPath/SkillScriptPath；MaximumPacketLength/RejectBadCrc32；Processes.Channel.Enabled/LinkPort 与 Game.EntranceBinding。
缺省值逐项对照 `ServerOptions` / `ProcessOptions`，不能只验证装机 JSON 的常量。环境覆盖包括上述被消费键，尤其 CRC、长度、广告地址、PVF/目录与拓扑开关；数组沿用同一 Configuration 提供器合并语义。
拓扑校验镜像 `ProcessOptions.Validate()`：同时 Enabled 与 EntranceBinding 为 true、LinkPort 越界均在绑定前失败。Channel.Enabled=false 或 Entrance.Enabled=false 时 Channel 不绑入口口并说明原因；两拓扑开关都 false 时 Server 的既有单体行为保留。
注意「同一份 `server.json`」的准确含义：**同一份来源**，构建时仓库根的 `server.json` 被 `Content` 复制到**各自工程的输出目录**（`DFLegacy.Server.csproj:20-22` 与本工程同形）。运行时两个进程读的是两份副本；因此**相对**的 `ScriptPvfPath` / `SkillScriptPath` 会按各自的 `AppContext.BaseDirectory` 解析（`ScriptFileSystem.cs:304-307`、`ChannelScriptSource.cs`），拆分部署必须用绝对路径或把两个 exe 放同一目录。该部署约束不在本波修改 `BUILDING.md` 的范围内，记入遗留项 D5-CONFIG-PATH。

### 本波 Ruling（2026-10-21 实施后，带代价）

1. **频道口「走频道路由」收窄为可观察的部分**：本波验证「该口由 game 角色监听 + 接得上 + 握手引擎不在该口服务」。完整玩法协议路由需要构造合法玩法帧或走 admin 记账，超出 Task 5 授权（且伪造正向断言比诚实留 open 更坏）。**代价**：D5-WIRING 的「玩法路由」一半仍然 open，须在 Task 8/11 用真实玩法帧或真机补；不能据此宣称该口玩法接线已验证。
2. **「正常停止退出」收窄为「停止后在超时内退出且端口释放」**：进程级信号驱动的优雅退出（Ctrl+C / stdin EOF）需要额外控制台信号管线或新增 production seam，本波不加。测试里的停止是 `Kill(entireProcessTree)` 后等退出，断言文案已按此改写，不再写成 "when asked to stop"。**代价**：`Program.cs` 的 `Console.CancelKeyPress` → `Cancel()` 分支与 OCE 返回 0 分支在本波零覆盖，记入 D5-START 的 open 范围。
3. **「启动失败也 Stop 已建监听」只由 code review 兜**：进程一退出操作系统就释放监听，进程外无法区分「finally 里 Stop 过」与「进程死了」。测试只断言可观察子集（非零退出、首口确实曾经绑上、退出后端口可用）。**代价**：该 finally 的行为没有任何自动化覆盖，生命周期调整时必须重读 `Program.cs` 的 try/finally。

### Step 0：恢复与隔离（先读，不开始写实现）

- [ ] 读 Global Constraints、本段、handoff 当前视图及 ledger 的最新 Recon 0.2.1 段；现场 `git status` / HEAD 与快照核对，仅刷新变化涉及的 C5 契约。
- [ ] 复核三份共享文件：slnx、smoke csproj、smoke Program；源码入口文件另有他人改动。保留快照，采用与任务相称的隔离；历史不建 worktree 的裁定是已有上下文，不能用 blanket stash 扰动别人的改动。
- [ ] 明确证伪假设：同一客户端输入、角色配置与脚本资产下，入口换宿主不会改变下载脚本、频道元数据、CRC/长度拒绝或端口路由。先确认进程测试能提供隔离配置和资产，缺口作为本任务首个验证步骤。

### Step 1：写精简且有判别力的失败测试

| 测试组 | 最少覆盖 | 不应扩大到 |
|---|---|---|
| 配置/内容（表驱动） | JSON 与环境覆盖、缺省；CRC 开/关、非默认长度；广告地址缺省回退到 GameplayDatagram.Host；PVF/目录/缺失脚本；非法拓扑不绑定 | 穷举无关配置、生产状态写入 |
| 接线/生命周期（真实进程或真实托管入口） | 单体三口同 Server；拆分 2311/8080 属 Channel、7001 属 Server；每个入口口实际发 11 收 12；频道口**接得上但握手引擎不在该口服务**（正向玩法路由不在本波，见下方 Ruling）；二次绑定失败后进程非零退出且不留监听、停止后在超时内退出并释放端口 | 只测 Split 或 pid 列表冒充托管接线 |
| I2 内容对比（同一个测试客户端） | 两模式相同输入与脚本资产：11/5/9/1 → 12/6/10/3；用各会话密钥解密/解压后逐字节比较协议 10 脚本及协议 3 元数据；配置透传在真实装配路径生效 | 仅直接 new 宿主，或沿用 Task 4 的合成脚本测试宣布新进程通过 |

`EntranceProtocolEngine.cs:10` 的 KeyMaterial 每会话随机：记录协议 12 的结构与本会话解码关系；原始密钥/密文自然差异不能误报。上述是 spec §6 I2 的握手脚本内容门槛；spec §7 最终 Tier 2 原始帧对比的例外/归一化仍待 Task 11 明确定义，不能据此宣布 Tier 2 已通过。
每次读回复和等退出都设超时；共用一个进程夹具，避免逐断言重复启动和全量重跑。Task 4 已覆盖的组件测试继续沿用。

### Step 2：实施本波

- [ ] 建 Channel 工程（net10.0、可执行；可沿原计划用 Web SDK 提供日志/配置依赖，但不建 Kestrel/WebApplication），补测试引用。
- [ ] 按 C5-CONFIG/HOST/SCRIPT 读配置和内容；缺失文件日志说明实际来源；按 C5-READ/LIFE/SINK 组装共享宿主、角色日志与计数。
- [ ] 接入 `--s2-ipc`。只实现 Task 5，不提前接链路、admin 合并、protocol 表或 acceptance 平台。

### Step 3：统一完成门槛（已执行；命令与结果见交接 §5）

```powershell
dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc
dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build
```

构建通过 smoke 工程的项目引用覆盖受影响的 managed 依赖，要求零警告零错误；不再把原生 slnx 的 dotnet CLI 失败当成该门槛。
全量可走已申报 `smoke-tests`（固定 Release，工具自身不带 `--no-build`）；该工具不能传 IPC 分组，后者需单独命令并记结果。一次有意义的修订跑受影响组，收尾再跑全量。
进程测试使用临时配置/数据目录和受控端口；若需要真 PVF，当前资产为 `<repo>/DNF2008/Script.pvf`，两进程使用相同 `DFLegacy__ScriptPvfPath`。不直接修改或占用已有客户端/服务端实例；结束后清理自己的进程和覆盖。
I2 的接线与内容对比、非默认读参数透传必须有实际记录才能标完成；设施或基线不足就记录 open 与取数步骤，保持 I2 未验。

### Step 4：审查、下游传播与交接

- [ ] finding 先分 plan-origin/code-origin，修计划后才按新契约修实现；独立复审覆盖生产方和真实消费者接线。
- [ ] Task 7 的启动/取消接入和统计投影、Task 8 的合并口径、Task 11 的对比方法仍为 `needs-refresh`；等各波开始核验，不照抄旧 `CancellationToken.None` 和 Received+Sent 合计。
- [ ] 更新 ledger 中稳定 ID 的当前状态，记录基线/配置/命令/结果位置/证明范围；失败或只覆盖组件不能写成 I2 通过。
- [ ] 逐文件 pathspec 提交本任务文件与计划修订，中文提交信息；共享文件只提交本任务 hunk，核对他人的补丁和未跟踪输入仍在。不要 `git add -A`，不要自动推送。

---

## Task 6: 链路客户端（game 侧）+ Tier 1 前半

> **2026-10-21 刷新（Task 5 完成后进入本波）：下面 Step 1/Step 3 的草案代码有 4 处会直接变红或掩盖缺陷，
> 已被本节「刷新裁定」取代；以裁定 + 仓库里的实现/测试为准，草案代码块保留作历史。**
>
> 刷新裁定（每条带代价，依据：`InterProcessLink.cs:12-27`、`InterProcessMessageIds.cs:10-19`、
> `InterProcessPayloads.cs:6-157`、`PacketFrame.cs:31-70`、`InterProcessCodecSmokeTests.cs:20-57`、spec §5.1/§5.3/§7.1）：
> 1. **`Connected` 的语义是「已注册成功」**（spec §5.3：`accepted=0` ⇒ 不建链），所以草案 Step 1 在服务端发
>    ACK **之前**断言 `client.Connected` 必红。改为 ACK 之后断言，并补一条 ACK 之前 `Connected == false`。
>    **代价**：多一条时序断言。
> 2. **草案所有读包都没有超时**（`PacketFrame.ReadAsync(stream)` 无 token）。按本仓库既有规则
>    （Task 5 交接 §7、`EntranceSplitSmokeTests.cs:461-481`）给每次读与每次等退出加超时，超时必须是具名 FAIL
>    而不是挂住整套 smoke。**代价**：测试代码变长。
> 3. **草案测试起 `RunAsync` 后从不 await，`DisposeAsync` 又立即 `Cancel`+`Dispose` CTS** —— 跑动中的读/写循环
>    会碰到已释放的 CTS（`ObjectDisposedException`）且异常无人观察。改为：`RunAsync` 内部 `using` 持有 CTS 并
>    只在退出时释放；`DisposeAsync` 只请求停止；测试先 Cancel 再 await run 任务。**代价**：`DisposeAsync` 不再
>    自带「等待退出」语义，调用方必须 await 自己的 run 任务。
> 4. **Tier 1 前半没覆盖完**：草案只验了 id/长度，没验 spec §7.1 第 1 项的**固定线上字节**（`[0]=0x7b`、
>    `[1]=0x80`、`[2..5]=49 LE`、`[6..9]=0`、`[10]=1`），也完全没测第 4 项**对端静默 3 个心跳周期 ⇒ 标记 down
>    并重连**。这两项补进本波测试（编解码的精确 hex 已由 `InterProcessCodecSmokeTests.cs:20-29` 覆盖，本波只补
>    **客户端真的把这些字节写到 socket 上**与**静默判死**）。**代价**：本波测试组多两个场景。
> 5. 提交命令改为逐文件 pathspec（仓库规则，禁止 `git add -A`），共享文件 `tests/DFLegacy.SmokeTests/Program.cs`
>    只暂存本任务那一行。

**Files:**
- Create: `src/DFLegacy.InterProcess/InterProcessLinkClient.cs`
- Create: `tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`

**Interfaces:**
- Consumes: Task 1 的载荷与 `InterProcessLink`。
- Produces:
  - `InterProcessLinkClientOptions(string Host, int Port, TimeSpan StatusInterval, int MissedStatusLimit, RegisterPayload Register, Func<StatusPayload> Status)`
  - `InterProcessLinkClient : IAsyncDisposable`：`Task RunAsync(CancellationToken)`、`bool Connected`、`DateTimeOffset? LastStatusAt`、`StatusPayload? LastRemoteStatus`（对端发来的，用于 admin 合并）、`RegisterAckPayload? LastAck`。
  - 事件：`Action? PeerUp`、`Action<string>? PeerDown`（原因文本）。

- [ ] **Step 1: 写失败测试**

`tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs`：

```csharp
using System.Net;
using System.Net.Sockets;
using DFLegacy.InterProcess;
using DFLegacy.Protocol;

internal static class InterProcessLinkSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var register = new RegisterPayload(
            InterProcessMessageIds.LinkProtocolVersion, 3011, 11, 100, 1, "127.0.0.1", 7001);
        await using var client = new InterProcessLinkClient(
            new InterProcessLinkClientOptions(
                "127.0.0.1", port, TimeSpan.FromMilliseconds(50), 3, register,
                () => new StatusPayload(1, 0, 0, 0, 0)));
        var run = client.RunAsync(CancellationToken.None);
        await Task.Delay(200);
        check(client.Connected, "the client connects to a listening link peer and reports Connected");

        var peer = await listener.AcceptTcpClientAsync();
        var stream = peer.GetStream();
        var first = await PacketFrame.ReadAsync(stream);
        check(first is not null
                && first.Type == InterProcessMessageIds.LinkCategory
                && first.ProtocolId == InterProcessMessageIds.Register
                && first.TotalLength == 49,
            "the first frame on the wire is a 49-byte REGISTER with category 0x7b");

        var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
        await InterProcessLink.Create(InterProcessMessageIds.RegisterAck, ack.Encode()).WriteAsync(stream);
        await Task.Delay(200);
        check(client.LastAck is { Accepted: 1 },
            "an accepted REGISTER_ACK marks the peer up with accepted=1");

        var status = await PacketFrame.ReadAsync(stream);
        check(status is not null
                && status.ProtocolId == InterProcessMessageIds.Status
                && status.TotalLength == 43,
            "the steady state sends a 43-byte STATUS (11 + 32) as heartbeat");

        await InterProcessLink.Create(
                InterProcessMessageIds.Status,
                new StatusPayload(1, 2, 0, 5, 6).Encode())
            .WriteAsync(stream);
        await Task.Delay(100);
        check(client.LastRemoteStatus is { ActiveSessions: 2 },
            "the client stores the peer's STATUS, which is what /api/status merges (not the one it sends)");

        var rejectedListener = new TcpListener(IPAddress.Loopback, 0);
        rejectedListener.Start();
        var rejectedPort = ((IPEndPoint)rejectedListener.LocalEndpoint).Port;
        await using var rejected = new InterProcessLinkClient(
            new InterProcessLinkClientOptions(
                "127.0.0.1", rejectedPort, TimeSpan.FromMilliseconds(50), 3, register,
                () => new StatusPayload(1, 0, 0, 0, 0)));
        var rejectedRun = rejected.RunAsync(CancellationToken.None);
        var rejectedPeer = await rejectedListener.AcceptTcpClientAsync();
        var rejectedStream = rejectedPeer.GetStream();
        await PacketFrame.ReadAsync(rejectedStream);
        var badAck = new RegisterAckPayload((ushort)(InterProcessMessageIds.LinkProtocolVersion + 1), 0, "x", 7001);
        await InterProcessLink.Create(InterProcessMessageIds.RegisterAck, badAck.Encode()).WriteAsync(rejectedStream);
        await Task.Delay(200);
        check(!rejected.Connected && rejected.LastAck is { Accepted: 0 },
            "a rejected REGISTER_ACK (version mismatch) leaves the link down without crashing");

        peer.Close();
        rejectedPeer.Close();
        listener.Stop();
        rejectedListener.Stop();
    }
}
```

`Program.cs` 的 `--s2-ipc` 组里插入：

```csharp
    await InterProcessLinkSmokeTests.RunAsync(Check);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`InterProcessLinkClient` 不存在

- [ ] **Step 3: 写实现**

`src/DFLegacy.InterProcess/InterProcessLinkClient.cs`：

```csharp
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

public sealed record InterProcessLinkClientOptions(
    string Host,
    int Port,
    TimeSpan StatusInterval,
    int MissedStatusLimit,
    RegisterPayload Register,
    Func<StatusPayload> Status);

/// <summary>
/// game 侧链路客户端：拨号 → REGISTER → 等 REGISTER_ACK → 周期性 STATUS（兼心跳）
/// → 连续 MissedStatusLimit 次缺失判对端 down 并重连。
/// 失败一律不抛给调用方：客户端服务不因链路问题中断（spec §5.3）。
/// </summary>
public sealed class InterProcessLinkClient(InterProcessLinkClientOptions options) : IAsyncDisposable
{
    private CancellationTokenSource? _stop;

    public bool Connected { get; private set; }

    public RegisterAckPayload? LastAck { get; private set; }

    /// <summary>对端最近一次发来的 STATUS（**不是**我们自己发出去的那条）。</summary>
    public StatusPayload? LastRemoteStatus { get; private set; }

    public DateTimeOffset? LastStatusAt { get; private set; }

    public Action? PeerUp { get; init; }

    public Action<string>? PeerDown { get; init; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(options.Host, options.Port, _stop.Token);
                using var stream = tcp.GetStream();
                await InterProcessLink.Create(InterProcessMessageIds.Register, options.Register.Encode())
                    .WriteAsync(stream, _stop.Token);

                var ackFrame = await PacketFrame.ReadAsync(stream, cancellationToken: _stop.Token);
                if (ackFrame is null
                    || !InterProcessLink.TryGetMessageId(ackFrame, out var ackId)
                    || ackId != InterProcessMessageIds.RegisterAck
                    || !RegisterAckPayload.TryDecode(ackFrame.Body.AsSpan(1), out var ack)
                    || ack is null
                    || ack.Accepted == 0
                    || ack.ProtocolVersion != InterProcessMessageIds.LinkProtocolVersion)
                {
                    LastAck = ack;
                    PeerDown?.Invoke("REGISTER_ACK rejected or malformed");
                    await Task.Delay(options.StatusInterval, _stop.Token);
                    continue;
                }

                LastAck = ack;
                Connected = true;
                PeerUp?.Invoke();
                await RunSteadyStateAsync(stream);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Connected = false;
                PeerDown?.Invoke(exception.Message);
                try
                {
                    await Task.Delay(options.StatusInterval, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        Connected = false;
    }

    /// <summary>
    /// 稳态：写循环周期性发 STATUS（兼心跳），读循环等对端的帧。
    /// 判定"对端 down"只看**收**：连续 MissedStatusLimit 个间隔没收到任何帧才算 down ——
    /// 单向心跳没有回包可等，会把健康链路误判成 down（spec §5.3）。
    /// </summary>
    private async Task RunSteadyStateAsync(NetworkStream stream)
    {
        using var loop = CancellationTokenSource.CreateLinkedTokenSource(_stop!.Token);
        var writer = WriteLoopAsync(stream, loop.Token);
        var reader = ReadLoopAsync(stream, loop.Token);
        var finished = await Task.WhenAny(writer, reader);
        loop.Cancel();
        try
        {
            await Task.WhenAll(writer, reader);
        }
        catch (OperationCanceledException)
        {
        }

        await finished;
    }

    private async Task WriteLoopAsync(NetworkStream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await InterProcessLink.Create(InterProcessMessageIds.Status, options.Status().Encode())
                .WriteAsync(stream, token);
            await Task.Delay(options.StatusInterval, token);
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(options.StatusInterval * options.MissedStatusLimit);
            PacketFrame? frame;
            try
            {
                frame = await PacketFrame.ReadAsync(stream, cancellationToken: timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException(
                    $"peer sent nothing for {options.MissedStatusLimit} status intervals");
            }

            if (frame is null)
            {
                throw new IOException("link peer closed the connection");
            }

            if (InterProcessLink.TryGetMessageId(frame, out var messageId)
                && messageId == InterProcessMessageIds.Status
                && StatusPayload.TryDecode(frame.Body.AsSpan(1), out var status)
                && status is not null)
            {
                LastRemoteStatus = status;
                LastStatusAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`

- [ ] **Step 5: 提交**

```bash
git add src/DFLegacy.InterProcess/InterProcessLinkClient.cs tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: 链路客户端（注册/心跳/超时/重连）+ Tier 1 前半断言"
```

---

## Task 7: 链路服务端（channel 侧）+ 端到端连通

> **2026-10-21 刷新（Task 6 完成后进入本波）：下面 Step 1/Step 3/接线的草案代码有 8 处与现场源码不符或会复现
> 已知缺陷，已被本节「刷新裁定」取代；草案代码块保留作历史。**
>
> 刷新裁定（每条带代价，依据：`src/DFLegacy.Channel/Program.cs:131-186`、`ChannelOptions.cs`、
> `InterProcessLinkClient.cs`（Task 6）、`EntranceHandshakeServer.cs:73-89`（Task 4 已修的 List 竞态）、
> ledger「Task 5（I2）」段与「Task 6（I3 前半）」段）：
> 1. **草案接线用的是不存在的符号**：`options.EntranceHost` / `options.ChannelName` / `options.ChannelPort` /
>    `options.LinkPort`。实际是 `options.Entrance.Host`、`options.Channel.Name`、`options.Channel.Port`、
>    `options.Processes.Channel.LinkPort`（`ChannelOptions.cs`）。**代价**：接线要按现场符号写。
> 2. **草案用 `CancellationToken.None` 起 `RunAsync` 并丢弃返回的 Task**，违反 C5-LIFE/2 与本波自己的门槛要求
>    （Task 5 交接 §3 Ruling 2：不要沿用永久运行示例）。改为：链路服务的 Start/Run 与握手宿主同处一个 try，
>    共用 `lifetime`，`finally` 里 `Cancel()` + 两个 `Stop()`。
> 3. **草案 `StatusPayload.TotalPackets = ReceivedPackets + SentPackets`**，直接违反 ledger 已记录的口径
>    （「不要把 Received+Sent 当 server 的 TotalPackets」）。改为 `TotalPackets = ReceivedPackets`（收包口径）并在
>    代码里写明；`TotalConnections = TotalSessions`、`LoggedInUsers = 0`（channel 进程不做登录）。
> 4. **草案 `PeerUp` 是单个 bool，并在每个会话的进入/退出都置位**：客户端重连时，旧会话的 finally 会把新会话刚
>    置上的 up 打回 down。改为**已注册会话计数**（`PeerUp => 计数 > 0`），并发下只由 `Interlocked` 维护。
> 5. **草案 `Stop()` 无锁 `Clear()` `_listeners`，而 `RunAsync` 正在枚举它** —— 这正是 Task 4 在
>    `EntranceHandshakeServer.cs:73-89` 修过的 "Collection was modified"。必须镜像那份修法：门内取快照 + 清空，
>    门外 Stop，且幂等。**代价**：多一个门对象。
> 6. **草案 `Stop()` 不关闭已建立的会话**，服务端停了但 handler 还挂着。改为跟踪已接受连接，`Stop()` 一并关闭。
> 7. **草案的端到端断言只覆盖 game→channel 方向**（`LastStatus`），且用固定 `Task.Delay(300)`。改为有界轮询，
>    并补 channel→game 方向（客户端 `LastRemoteStatus` 非空）——「端到端连通」必须双向。
> 8. **草案把端到端用例塞进 `InterProcessLinkSmokeTests.RunAsync`「两个 listener 停掉之前」**：Task 6 已把该文件
>    改成三个独立场景方法，没有那个插入点。改为：in-process 服务端场景新增一个方法；**真实两进程连通**放进
>    `ChannelProcessSmokeTests`（它已有进程夹具、端口分配与 env 构造，复用而不是复制一份）。**代价**：Task 7 的
>    测试落在两个文件里。
> 9. 草案 `PacketFrame.ReadAsync` 未设长度上限：链路帧最大 49 B，改为按 4 KiB 上限读，避免对端一个头就把 4 MiB
>    缓冲拉起来。**代价**：多一个常量。
> 10. 本波**不发** `NOTICE_CHANNEL_SERVER`（草案也没发）：第 4 种载荷到 Task 9/10 的 S2S 报文表才接，
>     记为遗留项 D7-NOTICE，别当成已连通。

**Files:**
- Create: `src/DFLegacy.InterProcess/InterProcessLinkServer.cs`
- Modify: `src/DFLegacy.Channel/Program.cs`（把链路服务端纳入同一个 try/finally）
- Modify: `tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs`（in-process 服务端场景）
- Modify: `tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs`（真实两进程连通）

**Interfaces:**
- Consumes: `InterProcessLink`、四种载荷的 `TryDecode`、`InterProcessLinkClient`（Task 6）。
- Produces: `InterProcessLinkServer(IPAddress address, int port, RegisterAckPayload ack, Func<StatusPayload> status, TimeSpan statusInterval, Action<string> log)`：
  `Task<int> StartAsync(CancellationToken)`（返回实际端口，支持 0）、`Task RunAsync(CancellationToken)`、`void Stop()`、
  `bool PeerUp`（= 已注册会话数 > 0）、`RegisterPayload? LastRegister`、`StatusPayload? LastStatus`（game 发来的）、`DateTimeOffset? LastStatusAt`。

> 草案 Step 1/Step 3 的代码块以下不再有效；第 1–10 条裁定 + 上面 Files/Interfaces 是现行契约。
> Step 2/Step 4 的命令按仓库门槛改为：构建 smoke 工程 → `--s2-ipc` → 全量；Step 5 的提交必须逐文件 pathspec。

- [ ] **Step 1: 写失败测试**

在 `InterProcessLinkSmokeTests.RunAsync` 末尾（两个 listener 停掉之前）追加：

```csharp
        var linkServer = new InterProcessLinkServer(
            IPAddress.Loopback,
            0,
            new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001),
            () => new StatusPayload(1, 0, 0, 7, 8),
            TimeSpan.FromMilliseconds(50),
            _ => { });
        var linkPort = await linkServer.StartAsync(CancellationToken.None);
        using var linkStop = new CancellationTokenSource();
        var linkRun = linkServer.RunAsync(linkStop.Token);
        await using var gameSide = new InterProcessLinkClient(
            new InterProcessLinkClientOptions("127.0.0.1", linkPort, TimeSpan.FromMilliseconds(50), 3, register));
        var gameRun = gameSide.RunAsync(CancellationToken.None);
        await Task.Delay(300);
        check(gameSide.Connected && linkServer.PeerUp,
            "client and server agree the link is up after REGISTER/ACK");
        check(linkServer.LastRegister is { GcNo: 3011, ChannelNo: 11, GamePort: 7001 },
            "the server decodes gc_no/channel_no/game port from REGISTER");
        check(linkServer.LastStatus is not null,
            "the server stores the last STATUS payload it received");
        linkStop.Cancel();
        await linkRun;
        linkServer.Stop();
        await gameSide.DisposeAsync();
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`InterProcessLinkServer` 不存在

- [ ] **Step 3: 写实现**

`src/DFLegacy.InterProcess/InterProcessLinkServer.cs`：

```csharp
using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

/// <summary>
/// channel 侧链路服务端：接受 game 的 REGISTER，回 REGISTER_ACK，之后持续接收 STATUS。
/// 只做校验与记账，不碰任何存档（spec §5.4：本刀链路不承载落盘变更）。
/// </summary>
public sealed class InterProcessLinkServer(
    IPAddress address,
    int port,
    RegisterAckPayload ack,
    Func<StatusPayload> status,
    TimeSpan statusInterval,
    Action<string> log)
{
    private readonly List<TcpListener> _listeners = [];

    public bool PeerUp { get; private set; }

    public RegisterPayload? LastRegister { get; private set; }

    public StatusPayload? LastStatus { get; private set; }

    public DateTimeOffset? LastStatusAt { get; private set; }

    public Task<int> StartAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        _listeners.Add(listener);
        var bound = ((IPEndPoint)listener.LocalEndpoint).Port;
        log($"Inter-process link listening at {address}:{bound}");
        return Task.FromResult(bound);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(_listeners.Select(listener => AcceptLoopAsync(listener, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        PeerUp = false;
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        _listeners.Clear();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using (client)
        {
            PeerUp = false;
            try
            {
                using var stream = client.GetStream();
                while (await PacketFrame.ReadAsync(stream, cancellationToken: cancellationToken) is { } frame)
                {
                    if (!InterProcessLink.TryGetMessageId(frame, out var messageId))
                    {
                        continue;
                    }

                    switch (messageId)
                    {
                        case InterProcessMessageIds.Register:
                            if (!RegisterPayload.TryDecode(frame.Body.AsSpan(1), out var register)
                                || register is null)
                            {
                                continue;
                            }

                            LastRegister = register;
                            var accepted = register.ProtocolVersion == InterProcessMessageIds.LinkProtocolVersion;
                            var reply = accepted
                                ? ack
                                : ack with { ProtocolVersion = InterProcessMessageIds.LinkProtocolVersion, Accepted = 0 };
                            await InterProcessLink.Create(InterProcessMessageIds.RegisterAck, reply.Encode())
                                .WriteAsync(stream, cancellationToken);
                            PeerUp = accepted;
                            log(accepted
                                ? $"Game process registered from {remote} (gc_no={register.GcNo}, channel_no={register.ChannelNo})"
                                : $"Game process from {remote} rejected: link protocol {register.ProtocolVersion} != {InterProcessMessageIds.LinkProtocolVersion}");
                            if (accepted)
                            {
                                _ = StatusLoopAsync(stream, cancellationToken);
                            }

                            break;
                        case InterProcessMessageIds.Status:
                            if (StatusPayload.TryDecode(frame.Body.AsSpan(1), out var status) && status is not null)
                            {
                                LastStatus = status;
                                LastStatusAt = DateTimeOffset.UtcNow;
                            }

                            break;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log($"Link peer {remote} dropped: {exception.Message}");
            }
            finally
            {
                PeerUp = false;
            }
        }
    }

    /// <summary>对端注册成功后，本侧也周期性发 STATUS：对方靠"收得到"判活（spec §5.3 双向心跳）。</summary>
    private async Task StatusLoopAsync(NetworkStream stream, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await InterProcessLink.Create(InterProcessMessageIds.Status, status().Encode())
                    .WriteAsync(stream, token);
                await Task.Delay(statusInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            log($"Link status writer stopped: {exception.Message}");
        }
    }
}
```

`src/DFLegacy.Channel/Program.cs` 在 `await server.StartAsync(...)` 之前插入：

```csharp
var link = new InterProcessLinkServer(
    IPAddress.Parse(options.EntranceHost),
    options.LinkPort,
    new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, options.ChannelName, options.ChannelPort),
    () => new StatusPayload(
        (ulong)Environment.TickCount64,
        (uint)sink.ActiveSessions,
        0,
        (ulong)sink.TotalSessions,
        (ulong)(sink.ReceivedPackets + sink.SentPackets)),
    TimeSpan.FromSeconds(1),
    message => Console.WriteLine(message));
await link.StartAsync(CancellationToken.None);
_ = link.RunAsync(CancellationToken.None);
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`（含 4 条 Tier 1 断言：REGISTER hex、长度自洽、`accepted=0` 不建链、3 次心跳缺失判 down）

- [ ] **Step 5: 提交**

```bash
git add src/DFLegacy.InterProcess/InterProcessLinkServer.cs src/DFLegacy.Channel/Program.cs tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs
git commit -m "S2: 链路服务端（注册/ACK/STATUS 记账）+ 端到端连通断言"
```

---

## Task 8: game 进程接入链路 + admin 计数与存活（I4 完成）

> **2026-10-21 刷新（Task 7 完成后进入本波）：草案 Step 3/Step 5 有 4 处与现场不符或不可作证，
> 已被本节「刷新裁定」取代；草案代码块保留作历史。**
>
> 刷新裁定（每条带代价，依据：`InterProcessLinkClient.cs`（Task 6）、`RuntimeState.cs:46-153`、
> `AdminAccessPolicy.cs:92-95`、`AdminAuthService.cs:14-25`、`Program.cs:182,331-355,735-778`）：
> 1. **`ChannelLinkService` 构造少了 `RuntimeState`**：草案 `ExecuteAsync` 用了 `runtime.*` 但构造参数没有它。改为
>    `(ServerOptions options, RuntimeState runtime, ILogger<ChannelLinkService> logger)`。
> 2. **`Status()` 把 `Sessions().Length` 同时塞给 `ActiveSessions` 和 `LoggedInUsers`**：game 不在 RuntimeState 记
>    「已登录角色数」。`LoggedInUsers` 改 0，`ActiveSessions` 用 `Sessions().Length`。
> 3. **`/api/status` 是 Admin 鉴权**（`AdminAccessPolicy.cs:92-95` 兜底 fail-closed）：草案「手工访问」会拿 401。
>    改为自动化：`JsonGameStore` 同进程预种账号 → 起真实 Server（`Admin.Users=[admin]` + 同 `DataPath`，
>    `ApplyBootstrapAdminsAsync` @Program.cs:182 启动提权）→ POST login → Bearer GET status。
> 4. **Step 5 手工验证不算证据**：改为真实两进程（Server split + Channel）端到端，同时覆盖接入链路、双侧存活、
>    `/api/status` 既有字段不回归 + 新增 `channelLink`。用例落在 `ChannelProcessSmokeTests`。

**Files:**
- Create: `src/DFLegacy.Server/StatusProjection.cs`
- Create: `src/DFLegacy.Server/ChannelLinkService.cs`
- Modify: `src/DFLegacy.Server/Program.cs`（注册 hosted service + `/api/status` 加 `channelLink` 字段）
- Create: `tests/DFLegacy.SmokeTests/StatusProjectionSmokeTests.cs`（纯投影）
- Modify: `tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs`（真实两进程：接入链路 + admin 登录 + `/api/status`）
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`（`--s2-ipc` 加 `StatusProjectionSmokeTests.Run`）

**Interfaces:**
- Consumes: `InterProcessLinkClient`（Task 6）；`RuntimeState.TotalConnections/TotalPackets/Sessions()/StartedAt`。
- Produces: `StatusProjection.ChannelLink(...)` → `ChannelLinkStatus` record；`ChannelLinkService : BackgroundService`（构造三参）。

- [ ] **Step 1: 写失败测试**

`tests/DFLegacy.SmokeTests/StatusProjectionSmokeTests.cs`：

```csharp
using DFLegacy.InterProcess;
using DFLegacy.Server;

internal static class StatusProjectionSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var down = StatusProjection.ChannelLink(
            connected: false, lastStatusAt: null, status: null,
            localConnections: 3, localPackets: 7, localSessions: 1);
        check(down is { Connected: false, RemoteSessions: 0 }
                && down.TotalConnections == 3 && down.TotalPackets == 7 && down.ActiveSessions == 1,
            "with the link down the projection equals the local counters (no invented values)");

        var status = new StatusPayload(10, 4, 2, 100, 200);
        var up = StatusProjection.ChannelLink(
            connected: true, lastStatusAt: DateTimeOffset.UnixEpoch, status: status,
            localConnections: 3, localPackets: 7, localSessions: 1);
        check(up is { Connected: true, RemoteSessions: 4 }
                && up.TotalConnections == 103 && up.TotalPackets == 207 && up.ActiveSessions == 5,
            "with the link up the handshake-side counters are added back into the admin totals");
    }
}
```

`Program.cs` 的 `--s2-ipc` 组里插入：

```csharp
    StatusProjectionSmokeTests.Run(Check);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: 编译失败，`StatusProjection` 不存在

- [ ] **Step 3: 写实现**

`src/DFLegacy.Server/StatusProjection.cs`：

```csharp
using DFLegacy.InterProcess;

namespace DFLegacy.Server;

public sealed record ChannelLinkStatus(
    bool Connected,
    DateTimeOffset? LastStatusAt,
    int RemoteSessions,
    long TotalConnections,
    long TotalPackets,
    int ActiveSessions);

/// <summary>
/// 把 game 侧的本地计数与 channel 侧上报的计数合并成 /api/status 的对外口径。
/// 链路断开时**只报本地值**（不发明数字）—— 这是 R1 的处置：让"谁挂了"能被看见。
/// </summary>
public static class StatusProjection
{
    public static ChannelLinkStatus ChannelLink(
        bool connected,
        DateTimeOffset? lastStatusAt,
        StatusPayload? status,
        long localConnections,
        long localPackets,
        int localSessions)
    {
        var remoteSessions = connected && status is not null ? (int)status.ActiveSessions : 0;
        return new ChannelLinkStatus(
            connected,
            lastStatusAt,
            remoteSessions,
            localConnections + (connected && status is not null ? (long)status.TotalConnections : 0),
            localPackets + (connected && status is not null ? (long)status.TotalPackets : 0),
            localSessions + remoteSessions);
    }
}
```

`src/DFLegacy.Server/ChannelLinkService.cs`：

```csharp
using System.Net;
using DFLegacy.InterProcess;

namespace DFLegacy.Server;

/// <summary>game 侧的链路客户端托管。仅在拆分模式（Processes.Channel.Enabled=true）下拨号。</summary>
public sealed class ChannelLinkService(
    ServerOptions options,
    ILogger<ChannelLinkService> logger) : BackgroundService
{
    public InterProcessLinkClient? Client { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Processes.Channel.Enabled)
        {
            return;
        }

        var register = new RegisterPayload(
            InterProcessMessageIds.LinkProtocolVersion,
            GcNo: (uint)(options.Channel.ServerNumber * 1000 + options.Channel.ChannelNumber),
            ChannelNo: (uint)options.Channel.ChannelNumber,
            MaximumUsers: (uint)options.Channel.MaximumUsers,
            CurrentUsers: (uint)options.Channel.CurrentUsers,
            AdvertisedHost: options.GameplayDatagram.ResolveAdvertisedHost(),
            GamePort: (uint)options.Channel.Port);
        var client = new InterProcessLinkClient(new InterProcessLinkClientOptions(
            options.Entrance.Host,
            options.Processes.Channel.LinkPort,
            TimeSpan.FromSeconds(1),
            MissedStatusLimit: 3,
            register,
            () => new StatusPayload(
                (ulong)Environment.TickCount64,
                (uint)runtime.Sessions().Length,
                (uint)runtime.Sessions().Length,
                (ulong)runtime.TotalConnections,
                (ulong)runtime.TotalPackets)))
        {
            PeerUp = () => logger.LogInformation("Inter-process link to the channel process is up"),
            PeerDown = reason => logger.LogWarning("Inter-process link to the channel process is down: {Reason}", reason),
        };
        Client = client;
        await client.RunAsync(stoppingToken);
    }
}
```

`src/DFLegacy.Server/Program.cs`：

1. 在 `builder.Services.AddHostedService<EntranceHandshakeService>();` 之后插入：

```csharp
builder.Services.AddSingleton<ChannelLinkService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ChannelLinkService>());
```

2. `/api/status` 的 lambda 形参里加上 `ChannelLinkService channelLink`，并在返回对象里加字段：

```csharp
        ChannelLink = StatusProjection.ChannelLink(
            channelLink.Client?.Connected ?? false,
            channelLink.Client?.LastStatusAt,
            channelLink.Client?.LastRemoteStatus,
            runtime.TotalConnections,
            runtime.TotalPackets,
            runtime.Sessions().Length),
```

> `TotalConnections`/`TotalPackets` 仍是本地值；合并口径以 `ChannelLink.TotalConnections` 暴露，避免改动既有字段语义（既有字段不回归是 I4 的门槛）。

- [ ] **Step 4: 跑测试确认通过 + 既有字段不回归**

Run: `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`
Expected: `S2 IPC smoke: 0 failures`

Run: `dotnet run --project tests/DFLegacy.SmokeTests`
Expected: 0 failures

- [ ] **Step 5: 手工验证 admin 新字段**

单体模式下启动 server，访问 `http://127.0.0.1:8081/api/status`：

```powershell
Invoke-RestMethod http://127.0.0.1:8081/api/status | ConvertTo-Json -Depth 4
```

Expected: 既有字段（`activeSessions`/`totalConnections`/`totalPackets`）仍在，新增 `channelLink.connected=false`（单体模式不拨号）。

- [ ] **Step 6: 提交**

```bash
git add src/DFLegacy.Server/StatusProjection.cs src/DFLegacy.Server/ChannelLinkService.cs src/DFLegacy.Server/Program.cs tests/DFLegacy.SmokeTests/StatusProjectionSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs
git commit -m "S2: game 进程接入链路并把 channel 侧计数合并进 /api/status"
```

---

## Task 9: `protocol/` 新增 S2S 报文表 + 契约扩展（I5 前半）

> **2026-10-22 刷新（Task 8 完成后进入本波）：草案的「总数 1171→1175、KIND_BASELINES += s2s、6 文件」
> 与现场实现不符，被本节「刷新裁定」取代；下面 Step 2/Step 3 的草案代码块保留作历史，不再照抄。**
>
> 刷新裁定（每条带代价，依据：`verify.py:39-66,154-187,190-214`、`render.py:10,102-118`、
> `importer.py:16,93-97`、`test_verify.py:48-71,166-176,281-296`）：
> 1. **S2S 独立成 `map/s2s-map.tsv`，不并入 `opcode-map.tsv`，`opcodes_total` 保持 1171**。草案说
>    「1171+4=1175、KIND_BASELINES += s2s」，但 `_check_baselines`（`verify.py:156`）与 `build_manifest`
>    （`render.py:102`）都**只从 `opcode-map.tsv` 计数**。把 S2S 混进去会：①污染官方六档/status/ext_mark
>    统计（S2S 的 `ext_mark="-"` 不在 `MARKS` 枚举、`tier=T4` 不是逆向深度）；②`importer.import_map`
>    （`importer.py:93`，从 `.superpowers` 原始产物重建 opcode-map）下次会把无原始来源的 S2S **静默删掉**；
>    ③`_check_internal`（`verify.py:198`）要求 opcode-map==ledger 对称，S2S 无官方 opcode 账。
>    **代价**：`verify`/`render` 各加一段「读 s2s-map.tsv 单独计数」，manifest 多一个 `s2s` 基线键。
> 2. **加第 6 个受跟踪文件的完整下游清单**（漏一处 verify 就红）：`render.TRACKED`（`render.py:10`）与
>    `verify.TRACKED_FILES`（`verify.py:39`）各 += `map/s2s-map.tsv`；`verify.KEY_COLUMNS`（`:55`）加
>    `"map/s2s-map.tsv": ("kind","id")`、`EXPECTED_COLUMNS`（`:61`）加 `"map/s2s-map.tsv": MAP_COLUMNS`；
>    `_check_baselines` 加**独立** `s2s` 计数（读 s2s-map.tsv，**不**混进六档/status/mark 那个 `rows` 循环）；
>    `render.build_manifest` 加 `s2s` 基线；`test_verify.py` 的 `_build` 夹具（`:48`）补 s2s-map.tsv、
>    `test_manifest_must_list_exactly_the_five_tracked_files`（`:170`）语义改为「6 文件」、
>    `test_baseline_total_is_pinned_to_1171`（`:281`）**不变**（1171 仍是官方硬编码基线）。
>    **2026-10-22 实施回填（裁定初稿漏项）**：下游清单还需 `protocol/tests/test_render.py` —— 其 `_base`
>    夹具（`:12`）调 `render.render_all`，`TRACKED` 扩到 6 文件后 `build_manifest` 对缺失产物抛
>    `FileNotFoundError`，且 `test_manifest_has_no_timestamp_and_keys_sorted`（`:35`）断言 `len(files)==5`；
>    不修它 pytest 首跑 6 红（实测）。已随 `6aeec02` 修复（夹具补 s2s-map.tsv + 断言 5→6）。
> 3. **S2S 不进 importer 流水线**：s2s-map.tsv 是人工维护台账（自选语义、无官方 `.rodata`），
>    `import_map` 不碰它、`_KIND_ORDER` 不加 S2S（它只管 opcode-map 排序）。
> 4. **NOTICE_CHANNEL_SERVER（D7-NOTICE）本波只登记、不接线**：4 条 S2S 报文（4/128/129/130）进台账，
>    服务端是否真发 NOTICE 归 D7-NOTICE（Task 11 真机时定）。`(kind,id)` 主键下 S2S id=4 与 CMD/NOTI 4 号不冲突。
>
> 门槛（沿用仓库 gate）：`python -m protocol.s1 render` 再 `python -m protocol.s1 verify`（仍 **5/5 PASS**，但含 s2s 基线）；
> `python -m pytest protocol -q` 全绿（含上述 2 个改过的夹具）。`protocol/README.md` 的「5 文件/1171 条」改成
> 「6 文件 / 1171 官方 + 4 S2S」。

**Files:**
- Create: `protocol/map/s2s-map.tsv`
- Modify: `protocol/importer.py`、`protocol/verify.py`、`protocol/manifest.json`
- Modify: `protocol/README.md`（登记新 kind）

**Interfaces:**
- Consumes: 现有 `protocol` 流水线（`python -m protocol.s1 verify` 今天 5/5 PASS）。
- Produces: `kind=S2S` 的报文表；`verify` 的契约从「kind ∈ {CMD,NOTI}、总数 1171、5 文件」扩展为「kind ∈ {CMD,NOTI,S2S}、总数 1171+4、6 文件」，且 5 项检查仍全绿。

- [ ] **Step 1: 先跑基线**

Run: `python -m protocol.s1 verify`
Expected: 5/5 PASS, exit 0。把输出原文贴进提交说明。

- [ ] **Step 2: 写表**

`protocol/map/s2s-map.tsv`（列与 `opcode-map.tsv` 严格同构；`status`/`tier` 必须取自 `protocol/verify.py:29-32` 的 `TIERS`/`STATUSES` 枚举，**不能自造标签**）：

```
kind	id	name	rodata_va	dispatcher	status	tier	read_count	write_count	ext_mark
S2S	4	NOTICE_CHANNEL_SERVER	-	DFLegacy.InterProcess.InterProcessLink	mapped	T4_fields-read+write	1	1	-
S2S	128	REGISTER	-	DFLegacy.InterProcess.InterProcessLink	mapped	T4_fields-read+write	1	1	-
S2S	129	REGISTER_ACK	-	DFLegacy.InterProcess.InterProcessLink	mapped	T4_fields-read+write	1	1	-
S2S	130	STATUS	-	DFLegacy.InterProcess.InterProcessLink	mapped	T4_fields-read+write	1	1	-
```

- `tier` 用 `T4_fields-read+write` 是因为这些报文的字段由我们自己**双向读写**；它不表示"逆向深度"，这一点写进 `protocol/README.md`，避免后人误读。
- `rodata_va` 填 `-`：这些报文没有官服 `.rodata` 来源（自选语义，spec §5.2）。

- [ ] **Step 3: 扩契约并跑 verify**

按 `protocol/importer.py:10-16` 的 `kind` 白名单加入 `"S2S"`；按 `protocol/verify.py:33` 的 `KIND_BASELINES` 加入 `("s2s", "S2S")`；按 `protocol/verify.py:36` 把 `BASELINE_OPCODES_TOTAL` 从 `1171` 改为 `1175`（= 1171 + 新增 4 行）；按 `protocol/verify.py:39-42` 的 `TRACKED_FILES` 加入 `map/s2s-map.tsv`，并同步 `KEY_COLUMNS`/`EXPECTED_COLUMNS`（`verify.py:55-66`）里该文件的键列；`protocol/manifest.json` 的 sha256 由 `python -m protocol.s1 import` 重新生成，**不要手填**。`protocol/README.md` 里写死的"5 文件/1171 条"一并改成"6 文件/1175 条"。

Run: `python -m protocol.s1 import`（重新生成表与清单）
Run: `python -m protocol.s1 verify`
Expected: 5/5 PASS, exit 0 —— **若 `rows_and_keys` 或 `baselines` 变红，说明总数或文件清单还有一处没同步，按报错信息补齐**

- [ ] **Step 4: Python 侧自测**

Run: `python -m pytest protocol/tests -q`
Expected: 全绿（既有用例不回归；若 `verify` 的契约常量有用例断言，同一次提交里更新）

- [ ] **Step 5: 提交**

```bash
git add protocol/map/s2s-map.tsv protocol/importer.py protocol/verify.py protocol/manifest.json protocol/README.md
git commit -m "S2: protocol 新增 S2S 报文表并把校验契约扩到 6 文件/1175 条（verify 仍 5/5）"
```

---

## Task 10: 验收平台支持多进程 + 本刀门场景（I5 中段）

> **2026-10-22 刷新（Task 8 完成后进入本波）：草案的 `SERVER_SPECS` 端口是猜的、`server_dir` 只读未实现、
> 多进程就绪只等 Entrance，被本节「刷新裁定」取代；下面 Step 1/Step 3 的草案代码块保留作历史。**
>
> 刷新裁定（每条带代价，依据：`model.py:91-104,113-122`、`runner.py:59-71,385-441,508-557`、
> `verdict.py:99-140`、`cli.py:172-199`）：
> 1. **`server` → `servers` 重命名三处触碰点**：`model.py:99`（`Manifest.server` 字段）、`model.py:120`
>    （`deterministic_view` 的 `"server"` 键遍历）、`runner.py:438`（`start_server` 写 `mf.server = rec`）。
>    `runner._load`（`:496`）用 `Manifest(**raw)` 反序列化，字段改名后无跨 run 兼容问题（run 目录每 run 新建）；
>    `deterministic_view` 键名变化要 `grep -rn '\.server\b\|\["server"\]\|\x27server\x27' tools/acceptance --include=*.py`
>    把每处调用点改成 `servers`（一处都不能漏）。
> 2. **`SERVER_SPECS` 端口不能硬编码、disjoint 判据必须按 (host,port,protocol)**：草案的
>    `game tcp[7001] udp[2311,7002,7003] / channel tcp[2311,8080]` 是猜的，且 `test_server_specs_declare_disjoint_ports`
>    把 tcp+udp 端口混在一起查重——2311 在 game 是 UDP、channel 是 TCP，本是合法共存却被误判冲突。
>    端口应从 `server.json` 的 `DFLegacy` 段派生：channel 绑 `Entrance.Port + AdditionalPorts + Processes.Channel.LinkPort`，
>    game 绑 `Channel.Port + GameplayDatagram/CharacterDatagram` 的 UDP。`parse_server_endpoints`（`runner.py:59`）
>    目前不解析 `GameplayDatagram`（PrimaryPort/SecondaryPort，见 `:33-34` 注释）与 `Processes.Channel.LinkPort`，需补。
> 3. **多进程就绪不能只等 Entrance**：`wait_until_listening`（`runner.py:534`）只取 `endpoints["Entrance"]`；
>    拆分模式下 channel 等 Entrance、game 等 Channel.Port，须按进程分别等各自的就绪端点。
> 4. **`judge` 的 `server_dir` 现在完全没被读**（`verdict.py:99` 形参，正文 `:105-140` 无引用）：草案「让 server_dir
>    真的被读」正确。落法：场景里 `evidence=[静态]` 的服务端侧期望，从 `server-*.log`（逐进程日志）搜 `pattern`；
>    `_evidence_files`（`:43`）目前只看 actor 目录 netdump，需为服务端侧期望加 `server-*.log` 搜索域。
>
> 门槛：`(cd tools) python -m pytest acceptance/tests -q` 全绿；`tools/acceptance/tests/test_multi_server.py`
> 新用例（逐进程 manifest 不覆写、disjoint 按 (host,port,protocol)、门场景有可失败期望）先写红再实现。
>
> **2026-10-22 实施回填（两处）**：①裁定 Files 清单漏 4 个既有测试，`servers` 重命名与门场景加入后
> **不交则提交即红**，必须同波提交：`test_model.py`（`server=`→`servers=`）、`test_cli_dry_run.py`
> （场景数 4→5）、`test_cli_orchestrate.py`（manifest 键 `server`→`servers`、日志名 `server.log`→`server-game.log`）、
> `test_runner.py`（日志名同上）。②「全绿」门槛在本机有**预先存在**的 3 红（`test_cli_orchestrate.py` 的
> wires/waits/ambiguous_pid 三用例）：根因是 Windows 排除端口段 **2280–2379 覆盖 TCP 2311**，`bind 127.0.0.1:2311`
> 报 `WinError 10013`，与是否在跑服务端无关（Lead 在 base 提交 `893383f` 的 worktree 复跑同样 3 红；
> `netsh interface ipv4 show excludedportrange protocol=tcp` 证实排除段）。非 Task 10 回归，记 open 项
> **D10-ENV-2311**；**Task 11 真机同样要绑 2311，开跑前必须先解决**（重启换段 / 调整 Hyper-V 动态端口范围 /
> 管理员保留），并把「全绿」门槛按该遗留项改读作「118 passed + 3 个已归因的环境红」。

**Files:**
- Modify: `tools/acceptance/model.py`（`Manifest` 支持按进程名的服务端记录）
- Modify: `tools/acceptance/runner.py`（多服务端启动/等待/端口预检）
- Modify: `tools/acceptance/cli.py`（每进程一份日志）
- Modify: `tools/acceptance/verdict.py`（`server_dir` 真的被读）
- Create: `tools/acceptance/scenarios/handshake-login-town.yaml`
- Create: `tools/acceptance/tests/test_multi_server.py`

**Interfaces:**
- Consumes: `Scenario`/`ActorSpec`/`Expect` 现有 dataclass；`Runner` 的启动/等待/预检函数。
- Produces: `Manifest.servers: dict[str, dict]`（键为进程名，如 `game`/`channel`，**不再被第 N 次启动覆写**）；`runner.SERVER_SPECS`（声明每个服务端进程：命令、就绪端口、日志名）。

- [ ] **Step 1: 写失败的 Python 测试**

`tools/acceptance/tests/test_multi_server.py`：

```python
import json
from pathlib import Path

from tools.acceptance import model, runner

REPO_ROOT = Path(__file__).resolve().parents[3]


def test_manifest_keeps_one_entry_per_process():
    manifest = model.Manifest(
        run_id="r1", git_sha="0" * 40, created_utc="2026-10-19T00:00:00Z",
        scenario_id="handshake-login-town", expect_t0="server-accept",
        actors=[{"name": "A", "driver": "real-client"}],
        servers={"game": {"pid": 1}, "channel": {"pid": 2}})
    payload = json.loads(manifest.to_json())
    assert payload["servers"]["channel"]["pid"] == 2
    assert "server" not in payload, "旧的单数 server 键必须消失，否则两处记法会分叉"


def test_server_specs_declare_disjoint_ports():
    specs = runner.SERVER_SPECS
    ports = [p for spec in specs.values() for p in spec["tcp_ports"]] + [
        p for spec in specs.values() for p in spec["udp_ports"]]
    assert len(ports) == len(set(ports)), "两个服务端进程不得声明同一端口"


def test_scenario_declares_a_falsifiable_expect():
    scenario = model.load_scenario(
        REPO_ROOT / "tools/acceptance/scenarios/handshake-login-town.yaml")
    assert scenario.has_falsifiable_expect(), "本刀的门场景必须至少有一条能失败的期望"
```

- [ ] **Step 2: 跑测试确认失败**

Run: `python -m pytest tools/acceptance/tests/test_multi_server.py -q`
Expected: `AttributeError` / `TypeError`（`Manifest` 还没有 `servers`；`SERVER_SPECS` 不存在）

- [ ] **Step 3: 改实现（三处最小改动）**

1. `model.py` 的 `Manifest`（`:91-104`）：把 `server: dict = field(default_factory=dict)` 换成 `servers: dict[str, dict] = field(default_factory=dict)`，并把 `deterministic_view`（`:113-122`）里的 `"server": {k: v for k, v in manifest.server.items() ...}` 改成按进程名遍历：

```python
        "servers": {
            name: {k: v for k, v in entry.items() if k != "pid"}
            for name, entry in manifest.servers.items()
        },
```

   然后跑 `grep -rn "\.server\b\|\[\"server\"\]\|'server'" tools/acceptance --include=*.py`，把每一处调用点改成 `servers`（**一处都不能漏**；`deterministic_view` 的键名变化会让跨 run 对比的 golden 文件失效，若 `tools/acceptance/verdicts/` 里有依赖它的 tracked 文件，同一次提交里更新）。
2. `runner.py`：新增模块级 `SERVER_SPECS`：

```python
SERVER_SPECS = {
    "game": {"project": "src/DFLegacy.Server", "tcp_ports": [7001], "udp_ports": [2311, 7002, 7003]},
    "channel": {"project": "src/DFLegacy.Channel", "tcp_ports": [2311, 8080], "udp_ports": []},
}
```

`start_server` 改成 `start_servers(names, ...)`：逐个 `Popen`，每个进程的 stdout 写到 `server-<name>.log`；`wait_until_listening` 对每个进程等它的 `tcp_ports` 全部可连；端口预检遍历所有声明的 tcp+udp（**修掉 spec §7.2 记的两个漏**）。

3. `verdict.py` 的 `judge`：`server_dir` 改为真的被读——当场景里存在 `evidence` 以 `[静态]` 标注的服务端侧期望时，从 `server_dir/server-*.log` 里找 `pattern`。**本刀只做一件事**：让 `server_dir` 不再是被忽略的形参，并加一条能失败的用例。

4. 新场景 `tools/acceptance/scenarios/handshake-login-town.yaml`：

```yaml
id: handshake-login-town
goal: 握手（11/5/9/1）→ 登录 → 选角 → 进城，拆分与单体两档逐字节一致（本刀的门）
expect_t0: server-accept
actors:
  - {name: A, driver: real-client}
steps:
  - actor: A
    action: 完成握手并登录
    expect:
      - {description: "c2s:connect（握手首帧）", level: "[存在]", pattern: "000B2B0000000000000001", evidence: "[抓包]"}
      - {description: "s2c:connect-ack（12 号回包）", level: "[存在]", pattern: "000C2F0000000000000001", evidence: "[抓包]"}
      - {description: "选角后进入城镇（人工确认同屏）", level: "[语义]"}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `python -m pytest tools/acceptance/tests -q`
Expected: 全绿。注意 `tests/test_cli_orchestrate.py` 会真 bind `127.0.0.1:2311`（`tests/test_cli_orchestrate.py:74-78`）⇒ 先确认没有 server 在跑，否则该文件必须单独跑。

- [ ] **Step 5: 提交**

```bash
git add tools/acceptance/model.py tools/acceptance/runner.py tools/acceptance/cli.py tools/acceptance/verdict.py tools/acceptance/scenarios/handshake-login-town.yaml tools/acceptance/tests/test_multi_server.py
git commit -m "S2: 验收平台支持多服务端进程与逐进程日志，新增本刀门场景"
```

---

## Task 11: Tier 2 / Tier 3 验收与收尾

> **2026-10-22 刷新（Task 9/10 完成后进入本波）：草案 Step 2 的「逐字节对比 + 例外清单」与现场证据不符，
> 且验收平台对拆分模式的编排还有半截缺口，被本节「刷新裁定」取代；下面 Step 1–5 草案保留作历史。**
>
> 刷新裁定（每条带代价，依据：`progress.md` Task 4/5 段、`tools/acceptance/cli.py:184`、
> `runner.py:98-135`、Task 10 门槛「118 passed + 3 环境红」）：
> 1. **Tier 2 = 按会话密钥解密后比明文，不是逐字节比密文（D5-RAW-COMPARE 收口）**。草案 Step 2 说「逐字节对比、
>    例外只允许时间戳/临时端口/会话 id」，但握手帧用**随机会话密钥**加密（Task 4 实录：4 个不同会话密钥解出同一明文，
>    解密顺序 `DecryptBlocks(Decompress(x))`），原始密文两档必然不同、且差异不是「例外清单」能枚举的。
>    落法：复用 Task 5 I2 口径 —— `11/5/9/1 → 12/6/10/3`，各会话密钥解密/解压后对协议 10 脚本、协议 3 元数据、
>    协议 6 缓存决定**逐字节比明文**；原始帧只比「结构一致性」（帧型/长度/顺序），不比密文字节。
>    **代价**：Tier 2 的「客户端字节不变」证据落在解密后内容，不能宣称「原始帧字节不变」（会话密钥加密的固有限制，与官方行为一致）。
> 2. **拆分模式编排缺半截：`orchestrate` 只起 `name="game"`，不起 channel（本波第一个实现步骤）**。Task 10 给了原语
>    （`start_server(name=…)`、`server_process_specs`、`_is_split_server`、`wait_until_listening` 拆分就绪），
>    但 `cli.py:184` 的 orchestrate 仍单进程。Tier 2 拆分档必须补「拆分模式同时起 `DFLegacy.Channel.exe`（channel）+
>    `DFLegacy.Server.exe`（game）、分别等就绪」；二选一：接入 orchestrate（推荐，配 `test_multi_server.py` 扩展用例）
>    或 runbook 明示手工两进程（须在 runbook 写明，不得悄悄手工）。
>    **代价**：不补则 Tier 2 拆分档跑不起来（只有 game、没有 channel 接管入口族）。
> 3. **D10-ENV-2311 必须先解决**：本机 Windows 排除端口段 2280–2379 覆盖 TCP 2311，`bind` 恒 `WinError 10013`；
>    Entrance 与 CharacterDatagram 都绑 2311 ⇒ 真机服务端起不来。开跑前用户侧解决（重启 / 管理员
>    `netsh int ipv4 set dynamic tcp start=49152 num=16384` 后重启 / 管理员保留），解决后
>    `python -m pytest acceptance/tests -q` 应 **121 passed**（`test_cli_orchestrate.py` 3 用例恢复绿）。
> 4. **D5-REAL 取数版本**：`net_dump.cpp` 仍未提交（他人工作区改动）；真机取样前固定构建/输入版本，
>    不把「必须提交他人改动」当先决条件（沿用 Task 5 交接 D5-REAL 口径）。

**现行 Step 0（进波先做，再跑 Step 1–5）**：①实现裁定 2 的 orchestrate 双进程接线（含扩展用例）；
②用户侧解决 D10-ENV-2311 并复跑 acceptance 到 121 passed；③按裁定 1 把 runbook 的对比脚本写成「解密后明文对比」；
④固定 net_dump 构建/输入版本并记录。

**Files:**
- Create: `docs/superpowers/plans/2026-10-19-s2-tier2-tier3-runbook.md`（执行手册，`git add -f`）
- Modify: `server.json`（**只在跑 Tier 2 时**临时切换 `Processes` 段，验完改回）
- Modify: `STATUS.md`（记录本刀结论与证据类别）

**Interfaces:**
- Consumes: Task 1–10 的全部产出。
- Produces: 一份可复跑的两档对比记录 + 真机一次记录。

- [ ] **Step 1: Tier 2 单体档基线**

`server.json` 保持 `Channel.Enabled=false` / `Game.EntranceBinding=true`：

Run: `python -m tools.acceptance.cli run handshake-login-town`
Expected: 产出 `runs/<runId>/actor-A/…`；把 verdict 与 netdump 路径记进 runbook。

- [ ] **Step 2: Tier 2 拆分档对比**

`server.json` 改成 `Channel.Enabled=true` / `Game.EntranceBinding=false`：

Run: `python -m tools.acceptance.cli run handshake-login-town`
Expected: 两档的客户端帧序列一致。**逐字节对比**用 runbook 里的脚本（对 `actor-A` 的 netdump 排序后比对），例外清单只允许：时间戳、临时端口、会话 id。任何其它差异 ⇒ 停，回到 Task 4/5 查。

- [ ] **Step 3: Tier 3 真机一次**

按 `docs/superpowers/plans/2026-10-07-local-multiclient-acceptance-s0.md` 的真机流程，两档各跑一次双客户端进城；比对服务端日志锚点（登录 / `SELECT_CHARACTER` / 进城 / 同屏）。把日志片段与结论写进 runbook，标 `[真机]`。

- [ ] **Step 4: 改回配置并跑一次全量回归**

Run: `dotnet build DFLegacy.Emulator.slnx`（零错误）
Run: `dotnet run --project tests/DFLegacy.SmokeTests`（0 failures）
Run: `python -m protocol.s1 verify`（5/5 PASS）
Run: `python -m pytest tools/acceptance/tests -q`（全绿，`test_cli_orchestrate.py` 单独跑）

- [ ] **Step 5: 提交**

```bash
git add -f docs/superpowers/plans/2026-10-19-s2-tier2-tier3-runbook.md
git add server.json STATUS.md
git commit -m "S2: Tier 2/Tier 3 验收记录（单体/拆分逐字节对比 + 真机一次）"
```

---

## Self-Review（原计划历史自审，不覆盖 Recon 0.2.1 修订）

| spec 要求 | 落在哪个任务 |
|---|---|
| §3.1 目标 1（握手族拆成 `DFLegacy.Channel`） | Task 3（宿主）+ Task 4（接线）+ Task 5（新 exe） |
| §3.1 目标 2（官方形状链路） | Task 1（帧/载荷）+ Task 6/7（生命周期） |
| §3.1 目标 3（落盘单写者不动） | Global Constraints + Task 5 的「禁止引用 Server」+ Task 7（链路不碰存档） |
| §3.1 目标 4（行为逐字节不变） | Task 4 Step 2/5（端口与绑定不变）+ Task 11 Tier 2 |
| §4.2 端口归属 | Task 2（纯函数）+ Task 4/5（各自绑定）+ Task 4 Step 5/Task 5 Step 5（真绑定验证） |
| §4.4 第二刀耦合清单 | 非目标，无任务（已在 spec 记录） |
| §5.1 帧壳参数 | Task 1（`InterProcessLink.Create` + hex 断言） |
| §5.2 四个报文 | Task 1（编解码）+ Task 6/7（收发） |
| §5.3 生命周期/失败语义 | Task 6（`accepted=0`、3 次心跳缺失、重连）+ Task 7（服务端 `PeerUp=false`） |
| §5.4 承重 1/2（admin 计数与存活） | Task 8 |
| §5.5 `protocol/` 契约 | Task 9 |
| §5.6 端口 7004 + 单体开关 | Task 2（配置与校验）+ Task 4（`Channel.Enabled` 时不绑） |
| §6 I1–I5 | I1=Task 4、I2=Task 5、I3=Task 6+7、I4=Task 8、I5=Task 9+10+11 |
| §7.1 Tier 1 四条 | Task 6/7 的断言（hex、长度、`accepted=0`、心跳缺失） |
| §7.1 Tier 2 / Tier 3 | Task 11 |
| §7.2 验收平台四处缺口 | Task 10 |
| §8 R1（新失败模式可见） | Task 8（`channelLink.connected`） |
| §8 R2（配置漂移） | Task 5（窄模型）+ Task 5 Step 1 的键一致性测试 |
| §8 R4（verdict 恒真） | Task 10 Step 3.3 + Step 1 的 `has_falsifiable_expect` 用例 |
| §8 R5（verify 5/5） | Task 9 Step 3 |
| §8 R9（17k 行类） | Task 4 的改动限定在 `ExecuteAsync` 的端口列表（**不碰 `HandleClientAsync`**） |
| §9 缺口 1–4（官方证据） | 非本计划范围（Docker 恢复后另立任务） |
| §9 缺口 5（验收平台） | Task 10 |
| §9 缺口 6/7（真机设施、GameProbe 7001） | Task 11 Step 3（真机）；Task 4 Step 5 会暴露 7001 实际归属 |

**历史 Placeholder 扫描（后续 Task 5 已改为契约与验收步骤，远期草案待刷新）**：原计划未使用「TBD/类似 Task N/适当处理」等占位表述；每个代码步骤都给了可直接落盘的实现或精确 diff。
**类型一致性**：`RegisterPayload`/`RegisterAckPayload`/`StatusPayload`/`NoticeChannelServerPayload` 的字段顺序在 Task 1 定义，Task 6/7/8 只按该顺序使用；`IEntranceSessionSink.Begin(service, remote)` 在 Task 3 定义，Task 4/5 的实现签名一致。

**本轮自审修掉的四个缺陷（留档，因为它们都会让实现跑偏）**：

1. **心跳方向**：原稿只让 game→channel 发 `STATUS`，而"连续 3 次缺失判 down"在单向心跳下必然误判（接收方没有回包可等）。已改为**双向 STATUS**，并同步修订设计稿 §5.2/§5.3。
2. **admin 合并取错了值**：原稿让 `/api/status` 合并**自己发出**的 STATUS；已改为合并 `LastRemoteStatus`（对端发来的），Task 6 的测试用一条 `ActiveSessions=2` 的入站帧钉住这一点。
3. **`protocol/` 行用了不存在的枚举值**：原稿写 `status=[设计]`、`tier=[字节]`，而 `protocol/verify.py:29-32` 只接受 `TIERS`/`STATUSES` 里的字面量。已改成 `mapped` / `T4_fields-read+write`，并补上 `KIND_BASELINES` 与 `1171 → 1175` 的具体改动点。
4. **Python 侧签名是猜的**：`Manifest` 的真实字段是 `git_sha/created_utc/scenario_id/server`（`model.py:91-104`），`load_scenario` 收的是 `Path`（`:149`）。已按实际签名重写测试，并把 `deterministic_view` 的同步改动列成显式步骤。

另外两处实现级修正：`EntranceHandshakeService` 不再伪造一个不存在的 `EntranceContentFactory(options, scripts)` 构造；channel 的 `Program.cs` 不再调 `BuildServiceProvider()`（ASP.NET 反模式），改为直接建 `LoggerFactory`。
