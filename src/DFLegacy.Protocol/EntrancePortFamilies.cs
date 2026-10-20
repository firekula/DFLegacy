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
