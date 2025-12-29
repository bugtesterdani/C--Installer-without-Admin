using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Reflection;

namespace Launcher_WPF
{
    /// <summary>
    /// Verwalter für Updates, Verifikation und Fallback-Startlogik im A/B-Schema für die WPF-Variante.
    /// </summary>
    public class Updater
    {
        /// <summary>HTTP-Client für Update-Metadaten und Payload.</summary>
        private readonly HttpClient _http = new HttpClient();
        /// <summary>Aktiver Slot ("A" oder "B").</summary>
        private string _active;
        /// <summary>Inaktiver Slot ("A" oder "B").</summary>
        private string _inactive;
        /// <summary>Rückgabecode des gestarteten Prozesses (falls verfügbar).</summary>
        public int retucode;

        public void CreateDirectories()
        {
            if (!Directory.Exists(Path.Combine(AppConfig.BasePath, "..")))
                Directory.CreateDirectory(Path.Combine(AppConfig.BasePath, ".."));
            if (!Directory.Exists(AppConfig.BasePath))
                Directory.CreateDirectory(AppConfig.BasePath);
        }

        /// <summary>
        /// Liest den aktiven Slot von der Festplatte (Standard "A", falls nicht vorhanden).
        /// </summary>
        public string GetActive()
        {
            if (!File.Exists(AppConfig.ActiveFile))
            {
                File.WriteAllText(AppConfig.ActiveFile, "A");
                if (!Directory.Exists(Path.Combine(AppConfig.BasePath, "A")))
                    Directory.CreateDirectory(Path.Combine(AppConfig.BasePath, "A"));
                var info = Task.Run(async () => await FetchUpdateInfoAsync()).Result;
                if (info == null)
                {
                    Console.WriteLine("Konnte Update-Informationen nicht laden.");
                    throw new Exception("Update-Infos nicht geladen.");
                }
                Task.Run(async () => await DownloadAndInstallAsync(AppConfig.VersionA, info)).Wait();
            }

            return File.ReadAllText(AppConfig.ActiveFile).Trim();
        }

        /// <summary>
        /// Bestimmt den inaktiven Slot basierend auf dem aktiven Slot.
        /// </summary>
        public void GetInactive()
        {
            _active = GetActive();
            _inactive = _active == "A" ? "B" : "A";
        }

        /// <summary>
        /// Aktualisiert den inaktiven Slot, falls eine neuere Version vorhanden ist, und markiert ihn als aktiv.
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
        /// Validiert eine Version über das signierte Manifest und Datei-Hashes.
        /// </summary>
        public bool ValidateVersion(string folder)
        {
            string manifest = Path.Combine(folder, "manifest.json");

            if (!File.Exists(manifest))
                return false;

            string publicKey = new publicpem().pem;

            var verifier = new ManifestVerifier(publicKey);

            return verifier.VerifyManifest(manifest, folder);
        }

        /// <summary>
        /// Startet die aktive Version und wechselt bei Fehlern auf die inaktive Version.
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
        /// Lädt Update-Metadaten vom konfigurierten Endpunkt.
        /// </summary>
        private async Task<UpdateInfo?> FetchUpdateInfoAsync()
        {
            var json = await _http.GetStringAsync(AppConfig.UpdateInfoUrl);
            return JsonSerializer.Deserialize<UpdateInfo>(json);
        }

        /// <summary>
        /// Prüft, ob der lokale Versionsstand dem angegebenen Remote-Stand entspricht.
        /// </summary>
        private static bool IsUpToDate(string targetDir, string remoteVersion)
        {
            return ReadLocalVersion(targetDir) == remoteVersion;
        }

        /// <summary>
        /// Liest die lokal installierte Version der Anwendung aus der Assembly-Datei im angegebenen Verzeichnis.
        /// Gibt "0.0.0.0" zurück, falls die DLL nicht existiert oder keine FileVersion vorhanden ist.
        /// </summary>
        /// <param name="targetDir">Das Verzeichnis, in dem sich die Anwendungs-Assembly befindet.</param>
        /// <returns>Versions-String (z. B. "1.2.3.4") oder "0.0.0.0", wenn keine Version gefunden wurde.</returns>
        private static string ReadLocalVersion(string targetDir)
        {
            string dllPath = Path.Combine(targetDir, "MeineApp.dll");

            if (!File.Exists(dllPath))
                return "0.0.0.0";

            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return info.FileVersion ?? "0.0.0.0";
        }

        /// <summary>
        /// Lädt das Payload-ZIP, entpackt es ins Zielverzeichnis und schreibt die neue Versionsdatei.
        /// </summary>
        private async Task DownloadAndInstallAsync(string targetDir, UpdateInfo info)
        {
            var data = await _http.GetByteArrayAsync(info.Url);
            File.WriteAllBytes(AppConfig.TempZip, data);

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(AppConfig.TempZip, targetDir);

            Console.WriteLine($"Version {_inactive} aktualisiert.");
        }

        /// <summary>
        /// Startet eine veröffentlichte .NET-Anwendung und erfasst den Exit-Code.
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
                var proc = Process.Start(psi);
                proc.WaitForExit();
                retucode = proc.ExitCode;
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
        /// <summary>Semantische Versionsangabe der Remote-Version.</summary>
        public string Version { get; set; }
        /// <summary>Download-URL für das Payload-ZIP.</summary>
        public string Url { get; set; }
    }
}
