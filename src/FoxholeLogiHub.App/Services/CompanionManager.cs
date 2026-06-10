using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace FoxholeLogiHub.App.Services;

/// <summary>Lance/arrête le companion FIR (fic.exe) en serveur HTTP local pour la reconnaissance.</summary>
public sealed class CompanionManager : IDisposable
{
    private const int Port = 8099;
    private Process? _process;

    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public bool Available => File.Exists(FicPath);

    private static string FicPath => Path.Combine(AppContext.BaseDirectory, "fic.exe");

    public void EnsureStarted()
    {
        if (!Available || IsListening())
            return;
        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = FicPath,
                Arguments = $"http-server 127.0.0.1:{Port}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch
        {
            // Companion indisponible : l'import par capture sera simplement inactif.
        }
    }

    // Sonde HTTP (pas un simple test TCP) : toute réponse HTTP prouve qu'un serveur écoute —
    // un autre process qui occuperait juste le port ne répondrait pas en HTTP.
    private static bool IsListening()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(600) };
            using var resp = http.Send(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{Port}/"));
            return true; // même un 404 = serveur HTTP présent
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill();
        }
        catch
        {
            // ignore
        }
    }
}
