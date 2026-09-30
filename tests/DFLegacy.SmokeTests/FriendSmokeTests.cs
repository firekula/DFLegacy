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
        CheckRequestedUserInfoDetailPacket(check);
        CheckBlacklistProtocol(check);
        await CheckUserInfoRequestBranchAsync(check);
        await CheckFriendStorageAsync(check);
        await CheckBlacklistStorageAsync(check);
        await CheckFriendServiceAsync(check);
        await CheckBlacklistServiceAsync(check);
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
        var add = GameProtocolEngine.CreateAddFriendResult(asciiName, areaUserId: 0x1234);
        check(add.Type == 1 && add.ProtocolId == 63,
            "a successful add answers on the type-1 command channel with id 63");
        // 完整成功包 = [非零标志][u32 名字长度 + 名字][A B C u32 D E F]，共 14+nameLen 字节。
        // 少发会让客户端用 0x30 填剩余：B=0x30 撞上 AppendEntry 的 `B < 5` 守卫，条目被静默拒绝。
        check(add.Payload.Length == 14 + asciiName.Length,
            "the add result is exactly 14 + name length bytes: the trailing six fields must all be present");
        var addTail = add.Payload.Length >= 14 + asciiName.Length
            ? add.Payload.AsSpan(1 + sizeof(uint) + asciiName.Length, 9)
            : Span<byte>.Empty;
        check(addTail.Length == 9
                && addTail[0] == 0
                && addTail[1] == 0
                && addTail[2] == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(addTail.Slice(3, 4)) == 0x1234
                && addTail[7] == 0
                && addTail[8] == 0,
            "the add tail is [A=0][B=0][C=0][u32 D=uid][E=0][F=0] when no profile is supplied");
        var offlineAdd = GameProtocolEngine.CreateAddFriendResult(asciiName).Payload;
        var offlineTail = offlineAdd.Length >= 14 + asciiName.Length
            ? offlineAdd.AsSpan(1 + sizeof(uint) + asciiName.Length, 9)
            : Span<byte>.Empty;
        check(offlineTail.Length == 9
                && BinaryPrimitives.ReadUInt32LittleEndian(offlineTail.Slice(3, 4)) == 0,
            "an offline add carries D=0 in the same 9-byte tail");

        // A/B/C = 等级/职业系/打包成长（与 NOTI 56 记录同源同序）：新加的条目立刻带真实
        // Lv./职业，不再停在 Lv.0 鬼剑士。A 是 u8，B 必须 <5（钳到 4 才能过 AppendEntry 守卫）。
        var profiledAdd = GameProtocolEngine.CreateAddFriendResult(
            asciiName,
            areaUserId: 0,
            level: 42,
            job: 5,
            growType: 13,
            awakeningType: 2).Payload;
        var profiledTail = profiledAdd.AsSpan(1 + sizeof(uint) + asciiName.Length, 9);
        check(profiledTail[0] == 42
                && profiledTail[1] == 4
                && profiledTail[2] == 0x2D
                && BinaryPrimitives.ReadUInt32LittleEndian(profiledTail.Slice(3, 4)) == 0
                && profiledTail[7] == 0
                && profiledTail[8] == 0,
            "the id-63 tail carries A=level, B=job (clamped <5), C=packed growth, and D=0 offline");

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
                && packet.Payload[1] == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(2, 4))
                    == peer.Length
                && packet.Payload.AsSpan(6, peer.Length).SequenceEqual(peer)
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    packet.Payload.AsSpan(6 + peer.Length, 4)) == text.Length
                && packet.Payload.AsSpan(10 + peer.Length, text.Length).SequenceEqual(text),
            "NOTI 65 is [type 0x12][flag 0 = body][u32 name + name][u32 text + text]");
    }

    // “查看信息”的回包必须是 mode 3：mode 1/3 共用同一条记录解析器（0x41A764），但只有
    // mode-3 记录尾部的 28 字节会打开资料卡窗口（CNRDAnotherUserPopUpWindow，窗口 id
    // 0x16）；回 mode 1 只更新目标模型，卡片根本不弹（“点了没反应”）。布局逐段对照
    // .superpowers/sdd/viewinfo-blacklist-recon.md §1.6。
    private static void CheckRequestedUserInfoDetailPacket(Action<bool, string> check)
    {
        var equipment = new[]
        {
            new GameInventoryEntry(
                Slot: 3,
                ItemId: 0x1234,
                CountOrValue: 7,
                State: 2,
                Durability: 55),
            new GameInventoryEntry(
                Slot: 10,
                ItemId: 0x4321,
                CountOrValue: 0,
                State: 0,
                Durability: 0),
            // 客户端只认槽 0..22；更高的槽必须被投影丢掉，否则它会进资料卡窗口的装备表。
            new GameInventoryEntry(Slot: 23, ItemId: 0xFFFF, CountOrValue: 1)
        };
        var packet = GameProtocolEngine.CreateRequestedUserInfoDetail(
            userId: 0x5678,
            equippedItems: equipment,
            equippedCreatureLevel: 9,
            experience: 123_456);

        check(packet.Type == 0 && packet.ProtocolId == GameProtocolEngine.UserInfoNotification,
            "the requested-user detail is a USERINFO notification");
        // 头部 13 字节 + 81 字节战斗数值块 + 装备表(1+2×10) + nPairs/creature 2 字节 + 28 字节尾。
        check(packet.Payload.Length == 1 + 2 + 2 + 4 + 4 + 81 + 1 + 2 * 10 + 1 + 1 + 28,
            "the mode-3 record is header + stat blob + equipment + nPairs + creature + the 28-byte tail");
        check(packet.Payload[0] == 3,
            "the record starts with mode 3: mode 1 would never open the info card");
        check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(1, 2)) == 1,
            "the mode-3 record count is one");
        check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3, 2)) == 0x5678,
            "the record echoes the requested uid verbatim");
        // uid 后那个 u32 是经验值（客户端 0x41A828 读出后交给 0x4974E0 经验 setter），
        // 不是名字槽位参数；写 0 时卡片经验条显示 0。
        check(BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(5, 4)) == 123_456,
            "the u32 after uid is the character's experience, echoed verbatim into the card");
        check(BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(9, 4)) == 81
                && packet.Payload.Length >= 13 + 81,
            "the stat blob keeps the existing 81-byte length-prefixed projection (it carries no experience)");

        const int equipmentOffset = 13 + 81;
        check(packet.Payload[equipmentOffset] == 2,
            "the equipment list is the two visible slots: slot 23 is outside the client's table");
        check(packet.Payload[equipmentOffset + 1] == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.Payload.AsSpan(equipmentOffset + 2, 2)) == 0x1234
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    packet.Payload.AsSpan(equipmentOffset + 4, 4)) == 7
                && packet.Payload[equipmentOffset + 8] == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.Payload.AsSpan(equipmentOffset + 9, 2)) == 55,
            "an equipment entry is slot:u8, item:u16, value:u32, state:u8, durability:u16");
        check(packet.Payload[equipmentOffset + 11] == 10
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.Payload.AsSpan(equipmentOffset + 12, 2)) == 0x4321,
            "the second worn slot follows at a ten-byte stride");

        const int pairsOffset = equipmentOffset + 1 + 2 * 10;
        check(packet.Payload[pairsOffset] == 0,
            "nPairs is zero: the mode-3 parser reads two bytes per pair and would misalign the 28-byte tail");
        check(packet.Payload[pairsOffset + 1] == 9,
            "the creature byte follows the empty pair table");
        // 先判长度再切片：28 字节尾被截断时必须是一条失败断言，而不是测试自身越界崩溃。
        var tailOffset = pairsOffset + 2;
        var tail = packet.Payload.Length >= tailOffset + 28
            ? packet.Payload.AsSpan(tailOffset, 28)
            : Span<byte>.Empty;
        check(tail.Length == 28 && tail.ToArray().All(value => value == 0),
            "the mode-3-only 28-byte tail is present and zero-filled (meanings unknown, pending live tuning)");

        // mode 1 = 给“其他玩家外观”补 CNUser+0x110 模型对象用的形态（2026-09-30 真机定位：
        // 只发 mode-0 区域用户记录时，他人记录里的模型对象永远是 NULL，结算界面 state 8 解引用即崩）。
        // 同一份记录，**只少 28 字节尾**——写了尾就会被详情解析器当资料卡参数读走。
        var modelOnly = GameProtocolEngine.CreateRequestedUserInfoDetail(
            userId: 0x5678,
            equippedItems: equipment,
            equippedCreatureLevel: 9,
            experience: 123_456,
            mode: 1);
        check(modelOnly.Payload[0] == 1,
            "the model-only record starts with mode 1");
        check(modelOnly.Payload.Length == packet.Payload.Length - 28,
            "mode 1 is byte-for-byte the mode-3 record without its 28-byte tail");
        check(modelOnly.Payload.AsSpan(1).SequenceEqual(
                packet.Payload.AsSpan(1, packet.Payload.Length - 1 - 28)),
            "everything after the mode byte is shared; mode 3 only appends the tail");

        var modeRejected = false;
        try
        {
            GameProtocolEngine.CreateRequestedUserInfoDetail(userId: 7, mode: 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            modeRejected = true;
        }

        check(modeRejected,
            "mode 0 is the area-user record, not a detail record: the builder refuses it");

        var empty = GameProtocolEngine.CreateRequestedUserInfoDetail(userId: 7);
        // 零装备记录 = 头部 13 + 81 字节 blob + 1 条数 + 1 nPairs + 1 creature + 28 尾 = 125。
        // （勘察里“44 字节”是 blobLen=0 的极简形态；本实现沿用现有 81 字节战斗数值块。）
        check(empty.Payload.Length == 125
                && empty.Payload[0] == 3
                && empty.Payload[13 + 81] == 0
                && empty.Payload[13 + 81 + 1] == 0
                && empty.Payload[13 + 81 + 2] == 0,
            "a minimal mode-3 record keeps the 81-byte blob: empty equipment, zero pairs and creature, and the tail");
    }

    // 黑名单三条命令/回包的字节布局（viewinfo-blacklist-recon.md §2）：全部走 type=1
    // 命令通道、没有通知包；130 是“清空 + 全量追加”的快照，可随时重发。
    private static void CheckBlacklistProtocol(Action<bool, string> check)
    {
        check(GameProtocolEngine.CommandPacketType == 1
                && GameProtocolEngine.RegisterToBlacklistCommand == 128
                && GameProtocolEngine.DeleteToBlacklistCommand == 129
                && GameProtocolEngine.RequestBlacklistCommand == 130,
            "the blacklist ids stay on the client's tables: type 1, add 128, remove 129, snapshot 130");
        check(GameProtocolEngine.BlacklistAlreadyListedError == 74
                && GameProtocolEngine.BlacklistCharacterNotFoundError == 76
                && GameProtocolEngine.BlacklistFullError == 78
                && GameProtocolEngine.BlacklistNotListedError == 0x4B,
            "the failure codes are the ones the client maps to its four blacklist messages");

        var name = Encoding.GetEncoding(936).GetBytes("布拉沃");
        var body = new byte[2 + 4 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2, 4), (uint)name.Length);
        name.CopyTo(body, 6);
        check(GameProtocolEngine.TryReadBlacklistName(body, out var parsed)
                && parsed.SequenceEqual(name),
            "CMD 128/129 carry u32 name length + name after the sequence number");
        check(!GameProtocolEngine.TryReadBlacklistName(body[..5], out _),
            "a truncated blacklist command is rejected");
        check(!GameProtocolEngine.TryReadBlacklistName([0, 0, 0, 0, 0, 0], out _),
            "an empty blacklist name is rejected");
        var embeddedNul = new byte[2 + 4 + 3];
        BinaryPrimitives.WriteUInt32LittleEndian(embeddedNul.AsSpan(2, 4), 3);
        embeddedNul[6] = (byte)'a';
        embeddedNul[7] = 0;
        check(!GameProtocolEngine.TryReadBlacklistName(embeddedNul, out _),
            "a blacklist name containing a NUL byte is rejected");
        var longName = new byte[2 + 4 + 31];
        BinaryPrimitives.WriteUInt32LittleEndian(longName.AsSpan(2, 4), 31);
        longName.AsSpan(6).Fill((byte)'a');
        check(!GameProtocolEngine.TryReadBlacklistName(longName, out _),
            "a blacklist name longer than thirty bytes is rejected");

        var add = GameProtocolEngine.CreateBlacklistAddResult(name);
        check(add.Type == 1 && add.ProtocolId == 128
                && add.Payload.Length == 1 + 4 + name.Length
                && add.Payload[0] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(add.Payload.AsSpan(1, 4)) == name.Length
                && add.Payload.AsSpan(5, name.Length).SequenceEqual(name),
            "a successful 128 replies [non-zero][u32 name length + name]");
        check(GameProtocolEngine.CreateBlacklistError(128, 74).Payload.SequenceEqual(new byte[] { 0, 74 })
                && GameProtocolEngine.CreateBlacklistError(128, 76).Payload.SequenceEqual(new byte[] { 0, 76 })
                && GameProtocolEngine.CreateBlacklistError(128, 78).Payload.SequenceEqual(new byte[] { 0, 78 }),
            "a failed 128 replies [0][74 already listed / 76 character not found / 78 list full]");
        check(GameProtocolEngine.CreateBlacklistError(128, 74).ProtocolId == 128,
            "the failure answers on the same id as its command");

        var remove = GameProtocolEngine.CreateBlacklistRemoveResult(name);
        check(remove.Type == 1 && remove.ProtocolId == 129
                && remove.Payload.Length == 1 + 4 + name.Length
                && remove.Payload[0] == 1
                && remove.Payload.AsSpan(5, name.Length).SequenceEqual(name),
            "a successful 129 replies the same [non-zero][u32 name length + name]");
        check(GameProtocolEngine.CreateBlacklistError(129, 0x4B).Payload
                .SequenceEqual(new byte[] { 0, 0x4B }),
            "a failed 129 replies [0][0x4B not on the list]");

        var snapshot = GameProtocolEngine.CreateBlacklistSnapshot(
        [
            new GameBlacklistEntry(Encoding.GetEncoding(936).GetBytes("布拉沃"), 2026, 9, 27),
            new GameBlacklistEntry(Encoding.ASCII.GetBytes("Bravo"), 2001, 1, 5)
        ]);
        check(snapshot.Type == 1 && snapshot.ProtocolId == 130,
            "the full snapshot answers on the type-1 command channel with id 130");
        check(snapshot.Payload.Length == 2 + (4 + 6 + 3) + (4 + 5 + 3)
                && snapshot.Payload[0] != 0
                && snapshot.Payload[1] == 2,
            "the 130 snapshot is [non-zero flag][count] + one record per entry");
        check(BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Payload.AsSpan(2, 4)) == 6
                && snapshot.Payload.AsSpan(6, 6)
                    .SequenceEqual(Encoding.GetEncoding(936).GetBytes("布拉沃"))
                && snapshot.Payload[12] == 2026 - 1900
                && snapshot.Payload[13] == 8
                && snapshot.Payload[14] == 27,
            "a snapshot record is u32 name length + name + (year-1900, month-1, day)");
        check(BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Payload.AsSpan(15, 4)) == 5
                && snapshot.Payload.AsSpan(19, 5).SequenceEqual(Encoding.ASCII.GetBytes("Bravo"))
                && snapshot.Payload[24] == 101
                && snapshot.Payload[25] == 0
                && snapshot.Payload[26] == 5,
            "the second record keeps the same layout and the date offsets");
        check(GameProtocolEngine.CreateBlacklistSnapshot([]).Payload
                .SequenceEqual(new byte[] { 1, 0 }),
            "an empty snapshot still carries the non-zero flag so the client clears its list");

        // 越界日期来自坏存档/手改：必须钳制到 u8 可表示范围，而不是抛异常把会话断掉。
        var clamped = GameProtocolEngine.CreateBlacklistSnapshot(
        [
            new GameBlacklistEntry(Encoding.ASCII.GetBytes("Bad"), 1800, 0, 99),
            new GameBlacklistEntry(Encoding.ASCII.GetBytes("Worse"), 9999, 13, 0)
        ]);
        check(clamped.Payload[1] == 2
                && clamped.Payload[9] == 0        // 年 1800 -> 1900
                && clamped.Payload[10] == 0       // 月 0 -> 1
                && clamped.Payload[11] == 31      // 日 99 -> 31
                && clamped.Payload[21] == 255     // 年 9999 -> 2155
                && clamped.Payload[22] == 11      // 月 13 -> 12
                && clamped.Payload[23] == 1,      // 日 0 -> 1
            "out-of-range saved dates are clamped to the wire range, so a bad save cannot kill the session");

        var rejectedEmpty = false;
        try
        {
            GameProtocolEngine.CreateBlacklistAddResult([]);
        }
        catch (ArgumentException)
        {
            rejectedEmpty = true;
        }

        check(rejectedEmpty, "an empty blacklist name cannot produce a command result packet");
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
                return Task.FromResult(roster);
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
                return Task.FromResult(GameProtocolEngine.CreateRequestedUserInfoDetail(uid));
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
                && resolvedDetail.Payload[0] == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(resolvedDetail.Payload.AsSpan(1, 2)) == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    resolvedDetail.Payload.AsSpan(3, 2)) == 0x5678,
            "the mode-3 reply is USERINFO mode 3, count 1, echoing the requested uid");
        check(resolvedDetail is not null
                && resolvedDetail.Payload.Length == 125
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
                return Task.FromResult(roster);
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
                return Task.FromResult(roster);
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

    // 好友行的等级/职业/成长：R+0x21 = 等级 u16、R+0x23 = 职业系（<5）、R+0x24 = 打包成长。
    // 旧结论“R+0x21 必须为 0”已作废（把 SecureInt 解密误当成取字符串）。
    private static void CheckFriendListPacket(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreateFriendList(
        [
            // 在线好友：等级 42、职业系 4（圣职者）、成长 13、觉醒 2 -> packed 0x2D。
            new GameFriendEntry(
                Encoding.GetEncoding(936).GetBytes("阿尔法"),
                Online: true,
                Level: 42,
                Job: 4,
                GrowType: 13,
                AwakeningType: 2),
            // 离线好友：同样带真实的等级/职业（行绘制在没有 user 记录时就读这些字段）。
            new GameFriendEntry(
                Encoding.ASCII.GetBytes("Bravo"),
                Online: false,
                Level: 7,
                Job: 1,
                GrowType: 1,
                AwakeningType: 0),
            // 越界职业 5：客户端 0x500B00 的 `cmp arg3,5; jge` 会整条拒绝，必须钳到 4。
            new GameFriendEntry(
                Encoding.ASCII.GetBytes("Clamp"),
                Online: false,
                Level: 60,
                Job: 5,
                GrowType: 0,
                AwakeningType: 0)
        ]);

        check(packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification,
            "the friend list uses notification fifty-six, the only notification that creates entries");
        check(packet.Payload.Length == 1 + 42 * 3,
            "the friend list is a byte count plus 42-byte records");
        check(packet.Payload[0] == 3, "the friend list header carries the entry count");

        var first = packet.Payload.AsSpan(1, 42);
        check(first[0] == 0 && first[1] == 0 && first[2] == 0,
            "a record starts with state, flag and reserved bytes");
        var firstName = first.Slice(3, 30);
        check(firstName[..6].SequenceEqual(Encoding.GetEncoding(936).GetBytes("阿尔法"))
            && firstName[6..].ToArray().All(b => b == 0),
            "the name occupies a NUL-padded thirty-byte field");
        check(BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(0x21, 2)) == 42,
            "R+0x21 is the friend's level: the row renders Lv.%d from it (not a uid, not a pointer)");
        check(first[0x23] == 4,
            "R+0x23 is the job branch the row passes to the job-name lookup");
        check(first[0x24] == 0x2D,
            "R+0x24 packs growth 13 in the low nibble and awakening 2 in the high nibble");
        check(first[0x25] == 0 && first[0x26] == 0 && first[0x27] == 0 && first[0x28] == 0
            && first[0x29] == 0,
            "R+0x25 and R+0x29 keep their unknown-semantics zero values");

        var second = packet.Payload.AsSpan(1 + 42, 42);
        check(second.Slice(3, 30)[..5].SequenceEqual(Encoding.ASCII.GetBytes("Bravo")),
            "an ASCII name uses the same thirty-byte field");
        check(BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(0x21, 2)) == 7
            && second[0x23] == 1
            && second[0x24] == 0x01,
            "an offline friend also carries level/job/growth: its row draws Lv.7 from these bytes");
        check(packet.Payload[1 + 42 + 0x20] == 0,
            "the last byte of the name field stays inside the record");

        var third = packet.Payload.AsSpan(1 + 42 * 2, 42);
        check(third[0x23] == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(third.Slice(0x21, 2)) == 60,
            "an out-of-range job is clamped to 4 so the client's <5 guard cannot reject the whole entry");
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
            "BUDDY_IN is state + length-prefixed name + flag, and its flag stays zero");

        // flag=1：有 user 记录时行才会取灰色的“未连接”样式并排到末组（0x7F8031 的分支 +
        // 0x7F75F0 的第四组）；代价是 [%s]已退出游戏 聊天提示被抑制（真机观察项）。
        var buddyOut = GameProtocolEngine.CreateBuddyOut(nameBytes);
        check(buddyOut.ProtocolId == GameProtocolEngine.BuddyOutNotification,
            "an offline friend uses BUDDY_OUT");
        check(buddyOut.Payload.Length == 1 + 4 + nameBytes.Length + 1
            && buddyOut.Payload[0] == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(buddyOut.Payload.AsSpan(1, 4)) == nameBytes.Length
            && buddyOut.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes)
            && buddyOut.Payload[^1] == 1,
            "BUDDY_OUT keeps its layout but carries flag 1, the only value that greys the row out");
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

    // 账号级黑名单存储：上限 10、名字唯一，且**所有名字比较都是大小写敏感的 Ordinal**
    // （与好友名单的 OrdinalIgnoreCase 相反，勘察 CNRDBlackListdManager::FindByName）。
    private static async Task CheckBlacklistStorageAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dflegacy-blacklist-tests", Guid.NewGuid().ToString("N"));
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
            await store.CreateCharacterAsync(
                other,
                new CreateCharacterRequest("Bravo"),
                CancellationToken.None);

            check((await store.GetBlacklistAsync(owner, CancellationToken.None)).Count == 0,
                "a fresh account has an empty blacklist");
            check(await store.AddBlacklistAsync(owner, "法师", CancellationToken.None)
                    == BlacklistUpdateFailure.None,
                "blacklisting an existing character name succeeds");
            check(await store.AddBlacklistAsync(owner, "法师", CancellationToken.None)
                    == BlacklistUpdateFailure.AlreadyBlacklisted,
                "blacklisting the same name twice is rejected");
            check(await store.AddBlacklistAsync(owner, "不存在的人", CancellationToken.None)
                    == BlacklistUpdateFailure.CharacterNotFound,
                "blacklisting an unknown character is rejected");
            // 角色存在性也走 Ordinal：客户端失败码 0x4C 的文案就是“请确认是否大小写错误”。
            check(await store.AddBlacklistAsync(owner, "bravo", CancellationToken.None)
                    == BlacklistUpdateFailure.CharacterNotFound,
                "a character is only found with its exact casing, unlike the friend list");
            check(await store.AddBlacklistAsync(owner, "Bravo", CancellationToken.None)
                    == BlacklistUpdateFailure.None,
                "the exact casing of the same character is accepted");

            var blacklist = await store.GetBlacklistAsync(owner, CancellationToken.None);
            check(blacklist.Count == 2
                    && blacklist[0].Name == "法师"
                    && blacklist[1].Name == "Bravo"
                    && blacklist.All(entry => entry.Year > 2000 && entry.Month is >= 1 and <= 12
                        && entry.Day is >= 1 and <= 31),
                "the blacklist keeps the insertion order, the exact name and a real date");

            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync(CancellationToken.None);
            var persisted = await reloaded.GetBlacklistAsync(owner, CancellationToken.None);
            check(persisted.Count == 2
                    && persisted[0].Name == "法师"
                    && persisted[1].Name == "Bravo"
                    && persisted[0].Year == blacklist[0].Year
                    && persisted[0].Month == blacklist[0].Month
                    && persisted[0].Day == blacklist[0].Day,
                "the blacklist is written to disk and survives a store reload");

            check(await store.RemoveBlacklistAsync(owner, "bravo", CancellationToken.None)
                    == BlacklistUpdateFailure.NotBlacklisted,
                "removal matches the entry name case-sensitively, unlike the friend list");
            check(await store.RemoveBlacklistAsync(owner, "  法师  ", CancellationToken.None)
                    == BlacklistUpdateFailure.None
                    && (await store.GetBlacklistAsync(owner, CancellationToken.None)).Count == 1,
                "removal trims the caller's surrounding whitespace and deletes the entry");
            check(await store.RemoveBlacklistAsync(owner, "法师", CancellationToken.None)
                    == BlacklistUpdateFailure.NotBlacklisted,
                "removing a non-listed name reports NotBlacklisted");

            check(await store.AddBlacklistAsync("ghost-account", "法师", CancellationToken.None)
                    == BlacklistUpdateFailure.AccountNotFound
                && await store.RemoveBlacklistAsync("ghost-account", "法师", CancellationToken.None)
                    == BlacklistUpdateFailure.AccountNotFound,
                "blacklist updates on an unknown account report AccountNotFound");
            check((await store.GetBlacklistAsync("ghost-account", CancellationToken.None)).Count == 0,
                "an unknown account reads as an empty blacklist");

            // 上限 10：目标角色都放在同一个账号上（单账号可见角色上限 24）。
            check(await store.RemoveBlacklistAsync(owner, "Bravo", CancellationToken.None)
                    == BlacklistUpdateFailure.None
                && (await store.GetBlacklistAsync(owner, CancellationToken.None)).Count == 0,
                "the cap fixture clears the remaining entry");
            var targetNames = new List<string>();
            for (var index = 0; index < 10; index++)
            {
                var name = $"目标{index:D2}";
                await store.CreateCharacterAsync(
                    other,
                    new CreateCharacterRequest(name),
                    CancellationToken.None);
                targetNames.Add(name);
            }

            for (var index = 0; index < 10; index++)
            {
                check(await store.AddBlacklistAsync(owner, targetNames[index], CancellationToken.None)
                        == BlacklistUpdateFailure.None,
                    $"blacklist entry {index + 1} of the ten-entry cap is accepted");
            }

            check(await store.AddBlacklistAsync(owner, "圣骑士", CancellationToken.None)
                    == BlacklistUpdateFailure.BlacklistFull,
                "the eleventh blacklist entry is rejected because the client's window holds ten");
            check((await store.GetBlacklistAsync(owner, CancellationToken.None)).Count == 10,
                "a rejected over-cap entry does not change the stored list");
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
            // 离线好友没有本地 user 记录，行就靠快照这三个字段画 `Lv.%d 职业`：必须真填，
            // 否则就是“离线好友显示 Lv.0 鬼剑士”。等级/职业来自该好友的存档角色投影。
            check(FriendRecordLevel(snapshots[0], 0) == bobFixture.Character!.Level
                    && FriendRecordClass(snapshots[0], 0) == (
                        (byte)Math.Clamp(bobFixture.Character.Job, 0, 4),
                        (byte)((bobFixture.Character.GrowType & 0x0F)
                            | ((bobFixture.Character.AwakeningType & 0x07) << 4))),
                "an offline friend's snapshot row carries his stored level/job/growth, so it no longer renders Lv.0");
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && packet.Payload[^1] == 1),
                "an offline friend is marked offline by a flag-1 BUDDY_OUT right after the snapshot");
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
            var aliceBeforeBravoLogin = alicePackets.Count;
            await service.AttachChannelAsync(
                "bobby", "布拉沃", areaUserId: 1, bravoSession, Record(bravoPackets), CancellationToken.None);
            check(alicePackets.Any(packet => packet.ProtocolId == GameProtocolEngine.BuddyInNotification),
                "a friend coming online notifies the accounts whose friend list holds that name");
            var bravoLoginTail = alicePackets.Skip(aliceBeforeBravoLogin).ToArray();
            var bravoOnlineRecord = bravoLoginTail
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.UserInfoNotification);
            check(bravoLoginTail.Length == 2
                    && bravoOnlineRecord is not null
                    && bravoOnlineRecord.Payload[0] == 0
                    && ReadUserInfoRecordUid(bravoOnlineRecord) == 1
                    && UserInfoRecordNameEquals(bravoOnlineRecord, "布拉沃")
                    && bravoLoginTail[1].ProtocolId == GameProtocolEngine.BuddyInNotification
                    && bravoLoginTail[1].Payload[^1] == 0
                    && BuddyNameExactly(bravoLoginTail[1], "布拉沃"),
                "a friend coming online pushes his mode-0 USERINFO record first, then exactly one flag-0 BUDDY_IN");
            check(bravoLoginTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "a presence change re-pushes no snapshot: the client appends one row per snapshot, so the ec67261 re-push duplicated the friend");
            check(bravoPackets.Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification) == 1,
                "logging in pushes exactly one friend-list snapshot per session");
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
            var offlineTail = alicePackets.Skip(aliceBeforeLiveDetach).ToArray();
            // 下线两条 BUDDY_OUT：flag 0（“[%s]已退出游戏”提示）在前、flag 1（灰色未连接）
            // 在后；顺序反了提示会被 flag=1 的守卫抑制。
            check(offlineTail.Length == 2
                    && offlineTail[0].ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && offlineTail[0].Payload[^1] == 0
                    && offlineTail[1].ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && offlineTail[1].Payload[^1] == 1
                    // 两条除末尾 flag 外完全一致：首字节 0、名字同拼写同长度。
                    && offlineTail[0].Payload[0] == 0
                    && offlineTail[1].Payload[0] == 0
                    && BuddyNameExactly(offlineTail[0], "布拉沃")
                    && BuddyNameExactly(offlineTail[1], "布拉沃")
                    && offlineTail[0].Payload.Length == offlineTail[1].Payload.Length,
                "a logout sends exactly two BUDDY_OUT for the stored name: flag 0 (chat notice) then flag 1 (grey row)");
            check(offlineTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "an offline transition re-pushes no snapshot either, so it cannot insert a duplicate row");

            // 删除只发 type=1 id 64（客户端 vector::erase）。加删都不推快照：NOTI 56 是
            // 无条件追加条目，推一次就把整份名单（含存活好友）再插一遍。
            check(await store.AddFriendAsync("alice", "艾琳", CancellationToken.None) == FriendUpdateFailure.None,
                "the removal fixture gives alice a second friend");
            var removed = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "布拉沃", CancellationToken.None);
            var removalPackets = alicePackets.Skip(removed).ToArray();
            check(removalPackets.Length == 1
                    && IsFriendCommandResult(removalPackets[0], 64, "布拉沃"),
                "a successful removal sends exactly one type-1 id-64 erase result");
            check(removalPackets.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification
                        && packet.ProtocolId != GameProtocolEngine.BuddyOutNotification),
                "removal pushes no snapshot: the surviving friend is not re-inserted and no BUDDY_OUT is fanned out");
            var aliceFriendsAfterRemoval = await store.GetFriendsAsync("alice", CancellationToken.None);
            check(aliceFriendsAfterRemoval.Count == 1 && aliceFriendsAfterRemoval[0].Name == "艾琳",
                "the service persists the removal and keeps the surviving friend");

            // 再删掉最后一位好友：同样只有 id 64；空快照既清不掉条目也没有必要。
            var removedLast = alicePackets.Count;
            await service.RemoveFriendAsync("alice", "阿尔法", "艾琳", CancellationToken.None);
            var lastRemovalPackets = alicePackets.Skip(removedLast).ToArray();
            check(lastRemovalPackets.Length == 1
                    && IsFriendCommandResult(lastRemovalPackets[0], 64, "艾琳"),
                "removing the last friend sends only the id-64 erase result");
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
                    .All(packet => packet.ProtocolId != 65),
                "a whisper never pushes NOTI 65: it must not open or write the target's 1:1 window");
            check(alicePackets.Skip(aliceBeforeOnlineWhisper).ToArray().Length == 0,
                "the whisper sender gets no packet at all: its own line is client-local and any echo would print the target as the speaker");

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

            // 加好友成功：先发 type=1 id 63 追加条目（客户端提示"[名字]已被添加到好友
            // 名单"），再按对方在线与否补一条 BUDDY_IN/BUDDY_OUT 就地定状态，在线时最后
            // 再补一条该好友的 USERINFO 记录（“查看信息”的 FindByUid 门要用）；绝不发
            // 快照——NOTI 56 无条件追加，会把老好友全部再插一份。
            var daveBeforeAdd = davePackets.Count;
            await service.AddFriendAsync("dave", "戴夫", "艾琳", CancellationToken.None);
            var addTail = davePackets.Skip(daveBeforeAdd).ToArray();
            check(addTail.Length == 2
                    && IsAddFriendResult(
                        addTail[0],
                        "艾琳",
                        areaUserId: 0,
                        level: (ushort)erinFixture.Character!.Level,
                        job: (byte)Math.Clamp(erinFixture.Character.Job, 0, 4),
                        growType: (byte)Math.Clamp(erinFixture.Character.GrowType, 0, 15),
                        awakeningType: (byte)Math.Clamp(erinFixture.Character.AwakeningType, 0, 7))
                    && addTail[0].Payload[0] == 1,
                "a successful add first sends the full id-63 result carrying the friend's level/job/growth (offline: D=0)");
            check(addTail.Length > 1
                    && addTail[1].ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && BuddyNameExactly(addTail[1], "艾琳")
                    && addTail[1].Payload[^1] == 1,
                "an offline friend gets a flag-1 BUDDY_OUT right after the id-63 append, greying the new row out");
            check(addTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "add never re-pushes the snapshot: NOTI 56 appends unconditionally, so the whole list would be duplicated");
            check(addTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.UserInfoNotification),
                "an offline friend's add pushes no USERINFO record: there is no uid to register");
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
            var erinOnlineRecord = davePackets.Skip(daveBeforeAdd)
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.UserInfoNotification);
            check(erinOnlineRecord is not null
                    && erinOnlineRecord.Payload[0] == 0
                    && ReadUserInfoRecordUid(erinOnlineRecord) == 7
                    && UserInfoRecordNameEquals(erinOnlineRecord, "艾琳"),
                "a friend coming online also gets his mode-0 USERINFO record pushed to the account that tracks him");
            var erinRecordClass = erinOnlineRecord is null
                ? default
                : ReadUserInfoRecordClass(erinOnlineRecord);
            check(erinOnlineRecord is not null
                    && erinRecordClass.Job == erinFixture.Character!.Job
                    && erinRecordClass.Growth == erinFixture.Character.GrowType
                    && erinRecordClass.Level
                        == (byte)Math.Clamp(erinFixture.Character.Level, 1, byte.MaxValue),
                "the pushed profile record takes job/growth/level from the stored character, not defaults");

            // 重连（再次登录）：恰好一条快照、在线好友不被标离线，且每个在线好友各补
            // 一条 mode-0 USERINFO 记录（登录路径）。
            var daveReconnect = Guid.NewGuid();
            var daveBeforeReconnect = davePackets.Count;
            await service.AttachChannelAsync(
                "dave", "戴夫", areaUserId: 4, daveReconnect, Record(davePackets), CancellationToken.None);
            var reconnectTail = davePackets.Skip(daveBeforeReconnect).ToArray();
            var reconnectSnapshot = reconnectTail
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification);
            check(reconnectTail.Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.BuddyConnListNotification) == 1
                    && reconnectSnapshot is not null
                    && reconnectSnapshot.Payload[0] == 1
                    && HasFriendName(reconnectSnapshot, 0, "艾琳"),
                "logging in sends exactly one snapshot holding the online friend");
            check(reconnectTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyOutNotification),
                "an online friend is not marked offline by BUDDY_OUT after the snapshot");
            check(reconnectSnapshot is not null
                    && FriendRecordLevel(reconnectSnapshot, 0) == erinFixture.Character!.Level
                    && FriendRecordClass(reconnectSnapshot, 0) == (
                        (byte)Math.Clamp(erinFixture.Character.Job, 0, 4),
                        (byte)((erinFixture.Character.GrowType & 0x0F)
                            | ((erinFixture.Character.AwakeningType & 0x07) << 4))),
                "an online friend's snapshot row also carries his stored level/job/growth (R+0x21 is the level)");
            var reconnectRecord = reconnectTail
                .FirstOrDefault(packet => packet.ProtocolId == GameProtocolEngine.UserInfoNotification);
            check(reconnectRecord is not null
                    && reconnectRecord.Payload[0] == 0
                    && ReadUserInfoRecordUid(reconnectRecord) == 7
                    && UserInfoRecordNameEquals(reconnectRecord, "艾琳"),
                "logging in pushes one mode-0 USERINFO record for each online friend");
            check(reconnectRecord is not null
                    && reconnectSnapshot is not null
                    && Array.IndexOf(reconnectTail, reconnectRecord)
                        < Array.IndexOf(reconnectTail, reconnectSnapshot),
                "the online-friend record is pushed before the snapshot so the appended entry finds its user object");
            check(service.TryGetCharacterByAreaUserId(7, out var onlineLookup)
                    && onlineLookup == "艾琳"
                    && !service.TryGetCharacterByAreaUserId(8, out _)
                    && !service.TryGetCharacterByAreaUserId(0, out _),
                "the friend service resolves an online character by area user id, and rejects free and zero ids");

            // “查看信息”不是好友专属：城镇里右键任何路人玩家同样走 CMD 8 mode 3。
            // 阿尔法（uid 11）在线但不在戴夫的好友名单里，反查与回包都必须覆盖他。
            check((await store.GetFriendsAsync("dave", CancellationToken.None))
                    .All(friend => friend.Name != "阿尔法"),
                "the fixture keeps 阿尔法 (uid 11) off dave's friend list");
            check(service.TryGetCharacterByAreaUserId(11, out var passerbyLookup)
                    && passerbyLookup == "阿尔法",
                "the uid reverse lookup covers any online character, not only the caller's friends");
            var passerbyBody = new byte[] { 0, 0, 11, 0, 3 }; // 序列号 + uid=11 + mode=3
            var passerbyDetail = await EntranceService.ResolveUserInfoReplyAsync(
                passerbyBody,
                uid => service.TryGetCharacterByAreaUserId(uid, out var foundName) ? foundName : null,
                (uid, _) => Task.FromResult(GameProtocolEngine.CreateRequestedUserInfoDetail(uid)),
                () => Task.FromResult(GameProtocolEngine.CreateUserInfo([])));
            check(passerbyDetail is not null
                    && passerbyDetail.ProtocolId == GameProtocolEngine.UserInfoNotification
                    && passerbyDetail.Payload[0] == 3
                    && BinaryPrimitives.ReadUInt16LittleEndian(passerbyDetail.Payload.AsSpan(1, 2)) == 1
                    && BinaryPrimitives.ReadUInt16LittleEndian(passerbyDetail.Payload.AsSpan(3, 2)) == 11,
                "a mode-3 request for a non-friend online character returns that character's profile with the uid echoed");

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
            check(erinMessages.All(packet => packet.ProtocolId != 65),
                "a whispered line never opens or writes the target's 1:1 window");
            var whisperEchoes = davePackets.Skip(daveBeforeWhisper).ToArray();
            check(whisperEchoes.Length == 0,
                "the whisper sender gets no packet at all, so nothing can mirror into a 1:1 window");

            // CMD 17 类型 18（1:1 窗口消息）：接收方收「发送者 mode-0 记录 → NOTI 65
            // type 0x12 flag 0」；发送方自己的行由客户端本地追加，只补一条对端记录。
            var erinBeforeMessenger = erinPackets.Count;
            var daveBeforeMessenger = davePackets.Count;
            var daveMessengerAppearance = GameProtocolEngine.CreateCurrentCharacterInfo(
                new GameCharacterSummary(0, encoding.GetBytes("戴夫"), Job: 0, GrowType: 4, Level: 20),
                userId: 4);
            await service.SendMessengerMessageAsync(
                "戴夫",
                daveMessengerAppearance,
                encoding.GetBytes("艾琳"),
                encoding.GetBytes("1:1消息"),
                CancellationToken.None);
            var erinMessenger = erinPackets.Skip(erinBeforeMessenger).ToArray();
            check(erinMessenger.Length == 2
                    && ReferenceEquals(erinMessenger[0], daveMessengerAppearance)
                    && erinMessenger[0].Payload[0] == 0
                    && ReadUserInfoRecordUid(erinMessenger[0]) == 4
                    && UserInfoRecordNameEquals(erinMessenger[0], "戴夫")
                    && IsMessengerMessage(erinMessenger[1], "戴夫", "1:1消息"),
                "a 1:1 message pushes the sender's mode-0 record (uid = the sender's area user id 4) to the peer, then the flag-0 NOTI 65 body");
            var daveMessengerReply = davePackets.Skip(daveBeforeMessenger).ToArray();
            check(daveMessengerReply.Length == 1
                    && daveMessengerReply[0].ProtocolId == GameProtocolEngine.UserInfoNotification
                    && daveMessengerReply[0].Payload[0] == 0
                    && ReadUserInfoRecordUid(daveMessengerReply[0]) == 7
                    && UserInfoRecordNameEquals(daveMessengerReply[0], "艾琳"),
                "the 1:1 sender only gets the target's minimal mode-0 record (uid = the target's area user id 7)");
            check(daveMessengerReply.All(packet => packet.ProtocolId != 65),
                "the 1:1 sender gets no NOTI 65 echo: the client appends its own line locally after sending");

            var daveBeforeSelfMessenger = davePackets.Count;
            await service.SendMessengerMessageAsync(
                "戴夫",
                senderAppearance: null,
                encoding.GetBytes("戴夫"),
                encoding.GetBytes("自言自语"),
                CancellationToken.None);
            var selfMessenger = davePackets.Skip(daveBeforeSelfMessenger).ToArray();
            check(selfMessenger.Length == 1
                    && selfMessenger[0].ProtocolId == GameProtocolEngine.MessageNotification
                    && selfMessenger[0].Payload[0] == (byte)GameMessageType.Type03White
                    && ReadMessageText(selfMessenger[0]) == "不能和自己进行1:1聊天",
                "a 1:1 message to yourself answers with the 1:1 wording instead of the whisper notice");

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
            check(davePackets.Count == daveBeforeBrokenWhisper,
                "a broken target channel leaks no exception and fabricates no sender packet for a whisper");
            // 1:1 时发送者侧的目标记录走 SendAsync（不经过目标通道），坏通道也不能吞掉它。
            var daveBeforeBrokenMessenger = davePackets.Count;
            await service.SendMessengerMessageAsync(
                "戴夫",
                senderAppearance: null,
                encoding.GetBytes("艾琳"),
                encoding.GetBytes("1:1消息"),
                CancellationToken.None);
            check(davePackets.Skip(daveBeforeBrokenMessenger).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.UserInfoNotification
                    && ReadUserInfoRecordUid(packet) == 7),
                "a broken target channel still delivers the target record to the 1:1 sender");

            var daveBeforeDetach = davePackets.Count;
            await service.DetachChannelAsync("艾琳", brokenErinSession, CancellationToken.None);
            var daveOfflinePackets = davePackets.Skip(daveBeforeDetach)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification)
                .ToArray();
            check(daveOfflinePackets.Length == 2
                    && daveOfflinePackets[0].Payload[^1] == 0
                    && daveOfflinePackets[1].Payload[^1] == 1
                    && BuddyNameEquals(daveOfflinePackets[0], "艾琳")
                    && BuddyNameEquals(daveOfflinePackets[1], "艾琳"),
                "a detached session marks the character offline for its friends (notice then grey flag)");
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
                senderAppearance: null,
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
                    && ginaPackets[1].Payload[^1] == 1
                    && BuddyNameExactly(ginaPackets[1], "ECHO"),
                "an offline friend is snapshotted and marked offline (flag 1) under its stored spelling");

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
            var caseBuddyOuts = ginaPackets.Skip(ginaBeforeEchoDetach)
                .Where(packet => packet.ProtocolId == GameProtocolEngine.BuddyOutNotification)
                .ToArray();
            check(caseBuddyOuts.Length == 2
                    && caseBuddyOuts[0].Payload[^1] == 0
                    && caseBuddyOuts[1].Payload[^1] == 1
                    && BuddyNameExactly(caseBuddyOuts[0], "ECHO")
                    && BuddyNameExactly(caseBuddyOuts[1], "ECHO")
                    && !BuddyNameExactly(caseBuddyOuts[0], "Echo")
                    && !BuddyNameExactly(caseBuddyOuts[1], "Echo"),
                "the two logout BUDDY_OUT packets use the recipient's stored spelling, flags 0 then 1");

            // 加好友后的状态更新：id 63 追加出来的条目停在构造默认状态，必须紧跟一条
            // BUDDY_IN/BUDDY_OUT（按名字就地改状态、不会追加），否则“刚加的在线好友”
            // 会一直显示离线，直到对方下一次上线/重登。
            await store.CreateAccountAsync(new CreateAccountRequest("ivan", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("judy", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("kent", "password", OicqCode: "123456"), CancellationToken.None);
            var ivanFixture = await store.CreateCharacterAsync("ivan", new CreateCharacterRequest("伊万"), CancellationToken.None);
            var judyFixture = await store.CreateCharacterAsync("judy", new CreateCharacterRequest("朱迪"), CancellationToken.None);
            var kentFixture = await store.CreateCharacterAsync("kent", new CreateCharacterRequest("肯特"), CancellationToken.None);
            check(ivanFixture.Created && judyFixture.Created && kentFixture.Created,
                "the add-state fixture creates its three characters");
            var ivanPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "ivan", "伊万", areaUserId: 0, Guid.NewGuid(), Record(ivanPackets), CancellationToken.None);
            await service.AttachChannelAsync(
                "judy", "朱迪", areaUserId: 21, Guid.NewGuid(), Record(new List<GameServerPacket>()), CancellationToken.None);

            var ivanBeforeOnlineAdd = ivanPackets.Count;
            await service.AddFriendAsync("ivan", "伊万", "朱迪", CancellationToken.None);
            var onlineAddTail = ivanPackets.Skip(ivanBeforeOnlineAdd).ToArray();
            check(onlineAddTail.Length == 3
                    && IsAddFriendResult(
                        onlineAddTail[0],
                        "朱迪",
                        areaUserId: 21,
                        level: (ushort)judyFixture.Character!.Level,
                        job: (byte)Math.Clamp(judyFixture.Character.Job, 0, 4),
                        growType: (byte)Math.Clamp(judyFixture.Character.GrowType, 0, 15),
                        awakeningType: (byte)Math.Clamp(judyFixture.Character.AwakeningType, 0, 7))
                    && onlineAddTail[1].ProtocolId == GameProtocolEngine.BuddyInNotification
                    && onlineAddTail[1].Payload[0] == 0
                    && onlineAddTail[1].Payload[^1] == 0
                    && BuddyNameExactly(onlineAddTail[1], "朱迪"),
                "adding an online friend sends the id-63 append carrying his level and uid, then a flag-0 BUDDY_IN");
            check(onlineAddTail.Length == 3
                    && onlineAddTail[2].ProtocolId == GameProtocolEngine.UserInfoNotification
                    && onlineAddTail[2].Payload[0] == 0
                    && ReadUserInfoRecordUid(onlineAddTail[2]) == 21
                    && UserInfoRecordNameEquals(onlineAddTail[2], "朱迪"),
                "the online friend's USERINFO record is pushed last, so 查看信息 can resolve the freshly added uid");
            check(onlineAddTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "the online-friend add still sends no snapshot");

            var ivanBeforeOfflineAdd = ivanPackets.Count;
            await service.AddFriendAsync("ivan", "伊万", "肯特", CancellationToken.None);
            var offlineAddTail = ivanPackets.Skip(ivanBeforeOfflineAdd).ToArray();
            check(offlineAddTail.Length == 2
                    && IsAddFriendResult(
                        offlineAddTail[0],
                        "肯特",
                        areaUserId: 0,
                        level: (ushort)kentFixture.Character!.Level,
                        job: (byte)Math.Clamp(kentFixture.Character.Job, 0, 4),
                        growType: (byte)Math.Clamp(kentFixture.Character.GrowType, 0, 15),
                        awakeningType: (byte)Math.Clamp(kentFixture.Character.AwakeningType, 0, 7))
                    && offlineAddTail[1].ProtocolId == GameProtocolEngine.BuddyOutNotification
                    && offlineAddTail[1].Payload[^1] == 1
                    && BuddyNameExactly(offlineAddTail[1], "肯特"),
                "adding an offline friend sends the id-63 append with D=0, then a flag-1 BUDDY_OUT");
            check(offlineAddTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.UserInfoNotification),
                "the offline-friend add pushes no USERINFO record");
            check(offlineAddTail.All(packet =>
                        packet.ProtocolId != GameProtocolEngine.BuddyConnListNotification),
                "the offline-friend add still sends no snapshot");

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

    // 黑名单服务层：三条命令的应答、130 的可重发全量快照，以及“客户端不拦、服务端
    // 双向拦”的悄悄话/1:1 过滤（viewinfo-blacklist-recon.md §2.4）。
    private static async Task CheckBlacklistServiceAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dflegacy-blacklist-service-tests", Guid.NewGuid().ToString("N"));
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

            await store.CreateAccountAsync(new CreateAccountRequest("alpha", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("bravo", "password", OicqCode: "123456"), CancellationToken.None);
            check((await store.CreateCharacterAsync(
                        "alpha",
                        new CreateCharacterRequest("阿尔法", GrowType: 3),
                        CancellationToken.None)).Created
                    && (await store.CreateCharacterAsync(
                        "bravo",
                        new CreateCharacterRequest("布拉沃", GrowType: 2),
                        CancellationToken.None)).Created,
                "the blacklist service fixture creates its characters");

            var alphaPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "alpha", "阿尔法", areaUserId: 11, Guid.NewGuid(), Record(alphaPackets), CancellationToken.None);
            var bravoPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "bravo", "布拉沃", areaUserId: 1, Guid.NewGuid(), Record(bravoPackets), CancellationToken.None);

            // CMD 128：成功回名字；重复回 74；找不到角色回 76。
            var beforeAdd = alphaPackets.Count;
            await service.AddBlacklistAsync(
                "alpha", "阿尔法", encoding.GetBytes("布拉沃"), CancellationToken.None);
            check(alphaPackets.Skip(beforeAdd).Count() == 1
                    && IsBlacklistResult(alphaPackets[beforeAdd], 128, "布拉沃"),
                "CMD 128 success sends exactly one type-1 id-128 result echoing the stored name");
            var beforeDuplicate = alphaPackets.Count;
            await service.AddBlacklistAsync(
                "alpha", "阿尔法", encoding.GetBytes("布拉沃"), CancellationToken.None);
            check(alphaPackets.Skip(beforeDuplicate).Count() == 1
                    && IsBlacklistError(alphaPackets[beforeDuplicate], 128, 74),
                "a duplicate CMD 128 answers [0][74] instead of a success flag");
            var beforeUnknown = alphaPackets.Count;
            await service.AddBlacklistAsync(
                "alpha", "阿尔法", encoding.GetBytes("不存在的人"), CancellationToken.None);
            check(alphaPackets.Skip(beforeUnknown).Count() == 1
                    && IsBlacklistError(alphaPackets[beforeUnknown], 128, 76),
                "blacklisting an unknown character answers [0][76]");

            // CMD 130：全量快照；可随时重发（客户端先 clear 再追加），没有每会话一次的约束。
            var beforeSnapshot = alphaPackets.Count;
            await service.SendBlacklistSnapshotAsync("alpha", "阿尔法", CancellationToken.None);
            check(alphaPackets.Skip(beforeSnapshot).Count() == 1
                    && IsBlacklistSnapshot(alphaPackets[beforeSnapshot], ["布拉沃"]),
                "CMD 130 replies the full snapshot on the type-1 id-130 packet");
            var beforeSecondSnapshot = alphaPackets.Count;
            await service.SendBlacklistSnapshotAsync("alpha", "阿尔法", CancellationToken.None);
            check(alphaPackets.Skip(beforeSecondSnapshot).Count() == 1
                    && IsBlacklistSnapshot(alphaPackets[beforeSecondSnapshot], ["布拉沃"]),
                "CMD 130 can be repeated: the client clears and re-appends, so there is no once-per-session guard");

            // 服务端双向拦截：A 拉黑 B 后，A→B 被拒且发起者（A）收到提示；B→A 也被拒，
            // 但被拉黑的一方（B）**一个包都不收**（原版“被拉黑的人看不到任何提示”）。
            var alphaBeforeBlockedWhisper = alphaPackets.Count;
            var bravoBeforeBlockedWhisper = bravoPackets.Count;
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 11,
                senderAppearance: GameProtocolEngine.CreateCurrentCharacterInfo(
                    new GameCharacterSummary(0, encoding.GetBytes("阿尔法"), Job: 0, GrowType: 3, Level: 20),
                    userId: 11),
                encoding.GetBytes("布拉沃"),
                messageType: 1,
                encoding.GetBytes("你好"),
                CancellationToken.None);
            var blockedBySender = alphaPackets.Skip(alphaBeforeBlockedWhisper).ToArray();
            check(bravoPackets.Count == bravoBeforeBlockedWhisper,
                "a blacklisted target receives nothing at all, not even the sender's appearance record");
            check(blockedBySender.Length == 1
                    && IsWhiteNotice(blockedBySender[0], "对方当前无法接收消息"),
                "the whisperer who blacklisted the peer gets one white notice and no delivery");

            var alphaBeforeCounterWhisper = alphaPackets.Count;
            var bravoBeforeCounterWhisper = bravoPackets.Count;
            await service.WhisperAsync(
                "布拉沃",
                senderAreaUserId: 1,
                senderAppearance: null,
                encoding.GetBytes("阿尔法"),
                messageType: 1,
                encoding.GetBytes("在吗"),
                CancellationToken.None);
            check(alphaPackets.Count == alphaBeforeCounterWhisper
                    && bravoPackets.Count == bravoBeforeCounterWhisper,
                "a whisperer blacklisted by the peer is dropped in total silence: no packet to either side");
            check(alphaPackets.Skip(alphaBeforeBlockedWhisper)
                    .Concat(bravoPackets.Skip(bravoBeforeBlockedWhisper))
                    .All(packet => packet.Type != GameProtocolEngine.CommandPacketType),
                "a refused whisper sends no type-1 command packet to either side");

            // 1:1（NOTI 65）复用同一套两向检查：原版客户端在这条路径上同样不拦；
            // 命中对端名单的那一路同样静默（一个包都不发）。
            var alphaBeforeBlockedMessenger = alphaPackets.Count;
            var bravoBeforeBlockedMessenger = bravoPackets.Count;
            await service.SendMessengerMessageAsync(
                "布拉沃",
                senderAppearance: null,
                encoding.GetBytes("阿尔法"),
                encoding.GetBytes("1:1消息"),
                CancellationToken.None);
            check(alphaPackets.Count == alphaBeforeBlockedMessenger
                    && bravoPackets.Count == bravoBeforeBlockedMessenger,
                "a 1:1 message from a blacklisted peer is dropped silently too: no NOTI 65 and no notice");

            // 解除拉黑后投递恢复（129 回成功结果，拦截随之解除）。
            await service.RemoveBlacklistAsync(
                "alpha", "阿尔法", encoding.GetBytes("布拉沃"), CancellationToken.None);
            check(IsBlacklistResult(alphaPackets[^1], 129, "布拉沃"),
                "CMD 129 success sends the id-129 result with the name");
            var bravoBeforeUnblocked = bravoPackets.Count;
            await service.WhisperAsync(
                "阿尔法",
                senderAreaUserId: 11,
                senderAppearance: null,
                encoding.GetBytes("布拉沃"),
                messageType: 1,
                encoding.GetBytes("现在可以了"),
                CancellationToken.None);
            check(bravoPackets.Skip(bravoBeforeUnblocked).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "现在可以了"),
                "removing the blacklist entry lifts the whisper block");
            var beforeFailedRemoval = alphaPackets.Count;
            await service.RemoveBlacklistAsync(
                "alpha", "阿尔法", encoding.GetBytes("布拉沃"), CancellationToken.None);
            check(alphaPackets.Skip(beforeFailedRemoval).Count() == 1
                    && IsBlacklistError(alphaPackets[beforeFailedRemoval], 129, 0x4B),
                "removing a name that is not on the list answers [0][0x4B]");

            // 名单已满：服务层把 BlacklistFull 映射成 78。
            await store.CreateAccountAsync(new CreateAccountRequest("crowd", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("delta", "password", OicqCode: "123456"), CancellationToken.None);
            for (var index = 0; index < 11; index++)
            {
                await store.CreateCharacterAsync(
                    "crowd",
                    new CreateCharacterRequest($"备胎{index:D2}"),
                    CancellationToken.None);
            }

            check((await store.CreateCharacterAsync(
                    "delta",
                    new CreateCharacterRequest("德尔塔"),
                    CancellationToken.None)).Created,
                "the full-blacklist fixture creates its owner character");
            for (var index = 0; index < 10; index++)
            {
                await store.AddBlacklistAsync("delta", $"备胎{index:D2}", CancellationToken.None);
            }

            var deltaPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "delta", "德尔塔", areaUserId: 21, Guid.NewGuid(), Record(deltaPackets), CancellationToken.None);
            var beforeFullAdd = deltaPackets.Count;
            await service.AddBlacklistAsync(
                "delta", "德尔塔", encoding.GetBytes("备胎10"), CancellationToken.None);
            check(deltaPackets.Skip(beforeFullAdd).Count() == 1
                    && IsBlacklistError(deltaPackets[beforeFullAdd], 128, 78),
                "a full blacklist answers [0][78] and sends no success flag");

            // 大小写敏感：拦截比较是逐字节 Ordinal（客户端 CNRDBlackListdManager::FindByName）。
            // 精确拼写命中 -> 拒绝；只差大小写 -> 不算命中，照常投递。
            await store.CreateAccountAsync(new CreateAccountRequest("frank", "password", OicqCode: "123456"), CancellationToken.None);
            await store.CreateAccountAsync(new CreateAccountRequest("gina", "password", OicqCode: "123456"), CancellationToken.None);
            check((await store.CreateCharacterAsync(
                        "frank",
                        new CreateCharacterRequest("Echo"),
                        CancellationToken.None)).Created
                    && (await store.CreateCharacterAsync(
                        "gina",
                        new CreateCharacterRequest("Gina"),
                        CancellationToken.None)).Created,
                "the case-sensitivity fixture creates the mixed-case characters");
            check(await store.AddBlacklistAsync("gina", "Echo", CancellationToken.None)
                    == BlacklistUpdateFailure.None,
                "gina blacklists the exact spelling Echo");
            var frankPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "frank", "Echo", areaUserId: 5, Guid.NewGuid(), Record(frankPackets), CancellationToken.None);
            var ginaPackets = new List<GameServerPacket>();
            await service.AttachChannelAsync(
                "gina", "Gina", areaUserId: 6, Guid.NewGuid(), Record(ginaPackets), CancellationToken.None);

            var ginaBeforeExact = ginaPackets.Count;
            var frankBeforeExact = frankPackets.Count;
            await service.WhisperAsync(
                "Gina",
                senderAreaUserId: 6,
                senderAppearance: null,
                encoding.GetBytes("Echo"),
                messageType: 1,
                encoding.GetBytes("精确拼写"),
                CancellationToken.None);
            check(ginaPackets.Skip(ginaBeforeExact).Count() == 1
                    && IsWhiteNotice(ginaPackets[ginaBeforeExact], "对方当前无法接收消息")
                    && frankPackets.Count == frankBeforeExact,
                "the exact blacklisted spelling is refused before delivery");

            var ginaBeforeDifferentCase = ginaPackets.Count;
            var frankBeforeDifferentCase = frankPackets.Count;
            await service.WhisperAsync(
                "Gina",
                senderAreaUserId: 6,
                senderAppearance: null,
                encoding.GetBytes("echo"),
                messageType: 1,
                encoding.GetBytes("不同大小写"),
                CancellationToken.None);
            check(frankPackets.Skip(frankBeforeDifferentCase).Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && ReadMessageText(packet) == "不同大小写"),
                "a differently-cased name is not the blacklisted entry: the whisper is still delivered");
            check(ginaPackets.Skip(ginaBeforeDifferentCase).All(packet =>
                    !IsWhiteNotice(packet, "对方当前无法接收消息")),
                "a case-mismatched name draws no blacklist notice");
            check(await store.RemoveBlacklistAsync("gina", "echo", CancellationToken.None)
                    == BlacklistUpdateFailure.NotBlacklisted,
                "the stored entry cannot be removed with a differently-cased spelling");
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

    // 42 字节记录偏移 0x21 的 u16：好友行 `Lv.%d` 直接读它（旧结论“必须为 0”已作废）。
    private static ushort FriendRecordLevel(GameServerPacket snapshot, int index) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            snapshot.Payload.AsSpan(1 + index * 42 + 0x21, 2));

    // 记录偏移 0x23（职业系）与 0x24（打包成长）：行在无本地 user 记录时就用它们画职业。
    private static (byte Job, byte PackedGrowth) FriendRecordClass(GameServerPacket snapshot, int index)
    {
        var record = snapshot.Payload.AsSpan(1 + index * 42, 42);
        return (record[0x23], record[0x24]);
    }

    // mode-0 USERINFO：payload = [u8 mode][u16 count=1][u16 uid][u32 名字长度 + 名字]…
    private static ushort ReadUserInfoRecordUid(GameServerPacket packet) =>
        BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3, 2));

    private static bool UserInfoRecordNameEquals(GameServerPacket packet, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.ProtocolId == GameProtocolEngine.UserInfoNotification
            && packet.Payload.Length >= 9 + nameBytes.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(5, 4))
                == nameBytes.Length
            && packet.Payload.AsSpan(9, nameBytes.Length).SequenceEqual(nameBytes);
    }

    // mode-0 USERINFO 名字之后的职业 / 成长 / 等级三字节（存档角色的最小投影）。
    private static (byte Job, byte Growth, byte Level) ReadUserInfoRecordClass(GameServerPacket packet)
    {
        var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(5, 4));
        var offset = 9 + nameLength;
        return (packet.Payload[offset], packet.Payload[offset + 1], packet.Payload[offset + 2]);
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

    // 比 BuddyNameEquals 更严：状态字节后必须正好是 u32 长度 + 完整名字 + 标志字节，
    // 用来钉死"推送里出现的就是这个名字的这份拼写"。末尾标志字节不在这里判：
    // BUDDY_IN 是 0、BUDDY_OUT 是 1（行转灰），各自在调用点显式断言。
    private static bool BuddyNameExactly(GameServerPacket packet, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.Payload.Length == 1 + sizeof(uint) + nameBytes.Length + 1
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(1, sizeof(uint)))
                == nameBytes.Length
            && packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes);
    }

    // type=1 删除结果（id 64）：[非零标志][u32 名字长度 + 名字][u8 可选结果码]。
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

    // type=1/id63 加好友成功结果（完整 14+nameLen 字节）：
    // [非零标志][u32 名字长度 + 名字][A=等级][B=职业系][C=打包成长][u32 D=uid][E=0][F=0]。
    // B 必须 < 5（客户端 AppendEntry 的守卫），尾部少发会被填 0x30 直接拒绝追加。
    private static bool IsAddFriendResult(
        GameServerPacket packet,
        string name,
        ushort areaUserId,
        ushort level,
        byte job,
        byte growType,
        byte awakeningType)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        if (packet.Type != 1
            || packet.ProtocolId != 63
            || packet.Payload.Length != 14 + nameBytes.Length
            || packet.Payload[0] == 0
            || BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(1, sizeof(uint)))
                != nameBytes.Length
            || !packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes))
        {
            return false;
        }

        var tail = packet.Payload.AsSpan(5 + nameBytes.Length);
        return tail[0] == (byte)Math.Clamp((int)level, 0, byte.MaxValue)
            && tail[1] == (byte)Math.Clamp((int)job, 0, 4)
            && tail[2] == (byte)((growType & 0x0F) | ((awakeningType & 0x07) << 4))
            && BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(3, 4)) == areaUserId
            && tail[7] == 0
            && tail[8] == 0;
    }

    // type=1 黑名单成功结果（128/129）：[非零标志][u32 名字长度 + 名字]。id 与字节值
    // 都写死成字面量：命令表/布局被改动时这些断言必须变红。
    private static bool IsBlacklistResult(GameServerPacket packet, byte command, string name)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        return packet.Type == 1
            && packet.ProtocolId == command
            && packet.Payload.Length == 1 + sizeof(uint) + nameBytes.Length
            && packet.Payload[0] != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(1, sizeof(uint)))
                == nameBytes.Length
            && packet.Payload.AsSpan(5, nameBytes.Length).SequenceEqual(nameBytes);
    }

    // type=1 黑名单失败结果：[0][u8 失败码]。
    private static bool IsBlacklistError(GameServerPacket packet, byte command, byte errorCode) =>
        packet.Type == 1
        && packet.ProtocolId == command
        && packet.Payload.SequenceEqual(new[] { (byte)0, errorCode });

    // type=1 id130 全量快照：名字与顺序完全一致、每条带 3 字节日期才算过
    // （130 的语义就是 clear + 全量追加，快照必须覆盖整份名单）。
    private static bool IsBlacklistSnapshot(GameServerPacket packet, IReadOnlyList<string> names)
    {
        var clientEncoding = Encoding.GetEncoding(936);
        if (packet.Type != 1
            || packet.ProtocolId != 130
            || packet.Payload.Length < 2
            || packet.Payload[0] == 0
            || packet.Payload[1] != names.Count)
        {
            return false;
        }

        var offset = 2;
        foreach (var name in names)
        {
            var nameBytes = clientEncoding.GetBytes(name);
            if (packet.Payload.Length < offset + sizeof(uint) + nameBytes.Length + 3
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    packet.Payload.AsSpan(offset, sizeof(uint))) != nameBytes.Length
                || !packet.Payload
                    .AsSpan(offset + sizeof(uint), nameBytes.Length)
                    .SequenceEqual(nameBytes))
            {
                return false;
            }

            offset += sizeof(uint) + nameBytes.Length + 3;
        }

        return offset == packet.Payload.Length;
    }

    // 白字系统提示（NOTI 12 类型 3）：黑名单拒绝只给发起者这一条。
    private static bool IsWhiteNotice(GameServerPacket packet, string text) =>
        packet.Type == 0
        && packet.ProtocolId == GameProtocolEngine.MessageNotification
        && packet.Payload.Length >= 7
        && packet.Payload[0] == (byte)GameMessageType.Type03White
        && ReadMessageText(packet) == text;

    // NOTI 65：只服务 CMD 17 类型 18 的 1:1 频道，type 恒 0x12；flag 是状态码，只有 0
    // 才投递正文（非 0 只弹“无法接受信息”并丢掉正文）。线上的字节值写死成字面量：
    // 常量被改动时这些断言必须变红。
    private static bool IsMessengerMessage(GameServerPacket packet, string name, string text)
    {
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        var textBytes = Encoding.GetEncoding(936).GetBytes(text);
        return packet.Type == 0
            && packet.ProtocolId == 65
            && packet.Payload.Length == 2 + 4 + nameBytes.Length + 4 + textBytes.Length
            && packet.Payload[0] == 0x12
            && packet.Payload[1] == 0
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
