using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record GuildSessionResult(GameServerPacket[] Packets, CharacterRecord? Character = null);

public sealed class GuildSession(JsonGameStore store, ScriptFileSystem scripts, CharacterSessionRegistry sessions)
{
    private Guid _characterId;
    private string? _name;
    private string? _address;

    public static bool Handles(byte command) => command is GameProtocolEngine.GuildMembersCommand
        or GameProtocolEngine.GuildCreateCommand or GameProtocolEngine.GuildInfoCommand
        or GameProtocolEngine.GuildAllMembersCommand or GameProtocolEngine.GuildMembersNextCommand
        or GameProtocolEngine.GuildCheckNameCommand or GameProtocolEngine.GuildCheckAddressCommand
        or GameProtocolEngine.GuildOpenCreateCommand or 76 or 78 or 162 or 163 or 164 or 165 or 166;

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
            [GameProtocolEngine.CreateGuildInfo(GuildRules.Encode(snapshot.Guild.Name), character.GuildRank,
                checked((ushort)snapshot.Members.Length))];
    }

    public async Task<GuildSessionResult> HandleAsync(PacketFrame request, string accountName,
        CharacterRecord? character, ushort userId, bool inTown, CancellationToken ct)
    {
        GuildSessionResult Error(byte error) => new([GameProtocolEngine.CreateCommandError(request, error)]);
        if (character is null || !inTown) { Reset(); return Error(17); }
        if (_characterId != character.Id) { Reset(); _characterId = character.Id; }
        var unavailable = request.ProtocolId switch
        {
            76 => "公会升级暂未开放。",
            78 => "公会申请加入暂未开放。",
            162 or 163 => "公会邀请加入暂未开放。",
            164 => "公会退出与移除成员暂未开放。",
            165 => "公会公告暂未开放。",
            166 => "公会会长转让暂未开放。",
            _ => null
        };
        if (unavailable is not null)
            return new([GameProtocolEngine.CreateCommandError(request, 17),
                GameProtocolEngine.CreateMessageNotification(GameMessageType.Type16Brown, userId, GuildRules.Encode(unavailable))]);
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
        if (!GameProtocolEngine.HasCommandBodyLength(request.Body, 2, true)) return Error(17);
        if (request.ProtocolId == GameProtocolEngine.GuildCreateCommand)
        {
            if (_name is null) return Error(109);
            if (_address is null) return Error(113);
            var result = await store.CreateGuildAsync(accountName, character.Id, _name, _address,
                GuildRules.CreationCost(scripts), ct);
            if (result.Error != 0) return Error(result.Error);
            var info = await InitialStateAsync(result.Character!, ct);
            return new([GameProtocolEngine.CreateCommandReply(request, true),
                GameProtocolEngine.CreateGuildCreated(result.Guild!.Id, GuildRules.Encode(result.Guild.Name)), ..info], result.Character);
        }
        var guild = await store.GetGuildAsync(character.GuildId, ct);
        if (guild.Guild is null) return Error(32);
        if (request.ProtocolId == GameProtocolEngine.GuildInfoCommand)
            return new([GameProtocolEngine.CreateCommandReply(request, true), ..await InitialStateAsync(character, ct)]);
        var members = request.ProtocolId == GameProtocolEngine.GuildMembersNextCommand ? [] : guild.Members
            .Select(member => new GameGuildMember(GuildRules.Encode(member.Name), (ushort)Math.Clamp(member.Level, 1, 255),
                (byte)member.Job, (byte)member.GrowType, (byte)member.AwakeningType,
                member.GuildRank, sessions.IsOnline(member.Id))).ToArray();
        return new([GameProtocolEngine.CreateGuildMembers(request.ProtocolId, guild.Guild.Id,
            GuildRules.Encode(guild.Guild.Name), members)]);
    }
}
