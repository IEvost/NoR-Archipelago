using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NoR.SceneScanner;

internal sealed class Scanner : IDisposable
{
    private static readonly JsonWriterOptions JsonOpts = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Japanese readable
    };

    private readonly Options _o;
    private readonly AssetsManager _am = new();
    private readonly Dictionary<(string file, long pathId), ScriptInfo?> _scriptCache = new();
    private readonly Dictionary<(string file, long pathId), string?> _nameCache = new();
    private readonly SortedDictionary<string, int> _typeCounts = new(StringComparer.Ordinal);
    private StreamWriter? _jpOut;
    private int _jpCount;

    private sealed record ScriptInfo(string ClassName, string Namespace, string Assembly);

    public Scanner(Options o)
    {
        _o = o;
        _am.LoadClassPackage(o.TpkPath);
        _am.MonoTempGenerator = new MonoCecilTempGenerator(o.ManagedDir);
    }

    public void Run()
    {
        Directory.CreateDirectory(_o.OutDir);
        Directory.CreateDirectory(Path.Combine(_o.OutDir, "text"));
        _jpOut = new StreamWriter(Path.Combine(_o.OutDir, "text", "jp_strings.jsonl"), false, new UTF8Encoding(false));

        var sceneNames = ReadBuildScenes();

        var files = Directory.GetFiles(_o.DataDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && (Regex.IsMatch(n, @"^level\d+$") || n.EndsWith(".assets", StringComparison.Ordinal)))
            .Cast<string>()
            .Where(n => _o.OnlyFiles == null || _o.OnlyFiles.IsMatch(n))
            .OrderBy(n => n.StartsWith("level", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(n => ExtractNumber(n))
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();

        foreach (var name in files)
        {
            try
            {
                ScanFile(name, sceneNames);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[{name}] FAILED: {e.Message}");
            }
            finally
            {
                // Keep memory bounded: unload everything after each file.
                _am.UnloadAll(false);
                _scriptCache.Clear();
                _nameCache.Clear();
            }
        }

        WriteSummary();
        Console.WriteLine($"Japanese strings: {_jpCount}");
    }

    // ---------------------------------------------------------------- build settings

    private List<string> ReadBuildScenes()
    {
        var path = Path.Combine(_o.DataDir, "globalgamemanagers");
        var inst = _am.LoadAssetsFile(path, false);
        _am.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
        var result = new List<string>();
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.BuildSettings))
        {
            var bf = _am.GetBaseField(inst, info);
            foreach (var s in bf["scenes"]["Array"].Children) result.Add(s.AsString);
        }

        // Player settings relevant for resolution / aspect ratio support.
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.PlayerSettings))
        {
            var ps = _am.GetBaseField(inst, info);
            var sb = new StringBuilder();
            foreach (var c in ps.Children)
            {
                string n = c.FieldName;
                if (!(n.Contains("Aspect", StringComparison.OrdinalIgnoreCase) || n.Contains("Screen", StringComparison.OrdinalIgnoreCase)
                      || n.Contains("Resolution", StringComparison.OrdinalIgnoreCase) || n.Contains("Fullscreen", StringComparison.OrdinalIgnoreCase)
                      || n.Contains("resizable", StringComparison.OrdinalIgnoreCase))) continue;
                if (c.Children.Count > 0 && c.Children[0].FieldName == "Array")
                    sb.AppendLine($"{n} = [{string.Join(", ", c.Children[0].Children.Select(x => x.Value?.AsString ?? x.Children.Count.ToString()))}]");
                else if (c.Value == null && c.Children.Count > 0)
                    sb.AppendLine($"{n} = {{{string.Join(", ", c.Children.Select(x => x.FieldName + "=" + x.Value?.AsString))}}}");
                else
                    sb.AppendLine($"{n} = {c.Value?.AsString}");
            }
            File.WriteAllText(Path.Combine(_o.OutDir, "player_settings.txt"), sb.ToString());
        }

        using var fs = File.Create(Path.Combine(_o.OutDir, "build_scenes.json"));
        using var w = new Utf8JsonWriter(fs, JsonOpts);
        w.WriteStartArray();
        for (int i = 0; i < result.Count; i++)
        {
            w.WriteStartObject();
            w.WriteNumber("index", i);
            w.WriteString("file", "level" + i);
            w.WriteString("path", result[i]);
            w.WriteString("name", Path.GetFileNameWithoutExtension(result[i]));
            w.WriteEndObject();
        }
        w.WriteEndArray();
        Console.WriteLine($"Unity {inst.file.Metadata.UnityVersion}, {result.Count} scenes in build");
        _am.UnloadAll(false);
        return result;
    }

    // ---------------------------------------------------------------- per file

    private void ScanFile(string fileName, List<string> sceneNames)
    {
        var path = Path.Combine(_o.DataDir, fileName);
        var inst = _am.LoadAssetsFile(path, true);
        _am.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);

        bool isScene = fileName.StartsWith("level", StringComparison.Ordinal);
        int sceneIndex = isScene ? (int)ExtractNumber(fileName) : -1;
        string sceneName = isScene && sceneIndex < sceneNames.Count ? Path.GetFileNameWithoutExtension(sceneNames[sceneIndex]) : fileName;

        var tree = new TransformTree(_am, inst);

        if (!isScene) DumpTextAssets(inst, fileName);

        var selected = new List<(AssetFileInfo info, ScriptInfo script, AssetTypeValueField field)>();
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            AssetTypeValueField header;
            try { header = _am.GetBaseField(inst, info, AssetReadFlags.SkipMonoBehaviourFields); }
            catch { continue; }

            var script = ResolveScript(inst, header["m_Script"]);
            if (script == null) continue;

            bool gameScript = script.Assembly.StartsWith("Assembly-CSharp", StringComparison.Ordinal) && script.Namespace.Length == 0;
            bool textScript = script.ClassName is "TextMeshProUGUI" or "TextMeshPro" or "Text";
            bool match = gameScript && _o.TypeFilter.IsMatch(script.ClassName);
            if (!gameScript && !textScript) continue;

            AssetTypeValueField full;
            try { full = _am.GetBaseField(inst, info); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[{fileName}] cannot read {script.ClassName} #{info.PathId}: {e.Message}");
                continue;
            }

            long goId = header["m_GameObject"]["m_PathID"].AsLong;
            string objPath = tree.GetPath(goId);
            CollectJapanese(full, fileName, sceneName, objPath, script.ClassName, "");

            if (match)
            {
                selected.Add((info, script, full));
                _typeCounts[(isScene ? "scene:" : "prefab:") + script.ClassName] =
                    _typeCounts.GetValueOrDefault((isScene ? "scene:" : "prefab:") + script.ClassName) + 1;
            }
        }

        if (selected.Count == 0) { Console.WriteLine($"[{fileName}] {sceneName}: nothing selected"); return; }

        string dir = Path.Combine(_o.OutDir, isScene ? "scenes" : "prefabs");
        Directory.CreateDirectory(dir);
        string outName = isScene ? $"{sceneIndex:000}_{Sanitize(sceneName)}.json" : Sanitize(fileName) + ".json";

        using (var fs = File.Create(Path.Combine(dir, outName)))
        using (var w = new Utf8JsonWriter(fs, JsonOpts))
        {
            w.WriteStartObject();
            w.WriteString("file", fileName);
            w.WriteString("scene", sceneName);
            w.WriteNumber("sceneIndex", sceneIndex);
            w.WriteStartArray("components");
            foreach (var (info, script, full) in selected.OrderBy(s => s.script.ClassName, StringComparer.Ordinal))
            {
                long goId = full["m_GameObject"]["m_PathID"].AsLong;
                w.WriteStartObject();
                w.WriteString("type", script.ClassName);
                w.WriteNumber("pathId", info.PathId);
                w.WriteString("path", tree.GetPath(goId));
                w.WriteBoolean("goActive", tree.IsActive(goId));
                w.WriteBoolean("enabled", full["m_Enabled"].AsByte != 0);
                var pos = tree.GetWorldPosition(goId);
                w.WriteStartObject("position");
                w.WriteNumber("x", Math.Round(pos.X, 3));
                w.WriteNumber("y", Math.Round(pos.Y, 3));
                w.WriteEndObject();
                w.WritePropertyName("fields");
                WriteScriptFields(w, inst, full);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        Console.WriteLine($"[{fileName}] {sceneName}: {selected.Count} components");
    }

    // ---------------------------------------------------------------- scripts & names

    private ScriptInfo? ResolveScript(AssetsFileInstance inst, AssetTypeValueField pptr)
    {
        long pathId = pptr["m_PathID"].AsLong;
        if (pathId == 0) return null;
        int fileId = pptr["m_FileID"].AsInt;
        string key = fileId == 0 ? inst.name : inst.file.Metadata.Externals[fileId - 1].PathName;
        if (_scriptCache.TryGetValue((key, pathId), out var cached)) return cached;

        ScriptInfo? result = null;
        try
        {
            var ext = _am.GetExtAsset(inst, pptr);
            if (ext.baseField != null)
            {
                result = new ScriptInfo(
                    ext.baseField["m_ClassName"].AsString,
                    ext.baseField["m_Namespace"].AsString,
                    ext.baseField["m_AssemblyName"].AsString);
            }
        }
        catch { /* missing script */ }
        _scriptCache[(key, pathId)] = result;
        return result;
    }

    private string? ResolveName(AssetsFileInstance inst, AssetTypeValueField pptr)
    {
        long pathId = pptr["m_PathID"].AsLong;
        if (pathId == 0) return null;
        int fileId = pptr["m_FileID"].AsInt;
        string key = fileId == 0 ? inst.name : (fileId - 1 < inst.file.Metadata.Externals.Count ? inst.file.Metadata.Externals[fileId - 1].PathName : "?");
        if (_nameCache.TryGetValue((key, pathId), out var cached)) return cached;

        string? name = null;
        try
        {
            var ext = _am.GetExtAsset(inst, pptr, false, AssetReadFlags.SkipMonoBehaviourFields);
            var bf = ext.baseField;
            if (bf != null)
            {
                var n = bf["m_Name"];
                if (!n.IsDummy) name = n.AsString;
                if (string.IsNullOrEmpty(name))
                {
                    // Components have no name of their own: use their GameObject's name.
                    var go = bf["m_GameObject"];
                    if (!go.IsDummy)
                    {
                        var goExt = _am.GetExtAsset(ext.file, go);
                        if (goExt.baseField != null) name = goExt.baseField["m_Name"].AsString;
                    }
                }
            }
        }
        catch { /* external file missing */ }
        _nameCache[(key, pathId)] = name;
        return name;
    }

    // ---------------------------------------------------------------- field serialization

    private static readonly HashSet<string> SkipFields = new(StringComparer.Ordinal)
    {
        "m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_ObjectHideFlags",
        "m_PrefabParentObject", "m_PrefabInternal",
    };

    private void WriteScriptFields(Utf8JsonWriter w, AssetsFileInstance inst, AssetTypeValueField full)
    {
        w.WriteStartObject();
        foreach (var c in full.Children)
        {
            if (SkipFields.Contains(c.FieldName)) continue;
            w.WritePropertyName(c.FieldName);
            WriteValue(w, inst, c, 0);
        }
        w.WriteEndObject();
    }

    private void WriteValue(Utf8JsonWriter w, AssetsFileInstance inst, AssetTypeValueField f, int depth)
    {
        if (f.TypeName.StartsWith("PPtr<", StringComparison.Ordinal))
        {
            long pathId = f["m_PathID"].AsLong;
            if (pathId == 0) { w.WriteNullValue(); return; }
            w.WriteStartObject();
            w.WriteString("$ptr", f.TypeName[5..^1].TrimStart('$'));
            w.WriteNumber("fileId", f["m_FileID"].AsInt);
            w.WriteNumber("pathId", pathId);
            var name = ResolveName(inst, f);
            if (name != null) w.WriteString("name", name);
            w.WriteEndObject();
            return;
        }

        var v = f.Value;
        if (v != null)
        {
            switch (v.ValueType)
            {
                case AssetValueType.Bool: w.WriteBooleanValue(v.AsBool); return;
                case AssetValueType.Int8: case AssetValueType.Int16: case AssetValueType.Int32: w.WriteNumberValue(v.AsInt); return;
                case AssetValueType.UInt8: case AssetValueType.UInt16: case AssetValueType.UInt32: w.WriteNumberValue(v.AsUInt); return;
                case AssetValueType.Int64: w.WriteNumberValue(v.AsLong); return;
                case AssetValueType.UInt64: w.WriteNumberValue(v.AsULong); return;
                case AssetValueType.Float: WriteFloat(w, v.AsFloat); return;
                case AssetValueType.Double: WriteFloat(w, v.AsDouble); return;
                case AssetValueType.String: w.WriteStringValue(v.AsString); return;
                case AssetValueType.ByteArray: w.WriteStringValue($"<bytes:{v.AsByteArray.Length}>"); return;
                case AssetValueType.Array: break; // handled below
            }
        }

        if (depth > 8) { w.WriteStringValue("<max depth>"); return; }

        // vector/List: single child "Array"
        if (f.Children.Count == 1 && f.Children[0].FieldName == "Array")
        {
            WriteArray(w, inst, f.Children[0], depth);
            return;
        }
        if (f.TemplateField.IsArray)
        {
            WriteArray(w, inst, f, depth);
            return;
        }

        w.WriteStartObject();
        foreach (var c in f.Children)
        {
            w.WritePropertyName(c.FieldName);
            WriteValue(w, inst, c, depth + 1);
        }
        w.WriteEndObject();
    }

    private void WriteArray(Utf8JsonWriter w, AssetsFileInstance inst, AssetTypeValueField arr, int depth)
    {
        w.WriteStartArray();
        int n = 0;
        foreach (var c in arr.Children)
        {
            if (++n > 2048) { w.WriteStringValue("<truncated>"); break; }
            WriteValue(w, inst, c, depth + 1);
        }
        w.WriteEndArray();
    }

    private static void WriteFloat(Utf8JsonWriter w, double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) w.WriteNullValue();
        else w.WriteNumberValue(Math.Round(d, 4));
    }

    // ---------------------------------------------------------------- Japanese text collection

    private void CollectJapanese(AssetTypeValueField f, string file, string scene, string objPath, string component, string fieldPath)
    {
        foreach (var c in f.Children)
        {
            if (c.TypeName.StartsWith("PPtr<", StringComparison.Ordinal)) continue;
            string p = c.FieldName == "Array" ? fieldPath : (fieldPath.Length == 0 ? c.FieldName : fieldPath + "." + c.FieldName);
            if (c.FieldName == "data" && f.TemplateField.IsArray) p = fieldPath + "[" + f.Children.IndexOf(c) + "]";

            if (c.Value != null && c.Value.ValueType == AssetValueType.String)
            {
                var s = c.Value.AsString;
                if (ContainsJapanese(s)) WriteJp(file, scene, objPath, component, p, s);
            }
            else if (c.Children.Count > 0)
            {
                CollectJapanese(c, file, scene, objPath, component, p);
            }
        }
    }

    private void WriteJp(string file, string scene, string objPath, string component, string field, string text)
    {
        _jpCount++;
        var line = JsonSerializer.Serialize(new { file, scene, obj = objPath, component, field, text },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        _jpOut!.WriteLine(line);
    }

    public static bool ContainsJapanese(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if ((c >= '぀' && c <= 'ヿ') || (c >= '一' && c <= '鿿') || (c >= '＀' && c <= '￯'))
                return true;
        return false;
    }

    // ---------------------------------------------------------------- text assets

    private void DumpTextAssets(AssetsFileInstance inst, string fileName)
    {
        var infos = inst.file.GetAssetsOfType(AssetClassID.TextAsset);
        if (infos.Count == 0) return;
        var dir = Path.Combine(_o.OutDir, "textassets");
        Directory.CreateDirectory(dir);
        foreach (var info in infos)
        {
            var bf = _am.GetBaseField(inst, info);
            string name = bf["m_Name"].AsString;
            var bytes = bf["m_Script"].AsByteArray;
            File.WriteAllBytes(Path.Combine(dir, Sanitize(name) + ".txt"), bytes);
        }
        Console.WriteLine($"[{fileName}] {infos.Count} TextAssets");
    }

    // ---------------------------------------------------------------- summary

    private void WriteSummary()
    {
        using var fs = File.Create(Path.Combine(_o.OutDir, "summary.json"));
        using var w = new Utf8JsonWriter(fs, JsonOpts);
        w.WriteStartObject();
        w.WriteNumber("japaneseStrings", _jpCount);
        w.WriteStartObject("componentCounts");
        foreach (var kv in _typeCounts) w.WriteNumber(kv.Key, kv.Value);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static long ExtractNumber(string s)
    {
        var m = Regex.Match(s, @"\d+");
        return m.Success ? long.Parse(m.Value) : -1;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    public void Dispose()
    {
        _jpOut?.Dispose();
        _am.UnloadAll(true);
    }
}

/// <summary>Hierarchy paths and world positions for one assets file.</summary>
internal sealed class TransformTree
{
    private readonly Dictionary<long, string> _goName = new();
    private readonly Dictionary<long, bool> _goActive = new();
    private readonly Dictionary<long, long> _goTransform = new();
    private readonly Dictionary<long, (long go, long father, Vector3 pos, Quaternion rot, Vector3 scale)> _tr = new();
    private readonly Dictionary<long, string> _pathCache = new();

    public TransformTree(AssetsManager am, AssetsFileInstance inst)
    {
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.GameObject))
        {
            var bf = am.GetBaseField(inst, info);
            _goName[info.PathId] = bf["m_Name"].AsString;
            _goActive[info.PathId] = bf["m_IsActive"].AsBool;
        }
        foreach (var cls in new[] { AssetClassID.Transform, AssetClassID.RectTransform })
        {
            foreach (var info in inst.file.GetAssetsOfType(cls))
            {
                var bf = am.GetBaseField(inst, info);
                long go = bf["m_GameObject"]["m_PathID"].AsLong;
                long father = bf["m_Father"]["m_PathID"].AsLong;
                var p = bf["m_LocalPosition"];
                var r = bf["m_LocalRotation"];
                var s = bf["m_LocalScale"];
                _tr[info.PathId] = (go, father,
                    new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat),
                    new Quaternion(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat, r["w"].AsFloat),
                    new Vector3(s["x"].AsFloat, s["y"].AsFloat, s["z"].AsFloat));
                _goTransform[go] = info.PathId;
            }
        }
    }

    public string GetPath(long goId)
    {
        if (_pathCache.TryGetValue(goId, out var cached)) return cached;
        string name = _goName.GetValueOrDefault(goId, "?");
        string result = name;
        if (_goTransform.TryGetValue(goId, out var t) && _tr.TryGetValue(t, out var tr) && tr.father != 0 && _tr.TryGetValue(tr.father, out var parent))
            result = GetPath(parent.go) + "/" + name;
        _pathCache[goId] = result;
        return result;
    }

    /// <summary>Active in hierarchy (self and all parents active).</summary>
    public bool IsActive(long goId)
    {
        for (long g = goId; g != 0;)
        {
            if (!_goActive.GetValueOrDefault(g, true)) return false;
            if (!_goTransform.TryGetValue(g, out var t) || !_tr.TryGetValue(t, out var tr) || tr.father == 0 || !_tr.TryGetValue(tr.father, out var parent)) break;
            g = parent.go;
        }
        return true;
    }

    public Vector3 GetWorldPosition(long goId)
    {
        if (!_goTransform.TryGetValue(goId, out var t)) return Vector3.Zero;
        return WorldPos(t, Vector3.Zero, 0);
    }

    private Vector3 WorldPos(long transformId, Vector3 local, int depth)
    {
        var tr = _tr[transformId];
        // point = parentTRS * (this TRS * local)
        var p = tr.pos + Vector3.Transform(local * tr.scale, tr.rot);
        if (tr.father == 0 || depth > 64 || !_tr.ContainsKey(tr.father)) return p;
        return WorldPos(tr.father, p, depth + 1);
    }
}
