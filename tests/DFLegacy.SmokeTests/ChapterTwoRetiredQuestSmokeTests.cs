using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoRetiredQuestSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-retired-quests-");
        var previous = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(936);
            var directory = root.CreateSubdirectory("quest").FullName;
            File.WriteAllText(Path.Combine(directory, "quest.lst"), "1 `1.qst`\n2 `2.qst`\n3 `3.qst`\n4 `4.qst`\n5 `5.qst`\n", encoding);
            for (var id = 1; id <= 5; id++)
                File.WriteAllText(Path.Combine(directory, $"{id}.qst"),
                    $"[npc index] 2\n[name]\n<11::quest{id}>\n[type]\n`[meet npc]`\n[int data]\n2\n", encoding);
            File.WriteAllText(Path.Combine(directory, "quest.chn.str"),
                "quest1>不使用\nquest2>正常任务\nquest3>新增任务三\nquest4>新增任务四\nquest5>新增任务五\n", encoding);
            var options = new ServerOptions { DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = "", SkillScriptPath = root.FullName };
            options.Channel.Port = 47001;
            var items = new ItemCatalog(new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance), NullLogger<ItemCatalog>.Instance);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("retired", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("retired", new("Retired"))).Character!;
            CharacterQuestRecord[] accepted = [new(1, 11), new(2, 7), new(999, 13)];
            await store.SaveCharacterQuestsAsync("retired", character.Id, accepted, [88]);
            await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options, 47001);
            await connection.LoginAsync("retired", character.Slot);
            check((await store.GetCharacterAsync("retired", character.Id))!.Quests!.SequenceEqual(accepted),
                "Chapter One selection preserves accepted historical quests");
            await connection.EnableChapterTwoAsync();
            await connection.SendAsync(connection.Command(GameProtocolEngine.SelectCharacterCommand, character.Slot));
            await connection.ReadAsync(1, GameProtocolEngine.SelectCharacterCommand);
            await connection.ReadAsync(0, 19);
            var selected = (await store.GetCharacterAsync("retired", character.Id))!;
            check(selected.Quests!.SequenceEqual(new[] { accepted[1] }),
                "Chapter Two selection frees slots occupied by hidden and removed quests, retaining valid progress");
            check(selected.CompletedQuestIds!.SequenceEqual(new ushort[] { 88 }), "completed history is unchanged");
            var archived = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(selected))?["ChapterTwoArchivedQuests"]?.AsArray();
            check(archived?.Count == 2, "removed accepted progress is archived instead of lost");
            foreach (ushort id in new ushort[] { 3, 4 })
            {
                var body = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(body, id);
                await connection.SendAsync(connection.Command(GameProtocolEngine.AcceptQuestCommand, body));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.AcceptQuestCommand);
                check(reply.Payload[0] == 1, $"freed slot accepts new quest {id}");
            }
            var saved = (await store.GetCharacterAsync("retired", character.Id))!;
            check(saved.Quests!.Select(q => q.QuestId).SequenceEqual(new ushort[] { 2, 3, 4 }), "all three active slots are usable");
            await connection.SendAsync(connection.Command(GameProtocolEngine.AcceptQuestCommand, 5, 0));
            var fullReply = await connection.ReadAsync(1, GameProtocolEngine.AcceptQuestCommand);
            check(fullReply.Payload[0] == 0 && (await store.GetCharacterAsync("retired", character.Id))!.Quests!.SequenceEqual(saved.Quests!),
                "three valid active quests still enforce the real slot limit");
            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await reloaded.InitializeAsync();
            var persisted = (await reloaded.GetCharacterAsync("retired", character.Id))!;
            check(persisted.Quests!.SequenceEqual(saved.Quests!), "restarting preserves active quest repair");
            check(JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(persisted))?["ChapterTwoArchivedQuests"]?.AsArray().Count == 2,
                "restarting preserves archived progress");
            var catalog = provider.GetRequiredService<QuestCatalog>();
            var stable = await store.ArchiveUnavailableChapterTwoQuestsAsync("retired", character.Id, catalog);
            check(stable!.Quests!.SequenceEqual(saved.Quests!) && stable.ChapterTwoArchivedQuests!.Count == 2,
                "repeated reconciliation neither removes valid quests nor duplicates archived entries");

            var another = (await store.CreateCharacterAsync("retired", new("Retry"))).Character!;
            await store.SaveCharacterQuestsAsync("retired", another.Id, accepted, [88]);
            var missingRoot = root.CreateSubdirectory("missing-catalog").FullName;
            var missingOptions = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = missingRoot };
            var missingCatalog = new QuestCatalog(new ScriptFileSystem(missingOptions, NullLogger<ScriptFileSystem>.Instance), NullLogger<QuestCatalog>.Instance);
            var untouched = await store.ArchiveUnavailableChapterTwoQuestsAsync("retired", another.Id, missingCatalog);
            check(untouched!.Quests!.SequenceEqual(accepted), "a missing catalog cannot clear every accepted quest");
            var beforeFailure = await File.ReadAllBytesAsync(options.DataPath);
            Directory.CreateDirectory(options.DataPath + ".tmp");
            var failed = false;
            try { await store.ArchiveUnavailableChapterTwoQuestsAsync("retired", another.Id, catalog); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
            finally { Directory.Delete(options.DataPath + ".tmp"); }
            var afterFailure = await File.ReadAllBytesAsync(options.DataPath);
            check(failed && (await store.GetCharacterAsync("retired", another.Id))!.Quests!.SequenceEqual(accepted)
                && beforeFailure.SequenceEqual(afterFailure),
                "a failed save preserves both memory and disk quest progress");
            var retry = await store.ArchiveUnavailableChapterTwoQuestsAsync("retired", another.Id, catalog);
            check(retry!.Quests!.SequenceEqual(new[] { accepted[1] }) && retry.ChapterTwoArchivedQuests!.Count == 2,
                "retry after a failed save commits once without losing progress");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previous);
            root.Delete(true);
        }
    }
}
