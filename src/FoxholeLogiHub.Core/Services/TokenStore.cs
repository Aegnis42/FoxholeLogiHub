namespace FoxholeLogiHub.Core.Services;

/// <summary>
/// Stocke le jeton d'authentification (JWT) localement.
/// Fichier dans %APPDATA%\FoxholeLogiHub (lisible seulement par l'utilisateur Windows).
/// </summary>
public sealed class TokenStore
{
    public string FilePath => Path.Combine(AppPaths.DataDirectory, "token.txt");

    public string? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        string token = File.ReadAllText(FilePath).Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    public void Save(string token) => File.WriteAllText(FilePath, token);

    public void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
