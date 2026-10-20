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
        // 配置里 AdditionalPorts 不保证升序；声明序必须原样保留（排序实现会在这里变红）。
        var (ordered, _) = EntrancePortFamilies.Split(2311, [1000, 8080], 7001);
        check(ordered.SequenceEqual([2311, 1000, 8080]),
            "entrance ports keep declaration order (2311, 1000, 8080) instead of being sorted");
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

        var zeroLinkPort = new ProcessOptions();
        zeroLinkPort.Channel.LinkPort = 0;
        var zeroLinkPortThrew = false;
        try { zeroLinkPort.Validate(); }
        catch (InvalidOperationException) { zeroLinkPortThrew = true; }
        check(zeroLinkPortThrew,
            "LinkPort=0 is rejected: the link port must be within 1..65535");

        var oversizedLinkPort = new ProcessOptions();
        oversizedLinkPort.Channel.LinkPort = 70000;
        var oversizedLinkPortThrew = false;
        try { oversizedLinkPort.Validate(); }
        catch (InvalidOperationException) { oversizedLinkPortThrew = true; }
        check(oversizedLinkPortThrew,
            "LinkPort=70000 is rejected: the link port must be within 1..65535");

        var split = new ProcessOptions();
        split.Channel.Enabled = true;
        split.Game.EntranceBinding = false;
        split.Validate();
        check(split.Channel.Enabled && !split.Game.EntranceBinding,
            "channel-owned entrance ports with game.EntranceBinding=false is a valid split");
    }
}
