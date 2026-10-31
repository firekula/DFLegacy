using System.Net.Sockets;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 跨会话投递的坏通道护栏（终审 I1）：每个成员/槽位的 <c>Send</c> 句柄写的是**他自己会话**的
/// <c>SerializedWriteStream</c>，成员被踢/掉线/关停时那个流会抛**他自己 token** 的
/// <see cref="OperationCanceledException"/>。缺了 OCE，异常会顺着**发送者**的会话冒泡：
/// 击杀/房间开放/拾取/NOTI 29 的广播把健康玩家的会话打断，扫描循环里则让"房间已经换了、
/// 后面几个成员的 NOTI 29 没发出去"。
/// 判定与既有的坏通道一致（IOException/ObjectDisposed/Socket），只是把 OCE 一并归入——
/// 这四处抄过四遍的 filter 漏的正是它，所以收到一个地方。
/// </summary>
internal static class CrossSessionDelivery
{
    /// <summary>
    /// 客户端文本编码（GBK / CP936，带替换回退——2008 客户端只认它）。
    /// 与 <c>EntranceService.CreateClientEncoding</c>、<c>FriendService.CreateClientEncoding</c> 同一口径；
    /// 放在这里是为了让**扫描线程**（<see cref="DungeonPartyService"/> 的结算倒计时系统消息）不必再抄一份。
    /// </summary>
    public static Encoding ClientEncoding { get; } = CreateClientEncoding();

    private static Encoding CreateClientEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    /// <summary>坏通道判定：目标会话已经不可投递（断线/被踢/服务端关停）。</summary>
    public static bool IsBrokenChannel(Exception exception) =>
        exception is IOException
            or ObjectDisposedException
            or SocketException
            or OperationCanceledException;

    /// <summary>带角色名的收件人描述（真机验收看日志，别只打 GUID）。</summary>
    public static string Describe(Guid characterId, string characterName, ushort areaUserId) =>
        $"{characterName} (uid {areaUserId}, {characterId})";

    /// <summary>
    /// 投一份包；目标通道坏了只告警、不抛出（发送者与其他收件人不受影响）。
    /// 与会话内发送不同，这里同时吞掉 OCE：那是**收件人**会话的取消，不是发送者的。
    /// </summary>
    public static async Task SendSafelyAsync(
        ILogger logger,
        GameServerPacket packet,
        string recipient,
        Func<GameServerPacket, ValueTask> send)
    {
        try
        {
            await send(packet);
        }
        catch (Exception exception) when (IsBrokenChannel(exception))
        {
            logger.LogWarning(
                exception,
                "Could not deliver cross-session packet {ProtocolId} to {Recipient}.",
                packet.ProtocolId,
                recipient);
        }
    }
}
