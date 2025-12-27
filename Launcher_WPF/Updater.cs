using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Launcher_WPF
{
    public class Updater
    {
        private readonly HttpClient _http = new HttpClient();
        private string _active;
        private string _inactive;
        public int retucode;

        public string GetActive()
        {
            if (!File.Exists(AppConfig.ActiveFile))
                File.WriteAllText(AppConfig.ActiveFile, "A");

            return File.ReadAllText(AppConfig.ActiveFile).Trim();
        }

        public void GetInactive()
        {
            _active = GetActive();
            _inactive = _active == "A" ? "B" : "A";
        }

        public async Task UpdateInactiveVersionAsync()
        {
            GetInactive();
            string targetDir = _active == "A" ? AppConfig.VersionA : AppConfig.VersionB;

            Console.WriteLine($"Prüfe Updates für Version {_active}...");

            var json = await _http.GetStringAsync(AppConfig.UpdateInfoUrl);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json);

            string versionFile = Path.Combine(targetDir, "version.txt");
            string localVersion = File.Exists(versionFile)
                ? File.ReadAllText(versionFile).Trim()
                : "0.0.0";

            if (localVersion == info.Version)
            {
                Console.WriteLine("Keine Updates für aktive Version.");
                return;
            }

            targetDir = _inactive == "A" ? AppConfig.VersionA : AppConfig.VersionB;
            Console.WriteLine($"Aktive Version veraltet. Prüfe Updates für inaktive Version {_inactive}...");

            json = await _http.GetStringAsync(AppConfig.UpdateInfoUrl);
            info = JsonSerializer.Deserialize<UpdateInfo>(json);

            versionFile = Path.Combine(targetDir, "version.txt");
            localVersion = File.Exists(versionFile)
                ? File.ReadAllText(versionFile).Trim()
                : "0.0.0";

            if (localVersion == info.Version)
            {
                Console.WriteLine("Keine Updates für inaktive Version.");
                return;
            }

            Console.WriteLine($"Update gefunden: {localVersion} → {info.Version}");

            var data = await _http.GetByteArrayAsync(info.Url);
            File.WriteAllBytes(AppConfig.TempZip, data);

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(AppConfig.TempZip, targetDir);

            File.WriteAllText(versionFile, info.Version);

            Console.WriteLine($"Version {_inactive} aktualisiert.");

            File.WriteAllText(AppConfig.ActiveFile, _inactive);
        }

        public bool ValidateVersion(string folder)
        {
            string manifest = Path.Combine(folder, "manifest.json");

            if (!File.Exists(manifest))
                return false;

            string publicKey = new publicpem().pem;

            var verifier = new ManifestVerifier(publicKey);

            return verifier.VerifyManifest(manifest, folder);
        }

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
        public string Version { get; set; }
        public string Url { get; set; }
    }
}
