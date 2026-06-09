using Microsoft.Win32;

namespace FoxholeLogiHub.Core.Steam;

/// <summary>Localise le dossier d'installation de Steam (registre, puis chemins par défaut).</summary>
public static class SteamLocator
{
    public static string? GetSteamPath()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p && Directory.Exists(p))
                    return p;
            }
            catch
            {
                // Registre inaccessible : on retombe sur les chemins par défaut.
            }
        }

        foreach (string candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                 })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
