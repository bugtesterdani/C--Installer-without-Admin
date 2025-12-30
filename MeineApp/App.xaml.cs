using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;

namespace MeineApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string channel = VersionInfo.GetChannel();
            string version = VersionInfo.GetVersion();

            Console.WriteLine($"Starte MeineApp {version} ({channel})");

            // einfache Heartbeat-Ausgabe
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    DateTime now = DateTime.UtcNow;
                    await Task.Delay(10000);
                    Console.WriteLine($"HEARTBEAT {now.AddMilliseconds(10000):O}");
                }
            });
        }
    }
}
