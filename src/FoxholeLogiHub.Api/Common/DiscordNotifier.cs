using System.Net;

namespace FoxholeLogiHub.Api.Common;

/// <summary>
/// Envoi de messages vers le webhook Discord d'un régiment. Toujours en meilleur effort :
/// fire-and-forget, jamais bloquant ni fatal pour la requête appelante. Un seul retry sur 429.
/// </summary>
public sealed class DiscordNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ILogger<DiscordNotifier> _log;

    public DiscordNotifier(ILogger<DiscordNotifier> log) => _log = log;

    /// <summary>Dev/tests uniquement (config AllowAnyWebhookUrl=1) : autorise une URL non-Discord.</summary>
    public static bool AllowAnyUrl;

    /// <summary>
    /// Valide/normalise le tag de rôle voulu par le CHEF (seule mention autorisée à passer) :
    /// "" = aucun ; un ID numérique devient &lt;@&amp;id&gt; ; @everyone/@here et &lt;@&amp;id&gt; passent tels quels.
    /// Renvoie null si le format n'est pas reconnu.
    /// </summary>
    public static string? NormalizeRoleTag(string? input)
    {
        string tag = (input ?? "").Trim();
        if (tag.Length == 0)
            return "";
        if (tag is "@everyone" or "@here")
            return tag;
        if (tag.Length is >= 5 and <= 25 && tag.All(char.IsAsciiDigit))
            return $"<@&{tag}>";
        if (System.Text.RegularExpressions.Regex.IsMatch(tag, @"^<@&\d{5,25}>$"))
            return tag;
        return null;
    }

    /// <summary>Préfixe un message par la mention du régiment (volontairement NON neutralisée — posée par le chef).</summary>
    public static string Tagged(string roleTag, string message) =>
        string.IsNullOrEmpty(roleTag) ? message : $"{roleTag} {message}";

    /// <summary>
    /// Neutralise un texte fourni par l'utilisateur (nom de stockpile, titre de demande…) avant
    /// insertion dans un message Discord : pas de mention de masse (@everyone/@here/&lt;@id&gt;), pas de
    /// saut de ligne ni de markdown qui casserait la mise en forme. Tronqué à 80 caractères.
    /// </summary>
    public static string Safe(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new System.Text.StringBuilder(text.Length);
        bool lastSpace = false;
        foreach (char c in text)
        {
            // Saut de ligne, tabulation, mentions (@, <…>) et markdown (`*_~|\) → espace.
            bool danger = c is '\n' or '\r' or '\t' or '@' or '`' or '*' or '_' or '~' or '<' or '>' or '|' or '\\';
            char ch = danger ? ' ' : c;
            if (ch == ' ')
            {
                if (!lastSpace) sb.Append(' ');   // fusionne les espaces consécutifs
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        string s = sb.ToString().Trim();
        return s.Length > 80 ? s[..80] + "…" : s;
    }

    /// <summary>Vrai si l'URL ressemble à un webhook Discord (on n'envoie jamais ailleurs).</summary>
    public static bool LooksValid(string url) =>
        (AllowAnyUrl && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        || url.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://canary.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);

    public void Send(string webhookUrl, string content)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl) || !LooksValid(webhookUrl) || string.IsNullOrWhiteSpace(content))
            return;
        string text = content.Length > 1900 ? content[..1900] + "…" : content;
        _ = Task.Run(async () =>
        {
            try
            {
                using var first = await Http.PostAsJsonAsync(webhookUrl, new { content = text });
                if (first.StatusCode == (HttpStatusCode)429)
                {
                    await Task.Delay(1500);
                    using var retry = await Http.PostAsJsonAsync(webhookUrl, new { content = text });
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Webhook Discord en échec : {Message}", ex.Message);
            }
        });
    }
}
