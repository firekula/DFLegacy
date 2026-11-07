using System.Net;
using System.Text;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;

HostingArguments hosting;
try
{
    hosting = HostingArguments.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
    return;
}

if (!File.Exists(hosting.ConfigPath))
{
    Console.Error.WriteLine("The server configuration file does not exist.");
    Environment.ExitCode = 2;
    return;
}

var builder = WebApplication.CreateBuilder(hosting.HostArguments);
// The default Windows Event Log provider can require an administrator-created
// event source. This emulator must be runnable as a normal desktop user.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Configuration.AddJsonFile(
    hosting.ConfigPath,
    optional: false,
    reloadOnChange: true);
// Registered after server.json so environment variables take precedence, as in
// a default host. Container and NAT deployments rely on this:
// DFLegacy__ScriptPvfPath=/pvf/Script.pvf, DFLegacy__Entrance__Host=0.0.0.0,
// DFLegacy__GameplayDatagram__AdvertisedHost=<address clients can reach>, ...
builder.Configuration.AddEnvironmentVariables();

var options = builder.Configuration.GetSection("DFLegacy").Get<ServerOptions>() ?? new ServerOptions();
options.Processes.Validate();
StoreProcessLease storeLease;
try
{
    storeLease = StoreProcessLease.Acquire(options.DataPath);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
    return;
}
using var storeLeaseLifetime = storeLease;
if (hosting.InitializeAdmin)
{
    Environment.ExitCode = await HostingArguments.InitializeAdministratorAsync(options, Console.In, Console.Out);
    return;
}

try { WebPanelHosting.Validate(options); }
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
    return;
}
options.Pvp.Validate(options);
PvpChannelConfiguration.ValidateRegionalChannels(options);
options.FallbackRelay.Validate(options);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var clientTextEncoding = Encoding.GetEncoding(
    936,
    EncoderFallback.ExceptionFallback,
    DecoderFallback.ExceptionFallback);
builder.WebHost.ConfigureKestrel(kestrel =>
    WebPanelHosting.Listen(kestrel, options));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ListenerReadiness>();
builder.Services.AddSingleton<ScriptFileSystem>();
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddSingleton<JsonGameStore>();
// 8081 管理接口的 token 鉴权（内存 token + 滑动过期 + 登录失败限流）。
// TimeProvider 显式注册，便于测试注入假时钟。
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<CharacterSessionRegistry>();
// 好友在线索引按角色名跨连接共享，必须单例：EntranceService 的所有会话共用它。
builder.Services.AddSingleton<FriendService>();
builder.Services.AddSingleton<MentorService>();
builder.Services.AddSingleton<PvpMapCatalog>();
builder.Services.AddSingleton<PvpService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PvpService>());
builder.Services.AddSingleton<PrivateStoreCatalog>();
builder.Services.AddSingleton<PrivateStoreService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<PrivateStoreService>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MentorService>());
// 通道表/会话表同样跨连接共享；托管后台扫描，单例与托管服务必须是同一个实例。
builder.Services.AddSingleton<ItemTradeService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ItemTradeService>());
// 组队通道与队伍表跨连接共享；托管后台扫描（超时收尾），单例与托管服务必须是同一个实例。
builder.Services.AddSingleton<PartyService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<PartyService>());
// 副本实例表跨连接共享（设计稿 §3.1）：单人 = 以角色 id 为键的实例，组队 = 以队伍 id 为键。
builder.Services.AddSingleton<DungeonInstanceRegistry>();
builder.Services.AddHostedService<DungeonDebugOverlayService>();
// 组队副本的编排跨连接共享（设计稿 §3.2）：同区域校验、选图/入场请求、跨会话广播、门口集结扫描。
builder.Services.AddSingleton<DungeonPartyService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DungeonPartyService>());
builder.Services.AddSingleton(serviceProvider =>
{
    // 区用户 id 池只接受 1..65535。MaximumUsers 以前超出这个范围只是无意义，现在会让
    // 工厂抛异常、整个服务起不来；这里夹取并告警，不改 ChannelOptions 的语义。
    var configuredUsers =
        serviceProvider.GetRequiredService<ServerOptions>().Channel.MaximumUsers;
    var capacity = Math.Clamp(configuredUsers, 1, ushort.MaxValue);
    if (capacity != configuredUsers)
    {
        serviceProvider
            .GetRequiredService<ILogger<AreaUserIdPool>>()
            .LogWarning(
                "Channel.MaximumUsers={ConfiguredUsers} is outside 1..{MaximumId}; the area user id pool capacity is clamped to {Capacity}.",
                configuredUsers,
                ushort.MaxValue,
                capacity);
    }

    return new AreaUserIdPool(capacity);
});
builder.Services.AddSingleton<TownSessionDirectory>();
builder.Services.AddSingleton<SkillCatalog>();
builder.Services.AddSingleton<QuestCatalog>();
builder.Services.AddSingleton<CharacterExperienceCatalog>();
builder.Services.AddSingleton<DungeonExperienceCatalog>();
builder.Services.AddSingleton<PvpExperienceCatalog>();
builder.Services.AddSingleton<CharacterSpCatalog>();
builder.Services.AddSingleton<CharacterStatCatalog>();
builder.Services.AddSingleton<CreatureExperienceCatalog>();
builder.Services.AddSingleton<StaminaRecoveryCatalog>();
builder.Services.AddSingleton<ItemCatalog>();
builder.Services.AddSingleton<CompoundCatalog>();
builder.Services.AddSingleton<AvatarCompoundCatalog>();
builder.Services.AddSingleton<EquipmentReinforcementCatalog>();
builder.Services.AddSingleton<ResealCatalog>();
builder.Services.AddSingleton<DisjointCatalog>();
builder.Services.AddSingleton<CeraShopCatalog>();
builder.Services.AddSingleton<PremiumBenefitCatalog>();
builder.Services.AddSingleton<DungeonCatalog>();
builder.Services.AddSingleton<WorldMapCatalog>();
builder.Services.AddSingleton<WorldDropCatalog>();
builder.Services.AddSingleton<GoldDropCatalog>();
builder.Services.AddSingleton<MonsterDropCatalog>();
builder.Services.AddSingleton<MonsterChampionDropCatalog>();
builder.Services.AddSingleton<DungeonDropGenerator>();
builder.Services.AddSingleton<HellDropCatalog>();
builder.Services.AddSingleton<ObjectDropCatalog>();
builder.Services.AddSingleton<ObjectDropGenerator>();
builder.Services.AddSingleton<ClearRewardCatalog>();
builder.Services.AddSingleton<ClearRewardGenerator>();
builder.Services.AddSingleton<GameplayDatagramService>();
builder.Services.AddSingleton<FallbackRelayService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FallbackRelayService>());
builder.Services.AddHostedService<EntranceHandshakeService>();
// I4：game 侧链路客户端（拆分模式拨号 channel 进程；单体模式 ExecuteAsync 直接返回）。
// 单例 + 托管：/api/status 要读同一个 Client 实例。
builder.Services.AddSingleton<ChannelLinkService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ChannelLinkService>());
builder.Services.AddHostedService<EntranceService>();
builder.Services.AddHostedService<CharacterDatagramService>();
builder.Services.AddHostedService<FatigueResetService>();
builder.Services.AddHostedService<WeaknessRecoveryService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<GameplayDatagramService>());
builder.Services.AddHostedService<TownGhostService>();
builder.Services.AddHostedService<ProbeService>();

var app = builder.Build();
var randomStatus = GameRandomSource.Shared.GetStatus();
app.Logger.LogInformation(
    "Game random source initialized: provider={Provider}, cpuRdSeed={CpuRdSeed}, nativeBridge={NativeBridge}, rdSeedEnabled={RdSeedEnabled}.",
    randomStatus.PreferredProvider,
    randomStatus.CpuSupportsRdSeed,
    randomStatus.NativeBridgeLoaded,
    randomStatus.RdSeedEnabled);

// Binding every interface while advertising that same wildcard address leaves
// clients with nowhere to dial. This is the default failure mode of a container
// or NAT deployment that only sets GameplayDatagram.Host.
var advertisedGameplayHost = options.GameplayDatagram.ResolveAdvertisedHost();
if (options.GameplayDatagram.Enabled
    && IPAddress.TryParse(options.GameplayDatagram.Host, out var gameplayBindAddress)
    && (IPAddress.Any.Equals(gameplayBindAddress)
        || IPAddress.IPv6Any.Equals(gameplayBindAddress))
    && (!IPAddress.TryParse(advertisedGameplayHost, out var gameplayAdvertisedAddress)
        || IPAddress.Any.Equals(gameplayAdvertisedAddress)
        || IPAddress.IPv6Any.Equals(gameplayAdvertisedAddress)))
{
    app.Logger.LogWarning(
        "GameplayDatagram.Host is {BindHost} but clients would be told '{AdvertisedHost}', which they cannot dial. Set DFLegacy:GameplayDatagram:AdvertisedHost (DFLegacy__GameplayDatagram__AdvertisedHost) to the address or host name clients reach this server on.",
        options.GameplayDatagram.Host,
        advertisedGameplayHost);
}

// The entrance metadata tells the client which host to DIAL for the channel, and
// that field used to be fed straight from Channel.Host -- a bind address. With
// the shipped Channel.Host=0.0.0.0 the client dialled "0.0.0.0:7001", which fails
// with WSAEWOULDBLOCK and surfaces as a "connection failed" popup plus a crash
// (measured 2026-10-04 from the client net dump). The value is now resolved
// instead, so this reports what the client will actually be told and flags the
// case where that address is only reachable from this machine.
static void WarnAboutChannelDialHost(WebApplication app, ServerOptions options)
{
    var dialHost = EntranceService.ResolveChannelDialHost(options);
    var wildcardBind = options.Channel.Host is "0.0.0.0" or "::" or "[::]" or "*" or "+";
    if (wildcardBind)
    {
        app.Logger.LogInformation(
            "Channel.Host={BindHost} is a bind address, so the channel metadata tells clients to dial '{DialHost}' instead.",
            options.Channel.Host,
            dialHost);
    }

    var loopback = dialHost is "127.0.0.1" or "::1" or "localhost"
        || (System.Net.IPAddress.TryParse(dialHost, out var parsed)
            && System.Net.IPAddress.IsLoopback(parsed));
    if (loopback)
    {
        app.Logger.LogWarning(
            "Clients will be told to dial '{DialHost}' for the channel, which only works on this machine. Set DFLegacy:GameplayDatagram:AdvertisedHost to the address clients reach this server on.",
            dialHost);
    }
}

var itemCatalog = app.Services.GetRequiredService<ItemCatalog>();
itemCatalog.Initialize();
var gameStore = app.Services.GetRequiredService<JsonGameStore>();
await gameStore.InitializeAsync();
// 引导管理员：Admin.Users 名单里的账号在启动时提为 Admin（名单里还没注册的账号记警告）。
await gameStore.ApplyBootstrapAdminsAsync();
var compoundCatalog = app.Services.GetRequiredService<CompoundCatalog>();
compoundCatalog.Initialize();
var avatarCompoundCatalog =
    app.Services.GetRequiredService<AvatarCompoundCatalog>();
avatarCompoundCatalog.Initialize();
var reinforcementCatalog =
    app.Services.GetRequiredService<EquipmentReinforcementCatalog>();
reinforcementCatalog.Initialize();
var resealCatalog = app.Services.GetRequiredService<ResealCatalog>();
resealCatalog.Initialize();
var characterSpCatalog = app.Services.GetRequiredService<CharacterSpCatalog>();
characterSpCatalog.Initialize();
var pvpExperienceCatalog = app.Services.GetRequiredService<PvpExperienceCatalog>();
pvpExperienceCatalog.Initialize();
if (options.Pvp.Enabled) _ = app.Services.GetRequiredService<PvpMapCatalog>().Entries;
var normalizedPvpGrades = await gameStore.NormalizePvpGradesAsync(
    pvpExperienceCatalog);
if (normalizedPvpGrades != 0)
{
    app.Logger.LogInformation(
        "Normalized {CharacterCount} persisted PvP grade(s) from cumulative points.",
        normalizedPvpGrades);
}
var characterStatCatalog = app.Services.GetRequiredService<CharacterStatCatalog>();
characterStatCatalog.Initialize();
var creatureExperienceCatalog =
    app.Services.GetRequiredService<CreatureExperienceCatalog>();
creatureExperienceCatalog.Initialize();
var staminaRecoveryCatalog =
    app.Services.GetRequiredService<StaminaRecoveryCatalog>();
staminaRecoveryCatalog.Initialize();
var disjointCatalog = app.Services.GetRequiredService<DisjointCatalog>();
disjointCatalog.Initialize();
var premiumBenefitCatalog = app.Services.GetRequiredService<PremiumBenefitCatalog>();
premiumBenefitCatalog.Initialize();
var monsterDropCatalog = app.Services.GetRequiredService<MonsterDropCatalog>();
monsterDropCatalog.Initialize();
var monsterChampionDropCatalog =
    app.Services.GetRequiredService<MonsterChampionDropCatalog>();
monsterChampionDropCatalog.Initialize();
var hellDropCatalog = app.Services.GetRequiredService<HellDropCatalog>();
hellDropCatalog.Initialize();
var addCashArgumentIndex = Array.FindIndex(
    args,
    argument => string.Equals(
        argument,
        "--add-cash-all",
        StringComparison.OrdinalIgnoreCase));
if (addCashArgumentIndex >= 0)
{
    if (addCashArgumentIndex + 1 >= args.Length
        || !int.TryParse(args[addCashArgumentIndex + 1], out var addCashAmount)
        || addCashAmount <= 0)
    {
        app.Logger.LogError("--add-cash-all requires a positive integer amount.");
        Environment.ExitCode = 2;
        return;
    }

    var updatedCharacters = await gameStore.AddCashToAllCharactersAsync(addCashAmount);
    app.Logger.LogInformation(
        "Added {Amount} Cera to {CharacterCount} characters.",
        addCashAmount,
        updatedCharacters);
    return;
}

if (args.Contains("--reset-maximum-transfer-roster", StringComparer.OrdinalIgnoreCase))
{
    var replacement = await gameStore.ReplaceAllCharactersAsync(
        "test",
        MaximumTransferRoster.Create());
    if (!replacement.Replaced)
    {
        app.Logger.LogError(
            "Could not replace the character roster: {Error}",
            replacement.Error);
        Environment.ExitCode = 2;
    }
    else
    {
        app.Logger.LogInformation(
            "Created {Count} maximum-level transfer characters on account test.",
            replacement.CharacterCount);
    }

    return;
}

await gameStore.MigrateLegacyQuestRewardsAsync(
    app.Services.GetRequiredService<QuestCatalog>(),
    app.Services.GetRequiredService<CharacterExperienceCatalog>(),
    characterSpCatalog,
    itemCatalog,
    skillCatalog: app.Services.GetRequiredService<SkillCatalog>());

app.UseWebPanels(options);
app.UseRouting();
app.UseManagerControl(Environment.GetEnvironmentVariable(ManagerControl.TokenEnvironmentVariable));
app.UseStaticFiles();

// 8081 管理接口鉴权。刻意集中成"路由表 + 一个中间件"而不是给三十多条路由各挂过滤器：
// 集中判定漏一条一眼可见，逐路由挂则漏挂一处就是一个越权口子（判定表见 AdminAccessPolicy，
// 并有 smoke 测试逐条钉死）。静态页面和注册/找回在表里是 Public，无需登录。
app.Use(async (context, next) =>
{
    var decision = AdminAccessPolicy.Resolve(
        context.Request.Method,
        context.Request.Path.Value ?? "/",
        options.EnableTestCeraTopUp);
    if (decision.Access == AdminAccess.Public)
    {
        await next(context);
        return;
    }

    var auth = context.RequestServices.GetRequiredService<AdminAuthService>();
    var token = AdminAccessPolicy.BearerToken(context.Request.Headers.Authorization.ToString());
    var info = auth.Validate(token);
    if (info is null)
    {
        await WriteJsonErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "需要登录（token 缺失或已失效）。");
        return;
    }

    if (decision.Access == AdminAccess.Admin && info.Role != AccountRole.Admin)
    {
        await WriteJsonErrorAsync(context, StatusCodes.Status403Forbidden, "需要管理员权限。");
        return;
    }

    if (decision.Access == AdminAccess.SelfAccount
        && info.Role != AccountRole.Admin
        && !string.Equals(info.UserName, decision.AccountName, StringComparison.OrdinalIgnoreCase))
    {
        await WriteJsonErrorAsync(context, StatusCodes.Status403Forbidden, "只能操作自己的账号。");
        return;
    }

    context.Items[AdminAccessPolicy.TokenInfoKey] = info;
    await next(context);
});
app.MapGet("/client", () => Results.Redirect("/client/client_pay.htm"));
app.MapGet("/client/", () => Results.Redirect("/client/client_pay.htm"));
app.MapGet("/client/client_pay", () => Results.Redirect("/client/client_pay.htm"));
app.MapGet("/client/client_diamond", () => Results.Redirect("/client/client_diamond.htm"));
app.MapGet("/admin", () => Results.Redirect("/admin/index.html"));

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "dflegacy-emulator" }));

app.MapGet("/api/status", (RuntimeState runtime, ScriptFileSystem scripts, ItemCatalog items, ChannelLinkService channelLink, FallbackRelayService fallbackRelay) => Results.Ok(new
{
    runtime.StartedAt,
    runtime.TotalConnections,
    runtime.TotalPackets,
    ActiveSessions = runtime.Sessions().Length,
    Entrance = new[] { options.Entrance.Port }
        .Concat(options.Entrance.AdditionalPorts)
        .Distinct()
        .Select(port => $"{options.Entrance.Host}:{port}"),
    CharacterDatagram = options.CharacterDatagram.Enabled
        ? $"{options.CharacterDatagram.Host}:{options.CharacterDatagram.Port}/udp"
        : "disabled",
    GameplayDatagram = options.GameplayDatagram.Enabled
        ? new[]
        {
            $"{options.GameplayDatagram.Host}:{options.GameplayDatagram.PrimaryPort}/udp",
            $"{options.GameplayDatagram.Host}:{options.GameplayDatagram.SecondaryPort}/udp"
        }
        : [],
    GameProbe = options.GameProbe.Enabled ? $"{options.GameProbe.Host}:{options.GameProbe.Port}" : "disabled",
    FallbackRelay = fallbackRelay.Snapshot(),
    Random = GameRandomSource.Shared.GetStatus(),
    ChannelScriptConfigured = scripts.FileExists("etc/channel_info.etc"),
    CachedItems = items.Count,
    // I4：既有字段不动；新增 channelLink 让双侧存活与计数对 admin 可见。
    // 本地值（runtime.*）仍是原来的口径；合并值通过 ChannelLink 暴露（StatusProjection 收包口径）。
    ChannelLink = StatusProjection.ChannelLink(
        channelLink.Client?.Connected ?? false,
        channelLink.Client?.LastStatusAt,
        channelLink.Client?.LastRemoteStatus,
        runtime.TotalConnections,
        runtime.TotalPackets,
        runtime.Sessions().Length)
}));

app.MapGet("/api/sessions", (RuntimeState runtime) => Results.Ok(runtime.Sessions()));
app.MapGet("/api/packets", (RuntimeState runtime) => Results.Ok(runtime.RecentPackets()));
app.MapGet("/api/accounts", async (JsonGameStore store, CancellationToken token) =>
    Results.Ok(await store.GetAccountsAsync(token)));

app.MapGet("/api/admin/characters", async (
    JsonGameStore store,
    CharacterSessionRegistry sessions,
    TownSessionDirectory town,
    CancellationToken token) =>
{
    var accounts = await store.GetAccountsAsync(token);
    return Results.Ok(accounts
        .SelectMany(account => account.Characters.Select(character =>
        {
            var inTown = town.TryGetSnapshot(character.Id, out var snapshot);
            return new
            {
                character.Id,
                character.CharacterNo,
                character.Name,
                character.Job,
                character.GrowType,
                character.Level,
                account.UserName,
                account.AccountUid,
                Online = sessions.IsOnline(character.Id),
                TownId = inTown ? snapshot.TownId : (byte?)null,
                AreaId = inTown ? snapshot.AreaId : (byte?)null,
                AreaUserId = inTown ? snapshot.AreaUserId : (ushort?)null
            };
        }))
        .OrderByDescending(character => character.Online)
        .ThenBy(character => character.UserName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(character => character.CharacterNo));
});

app.MapGet("/api/admin/items", (
    string? query,
    AdminMailAttachmentKind? kind,
    int? limit,
    ItemCatalog items) =>
{
    if (kind.HasValue
        && (kind == AdminMailAttachmentKind.None || !Enum.IsDefined(kind.Value)))
    {
        return Results.BadRequest(new { error = "kind must be Item, Avatar or CreatureEgg." });
    }

    var normalizedQuery = query?.Trim() ?? string.Empty;
    var maximum = Math.Clamp(limit ?? 40, 1, 100);
    var result = items.Definitions.Values
        .Where(AdminMailComposer.IsSupportedAttachment)
        .Where(definition => !kind.HasValue
            || AdminMailComposer.GetAttachmentKind(definition) == kind.Value)
        .Where(definition => normalizedQuery.Length == 0
            || definition.Id.ToString().Contains(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase)
            || definition.Name.Contains(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase)
            || definition.ScriptPath.Contains(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(definition =>
            definition.Id.ToString() == normalizedQuery)
        .ThenBy(definition => definition.Id)
        .Take(maximum)
        .Select(definition => new
        {
            definition.Id,
            Name = string.IsNullOrWhiteSpace(definition.Name)
                ? $"Item {definition.Id}"
                : definition.Name,
            AttachmentKind = AdminMailComposer.GetAttachmentKind(definition),
            ScriptKind = definition.ScriptKind.ToString(),
            InventoryCategory = definition.InventoryCategory.ToString(),
            definition.TypeTag,
            AttachType = definition.AttachType.ToString(),
            definition.StackLimit,
            Durability = items.GetInitialDurability(definition.Id),
            definition.ScriptPath
        });
    return Results.Ok(result);
});

app.MapGet("/api/admin/dungeons", (DungeonCatalog dungeons) => Results.Ok(
    dungeons.DungeonIds.Select(dungeonId =>
    {
        dungeons.TryGetDefinition(dungeonId, out var definition);
        return new
        {
            DungeonId = dungeonId,
            definition.MinimumLevel,
            definition.BasisLevel,
            definition.IsHellDungeon,
            definition.GoldCardUse
        };
    })));

app.MapGet("/api/admin/message-types", () => Results.Ok(
    Enum.GetValues<GameMessageType>().Select(messageType => new
    {
        Value = (byte)messageType,
        Name = messageType.ToString()
    })));

app.MapPost("/api/admin/characters/{characterId:guid}/mail", async (
    Guid characterId,
    AdminSendMailRequest request,
    JsonGameStore store,
    ItemCatalog items,
    CharacterSessionRegistry sessions,
    CancellationToken token) =>
{
    if (!AdminMailComposer.TryCompose(request, items, out var mailRequest, out var error))
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        _ = clientTextEncoding.GetBytes(mailRequest.Sender);
        _ = clientTextEncoding.GetBytes(mailRequest.Text ?? string.Empty);
    }
    catch (EncoderFallbackException)
    {
        return Results.BadRequest(new
        {
            error = "Mail sender or text contains characters that cannot be encoded as CP936."
        });
    }

    var result = await store.SendCharacterMailAsync(characterId, mailRequest, token);
    if (!result.Created)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    var notifiedOnlineCharacter = sessions.NotifyMailReceived(characterId);
    return Results.Created(
        $"/api/characters/{characterId}/mail/{result.Mail!.Id}",
        new { result.Mail, NotifiedOnlineCharacter = notifiedOnlineCharacter });
});

app.MapPost("/api/admin/broadcast/message", (
    AdminBroadcastMessageRequest request,
    CharacterSessionRegistry sessions) =>
{
    if (string.IsNullOrEmpty(request.Message))
    {
        return Results.BadRequest(new { error = "Message must not be empty." });
    }

    if (!Enum.IsDefined(request.MessageType))
    {
        return Results.BadRequest(new { error = "MessageType is not supported." });
    }

    byte[] messageBytes;
    try
    {
        messageBytes = clientTextEncoding.GetBytes(request.Message);
    }
    catch (EncoderFallbackException)
    {
        return Results.BadRequest(new
        {
            error = "Message contains characters that cannot be encoded as CP936."
        });
    }

    if (messageBytes.Length >= 0x100)
    {
        return Results.BadRequest(new
        {
            error = "CP936-encoded message must be shorter than 256 bytes."
        });
    }

    var recipients = sessions.NotifyMessageAll(
        request.MessageType,
        request.TargetAreaUserId,
        messageBytes);
    return Results.Ok(new
    {
        queued = true,
        recipients,
        request.MessageType,
        request.TargetAreaUserId,
        encodedLength = messageBytes.Length
    });
});

app.MapPost("/api/admin/broadcast/popup", (
    AdminBroadcastPopupRequest request,
    CharacterSessionRegistry sessions) =>
{
    if (string.IsNullOrEmpty(request.Message))
    {
        return Results.BadRequest(new { error = "Message must not be empty." });
    }

    byte[] messageBytes;
    try
    {
        messageBytes = clientTextEncoding.GetBytes(request.Message);
    }
    catch (EncoderFallbackException)
    {
        return Results.BadRequest(new
        {
            error = "Message contains characters that cannot be encoded as CP936."
        });
    }

    if (messageBytes.Length >= 0x100)
    {
        return Results.BadRequest(new
        {
            error = "CP936-encoded message must be shorter than 256 bytes."
        });
    }

    var recipients = sessions.NotifyPopupAll(messageBytes);
    return Results.Ok(new
    {
        queued = true,
        recipients,
        encodedLength = messageBytes.Length
    });
});

app.MapPut("/api/admin/characters/{characterId:guid}/dungeons/{dungeonId:int}", async (
    Guid characterId,
    int dungeonId,
    AdminSetDungeonDifficultyRequest request,
    JsonGameStore store,
    DungeonCatalog dungeons,
    CharacterSessionRegistry sessions,
    CancellationToken token) =>
{
    if (dungeonId is <= 0 or > ushort.MaxValue
        || !dungeons.TryGetDefinition((ushort)dungeonId, out _))
    {
        return Results.BadRequest(new { error = "Dungeon id is not present in the loaded PVF." });
    }

    if (request.MaximumDifficulty > DungeonDifficultyProgression.HighestDifficulty)
    {
        return Results.BadRequest(new { error = "MaximumDifficulty must be between 0 and 3." });
    }

    var updated = await store.SetCharacterDungeonDifficultyAsync(
        characterId,
        (ushort)dungeonId,
        request.MaximumDifficulty,
        token);
    if (updated is null)
    {
        return Results.NotFound(new { error = "Character does not exist." });
    }

    var notifiedOnlineCharacter = sessions.NotifyDungeonPermission(
        characterId,
        (ushort)dungeonId,
        request.MaximumDifficulty);
    return Results.Ok(new
    {
        characterId,
        DungeonId = (ushort)dungeonId,
        request.MaximumDifficulty,
        NotifiedOnlineCharacter = notifiedOnlineCharacter
    });
});

// Test top-ups are explicitly opt-in; production payment mutations remain admin-only.
app.MapGet("/client/client-pay-settings.js", TestCeraEndpoint.Settings);
app.MapPost("/api/client-pay/test-cera", TestCeraEndpoint.HandleAsync);
app.MapPost("/api/client-pay/test-diamond", TestCeraEndpoint.HandleDiamondAsync);
app.MapPost("/api/client-pay/cera", async (
    ClientCeraTopUpRequest request,
    JsonGameStore store,
    CharacterSessionRegistry characterSessions,
    CancellationToken token) =>
{
    var result = await store.GrantCeraToAccountAsync(
        request.Account,
        request.Amount,
        token);
    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    var characters = await store.GetCharactersAsync(result.Account, token);
    foreach (var character in characters)
    {
        characterSessions.NotifyCera(
            character.Id,
            checked((uint)Math.Max(0, character.Cash)));
    }

    return Results.Ok(result);
});

app.MapPost("/api/client-pay/diamond", async (
    ClientDiamondGrantRequest request,
    JsonGameStore store,
    CharacterSessionRegistry characterSessions,
    CancellationToken token) =>
{
    var result = await store.GrantDiamondToAccountAsync(
        request.Account,
        request.Days,
        token);
    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    var characters = await store.GetCharactersAsync(result.Account, token);
    foreach (var character in characters)
    {
        characterSessions.NotifyPremiumInfo(
            character.Id,
            GameProtocolEngine.BlackDiamondServiceType,
            result.RemainingSeconds);
    }
    foreach (var characterId in result.WeaknessRecoveredCharacterIds ?? [])
    {
        characterSessions.NotifyWeaknessRecovery(
            characterId,
            DungeonWeaknessPolicy.FullStamina);
    }

    return Results.Ok(result);
});

app.MapPost("/api/client-pay/premium", async (
    ClientPremiumGrantRequest request,
    JsonGameStore store,
    CharacterSessionRegistry characterSessions,
    CancellationToken token) =>
{
    var result = await store.GrantPremiumServiceToAccountAsync(
        request.Account,
        request.ServiceType,
        request.Days,
        token);
    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    var characters = await store.GetCharactersAsync(result.Account, token);
    foreach (var character in characters)
    {
        characterSessions.NotifyPremiumInfo(
            character.Id,
            result.ServiceType,
            result.RemainingSeconds);
    }
    foreach (var characterId in result.WeaknessRecoveredCharacterIds ?? [])
    {
        characterSessions.NotifyWeaknessRecovery(
            characterId,
            DungeonWeaknessPolicy.FullStamina);
    }

    return Results.Ok(result);
});

// 登录换取 Bearer token。限流语义（安全复审后重设计）：
//   1) 来源级预算：同一 IP 的总尝试次数受限，挡单机扫号/枚举；
//   2) 账号级预算：同一账号的错误密码额度受限（跨来源），挡分布式定向爆破；
//   3) **凭据正确一律放行**并清零失败预算——否则匿名者连续失败 5 次就能把唯一管理员
//      永久锁在门外，那是比爆破更廉价的拒绝服务。
// 失败响应不区分"账号不存在/密码错误"，不给攻击者枚举账号的额外信息。
app.MapPost("/api/auth/login", async (
    AdminLoginRequest request,
    JsonGameStore store,
    AdminAuthService auth,
    HttpContext http,
    CancellationToken token) =>
{
    var userName = request.UserName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(request.Password))
    {
        return Results.BadRequest(new { error = "账号和密码不能为空。" });
    }

    var source = http.Connection.RemoteIpAddress?.ToString();
    // 两道预算都在校验凭据之前判定（命中即 429，不做凭据比较，这才真正拖慢爆破）：
    //   来源级 = 单机尝试额度；账号级 = 失败来自多个不同来源（分布式）才锁。
    if (auth.IsSourceThrottled(source))
    {
        return Results.Json(
            new { error = "尝试过于频繁，请稍后再试。" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    if (auth.IsAccountThrottled(userName))
    {
        return Results.Json(
            new { error = "该账号失败次数过多，暂时无法登录，请稍后再试。" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    auth.RegisterAttempt(source);

    var login = await store.AuthenticateWebAsync(userName, request.Password, auth, token);
    if (login is null)
    {
        auth.RegisterFailure(userName, source);
        return Results.Unauthorized();
    }

    // token 不进任何缓存（含中间代理）。
    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(login);
});

app.MapPost("/api/auth/logout", (AdminAuthService auth, HttpContext http) =>
{
    auth.Logout(AdminAccessPolicy.BearerToken(http.Request.Headers.Authorization.ToString()));
    return Results.Ok(new { success = true });
});

// 本人的角色列表：普通用户只能看自己（策略表里是 SelfAccount），管理员可查任意账号。
app.MapGet("/api/accounts/{userName}/characters", async (
    string userName,
    JsonGameStore store,
    CancellationToken token) =>
{
    var account = await store.GetAccountAsync(userName, token);
    return account is null
        ? Results.NotFound(new { error = "账号不存在。" })
        : Results.Ok(account);
});

app.MapGet("/api/admin/users", async (JsonGameStore store, CancellationToken token) =>
    Results.Ok(await store.GetAccountsAsync(token)));

app.MapPut("/api/admin/users/{userName}/password", async (
    string userName, AdminSetAccountPasswordRequest request, JsonGameStore store,
    AdminAuthService auth, HttpContext http, CancellationToken token) =>
    ModerationReply(await store.SetAccountPasswordByAdminAsync(userName, ModerationActor(http), request, auth, token)));

app.MapPut("/api/admin/users/{userName}/ban", async (
    string userName, AdminSetAccountBanRequest request, JsonGameStore store,
    AdminAuthService auth, CharacterSessionRegistry sessions, HttpContext http, CancellationToken token) =>
    ModerationReply(await store.SetAccountBanByAdminAsync(userName, ModerationActor(http), request, auth, sessions, token)));

app.MapDelete("/api/admin/users/{userName}/ban", async (
    string userName, JsonGameStore store, AdminAuthService auth, HttpContext http, CancellationToken token) =>
    ModerationReply(await store.ClearAccountBanByAdminAsync(userName, ModerationActor(http), auth, token)));

app.MapDelete("/api/admin/characters/{characterId:guid}", async (
    Guid characterId, JsonGameStore store, CharacterSessionRegistry sessions, HttpContext http, CancellationToken token) =>
    ModerationReply(await store.DeleteOfflineCharacterByAdminAsync(characterId, ModerationActor(http), sessions, token)));

static string ModerationActor(HttpContext http) =>
    ((AdminTokenInfo)http.Items[AdminAccessPolicy.TokenInfoKey]!).UserName;

static IResult ModerationReply(ModerationResult result) => result.Status switch
{
    ModerationStatus.Success => Results.Ok(new { success = true, result.RevokedTokens, result.Ban }),
    ModerationStatus.InvalidRequest => Results.BadRequest(new { error = result.Error }),
    ModerationStatus.NotFound => Results.NotFound(new { error = result.Error }),
    ModerationStatus.Conflict => Results.Conflict(new { error = result.Error }),
    _ => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable)
};

app.MapPut("/api/admin/users/{userName}/role", async (
    string userName,
    SetAccountRoleRequest request,
    JsonGameStore store,
    AdminAuthService auth,
    HttpContext http,
    CancellationToken token) =>
{
    var role = request.Role?.Trim().ToLowerInvariant() switch
    {
        "admin" => (AccountRole?)AccountRole.Admin,
        "player" => AccountRole.Player,
        _ => null
    };
    if (role is null)
    {
        return Results.BadRequest(new { error = "role 只能是 Admin 或 Player。" });
    }

    var actor = http.Items[AdminAccessPolicy.TokenInfoKey] as AdminTokenInfo;
    var result = await store.SetRoleAsync(userName, role.Value, actor?.UserName ?? string.Empty, token);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    // 角色变更立即作废该账号的旧 token：降级立刻生效（提权需重新登录，符合最小惊讶）。
    var revoked = auth.RevokeUser(userName);
    return Results.Ok(new
    {
        success = true,
        userName,
        role = role.Value.ToString(),
        revokedTokens = revoked
    });
});

app.MapPut("/api/admin/users/{userName}/kind", async (
    string userName, SetAccountKindRequest request, JsonGameStore store,
    RuntimeState runtime, AdminAuthService auth, CancellationToken token) =>
{
    var kind = request.Kind?.Trim().ToLowerInvariant() switch
    {
        "test" => (AccountKind?)AccountKind.Test,
        "normal" => AccountKind.Normal,
        _ => null
    };
    if (kind is null) return Results.BadRequest(new { error = "账号类型仅支持 Test 或 Normal。" });
    var result = await store.SetAccountKindAsync(userName, kind.Value, token,
        () => runtime.Sessions().Any(session => session.Service.StartsWith("entrance:", StringComparison.Ordinal)
            || session.Service.StartsWith("channel:", StringComparison.Ordinal)));
    if (!result.Updated) return Results.BadRequest(new { error = result.Error });
    var revoked = auth.RevokeUser(userName);
    return Results.Ok(new { success = true, userName = result.Account!.UserName,
        kind = result.Account.Kind.ToString(), accountUid = result.Account.AccountUid, revokedTokens = revoked });
});

app.MapPost("/api/accounts", async (CreateAccountRequest request, JsonGameStore store, CancellationToken token) =>
{
    var result = await store.CreateAccountAsync(request with { TestAccount = false }, token);
    if (result.Created)
    {
        return Results.Created($"/api/accounts/{result.Account!.UserName}", result.Account);
    }

    return string.Equals(result.Error, "账号已存在。", StringComparison.Ordinal)
        ? Results.Conflict(new { error = result.Error })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPut("/api/accounts/{userName}/password", async (
    string userName,
    ChangeAccountPasswordRequest request,
    JsonGameStore store,
    AdminAuthService auth,
    CancellationToken token) =>
{
    var result = await store.ChangePasswordAsync(userName, request, token);
    if (result.Success)
    {
        // 凭据变了，旧会话立即失效（安全复审 F3）：改密后必须重新登录。
        auth.RevokeUser(userName);
        return Results.Ok(result);
    }

    return result switch
    {
        { Failure: AccountPasswordUpdateFailure.InvalidCredentials } => Results.Unauthorized(),
        _ => Results.BadRequest(result)
    };
});

app.MapPost("/api/accounts/{userName}/password/reset", async (
    string userName,
    ResetAccountPasswordRequest request,
    JsonGameStore store,
    AdminAuthService auth,
    HttpContext http,
    CancellationToken token) =>
{
    // 找回密码是公开接口（忘记密码时无路可走更糟），但 QQ 码是可猜的短密钥，
    // 必须与登录共用同一套预算（安全复审 F1：否则等于一条无认证的账号接管通道）。
    var source = http.Connection.RemoteIpAddress?.ToString();
    if (auth.IsSourceThrottled(source) || auth.IsAccountThrottled(userName))
    {
        return Results.Json(
            new { error = "尝试过于频繁，请稍后再试。" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    auth.RegisterAttempt(source);
    var result = await store.ResetPasswordAsync(userName, request, token);
    if (result.Success)
    {
        auth.ClearFailures(userName);
        auth.RevokeUser(userName);
        return Results.Ok(result);
    }

    auth.RegisterFailure(userName, source);
    return result switch
    {
        { Failure: AccountPasswordUpdateFailure.InvalidRecoveryInformation } => Results.Unauthorized(),
        _ => Results.BadRequest(result)
    };
});

app.MapPost("/api/accounts/{userName}/characters", async (
    string userName,
    CreateCharacterRequest request,
    JsonGameStore store,
    CancellationToken token) =>
{
    var result = await store.CreateCharacterAsync(userName, request, token);
    return result.Created
        ? Results.Created($"/api/accounts/{userName}/characters/{result.Character!.Id}", result.Character)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPut("/api/characters/{characterId:guid}/fatigue", async (
    Guid characterId,
    SetCharacterFatigueRequest request,
    JsonGameStore store,
    CharacterSessionRegistry characterSessions,
    CancellationToken token) =>
{
    try
    {
        var character = await store.SetCharacterUsedFatigueAsync(
            characterId,
            request.UsedFatigue,
            token);
        if (character is null)
            return Results.NotFound();

        characterSessions.NotifyFatigueReset(characterId);
        return Results.Ok(character);
    }
    catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "usedFatigue")
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/characters/{characterId:guid}/mail", async (
    Guid characterId,
    JsonGameStore store,
    CancellationToken token) =>
    Results.Ok(await store.GetCharacterMailsAsync(characterId, token)));

app.MapPost("/api/characters/{characterId:guid}/mail", async (
    Guid characterId,
    CreateCharacterMailRequest request,
    JsonGameStore store,
    CharacterSessionRegistry sessions,
    CancellationToken token) =>
{
    var result = await store.SendCharacterMailAsync(characterId, request, token);
    if (!result.Created)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    sessions.NotifyMailReceived(characterId);
    return Results.Created(
        $"/api/characters/{characterId}/mail/{result.Mail!.Id}",
        result.Mail);
});

app.MapPost("/api/characters/{characterId:guid}/message", (
    Guid characterId,
    SendCharacterMessageRequest request,
    CharacterSessionRegistry sessions) =>
{
    if (string.IsNullOrEmpty(request.Message))
    {
        return Results.BadRequest(new { error = "Message must not be empty." });
    }

    if (!Enum.IsDefined(request.MessageType))
    {
        return Results.BadRequest(new
        {
            error = "MessageType must be a defined DFLegacy client message type."
        });
    }

    byte[] messageBytes;
    try
    {
        messageBytes = clientTextEncoding.GetBytes(request.Message);
    }
    catch (EncoderFallbackException)
    {
        return Results.BadRequest(new
        {
            error = "Message contains characters that cannot be encoded as CP936."
        });
    }

    if (messageBytes.Length >= 0x100)
    {
        return Results.BadRequest(new
        {
            error = "CP936-encoded message must be shorter than 256 bytes."
        });
    }

    if (!sessions.NotifyMessage(
            characterId,
            request.MessageType,
            request.TargetAreaUserId,
            messageBytes))
    {
        return Results.Conflict(new { error = "Character is not online." });
    }

    return Results.Ok(new
    {
        queued = true,
        characterId,
        request.MessageType,
        request.TargetAreaUserId,
        encodedLength = messageBytes.Length
    });
});

app.MapPost("/api/characters/{characterId:guid}/popup", (
    Guid characterId,
    SendCharacterPopupRequest request,
    CharacterSessionRegistry sessions) =>
{
    if (string.IsNullOrEmpty(request.Message))
    {
        return Results.BadRequest(new { error = "Message must not be empty." });
    }

    byte[] messageBytes;
    try
    {
        messageBytes = clientTextEncoding.GetBytes(request.Message);
    }
    catch (EncoderFallbackException)
    {
        return Results.BadRequest(new
        {
            error = "Message contains characters that cannot be encoded as CP936."
        });
    }

    if (messageBytes.Length >= 0x100)
    {
        return Results.BadRequest(new
        {
            error = "CP936-encoded message must be shorter than 256 bytes."
        });
    }

    if (!sessions.NotifyPopup(characterId, messageBytes))
    {
        return Results.Conflict(new { error = "Character is not online." });
    }

    return Results.Ok(new
    {
        queued = true,
        characterId,
        encodedLength = messageBytes.Length
    });
});

app.MapGet("/", () => Results.Text(
    "DFLegacy emulator is running. GET /api/status, /api/sessions, /api/packets, /api/accounts\n",
    "text/plain; charset=utf-8"));

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Admin API ready at http://{Host}:{Port}", options.Admin.Host, options.Admin.Port);
    app.Logger.LogInformation(
        "Admin API requires login; bootstrap admins: {Admins}. (Bearer token, {Hours}h sliding, {MaxHours}h max)",
        options.Admin.Users.Length == 0 ? "(none configured)" : string.Join(", ", options.Admin.Users),
        options.Admin.TokenHours,
        options.Admin.MaxTokenLifetimeHours);
    WarnAboutBootstrapAndExposure(app, options);
    WarnAboutChannelDialHost(app, options);
    var scripts = app.Services.GetRequiredService<ScriptFileSystem>();
    if (!scripts.FileExists("etc/channel_info.etc"))
    {
        app.Logger.LogWarning(
            "etc/channel_info.etc is missing from Script.pvf. Entrance handshake works, but protocol 9 download is disabled.");
    }
});

// 两条部署期最容易踩的安全坑，启动时说清楚（安全复审 F4/F9）：
// 1) 引导名单里的账号还没注册 + 管理端口对外 = 任何人抢注该名字后、下次重启就会被提升为管理员；
// 2) 管理端口绑在通配地址上而没做 TLS 反代 = 密码与 token 明文过网。
static void WarnAboutBootstrapAndExposure(WebApplication app, ServerOptions options)
{
    var wildcardHost = options.Admin.Host is "::" or "0.0.0.0" or "[::]" or "*" or "+";
    if (wildcardHost)
    {
        app.Logger.LogWarning(
            "Admin.Host={Host} exposes the admin API beyond this machine. " +
            "Restrict the source range with a firewall and terminate HTTPS in front of it " +
            "(passwords and bearer tokens are otherwise sent in clear text); " +
            "or set Admin.Host=127.0.0.1 and use an SSH tunnel.",
            options.Admin.Host);
    }

    if (options.Admin.Users.Length == 0)
    {
        app.Logger.LogWarning(
            "Admin.Users is empty: nobody can log into the admin API. " +
            "Set DFLegacy:Admin:Users (DFLegacy__Admin__Users__0) to the account name you will use.");
        return;
    }

    var store = app.Services.GetRequiredService<JsonGameStore>();
    var missing = options.Admin.Users
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Where(name => store.GetAccountAsync(name).GetAwaiter().GetResult() is null)
        .ToArray();
    if (missing.Length > 0)
    {
        app.Logger.LogWarning(
            "Admin.Users lists {Missing}, but those accounts do not exist yet. Promotion happens only " +
            "at startup, so register those names YOURSELF first, then restart — if the admin port is " +
            "reachable before you do, somebody else can register the name and be promoted at your next restart.",
            string.Join(", ", missing));
    }
}

await app.RunAsync();

static async Task WriteJsonErrorAsync(HttpContext context, int statusCode, string message)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsJsonAsync(new { error = message });
}
