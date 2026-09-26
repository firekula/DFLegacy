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

    public async Task<PasswordResult> ChangePasswordAsync(
        string userName, string oldPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var body = new ChangeAccountPasswordRequest(oldPassword, newPassword);
        using var response = await PutJsonAsync(
            $"api/accounts/{UrlEncode(userName)}/password", body, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new PasswordResult(PasswordOutcome.Success, null),
            HttpStatusCode.Unauthorized => new PasswordResult(PasswordOutcome.InvalidCredentials, "账号或密码错误。"),
            _ => new PasswordResult(PasswordOutcome.InvalidRequest,
                await ReadErrorAsync(response, cancellationToken))
        };
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

    private async Task<HttpResponseMessage> PostJsonAsync(string relativeUri, object body, CancellationToken ct)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await _http.PostAsync(relativeUri, content, ct);
    }

    private async Task<HttpResponseMessage> PutJsonAsync(string relativeUri, object body, CancellationToken ct)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await _http.PutAsync(relativeUri, content, ct);
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
