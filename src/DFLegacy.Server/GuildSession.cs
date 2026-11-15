using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record GuildSessionResult(GameServerPacket[] Packets, CharacterRecord? Character = null,
    bool ProgressionChanged = false);

public sealed partial class GuildSession(JsonGameStore store, ScriptFileSystem scripts, CharacterSessionRegistry sessions)
{
    private Guid _characterId;
    private string? _name;
    private string? _address;

    public static bool Handles(byte command) => command is GameProtocolEngine.GuildMembersCommand
        or GameProtocolEngine.GuildCreateCommand or GameProtocolEngine.GuildInfoCommand
        or GameProtocolEngine.GuildAllMembersCommand or GameProtocolEngine.GuildMembersNextCommand
        or GameProtocolEngine.GuildSetAnnouncementCommand
        or GameProtocolEngine.GuildCheckNameCommand or GameProtocolEngine.GuildCheckAddressCommand
        or GameProtocolEngine.GuildOpenCreateCommand or GameProtocolEngine.GuildDissolveCommand
        or GameProtocolEngine.GuildInviteCommand or GameProtocolEngine.GuildInviteResponseCommand
        or GameProtocolEngine.GuildSecedeCommand or GameProtocolEngine.GuildDelegateMasterCommand
        or GameProtocolEngine.GuildSetMemberRankCommand
        or GameProtocolEngine.GuildUpgradeCommand or 78;

    internal static GameServerPacket CreateInfoPacket(GuildRecord guild, byte rank, int memberCount) =>
        GameProtocolEngine.CreateGuildInfo(GuildRules.Encode(guild.Name), rank, checked((ushort)memberCount),
            GuildRules.Encode(guild.Announcement ?? ""), guild.Level, guild.Experience, guild.SkillPoints,
            GuildBenefits.For(guild, 1).Skills().ToArray());

    public void Reset()
    {
        _characterId = Guid.Empty;
        _name = null;
        _address = null;
    }

    public async Task<GameServerPacket[]> InitialStateAsync(CharacterRecord character, CancellationToken ct)
    {
        var snapshot = await store.GetGuildAsync(character.GuildId, ct);
        return snapshot.Guild is null ? [] :
            [CreateInfoPacket(snapshot.Guild, character.GuildRank, snapshot.Members.Length)];
    }

    public async Task<GuildSessionResult> HandleAsync(PacketFrame request, string accountName,
        CharacterRecord? character, ushort userId, bool inTown, CancellationToken ct, int skillPoints = 0)
    {
        GuildSessionResult Error(byte error) => new([request.ProtocolId == GameProtocolEngine.GuildDissolveCommand
            ? GameProtocolEngine.CreateGuildDissolved(false) : GameProtocolEngine.CreateCommandError(request, error)]);
        if (character is null || !inTown) { Reset(); return Error(17); }
        if (_characterId != character.Id) { Reset(); _characterId = character.Id; }
        if (request.ProtocolId is GameProtocolEngine.GuildInviteCommand or GameProtocolEngine.GuildInviteResponseCommand)
            return await HandleInvitationAsync(request, accountName, character, ct);
        if (request.ProtocolId is GameProtocolEngine.GuildSecedeCommand or GameProtocolEngine.GuildDelegateMasterCommand
            or GameProtocolEngine.GuildSetMemberRankCommand)
            return await HandleMembershipAsync(request, accountName, character, ct);
        if (request.ProtocolId == GameProtocolEngine.GuildUpgradeCommand || GuildProgression.IsPurchase(request))
            return await HandleProgressionAsync(request, accountName, character, skillPoints, ct);
        if (request.ProtocolId == 78)
            return new([GameProtocolEngine.CreateCommandError(request, 17),
                GameProtocolEngine.CreateMessageNotification(GameMessageType.Type16Brown, userId, GuildRules.Encode("公会申请加入暂未开放。"))]);
        if (request.ProtocolId == GameProtocolEngine.GuildOpenCreateCommand)
        {
            _name = null;
            _address = null;
            return new([]); // C2 opens locally and has no CMD169 receiver.
        }
        if (request.ProtocolId is GameProtocolEngine.GuildCheckNameCommand or GameProtocolEngine.GuildCheckAddressCommand)
        {
            var nameCheck = request.ProtocolId == GameProtocolEngine.GuildCheckNameCommand;
            if (nameCheck) _name = null; else _address = null;
            if (!GameProtocolEngine.TryReadGuildText(request.Body, out var bytes)) return Error(nameCheck ? (byte)106 : (byte)110);
            string value;
            var valid = nameCheck ? GuildRules.TryReadName(bytes, out value) : GuildRules.TryReadAddress(bytes, out value);
            if (!valid) return Error(nameCheck ? (byte)108 : (byte)112);
            var available = nameCheck ? await store.IsGuildNameAvailableAsync(value, ct)
                : await store.IsGuildAddressAvailableAsync(value, ct);
            if (!available) return Error(nameCheck ? (byte)107 : (byte)111);
            if (nameCheck) _name = value; else _address = value;
            return new([GameProtocolEngine.CreateCommandReply(request, true)]);
        }
        if (request.ProtocolId == GameProtocolEngine.GuildSetAnnouncementCommand)
        {
            if (!GameProtocolEngine.TryReadGuildAnnouncement(request.Body, out var announcement)) return Error(108);
            var update = await store.SetGuildAnnouncementAsync(accountName, character.Id, character.GuildId, announcement, ct);
            if (update.Error != 0) return Error(update.Error);
            if (update.Members is not null)
                foreach (var member in update.Members)
                    sessions.NotifyGuildAnnouncementChanged(member, character.GuildId);
            return new([GameProtocolEngine.CreateCommandReply(request, true)]);
        }
        if (!GameProtocolEngine.HasCommandBodyLength(request.Body, 2, true)) return Error(17);
        if (request.ProtocolId == GameProtocolEngine.GuildDissolveCommand)
        {
            var dissolution = await store.DissolveGuildAsync(accountName, character.Id, character.GuildId, ct);
            if (dissolution is null) return Error(17);
            Reset();
            foreach (var member in dissolution.OtherMembers)
                sessions.NotifyGuildDissolved(member, character.GuildId);
            return new([GameProtocolEngine.CreateGuildDissolved(true)], dissolution.Founder);
        }
        if (request.ProtocolId == GameProtocolEngine.GuildCreateCommand)
        {
            if (_name is null) return Error(109);
            if (_address is null) return Error(113);
            var result = await store.CreateGuildAsync(accountName, character.Id, _name, _address,
                GuildRules.CreationCost(scripts), ct);
            if (result.Error != 0) return Error(result.Error);
            var info = await InitialStateAsync(result.Character!, ct);
            return new([GameProtocolEngine.CreateCommandReply(request, true),
                GameProtocolEngine.CreateGuildCreated(result.Guild!.Id, GuildRules.Encode(result.Guild.Name)),
                ..info, GameProtocolEngine.CreateGuildSelfRank(result.Character!.GuildRank)], result.Character);
        }
        var guild = await store.GetGuildAsync(character.GuildId, ct);
        if (guild.Guild is null) return Error(32);
        if (request.ProtocolId == GameProtocolEngine.GuildInfoCommand)
            return new([GameProtocolEngine.CreateCommandReply(request, true), ..await InitialStateAsync(character, ct)]);
        return new([CreateRosterPacket(request.ProtocolId, guild.Guild, guild.Members)]);
    }

    internal GameServerPacket CreateRosterPacket(byte command, GuildRecord guild, CharacterRecord[] members) =>
        CreateRosterPacket(command, guild, members, sessions);

    internal static GameServerPacket CreateRosterPacket(byte command, GuildRecord guild, CharacterRecord[] members,
        CharacterSessionRegistry sessions)
    {
        var rows = command == GameProtocolEngine.GuildMembersNextCommand ? [] : members
            .Select(member => new GameGuildMember(GuildRules.Encode(member.Name), (ushort)Math.Clamp(member.Level, 1, 255),
                (byte)member.Job, (byte)member.GrowType, (byte)member.AwakeningType,
                member.GuildRank, sessions.IsOnline(member.Id))).ToArray();
        return GameProtocolEngine.CreateGuildMembers(command, guild.Id, GuildRules.Encode(guild.Name), rows);
    }
}
