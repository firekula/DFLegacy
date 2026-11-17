using DFLegacy.UpdateCore.Tests;

if (args is ["--chapter-transition"]) return ChapterTransitionTests.Run();
if (args is ["--engine"])
{
    var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-engine-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try { await EngineTests.Run(scratch); Console.WriteLine("Engine: OK"); return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    finally { Directory.Delete(scratch, true); }
}
var result = await TestRun.RunAsync(args);
return result == 0 && args.Length == 0 ? ChapterTransitionTests.Run() : result;
