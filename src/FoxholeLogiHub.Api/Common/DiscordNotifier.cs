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
