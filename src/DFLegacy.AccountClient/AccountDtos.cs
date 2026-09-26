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
