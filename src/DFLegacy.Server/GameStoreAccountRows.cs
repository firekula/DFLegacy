using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.Server;

// Fixture tools edit the array in place and preserve all unrelated save fields.
public static class GameStoreAccountRows
{
    public static JsonArray Get(JsonNode state) => state["Accounts"] switch
    {
        JsonArray rows => rows,
        JsonObject envelope when envelope["Version"]?.GetValue<int>() == 2
            && envelope["Records"] is JsonArray rows => rows,
        _ => throw new JsonException("Unsupported account list in game store snapshot.")
    };
}
