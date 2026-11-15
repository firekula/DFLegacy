namespace DFLegacy.AccountClient;

// 请求 DTO 字段名镜像服务端 JsonGameStore.cs，序列化用 camelCase（见客户端 JsonOptions）。
public sealed record CreateAccountRequest(
    string UserName,
    string Password,
    bool TestAccount = false,
    string? OicqCode = null);

public sealed record ChangeAccountPasswordRequest(string OldPassword, string NewPassword);

public sealed record ResetAccountPasswordRequest(string OicqCode, string NewPassword);

// 服务端 201 响应 PublicAccountRecord 的子集；多余字段（characters 等）默认被忽略。
public sealed record PublicAccount(Guid Id, string UserName, uint AccountUid, int Cash, int Kind);

public enum RegistrationOutcome { Success, AlreadyExists, Invalid }

public sealed record RegistrationResult(
    RegistrationOutcome Outcome,
    string? Error,
    PublicAccount? Account);

public enum PasswordOutcome { Success, InvalidCredentials, InvalidRecovery, InvalidRequest }

public sealed record PasswordResult(PasswordOutcome Outcome, string? Error);

// ---- 8081 管理接口的 Bearer token 登录 ----
// 改密不再是匿名接口：8081 对外绑定后，「旧密码自证」会变成公网爆破入口，
// 因此改为先用旧密码登录换 token，再带 token 改密（失败限流集中在登录一处）。

public sealed record LoginRequest(string UserName, string Password);

public sealed record LoginResponse(
    bool Success,
    string? Token,
    string? UserName,
    int Role,
    DateTimeOffset? ExpiresAt);

public enum LoginOutcome
{
    Success,
    InvalidCredentials,

    /// <summary>失败次数过多，被服务端按「账号 + 来源 IP」限流。</summary>
    Locked,

    InvalidRequest
}

public sealed record LoginResult(
    LoginOutcome Outcome,
    string? Token,
    string? UserName,
    int Role,
    string? Error);
