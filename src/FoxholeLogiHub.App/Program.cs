using System.Windows;
using Velopack;

namespace FoxholeLogiHub.App;

/// <summary>
/// Point d'entrée : Velopack DOIT s'exécuter avant WPF — il intercepte les lancements spéciaux
/// (install, mise à jour, désinstallation) faits par Setup.exe/Update.exe et sort immédiatement.
/// En exécution « lâche » (dev, dotnet run), il ne fait rien.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
