using System.Text.Json;

namespace NoR.DataBuilder;

internal sealed record SceneComponent(
    string Scene, int SceneIndex, string Type, long PathId, string Path, bool Active, double X, double Y, JsonElement Fields)
{
    public int Int(string field, int fallback = -1) =>
        Fields.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    public string? Str(string field) =>
        Fields.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public long PtrPathId(string field) =>
        Fields.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("pathId", out var p) ? p.GetInt64() : 0;

    public string? PtrName(string field) =>
        Fields.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var n) ? n.GetString() : null;
}

internal sealed class SceneData
{
    public readonly List<SceneComponent> Components = new();
    public readonly SortedDictionary<int, string> Scenes = new();

    public IEnumerable<SceneComponent> OfType(string type) => Components.Where(c => c.Type == type);

    public static SceneData Load(string dir)
    {
        var data = new SceneData();
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            // Documents stay alive for the process lifetime because the records keep JsonElements.
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            string scene = root.GetProperty("scene").GetString()!;
            int index = root.GetProperty("sceneIndex").GetInt32();
            data.Scenes[index] = scene;
            foreach (var c in root.GetProperty("components").EnumerateArray())
            {
                var pos = c.GetProperty("position");
                data.Components.Add(new SceneComponent(
                    scene, index,
                    c.GetProperty("type").GetString()!,
                    c.GetProperty("pathId").GetInt64(),
                    c.GetProperty("path").GetString()!,
                    c.GetProperty("goActive").GetBoolean(),
                    pos.GetProperty("x").GetDouble(),
                    pos.GetProperty("y").GetDouble(),
                    c.GetProperty("fields")));
            }
        }
        return data;
    }
}
