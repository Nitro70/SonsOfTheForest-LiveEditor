using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveEditor.Net;

public readonly record struct IncomingRequest(long Id, string Cmd, JsonElement? Args);

/// <summary>
/// NDJSON wire format per PLAN.md section 4.3. One JSON object per line, no
/// embedded newlines (guaranteed by JSON string escaping).
/// </summary>
public static class Envelope
{
    public static IncomingRequest? TryParseRequest(string line)
    {
        try
        {
            // Defensive: tolerate a stray leading UTF-8 BOM char. System.Text.Json's
            // parser rejects U+FEFF as leading "whitespace", and plenty of JSON
            // writers in other languages emit one on the first line of a stream.
            line = line.TrimStart((char)0xFEFF);
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("id", out var idEl)) return null;
            if (!root.TryGetProperty("cmd", out var cmdEl)) return null;

            var cmd = cmdEl.GetString();
            if (string.IsNullOrEmpty(cmd)) return null;

            // Clone: the JsonElement must outlive this JsonDocument, since args is
            // read later on the main-thread dispatcher job, well after we return.
            JsonElement? args = root.TryGetProperty("args", out var a) ? a.Clone() : null;

            return new IncomingRequest(idEl.GetInt64(), cmd, args);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort id extraction from a line that failed full parsing, so an error
    /// reply can still be correlated by the client rather than timing out.
    /// </summary>
    public static long TrySalvageId(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line.TrimStart((char)0xFEFF));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.Number &&
                idEl.TryGetInt64(out var id))
            {
                return id;
            }
        }
        catch (Exception)
        {
            // genuinely unparseable
        }
        return 0;
    }

    public static string BuildOkResponse(long id, JsonNode? result) =>
        new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result ?? new JsonObject() }
            .ToJsonString();

    public static string BuildErrorResponse(long id, string code, string message, JsonNode? data = null)
    {
        var err = new JsonObject { ["code"] = code, ["message"] = message };
        if (data != null) err["data"] = data;
        return new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = err }.ToJsonString();
    }

    public static string BuildPush(string eventName, JsonNode? data = null) =>
        new JsonObject { ["event"] = eventName, ["data"] = data ?? new JsonObject() }.ToJsonString();
}
