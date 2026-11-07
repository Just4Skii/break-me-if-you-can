using System.Security.Cryptography;
using System.Text;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Generation;

/// <summary>Canonical, deterministic persistence of the evidence manifest.</summary>
public static class EvidenceManifestFile
{
    public const long MaxSizeBytes = 1024 * 1024;

    public static string Write(CiEvidenceManifest manifest, string path)
    {
        var json = EvidenceJson.Serialize(manifest);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return Sha256(json);
    }

    public static CiEvidenceManifest Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Evidence manifest not found.", path);
        }

        if (info.Length > MaxSizeBytes)
        {
            throw new InvalidDataException("Evidence manifest exceeds the maximum accepted size.");
        }

        var json = File.ReadAllText(path);
        return EvidenceJson.Deserialize(json)
               ?? throw new InvalidDataException("Evidence manifest deserialized to null.");
    }

    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}
