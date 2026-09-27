using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FriendSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CheckFriendListPacket(check);
        CheckBuddyPackets(check);
        CheckFriendNameParsing(check);
        CheckWhisperParsing(check);
        await CheckFriendStorageAsync(check);
        await CheckFriendServiceAsync(check);
    }

    private static void CheckFriendListPacket(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreateFriendList(
        [
            new GameFriendEntry(Encoding.GetEncoding(936).GetBytes("阿尔法"), Online: true),
            new GameFriendEntry(Encoding.ASCII.GetBytes("Bravo"), Online: false)
        ]);

        check(packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification,
            "the friend list uses notification fifty-six, the only notification that creates entries");
        check(packet.Payload.Length == 1 + 42 * 2,
            "the friend list is a byte count plus 42-byte records");
        check(packet.Payload[0] == 2, "the friend list header carries the entry count");

        var first = packet.Payload.AsSpan(1, 42);
        check(first[0] == 0 && first[1] == 0 && first[2] == 0,
            "a record starts with state, flag and reserved bytes");
        var firstName = first.Slice(3, 30);
        check(firstName[..6].SequenceEqual(Encoding.GetEncoding(936).GetBytes("阿尔法"))
            && firstName[6..].ToArray().All(b => b == 0),
            "the name occupies a NUL-padded thirty-byte field");
        check(first[0x21] == 0 && first[0x22] == 0 && first[0x23] == 0 && first[0x24] == 0
            && first[0x25] == 0 && first[0x26] == 0 && first[0x27] == 0 && first[0x28] == 0
            && first[0x29] == 0,
            "the five fields with unknown semantics are zero-filled for now");

        var second = packet.Payload.AsSpan(1 + 42, 42);
        check(second.Slice(3, 30)[..5].SequenceEqual(Encoding.ASCII.GetBytes("Bravo")),
            "an ASCII name uses the same thirty-byte field");
        check(packet.Payload[1 + 42 + 0x20] == 0,
            "the last byte of the name field stays inside the record");
    }

    private static void CheckBuddyPackets(Action<bool, string> check)
    {
        var nameBytes = Encoding.ASCII.GetBytes("Bravo");
        var buddyIn = GameProtocolEngine.CreateBuddyIn(state: 0, nameBytes, flag: 0);
        check(buddyIn.ProtocolId == GameProtocolEngine.BuddyInNotification,
            "an online friend uses BUDDY_IN");
        check(buddyIn.Payload.Length == 1 + 4 + nameBytes.Length + 1
            && buddyIn.Payload[0] == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(buddyIn.Payload.AsSpan(1, 4)) == nameBytes.Length
            && buddyIn.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes)
            && buddyIn.Payload[^1] == 0,
            "BUDDY_IN is state + length-prefixed name + flag");

        var buddyOut = GameProtocolEngine.CreateBuddyOut(nameBytes, flag: 0);
        check(buddyOut.ProtocolId == GameProtocolEngine.BuddyOutNotification,
            "an offline friend uses BUDDY_OUT");
        check(buddyOut.Payload.Length == 1 + 4 + nameBytes.Length + 1
            && buddyOut.Payload[0] == 0,
            "BUDDY_OUT's first byte is ignored by the client and sent as zero");
    }

    private static void CheckFriendNameParsing(Action<bool, string> check)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes("阿尔法");
        var body = new byte[2 + 4 + nameBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2, 4), (uint)nameBytes.Length);
        nameBytes.CopyTo(body, 6);
        check(GameProtocolEngine.TryReadFriendName(body, out var parsed)
            && parsed.SequenceEqual(nameBytes),
            "ADD_FRIEND/REMOVE_FRIEND carry a length-prefixed name after the sequence number");

        check(!GameProtocolEngine.TryReadFriendName(body[..5], out _),
            "a truncated friend command is rejected");
        check(!GameProtocolEngine.TryReadFriendName([0, 0, 0, 0, 0, 0], out _),
            "an empty friend name is rejected");
        var longName = new byte[2 + 4 + 31];
        BinaryPrimitives.WriteUInt32LittleEndian(longName.AsSpan(2, 4), 31);
        longName.AsSpan(6).Fill((byte)'a'); // 全非零：长度上限必须是唯一的拒绝理由
        check(!GameProtocolEngine.TryReadFriendName(longName, out _),
            "a name longer than thirty bytes is rejected");
        var embeddedNul = new byte[2 + 4 + 3];
        BinaryPrimitives.WriteUInt32LittleEndian(embeddedNul.AsSpan(2, 4), 3);
        embeddedNul[6] = (byte)'a';
        embeddedNul[7] = 0;
        check(!GameProtocolEngine.TryReadFriendName(embeddedNul, out _),
            "a name containing a NUL byte is rejected");
    }

    private static void CheckWhisperParsing(Action<bool, string> check)
    {
        var text = Encoding.GetEncoding(936).GetBytes("悄悄话正文");
        var target = Encoding.GetEncoding(936).GetBytes("Bravo");
        var body = new byte[2 + 1 + 2 + 4 + 4 + text.Length + 4 + target.Length];
        body[2] = 1;                     // message type 1 = whisper
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9, 4), (uint)text.Length);
        text.CopyTo(body, 13);
        BinaryPrimitives.WriteUInt32LittleEndian(
            body.AsSpan(13 + text.Length, 4),
            (uint)target.Length);
        target.CopyTo(body, 17 + text.Length);

        check(GameProtocolEngine.TryReadWhisperTarget(
                body, text.Length, out var parsedTarget)
            && parsedTarget.SequenceEqual(target),
            "a whisper appends a length-prefixed target name after the text");

        check(!GameProtocolEngine.TryReadWhisperTarget(body[..(body.Length - 1)], text.Length, out _),
            "a whisper without a target name is rejected");
    }

    private static async Task CheckFriendStorageAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dflegacy-friend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(directory, "state.json"),
                ScriptPvfPath = "unused.pvf"
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);

            const string owner = "owner";
            const string other = "other";
            await store.CreateAccountAsync(
                new CreateAccountRequest(owner, "password", OicqCode: "123456"),
                CancellationToken.None);
            await store.CreateAccountAsync(
                new CreateAccountRequest(other, "password", OicqCode: "123456"),
                CancellationToken.None);
            await store.CreateCharacterAsync(
                owner,
                new CreateCharacterRequest("圣骑士", GrowType: 4),
                CancellationToken.None);
            await store.CreateCharacterAsync(
                other,
                new CreateCharacterRequest("法师", GrowType: 3),
                CancellationToken.None);

            check((await store.GetFriendsAsync(owner, CancellationToken.None)).Count == 0,
                "a fresh account has no friends");
            check(await store.AddFriendAsync(owner, "法师", CancellationToken.None) == FriendUpdateFailure.None,
                "adding an existing character name succeeds");
            check(await store.AddFriendAsync(owner, "法师", CancellationToken.None) == FriendUpdateFailure.AlreadyFriend,
                "adding the same friend twice is rejected");
            check(await store.AddFriendAsync(owner, "圣骑士", CancellationToken.None) == FriendUpdateFailure.Self,
                "adding your own character is rejected");
            check(await store.AddFriendAsync(owner, "不存在的人", CancellationToken.None)
                    == FriendUpdateFailure.CharacterNotFound,
                "adding an unknown character is rejected");

            var friends = await store.GetFriendsAsync(owner, CancellationToken.None);
            check(friends.Count == 1 && friends[0].Name == "法师",
                "the friend list round-trips through the save file");
            check((await store.GetFriendsAsync(other, CancellationToken.None)).Count == 0,
                "adding a friend is one-way: the other account's list is untouched");

            check(await store.AddFriendAsync("ghost-account", "法师", CancellationToken.None)
                    == FriendUpdateFailure.AccountNotFound,
                "adding a friend to an unknown account reports AccountNotFound");
            check(await store.RemoveFriendAsync("ghost-account", "法师", CancellationToken.None)
                    == FriendUpdateFailure.AccountNotFound,
                "removing a friend from an unknown account reports AccountNotFound");
            check((await store.GetFriendsAsync(owner, CancellationToken.None)).Count == 1
                    && (await store.GetFriendsAsync(other, CancellationToken.None)).Count == 0,
                "unknown-account updates leave existing friend lists untouched");

            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync(CancellationToken.None);
            var persisted = await reloaded.GetFriendsAsync(owner, CancellationToken.None);
            check(persisted.Count == 1 && persisted[0].Name == "法师" && persisted[0].AddedAtUnix > 0,
                "the friend list is written to disk and survives a store reload");

            check(await store.RemoveFriendAsync(owner, "法师", CancellationToken.None) == FriendUpdateFailure.None
                && (await store.GetFriendsAsync(owner, CancellationToken.None)).Count == 0,
                "removing a friend works");
            check(await store.RemoveFriendAsync(owner, "法师", CancellationToken.None) == FriendUpdateFailure.NotFriend,
                "removing a non-friend is rejected");

            // trim：存下来的是去掉首尾空格后的调用方名字
            check(await store.AddFriendAsync(owner, "  法师  ", CancellationToken.None) == FriendUpdateFailure.None,
                "a friend name with surrounding whitespace is accepted");
            var trimmed = await store.GetFriendsAsync(owner, CancellationToken.None);
            check(trimmed.Count == 1 && trimmed[0].Name == "法师",
                "the stored friend name is the trimmed caller name");
            check(await store.AddFriendAsync(owner, "法师", CancellationToken.None) == FriendUpdateFailure.AlreadyFriend,
                "the trimmed name is what duplicate detection compares");
            check(await store.RemoveFriendAsync(owner, "  法师  ", CancellationToken.None) == FriendUpdateFailure.None
                    && (await store.GetFriendsAsync(owner, CancellationToken.None)).Count == 0,
                "removal trims the caller's surrounding whitespace too");

            // 大小写：存储保留调用方大小写，角色名匹配大小写不敏感
            await store.CreateCharacterAsync(
                other,
                new CreateCharacterRequest("Bravo"),
                CancellationToken.None);
            check(await store.AddFriendAsync(owner, "BRAVO", CancellationToken.None) == FriendUpdateFailure.None,
                "an existing character is found regardless of the caller's casing");
            var cased = await store.GetFriendsAsync(owner, CancellationToken.None);
            check(cased.Count == 1 && cased[0].Name == "BRAVO",
                "the stored friend name keeps the caller's casing, not the character's");
            check(await store.AddFriendAsync(owner, "Bravo", CancellationToken.None) == FriendUpdateFailure.AlreadyFriend,
                "a differently cased duplicate is still rejected");
            check(await store.RemoveFriendAsync(owner, "bravo", CancellationToken.None) == FriendUpdateFailure.None
                    && (await store.GetFriendsAsync(owner, CancellationToken.None)).Count == 0,
                "removal matches the friend name case-insensitively");

            // 上限 32：好友角色建在各自独立的账号上（同一账号的角色槽位有限）
            for (var index = 0; index < 33; index++)
            {
                var friendAccount = $"friend{index:D2}";
                await store.CreateAccountAsync(
                    new CreateAccountRequest(friendAccount, "password", OicqCode: "123456"),
                    CancellationToken.None);
                await store.CreateCharacterAsync(
                    friendAccount,
                    new CreateCharacterRequest($"好友{index:D2}"),
                    CancellationToken.None);
            }

            var added = 0;
            for (var index = 0; index < 32; index++)
            {
                if (await store.AddFriendAsync(owner, $"好友{index:D2}", CancellationToken.None)
                    == FriendUpdateFailure.None)
                {
                    added++;
                }
            }

            check(added == 32, "the first thirty-two friends are accepted");
            check(await store.AddFriendAsync(owner, "好友32", CancellationToken.None)
                    == FriendUpdateFailure.FriendListFull,
                "the thirty-third friend is rejected because the client's window holds thirty-two");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckFriendServiceAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dflegacy-friend-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(directory, "state.json"),
                ScriptPvfPath = "unused.pvf"
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            var registry = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var service = new FriendService(store, registry, NullLogger<FriendService>.Instance);
            var encoding = Encoding.GetEncoding(936);

            await store.CreateAccountAsync(new CreateAccountRequest("alice", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("bobby", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("dave", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("erin", "password", OicqCode: "123456"), CancellationToken.None);
            var aliceFixture = await store.CreateCharacterAsync(
                "alice", new CreateCharacterRequest("阿尔法", GrowType: 3), CancellationToken.None);
            var bobFixture = await store.CreateCharacterAsync(
                "bobby", new CreateCharacterRequest("布拉沃", GrowType: 2), CancellationToken.None);
            var daveFixture = await store.CreateCharacterAsync(
                "dave", new CreateCharacterRequest("戴夫", GrowType: 4), CancellationToken.None);
            var erinFixture = await store.CreateCharacterAsync(
                "erin", new CreateCharacterRequest("艾琳", GrowType: 3), CancellationToken.None);
            check(aliceFixture.Created && bobFixture.Created && daveFixture.Created && erinFixture.Created,
                "the friend service fixture creates its characters");

            // 登记在线（假通道只记录收到的包）
            var alicePackets = new List<GameServerPacket>();
            service.AttachChannel("alice", "阿尔法", packet =>
            {
                alicePackets.Add(packet);
                return ValueTask.CompletedTask;
            });

            await store.AddFriendAsync("alice", "布拉沃", CancellationToken.None);
            await service.RegisterOnlineAsync("alice", "阿尔法", CancellationToken.None);

            var snapshots = alicePackets
                .Where(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification)
                .ToArray();
            check(snapshots.Length == 1
                    && snapshots[0].Payload[0] == 1
                    && HasFriendName(snapshots[0], 0, "布拉沃"),
                "logging in sends exactly one friend-list snapshot holding the account's friends");
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification),
                "an offline friend is marked offline by BUDDY_OUT right after the snapshot");
            check(alicePackets.All(packet => packet.ProtocolId != GameProtocolEngine.BuddyInNotification),
                "an offline friend is never announced as online with BUDDY_IN");

            // 无关账号：在线但没有把任何人加进名单
            var davePackets = new List<GameServerPacket>();
            service.AttachChannel("dave", "戴夫", packet =>
            {
                davePackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            await service.RegisterOnlineAsync("dave", "戴夫", CancellationToken.None);
            check(davePackets.Count == 0,
                "an account with no friends receives no snapshot because there is nothing to create");
            var daveBeforeStranger = davePackets.Count;

            var bravoPackets = new List<GameServerPacket>();
            service.AttachChannel("bobby", "布拉沃", packet =>
            {
                bravoPackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            await service.RegisterOnlineAsync("bobby", "布拉沃", CancellationToken.None);
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification),
                "a friend coming online notifies the accounts whose friend list holds that name");
            check(davePackets.Count == daveBeforeStranger,
                "an online account with no matching friend entry receives no buddy notification");

            await service.UnregisterOnlineAsync("bobby", "布拉沃", CancellationToken.None);
            check(alicePackets.Count(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification) >= 2,
                "a friend going offline notifies the same accounts with BUDDY_OUT");
            check(davePackets.Count == daveBeforeStranger,
                "an unrelated account stays silent when somebody else's friend goes offline");

            var removed = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "布拉沃", CancellationToken.None);
            check(alicePackets.Skip(removed)
                    .Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification
                        && packet.Payload[0] == 0),
                "removing a friend re-sends an empty snapshot");
            check((await store.GetFriendsAsync("alice", CancellationToken.None)).Count == 0,
                "the service persists the removal through the store");

            var afterRemove = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "不存在的人", CancellationToken.None);
            var failedRemoval = alicePackets.Skip(afterRemove).ToArray();
            var notice = failedRemoval
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification);
            check(notice is not null && notice.Payload[0] == (byte)GameMessageType.Type03White,
                "a failed friend operation answers with a white system notice instead of silence");
            check(notice is not null && ReadMessageText(notice) == "不是好友",
                "a failed removal explains itself with 不是好友");
            check(failedRemoval.All(packet =>
                    packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "a failed removal does not re-send the friend-list snapshot");
            check((await store.GetFriendsAsync("alice", CancellationToken.None)).Count == 0,
                "a failed removal changes no state");

            var afterOfflineWhisper = alicePackets.Count;
            var bravoBeforeOfflineWhisper = bravoPackets.Count;
            await service.WhisperAsync(
                "alice",
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                Encoding.GetEncoding(936).GetBytes("布拉沃"),
                messageType: 1,
                Encoding.GetEncoding(936).GetBytes("你好"),
                CancellationToken.None);
            var offlineNotice = alicePackets.Skip(afterOfflineWhisper)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification);
            check(bravoPackets.Count == bravoBeforeOfflineWhisper
                    && offlineNotice is not null
                    && offlineNotice.Payload[0] == (byte)GameMessageType.Type03White,
                "whispering to an offline friend only tells the sender");
            check(offlineNotice is not null && ReadMessageText(offlineNotice) == "对方不在线",
                "the offline whisper notice says 对方不在线");

            service.AttachChannel("bobby", "布拉沃", packet =>
            {
                bravoPackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            await service.RegisterOnlineAsync("bobby", "布拉沃", CancellationToken.None);
            var bravoBefore = bravoPackets.Count;
            var aliceBeforeOnlineWhisper = alicePackets.Count;
            await service.WhisperAsync(
                "alice",
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                Encoding.GetEncoding(936).GetBytes("布拉沃"),
                messageType: 1,
                Encoding.GetEncoding(936).GetBytes("你好"),
                CancellationToken.None);
            var delivered = bravoPackets.Skip(bravoBefore)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            check(delivered.Length == 1 && delivered[0].Payload[1] == 1,
                "a whisper reaches the online target with the sender uid the client resolves to a name");
            check(bravoPackets.Skip(bravoBefore)
                    .All(packet => packet.ProtocolId != GameProtocolEngine.UserInfoNotification),
                "a null sender appearance pushes no user-info record");
            var echoed = alicePackets.Skip(aliceBeforeOnlineWhisper)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            check(echoed.Length == 1
                    && echoed[0].Payload[0] == 1
                    && ReadMessageText(echoed[0]) == "你好",
                "the sender sees its own whisper echoed once");

            // 陈旧会话的注销不能把新会话踢下线（账号不匹配时必须静默忽略）
            var aliceBeforeStaleUnregister = alicePackets.Count;
            var bravoBeforeStaleUnregister = bravoPackets.Count;
            await service.UnregisterOnlineAsync("dave", "布拉沃", CancellationToken.None);
            await service.WhisperAsync(
                "alice",
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                Encoding.GetEncoding(936).GetBytes("布拉沃"),
                messageType: 1,
                Encoding.GetEncoding(936).GetBytes("你好"),
                CancellationToken.None);
            check(alicePackets.Skip(aliceBeforeStaleUnregister).All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyOutNotification)
                    && bravoPackets.Skip(bravoBeforeStaleUnregister)
                        .Any(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification),
                "a stale unregister from another account cannot take a live session offline");

            // 加好友成功：重推快照，离线的新好友随后被标 -1
            var daveBeforeAdd = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "艾琳", CancellationToken.None);
            var addedSnapshot = davePackets.Skip(daveBeforeAdd)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification);
            check(addedSnapshot is not null
                    && addedSnapshot.Payload[0] == 1
                    && HasFriendName(addedSnapshot, 0, "艾琳"),
                "a successful add re-sends the adder's snapshot with the new friend");
            check(davePackets.Skip(daveBeforeAdd).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && BuddyNameEquals(packet, "艾琳")),
                "an offline friend is marked offline right after the add snapshot");
            check((await store.GetFriendsAsync("dave", CancellationToken.None)).Count == 1,
                "a successful add is persisted by the store");

            var erinPackets = new List<GameServerPacket>();
            service.AttachChannel("erin", "艾琳", packet =>
            {
                erinPackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            var daveBeforeErinOnline = davePackets.Count;
            await service.RegisterOnlineAsync("erin", "艾琳", CancellationToken.None);
            var buddyIn = davePackets.Skip(daveBeforeErinOnline)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification);
            check(buddyIn is not null
                    && buddyIn.Payload[0] == 0
                    && BuddyNameEquals(buddyIn, "艾琳"),
                "an added friend coming online is announced to the account that holds the name");

            var appearance = GameProtocolEngine.CreateCurrentCharacterInfo(
                new GameCharacterSummary(0, encoding.GetBytes("戴夫"), Job: 0, GrowType: 4, Level: 20),
                userId: 7);
            var erinBeforeWhisper = erinPackets.Count;
            var daveBeforeWhisper = davePackets.Count;
            await service.WhisperAsync(
                "dave",
                "戴夫",
                senderAreaUserId: 7,
                senderAppearance: appearance,
                encoding.GetBytes("艾琳"),
                messageType: 1,
                encoding.GetBytes("在吗"),
                CancellationToken.None);
            var erinMessages = erinPackets.Skip(erinBeforeWhisper).ToList();
            var appearanceIndex = erinMessages.FindIndex(packet => ReferenceEquals(packet, appearance));
            var messageIndex = erinMessages.FindIndex(packet =>
                packet.ProtocolId == GameProtocolEngine.MessageNotification);
            check(appearanceIndex >= 0 && messageIndex > appearanceIndex,
                "a whisper pushes the sender's appearance record to the target before the text");
            check(messageIndex >= 0
                    && erinMessages[messageIndex].Payload[1] == 7
                    && ReadMessageText(erinMessages[messageIndex]) == "在吗",
                "the online whisper carries the sender uid and the text");
            var daveEcho = davePackets.Skip(daveBeforeWhisper)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            check(daveEcho.Length == 1 && ReadMessageText(daveEcho[0]) == "在吗",
                "the sender sees the appearance whisper echoed back to itself once");

            // DetachChannel 单独调用也必须把角色离线告诉好友
            var erinWentOffline = new TaskCompletionSource();
            service.AttachChannel("dave", "戴夫", packet =>
            {
                davePackets.Add(packet);
                if (packet.ProtocolId == GameProtocolEngine.BuddyOutNotification)
                {
                    erinWentOffline.TrySetResult();
                }

                return ValueTask.CompletedTask;
            });
            var daveBeforeDetach = davePackets.Count;
            service.DetachChannel("艾琳");
            _ = await Task.WhenAny(
                erinWentOffline.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            check(erinWentOffline.Task.IsCompleted
                    && davePackets.Skip(daveBeforeDetach).Any(packet =>
                        packet.ProtocolId == GameProtocolEngine.BuddyOutNotification
                        && BuddyNameEquals(packet, "艾琳")),
                "a detached session alone still marks the character offline for its friends");
            var erinAfterDetach = erinPackets.Count;
            await service.WhisperAsync(
                "dave",
                "戴夫",
                senderAreaUserId: 7,
                senderAppearance: null,
                encoding.GetBytes("艾琳"),
                messageType: 1,
                encoding.GetBytes("还在吗"),
                CancellationToken.None);
            check(erinPackets.Count == erinAfterDetach
                    && davePackets.Skip(daveBeforeDetach).Any(packet =>
                        packet.ProtocolId == GameProtocolEngine.MessageNotification
                        && ReadMessageText(packet) == "对方不在线"),
                "a whisper to a detached character goes nowhere and tells the sender why");

            // 没有通道就是不在线：登记也不能对外宣告上线
            var daveBeforeUnattachedRegister = davePackets.Count;
            await service.RegisterOnlineAsync("erin", "艾琳", CancellationToken.None);
            check(davePackets.Count == daveBeforeUnattachedRegister,
                "registering a character without a channel announces nothing to its friends");

            // 失败提示逐条映射，且失败不改变任何状态
            var daveBeforeFailedAdd = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "不存在的人", CancellationToken.None);
            var failedAdd = davePackets.Skip(daveBeforeFailedAdd).ToArray();
            check(failedAdd.FirstOrDefault(packet =>
                        packet.ProtocolId == GameProtocolEngine.MessageNotification) is { } notFound
                    && ReadMessageText(notFound) == "找不到该角色",
                "adding an unknown character explains itself with 找不到该角色");
            check(failedAdd.All(packet => packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "a failed add does not re-send the snapshot");
            var daveFriends = await store.GetFriendsAsync("dave", CancellationToken.None);
            check(daveFriends.Count == 1 && daveFriends[0].Name == "艾琳",
                "a failed add changes no state");

            var daveBeforeSelf = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "戴夫", CancellationToken.None);
            check(davePackets.Skip(daveBeforeSelf).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "不能加自己"),
                "adding your own character explains itself with 不能加自己");

            var daveBeforeDuplicate = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "艾琳", CancellationToken.None);
            check(davePackets.Skip(daveBeforeDuplicate).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "已经是好友"),
                "adding an existing friend explains itself with 已经是好友");

            var ghostPackets = new List<GameServerPacket>();
            service.AttachChannel("ghost-account", "幽灵", packet =>
            {
                ghostPackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            await service.AddFriendAsync("ghost-account", "幽灵", "艾琳", CancellationToken.None);
            check(ghostPackets.Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "操作失败"),
                "an operation that fails without a specific reason says 操作失败");

            // 上限 32：33 个备胎角色放在两个账号上（每个账号最多 24 个可见角色）
            await store.CreateAccountAsync(new CreateAccountRequest("full", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("crowd0", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("crowd1", "password", OicqCode: "123456"), CancellationToken.None);
            check((await store.CreateCharacterAsync("full", new CreateCharacterRequest("满载"), CancellationToken.None)).Created,
                "the full-list fixture creates its character");
            var crowdNames = new List<string>();
            for (var index = 0; index < 33; index++)
            {
                var name = $"备胎{index:D2}";
                await store.CreateCharacterAsync(
                    index < MaximumTransferRoster.MaximumVisibleCharacters ? "crowd0" : "crowd1",
                    new CreateCharacterRequest(name),
                    CancellationToken.None);
                crowdNames.Add(name);
            }

            for (var index = 0; index < 32; index++)
            {
                await store.AddFriendAsync("full", crowdNames[index], CancellationToken.None);
            }

            var fullPackets = new List<GameServerPacket>();
            service.AttachChannel("full", "满载", packet =>
            {
                fullPackets.Add(packet);
                return ValueTask.CompletedTask;
            });
            await service.AddFriendAsync("full", "满载", crowdNames[32], CancellationToken.None);
            check(fullPackets.Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "好友已满"),
                "a full friend list explains itself with 好友已满 instead of adding a thirty-third friend");
            check((await store.GetFriendsAsync("full", CancellationToken.None)).Count == 32,
                "the rejected thirty-third friend is not added");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool HasFriendName(GameServerPacket snapshot, int index, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        var field = snapshot.Payload.AsSpan(1 + index * 42 + 3, 30);
        return field[..nameBytes.Length].SequenceEqual(nameBytes)
            && field[nameBytes.Length..].ToArray().All(value => value == 0);
    }

    private static bool BuddyNameEquals(GameServerPacket packet, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes);
    }

    private static string ReadMessageText(GameServerPacket packet) =>
        Encoding.GetEncoding(936).GetString(
            packet.Payload,
            7,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(3, 4)));
}
