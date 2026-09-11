using System.IO;
using System.Text.Json.Nodes;

namespace ContosoForge.PipelineStudio;

public sealed record CatalogEntry(string Root, string RunId, string Label, string RecordedAt)
{
    public override string ToString() => $"{Label} · {RunId} · {Root}";
}

/// <summary>Locators only. An exclusive sibling lock serializes read/merge/atomic replace across processes.</summary>
public sealed class RunCatalog(string path)
{
    internal static string UserDataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContosoForge");
    public static string DefaultPath => Path.Combine(UserDataDirectory, "runs.json");
    public IReadOnlyList<CatalogEntry> List()
    {
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var document = JsonNode.Parse(stream) as JsonObject ?? throw new InvalidDataException("Malformed run catalog");
        if (document["version"]?.GetValue<int>() != 1) throw new InvalidDataException("Unsupported run catalog. Import a run folder using a new catalog; keep this file for recovery: " + path);
        return ((JsonArray?)document["entries"] ?? throw new InvalidDataException("Missing catalog entries")).Select(n => {
            var item = (JsonObject)n!;
            return new CatalogEntry(RunEvidence.Text(item, "root"), RunEvidence.Text(item, "runId"), RunEvidence.Text(item, "label"), RunEvidence.Text(item, "recordedAt"));
        }).ToArray();
    }
    public async Task Register(CatalogEntry entry)
    {
        entry = entry with { Root = Path.GetFullPath(entry.Root).TrimEnd(Path.DirectorySeparatorChar) };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        FileStream? held = null;
        for (var attempt = 0; held is null && attempt < 100; attempt++)
        {
            try { held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(50); }
        }
        using var lease = held ?? throw new IOException("Run catalog is busy; retry registration.");
        var entries = List().ToList(); // Never overwrite a corrupt or unsupported index.
        if (entries.Any(e => e.RunId == entry.RunId && string.Equals(e.Root, entry.Root, StringComparison.OrdinalIgnoreCase))) return;
        entries.Add(entry); // Conflicting IDs retain both locators; evidence is checked on selection.
        var array = new JsonArray();
        foreach (var e in entries) array.Add(new JsonObject { ["root"] = e.Root, ["runId"] = e.RunId, ["label"] = e.Label, ["recordedAt"] = e.RecordedAt });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["version"] = 1, ["entries"] = array }.ToJsonString());
                stream.Write(bytes); stream.Flush(true);
            }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException) when (attempt < 39 && File.Exists(temporary))
                {
                    // A reader outside Studio may briefly deny replacement. Keep our writer lock and staged bytes.
                    await Task.Delay(50);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
