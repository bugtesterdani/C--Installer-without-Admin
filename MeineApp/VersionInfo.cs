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
            Assembly entryAssembly = Assembly.GetEntryAssembly();  
            if (entryAssembly != null)
            {
                // Get the path to the entry assembly (e.g., "C:\Program Files\MyApp\MyApp.exe")
                string exePath = entryAssembly.Location;
                
                // Read version info from the executable file
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
                string fileVersion = fileVersionInfo.FileVersion;
                Console.WriteLine($"File Version: {fileVersion}"); // Output: "1.0.0.0"
                return fileVersion;
            }
            else
            {
                return "0.0.0.0";
            }
        }
    }
}
