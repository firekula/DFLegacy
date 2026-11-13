using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 3) throw new ArgumentException("<stopped source state> <server PVF> <new output directory>");
var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
Directory.CreateDirectory(output);
var originalBytes = File.ReadAllBytes(source);
var original = JsonNode.Parse(originalBytes)!;
var working = Path.Combine(output,"working-state.json");
File.WriteAllBytes(working,originalBytes);
var options = new ServerOptions { DataPath=working, ScriptPvfPath=Path.GetFullPath(args[1]), SeedTestAccount=false, SkipUnsupportedChapterTwoCashContainers=true };
var scripts = new ScriptFileSystem(options,NullLogger<ScriptFileSystem>.Instance);
var catalog = new ItemCatalog(scripts,NullLogger<ItemCatalog>.Instance,options);
var store = new JsonGameStore(options,NullLogger<JsonGameStore>.Instance,itemCatalog:catalog);
await store.InitializeAsync();
CharacterItemRecord Item(ushort id,ushort slot,uint count=1) => CharacterItemIdentity.Ensure(new CharacterItemRecord(slot,id,count,
    Durability:catalog.GetInitialDurability(id),SealState:CharacterItemSealing.GetInitialSealState(catalog.Definitions[id])),catalog);
var logins = new List<object>();
var records = new List<object>();
var accounts = new[] { "edgeqa", "peerqa" };
foreach (var account in accounts)
{
    if(original["Accounts"]!.AsArray().Any(a=>(string?)a!["UserName"]==account)) throw new InvalidOperationException("Fixture account already exists.");
    var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    var created=await store.CreateAccountAsync(new(account,password,OicqCode:"112233"));
    if(!created.Created) throw new InvalidOperationException(created.Error);
    logins.Add(new{Account=account,Password=password});
    var names=account=="edgeqa" ? new[]{"续测溢出","续测仓库","续测摆摊","续测队长"} : new[]{"续测收件","续测队员"};
    foreach(var name in names)
    {
        var result=await store.CreateCharacterAsync(account,new(name,Job:3,GrowType:1,Level:60));
        var c=result.Character??throw new InvalidOperationException(result.Error);
        var inventory=new List<CharacterItemRecord>{Item(1,1,50),Item(8,41,20),Item(3037,73,495),Item(27301,9)};
        var warehouse=new List<CharacterItemRecord>();
        var avatar=new List<CharacterItemRecord>();
        if(name=="续测溢出")
        {
            inventory.Add(Item(41,42,1));
            for(ushort slot=0;slot<100;slot++) avatar.Add(Item(39009,slot));
        }
        if(name=="续测仓库")
        {
            warehouse.Add(Item(3038,0,1000));
            warehouse.Add(Item(3037,1,1));
            warehouse.Add(Item(3037,3,1));
            warehouse.Add(Item(3037,5,1));
        }
        if(name=="续测摆摊")
        {
            inventory.RemoveAll(i=>i.Slot==41);
            for(ushort slot=41;slot<51;slot++) inventory.Add(Item(8,slot,(uint)(slot-40)));
        }
        c=await store.SaveCharacterItemSpacesAsync(account,c.Id,1_000_000,inventory,c.Equipment??[],warehouse,
            avatarInventory:avatar,creatureInventory:[],warehouseCapacity:120,expectedWarehouseCapacity:c.WarehouseCapacity)
            ??throw new InvalidOperationException("Fixture inventory save failed.");
        c=await store.SaveCharacterLocationAsync(account,c.Id,1,0,1435,273,5)??throw new InvalidOperationException("Fixture location save failed.");
        records.Add(new{Account=account,c.Id,Name=name,c.Inventory,c.Warehouse,AvatarCount=c.AvatarInventory?.Count,
            Weight=CharacterInventoryPlanner.CalculateWeight(c.Inventory??[],catalog)});
    }
    var topup=await store.GrantCeraToAccountAsync(account,1_000_000);
    if(!topup.Success) throw new InvalidOperationException(topup.Error);
}
var prepared=JsonNode.Parse(File.ReadAllBytes(working))!;
foreach(var account in accounts)
{
    var a=prepared["Accounts"]!.AsArray().Single(a=>(string?)a!["UserName"]==account)!.DeepClone();
    foreach(var c in a["Characters"]!.AsArray()) c!["TutorialStarted"]=true;
    original["Accounts"]!.AsArray().Add(a);
}
foreach(var key in new[]{"NextCharacterNo","NextNormalAccountUid","NextTestAccountUid"}) original[key]=prepared[key]!.DeepClone();
if(!File.ReadAllBytes(source).SequenceEqual(originalBytes)) throw new IOException("Source changed while preparing fixture.");
var json=new JsonSerializerOptions{WriteIndented=true};
File.WriteAllText(Path.Combine(output,"prepared-state.json"),original.ToJsonString(json));
File.WriteAllText(Path.Combine(output,"qa-logins.json"),JsonSerializer.Serialize(logins,json));
File.WriteAllText(Path.Combine(output,"fixture.json"),JsonSerializer.Serialize(new{SourceSha256=Convert.ToHexString(SHA256.HashData(originalBytes)),
    Accounts=accounts,Characters=records,OriginalAccountsPreserved=true},json));
Console.WriteLine($"Prepared {records.Count} new characters in two isolated accounts; original accounts retained exactly.");
