namespace DFLegacy.Server;

/// <summary>
/// 调试用聊天命令（**真机测试专用，不是官服机制**）。
///
/// 用途：S5 的掉落/拾取需要用真机反复验证（掷骰归属、`NOTI 39` 的骰子分、
/// 幽灵物件、背包刷新……），靠自然掉落刷样本太慢，所以给一个"满掉落"开关。
///
/// 约束（2026-10-03 用户裁决）：
/// * 触发词**精确匹配**（忽略首尾空白）：<see cref="DropEverythingCommand"/>；
/// * **触发词不带斜杠**——客户端的聊天发送器把任何以 `/` 开头的输入当它自己的聊天命令
///   吃掉（`/help` 打它自己的命令表，`/zzz`、`//掉落测试` 静默丢弃），一个包都不发出来；
///   原定的 `//掉落测试` 因此**永远到不了服务端**（2026-10-03 真机实测 + 客户端静态分析）；
/// * **只允许 <see cref="AllowedAccount"/> 账号**触发——别的账号打出同样文本仍按普通聊天处理；
/// * 语义是**开/关切换**，作用域 = 触发者**当前所在的副本实例**
///   （见 <see cref="DungeonInstance.ToggleDropDebugAll"/>），实例销毁即失效；
/// * 这是临时测试能力：测试做完就删（台账/STATUS 里按"已知缺口"记着）。
/// </summary>
internal static class DebugChatCommands
{
    /// <summary>
    /// 开启/关闭"满掉落"的聊天触发词（客户端聊天框里**原样、不带斜杠**输入）。
    /// 带斜杠的写法会被客户端自己吃掉，见类型注释。
    /// </summary>
    public const string DropEverythingCommand = "掉落测试";

    /// <summary>只有这个账号能用调试命令（大小写不敏感）。</summary>
    public const string AllowedAccount = "test";

    /// <summary>
    /// 这条聊天正文是不是"满掉落"开关命令（账号也必须匹配）。
    /// 纯函数：不碰会话状态，单测直接钉。
    /// </summary>
    public static bool IsDropEverythingCommand(string? message, string? accountName) =>
        string.Equals(accountName, AllowedAccount, StringComparison.OrdinalIgnoreCase)
        && string.Equals(message?.Trim(), DropEverythingCommand, StringComparison.Ordinal);
}
