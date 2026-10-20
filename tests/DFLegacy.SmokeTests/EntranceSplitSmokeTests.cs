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
