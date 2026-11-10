using System.Text.Json.Serialization;

namespace DFLegacy.Server;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdminOperationStatus { Prepared, Completed, Rejected, Unknown }

public sealed record AdminVictoryPointRequest(Guid OperationId, uint Amount);
public sealed record AdminMailBatchRequest(Guid OperationId, AdminSendMailRequest Mail, Guid? RetryOf = null);
public sealed record AdminVictoryPointResult(AdminOperationStatus Status, Guid OperationId,
    Guid CharacterId, uint Amount, uint Balance, bool Replayed = false, string? Error = null);
public sealed record AdminMailTarget(Guid CharacterId, string Name);
public sealed record AdminMailDelivery(Guid CharacterId, string Name, bool Success,
    uint? MailId = null, string? Error = null, bool AlreadyDelivered = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] uint[]? MailIds = null);
public sealed record AdminMailSendResult(AdminOperationStatus Status,
    IReadOnlyList<CharacterMailRecord> Mails, string? Error = null);
public sealed record AdminMailBatchResult(AdminOperationStatus Status, Guid OperationId,
    int TargetCount, int SentCount, int FailedCount, int SkippedCount,
    IReadOnlyList<AdminMailDelivery> Deliveries, bool Replayed = false, string? Error = null);

internal sealed record AdminVictoryPointReceipt(Guid OperationId, string Actor, string Fingerprint,
    Guid CharacterId, uint Amount, uint Balance);
internal sealed record AdminMailBatchReceipt(Guid OperationId, string Actor, string Fingerprint,
    AdminSendMailRequest Mail, Guid RootOperationId, Guid? RetryOf, AdminMailTarget[] Recipients,
    bool Completed, AdminMailDelivery[] Deliveries);
