using DFLegacy.Server;
using DFLegacy.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class MigrationFixture
{
    internal static async Task<int> Verify(string prepared,string pvf,string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)) throw new IOException("Verification output already exists");
        Directory.CreateDirectory(output);
        var working=Path.Combine(output,"state.json"); File.Copy(prepared,working);
        var options=new ServerOptions{DataPath=working,ScriptPvfPath=Path.GetFullPath(pvf),SeedTestAccount=false,SkipUnsupportedChapterTwoCashContainers=true};
        var scripts=new ScriptFileSystem(options,NullLogger<ScriptFileSystem>.Instance);
        var items=new ItemCatalog(scripts,NullLogger<ItemCatalog>.Instance,options);
        var store=new JsonGameStore(options,NullLogger<JsonGameStore>.Instance,itemCatalog:items);
        var source=JsonNode.Parse(File.ReadAllText(prepared))!["Accounts"]!.Deserialize<AccountRecord[]>()!;
        var before=source.Single(a=>a.UserName=="migrationqa").Characters;
        var checks=new List<object>(); var failures=0;
        void Check(bool value,string name) {checks.Add(new {Passed=value,Name=name});Console.WriteLine($"{(value?"PASS":"FAIL")} {name}");if(!value)failures++;}
        await store.InitializeAsync();
        foreach(var original in before)
        {
            var loaded=(await store.GetCharacterAsync("migrationqa",original.Id))!;
            Check(loaded.WarehouseCapacity==120,$"{original.Name}: maximum warehouse unlocked");
            Check(JsonSerializer.Serialize(loaded)==JsonSerializer.Serialize(original),$"{original.Name}: C2 store startup retains complete character");
            var main=CharacterInventoryPlanner.Normalize(loaded.Inventory??[],items);
            var pets=CharacterCreatureInventoryPlanner.Normalize(loaded.CreatureInventory??[],items);
            var bank=CharacterInventoryPlanner.NormalizeWarehouse(loaded.Warehouse??[],items,120);
            Check(main.OverflowItems.Count==0&&pets.OverflowItems.Count==0&&bank.OverflowItems.Count==0,$"{original.Name}: no normalization overflow");
            Check((loaded.Inventory??[]).OrderBy(x=>x.Slot).SequenceEqual(main.Inventory.Values.OrderBy(x=>x.Slot)),$"{original.Name}: full main bag and disabled items preserve every field");
            Check((loaded.CreatureInventory??[]).OrderBy(x=>x.Slot).SequenceEqual(pets.Inventory.Values.OrderBy(x=>x.Slot)),$"{original.Name}: every pet UUID/UID/name/level/experience/hunger preserved");
            Check((loaded.Warehouse??[]).OrderBy(x=>x.Slot).SequenceEqual(bank.Warehouse.Values.OrderBy(x=>x.Slot)),$"{original.Name}: full warehouse every item field preserved");
            await store.SaveCharacterItemSpacesAsync("migrationqa",loaded.Id,loaded.Gold,main.Inventory.Values,loaded.Equipment??[],bank.Warehouse.Values,
                avatarInventory:loaded.AvatarInventory??[],creatureInventory:pets.Inventory.Values);
        }
        store=new(options,NullLogger<JsonGameStore>.Instance,itemCatalog:items); await store.InitializeAsync();
        foreach(var original in before)
        {
            var loaded=(await store.GetCharacterAsync("migrationqa",original.Id))!;
            Check((loaded.CreatureInventory??[]).SequenceEqual(original.CreatureInventory??[]),$"{original.Name}: pets preserved after save/reload");
            Check((loaded.Warehouse??[]).SequenceEqual(original.Warehouse??[]),$"{original.Name}: warehouse preserved after save/reload");
            Check((loaded.Mailbox??[]).SequenceEqual(original.Mailbox??[]),$"{original.Name}: mail attachments and stored states preserved after save/reload");
        }
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Failures=failures,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Checks {checks.Count}; failures {failures}"); return failures==0?0:1;
    }

    internal static async Task Prepare(string output,string sourcePath,string c1Path,ItemCatalog c1,ItemCatalog c2,ItemDefinition[] pets,ItemDefinition[] titles,ItemDefinition[] excluded)
    {
        var path=Path.Combine(output,"prepared-c1-state.json");
        File.Copy(sourcePath,path);
        var options=new ServerOptions {DataPath=path,ScriptPvfPath=c1Path,SeedTestAccount=false};
        var store=new JsonGameStore(options,NullLogger<JsonGameStore>.Instance,itemCatalog:c1);
        await store.InitializeAsync();
        const string account="migrationqa";
        const string password="C2MigrateLocal"; // Isolated fixture account; launcher accepts 6-16 alphanumeric characters.
        var created=await store.CreateAccountAsync(new(account,password,OicqCode:"112233"));
        if(!created.Created) throw new InvalidOperationException(created.Error);
        var serial=10000u;
        CharacterItemRecord Item(ItemDefinition d,ushort slot)
        {
            var pet=CharacterCreatureInventoryLayout.IsCreature(d);
            return new(slot,d.Id,pet?++serial:1,Durability:c1.GetInitialDurability(d.Id),SealState:CharacterItemSealing.GetInitialSealState(d),
                CreatureStomach:pet?(byte)73:null,CreatureStomachRemainderSeconds:pet?(byte)17:null,
                CreatureExperience:pet?1234u:null,CreatureLevel:pet?(byte)12:null,
                CreatureName:pet?$"迁移{d.Id}":null,CreatureNoCharge:pet?true:null,InstanceId:Guid.NewGuid());
        }
        var generated=new List<Guid>();
        var all=pets.Concat(titles).ToArray();
        var now=(uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for(var batch=0;batch<(titles.Length+31)/32;batch++)
        {
            var result=await store.CreateCharacterAsync(account,new($"迁移满仓{batch+1}",Job:3,GrowType:1,Level:60));
            var c=result.Character ?? throw new InvalidOperationException(result.Error);
            generated.Add(c.Id);
            // Legal native spaces: 32 equipment slots, 140 creature slots, 120 warehouse slots.
            var bag=Enumerable.Range(0,32).Select(i=>Item(titles[(batch*32+i)%titles.Length],(ushort)(9+i))).ToList();
            bag.Add(new(1,1,20));
            var creatures=Enumerable.Range(0,140).Select(i=>Item(pets[i%pets.Length],(ushort)i)).ToList();
            var warehouse=Enumerable.Range(0,120).Select(i=>Item(all[(batch*120+i)%all.Length],(ushort)i)).ToList();
            var mails=Enumerable.Range(0,255).Select(i=>{
                var d=all[i%all.Length];
                return new CharacterMailRecord((uint)i+1,"迁移测试","第一章目录压力样本",0,
                    new(d.Id,1,Durability:c1.GetInitialDurability(d.Id),SealState:CharacterItemSealing.GetInitialSealState(d),InstanceId:Guid.NewGuid()),
                    now,now+30*86400,State:i%2==0?(ushort)1:(ushort)3);
            }).ToList();
            // This is fixture construction, not a fabricated purchase record. C1 mail attachments
            // have no pet-level fields; ordinary purchased eggs receive state when claimed.
            Replace(path,c with {Inventory=bag,CreatureInventory=creatures,Warehouse=warehouse,Mailbox=mails,WarehouseCapacity=120});
            store=new(options,NullLogger<JsonGameStore>.Instance,itemCatalog:c1);
            await store.InitializeAsync();
        }
        var deleted=(await store.CreateCharacterAsync(account,new("迁移禁用物",Job:3,GrowType:1,Level:60))).Character!;
        generated.Add(deleted.Id);
        var retired=excluded.Select((d,i)=>Item(d,(ushort)(41+i)) with {CountOrValue=(uint)(i+2)}).ToList();
        Replace(path,deleted with {Inventory=retired,Warehouse=retired.Select((x,i)=>x with {Slot=(ushort)i,InstanceId=Guid.NewGuid()}).ToList(),
            Mailbox=retired.Select((x,i)=>new CharacterMailRecord((uint)i+1,"迁移测试","第二章禁用内容",0,new(x.ItemId,x.CountOrValue,InstanceId:Guid.NewGuid()),now,now+30*86400)).ToList(),WarehouseCapacity=120});
        store=new(options,NullLogger<JsonGameStore>.Instance,itemCatalog:c1);
        await store.InitializeAsync();
        var buyer=(await store.CreateCharacterAsync(account,new("迁移购买方",Job:3,GrowType:1,Level:60))).Character!;
        var recipient=(await store.CreateCharacterAsync(account,new("迁移购宠",Job:3,GrowType:1,Level:60))).Character!;
        generated.AddRange([buyer.Id,recipient.Id]);
        await store.GrantCeraToAccountAsync(account,1_000_000);
        var scripts=new ScriptFileSystem(options,NullLogger<ScriptFileSystem>.Instance);
        var shop=new CeraShopCatalog(scripts,c1,NullLogger<CeraShopCatalog>.Instance);
        var eligible=shop.Products.Where(p=>p.Section=="creature")
            .Where(p=>shop.TryResolveCreatureEggGift(new(255,0,p.CommodityNo),out _,out _)).OrderBy(p=>p.CommodityNo).ToArray();
        var receipts=new List<object>();
        // The source catalog limits received gifts per month. One paid sample is
        // sufficient to cover actual purchase provenance; do not bypass that limit.
        foreach(var p in eligible.Where(p=>p.CommodityNo==105009).Take(1))
        {
            var result=await store.GiftCeraShopAsync(account,buyer.Id,recipient.Name,[new(255,0,p.CommodityNo)],shop);
            receipts.Add(new {p.CommodityNo,p.ItemId,p.CashPrice,result.Status,result.Error,result.SenderCash});
            if(result.Status!=DungeonRewardLedgerStatus.Committed) throw new InvalidOperationException($"C1 gift purchase failed: {p.CommodityNo}/{result.Error}");
        }
        var final=JsonNode.Parse(File.ReadAllText(path))!;
        foreach(var a in final["Accounts"]!.AsArray()) foreach(var c in a!["Characters"]!.AsArray()) c!["WarehouseCapacity"]=120;
        // These are established level-60 migration fixtures, not new-player tutorial tests.
        foreach(var c in final["Accounts"]!.AsArray().Single(a=>a!["UserName"]!.GetValue<string>()==account)!["Characters"]!.AsArray())
            c!["TutorialStarted"]=true;
        File.WriteAllText(path,final.ToJsonString(new(){WriteIndented=true}));
        var roster=final["Accounts"]!.AsArray().Single(x=>x!["UserName"]!.GetValue<string>()==account)!["Characters"]!.Deserialize<CharacterRecord[]>()!;
        var coverage=new {Account=account,Characters=roster.Select(c=>new {c.Id,c.Name,c.WarehouseCapacity,
            InventoryCount=c.Inventory?.Count,CreatureCount=c.CreatureInventory?.Count,WarehouseCount=c.Warehouse?.Count,MailCount=c.Mailbox?.Count}),
            UniqueTitlesInBags=roster.SelectMany(c=>c.Inventory??[]).Select(x=>x.ItemId).Intersect(titles.Select(x=>x.Id)).Count(),
            UniquePetsInBags=roster.SelectMany(c=>c.CreatureInventory??[]).Select(x=>x.ItemId).Intersect(pets.Select(x=>x.Id)).Count(),
            UniqueInWarehouse=roster.SelectMany(c=>c.Warehouse??[]).Select(x=>x.ItemId).Intersect(all.Select(x=>x.Id)).Count(),
            UniqueInMail=roster.SelectMany(c=>c.Mailbox??[]).Select(x=>x.Attachment?.ItemId??0).Intersect(all.Select(x=>x.Id)).Count(),
            ExpectedPets=pets.Length,ExpectedTitles=titles.Length,ExpectedCombined=all.Length,PurchasedEggs=receipts.Count};
        File.WriteAllText(Path.Combine(output,"fixture-coverage.json"),JsonSerializer.Serialize(coverage,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"c1-purchase-receipts.json"),JsonSerializer.Serialize(receipts,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"fixture-login.txt"),$"Isolated generated account only\n{account}\n{password}\n");
        Console.WriteLine($"Prepared C1 fixture: {roster.Length} characters, {pets.Length} pets, {titles.Length} titles, {receipts.Count} real catalog purchases.");
    }

    private static void Replace(string path,CharacterRecord replacement)
    {
        var root=JsonNode.Parse(File.ReadAllText(path))!;
        foreach(var a in root["Accounts"]!.AsArray())
        {
            var characters=a!["Characters"]!.AsArray();
            for(var i=0;i<characters.Count;i++) if(characters[i]!["Id"]!.GetValue<Guid>()==replacement.Id)
                characters[i]=JsonSerializer.SerializeToNode(replacement);
        }
        File.WriteAllText(path,root.ToJsonString(new(){WriteIndented=true}));
    }
}
