using System.IO;

namespace FoxholeLogiHub.App.Services;

/// <summary>Création/suppression du raccourci Bureau (.lnk vers l'exe courant, via WScript.Shell).</summary>
public static class DesktopShortcut
{
    private static string LnkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FoxholeLogiHub.lnk");

    public static bool Exists() => File.Exists(LnkPath);

    public static void Create()
    {
        string exe = Environment.ProcessPath!;
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell indisponible.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic lnk = shell.CreateShortcut(LnkPath);
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = Path.GetDirectoryName(exe);
            lnk.IconLocation = exe + ",0";
            lnk.Description = "FoxholeLogiHub — le QG logistique de votre régiment Foxhole";
            lnk.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    public static void Remove()
    {
        if (File.Exists(LnkPath))
            File.Delete(LnkPath);
    }
}
