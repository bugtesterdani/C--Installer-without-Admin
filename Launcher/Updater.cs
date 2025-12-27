using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Launcher
{
    /// <summary>
    /// Handles update download, verification, and launching with a dual-slot (A/B) strategy.
    /// </summary>
    public class Updater
    {
        /// <summary>HTTP client for fetching update metadata and payloads.</summary>
        private readonly HttpClient _http = new HttpClient();
        /// <summary>Currently active slot identifier ("A" oder "B").</summary>
        private string _active;
        /// <summary>Currently inactive slot identifier ("A" oder "B").</summary>
        private string _inactive;

        /// <summary>
        /// Reads the active slot marker from disk (defaults to "A" if missing).
        /// </summary>
        public string GetActive()
        {
            if (!File.Exists(AppConfig.ActiveFile))
                File.WriteAllText(AppConfig.ActiveFile, "A");

            return File.ReadAllText(AppConfig.ActiveFile).Trim();
        }

        /// <summary>
        /// Determines the inactive slot based on the current active slot.
        /// </summary>
        public void GetInactive()
        {
            _active = GetActive();
            _inactive = _active == "A" ? "B" : "A";
        }

        /// <summary>
        /// Updates the inactive slot if a newer version is available and marks it active after download.
        /// </summary>
        public async Task UpdateInactiveVersionAsync()
        {
            GetInactive();
            string activeDir = _active == "A" ? AppConfig.VersionA : AppConfig.VersionB;
            string inactiveDir = _inactive == "A" ? AppConfig.VersionA : AppConfig.VersionB;

            Console.WriteLine($"Prüfe Updates für Version {_active}...");

            var info = await FetchUpdateInfoAsync();
            if (info == null)
            {
                Console.WriteLine("Konnte Update-Informationen nicht laden.");
                return;
            }

            if (IsUpToDate(activeDir, info.Version))
            {
                Console.WriteLine("Keine Updates für aktive Version.");
                return;
            }

            Console.WriteLine($"Aktive Version veraltet. Prüfe Updates für inaktive Version {_inactive}...");

            if (IsUpToDate(inactiveDir, info.Version))
            {
                Console.WriteLine("Keine Updates für inaktive Version.");
                return;
            }

            Console.WriteLine($"Update gefunden: {ReadLocalVersion(inactiveDir)} → {info.Version}");

            await DownloadAndInstallAsync(inactiveDir, info);

            File.WriteAllText(AppConfig.ActiveFile, _inactive);
        }

        /// <summary>
        /// Validates a version folder by checking the signed manifest and file hashes.
        /// </summary>
        public bool ValidateVersion(string folder)
        {
            string manifest = Path.Combine(folder, "manifest.json");

            if (!File.Exists(manifest))
                return false;

            string publicKey = File.ReadAllText("public.pem");

            var verifier = new ManifestVerifier(publicKey);

            return verifier.VerifyManifest(manifest, folder);
        }

        /// <summary>
        /// Starts the active version and falls back to the inactive slot if validation or launch fails.
        /// </summary>
        public bool StartWithFallback()
        {
            GetInactive();

            string activeFolder = _active == "A" ? AppConfig.VersionA : AppConfig.VersionB;
            string inactiveFolder = _inactive == "A" ? AppConfig.VersionA : AppConfig.VersionB;

            Console.WriteLine($"Starte aktive Version {_active}...");
            // 1. Prüfen ob aktive Version gültig ist
            if (ValidateVersion(activeFolder))
                if (TryStart(activeFolder))
                    return true;

            Console.WriteLine("Aktive Version fehlerhaft! Fallback...");
            // 2. Fallback
            if (ValidateVersion(inactiveFolder))
            {
                File.WriteAllText(AppConfig.ActiveFile, _inactive);
                if (TryStart(inactiveFolder))
                    return true;
            }

            Console.WriteLine("Beide Versionen beschädigt.");
            Directory.Delete(AppConfig.BasePath, true);
            Directory.CreateDirectory(AppConfig.BasePath);
            return false;
        }


        /// <summary>
        /// Fetches update metadata from the configured endpoint.
        /// </summary>
        private async Task<UpdateInfo?> FetchUpdateInfoAsync()
        {
            var json = await _http.GetStringAsync(AppConfig.UpdateInfoUrl);
            return JsonSerializer.Deserialize<UpdateInfo>(json);
        }

        /// <summary>
        /// Returns true if the local version file matches the supplied remote version.
        /// </summary>
        private static bool IsUpToDate(string targetDir, string remoteVersion)
        {
            return ReadLocalVersion(targetDir) == remoteVersion;
        }

        /// <summary>
        /// Reads the local version from version.txt or returns "0.0.0" if missing.
        /// </summary>
        private static string ReadLocalVersion(string targetDir)
        {
            string versionFile = Path.Combine(targetDir, "version.txt");
            return File.Exists(versionFile)
                ? File.ReadAllText(versionFile).Trim()
                : "0.0.0";
        }

        /// <summary>
        /// Downloads the payload ZIP, extracts it into the target directory, and writes the new version marker.
        /// </summary>
        private async Task DownloadAndInstallAsync(string targetDir, UpdateInfo info)
        {
            var data = await _http.GetByteArrayAsync(info.Url);
            File.WriteAllBytes(AppConfig.TempZip, data);

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(AppConfig.TempZip, targetDir);

            File.WriteAllText(Path.Combine(targetDir, "version.txt"), info.Version);

            Console.WriteLine($"Version {_inactive} aktualisiert.");
        }

        /// <summary>
        /// Tries to launch a published .NET application from the given folder.
        /// </summary>
        private bool TryStart(string exe)
        {
            exe = Path.Combine(exe, "MeineApp.dll");
            try
            {
                var psi = new ProcessStartInfo("dotnet", $"\"{exe}\"")
                {
                    UseShellExecute = false
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class UpdateInfo
    {
        /// <summary>Remote semantic version string.</summary>
        public string Version { get; set; }
        /// <summary>Download URL for the update payload ZIP.</summary>
        public string Url { get; set; }
    }
}
