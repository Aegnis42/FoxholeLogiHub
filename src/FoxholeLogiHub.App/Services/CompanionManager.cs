using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

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

    private static bool IsListening()
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", Port, null, null);
            bool ok = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
            if (ok) client.EndConnect(result);
            return ok;
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
