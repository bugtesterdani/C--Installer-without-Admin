using System.IO;

namespace Launcher
{
    public static class AppConfig
    {
        public static string BasePath =>
            @"C:\ProgramData\MeineFirma\MeineApp";

        public static string ActiveFile =>
            Path.Combine(BasePath, "active.txt");

        public static string VersionA =>
            Path.Combine(BasePath, "A");

        public static string VersionB =>
            Path.Combine(BasePath, "B");

        public static string UpdateInfoUrl =>
            "http://localhost:8000/update.json";

        public static string TempZip =>
            Path.Combine(Path.GetTempPath(), "MeineApp_Update.zip");
    }
}
