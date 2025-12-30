using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Launcher_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Updater update = new Updater();
        private bool autorefresh_enabled = false;

        public MainWindow()
        {
            InitializeComponent();
            update.CreateDirectories();
            update.AppExited += OnAppExited;
            Task.Run(AutoRefresh);
        }

        private async void btnupdate(object sender, RoutedEventArgs e)
        {
            RunLabel.Content = "Update wird ausgeführt...";
            autorefresh_enabled = true;
            await StartApp();
            RunLabel.Content = update.StatusMessage;
        }

        private async Task StartApp()
        {
            Console.WriteLine("=== MeineApp Launcher ===");

            // Update inactive version
            await update.UpdateInactiveVersionAsync();

            // Start active version with fallback
            if (!await update.StartWithFallbackAsync())
            {
                // Lade frische Version herunter und versuche erneut
                Console.WriteLine("Fehler: Konnte keine Version starten.");
                await update.UpdateInactiveVersionAsync();
                await update.StartWithFallbackAsync();
            }
        }

        private async Task AutoRefresh()
        {
            if (autorefresh_enabled)
            {
                Dispatcher.Invoke(() =>
                {
                    RunLabel.Content = $"{update.StatusMessage}";
                });
            }
            await Task.Delay(100);
            await Task.Run(async () => { await Task.Delay(1); Task.Run(AutoRefresh); });
        }

        private void OnAppExited(int exitCode)
        {
            autorefresh_enabled = false;
            Dispatcher.Invoke(() =>
            {
                RunLabel.Content = $"{update.StatusMessage} (Code {exitCode})";
            });
        }
    }
}
