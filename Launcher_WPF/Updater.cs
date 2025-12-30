using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using System.Threading;
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

        /// <summary>
        /// Wird ausgelöst, wenn der gestartete Prozess beendet wurde.
        /// </summary>
        public event Action<int>? AppExited;

        private CancellationTokenSource? _heartbeatCts;
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(15);
        private DateTime _lastHeartbeat = DateTime.MinValue;
        public TimeSpan LastHeartbeatPing { get; private set; } = TimeSpan.Zero;

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
                LastValidationError = $"Validierung fehlgeschlagen für {folder}: {failureReason}";
            return result;
        }

        /// <summary>
        /// Startet die aktive Version und wechselt bei Fehlern auf die inaktive Version.
        /// </summary>
        public async Task<bool> StartWithFallbackAsync()
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
                if (await TryStartAsync(activeFolder))
                {
                    StatusMessage = $"Aktive Version {_active} wurde gestartet.";
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
                if (await TryStartAsync(inactiveFolder))
                {
                    StatusMessage = $"Fallback-Version {_inactive} wurde gestartet.";
                    return true;
                }

                StatusMessage = $"Fallback-Version {_inactive} konnte nicht gestartet werden.";
            }
            else
            {
                StatusMessage = $"Fallback-Version {_inactive} ungültig ({LastValidationError}).";
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
            string localVersion = ReadLocalVersion(targetDir);
            if (localVersion.Split('.').Count() < 4 || remoteVersion.Split('.').Count() < 4)
                return false;
            
            int compared = checktwocompare(localVersion, remoteVersion, 0);
            if (compared == 2)
                return true;                // Ist aktueller
            else if (compared == 0)
                return false;               // Ist veraltet
            
            compared = checktwocompare(localVersion, remoteVersion, 1);
            if (compared == 2)
                return true;                // Ist aktueller
            else if (compared == 0)
                return false;               // Ist veraltet
            
            compared = checktwocompare(localVersion, remoteVersion, 2);
            if (compared == 2)
                return true;                // Ist aktueller
            else if (compared == 0)
                return false;               // Ist veraltet

            compared = checktwocompare(localVersion, remoteVersion, 3);
            if (compared == 0)
                return false;               // Ist veraltet
            else
                return true;                // Ist aktueller oder gleich
        }

        private static int checktwocompare(string localVersion, string remoteVersion, int index)
        {
            int num1 = Convert.ToInt16(localVersion.Split('.')[index]);
            int num2 = Convert.ToInt16(remoteVersion.Split('.')[index]);
            if (num1 > num2)
                return 2;
            else if (num1 == num2)
                return 1;
            else
                return 0;
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
        private Task<bool> TryStartAsync(string exe)
        {
            exe = Path.Combine(exe, "MeineApp.exe");
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                if (proc == null)
                    return Task.FromResult(false);

                // PIPE-Kommunikation über stdout/stdin
                _heartbeatCts?.Cancel();
                _heartbeatCts = new CancellationTokenSource();
                _lastHeartbeat = DateTime.UtcNow;
                var token = _heartbeatCts.Token;

                var heartbeatTask = Task.Run(() => MonitorHeartbeatAsync(proc, token), token);
                var readOutputTask = Task.Run(() => ReadPipeAsync(proc, token), token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await proc.WaitForExitAsync(token);
                        retucode = proc.ExitCode;
                    }
                    catch (OperationCanceledException)
                    {
                        // beabsichtigter Abbruch bei Cancellation
                    }
                    finally
                    {
                        _heartbeatCts.Cancel();
                        await Task.WhenAll(Task.WhenAll(heartbeatTask, readOutputTask).ContinueWith(_ => Task.CompletedTask));
                        StatusMessage = $"Anwendung beendet. ({retucode})";
                        AppExited?.Invoke(retucode);
                    }
                }, CancellationToken.None);

                return Task.FromResult(true);
            }
            catch
            {
                retucode += -10;
                return Task.FromResult(false);
            }
        }

        private async Task MonitorHeartbeatAsync(Process proc, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !proc.HasExited)
                {
                    await Task.Delay(_heartbeatInterval, token);
                    if (DateTime.UtcNow - _lastHeartbeat > _heartbeatTimeout)
                    {
                        Console.WriteLine("Heartbeat: Anwendung reagiert nicht mehr.");
                        if (token.IsCancellationRequested)
                            return;
                        StatusMessage = "Heartbeat: Anwendung reagiert nicht mehr.";
                        await Task.Delay(_heartbeatInterval * 2, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // erwartet bei Stop
            }
        }

        private async Task ReadPipeAsync(Process proc, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !proc.HasExited)
                {
                    var line = await proc.StandardOutput.ReadLineAsync(token);
                    if (token.IsCancellationRequested)
                        return;
                    if (line == null)
                        break;

                    if (line.StartsWith("HEARTBEAT", StringComparison.OrdinalIgnoreCase))
                    {
                        var now = DateTime.UtcNow;
                        if (TryParseHeartbeatTimestamp(line, out var sentUtc))
                        {
                            LastHeartbeatPing = now - sentUtc;
                            Console.WriteLine($"Heartbeat empfangen (Ping: {LastHeartbeatPing.TotalMilliseconds:F0} ms)");
                            if (token.IsCancellationRequested)
                                return;
                            StatusMessage = $"Heartbeat OK (Ping: {LastHeartbeatPing.TotalMilliseconds:F0} ms)";
                        }
                        else
                        {
                            LastHeartbeatPing = TimeSpan.Zero;
                            Console.WriteLine("Heartbeat empfangen.");
                            if (token.IsCancellationRequested)
                                return;
                            StatusMessage = "Heartbeat OK";
                        }

                        _lastHeartbeat = now;
                        continue;
                    }

                    Console.WriteLine($"APP: {line}");
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
            {
                // Ignorieren, wenn der Stream geschlossen wurde oder Cancellation erfolgte
            }
        }

        private static bool TryParseHeartbeatTimestamp(string line, out DateTime heartbeatTimeUtc)
        {
            heartbeatTimeUtc = DateTime.MinValue;
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;

            return DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out heartbeatTimeUtc);
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
