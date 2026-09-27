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
        CheckFriendCommandResults(check);
        CheckMessengerMessage(check);
        await CheckUserInfoRequestBranchAsync(check);
        await CheckFriendStorageAsync(check);
        await CheckFriendServiceAsync(check);
    }

    // type=1 命令通道上的加/删好友结果：客户端接收循环把包头 type 1 的 body 首字节当结果
    // 标志，只有这两条包能打印“[名字]已被添加到好友名单”/真正 vector::erase 删除条目。
    private static void CheckFriendCommandResults(Action<bool, string> check)
    {
        check(GameProtocolEngine.CommandPacketType == 1
                && GameProtocolEngine.AddFriendCommand == 63
                && GameProtocolEngine.RemoveFriendCommand == 64
                && GameProtocolEngine.MessageOtherChannelNotification == 65,
            "the friend ids stay on the client's tables: type 1, add 63, remove 64, messenger 65");

        var asciiName = Encoding.ASCII.GetBytes("Bravo");
        var add = GameProtocolEngine.CreateAddFriendResult(asciiName);
        check(add.Type == 1 && add.ProtocolId == 63,
            "a successful add answers on the type-1 command channel with id 63");
        check(add.Payload.SequenceEqual(
                new byte[] { 1, 5, 0, 0, 0, (byte)'B', (byte)'r', (byte)'a', (byte)'v', (byte)'o', 0 }),
            "the add result bytes are exactly [non-zero flag][u32 name length + name][error code 0]");

        var gbkName = Encoding.GetEncoding(936).GetBytes("布拉沃");
        var remove = GameProtocolEngine.CreateRemoveFriendResult(gbkName);
        check(remove.Type == 1 && remove.ProtocolId == 64,
            "a successful removal answers on the type-1 command channel with id 64");
        check(remove.Payload.Length == 1 + sizeof(uint) + gbkName.Length + 1
                && remove.Payload[0] != 0
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    remove.Payload.AsSpan(1, sizeof(uint))) == gbkName.Length
                && remove.Payload.AsSpan(5, gbkName.Length).SequenceEqual(gbkName)
                && remove.Payload[^1] == 0,
            "the removal result repeats the [flag][name][error code] body that erases the entry by name");

        var rejectedEmptyName = false;
        try
        {
            GameProtocolEngine.CreateAddFriendResult([]);
        }
        catch (ArgumentException)
        {
            rejectedEmptyName = true;
        }

        check(rejectedEmptyName, "an empty friend name cannot produce a command result packet");
    }

    // CMD 17 类型 18 是 1:1 窗口消息：正文尾部带对端名字，客户端用 NOTI 65 按名字找/建窗口。
    private static void CheckMessengerMessage(Action<bool, string> check)
    {
        var text = Encoding.GetEncoding(936).GetBytes("在吗");
        var peer = Encoding.GetEncoding(936).GetBytes("Bravo");
        var body = new byte[2 + 1 + 2 + 4 + 4 + text.Length + 4 + peer.Length];
        body[2] = 0x12; // 类型 18
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9, 4), (uint)text.Length);
        text.CopyTo(body, 13);
        BinaryPrimitives.WriteUInt32LittleEndian(
            body.AsSpan(13 + text.Length, 4),
            (uint)peer.Length);
        peer.CopyTo(body, 17 + text.Length);

        check(EntranceService.TryReadSendMessage(body, out var parsed)
                && parsed.MessageType == (byte)GameMessageType.Type18DirectOutput
                && parsed.TargetOrItemSpace == 0
                && parsed.SlotOrReserved == 0
                && parsed.MessageBytes.SequenceEqual(text),
            "a CMD 17 type-18 1:1 body parses with the shared chat layout");
        check(GameProtocolEngine.TryReadWhisperTarget(body, text.Length, out var parsedPeer)
                && parsedPeer.SequenceEqual(peer),
            "the 1:1 body ends with a length-prefixed peer name");
        check(!EntranceService.TryReadSendMessage(body[..(13 + text.Length)], out _),
            "a type-18 body without a peer name is rejected");

        var packet = GameProtocolEngine.CreateMessageOtherChannel(peer, text);
        check(packet.Type == 0 && packet.ProtocolId == 65,
            "1:1 traffic is carried by NOTI 65");
        check(packet.Payload.Length == 2 + 4 + peer.Length + 4 + text.Length
                && packet.Payload[0] == 0x12
                && packet.Payload[1] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(2, 4))
                    == peer.Length
                && packet.Payload.AsSpan(6, peer.Length).SequenceEqual(peer)
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    packet.Payload.AsSpan(6 + peer.Length, 4)) == text.Length
                && packet.Payload.AsSpan(10 + peer.Length, text.Length).SequenceEqual(text),
            "NOTI 65 is [type 0x12][flag 1][u32 name + name][u32 text + text]");
    }

    // CMD 8 的 mode 分支：角色选择(0xFFFF + mode 2) 保持账号角色卡列表，城镇“查看信息”
    // (uid + mode 3) 必须回该角色资料；一律回 mode 2 列表正是“通关[%s]…”弹窗的成因。
    private static async Task CheckUserInfoRequestBranchAsync(Action<bool, string> check)
    {
        var rosterBody = new byte[] { 0, 0, 0xFF, 0xFF, 2 };
        var rosterKind = EntranceService.ClassifyUserInfoRequest(rosterBody, out var rosterUid);
        check(rosterKind == EntranceService.UserInfoReplyKind.Roster && rosterUid == 0,
            "a mode-2 GET_USERINFO keeps the account-roster reply");

        var rosterInvoked = false;
        var lookupInvoked = false;
        var detailInvoked = false;
        var roster = GameProtocolEngine.CreateUserInfo([]);
        var resolvedRoster = await EntranceService.ResolveUserInfoReplyAsync(
            rosterBody,
            _ =>
            {
                lookupInvoked = true;
                return "谁";
            },
            (_, _) =>
            {
                detailInvoked = true;
                return roster;
            },
            () =>
            {
                rosterInvoked = true;
                return Task.FromResult(roster);
            });
        check(rosterInvoked && !detailInvoked && !lookupInvoked
                && ReferenceEquals(resolvedRoster, roster),
            "a mode-2 request still resolves to the account roster and never looks a uid up");
        check(roster.Payload.Length == 3
                && roster.Payload[0] == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(1, 2)) == 0,
            "the roster reply is still USERINFO mode 2 with the account's character count");

        var detailBody = new byte[] { 0x34, 0x12, 0x78, 0x56, 3 };
        var detailKind = EntranceService.ClassifyUserInfoRequest(detailBody, out var detailUid);
        check(detailKind == EntranceService.UserInfoReplyKind.CharacterDetail
                && detailUid == 0x5678,
            "a mode-3 GET_USERINFO asks for that uid's character detail");

        rosterInvoked = false;
        lookupInvoked = false;
        detailInvoked = false;
        ushort detailFactoryUid = 0;
        var detailFactoryName = string.Empty;
        var resolvedDetail = await EntranceService.ResolveUserInfoReplyAsync(
            detailBody,
            uid =>
            {
                lookupInvoked = true;
                check(uid == 0x5678, "the uid reverse lookup receives the requested uid");
                return "艾琳";
            },
            (uid, name) =>
            {
                detailInvoked = true;
                detailFactoryUid = uid;
                detailFactoryName = name;
                return GameProtocolEngine.CreateCurrentCharacterDetails(uid);
            },
            () =>
            {
                rosterInvoked = true;
                return Task.FromResult(roster);
            });
        check(lookupInvoked && detailInvoked && !rosterInvoked
                && detailFactoryUid == 0x5678
                && detailFactoryName == "艾琳",
            "a mode-3 request looks the uid up and resolves to that character's detail, never the roster");
        check(resolvedDetail is not null
                && resolvedDetail.ProtocolId == GameProtocolEngine.UserInfoNotification
                && resolvedDetail.Payload[0] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(resolvedDetail.Payload.AsSpan(1, 2)) == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    resolvedDetail.Payload.AsSpan(3, 2)) == 0x5678,
            "the mode-3 reply is USERINFO mode 1, count 1, echoing the requested uid");
        check(resolvedDetail is not null
                && resolvedDetail.Payload.Length == 97
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    resolvedDetail.Payload.AsSpan(9, 4)) == 81,
            "the detail record starts with uid, a u32 and the 81-byte stat blob the mode-1/3 parser reads");

        // uid 无人持有（离线/别的区）：反查落空 → 不回包。
        rosterInvoked = false;
        detailInvoked = false;
        var resolvedUnknown = await EntranceService.ResolveUserInfoReplyAsync(
            detailBody,
            _ => null,
            (_, _) =>
            {
                detailInvoked = true;
                return roster;
            },
            () =>
            {
                rosterInvoked = true;
                return Task.FromResult(roster);
            });
        check(resolvedUnknown is null && !detailInvoked && !rosterInvoked,
            "a mode-3 request for a uid no online character holds gets no reply at all");

        var emptyUidBody = new byte[] { 0, 0, 0, 0, 3 };
        check(EntranceService.ClassifyUserInfoRequest(emptyUidBody, out var emptyUid)
                == EntranceService.UserInfoReplyKind.Ignore
                && emptyUid == 0,
            "a mode-3 GET_USERINFO without a uid is ignored instead of answered with the roster");
        rosterInvoked = false;
        lookupInvoked = false;
        detailInvoked = false;
        var resolvedIgnore = await EntranceService.ResolveUserInfoReplyAsync(
            emptyUidBody,
            _ =>
            {
                lookupInvoked = true;
                return null;
            },
            (_, _) =>
            {
                detailInvoked = true;
                return roster;
            },
            () =>
            {
                rosterInvoked = true;
                return Task.FromResult(roster);
            });
        check(resolvedIgnore is null && !lookupInvoked && !detailInvoked && !rosterInvoked,
            "a uid-less mode-3 request resolves to no reply at all and never reaches the lookup");
        check(EntranceService.ClassifyUserInfoRequest([0, 0], out _)
                == EntranceService.UserInfoReplyKind.Roster,
            "a GET_USERINFO without a payload keeps the legacy roster behaviour");
    }

    private static void CheckFriendListPacket(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreateFriendList(
        [
            new GameFriendEntry(
                Encoding.GetEncoding(936).GetBytes("阿尔法"),
                Online: true,
                AreaUserId: 0x1234),
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
        check(BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(0x21, 2)) == 0x1234,
            "an online record carries its area user id in the u16 the client's 查看信息 sends back");
        check(first[0x23] == 0 && first[0x24] == 0
            && first[0x25] == 0 && first[0x26] == 0 && first[0x27] == 0 && first[0x28] == 0
            && first[0x29] == 0,
            "the four fields with unknown semantics are zero-filled for now");

        var second = packet.Payload.AsSpan(1 + 42, 42);
        check(second.Slice(3, 30)[..5].SequenceEqual(Encoding.ASCII.GetBytes("Bravo")),
            "an ASCII name uses the same thirty-byte field");
        check(BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(0x21, 2)) == 0,
            "a record left at the default carries a zero area user id");
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
            var service = new FriendService(store, NullLogger<FriendService>.Instance);
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

            await store.AddFriendAsync("alice", "布拉沃", CancellationToken.None);
            var alicePackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "alice", "阿尔法", areaUserId: 11, Guid.NewGuid(), Record(alicePackets), CancellationToken.None);

            var snapshots = alicePackets
                .Where(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification)
                .ToArray();
            check(snapshots.Length == 1
                    && snapshots[0].Payload[0] == 1
                    && HasFriendName(snapshots[0], 0, "布拉沃"),
                "logging in sends exactly one friend-list snapshot holding the account's friends");
            check(FriendRecordAreaUserId(snapshots[0], 0) == 0,
                "an offline friend's record leaves the 0x21 uid field at zero, so 查看信息 stays silent");
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification),
                "an offline friend is marked offline by BUDDY_OUT right after the snapshot");
            check(alicePackets.All(packet => packet.ProtocolId != GameProtocolEngine.BuddyInNotification),
                "an offline friend is never announced as online with BUDDY_IN");

            // 无关账号：挂着通道但没有把任何人加进名单
            var davePackets = new List<GameServerPacket>();
            var daveSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "dave", "戴夫", areaUserId: 4, daveSession, Record(davePackets), CancellationToken.None);
            check(davePackets.Count == 1
                    && davePackets[0].ProtocolId == GameProtocolEngine.BuddyConnListNotification
                    && davePackets[0].Payload[0] == 0,
                "an account with no friends still gets an empty snapshot so a stale client list is cleared");
            var daveBeforeStranger = davePackets.Count;

            var bravoPackets = new List<GameServerPacket>();
            var bravoSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "bobby", "布拉沃", areaUserId: 1, bravoSession, Record(bravoPackets), CancellationToken.None);
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification),
                "a friend coming online notifies the accounts whose friend list holds that name");
            check(davePackets.Count == daveBeforeStranger,
                "an online account with no matching friend entry receives no buddy notification");

            // 陈旧会话的清理不能误删新会话（比较再删）
            var staleBravoSession = bravoSession;
            bravoSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "bobby", "布拉沃", areaUserId: 1, bravoSession, Record(bravoPackets), CancellationToken.None);
            var aliceBeforeStaleDetach = alicePackets.Count;
            var bravoBeforeStaleDetach = bravoPackets.Count;
            await service.DetachChannelAsync("布拉沃", staleBravoSession, CancellationToken.None);
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                encoding.GetBytes("布拉沃"),
                messageType: 1,
                encoding.GetBytes("你好"),
                CancellationToken.None);
            check(alicePackets.Skip(aliceBeforeStaleDetach)
                        .All(packet => packet.ProtocolId != GameProtocolEngine.BuddyOutNotification)
                    && bravoPackets.Skip(bravoBeforeStaleDetach)
                        .Any(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification),
                "a detach from an old session cannot take the channel of the current session offline");

            var aliceBeforeLiveDetach = alicePackets.Count;
            await service.DetachChannelAsync("布拉沃", bravoSession, CancellationToken.None);
            check(alicePackets.Skip(aliceBeforeLiveDetach)
                    .Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification),
                "the session that owns the channel goes offline and friends hear BUDDY_OUT");
            check(alicePackets.Count(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification) >= 2,
                "a friend going offline notifies the same accounts with BUDDY_OUT");

            // 删除后仍有存活好友：重推的快照必须保留艾琳（而不是整份清空），
            // 并照旧给这个离线好友补 BUDDY_OUT（紧跟快照之后）。
            check(await store.AddFriendAsync("alice", "艾琳", CancellationToken.None) == FriendUpdateFailure.None,
                "the removal fixture gives alice a second, offline friend");
            var removed = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "布拉沃", CancellationToken.None);
            var removalPackets = alicePackets.Skip(removed).ToArray();
            var survivorSnapshot = removalPackets
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification);
            check(survivorSnapshot is not null
                    && survivorSnapshot.Payload[0] == 1
                    && HasFriendName(survivorSnapshot, 0, "艾琳"),
                "removing a friend re-sends a snapshot that still holds the remaining friend");
            var survivorSnapshotIndex = survivorSnapshot is null
                ? -1
                : Array.IndexOf(removalPackets, survivorSnapshot);
            check(survivorSnapshotIndex >= 0
                    && removalPackets.Skip(survivorSnapshotIndex).Any(packet =>
                        packet.ProtocolId == GameProtocolEngine.BuddyOutNotification
                        && BuddyNameEquals(packet, "艾琳")),
                "the remaining offline friend is still marked offline after the removal snapshot");
            var removalResult = removalPackets
                .FirstOrDefault(packet => packet.Type == 1 && packet.ProtocolId == 64);
            check(removalResult is not null
                    && IsFriendCommandResult(removalResult, 64, "布拉沃"),
                "a successful removal answers with a type-1 id-64 result that erases the entry by name");
            check(removalResult is not null
                    && Array.IndexOf(removalPackets, removalResult) > survivorSnapshotIndex,
                "the removal result follows the refreshed snapshot so the client's last state is the erase");
            var aliceFriendsAfterRemoval = await store.GetFriendsAsync("alice", CancellationToken.None);
            check(aliceFriendsAfterRemoval.Count == 1 && aliceFriendsAfterRemoval[0].Name == "艾琳",
                "the service persists the removal and keeps the surviving friend");

            // 再删掉最后一位好友：空快照仍然是"条目已消失"的唯一表现。
            var removedLast = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "艾琳", CancellationToken.None);
            check(alicePackets.Skip(removedLast)
                    .Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification
                        && packet.Payload[0] == 0),
                "removing the last friend re-sends an empty snapshot");
            check(alicePackets.Skip(removedLast).Any(packet =>
                    IsFriendCommandResult(packet, 64, "艾琳")),
                "removing the last friend also sends the id-64 erase result");
            check((await store.GetFriendsAsync("alice", CancellationToken.None)).Count == 0,
                "the service persists the last removal through the store");

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
            check(failedRemoval.All(packet =>
                    packet.Type != GameProtocolEngine.CommandPacketType),
                "a failed removal sends no type-1 success packet");
            check((await store.GetFriendsAsync("alice", CancellationToken.None)).Count == 0,
                "a failed removal changes no state");

            var afterOfflineWhisper = alicePackets.Count;
            var bravoBeforeOfflineWhisper = bravoPackets.Count;
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                encoding.GetBytes("布拉沃"),
                messageType: 1,
                encoding.GetBytes("你好"),
                CancellationToken.None);
            var offlineNotice = alicePackets.Skip(afterOfflineWhisper)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification);
            check(bravoPackets.Count == bravoBeforeOfflineWhisper
                    && offlineNotice is not null
                    && offlineNotice.Payload[0] == (byte)GameMessageType.Type03White,
                "whispering to an offline friend only tells the sender");
            check(offlineNotice is not null && ReadMessageText(offlineNotice) == "对方不在线",
                "the offline whisper notice says 对方不在线");

            bravoSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "bobby", "布拉沃", areaUserId: 1, bravoSession, Record(bravoPackets), CancellationToken.None);
            var bravoBefore = bravoPackets.Count;
            var aliceBeforeOnlineWhisper = alicePackets.Count;
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                encoding.GetBytes("布拉沃"),
                messageType: 1,
                encoding.GetBytes("你好"),
                CancellationToken.None);
            var delivered = bravoPackets.Skip(bravoBefore)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            check(delivered.Length == 1 && delivered[0].Payload[1] == 1,
                "a whisper reaches the online target with the sender uid the client resolves to a name");
            check(bravoPackets.Skip(bravoBefore)
                    .All(packet => packet.ProtocolId != GameProtocolEngine.UserInfoNotification),
                "a null sender appearance pushes no user-info record");
            check(bravoPackets.Skip(bravoBefore)
                    .Any(packet => IsMessengerMessage(packet, "阿尔法", "你好")),
                "the target's 1:1 window also gets a NOTI 65 named after the sender, next to the chat line");
            var echo12 = alicePackets.Skip(aliceBeforeOnlineWhisper)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            var echo65 = alicePackets.Skip(aliceBeforeOnlineWhisper)
                .Where(packet => packet.ProtocolId == 65)
                .ToArray();
            check(echo12.Length == 0,
                "the sender gets no NOTI 12 echo: the client's own-uid branch renders nothing");
            check(echo65.Length == 1 && IsMessengerMessage(echo65[0], "布拉沃", "你好"),
                "the sender's own 1:1 window gets exactly one NOTI 65 named after the target");

            var aliceBeforeSelfWhisper = alicePackets.Count;
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 1,
                senderAppearance: null,
                encoding.GetBytes("阿尔法"),
                messageType: 1,
                encoding.GetBytes("自言自语"),
                CancellationToken.None);
            var selfWhisperPackets = alicePackets.Skip(aliceBeforeSelfWhisper).ToArray();
            check(selfWhisperPackets.Length == 1
                    && selfWhisperPackets[0].ProtocolId == GameProtocolEngine.MessageNotification
                    && selfWhisperPackets[0].Payload[0] == (byte)GameMessageType.Type03White
                    && ReadMessageText(selfWhisperPackets[0]) == "不能给自己发悄悄话",
                "whispering to yourself answers 不能给自己发悄悄话 instead of delivering twice");

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
            var addTail = davePackets.Skip(daveBeforeAdd).ToArray();
            var addResult = addTail
                .FirstOrDefault(packet => packet.Type == 1 && packet.ProtocolId == 63);
            check(addResult is not null
                    && IsFriendCommandResult(addResult, 63, "艾琳")
                    && addResult.Payload[0] == 1,
                "a successful add answers with a type-1 id-63 result carrying the added name");
            check(Array.IndexOf(addTail, addResult) < Array.IndexOf(addTail, addedSnapshot),
                "the add result precedes the snapshot so the snapshot and BUDDY_OUT stay the last writers of the entry state");
            check((await store.GetFriendsAsync("dave", CancellationToken.None)).Count == 1,
                "a successful add is persisted by the store");

            var erinPackets = new List<GameServerPacket>();
            var erinSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "erin", "艾琳", areaUserId: 7, erinSession, Record(erinPackets), CancellationToken.None);
            var buddyIn = davePackets.Skip(daveBeforeAdd)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification);
            check(buddyIn is not null
                    && buddyIn.Payload[0] == 0
                    && BuddyNameEquals(buddyIn, "艾琳"),
                "an added friend coming online is announced to the account that holds the name");

            // 重连快照里在线好友不得被标成离线
            var daveReconnect = Guid.NewGuid();
            var daveBeforeReconnect = davePackets.Count;
            await service.AttachChannelAsync(
                "dave", "戴夫", areaUserId: 4, daveReconnect, Record(davePackets), CancellationToken.None);
            var reconnectSnapshot = davePackets.Skip(daveBeforeReconnect)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification);
            check(reconnectSnapshot is not null
                    && reconnectSnapshot.Payload[0] == 1
                    && HasFriendName(reconnectSnapshot, 0, "艾琳")
                    && davePackets.Skip(daveBeforeReconnect)
                        .All(packet => packet.ProtocolId != GameProtocolEngine.BuddyOutNotification),
                "an online friend is not marked offline by BUDDY_OUT after the snapshot");
            check(reconnectSnapshot is not null
                    && FriendRecordAreaUserId(reconnectSnapshot, 0) == 7,
                "an online friend's record carries its area user id in the 0x21 field");
            check(service.TryGetCharacterByAreaUserId(7, out var onlineLookup)
                    && onlineLookup == "艾琳"
                    && !service.TryGetCharacterByAreaUserId(8, out _)
                    && !service.TryGetCharacterByAreaUserId(0, out _),
                "the friend service resolves an online character by area user id, and rejects free and zero ids");

            var appearance = GameProtocolEngine.CreateCurrentCharacterInfo(
                new GameCharacterSummary(0, encoding.GetBytes("戴夫"), Job: 0, GrowType: 4, Level: 20),
                userId: 7);
            var erinBeforeWhisper = erinPackets.Count;
            var daveBeforeWhisper = davePackets.Count;
            await service.WhisperAsync(
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
            var messengerIndex = erinMessages.FindIndex(packet =>
                packet.ProtocolId == 65);
            check(messengerIndex > messageIndex
                    && IsMessengerMessage(erinMessages[messengerIndex], "戴夫", "在吗"),
                "the target gets the appearance, then NOTI 12, then a NOTI 65 named after the sender");
            var daveEcho = davePackets.Skip(daveBeforeWhisper)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification)
                .ToArray();
            var daveMessenger = davePackets.Skip(daveBeforeWhisper)
                .Where(packet => packet.ProtocolId == 65)
                .ToArray();
            check(daveEcho.Length == 0,
                "the appearance whisper leaves the sender without a NOTI 12 echo");
            check(daveMessenger.Length == 1
                    && IsMessengerMessage(daveMessenger[0], "艾琳", "在吗"),
                "the sender gets exactly one NOTI 65 named after the target");

            // CMD 17 类型 18（1:1 窗口消息）：双方各一条 NOTI 65，没有 NOTI 12、没有外观包。
            var erinBeforeMessenger = erinPackets.Count;
            var daveBeforeMessenger = davePackets.Count;
            await service.SendMessengerMessageAsync(
                "戴夫",
                encoding.GetBytes("艾琳"),
                encoding.GetBytes("1:1消息"),
                CancellationToken.None);
            var erinMessenger = erinPackets.Skip(erinBeforeMessenger).ToArray();
            check(erinMessenger.Length == 1
                    && IsMessengerMessage(erinMessenger[0], "戴夫", "1:1消息"),
                "a 1:1 message reaches the peer as a single NOTI 65 named after the sender");
            var daveMessengerReply = davePackets.Skip(daveBeforeMessenger).ToArray();
            check(daveMessengerReply.Length == 1
                    && IsMessengerMessage(daveMessengerReply[0], "艾琳", "1:1消息"),
                "the 1:1 sender gets exactly one NOTI 65 named after the peer");

            // 目标通道坏掉时，逐接收者 try/catch 保证发送者自己的 1:1 行仍然到达
            var brokenErinSession = Guid.NewGuid();
            var breakErinChannel = false;
            await service.AttachChannelAsync(
                "erin",
                "艾琳",
                areaUserId: 7,
                brokenErinSession,
                packet =>
                {
                    if (breakErinChannel)
                    {
                        throw new InvalidOperationException("channel is gone");
                    }

                    erinPackets.Add(packet);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);
            breakErinChannel = true;
            var daveBeforeBrokenWhisper = davePackets.Count;
            await service.WhisperAsync(
                "戴夫",
                senderAreaUserId: 7,
                senderAppearance: null,
                encoding.GetBytes("艾琳"),
                messageType: 1,
                encoding.GetBytes("在吗"),
                CancellationToken.None);
            check(davePackets.Skip(daveBeforeBrokenWhisper).Count(packet =>
                        packet.ProtocolId == 65) == 1
                    && davePackets.Skip(daveBeforeBrokenWhisper)
                        .All(packet => packet.ProtocolId
                            != GameProtocolEngine.MessageNotification),
                "a broken target channel does not swallow the sender's 1:1 line");

            var daveBeforeDetach = davePackets.Count;
            await service.DetachChannelAsync("艾琳", brokenErinSession, CancellationToken.None);
            check(davePackets.Skip(daveBeforeDetach).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && BuddyNameEquals(packet, "艾琳")),
                "a detached session marks the character offline for its friends");
            var erinAfterDetach = erinPackets.Count;
            await service.WhisperAsync(
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

            // 1:1 窗口消息给离线角色：仍是白字提示，且不产生任何 NOTI 65。
            var daveBeforeOfflineMessenger = davePackets.Count;
            await service.SendMessengerMessageAsync(
                "戴夫",
                encoding.GetBytes("艾琳"),
                encoding.GetBytes("还在吗"),
                CancellationToken.None);
            check(erinPackets.Count == erinAfterDetach
                    && davePackets.Skip(daveBeforeOfflineMessenger).All(packet =>
                        packet.ProtocolId != 65)
                    && davePackets.Skip(daveBeforeOfflineMessenger).Any(packet =>
                        packet.ProtocolId == GameProtocolEngine.MessageNotification
                        && ReadMessageText(packet) == "对方不在线"),
                "a 1:1 message to a detached character sends no window line and only tells the sender");

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
            check(failedAdd.All(packet => packet.Type != GameProtocolEngine.CommandPacketType),
                "a failed add sends the notice but no type-1 success flag");
            var daveFriends = await store.GetFriendsAsync("dave", CancellationToken.None);
            check(daveFriends.Count == 1 && daveFriends[0].Name == "艾琳",
                "a failed add changes no state");

            var daveBeforeSelf = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "戴夫", CancellationToken.None);
            check(davePackets.Skip(daveBeforeSelf).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "不能加自己")
                && davePackets.Skip(daveBeforeSelf)
                    .All(packet => packet.Type != GameProtocolEngine.CommandPacketType),
                "adding your own character explains itself with 不能加自己 and sends no success flag");

            var daveBeforeDuplicate = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "艾琳", CancellationToken.None);
            check(davePackets.Skip(daveBeforeDuplicate).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "已经是好友")
                && davePackets.Skip(daveBeforeDuplicate)
                    .All(packet => packet.Type != GameProtocolEngine.CommandPacketType),
                "adding an existing friend explains itself with 已经是好友 and sends no success flag");

            var ghostPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "ghost-account", "幽灵", areaUserId: 0, Guid.NewGuid(), Record(ghostPackets), CancellationToken.None);
            await service.AddFriendAsync("ghost-account", "幽灵", "艾琳", CancellationToken.None);
            check(ghostPackets.Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "操作失败"),
                "an operation that fails without a specific reason says 操作失败");

            // 名单条目按"接收方存下来的拼写"匹配与回推：在线角色名只有大小写不同
            // 时，BUDDY_IN/BUDDY_OUT 也必须用存储拼写，否则客户端更新不到对应条目。
            await store.CreateAccountAsync(new CreateAccountRequest("frank", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("gina", "password", OicqCode: "123456"), CancellationToken.None);
            check((await store.CreateCharacterAsync(
                    "frank",
                    new CreateCharacterRequest("Echo"),
                    CancellationToken.None)).Created,
                "the case-spelling fixture creates the mixed-case character");
            check(await store.AddFriendAsync("gina", "ECHO", CancellationToken.None) == FriendUpdateFailure.None,
                "the case-spelling fixture stores the friend under the caller's uppercase spelling");
            var ginaPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync("gina", "吉娜", areaUserId: 0, Guid.NewGuid(), Record(ginaPackets), CancellationToken.None);
            check(ginaPackets.Count == 2
                    && ginaPackets[0].ProtocolId == GameProtocolEngine.BuddyConnListNotification
                    && HasFriendName(ginaPackets[0], 0, "ECHO")
                    && ginaPackets[1].ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && BuddyNameExactly(ginaPackets[1], "ECHO"),
                "an offline friend is snapshotted and marked offline under its stored spelling");

            var echoSession = Guid.NewGuid();
            await service.AttachChannelAsync(
                "frank", "Echo", areaUserId: 0, echoSession, Record(new List<GameServerPacket>()), CancellationToken.None);
            var caseBuddyIn = ginaPackets
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification);
            check(caseBuddyIn is not null
                    && caseBuddyIn.Payload[0] == 0
                    && BuddyNameExactly(caseBuddyIn, "ECHO")
                    && !BuddyNameExactly(caseBuddyIn, "Echo"),
                "BUDDY_IN uses the recipient's stored spelling when the online character differs only in case");

            var ginaBeforeEchoDetach = ginaPackets.Count;
            await service.DetachChannelAsync("Echo", echoSession, CancellationToken.None);
            var caseBuddyOut = ginaPackets.Skip(ginaBeforeEchoDetach)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification);
            check(caseBuddyOut is not null
                    && BuddyNameExactly(caseBuddyOut, "ECHO")
                    && !BuddyNameExactly(caseBuddyOut, "Echo"),
                "BUDDY_OUT uses the recipient's stored spelling when the online character differs only in case");

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
            await service.AttachChannelAsync(
                "full", "满载", areaUserId: 0, Guid.NewGuid(), Record(fullPackets), CancellationToken.None);
            await service.AddFriendAsync("full", "满载", crowdNames[32], CancellationToken.None);
            check(fullPackets.Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "好友已满")
                && fullPackets.All(packet =>
                    packet.Type != GameProtocolEngine.CommandPacketType),
                "a full friend list explains itself with 好友已满 instead of adding a thirty-third friend");
            check((await store.GetFriendsAsync("full", CancellationToken.None)).Count == 32,
                "the rejected thirty-third friend is not added");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Func<GameServerPacket, ValueTask> Record(List<GameServerPacket> packets) =>
        packet =>
        {
            packets.Add(packet);
            return ValueTask.CompletedTask;
        };

    // 42 字节记录偏移 0x21 的 u16：在线好友的区用户 id（客户端“查看信息”按它发 CMD 8 mode 3）。
    private static ushort FriendRecordAreaUserId(GameServerPacket snapshot, int index) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            snapshot.Payload.AsSpan(1 + index * 42 + 0x21, 2));

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

    // 比 BuddyNameEquals 更严：状态字节后必须正好是 u32 长度 + 完整名字 + 标志字节，
    // 用来钉死"推送里出现的就是这个名字的这份拼写"。
    private static bool BuddyNameExactly(GameServerPacket packet, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.Payload.Length == 1 + sizeof(uint) + nameBytes.Length + 1
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(1, sizeof(uint)))
                == nameBytes.Length
            && packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes)
            && packet.Payload[^1] == 0;
    }

    // type=1 加/删好友结果：只有 [非零标志][u32 名字长度 + 名字][u8 错误码] 三个字段。
    // 命令通道的包头 type 写死成字面量 1：常量被改动时这些断言必须变红。
    private static bool IsFriendCommandResult(GameServerPacket packet, byte command, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.Type == 1
            && packet.ProtocolId == command
            && packet.Payload.Length == 1 + sizeof(uint) + nameBytes.Length + 1
            && packet.Payload[0] != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(
                packet.Payload.AsSpan(1, sizeof(uint))) == nameBytes.Length
            && packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes)
            && packet.Payload[^1] == 0;
    }

    // NOTI 65：客户端只对 type=0x12 且 flag!=0 走“按名字找/建 1:1 窗口”的分支。
    // 线上的字节值写死成字面量：常量被改动时这些断言必须变红。
    private static bool IsMessengerMessage(GameServerPacket packet, string name, string text)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        var textBytes = Encoding.GetEncoding(936).GetBytes(text);
        return packet.Type == 0
            && packet.ProtocolId == 65
            && packet.Payload.Length == 2 + 4 + nameBytes.Length + 4 + textBytes.Length
            && packet.Payload[0] == 0x12
            && packet.Payload[1] == 1
            && BinaryPrimitives.ReadUInt32LittleEndian(
                packet.Payload.AsSpan(2, 4)) == nameBytes.Length
            && packet.Payload.AsSpan(6, nameBytes.Length).SequenceEqual(nameBytes)
            && BinaryPrimitives.ReadUInt32LittleEndian(
                packet.Payload.AsSpan(6 + nameBytes.Length, 4)) == textBytes.Length
            && packet.Payload
                .AsSpan(10 + nameBytes.Length, textBytes.Length)
                .SequenceEqual(textBytes);
    }

    private static string ReadMessageText(GameServerPacket packet) =>
        Encoding.GetEncoding(936).GetString(
            packet.Payload,
            7,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(3, 4)));
}
