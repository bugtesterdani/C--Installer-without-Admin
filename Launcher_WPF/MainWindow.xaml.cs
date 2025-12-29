using System.Text;
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

        public MainWindow()
        {
            InitializeComponent();
            update.CreateDirectories();
        }

        private void btnupdate(object sender, RoutedEventArgs e)
        {
            var t = Task.Run(StartApp);
            t.Wait();
            RunLabel.Content = update.retucode.ToString();
        }

        private async Task StartApp()
        {
            Console.WriteLine("=== MeineApp Launcher ===");

            // Update inactive version
            await update.UpdateInactiveVersionAsync();

            // Start active version with fallback
            if (!update.StartWithFallback())
            {
                // Lade frische Version herunter und versuche erneut
                Console.WriteLine("Fehler: Konnte keine Version starten.");
                await update.UpdateInactiveVersionAsync();
                update.StartWithFallback();
            }
        }
    }
}