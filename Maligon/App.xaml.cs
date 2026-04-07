using System.Configuration;
using System.Data;
using System.Windows;

namespace Maligon
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        Checker checker = new Checker();
        ModelLoader loader = new ModelLoader();
        

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var window = new MainWindow(checker, loader);
            this.MainWindow = window;
            window.Show();
        }
    }

}
