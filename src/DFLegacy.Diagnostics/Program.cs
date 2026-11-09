using DFLegacy.Diagnostics;

if(args.Length!=4||args[0]!="--session"||args[2]!="--capability")return 2;
try
{
    var session=DiagnosticSession.Open(args[1],args[3]);
    await DiagnosticHelper.RunAsync(session);
    return 0;
}
catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
{
    return 1;
}
