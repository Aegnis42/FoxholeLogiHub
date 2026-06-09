using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Pilote la connexion « Sign in through Steam » : ouvre la page Steam dans le navigateur,
/// écoute sur une boucle locale (loopback) le retour du serveur, et récupère le jeton JWT.
/// Utilise un TcpListener brut (pas d'ACL/admin requis, contrairement à HttpListener).
/// </summary>
public sealed class SteamAuthService
{
    /// <summary>Lance la connexion et retourne le JWT, ou null si annulé/expiré.</summary>
    public async Task<string?> LoginAsync(string apiBaseUrl, CancellationToken ct = default)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int listenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = $"http://127.0.0.1:{listenPort}/";
            string loginUrl = $"{apiBaseUrl.TrimEnd('/')}/auth/steam/login?redirect={Uri.EscapeDataString(redirect)}";

            OpenBrowser(loginUrl);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            using TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token);
            using NetworkStream stream = client.GetStream();

            var buffer = new byte[8192];
            int read = await stream.ReadAsync(buffer, timeout.Token);
            string request = Encoding.UTF8.GetString(buffer, 0, read);
            string? token = ExtractToken(request);

            await WriteResponseAsync(stream, token is not null, ct);
            return token;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string? ExtractToken(string httpRequest)
    {
        const string marker = "?token=";
        int idx = httpRequest.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        int start = idx + marker.Length;
        int end = httpRequest.IndexOfAny(new[] { ' ', '&', '\r', '\n' }, start);
        if (end < 0)
            end = httpRequest.Length;

        string value = httpRequest[start..end];
        return string.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, bool ok, CancellationToken ct)
    {
        string message = ok
            ? "<h2>Connexion Steam réussie ✓</h2><p>Tu peux fermer cet onglet et revenir à FoxholeLogiHub.</p>"
            : "<h2>Échec de la connexion</h2><p>Réessaie depuis l'application.</p>";
        string html =
            "<!doctype html><html><head><meta charset='utf-8'><title>FoxholeLogiHub</title></head>" +
            "<body style=\"font-family:Segoe UI,sans-serif;background:#1b1e24;color:#e6e6e6;text-align:center;padding-top:60px\">" +
            message + "</body></html>";

        byte[] body = Encoding.UTF8.GetBytes(html);
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(headers), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
