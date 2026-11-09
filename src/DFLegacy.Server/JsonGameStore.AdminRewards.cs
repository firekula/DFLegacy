using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    private List<AdminVictoryPointReceipt> _adminVictoryPointGrants = [];
    private List<AdminMailBatchReceipt> _adminMailBatches = [];

    public async Task<AdminVictoryPointResult> GrantAdminVictoryPointsAsync(string actor,
        Guid operationId, Guid characterId, uint amount, CancellationToken ct = default)
    {
        AdminVictoryPointResult Reject(string error, AdminOperationStatus status = AdminOperationStatus.Rejected) =>
            new(status, operationId, characterId, amount, 0, Error: error);
        if (string.IsNullOrWhiteSpace(actor) || operationId == Guid.Empty || characterId == Guid.Empty || amount == 0)
            return Reject("invalid-request");
        actor = actor.Trim().ToUpperInvariant();
        var fingerprint = AdminFingerprint(new { CharacterId = characterId, Amount = amount });
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return Reject("store-quarantined", AdminOperationStatus.Unknown);
            var old = _adminVictoryPointGrants.Find(r => r.OperationId == operationId);
            if (old is not null)
                return old.Actor == actor && old.Fingerprint == fingerprint
                    ? new(AdminOperationStatus.Completed, operationId, old.CharacterId, old.Amount, old.Balance, true)
                    : Reject("operation-conflict");
            if (_adminMailBatches.Any(r => r.OperationId == operationId)) return Reject("operation-conflict");
            var character = StoreCharacter(_accounts, characterId);
            if (character is null) return Reject("character-not-found");
            var balance = (ulong)character.VictoryPoints + amount;
            if (balance > uint.MaxValue) return Reject("victory-points-overflow");
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, character with { VictoryPoints = (uint)balance });
            var receipt = new AdminVictoryPointReceipt(operationId, actor, fingerprint, characterId, amount, (uint)balance);
            var grants = new List<AdminVictoryPointReceipt>(_adminVictoryPointGrants) { receipt };
            if (!await CommitAdminSnapshotAsync(accounts, grants, _adminMailBatches, ct))
                return Reject("write-outcome-unknown", AdminOperationStatus.Unknown);
            return new(AdminOperationStatus.Completed, operationId, characterId, amount, receipt.Balance);
        }
        finally { _gate.Release(); }
    }

    public async Task<AdminMailBatchResult> PrepareAdminMailBatchAsync(string actor,
        Guid operationId, AdminSendMailRequest mail, Guid? retryOf = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || operationId == Guid.Empty || mail is null || retryOf == Guid.Empty)
            return RejectAdminBatch(operationId, "invalid-request");
        if (itemCatalog is null) return RejectAdminBatch(operationId, "item-catalog-unavailable");
        if (!AdminMailComposer.TryCompose(mail, itemCatalog, out _, out var error))
            return RejectAdminBatch(operationId, error!);
        actor = actor.Trim().ToUpperInvariant();
        mail = mail with { Sender = mail.Sender.Trim() };
        var fingerprint = AdminFingerprint(new { Mail = mail, RetryOf = retryOf });
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return RejectAdminBatch(operationId, "store-quarantined", AdminOperationStatus.Unknown);
            var old = _adminMailBatches.Find(r => r.OperationId == operationId);
            if (old is not null)
                return old.Actor == actor && old.Fingerprint == fingerprint
                    ? AdminBatchResult(old, replayed: true) : RejectAdminBatch(operationId, "operation-conflict");
            if (_adminVictoryPointGrants.Any(r => r.OperationId == operationId))
                return RejectAdminBatch(operationId, "operation-conflict");
            Guid rootId = operationId;
            AdminMailTarget[] recipients;
            if (retryOf is { } parentId)
            {
                var parent = _adminMailBatches.Find(r => r.OperationId == parentId);
                if (parent is null || parent.Actor != actor || !parent.Completed)
                    return RejectAdminBatch(operationId, "retry-batch-not-found-or-incomplete");
                if (AdminFingerprint(parent.Mail) != AdminFingerprint(mail))
                    return RejectAdminBatch(operationId, "retry-content-mismatch");
                rootId = parent.RootOperationId;
                var successful = SuccessfulAdminMailRecipients(rootId);
                recipients = parent.Recipients.Where(r => !successful.ContainsKey(r.CharacterId)).ToArray();
            }
            else
            {
                recipients = _accounts.SelectMany(a => a.Characters)
                    .Select(c => new AdminMailTarget(c.Id, c.Name)).ToArray();
            }
            var receipt = new AdminMailBatchReceipt(operationId, actor, fingerprint, mail,
                rootId, retryOf, recipients, false, []);
            var batches = new List<AdminMailBatchReceipt>(_adminMailBatches) { receipt };
            if (!await CommitAdminSnapshotAsync(_accounts, _adminVictoryPointGrants, batches, ct))
                return RejectAdminBatch(operationId, "write-outcome-unknown", AdminOperationStatus.Unknown);
            return AdminBatchResult(receipt);
        }
        finally { _gate.Release(); }
    }

    public async Task<AdminMailBatchResult> GetAdminMailBatchAsync(string actor, Guid operationId,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var receipt = _adminMailBatches.Find(r => r.OperationId == operationId
                && r.Actor.Equals(actor?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (_rewardQuarantined) return RejectAdminBatch(operationId, "store-quarantined", AdminOperationStatus.Unknown);
            return receipt is null ? RejectAdminBatch(operationId, "batch-not-found") : AdminBatchResult(receipt);
        }
        finally { _gate.Release(); }
    }

    public async Task<AdminMailBatchResult> SendAdminMailBatchAsync(string actor, Guid operationId,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return RejectAdminBatch(operationId, "store-quarantined", AdminOperationStatus.Unknown);
            var receipt = _adminMailBatches.Find(r => r.OperationId == operationId
                && r.Actor.Equals(actor?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (receipt is null) return RejectAdminBatch(operationId, "batch-not-found");
            if (receipt.Completed) return AdminBatchResult(receipt, replayed: true);
            if (itemCatalog is null) return RejectAdminBatch(operationId, "item-catalog-unavailable");
            if (!AdminMailComposer.TryCompose(receipt.Mail, itemCatalog, out var composed, out var error))
                return RejectAdminBatch(operationId, error!);
            var accounts = CopyStoreAccountsLocked();
            var successful = SuccessfulAdminMailRecipients(receipt.RootOperationId);
            var usedIds = _accounts.SelectMany(a => a.Characters).SelectMany(c => c.Mailbox ?? [])
                .Select(m => m.Id).ToHashSet();
            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var deliveries = new List<AdminMailDelivery>();
            foreach (var target in receipt.Recipients)
            {
                if (successful.TryGetValue(target.CharacterId, out var prior))
                {
                    deliveries.Add(prior with { AlreadyDelivered = true });
                    continue;
                }
                var character = StoreCharacter(accounts, target.CharacterId);
                if (character is null)
                {
                    deliveries.Add(new(target.CharacterId, target.Name, false, Error: "character-not-found"));
                    continue;
                }
                var mailbox = (character.Mailbox ?? []).Where(m => IsMailAvailable(m, now)).ToList();
                if (mailbox.Count >= byte.MaxValue)
                {
                    deliveries.Add(new(character.Id, character.Name, false, Error: "mailbox-full"));
                    continue;
                }
                uint mailId;
                do { mailId = AllocateMailIdLocked(); } while (!usedIds.Add(mailId));
                // Unique equipment/avatar/egg identities belong to each recipient, not the batch.
                var request = EnsureMailAttachmentInstanceId(composed with
                {
                    Attachment = composed.Attachment is { } attachment ? attachment with { InstanceId = Guid.Empty } : null
                });
                var mail = new CharacterMailRecord(mailId, request.Sender, request.Text, request.Gold,
                    request.Attachment, now, checked(now + (uint)TimeSpan.FromDays(30).TotalSeconds));
                mailbox.Add(mail);
                var updated = ReconcileAvatarLifetimes(character with { Mailbox = mailbox }, character);
                SetStoreCharacter(accounts, updated);
                deliveries.Add(new(character.Id, character.Name, true, mailId));
            }
            var completed = receipt with { Completed = true, Deliveries = deliveries.ToArray() };
            var batches = _adminMailBatches.Select(r => r.OperationId == operationId ? completed : r).ToList();
            if (!await CommitAdminSnapshotAsync(accounts, _adminVictoryPointGrants, batches, ct))
                return RejectAdminBatch(operationId, "write-outcome-unknown", AdminOperationStatus.Unknown);
            return AdminBatchResult(completed);
        }
        finally { _gate.Release(); }
    }

    private Dictionary<Guid, AdminMailDelivery> SuccessfulAdminMailRecipients(Guid rootId) =>
        _adminMailBatches.Where(r => r.RootOperationId == rootId && r.Completed)
            .SelectMany(r => r.Deliveries).Where(d => d.Success)
            .GroupBy(d => d.CharacterId).ToDictionary(g => g.Key, g => g.First());

    private static string AdminFingerprint<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    private static AdminMailBatchResult RejectAdminBatch(Guid id, string error,
        AdminOperationStatus status = AdminOperationStatus.Rejected) => new(status, id, 0, 0, 0, 0, [], Error: error);

    private static AdminMailBatchResult AdminBatchResult(AdminMailBatchReceipt receipt, bool replayed = false) =>
        new(receipt.Completed ? AdminOperationStatus.Completed : AdminOperationStatus.Prepared,
            receipt.OperationId, receipt.Recipients.Length,
            receipt.Deliveries.Count(d => d.Success && !d.AlreadyDelivered),
            receipt.Deliveries.Count(d => !d.Success), receipt.Deliveries.Count(d => d.AlreadyDelivered),
            receipt.Deliveries.ToArray(), replayed);

    private async Task<bool> CommitAdminSnapshotAsync(List<AccountRecord> accounts,
        List<AdminVictoryPointReceipt> grants, List<AdminMailBatchReceipt> batches, CancellationToken ct)
    {
        try
        {
            await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct,
                adminVictoryPointGrants: grants, adminMailBatches: batches);
        }
        catch (Exception error)
        {
            _rewardQuarantined = true;
            try { logger.LogError(error, "GM reward snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
            return false;
        }
        _accounts = accounts;
        _adminVictoryPointGrants = grants;
        _adminMailBatches = batches;
        return true;
    }
}
