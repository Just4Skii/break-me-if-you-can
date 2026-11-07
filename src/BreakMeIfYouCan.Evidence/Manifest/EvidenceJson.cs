using System.Text.Json;
using System.Text.Json.Serialization;

namespace BreakMeIfYouCan.Evidence.Manifest;

/// <summary>
/// One serialization contract for the evidence manifest. The generator writes it and the
/// API reads it, so the wire shape must not drift between the two processes.
/// </summary>
public static class EvidenceJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(CiEvidenceManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    public static CiEvidenceManifest? Deserialize(string json) =>
        JsonSerializer.Deserialize<CiEvidenceManifest>(json, Options);
}
