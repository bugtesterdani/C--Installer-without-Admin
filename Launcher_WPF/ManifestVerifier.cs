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
    /// <summary>PEM-kodierter öffentlicher Schlüssel zur Verifikation.</summary>
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
        return TryVerifyManifest(manifestPath, baseFolder, out _);
    }

    /// <summary>
    /// Validiert Manifest und gibt im Fehlerfall eine Begründung zurück.
    /// </summary>
    public bool TryVerifyManifest(string manifestPath, string baseFolder, out string failureReason)
    {
        failureReason = string.Empty;

        if (!File.Exists(manifestPath))
        {
            failureReason = $"Manifest fehlt ({manifestPath}).";
            return false;
        }

        JsonDocument doc;
        try
        {
            var json = File.ReadAllText(manifestPath);
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            failureReason = $"Manifest konnte nicht gelesen werden: {ex.Message}";
            return false;
        }

        var root = doc.RootElement;

        if (!root.TryGetProperty("signature", out var sigElement))
        {
            failureReason = "Signatur fehlt im Manifest.";
            return false;
        }

        string signatureBase64 = sigElement.GetString();
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (Exception ex)
        {
            failureReason = $"Signatur ist kein gültiges Base64: {ex.Message}";
            return false;
        }

        // Manifest ohne Signatur neu erzeugen
        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Object)
        {
            failureReason = "Dateiliste fehlt oder ist kein Objekt.";
            return false;
        }

        Dictionary<string, string> files;
        try
        {
            files = filesElement.Deserialize<Dictionary<string, string>>();
        }
        catch (Exception ex)
        {
            failureReason = $"Dateiliste im Manifest fehlerhaft: {ex.Message}";
            return false;
        }

        if (files.Count() < 1)
        {
            failureReason = "File Datei enthält keine Dateien.";
            return false;
        }
        Dictionary<string, string> filebytes = new();
        foreach (var file in files)
        {
            filebytes.Add(NormalizeRelativePath(file.Key), file.Value);
        }

        var unsigned = new
        {
            version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null,
            filebytes
        };

        if (unsigned.version is null)
        {
            failureReason = "Versionsfeld fehlt oder ist leer.";
            return false;
        }

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
        {
            failureReason = "Signaturprüfung fehlgeschlagen.";
            return false;
        }

        // Hashes prüfen
        foreach (var kv in files)
        {
            string rel = NormalizeRelativePath(kv.Key);
            string expectedHash = kv.Value;

            string fullPath = Path.Combine(baseFolder, rel);

            if (!File.Exists(fullPath))
            {
                failureReason = $"Datei fehlt: {rel}";
                return false;
            }

            using var sha = SHA256.Create();
            var fileBytes = File.ReadAllBytes(fullPath);
            var hash = BitConverter.ToString(sha.ComputeHash(fileBytes)).Replace("-", "").ToLower();

            if (hash != expectedHash)
            {
                failureReason = $"Hash stimmt nicht: {rel}";
                return false;
            }
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
    /// Normalisiert relative Pfade aus dem Manifest:
    /// - wandelt "\" in "/" um
    /// - entfernt leere Segmente und "."
    /// - lehnt Pfade mit ".." ab
    /// - mappt anschließend auf den lokalen Separator
    /// </summary>
    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var posix = path.Replace("\\", "/");

        var parts = posix.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
                throw new InvalidOperationException("Unzulässiger Pfad im Manifest (..).");
            cleaned.Add(part);
        }

        var normalizedPosix = string.Join("/", cleaned);

        return Path.DirectorySeparatorChar == '/'
            ? normalizedPosix
            : normalizedPosix.Replace("/", Path.DirectorySeparatorChar.ToString());
    }
}
