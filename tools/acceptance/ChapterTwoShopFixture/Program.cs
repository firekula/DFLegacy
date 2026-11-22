using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

if (args.Length != 3) throw new ArgumentException("<stopped source state> <candidate server PVF> <new output directory>");
var source = Path.GetFullPath(args[0]);
var pvf = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory; no existing fixture may be overwritten.");
Directory.CreateDirectory(output);
var working = Path.Combine(output, "working-state.json");
File.Copy(source, working);
var sourceBytes = File.ReadAllBytes(source);
var original = JsonNode.Parse(sourceBytes)!;
const string account = "shopqa";
if (GameStoreAccountRows.Get(original).Any(a => (string?)a!["UserName"] == account)) throw new InvalidOperationException("Test account already exists");
var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
var options = new ServerOptions { DataPath = working, ScriptPvfPath = pvf, SeedTestAccount = false, SkipUnsupportedChapterTwoCashContainers = true };
var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
var catalog = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: catalog);
await store.InitializeAsync();
var made = await store.CreateAccountAsync(new(account, password, OicqCode: "112233"));
if (!made.Created) throw new InvalidOperationException(made.Error);
var tests = new (string Name, byte Town, byte Area, short X, short Y)[] {
    ("商店验收土",1,0,1435,273), ("商店验收二",2,0,335,284), ("商店验收三",3,0,2392,282),
    ("商店验收四",3,1,1246,288), ("商店验收五",4,0,2038,228),
    ("满格验收",1,0,1435,273), ("超重验收",1,0,1435,273), ("礼包验收",1,0,1435,273)
};
CharacterItemRecord Item(ushort id, ushort slot, uint count = 1) => CharacterItemIdentity.Ensure(new CharacterItemRecord(slot,id,count,
    Durability: catalog.GetInitialDurability(id), SealState: CharacterItemSealing.GetInitialSealState(catalog.Definitions[id])),catalog);
var freeEquipment = catalog.Definitions[26050]; // Real visible 1g title, not a hidden default-weapon placeholder.
if (!freeEquipment.IsTitle || freeEquipment.Weight is < 0 or > 100) throw new InvalidOperationException("Unexpected full-bag fixture item");
var heavyEquipment = catalog.Definitions.Values.Where(d => d.InventoryCategory == ItemInventoryCategory.Equipment && d.Weight is >= 4000 and <= 12000).OrderByDescending(d => d.Weight).First();
var records = new List<object>();
foreach (var test in tests)
{
    var result = await store.CreateCharacterAsync(account, new(test.Name, Job: 3, GrowType: 1, Level: 60));
    var character = result.Character ?? throw new InvalidOperationException(result.Error);
    var inventory = new List<CharacterItemRecord> { Item(1,1,50), Item(7138,41,5), Item(8,42,20), Item(3037,73,1000) };
    if (test.Name == "满格验收")
    {
        inventory = [Item(1,1,50), Item(7138,41,5)];
        for (ushort slot=3; slot<9; slot++) inventory.Add(Item(8,slot));
        for (ushort slot=9; slot<41; slot++) inventory.Add(Item(freeEquipment.Id,slot));
    }
    if (test.Name == "超重验收")
    {
        for (ushort slot=9; slot<25; slot++) inventory.Add(Item(heavyEquipment.Id,slot));
    }
    if (test.Name == "礼包验收")
    {
        inventory = [Item(1,1,50)];
        ushort slot=41;
        foreach (ushort id in new ushort[] {38,39,40,41,49,52,53,54,55,56}) inventory.Add(Item(id,slot++,2));
    }
    var saved = await store.SaveCharacterItemSpacesAsync(account,character.Id,1_000_000,inventory,character.Equipment??[],[],
        avatarInventory: [], creatureInventory: [], warehouseCapacity:120, expectedWarehouseCapacity:character.WarehouseCapacity);
    if(saved is null) throw new InvalidOperationException("Fixture save failed");
    saved=await store.SaveCharacterLocationAsync(account,character.Id,test.Town,test.Area,test.X,test.Y,5);
    if(saved is null) throw new InvalidOperationException("Fixture location failed");
    records.Add(new {saved.Id,saved.Name,saved.TownId,saved.AreaId,saved.X,saved.Y,
        Weight=CharacterInventoryPlanner.CalculateWeight(saved.Inventory??[],catalog),saved.Inventory});
}
var topup=await store.GrantCeraToAccountAsync(account,1_000_000);
if(!topup.Success) throw new InvalidOperationException(topup.Error);
var prepared = JsonNode.Parse(File.ReadAllBytes(working))!;
var qa = GameStoreAccountRows.Get(prepared).Single(a => (string?)a!["UserName"]==account)!.DeepClone();
// Established shop fixtures must enter their prepared town rather than the
// first-login tutorial, which overrides the normal location on the client.
foreach(var c in qa["Characters"]!.AsArray()) c!["TutorialStarted"]=true;
// Preserve original account records exactly. Initialization may normalize state;
// only the freshly created account and allocated ID counters enter the candidate.
GameStoreAccountRows.Get(original).Add(qa);
foreach(var key in new[]{"NextCharacterNo","NextNormalAccountUid","NextTestAccountUid"})
    original[key]=prepared[key]!.DeepClone();
var json = new JsonSerializerOptions {WriteIndented=true};
File.WriteAllText(Path.Combine(output,"prepared-state.json"),original.ToJsonString(json));
File.WriteAllText(Path.Combine(output,"qa-login.json"),JsonSerializer.Serialize(new {Account=account,Password=password},json));
File.WriteAllText(Path.Combine(output,"fixture.json"),JsonSerializer.Serialize(new {SourceSha256=Convert.ToHexString(SHA256.HashData(sourceBytes)),
    PvfSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pvf))),Account=account,Characters=records,
    FullBagItem=freeEquipment.Id,HeavyItem=heavyEquipment.Id,OriginalAccountsPreserved=true},json));
if(!File.ReadAllBytes(source).SequenceEqual(sourceBytes)) throw new IOException("Source changed while preparing");
Console.WriteLine($"Prepared {tests.Length} new isolated fixture characters; existing accounts preserved; no live installation.");
