using DFLegacy.UpdateCore.Tests;

if (args is ["--chapter-transition"]) return ChapterTransitionTests.Run();
var result = await TestRun.RunAsync(args);
return result == 0 && args.Length == 0 ? ChapterTransitionTests.Run() : result;
