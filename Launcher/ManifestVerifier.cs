using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Prüft ein Manifest auf gültige RSA-Signatur und Dateihashes.
/// </summary>
public class ManifestVerifier
{
    /// <summary>PEM-kodierter öffentlicher Schlüssel, der zur Verifikation genutzt wird.</summary>
    private readonly string _publicKeyPem;

    /// <summary>
    /// Erstellt einen Verifier mit dem angegebenen öffentlichen Schlüssel.
    /// </summary>
    public ManifestVerifier(string publicKeyPem)
    {
        _publicKeyPem = publicKeyPem;
    }

    /// <summary>
    /// Validiert Manifest-Signatur und SHA-256-Hashes aller referenzierten Dateien.
    /// </summary>
    public bool VerifyManifest(string manifestPath, string baseFolder)
    {
        var json = File.ReadAllText(manifestPath);
        var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        // Signatur extrahieren
        if (!root.TryGetProperty("signature", out var signatureElement))
            return false;

        string signatureBase64 = signatureElement.GetString();
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch
        {
            return false;
        }

        // Manifest ohne Signatur neu erzeugen
        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Object)
            return false;

        Dictionary<string, string> files;
        try
        {
            files = filesElement.Deserialize<Dictionary<string, string>>();
        }
        catch
        {
            return false;
        }

        var unsigned = new
        {
            version = root.TryGetProperty("version", out var versionEl) ? versionEl.GetString() : null,
            files
        };

        if (unsigned.version is null)
            return false;

        byte[] unsignedBytes = CanonicalJson(unsigned);

        // Öffentlichen Schlüssel laden
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_publicKeyPem);

        // Signatur prüfen
        bool validSignature = rsa.VerifyData(
            unsignedBytes,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        if (!validSignature)
            return false;

        // Hashes prüfen
        var files = unsigned.files;

        foreach (var kv in files)
        {
            string rel = NormalizeRelativePath(kv.Key);
            string expectedHash = kv.Value;

            string fullPath = Path.Combine(baseFolder, rel);

            if (!File.Exists(fullPath))
                return false;

            using var sha = SHA256.Create();
            var fileBytes = File.ReadAllBytes(fullPath);
            var hash = BitConverter.ToString(sha.ComputeHash(fileBytes)).Replace("-", "").ToLower();

            if (hash != expectedHash)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Erzeugt kanonische JSON-Bytes, die identisch zu Python/canonical_json sind.
    /// </summary>
    public static byte[] CanonicalJson(object obj)
    {
        // 1. Objekt in Dictionary umwandeln
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);

        // 2. Rekursiv sortieren
        var sorted = SortElement(doc.RootElement);

        // 3. Ohne Leerzeichen serialisieren
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string canonical = JsonSerializer.Serialize(sorted, options);
        return Encoding.UTF8.GetBytes(canonical);
    }

    /// <summary>
    /// Sortiert JSON-Elemente rekursiv und liefert eine strukturierte Darstellung mit stabiler Reihenfolge.
    /// </summary>
    private static object SortElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = SortElement(prop.Value);
                return dict;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(SortElement).ToList();

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.GetDecimal();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Null:
                return null;

            default:
                throw new Exception("Unsupported JSON type");
        }
    }

    /// <summary>
    /// Normalisiert relative Pfade aus dem Manifest auf einen plattformspezifischen Separator,
    /// behält aber die Posix-Logik aus dem Manifest bei ("/" wird bevorzugt).
    /// </summary>
    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        // Zuerst auf POSIX umstellen, dann auf das lokale Dateisystem abbilden.
        var posix = path.Replace("\\", "/");
        return Path.DirectorySeparatorChar == '/'
            ? posix
            : posix.Replace("/", Path.DirectorySeparatorChar.ToString());
    }
}
