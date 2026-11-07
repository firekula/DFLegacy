namespace DFLegacy.Protocol;

/// <summary>One party slot's black-diamond card. An enabled card may roll no equipment.</summary>
public sealed record GamePremiumCardReward(bool Enabled, ushort? ItemId);
