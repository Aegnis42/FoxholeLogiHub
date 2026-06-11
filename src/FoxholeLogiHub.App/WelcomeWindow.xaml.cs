using System.Windows;
using FoxholeLogiHub.App.Services;
using Microsoft.Win32;

namespace FoxholeLogiHub.App;

/// <summary>
/// Écran de bienvenue affiché au PREMIER lancement après installation : raccourci Bureau
/// (le Setup n'en impose plus) et lancement avec Windows. Tout reste modifiable dans Paramètres.
/// </summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void OnGoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ChkDesktop.IsChecked == true)
                DesktopShortcut.Create();
        }
        catch { /* un raccourci raté ne bloque pas l'accueil */ }

        try
        {
            if (ChkStartup.IsChecked == true)
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                key.SetValue("FoxholeLogiHub", $"\"{Environment.ProcessPath}\"");
                var store = new Core.Services.SettingsStore();
                var s = store.Load();
                s.StartWithWindows = true;
                store.Save(s);
            }
        }
        catch { }

        Close();
    }
}
