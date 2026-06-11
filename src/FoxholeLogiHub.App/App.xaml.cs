using System.Diagnostics;
using System.Windows;

namespace FoxholeLogiHub.App;

/// <summary>
/// Interaction logic for App.xaml — installe un filet de sécurité : une exception non gérée
/// (typiquement un `async void` gestionnaire d'événement qui lève) est journalisée et avalée
/// au lieu de tuer l'application.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Debug.WriteLine($"[UI] Exception non gérée : {args.Exception}");
            MessageBox.Show(
                "Une erreur inattendue est survenue. L'application reste ouverte ; relance l'action.",
                "FoxholeLogiHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true; // ne pas crasher l'app
        };

        // Tâches orphelines (Task non attendue qui lève) : journalisées, jamais fatales.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Debug.WriteLine($"[Task] Exception non observée : {args.Exception}");
            args.SetObserved();
        };
    }
}
