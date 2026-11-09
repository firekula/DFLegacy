using System.Globalization;
using System.Net;
using System.Text;
using DFLegacy.Protocol;
using Microsoft.AspNetCore.Http;

namespace DFLegacy.Server;

public static class TestCeraEndpoint
{
    public static IResult Settings(HttpResponse response, ServerOptions options)
    {
        response.Headers.CacheControl = "no-store";
        return Results.Content("window.DFLegacyClientPay={testCeraEnabled:" +
            (options.EnableTestCeraTopUp ? "true" : "false") + "};", "application/javascript", Encoding.UTF8);
    }

    public static async Task<IResult> HandleAsync(HttpRequest request, ServerOptions options,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken token)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IResult Html(string message, int status) => Results.Content(
            "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:transparent;color:#ddd;font:12px sans-serif\">" +
            WebUtility.HtmlEncode(message) + "<!--" + new string(' ', 1100) + "--></body></html>", "text/html", Encoding.UTF8, status);
        if (!options.EnableTestCeraTopUp) return Html("测试点券充值未启用。", 400);
        if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            return Html("仅接受表单提交。", 400);
        IFormCollection form;
        try { form = await request.ReadFormAsync(token); }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return Html("表单内容无效，请重新填写。", 400);
        }
        if (form["characterName"].Count != 1 || form["amount"].Count != 1 ||
            !int.TryParse(form["amount"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            return Html("请输入角色名和有效整数点券数量。", 400);
        var result = await store.GrantTestCeraToCharacterAsync(form["characterName"].ToString(), amount, token);
        if (!result.Success) return Html(result.Error ?? "充值失败。", 400);
        foreach (var id in result.CharacterIds) sessions.NotifyCera(id, checked((uint)result.Cash));
        return Html($"测试点券充值成功：已增加 {amount} 点券，余额 {result.Cash}。", 200);
    }

    public static async Task<IResult> HandleDiamondAsync(HttpRequest request, ServerOptions options,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken token)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IResult Html(string message, int status) => Results.Content(
            "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:transparent;color:#ddd;font:12px sans-serif\">" +
            WebUtility.HtmlEncode(message) + "<!--" + new string(' ', 1100) + "--></body></html>", "text/html", Encoding.UTF8, status);
        if (!options.EnableTestCeraTopUp) return Html("测试黑钻充值未启用。", 400);
        if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            return Html("仅接受表单提交。", 400);
        IFormCollection form;
        try { form = await request.ReadFormAsync(token); }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return Html("表单内容无效，请重新填写。", 400);
        }
        if (form["characterName"].Count != 1 || form["days"].Count != 1 ||
            !int.TryParse(form["days"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var days))
            return Html("请输入角色名和有效整数黑钻天数。", 400);
        var result = await store.GrantTestDiamondToCharacterAsync(form["characterName"].ToString(), days, token);
        if (!result.Success) return Html(result.Error ?? "充值失败。", 400);
        var characters = await store.GetCharactersAsync(result.Account, token);
        foreach (var character in characters)
            sessions.NotifyPremiumInfo(character.Id, GameProtocolEngine.BlackDiamondServiceType, result.RemainingSeconds);
        foreach (var id in result.WeaknessRecoveredCharacterIds ?? [])
            sessions.NotifyWeaknessRecovery(id, DungeonWeaknessPolicy.FullStamina);
        return Html($"测试黑钻充值成功：已增加 {days} 天黑钻。", 200);
    }
}
