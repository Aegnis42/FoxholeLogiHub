using System.Security.Cryptography;

namespace FoxholeLogiHub.Api;

/// <summary>Génère des codes d'ami courts, lisibles (base32 sans caractères ambigus).</summary>
public static class FriendCodeGenerator
{
    // Pas de 0/O/1/I/L pour éviter les confusions à la lecture.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 6)
    {
        Span<char> chars = stackalloc char[length];
        for (int i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Normalise une saisie utilisateur (majuscules, sans tirets ni espaces).</summary>
    public static string Normalize(string input) =>
        new string((input ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
