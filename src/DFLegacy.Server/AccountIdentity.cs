namespace DFLegacy.Server;

public enum AccountKind
{
    Invalid,
    Test,
    Reserved,
    Normal
}

/// <summary>
/// 管理接口的权限档位。与 <see cref="AccountKind"/>（UID 段分类）无关：
/// Kind 决定账号在游戏里的身份段，Role 只决定 8081 管理接口能做什么。
/// </summary>
public enum AccountRole
{
    /// <summary>普通玩家：只能看自己的角色。</summary>
    Player = 0,

    /// <summary>管理员：发邮件、广播、充值、改疲劳等。</summary>
    Admin = 1
}

public static class AccountUidRules
{
    public const uint InvalidUid = 0;
    public const uint MaximumTestUid = 10_000_000;
    public const uint NormalUidStart = 18_000_000;

    public static bool IsTest(uint accountUid) =>
        accountUid is >= 1 and <= MaximumTestUid;

    public static bool IsNormal(uint accountUid) =>
        accountUid >= NormalUidStart;

    public static bool IsReserved(uint accountUid) =>
        accountUid > MaximumTestUid && accountUid < NormalUidStart;

    public static AccountKind Classify(uint accountUid) =>
        accountUid == InvalidUid
            ? AccountKind.Invalid
            : IsTest(accountUid)
                ? AccountKind.Test
                : IsNormal(accountUid)
                    ? AccountKind.Normal
                    : AccountKind.Reserved;

    public static bool IsValidPersisted(uint accountUid) =>
        accountUid != InvalidUid;

    public static bool IsAllocatable(uint accountUid) =>
        IsTest(accountUid) || IsNormal(accountUid);
}

public sealed record AuthenticatedAccount(
    uint AccountUid,
    string UserName,
    AccountKind Kind);

public sealed record CharacterRosterEntry(
    byte Slot,
    uint CharacterNo,
    Guid StorageId,
    string Name);
