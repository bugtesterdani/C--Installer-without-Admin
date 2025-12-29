using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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
        public int retucode = -1;
        /// <summary>Letzte Statusmeldung für die UI.</summary>
        public string StatusMessage { get; private set; } = "Bereit.";
        /// <summary>Letzte Fehlerursache bei der Manifest-Validierung.</summary>
        public string LastValidationError { get; private set; } = string.Empty;

        public void CreateDirectories()
        {
            if (!Directory.Exists(Path.Combine(AppConfig.BasePath, "..")))
                Directory.CreateDirectory(Path.Combine(AppConfig.BasePath, ".."));
            if (!Directory.Exists(AppConfig.BasePath))
                Directory.CreateDirectory(AppConfig.BasePath);
        }

        /// /// <summary>
        /// Ermittelt die aktuell aktive Installationsversion (A oder B).
        /// Falls noch keine aktive Version existiert, wird Version A initialisiert,
        /// notwendige Verzeichnisse werden angelegt und das Update heruntergeladen
        /// und installiert.
        /// </summary>
        /// <returns>
        /// Ein <see cref="Task{String}"/> mit dem Kennzeichen der aktiven Version
        /// (z. B. "A" oder "B").
        /// </returns>
        /// <exception cref="Exception">
        /// Wird ausgelöst, wenn die Update-Informationen nicht geladen werden konnten.
        /// </exception>
        public async Task<string> GetActive()
        {
            if (!File.Exists(AppConfig.ActiveFile))
            {
                File.WriteAllText(AppConfig.ActiveFile, "A");
                if (!Directory.Exists(Path.Combine(AppConfig.BasePath, "A")))
                    Directory.CreateDirectory(Path.Combine(AppConfig.BasePath, "A"));
                var info = await FetchUpdateInfoAsync();
                if (info == null)
                {
                    Console.WriteLine("Konnte Update-Informationen nicht laden.");
                    throw new Exception("Update-Infos nicht geladen.");
                }
                await DownloadAndInstallAsync(AppConfig.VersionA, info);
            }

            return File.ReadAllText(AppConfig.ActiveFile).Trim();
        }

        /// <summary>
        /// Bestimmt den inaktiven Slot basierend auf dem aktiven Slot.
        /// </summary>
        public void GetInactive()
        {
            _active = GetActive().GetAwaiter().GetResult();
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
            StatusMessage = $"Prüfe Updates für Version {_active}...";

            var info = await FetchUpdateInfoAsync();
            if (info == null)
            {
                Console.WriteLine("Konnte Update-Informationen nicht laden.");
                StatusMessage = "Update-Informationen konnten nicht geladen werden.";
                return;
            }

            if (IsUpToDate(activeDir, info.Version))
            {
                Console.WriteLine("Keine Updates für aktive Version.");
                StatusMessage = "Aktive Version ist aktuell.";
                return;
            }

            Console.WriteLine($"Aktive Version veraltet. Prüfe Updates für inaktive Version {_inactive}...");

            if (IsUpToDate(inactiveDir, info.Version))
            {
                Console.WriteLine("Keine Updates für inaktive Version.");
                StatusMessage = "Inaktive Version ist bereits aktuell.";
                return;
            }

            Console.WriteLine($"Update gefunden: {ReadLocalVersion(inactiveDir)} → {info.Version}");
            StatusMessage = $"Update gefunden: {ReadLocalVersion(inactiveDir)} → {info.Version}";

            await DownloadAndInstallAsync(inactiveDir, info);

            File.WriteAllText(AppConfig.ActiveFile, _inactive);
            StatusMessage = $"Version {_inactive} wurde auf {info.Version} aktualisiert und aktiviert.";
        }

        /// <summary>
        /// Validiert eine Version über das signierte Manifest und Datei-Hashes.
        /// </summary>
        public bool ValidateVersion(string folder)
        {
            LastValidationError = string.Empty;
            string manifest = Path.Combine(folder, "manifest.json");

            if (!File.Exists(manifest))
            {
                LastValidationError = $"Manifest nicht gefunden: {manifest}";
                Console.WriteLine(LastValidationError);
                return false;
            }

            string publicKey = new publicpem().pem;

            var verifier = new ManifestVerifier(publicKey);

            var result = verifier.TryVerifyManifest(manifest, folder, out var failureReason);
            LastValidationError = result ? string.Empty : failureReason;
            if (!result)
                Console.WriteLine($"Validierung fehlgeschlagen für {folder}: {failureReason}");
            return result;
        }

        /// <summary>
        /// Startet die aktive Version und wechselt bei Fehlern auf die inaktive Version.
        /// </summary>
        public bool StartWithFallback()
        {
            GetInactive();

            retucode = -5;
            StatusMessage = $"Starte aktive Version {_active}...";

            string activeFolder = _active == "A" ? AppConfig.VersionA : AppConfig.VersionB;
            string inactiveFolder = _inactive == "A" ? AppConfig.VersionA : AppConfig.VersionB;

            Console.WriteLine($"Starte aktive Version {_active}...");
            // 1. Prüfen ob aktive Version gültig ist
            if (ValidateVersion(activeFolder))
            {
                if (TryStart(activeFolder))
                {
                    StatusMessage = $"Aktive Version {_active} beendet mit Code {retucode}.";
                    return true;
                }

                StatusMessage = $"Aktive Version {_active} konnte nicht gestartet werden.";
            }
            else
            {
                StatusMessage = $"Aktive Version {_active} ungültig ({LastValidationError}). Versuche Fallback.";
            }

            Console.WriteLine("Aktive Version fehlerhaft! Fallback...");
            // 2. Fallback
            if (ValidateVersion(inactiveFolder))
            {
                File.WriteAllText(AppConfig.ActiveFile, _inactive);
                if (TryStart(inactiveFolder))
                {
                    StatusMessage = $"Fallback-Version {_inactive} beendet mit Code {retucode}.";
                    return true;
                }

                StatusMessage = $"Fallback-Version {_inactive} konnte nicht gestartet werden.";
            }
            else
            {
                StatusMessage = $"Fallback-Version {_inactive} ungültig ({LastValidationError}).";
            }

            Console.WriteLine("Beide Versionen beschädigt.");
            retucode = -20;
            StatusMessage = $"Keine gültige Version gefunden ({LastValidationError}). Installationsprogramm wird neu aufgebaut.";
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
            exe = Path.Combine(exe, "MeineApp.exe");
            try
            {
                var psi = new ProcessStartInfo(exe)
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
                retucode += -10;
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
