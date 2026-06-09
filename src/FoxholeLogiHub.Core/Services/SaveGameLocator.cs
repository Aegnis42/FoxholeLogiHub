using System.Text;

namespace FoxholeLogiHub.Core.Services;

/// <summary>
/// Localise les fichiers de sauvegarde Foxhole dans le profil de l'utilisateur
/// (%LOCALAPPDATA%\Foxhole\Saved\SaveGames).
/// </summary>
public static class SaveGameLocator
{
    /// <summary>Dossier des sauvegardes, ou null s'il n'existe pas.</summary>
    public static string? GetSaveDirectory()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(local, "Foxhole", "Saved", "SaveGames");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Steam ID du joueur, lu depuis UserData.sav (int32 longueur + chaîne ASCII).</summary>
    public static string? GetSteamId()
    {
        string? dir = GetSaveDirectory();
        if (dir is null)
            return null;

        string userData = Path.Combine(dir, "UserData.sav");
        if (!File.Exists(userData))
            return null;

        byte[] bytes = File.ReadAllBytes(userData);
        if (bytes.Length < 4)
            return null;

        int len = BitConverter.ToInt32(bytes, 0);
        if (len <= 0 || bytes.Length < 4 + len)
            return null;

        return Encoding.ASCII.GetString(bytes, 4, len).TrimEnd('\0');
    }

    /// <summary>Chemin du .sav du joueur (&lt;steamid&gt;.sav), ou null si introuvable.</summary>
    public static string? GetPlayerSavePath()
    {
        string? dir = GetSaveDirectory();
        if (dir is null)
            return null;

        string? steamId = GetSteamId();
        if (steamId is not null)
        {
            string path = Path.Combine(dir, steamId + ".sav");
            if (File.Exists(path))
                return path;
        }

        // Repli : premier fichier <numérique>.sav du dossier.
        return Directory.EnumerateFiles(dir, "*.sav")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).All(char.IsDigit));
    }
}
