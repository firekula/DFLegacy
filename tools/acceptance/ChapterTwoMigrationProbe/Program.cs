using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args is ["--all-jars", var allJarsSource, var allJarsTarget, var allJarsOutput])
    return MigratedJarAudit.Run(allJarsSource, allJarsTarget, allJarsOutput, allJars: true);

if (args is ["--jars", var jarsSource, var jarsTarget, var jarsOutput])
    return MigratedJarAudit.Run(jarsSource, jarsTarget, jarsOutput);

if(args is ["--verify",var preparedState,var targetPvf,var verificationOutput])
    return await MigrationFixture.Verify(preparedState,targetPvf,verificationOutput);

if (args is ["--containers", var sourceContainersPvf, var targetContainersPvf, var containersOutput])
    return CeraContainerAudit.Run(sourceContainersPvf, targetContainersPvf, containersOutput);

if (args is ["--skill-grants", var grantsPvf, var jobText, var growText, var awakeningText])
{
    var scripts = new ScriptFileSystem(new ServerOptions { ScriptPvfPath = Path.GetFullPath(grantsPvf) }, NullLogger<ScriptFileSystem>.Instance);
    var skills = new SkillCatalog(scripts, NullLogger<SkillCatalog>.Instance);
    Console.WriteLine(JsonSerializer.Serialize(skills.GetGrantedSkills(
        int.Parse(jobText), int.Parse(growText), int.Parse(awakeningText)), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: ChapterTwoMigrationProbe <C1 pvf> <C2 pvf> <source state> <new output directory>");
    return 2;
}
var c1Path = Path.GetFullPath(args[0]);
var c2Path = Path.GetFullPath(args[1]);
var sourcePath = Path.GetFullPath(args[2]);
var output = Path.GetFullPath(args[3]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory; original evidence is never overwritten.");
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, jsonOptions));
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
var inputs = new[] {c1Path, c2Path, sourcePath}.Select(p => new {Path=p, Sha256=Hash(p)}).ToArray();
File.Copy(sourcePath, Path.Combine(output, "source-state.original.json"));
Write("inputs.json", inputs);
ItemCatalog Catalog(string p, bool c2) => new(new ScriptFileSystem(new ServerOptions {ScriptPvfPath=p}, NullLogger<ScriptFileSystem>.Instance), NullLogger<ItemCatalog>.Instance, new ServerOptions {SkipUnsupportedChapterTwoCashContainers=c2});
var c1 = Catalog(c1Path, false);
var c2 = Catalog(c2Path, true);
var c2Scripts = new ScriptFileSystem(new ServerOptions {ScriptPvfPath=c2Path}, NullLogger<ScriptFileSystem>.Instance);
var c2Indexed = new HashSet<ushort>();
foreach(var family in new[]{"equipment","stackable"})
    foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(c2Scripts.ReadAllText($"{family}/{family}.lst"),@"(?m)^\s*(\d+)\s+`([^`]+)`"))
        if(ushort.TryParse(match.Groups[1].Value,out var id)) c2Indexed.Add(id);
var items = c1.Definitions.Values.OrderBy(x=>x.Id).ToArray();
var pets = items.Where(CharacterCreatureInventoryLayout.IsCreature).ToArray();
var titles = items.Where(x=>x.IsTitle).ToArray();
object Describe(ItemDefinition d) => new {d.Id,d.Name,d.ScriptPath,d.ScriptKind,d.TypeTag,d.InventoryCategory,d.CreatureSubType,d.CreatureOutputIndex,d.CreatureSpecies,d.StackLimit,d.AttachType};
var differences = items.Select(d => {
    c2.TryGetDefinition(d.Id,out var next);
    var sameIdentity = next is not null && d.ScriptPath.Equals(next.ScriptPath,StringComparison.OrdinalIgnoreCase)
        && d.ScriptKind == next.ScriptKind && d.TypeTag == next.TypeTag
        && (!CharacterCreatureInventoryLayout.IsCreature(d) || (d.CreatureSubType == next.CreatureSubType && d.CreatureOutputIndex == next.CreatureOutputIndex && d.CreatureSpecies == next.CreatureSpecies));
    return new {d.Id,Family=CharacterCreatureInventoryLayout.IsCreature(d)?"pet":d.IsTitle?"title":"other",
        Status=next is null?(c2Indexed.Contains(d.Id)?"disabled-by-c2-policy":"absent"):sameIdentity?"same-identity":"changed-identity",
        C1=Describe(d),C2=next is null?null:Describe(next)};
}).ToArray();
Write("catalog-differences.json", differences);
Write("c1-pets.json", pets);
Write("c1-titles.json", titles);
var petChecks = pets.Select((d,i)=> {
    var original = new CharacterItemRecord(0,d.Id,(uint)(7000+i),SealState:d.CreatureSubType==1?(byte)1:(byte)0,
        CreatureStomach:73,CreatureExperience:1234,CreatureLevel:12,CreatureName:$"迁移{i+1}",
        CreatureNoCharge:true,CreatureStomachRemainderSeconds:17,InstanceId:Guid.NewGuid());
    var normalized = CharacterCreatureInventoryPlanner.Normalize([original],c2);
    return new {d.Id,d.Name,Original=original,Result=normalized.Inventory.Values.ToArray(),normalized.OverflowItems,
        ExactPreservation=normalized.Inventory.Count==1 && normalized.Inventory.Values.Single()==original};
}).ToArray();
Write("pet-normalization.json", petChecks);
var source = JsonNode.Parse(File.ReadAllText(sourcePath))!;
var accounts = source is JsonArray a ? a : source["Accounts"]!.AsArray();
var sourceCharacters = accounts.SelectMany(a=>a!["Characters"]!.AsArray()).ToArray();
var held = new List<object>();
foreach(var character in sourceCharacters)
foreach(var space in new[]{"Inventory","Equipment","Warehouse","AvatarInventory","CreatureInventory","Mailbox"})
foreach(var node in character![space]?.AsArray() ?? [])
{
    var entry = space=="Mailbox" ? node?["Attachment"] : node;
    if(entry is null) continue;
    var id=entry["ItemId"]!.GetValue<ushort>();
    var delta=differences.FirstOrDefault(x=>x.Id==id);
    held.Add(new {Character=character["Id"]!.GetValue<Guid>(),Space=space,ItemId=id,
        Status=delta?.Status ?? (c2.Contains(id)?"not-in-source-catalog":"absent-in-both"),Entry=entry.DeepClone()});
}
Write("source-holdings.json",held);
var statePath=Path.Combine(output,"roundtrip-state.json");
File.Copy(sourcePath,statePath);
var opts=new ServerOptions {DataPath=statePath,ScriptPvfPath=c2Path,SeedTestAccount=false,SkipUnsupportedChapterTwoCashContainers=true};
var store=new JsonGameStore(opts,NullLogger<JsonGameStore>.Instance,itemCatalog:c2);
await store.InitializeAsync();
// Force a normal save using an existing character location; no item edits here.
var first=sourceCharacters.FirstOrDefault();
if(first is not null)
{
    var saved=await store.GetCharacterByNameAsync(first["Name"]!.GetValue<string>());
    var accountName=accounts.First(a=>a!["Characters"]!.AsArray().Any(c=>c!["Id"]!.GetValue<Guid>()==first["Id"]!.GetValue<Guid>()))!["UserName"]!.GetValue<string>();
    if(saved is not null) await store.SaveCharacterLocationAsync(accountName,saved.Id,saved.TownId,saved.AreaId,saved.X,saved.Y,saved.Direction);
}
Write("summary.json",new {C1Count=c1.Count,C2Count=c2.Count,Pets=pets.Length,Titles=titles.Length,
    Missing=differences.Count(x=>x.Status=="absent"),ChangedIdentity=differences.Count(x=>x.Status=="changed-identity"),
    Disabled=differences.Count(x=>x.Status=="disabled-by-c2-policy"),
    MissingPets=differences.Where(x=>x.Family=="pet"&&x.Status=="absent").Select(x=>x.Id),
    MissingTitles=differences.Where(x=>x.Family=="title"&&x.Status=="absent").Select(x=>x.Id),
    PetExactPreservation=petChecks.Count(x=>x.ExactPreservation),SourceAccounts=accounts.Count,SourceCharacters=sourceCharacters.Length,
    SourceUnchanged=inputs.All(x=>Hash(x.Path)==x.Sha256)});
Console.WriteLine(File.ReadAllText(Path.Combine(output,"summary.json")));
await MigrationFixture.Prepare(output, sourcePath, c1Path, c1, c2, pets, titles, items.Where(d=>!c2.Contains(d.Id)).ToArray());
return inputs.All(x=>Hash(x.Path)==x.Sha256)?0:1;
