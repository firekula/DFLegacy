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
}
