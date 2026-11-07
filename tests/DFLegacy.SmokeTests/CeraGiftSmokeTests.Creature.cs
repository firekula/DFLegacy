using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class CeraGiftSmokeTests
{
    private sealed class EggFixture(int? max = null, Action<string>? mutate = null) : IAsyncDisposable
    {
        internal readonly CreatureGiftFixture Catalog = new(max, mutate);
        internal readonly Clock Clock = new();
        internal ServerOptions Options => Catalog.Options;
        internal ItemCatalog Items => Catalog.Items;
        internal CeraShopCatalog Shop => Catalog.Shop;
        internal JsonGameStore Open(IDungeonRewardSnapshotWriter? writer = null) => writer is null
            ? new(Options, NullLogger<JsonGameStore>.Instance, itemCatalog: Items, avatarTimeProvider: Clock)
            : new(Options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: Items, avatarTimeProvider: Clock);
        internal async Task<(CharacterRecord A, CharacterRecord B)> Seed(JsonGameStore store)
        {
            await store.CreateAccountAsync(new("sender", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("target", "password", OicqCode: "123456"));
            var roster = ((await store.CreateCharacterAsync("sender", new("Buyer", Level: 1))).Character!,
                (await store.CreateCharacterAsync("target", new("收礼", Job: 1, Level: 1))).Character!);
            // Gift transactions need their own funded test wallets.
            await store.GrantCeraToAccountAsync("sender", 50_000);
            await store.GrantCeraToAccountAsync("target", 50_000);
            return ((await store.GetCharacterAsync("sender", roster.Item1.Id))!,
                (await store.GetCharacterAsync("target", roster.Item2.Id))!);
        }
        internal Task<CeraGiftResult> Gift(JsonGameStore store, CharacterRecord sender, string recipient, uint commodity = 105009) =>
            store.GiftCeraShopAsync("sender", sender.Id, recipient, [new(0xFF, 0, commodity)], Shop);
        public ValueTask DisposeAsync() { Catalog.Dispose(); return ValueTask.CompletedTask; }
    }

    private static Task CreatureCatalogAndWire(Action<bool, string> check)
    {
        using var f = new CreatureGiftFixture();
        check(CreatureGiftFixture.VerifyProvenance(), "creature fixtures preserve all30 raw CP936 input/output PVF entries and original hashes");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(CreatureGiftFixture.Source, "manifest.json")))!;
        foreach (var row in manifest["eggs"]!.AsArray())
        {
            var commodity = row!["commodity"]!.GetValue<uint>();
            check(f.Shop.TryResolveCreatureEggGift(new(255, 0, commodity), out var p, out var d)
                && p.ItemId == row["item"]!.GetValue<ushort>() && p.Quantity == 1 && p.AttributeValue == 0
                && p.CashPrice == row["price"]!.GetValue<int>() && p.ClientCategory == 3
                && d.CreatureOutputIndex == row["output"]!.GetValue<int>(), $"actual permanent sealed egg{commodity} resolves exact price/item/output");
        }
        check(f.Items.TryGetDefinition(63007, out var charp) && charp.Name == "宠物蛋 (恰克斯)", "actual CP936 Charp name independently matches Chinese text");
        check(f.Items.TryGetDefinition(24, out var food) && food.InventoryCategory == ItemInventoryCategory.Creature
            && f.Items.TryGetDefinition(25, out var rename) && rename.InventoryCategory == ItemInventoryCategory.Creature,
            "excluded food and rename are defined creature-category stackables, not missing-item shortcuts");
        check(f.Items.TryGetDefinition(195, out var package) && package.CeraPackage is not null
            && f.Items.TryGetDefinition(196, out var booster) && booster.CeraBooster is not null
            && f.Items.TryGetDefinition(197, out var lottery) && lottery.Lottery is not null
            && f.Shop.TryGetProduct(305190, out var direct) && direct.DirectCurrencyGrant is not null,
            "excluded synthetic container/lottery/direct-effect products have real parsed payloads");
        foreach (var request in new GameCeraShopPurchaseItem[] { new(3,0,105009), new(0,0,105009), new(255,1,105009), new(255,2,105009), new(255,3,105009), new(255,0,999999), new(255,0,105001), new(255,0,105002), new(255,0,105003), new(255,0,105004), new(255,0,105005), new(255,0,305190), new(255,0,305195), new(255,0,305196), new(255,0,305197) })
            check(!f.Shop.TryResolveCreatureEggGift(request, out _, out _), $"egg rejects native selection or excluded commodity{request}");
        foreach (var tag in new[] { "usable period", "expiration date", "usable period after unsealing" })
        {
            check(CreatureEggGiftLifetime.IsPermanent($"[{tag}] 0") && CreatureEggGiftLifetime.IsPermanent($"[{tag}]\n0"), $"egg lifetime {tag} accepts explicit zero inline/next line");
            foreach (var value in new[] { "1", "-1", "1790000000", "bad", "", "0 junk", "0 1", "`0`", "0 `junk`", "0.0" })
                check(!CreatureEggGiftLifetime.IsPermanent($"[{tag}] {value}"), $"egg lifetime {tag} fails closed for '{value}'");
            check(!CreatureEggGiftLifetime.IsPermanent($"[{tag}]0\n[{tag}]1"), $"egg duplicate {tag} cannot hide finite lifetime");
        }
        check(CreatureEggGiftLifetime.IsPermanent("// [usable period] 7\n[name] `// [expiration date] 9`\n/* [usable period after unsealing] 5 */\n[usable period] 0"), "comments and quoted tag names cannot make permanent egg finite");
        foreach (var quantity in new[] { "0", "2", "bad", "-1" })
        {
            using var bad = new CreatureGiftFixture(mutate: root => File.AppendAllText(Path.Combine(root,"etc/newcashshop.etc"), $"\n[creature]\n105009 63007 {quantity} 0 0 6600 `Raw boundary` 0 0\n[/creature]\n"));
            check(bad.Shop.TryGetProduct(105009, out var normalized) && normalized.Quantity == (quantity == "2" ? 2u : 1u)
                && !bad.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), $"raw egg quantity{quantity} denied while preserving generic/selfbuy quantity normalization");
        }
        // Each variant alters the real input script, never a fake ItemDefinition.
        var original = File.ReadAllBytes(Path.Combine(CreatureGiftFixture.Source,"equipment/creature/egg_charp.equ"));
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(936);
        var text = encoding.GetString(original);
        foreach (var variant in new[] {
            text + "\n[usable period] 1", text + "\n[expiration date] bad", text + "\n[usable period after unsealing] 2",
            text.Replace("[sub type] 1", "[sub type] 0"),
            text.Replace("[sub type] 1", "[sub type] 2"),
            text.Replace("[sealing]", "[free]"),
            text.Replace("63003", "63999"), text.Replace("63003", "63007"), text.Replace("[creature]", "[artifact red]") })
        {
            using var bad = new CreatureGiftFixture(mutate: root => File.WriteAllBytes(Path.Combine(root,"equipment/creature/egg_charp.equ"), encoding.GetBytes(variant)));
            check(bad.Shop.TryGetProduct(105009, out var candidate) && candidate.AttributeValue == 0
                && !bad.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), "actual egg metadata boundary denies finite/malformed/subtype/nonsealed/output/type variant");
        }
        using (var finalValid = new CreatureGiftFixture(mutate: root => File.AppendAllText(Path.Combine(root,"etc/newcashshop.etc"),
            "\n[creature]\n105009 63007 0 0 0 6600 `Invalid first` 0 0\n105009 63007 1 0 0 6600 `Valid final` 0 0\n[/creature]\n")))
            check(finalValid.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), "final successful duplicate row replaces raw egg quantity eligibility in both directions");
        foreach (var suffix in new[] { "[creature]\n105009 63007 1 0 0 6600 `Sticker` 99 0", "[gift disallowance]\n105009" })
        {
            using var bad = new CreatureGiftFixture(mutate: root => File.AppendAllText(Path.Combine(root,"etc/newcashshop.etc"),"\n"+suffix+"\n"));
            check(!bad.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), "egg rejects giftDisallowance/sticker99");
        }
        using (var missing = new CreatureGiftFixture(mutate: root => File.Delete(Path.Combine(root,"equipment/creature/egg_charp.equ"))))
            check(!missing.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), "egg missing script never defaults to permanent");
        using (var unreadable = new CreatureGiftFixture())
        {
            _ = unreadable.Shop.Count; // Load canonical definition before denying the lifetime source read.
            using var locked = new FileStream(Path.Combine(unreadable.Root, "equipment/creature/egg_charp.equ"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            check(!unreadable.Shop.TryResolveCreatureEggGift(new(255,0,105009),out _,out _), "unreadable lifetime source is denied even with a cached canonical egg definition");
        }
        byte[] requestBytes = [0x12,0,1,4,0,0,0,0xCA,0xD5,0xC0,0xF1,1,0xFF,0,0,0x31,0x9A,1,0];
        check(GameProtocolEngine.TryReadCeraShopPurchase(requestBytes,out var purchase) && purchase.IsGift && purchase.RecipientName == "收礼"
            && purchase.Products.SequenceEqual(new[] {new GameCeraShopPurchaseItem(255,0,105009)}), "literal ACT1 gift egg request FF0000319A0100 decodes CP936 receiver and exact selection");
        byte[] reply = [1,1,3,0,0,0,0x31,0x9A,1,0,0x31,0x9A,1,0,0x31,0x9A,1,0,255,255,0,0,255,255,0,0,0,0];
        check(GameProtocolEngine.CreateCeraShopBuyReply(3,105009,isGift:true).Payload.SequenceEqual(reply), "egg gift success literal category3/progress keys/empty buyer rewards");
        return Task.CompletedTask;
    }

    private static async Task CreatureGiftTransactions(Action<bool,string> check)
    {
        await using var f = new EggFixture(); var store=f.Open(); var (a,b)=await f.Seed(store);
        var result=await f.Gift(store,a,b.Name);
        var sender=await Character(store,"sender",a.Id); var target=await Character(store,"target",b.Id);
        var attachment=target.Mailbox?.SingleOrDefault()?.Attachment;
        check(result.Status==DungeonRewardLedgerStatus.Committed && result.SenderCash==43400 && sender.Cash==43400
            && (sender.CreatureInventory??[]).SequenceEqual(a.CreatureInventory??[]) && (sender.Inventory??[]).SequenceEqual(a.Inventory??[])
            && (sender.AvatarInventory??[]).SequenceEqual(a.AvatarInventory??[]) && (sender.Equipment??[]).SequenceEqual(a.Equipment??[])
            && (sender.Warehouse??[]).SequenceEqual(a.Warehouse??[]) && result.Products is { Count: 1 }
            && result.Products[0].CommodityNo == 105009 && result.Products[0].ClientCategory == 3 && attachment is {ItemId:63007,CountOrValue:1,State:0,Durability:0,SealState:1}
            && attachment.InstanceId!=Guid.Empty && attachment.AvatarRemainingSeconds is null && attachment.AvatarAbilityIndex is null
            && attachment.AvatarExpiresAtUnixSeconds is null && attachment.EquipmentQualitySeed is null,
            "egg atomic gift debits6600, creates fresh sealed UUID mail, gives sender no item or avatar metadata");
        if (attachment is not null)
        {
            await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, b.Inventory??[], b.Equipment??[], b.Warehouse??[], creatureInventory:b.CreatureInventory??[]);
            check((await store.GetCharacterMailsAsync(b.Id)).Single().Attachment?.InstanceId == attachment.InstanceId,
                "stale pre-gift target actor save preserves canonical egg mail UUID");
        }
        await using var same = new EggFixture(); var ss=same.Open();var(sa,_)=await same.Seed(ss);
        var alt=(await ss.CreateCharacterAsync("sender",new("SameAlt",Level:1))).Character!;
        check((await same.Gift(ss,sa,alt.Name)).Status==DungeonRewardLedgerStatus.Committed
            && (await Character(ss,"sender",alt.Id)).Cash==43400 && (await ss.GetCharacterMailsAsync(alt.Id)).Single().Attachment?.ItemId==63007
            && (await ss.QueryCeraGiftRecipientAsync(sa.Id,alt.Name)).ReceivedCount == 1,
            "same-account low-level offline egg target retains canonical mailbox and shared cash");
        foreach(var commodity in new uint[]{105001,105005,999999})
        {
            await using var bad=new EggFixture(max:2);var bs=bad.Open();var(ba,bb)=await bad.Seed(bs);
            var before=await File.ReadAllBytesAsync(bad.Options.DataPath);
            var r=await bs.GiftCeraShopAsync("sender",ba.Id,bb.Name,[new(255,0,105009),new(255,0,commodity)],bad.Shop);
            var after=await File.ReadAllBytesAsync(bad.Options.DataPath);
            check(r.Status==DungeonRewardLedgerStatus.Rejected && r.Error==(commodity==999999?21:8)
                && before.SequenceEqual(after), $"mixed egg and invalid{commodity} rolls back entire cash/mail/quota snapshot");
        }
    }
    // Reflection uses the exact planner invoked by the native mail command, not a substitute placement path.
    private static bool PlanEggMail(CharacterRecord target, CharacterMailAttachmentRecord attachment, ItemCatalog items,
        out Dictionary<ushort,CharacterItemRecord> main, out Dictionary<ushort,CharacterItemRecord> avatar,
        out Dictionary<ushort,CharacterItemRecord> creatures, out byte? list, out ushort? slot)
    {
        object?[] args = [ (target.Inventory??[]).ToDictionary(i=>i.Slot), (target.AvatarInventory??[]).ToDictionary(i=>i.Slot),
            (target.CreatureInventory??[]).ToDictionary(i=>i.Slot), attachment, items, uint.MaxValue, null, null, null, null, null, null ];
        var ok=(bool)typeof(EntranceService).GetMethod("TryPlanMailAttachment",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
        main=(Dictionary<ushort,CharacterItemRecord>)args[6]!;
        avatar=(Dictionary<ushort,CharacterItemRecord>)args[7]!;
        creatures=(Dictionary<ushort,CharacterItemRecord>)args[8]!;list=(byte?)args[9];slot=(ushort?)args[10];return ok;
    }
    private static bool FreshEgg(CharacterItemRecord item) => item is {ItemId:63007,State:0,Durability:0,SealState:1,
        CreatureStomach:100,CreatureStomachRemainderSeconds:0,CreatureExperience:0,CreatureLevel:1,
        CreatureName:"宠物蛋 (恰克斯)",CreatureNoCharge:false} && item.CountOrValue!=0 && item.InstanceId!=Guid.Empty;

    private static async Task CreatureGiftLifecycle(Action<bool,string> check)
    {
        await using(var f=new EggFixture(max:2))
        {
            var store=f.Open();var(a,b)=await f.Seed(store);
            var old=new CharacterItemRecord(0,63003,1,CreatureStomach:73,CreatureStomachRemainderSeconds:11,
                CreatureExperience:19,CreatureLevel:2,CreatureName:"Existing",CreatureNoCharge:true,InstanceId:Guid.NewGuid());
            await store.SaveCharacterItemSpacesAsync("target",b.Id,b.Gold,b.Inventory??[],b.Equipment??[],b.Warehouse??[],creatureInventory:[old]);
            var r1=await f.Gift(store,a,b.Name);var r2=await f.Gift(store,a,b.Name);
            check(r1.Status==DungeonRewardLedgerStatus.Committed && r2.Status==DungeonRewardLedgerStatus.Committed,"two egg gifts commit as two ordinary quota units");
            if(r1.Status!=DungeonRewardLedgerStatus.Committed||r2.Status!=DungeonRewardLedgerStatus.Committed)return;
            var target=await Character(store,"target",b.Id);var mails=target.Mailbox!.ToArray();
            var ids=mails.Select(m=>m.Attachment!.InstanceId).ToArray();
            check(ids.Distinct().Count()==2 && ids.All(id=>id!=Guid.Empty)
                && mails.All(m=>m.Attachment is {ItemId:63007,CountOrValue:1,SealState:1,Durability:0}),
                "two unclaimed sealed egg mails have distinct committed UUIDs, count1 is quantity");
            // Reload actual serialized state before either claim: waiting in mail never hatches/changes identity.
            store=f.Open();await store.InitializeAsync();target=await Character(store,"target",b.Id);
            check(target.Mailbox!.Select(m=>m.Attachment!.InstanceId).SequenceEqual(ids)
                && target.CreatureInventory!.Single()==old,"unclaimed restart preserves both UUIDs and existing creature mutable state");
            foreach(var mail in mails)
            {
                var ok=PlanEggMail(target,mail.Attachment!,f.Items,out var main,out var avatars,out var pets,out var list,out var slot);
                check(ok && list==7 && slot.HasValue && main.Values.SequenceEqual(target.Inventory??[])
                    && avatars.Count==(target.AvatarInventory?.Count??0),"real mail planner routes egg only to native creature list7");
                if(!ok)continue;
                var egg=pets.Values.Single(i=>i.InstanceId==mail.Attachment!.InstanceId);
                check(FreshEgg(egg) && egg.CountOrValue!=1 && pets[0]==old
                    && pets.Values.Select(i=>i.CountOrValue).Distinct().Count()==pets.Count,"egg claim preserves UUID, allocates collision-free nonzero wire UID, leaves UID1 creature intact");
                var claim=await store.ClaimCharacterMailAsync("target",b.Id,mail.Id,target.Gold,main.Values,target.Equipment??[],
                    avatarInventory:avatars.Values,creatureInventory:pets.Values);
                check(claim is not null,"real store commits planned egg claim");
                var repeat=await store.ClaimCharacterMailAsync("target",b.Id,mail.Id,target.Gold,main.Values,target.Equipment??[],creatureInventory:pets.Values);
                check(repeat is null,"repeated same egg mail claim cannot duplicate instance");
                store=f.Open();await store.InitializeAsync();target=await Character(store,"target",b.Id);
                check(target.CreatureInventory!.Single(i=>i.InstanceId==egg.InstanceId)==egg,"claimed egg restart preserves UUID, UID and every fresh default");
                var wire=CharacterItemWireProjection.Project(egg,f.Items);
                var packet=GameProtocolEngine.CreateItemList(7,[new(egg.Slot,egg.ItemId,wire.AddInfo,wire.ItemAttr,wire.Durability,wire.SealState)]);
                check(packet.Payload[0]==7 && packet.Payload[1]==1 && BitConverter.ToUInt32(packet.Payload,7)==egg.CountOrValue
                    && packet.Payload[14]==1,"real native ITEM_LIST serializes egg list7, allocated UID and seal1");
            }
            var chosen=target.CreatureInventory!.Single(i=>i.InstanceId==ids[0]);
            var hatchOk=CharacterCreatureInventoryPlanner.TryHatch(target.CreatureInventory!.ToDictionary(i=>i.Slot),chosen.Slot,f.Items,out var hatch,out _);
            check(hatchOk,"received sealed egg enters existing hatch path");
            if(hatchOk)
            {
                var hatched=hatch.Inventory[chosen.Slot];
                check(hatched.ItemId==63003 && hatched.InstanceId==chosen.InstanceId && hatched.CountOrValue==chosen.CountOrValue
                    && hatched is {SealState:0,Durability:0,CreatureStomach:100,CreatureExperience:0,CreatureLevel:1,CreatureNoCharge:false}
                    && f.Items.TryGetDefinition(63003,out var output) && hatched.CreatureName==output.Name,"hatch output63003 retains committed UUID/wire UID and applies existing output defaults");
                await store.SaveCharacterItemSpacesAsync("target",b.Id,target.Gold,target.Inventory??[],target.Equipment??[],target.Warehouse??[],creatureInventory:hatch.Inventory.Values);
                store=f.Open();await store.InitializeAsync();target=await Character(store,"target",b.Id);
                check(target.CreatureInventory!.Single(i=>i.InstanceId==chosen.InstanceId)==hatched
                    && target.CreatureInventory!.Single(i=>i.InstanceId==ids[1]).ItemId==63007,"hatched restart preserves identity without hatching another gift egg");
            }
        }
        await CreatureGiftCapacityAndExpiry(check);
    }

    private static async Task CreatureGiftCapacityAndExpiry(Action<bool,string> check)
    {
        await using(var f=new EggFixture())
        {
            var store=f.Open();var(a,b)=await f.Seed(store);
            var full=Enumerable.Range(0,140).Select(i=>new CharacterItemRecord((ushort)i,63003,(uint)i+1,
                CreatureStomach:100,CreatureLevel:1,InstanceId:Guid.NewGuid())).ToArray();
            await store.SaveCharacterItemSpacesAsync("target",b.Id,b.Gold,b.Inventory??[],b.Equipment??[],b.Warehouse??[],creatureInventory:full);
            check((await f.Gift(store,a,b.Name)).Status==DungeonRewardLedgerStatus.Committed,"full140 creature bag does not prevent egg mail gift");
            var target=await Character(store,"target",b.Id);var mail=target.Mailbox!.Single();
            var before=await File.ReadAllBytesAsync(f.Options.DataPath);
            check(!PlanEggMail(target,mail.Attachment!,f.Items,out _,out _,out _,out _,out _),"real mail planner refuses full creature bag");
            var after=await File.ReadAllBytesAsync(f.Options.DataPath);
            check(before.SequenceEqual(after) && (await store.GetCharacterMailsAsync(b.Id)).Single().Attachment?.InstanceId==mail.Attachment!.InstanceId
                && await store.GetAccountCeraAsync("sender")==43400,"failed full-bag planning consumes neither gift mail UUID nor additional cash");
            await store.SaveCharacterItemSpacesAsync("target",b.Id,target.Gold,target.Inventory??[],target.Equipment??[],target.Warehouse??[],creatureInventory:full.Skip(1));
            target=await Character(store,"target",b.Id);
            var ok=PlanEggMail(target,mail.Attachment!,f.Items,out var main,out var avatar,out var pets,out _,out _);
            check(ok && (await store.ClaimCharacterMailAsync("target",b.Id,mail.Id,target.Gold,main.Values,target.Equipment??[],avatarInventory:avatar.Values,creatureInventory:pets.Values)) is not null
                && pets.Values.Single(i=>i.InstanceId==mail.Attachment!.InstanceId).Slot==0,"freeing one slot permits original egg mail claim with original UUID");
        }
        await using(var f=new EggFixture())
        {
            var store=f.Open();var(a,b)=await f.Seed(store);
            for(var i=0;i<255;i++)await store.SendCharacterMailAsync(b.Id,new("Old","Full"));
            var before=await File.ReadAllBytesAsync(f.Options.DataPath);var r=await f.Gift(store,a,b.Name);var after=await File.ReadAllBytesAsync(f.Options.DataPath);
            check(r.Status==DungeonRewardLedgerStatus.Rejected && r.Error==4 && before.SequenceEqual(after),"full255 valid mailbox rejects egg gift without cash/quota mutation");
        }
        await using(var f=new EggFixture())
        {
            var store=f.Open();var(a,b)=await f.Seed(store);await f.Gift(store,a,b.Name);
            var target=await Character(store,"target",b.Id);var mail=target.Mailbox!.Single();
            var available=typeof(JsonGameStore).GetMethod("IsMailAvailable",BindingFlags.NonPublic|BindingFlags.Static)!;
            bool At(uint now)=>(bool)available.Invoke(null,[mail,now])!;
            check(mail.ExpiresAt-mail.SentAt==2592000 && At(mail.SentAt+2591999) && !At(mail.SentAt+2592000) && !At(mail.SentAt+2592001),
                "permanent egg gift mail lasts30days; exact expiry equality and following second are unavailable");
            var state=JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            var expired=state["Accounts"]![1]!["Characters"]![0]!["Mailbox"]![0]!;
            expired["SentAt"]=1;expired["ExpiresAt"]=2;
            await File.WriteAllTextAsync(f.Options.DataPath,state.ToJsonString());
            store=f.Open();await store.InitializeAsync();
            check((await store.ClaimCharacterMailAsync("target",b.Id,mail.Id,target.Gold,target.Inventory??[],target.Equipment??[],
                creatureInventory:[new(0,63007,1,InstanceId:mail.Attachment!.InstanceId)])) is null
                && ((await Character(store,"target",b.Id)).CreatureInventory?.Count??0)==0,"real restarted store refuses expired permanent-egg mail without granting a creature");
        }
    }

    private static async Task CreatureGiftAtomicRegression(Action<bool,string> check)
    {
        var kinds=new GameCeraShopPurchaseItem[]{new(255,0,100018),new(4,1,270000),new(255,0,105009)};
        foreach(var first in kinds)
        foreach(var second in kinds.Where(k=>k.CommodityNo!=first.CommodityNo))
        {
            await using var f=new EggFixture();var store=f.Open();var(a,b)=await f.Seed(store);
            var one=await store.GiftCeraShopAsync("sender",a.Id,b.Name,[first],f.Shop);
            var before=await File.ReadAllBytesAsync(f.Options.DataPath);
            var two=await store.GiftCeraShopAsync("sender",a.Id,b.Name,[second],f.Shop);var after=await File.ReadAllBytesAsync(f.Options.DataPath);
            check(one.Status==DungeonRewardLedgerStatus.Committed && two.Status==DungeonRewardLedgerStatus.Rejected && two.Error==4 && before.SequenceEqual(after),
                $"shared cap1: gift{first.CommodityNo} blocks later kind{second.CommodityNo} atomically");
        }
        await using(var f=new EggFixture())
        {
            var store=f.Open();var(a,b)=await f.Seed(store);
            var results=await Task.WhenAll(f.Gift(store,a,b.Name),store.GiftCeraShopAsync("sender",a.Id,b.Name,[new(4,1,270000)],f.Shop));
            var mails=await store.GetCharacterMailsAsync(b.Id);
            var price=mails.Single().Attachment!.ItemId==63007?6600:300;
            check(results.Count(r=>r.Status==DungeonRewardLedgerStatus.Committed)==1 && results.Any(r=>r.Error==4)
                && mails.Length==1 && await store.GetAccountCeraAsync("sender")==50000-price,"egg/avatar concurrent final receiver quota commits exactly one mailbox/debit/count");
        }
        await using(var f=new EggFixture(max:2))
        {
            var store=f.Open();var(a,b)=await f.Seed(store);
            await store.CreateAccountAsync(new("other","password",OicqCode:"123456"));var other=(await store.CreateCharacterAsync("other",new("OtherTarget"))).Character!;
            await store.SaveCharacterCeraShopPurchaseAsync("sender",a.Id,6600,a.Gold,a.Inventory??[],a.Equipment??[]);
            var results=await Task.WhenAll(f.Gift(store,a,b.Name),f.Gift(store,a,other.Name));
            check(results.Count(r=>r.Status==DungeonRewardLedgerStatus.Committed)==1 && results.Any(r=>r.Error==11)
                && await store.GetAccountCeraAsync("sender")==0
                && (await store.GetCharacterMailsAsync(b.Id)).Length+(await store.GetCharacterMailsAsync(other.Id)).Length==1,"two egg sender sessions cannot spend canonical6600 twice across recipient accounts");
        }
        foreach(var afterReplace in new[]{false,true})
        {
            await using var f=new EggFixture();var writer=new FaultWriter(afterReplace);var store=f.Open(writer);var(a,b)=await f.Seed(store);writer.Armed=true;
            var result=await f.Gift(store,a,b.Name);var calls=writer.Calls;var retry=await f.Gift(store,a,b.Name);
            check(result.Status==DungeonRewardLedgerStatus.Unknown && retry.Status==DungeonRewardLedgerStatus.Unknown && writer.Calls==calls
                && (await Character(store,"sender",a.Id)).Cash==50000,"egg snapshot uncertainty quarantines and prevents retry/speculative actor publication");
            store=f.Open();await store.InitializeAsync();var target=await Character(store,"target",b.Id);var attachment=target.Mailbox?.SingleOrDefault()?.Attachment;
            check(await store.GetAccountCeraAsync("sender")== (afterReplace?43400:50000) && (target.Mailbox?.Count??0)==(afterReplace?1:0)
                && (await store.QueryCeraGiftRecipientAsync(a.Id,b.Name)).ReceivedCount==(afterReplace?1:0)
                && (!afterReplace || attachment is {ItemId:63007,CountOrValue:1,SealState:1,Durability:0} && attachment.InstanceId!=Guid.Empty),
                $"egg fault restart afterReplace={afterReplace} observes complete cash/mail/quota/UUID or none");
        }
        await CreatureGiftSessionsAndEconomicState(check);
    }
    private static async Task CreatureGiftSessionsAndEconomicState(Action<bool, string> check)
    {
        await using var f = new EggFixture(); var store = f.Open(); var (a, b) = await f.Seed(store);
        var alt = (await store.CreateCharacterAsync("sender", new("SameAccountOnline"))).Character!;
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var now = DateTimeOffset.UtcNow; var pickup = Guid.NewGuid(); var match = Guid.NewGuid();
        state["DungeonRewardLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonRewardLedgerEntry(
            "egg-prior-card", "prior-fingerprint", a.Id, 12, 0, 1, 1, DungeonRewardLedgerStatus.Committed,
            now, now, null, 17, null, null) });
        state["DungeonGoldLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldLedgerEntry(pickup, [a.Id], [0], [19], [19]) });
        state["PvpLedger"] = JsonSerializer.SerializeToNode(new[] { new PvpSettlementReceipt(match, "prior-pvp",
            [new(a.Id, 37, 12, 0, 1, 0, 1, 1, 12)]) });
        var sender = state["Accounts"]![0]!["Characters"]![0]!;
        sender["VictoryPoints"] = 37; sender["PvpPoints"] = 12; sender["PvpWins"] = 1;
        sender["PendingDungeonGoldCredits"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldCredit(pickup, 19) });
        sender["MentorRegistration"] = JsonSerializer.SerializeToNode(new MentorRegistrationState(1790000000, 0));
        var apprentice = state["Accounts"]![1]!["Characters"]![0]!;
        apprentice["MentorCharacterId"] = JsonSerializer.SerializeToNode(a.Id);
        apprentice["MentorRelationshipExperience"] = 77;
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        // Compare normalized canonical state; initialization owns existing legacy normalization.
        state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var ledgerBefore = new[] { "DungeonRewardLedger", "DungeonGoldLedger", "PvpLedger" }
            .ToDictionary(k => k, k => state[k]!.ToJsonString());
        var actorBefore = await Character(store, "sender", a.Id);
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var cashA = new CeraCashInvalidation(); var cashAlt = new CeraCashInvalidation(); var alarms = 0;
        var leaseA = await sessions.ClaimAsync(a.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashA.Invalidate);
        var leaseAlt = await sessions.ClaimAsync(alt.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashAlt.Invalidate);
        var leaseB = await sessions.ClaimAsync(b.Id, Guid.NewGuid(), () => { }, default, mailReceived: () => alarms++);
        try
        {
            await store.GrantCeraToAccountAsync("sender", 1000);
            var result = await f.Gift(store, a, b.Name);
            await CeraGiftSessionEffects.NotifyCommittedAsync(result, "sender", store, sessions);
            sessions.NotifyCera(a.Id, 51000); sessions.NotifyCera(alt.Id, 51000);
            check(await cashA.ConsumeAsync(store, "sender") == 44400
                && await cashAlt.ConsumeAsync(store, "sender") == 44400 && alarms == 1
                && (await Character(store, "sender", a.Id)).CreatureInventory?.Count is null or 0,
                "egg gift sends target only mail invalidation, no buyer item, and late recharge notice rereads canonical44400 for both sender sessions");
            await CeraGiftSessionEffects.NotifyCommittedAsync(new(DungeonRewardLedgerStatus.Unknown), "sender", store, sessions);
            await CeraGiftSessionEffects.NotifyCommittedAsync(new(DungeonRewardLedgerStatus.Rejected), "sender", store, sessions);
            check(alarms == 1 && await cashAlt.ConsumeAsync(store, "sender") is null,
                "egg Unknown/rejected result publishes no success cash or mail notification");
            var actorAfter = await Character(store, "sender", a.Id);
            var apprenticeAfter = await Character(store, "target", b.Id);
            state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            check(ledgerBefore.All(pair => state[pair.Key]!.ToJsonString() == pair.Value)
                && actorAfter.VictoryPoints == 37 && actorAfter.PvpPoints == 12 && actorAfter.PvpWins == 1
                && actorAfter.PendingDungeonGoldCredits!.Single().PickupId == pickup
                && actorAfter.MentorRegistration == actorBefore.MentorRegistration
                && apprenticeAfter.MentorCharacterId == a.Id && apprenticeAfter.MentorRelationshipExperience == 77,
                "egg gift snapshot preserves PvP receipt/VP/results, gold credit, mentor registration and prior economic ledgers");
        }
        finally { sessions.Release(leaseA); sessions.Release(leaseAlt); sessions.Release(leaseB); }
    }
}
