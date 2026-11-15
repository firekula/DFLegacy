namespace DFLegacy.Protocol;

/// <summary>Display-only free-card contents; excludes personal premium rewards.</summary>
public sealed record GameClearCardReward(uint GoldAmount, ushort? ItemId);
