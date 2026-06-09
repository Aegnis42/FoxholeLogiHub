namespace FoxholeLogiHub.Core.Services;

/// <summary>Emplacements des données locales de l'application (%APPDATA%\FoxholeLogiHub).</summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FoxholeLogiHub");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string AccountFile => Path.Combine(DataDirectory, "account.json");
}
