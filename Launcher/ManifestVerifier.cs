using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class ManifestVerifier
{
    private readonly string _publicKeyPem;

    public ManifestVerifier(string publicKeyPem)
    {
        _publicKeyPem = publicKeyPem;
    }

    public bool VerifyManifest(string manifestPath, string baseFolder)
    {
        var json = File.ReadAllText(manifestPath);
        var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        // Signatur extrahieren
        string signatureBase64 = doc.RootElement.GetProperty("signature").GetString();
        byte[] signature = Convert.FromBase64String(signatureBase64);

        // Manifest ohne Signatur neu erzeugen
        // JSON ohne Signatur extrahieren

        var unsigned = new
        {
            version = root.GetProperty("version").GetString(),
            files = root.GetProperty("files").Deserialize<Dictionary<string, string>>()
        };

        // Python-kompatible Serialisierung
        string unsignedJson = JsonSerializer.Serialize(unsigned, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null, // KEIN CamelCase!
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

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
            string rel = kv.Key;
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

    private static object SortElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new SortedDictionary<string, object>();
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
}
