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
    /// <summary>Premier lancement après installation → écran de bienvenue (raccourci Bureau…).</summary>
    public static bool IsFirstRun { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(_ => IsFirstRun = true)
            .Run();

        // Forçage pour tester l'écran de bienvenue en dev.
        if (Environment.GetEnvironmentVariable("FLH_FIRSTRUN") == "1")
            IsFirstRun = true;

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
