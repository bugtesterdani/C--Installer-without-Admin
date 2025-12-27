using System;
using System.IO;
using System.Threading.Tasks;

namespace Launcher
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var updater = new Updater();

            Console.WriteLine("=== MeineApp Launcher ===");

            // Update inactive version
            await updater.UpdateInactiveVersionAsync();

            // Start active version with fallback
            if (!updater.StartWithFallback())
            {
                // Lade frische Version herunter und versuche erneut
                Console.WriteLine("Fehler: Konnte keine Version starten.");
                await updater.UpdateInactiveVersionAsync();
                updater.StartWithFallback();
            }
        }
    }
}
