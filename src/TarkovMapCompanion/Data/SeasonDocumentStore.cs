using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovMapCompanion.Diagnostics;

namespace TarkovMapCompanion.Data;

/// <summary>
/// One season-1 "KORD BREACH" TerraGroup document spawn point.
/// </summary>
/// <remarks>
/// Coordinates are game world (x, z); height is not supplied. The source site's own text is English
/// (<see cref="NameEn"/>); <see cref="NameZh"/> is the composed Chinese label the layer displays.
/// </remarks>
public sealed class SeasonDocumentPoint
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("map")] public string Map { get; set; } = "";
    [JsonPropertyName("nameZh")] public string? NameZh { get; set; }

    /// <summary>
    /// The spawn's English title from the source, e.g. <c>Warehouse 5 Shelves</c>. Null in a v1
    /// snapshot, which predates the field; the Chinese name is then the only one there is.
    /// </summary>
    [JsonPropertyName("nameEn")] public string? NameEn { get; set; }

    [JsonPropertyName("typeZh")] public string? TypeZh { get; set; }
    [JsonPropertyName("titleZh")] public string? TitleZh { get; set; }
    [JsonPropertyName("floor")] public string? Floor { get; set; }

    /// <summary>
    /// The floor layer this spawn belongs to, as a <c>MapFloor.Name</c>, or null for the ground
    /// floor.
    /// </summary>
    /// <remarks>
    /// Schema v2 field. A v1 snapshot has no <c>floorKey</c> and deserializes to null here, which
    /// is the same meaning as an explicit null: ground floor. The value is used as a layer key, not
    /// displayed -- <see cref="Floor"/> is the human-readable one -- so it must be compared, never
    /// shown.
    /// </remarks>
    [JsonPropertyName("floorKey")] public string? FloorKey { get; set; }

    [JsonPropertyName("keyRequiredZh")] public string? KeyRequiredZh { get; set; }

    /// <summary>False when nobody has confirmed the spawn in game; drives the "?" styling.</summary>
    [JsonPropertyName("confirmed")] public bool Confirmed { get; set; }

    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
}

/// <summary>Whole snapshot of season document spawns; embedded read-only data.</summary>
public sealed class SeasonDocumentsDocument
{
    /// <summary>
    /// Snapshot schema. 1 is the pre-floor shape, where every point is implicitly ground level;
    /// 2 adds <see cref="SeasonDocumentPoint.FloorKey"/>. Absent means 1.
    /// </summary>
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("generatedAt")] public string? GeneratedAt { get; set; }
    [JsonPropertyName("points")] public List<SeasonDocumentPoint> Points { get; set; } = [];
}

/// <summary>
/// Loads the bundled season document spawn snapshot, keyed by map.
/// </summary>
/// <remarks>
/// Embedded-only: the snapshot ships with the app and has no user-override file, unlike
/// <see cref="ExtractNotesStore"/>. A missing or unreadable resource leaves an empty store so the
/// layer simply draws nothing rather than taking the map down.
/// </remarks>
public sealed class SeasonDocumentStore
{
    private const string SnapshotResourceName = "TarkovMapCompanion.Data.Snapshots.season-documents.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Guards the one-time warning about a missing snapshot.</summary>
    private static int _missingResourceLogged;

    private readonly Dictionary<string, List<SeasonDocumentPoint>> _byMap = new(StringComparer.Ordinal);

    /// <summary>
    /// Schema of the snapshot that was loaded: 1 for the pre-floor shape where every point is
    /// implicitly ground level, 2 once points carry <see cref="SeasonDocumentPoint.FloorKey"/>.
    /// 0 when nothing loaded.
    /// </summary>
    /// <remarks>
    /// Nothing branches on this -- both shapes mean the same thing to the reader, because a v1
    /// point simply has no floor key -- so it exists to make a stale snapshot diagnosable rather
    /// than to drive behaviour.
    /// </remarks>
    public int SchemaVersion { get; private set; }

    public SeasonDocumentStore() => Load();

    private SeasonDocumentStore(SeasonDocumentsDocument? document) => Merge(document);

    /// <summary>How many document spawns this map has. 0 when it has none.</summary>
    public int CountFor(string map) =>
        _byMap.TryGetValue(NormalizeMap(map), out var points) ? points.Count : 0;

    /// <summary>The document spawns for a map, or empty. Never null.</summary>
    public IReadOnlyList<SeasonDocumentPoint> FindFor(string map) =>
        _byMap.TryGetValue(NormalizeMap(map), out var points) ? points : [];

    /// <summary>
    /// Builds a store from an in-memory snapshot. Tests use this so they do not depend on the
    /// bundled resource; production always goes through the parameterless constructor.
    /// </summary>
    internal static SeasonDocumentStore FromJson(string json) =>
        new(JsonSerializer.Deserialize<SeasonDocumentsDocument>(json, JsonOptions));

    private void Load()
    {
        var document = ReadEmbedded();

        if (document is null && Interlocked.Exchange(ref _missingResourceLogged, 1) == 0)
            Log.Warn("season documents: bundled snapshot missing or unreadable; the layer will be empty");

        Merge(document);
    }

    private void Merge(SeasonDocumentsDocument? document)
    {
        if (document is null)
            return;

        SchemaVersion = document.SchemaVersion;

        foreach (var point in document.Points)
        {
            if (string.IsNullOrWhiteSpace(point.Map) || string.IsNullOrWhiteSpace(point.Id))
                continue;

            var key = NormalizeMap(point.Map);
            if (!_byMap.TryGetValue(key, out var bucket))
                _byMap[key] = bucket = [];

            bucket.Add(point);
        }
    }

    /// <summary>
    /// Map match key. The snapshot uses tarkov.dev-style slugs ("ground-zero", "the-lab") while the
    /// catalog's display names are prose ("Ground Zero", "The Lab"), so both sides are folded to
    /// lowercase alphanumerics before comparison.
    /// </summary>
    internal static string NormalizeMap(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    private static SeasonDocumentsDocument? ReadEmbedded()
    {
        try
        {
            using var stream = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream(SnapshotResourceName);

            return stream is null ? null : JsonSerializer.Deserialize<SeasonDocumentsDocument>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warn($"season documents: bundled snapshot unreadable: {ex.Message}");
            return null;
        }
    }
}
