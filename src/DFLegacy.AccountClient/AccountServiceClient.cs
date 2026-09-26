using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DFLegacy.AccountClient;

public sealed class AccountServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public AccountServiceClient(HttpClient http)
    {
        _http = http;
        if (_http.BaseAddress is null)
            throw new ArgumentException("HttpClient 必须设置 BaseAddress。", nameof(http));
    }

    public AccountServiceClient(string baseUrl)
        : this(new HttpClient { BaseAddress = new Uri(EnsureTrailingSlash(baseUrl)) }) { }

    public async Task<RegistrationResult> RegisterAsync(
        string userName, string password, string? oicqCode, bool testAccount,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateAccountRequest(userName, password, testAccount, oicqCode);
        using var response = await PostJsonAsync("api/accounts", body, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Created => new RegistrationResult(
                RegistrationOutcome.Success, null,
                await ReadAsync<PublicAccount>(response, cancellationToken)),
            HttpStatusCode.Conflict => new RegistrationResult(
                RegistrationOutcome.AlreadyExists,
                await ReadErrorAsync(response, cancellationToken), null),
            _ => new RegistrationResult(
                RegistrationOutcome.Invalid,
                await ReadErrorAsync(response, cancellationToken), null)
        };
    }

    /// <summary>
    /// 登录取 token。8081 对外后改密等接口不再接受匿名调用：客户端先用旧密码登录换 token，
    /// 爆破面因此收敛到登录一处（服务端按「账号 + 来源 IP」限流）。
    /// </summary>
    public async Task<LoginResult> LoginAsync(
        string userName, string password,
        CancellationToken cancellationToken = default)
    {
        var body = new LoginRequest(userName, password);
        using var response = await PostJsonAsync("api/auth/login", body, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var payload = await ReadAsync<LoginResponse>(response, cancellationToken);
                return payload is { Success: true, Token: { Length: > 0 } token }
                    ? new LoginResult(LoginOutcome.Success, token, payload.UserName, payload.Role, null)
                    : new LoginResult(
                        LoginOutcome.InvalidRequest, null, null, 0, "服务端登录响应缺少 token。");
            case HttpStatusCode.Unauthorized:
                // 服务端刻意不区分"账号不存在"与"密码错误"，这里同样只给一句模糊提示。
                return new LoginResult(LoginOutcome.InvalidCredentials, null, null, 0, "账号或密码错误。");
            case HttpStatusCode.TooManyRequests:
                return new LoginResult(
                    LoginOutcome.Locked, null, null, 0,
                    await ReadErrorAsync(response, cancellationToken) ?? "失败次数过多，请稍后再试。");
            default:
                return new LoginResult(
                    LoginOutcome.InvalidRequest, null, null, 0,
                    await ReadErrorAsync(response, cancellationToken));
        }
    }

    /// <summary>作废当前 token。改密/找回后服务端已吊销，这里主动登出只是清理会话，失败无妨。</summary>
    public async Task LogoutAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            using var response = await PostJsonAsync("api/auth/logout", new { }, cancellationToken, token);
        }
        catch (HttpRequestException)
        {
            // 登出失败不影响用户：服务端侧该 token 已随改密被吊销，或到期后自然失效。
        }
    }

    public async Task<PasswordResult> ChangePasswordAsync(
        string userName, string oldPassword, string newPassword,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        var body = new ChangeAccountPasswordRequest(oldPassword, newPassword);
        using var response = await PutJsonAsync(
            $"api/accounts/{UrlEncode(userName)}/password", body, cancellationToken, token);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return new PasswordResult(PasswordOutcome.Success, null);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 401 有两种来源：旧密码错误（端点返回，无体）与 token 缺失/失效（中间件返回，带 error 体）。
            // 用"调用方是否带了 token"区分最可靠——靠响应体判断会被 ReasonPhrase 兜底干扰。
            if (!string.IsNullOrEmpty(token))
            {
                return new PasswordResult(
                    PasswordOutcome.InvalidRequest,
                    await ReadErrorAsync(response, cancellationToken) ?? "登录状态已失效，请重试。");
            }

            return new PasswordResult(PasswordOutcome.InvalidCredentials, "账号或密码错误。");
        }

        return new PasswordResult(PasswordOutcome.InvalidRequest,
            await ReadErrorAsync(response, cancellationToken));
    }

    public async Task<PasswordResult> ResetPasswordAsync(
        string userName, string oicqCode, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var body = new ResetAccountPasswordRequest(oicqCode, newPassword);
        using var response = await PostJsonAsync(
            $"api/accounts/{UrlEncode(userName)}/password/reset", body, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new PasswordResult(PasswordOutcome.Success, null),
            HttpStatusCode.Unauthorized => new PasswordResult(PasswordOutcome.InvalidRecovery, "账号或 QQ 错误。"),
            _ => new PasswordResult(PasswordOutcome.InvalidRequest,
                await ReadErrorAsync(response, cancellationToken))
        };
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private Task<HttpResponseMessage> PostJsonAsync(
        string relativeUri, object body, CancellationToken ct, string? token = null) =>
        SendJsonAsync(HttpMethod.Post, relativeUri, body, token, ct);

    private Task<HttpResponseMessage> PutJsonAsync(
        string relativeUri, object body, CancellationToken ct, string? token = null) =>
        SendJsonAsync(HttpMethod.Put, relativeUri, body, token, ct);

    // 用 HttpRequestMessage 而不是给 HttpClient 挂默认头：默认头是共享状态，
    // 多窗口并发调用时会串号。
    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method, string relativeUri, object body, string? token, CancellationToken ct)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(method, relativeUri) { Content = content };
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return await _http.SendAsync(request, ct);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var body = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, JsonOptions);
            return body?.Error;
        }
        catch (JsonException)
        {
            return response.ReasonPhrase;
        }
    }

    private static string UrlEncode(string value) =>
        System.Net.WebUtility.UrlEncode(value);

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";

    private sealed record ErrorResponse(string? Error);
}
