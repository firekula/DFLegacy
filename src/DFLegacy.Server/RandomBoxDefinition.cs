using System.Globalization;
using System.Text.RegularExpressions;

namespace DFLegacy.Server;

public sealed record RandomBoxReward(ushort ItemId, uint Count, int Weight, byte Attribute);

public sealed record RandomBoxRewardGroup(RandomBoxReward Fallback, IReadOnlyList<RandomBoxReward> Rewards)
{
    public RandomBoxReward Select(IDropRandomSource random)
    {
        var roll = random.Next(LotteryItemDefinition.RollUpperBound);
        var cumulative = 0;
        foreach (var reward in Rewards)
        {
            cumulative += reward.Weight;
            if (roll < cumulative) return reward;
        }
        return Fallback;
    }
}

public sealed record RandomBoxDefinition(
    IReadOnlyList<RandomBoxRewardGroup> Groups,
    IReadOnlyDictionary<ushort, uint> RequiredItems)
{
    internal static RandomBoxDefinition? Parse(string typeTag, string text)
    {
        if (typeTag != "random upgradable legacy") return null;
        var block = Regex.Match(text, @"\[RANDOMBOX\](.*?)\[/RANDOMBOX\]", RegexOptions.Singleline);
        if (!block.Success) return null;
        var groups = new List<RandomBoxRewardGroup>();
        foreach (Match group in Regex.Matches(block.Groups[1].Value, @"\[int data\](.*?)\[/int data\]", RegexOptions.Singleline))
        {
            var rows = Regex.Replace(group.Groups[1].Value, @"//[^\r\n]*", "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            if (rows.Length == 0 || rows[0].Length != 3) return null;
            var fallback = ReadReward(rows[0][0], rows[0][1], 0, rows[0][2]);
            if (fallback is null) return null;
            var rewards = new List<RandomBoxReward>();
            long total = 0;
            foreach (var row in rows.Skip(1))
            {
                if (row.Length != 4) return null;
                var reward = ReadReward(row[0], row[2], row[1], row[3]);
                if (reward is null || reward.Weight <= 0) return null;
                total += reward.Weight;
                if (total > LotteryItemDefinition.RollUpperBound) return null;
                rewards.Add(reward);
            }
            groups.Add(new(fallback, rewards.AsReadOnly()));
        }
        var removal = Regex.Match(block.Groups[1].Value,
            @"\[sealing removal item\](.*?)\[/sealing removal item\]", RegexOptions.Singleline);
        if (!removal.Success || groups.Count == 0) return null;
        var values = Regex.Matches(removal.Groups[1].Value, @"\d+")
            .Select(match => long.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length == 0 || values[0] <= 0 || values.Length != 1 + values[0] * 2) return null;
        var required = new Dictionary<ushort, uint>();
        for (var i = 1; i < values.Length; i += 2)
        {
            if (values[i] is <= 0 or > ushort.MaxValue || values[i + 1] is <= 0 or > uint.MaxValue
                || !required.TryAdd((ushort)values[i], (uint)values[i + 1])) return null;
        }
        return new(groups.AsReadOnly(), required);
    }

    private static RandomBoxReward? ReadReward(long itemId, long count, long weight, long attribute) =>
        itemId is > 0 and <= ushort.MaxValue && count is > 0 and <= uint.MaxValue
            && weight is >= 0 and <= LotteryItemDefinition.RollUpperBound && attribute is >= 0 and <= byte.MaxValue
            ? new((ushort)itemId, (uint)count, (int)weight, (byte)attribute) : null;
}
