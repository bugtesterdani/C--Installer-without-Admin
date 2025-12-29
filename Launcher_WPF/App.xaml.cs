using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;

namespace Launcher_WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var german = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = german;
            CultureInfo.CurrentUICulture = german;
            CultureInfo.DefaultThreadCurrentCulture = german;
            CultureInfo.DefaultThreadCurrentUICulture = german;

            base.OnStartup(e);
        }
    }

}
