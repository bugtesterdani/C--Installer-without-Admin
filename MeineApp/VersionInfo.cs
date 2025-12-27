using System;
using System.IO;

namespace MeineApp
{
    public static class VersionInfo
    {
        public static string GetChannel()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;

            if (exePath.Contains(@"\A\"))
                return "A";

            if (exePath.Contains(@"\B\"))
                return "B";

            return "Unknown";
        }

        public static string GetVersion()
        {
            string versionFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "version.txt");

            return File.Exists(versionFile)
                ? File.ReadAllText(versionFile).Trim()
                : "0.0.0";
        }
    }
}