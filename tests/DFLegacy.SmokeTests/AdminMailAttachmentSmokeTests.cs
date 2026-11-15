using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Server;
using DFLegacy.Protocol;
using Microsoft.Extensions.DependencyInjection;

internal static class AdminMailAttachmentSmokeTests
{
    private sealed class FaultWriter(bool after) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Writes;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            Writes++;
            if (Armed && !after) throw new IOException("multi-mail before replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, bytes, ct);
            if (Armed) throw new IOException("multi-mail after replacement");
        }
    }

    private static AdminSendMailRequest Read(string json) =>
        JsonSerializer.Deserialize<AdminSendMailRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var store = f.Open();
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("mailmulti", "password", OicqCode: "123456"));
        var a = (await store.CreateCharacterAsync("mailmulti", new("MailOne"))).Character!;
        var b = (await store.CreateCharacterAsync("mailmulti", new("MailTwo"))).Character!;
        var request = Read("""
            {"sender":"GM","text":"Multi","gold":17,"attachments":[
              {"attachmentKind":1,"itemId":200,"quantity":1},
              {"attachmentKind":1,"itemId":1000,"quantity":2501},
              {"attachmentKind":1,"itemId":200,"quantity":1}]}
            """);
        var id = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", id, request);
        var sent = await store.SendAdminMailBatchAsync("gm", id);
        var aMails = await store.GetCharacterMailsAsync(a.Id);
        var bMails = await store.GetCharacterMailsAsync(b.Id);
        check(sent.SentCount == 2 && aMails.Length == 3 && bMails.Length == 3,
            "GM attachment slots produce a complete native-mail bundle for each recipient");
        check(sent.Deliveries.All(d => d.MailIds is { Length: 3 } && d.MailId == d.MailIds[0])
            && sent.Deliveries.SelectMany(d => d.MailIds ?? []).Distinct().Count() == 6
            && aMails.Single(m => m.Id == sent.Deliveries.Single(d => d.CharacterId == a.Id).MailId).Gold == 17,
            "bundle receipts list all distinct mail IDs and preserve the first gold-bearing MailId");
        check(aMails.Sum(m => m.Gold) == 17 && aMails.Count(m => m.Gold == 17) == 1
            && aMails.SingleOrDefault(m => m.Attachment?.ItemId == 1000)?.Attachment?.CountOrValue == 2501,
            "GM bundle preserves a large stack in one attachment and grants gold only once");
        var identities = aMails.Concat(bMails).Where(m => m.Attachment?.ItemId == 200)
            .Select(m => m.Attachment!.InstanceId).ToArray();
        check(identities.Length == 4 && identities.All(value => value != Guid.Empty) && identities.Distinct().Count() == 4,
            "repeated equipment slots and different recipients receive independent identities");
        check((await store.SendAdminMailBatchAsync("gm", id)).Replayed
            && (await store.GetCharacterMailsAsync(a.Id)).Length == 3,
            "replayed multi-slot confirmation does not duplicate any attachment");

        var invalidRequests = new[]
        {
            """{"sender":"GM","text":"Bad","attachments":[{"attachmentKind":1,"itemId":1000,"quantity":2},{"attachmentKind":1,"itemId":999,"quantity":1}]}""",
            """{"sender":"GM","text":"Bad","attachments":[{"attachmentKind":1,"itemId":1000,"quantity":0}]}""",
            """{"sender":"GM","text":"Bad","attachments":[{"attachmentKind":1,"itemId":200,"quantity":2}]}""",
            """{"sender":"GM","text":"Bad","attachments":[{"attachmentKind":0,"itemId":1000,"quantity":1}]}""",
            """{"sender":"GM","text":"Bad","attachments":[null]}""",
            """{"sender":"GM","text":"Bad","attachmentKind":1,"itemId":200,"attachments":[]}""",
            """{"sender":"GM","text":"Bad","quantity":2,"attachments":[]}""",
            """{"sender":"GM","text":"Bad","avatarAbilityIndex":1,"attachments":[]}""",
            """{"sender":"GM","text":"Bad","itemId":1000,"attachments":[]}"""
        };
        foreach (var (json, index) in invalidRequests.Select((json, index) => (json, index)))
        {
            var rejected = await store.PrepareAdminMailBatchAsync("gm", Guid.NewGuid(), Read(json));
            check(rejected.Status == AdminOperationStatus.Rejected, $"invalid or ambiguous GM attachment list {index} is rejected before preview");
        }
        var eleven = Read("{\"sender\":\"GM\",\"text\":\"Too many\",\"attachments\":["
            + string.Join(',', Enumerable.Repeat("{\"attachmentKind\":1,\"itemId\":1000,\"quantity\":1}", 11)) + "]}");
        check((await store.PrepareAdminMailBatchAsync("gm", Guid.NewGuid(), eleven)).Status == AdminOperationStatus.Rejected,
            "an eleventh GM attachment slot is rejected");

        await CheckCapacityAndRetryAsync(check);
        await CheckLegacyReceiptAsync(check);
        await CheckSingleRecipientAsync(check);
        await CheckNativeClaimsAsync(check);
        foreach (var after in new[] { false, true })
            await CheckFaultsAsync(check, after);
    }

    private static async Task CheckCapacityAndRetryAsync(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var store = f.Open(); await store.InitializeAsync();
        await store.CreateAccountAsync(new("capacity", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("capacity", new("Capacity"))).Character!;
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        state["Accounts"]![0]!["Characters"]![0]!["Mailbox"] = JsonSerializer.SerializeToNode(
            Enumerable.Range(1, 254).Select(n => new CharacterMailRecord((uint)n, "Old", "Full", 0, null, 1, 0)).ToArray());
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        var request = Read("""{"sender":"GM","text":"Capacity bundle","gold":9,"attachments":[{"attachmentKind":1,"itemId":200,"quantity":1},{"attachmentKind":1,"itemId":1000,"quantity":3}]}""");
        var original = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", original, request);
        var failed = await store.SendAdminMailBatchAsync("gm", original);
        check(failed.FailedCount == 1 && failed.Deliveries.Single().Error == "mailbox-full"
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 254,
            "one free mailbox row cannot partially deliver a two-slot bundle");
        check((await store.SendAdminCharacterMailAsync(c.Id, request)).Error == "mailbox-full"
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 254,
            "single-recipient mail also rejects the entire bundle when only one mailbox row remains");
        await store.DeleteCharacterMailAsync(c.Id, 1);
        var retryA = Guid.NewGuid(); var retryB = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", retryA, request, original);
        await store.PrepareAdminMailBatchAsync("gm", retryB, request, original);
        var results = await Task.WhenAll(store.SendAdminMailBatchAsync("gm", retryA), store.SendAdminMailBatchAsync("gm", retryB));
        var delivered = (await store.GetCharacterMailsAsync(c.Id)).Where(m => m.Text == "Capacity bundle").ToArray();
        check(results.Sum(r => r.SentCount) == 1 && results.Sum(r => r.SkippedCount) == 1
            && delivered.Length == 2 && delivered.Sum(m => m.Gold) == 9,
            "concurrent failed-recipient retries deliver the complete bundle exactly once");
    }

    private static async Task CheckLegacyReceiptAsync(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var store = f.Open(); await store.InitializeAsync();
        await store.CreateAccountAsync(new("legacy", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("legacy", new("Legacy"))).Character!;
        var id = Guid.NewGuid();
        const string oldMail = """{"Sender":"GM","Text":"Legacy","Gold":17,"AttachmentKind":1,"ItemId":200,"Quantity":1,"AvatarAbilityIndex":0}""";
        var oldFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"Mail\":" + oldMail + ",\"RetryOf\":null}")));
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        state["AdminMailBatches"] = new JsonArray(new JsonObject
        {
            ["OperationId"] = id.ToString(), ["Actor"] = "GM", ["Fingerprint"] = oldFingerprint,
            ["Mail"] = JsonNode.Parse(oldMail), ["RootOperationId"] = id.ToString(), ["RetryOf"] = null,
            ["Recipients"] = new JsonArray(new JsonObject { ["CharacterId"] = c.Id.ToString(), ["Name"] = c.Name }),
            ["Completed"] = false, ["Deliveries"] = new JsonArray()
        });
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        var replay = await store.PrepareAdminMailBatchAsync("gm", id, JsonSerializer.Deserialize<AdminSendMailRequest>(oldMail)!);
        var sent = await store.SendAdminMailBatchAsync("gm", id);
        check(replay.Replayed && replay.Status == AdminOperationStatus.Prepared && sent.SentCount == 1
            && (await store.GetCharacterMailsAsync(c.Id)).Single().Attachment?.ItemId == 200,
            "an original-format durable preview retains its fingerprint and remains sendable after upgrade");
        state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        state["AdminMailBatches"]![0]!["Deliveries"]![0]!.AsObject().Remove("MailIds");
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        var completed = await store.SendAdminMailBatchAsync("gm", id);
        check(completed.Replayed && completed.Deliveries.Single().MailId is > 0
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 1,
            "legacy completed receipts with only MailId remain readable and cannot redeliver");
    }

    private static async Task CheckSingleRecipientAsync(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var writer = new FaultWriter(false);
        var store = f.Open(writer); await store.InitializeAsync();
        await store.CreateAccountAsync(new("single", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("single", new("Single"))).Character!;
        var bad = Read("""{"sender":"GM","text":"Invalid last slot","attachments":[{"attachmentKind":1,"itemId":1000,"quantity":2},{"attachmentKind":1,"itemId":999,"quantity":1}]}""");
        var before = writer.Writes;
        var rejected = await store.SendAdminCharacterMailAsync(c.Id, bad);
        check(rejected.Status == AdminOperationStatus.Rejected && writer.Writes == before
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 0,
            "single-recipient validation rejects an invalid later slot without writing any mail");
        var slots = Enumerable.Range(0, 10).Select(_ => new AdminMailAttachmentRequest(AdminMailAttachmentKind.Item, 200)).ToArray();
        var ten = new AdminSendMailRequest("GM", "Ten", 23, Attachments: slots);
        var delivered = await store.SendAdminCharacterMailAsync(c.Id, ten);
        check(delivered.Status == AdminOperationStatus.Completed && delivered.Mails.Count == 10
            && writer.Writes == before + 1 && delivered.Mails[0].Gold == 23
            && delivered.Mails.Skip(1).All(m => m.Gold == 0)
            && delivered.Mails.Select(m => m.Attachment!.InstanceId).Distinct().Count() == 10,
            "ten single-recipient slots commit in one snapshot with one gold grant and distinct identities");
        var empty = await store.SendAdminCharacterMailAsync(c.Id, new("GM", "Gold only", 7, Attachments: []));
        check(empty.Status == AdminOperationStatus.Completed && empty.Mails is [{ Gold: 7, Attachment: null }],
            "an empty attachment list creates one gold-and-text mail");
        var text = await store.SendAdminCharacterMailAsync(c.Id, new("GM", "Text only", Attachments: []));
        check(text.Status == AdminOperationStatus.Completed && text.Mails is [{ Gold: 0, Attachment: null }],
            "an empty attachment list with zero gold still creates one text mail");
        var legacy = await store.SendAdminCharacterMailAsync(c.Id,
            new("GM", "Legacy single", 5, AdminMailAttachmentKind.Item, 1000, 4));
        check(legacy.Status == AdminOperationStatus.Completed
            && legacy.Mails is [{ Gold: 5, Attachment.ItemId: 1000, Attachment.CountOrValue: 4 }],
            "the single-recipient atomic path retains legacy attachment requests");
        var id = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", id, ten);
        slots[0] = new(AdminMailAttachmentKind.Item, 1000, 99);
        var frozen = await store.SendAdminMailBatchAsync("gm", id);
        var frozenIds = frozen.Deliveries.Single().MailIds!;
        var frozenMails = (await store.GetCharacterMailsAsync(c.Id)).Where(m => frozenIds.Contains(m.Id)).ToArray();
        check(frozenMails.Length == 10 && frozenMails.All(m => m.Attachment?.ItemId == 200),
            "prepared attachment slots are frozen even if the caller later replaces an array element");
    }

    private static async Task CheckFaultsAsync(Action<bool, string> check, bool after)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var writer = new FaultWriter(after);
        var store = f.Open(writer); await store.InitializeAsync();
        await store.CreateAccountAsync(new("mailfault", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("mailfault", new("MailFault"))).Character!;
        var request = new AdminSendMailRequest("GM", "Atomic fault", 31, Attachments:
            [new(AdminMailAttachmentKind.Item, 200), new(AdminMailAttachmentKind.Item, 1000, 3)]);
        writer.Armed = true;
        var single = await store.SendAdminCharacterMailAsync(c.Id, request);
        check(single.Status == AdminOperationStatus.Unknown && single.Mails.Count == 0
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 0
            && (await store.SendAdminCharacterMailAsync(c.Id, request)).Error == "store-quarantined",
            $"single bundle write failure afterReplace={after} does not install or retry uncertain memory state");
        store = f.Open(); await store.InitializeAsync();
        var disk = await store.GetCharacterMailsAsync(c.Id);
        check(disk.Length == (after ? 2 : 0) && disk.Sum(m => m.Gold) == (after ? 31 : 0),
            $"single bundle write failure afterReplace={after} leaves all-or-none durable mail and gold");
        writer.Armed = false; store = f.Open(writer); await store.InitializeAsync();
        var batch = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", batch, request with { Text = "Broadcast fault" });
        writer.Armed = true;
        var failed = await store.SendAdminMailBatchAsync("gm", batch);
        check(failed.Status == AdminOperationStatus.Unknown
            && !(await store.GetCharacterMailsAsync(c.Id)).Any(m => m.Text == "Broadcast fault"),
            $"broadcast bundle write failure afterReplace={after} does not install an uncertain delivery");
        store = f.Open(); await store.InitializeAsync();
        var recovered = await store.SendAdminMailBatchAsync("gm", batch);
        var delivered = (await store.GetCharacterMailsAsync(c.Id)).Where(m => m.Text == "Broadcast fault").ToArray();
        check(recovered.Status == AdminOperationStatus.Completed && recovered.Replayed == after
            && recovered.Deliveries.Single().MailIds is { Length: 2 }
            && delivered.Length == 2 && delivered.Sum(m => m.Gold) == 31,
            $"broadcast restart afterReplace={after} recovers the bundle and receipt exactly once");
    }

    private static async Task CheckNativeClaimsAsync(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        var store = f.Open(); await store.InitializeAsync();
        await store.CreateAccountAsync(new("mailclaim", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("mailclaim", new("MailClaim"))).Character!;
        await store.SaveCharacterInventoryAsync("mailclaim", c.Id, 0, [], []);
        await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(f.Options, store, f.Items);
        var sessions = services.GetRequiredService<CharacterSessionRegistry>();
        var service = ActivatorUtilities.CreateInstance<EntranceService>(services);
        await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, f.Options);
        await connection.LoginAsync("mailclaim", c.Slot);
        var sent = await store.SendAdminCharacterMailAsync(c.Id, new("GM", "Native bundle", 19, Attachments:
            [new(AdminMailAttachmentKind.Item, 1000, 2), new(AdminMailAttachmentKind.Item, 1000, 3)]));
        check(sessions.NotifyMailReceived(c.Id), "multi-mail delivery reaches the online character lease");
        var snapshot = await connection.ReadAsync(0, GameProtocolEngine.MailboxMailListNotification);
        check(snapshot.Payload[0] == 2, "online NOTI 97 presents two compatible single-item mail packages");
        foreach (var mail in sent.Mails)
        {
            var idBytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(idBytes, mail.Id);
            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, idBytes));
            var reply = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            check(reply.Payload[0] == 1, "each mail in the GM bundle is independently claimable by native CMD 98");
        }
        var claimed = (await store.GetCharacterAsync("mailclaim", c.Id))!;
        check(claimed.Gold == 19 && claimed.Inventory!.Where(i => i.ItemId == 1000).Sum(i => (long)i.CountOrValue) == 5
            && claimed.Mailbox!.All(m => m.Gold == 0 && m.Attachment is null),
            "claiming the complete bundle grants exactly the requested item quantities and gold");
        var repeatBytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(repeatBytes, sent.Mails[0].Id);
        await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, repeatBytes));
        var repeat = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
        check(repeat.Payload[0] == 0 && (await store.GetCharacterAsync("mailclaim", c.Id))!.Gold == 19,
            "a repeated native claim cannot duplicate the bundle's gold");
    }
}
