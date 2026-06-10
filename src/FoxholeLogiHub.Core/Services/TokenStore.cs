using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FoxholeLogiHub.Core.Services;

/// <summary>
/// Stocke le jeton d'authentification (JWT) localement, chiffré au repos via DPAPI
/// (lié au compte Windows — token.bin). Migre automatiquement l'ancien token.txt en clair.
/// Un jeton expiré est traité comme absent (l'app redemande la connexion Steam).
/// </summary>
public sealed class TokenStore
{
    public string FilePath => Path.Combine(AppPaths.DataDirectory, "token.bin");
    private string LegacyPath => Path.Combine(AppPaths.DataDirectory, "token.txt");

    public string? Load()
    {
        string? token = LoadRaw();
        if (token is null)
            return null;
        if (IsExpired(token))
        {
            Clear();
            return null;
        }
        return token;
    }

    public void Save(string token)
    {
        byte[] plain = Encoding.UTF8.GetBytes(token);
        byte[] data = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)
            : plain;
        File.WriteAllBytes(FilePath, data);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            if (File.Exists(LegacyPath))
                File.Delete(LegacyPath);
        }
        catch
        {
            // best-effort
        }
    }

    private string? LoadRaw()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                byte[] data = File.ReadAllBytes(FilePath);
                byte[] plain = OperatingSystem.IsWindows()
                    ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)
                    : data;
                string token = Encoding.UTF8.GetString(plain).Trim();
                return token.Length == 0 ? null : token;
            }

            // Migration : ancien jeton en clair (token.txt) → re-stocké chiffré.
            if (File.Exists(LegacyPath))
            {
                string token = File.ReadAllText(LegacyPath).Trim();
                if (token.Length == 0)
                    return null;
                Save(token);
                File.Delete(LegacyPath);
                return token;
            }
        }
        catch
        {
            // Fichier corrompu ou chiffré par un autre compte Windows : on repart de zéro.
            Clear();
        }
        return null;
    }

    /// <summary>
    /// Lit le claim exp du JWT (sans valider la signature — c'est le serveur qui fait foi)
    /// pour éviter de démarrer avec un jeton qu'on sait déjà périmé. Marge de 5 minutes.
    /// </summary>
    public static bool IsExpired(string token)
    {
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3)
                return false; // pas un JWT standard : on laisse le serveur trancher
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!doc.RootElement.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number)
                return false;
            return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) <= DateTimeOffset.UtcNow.AddMinutes(5);
        }
        catch
        {
            return false;
        }
    }
}
