using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Fonds de carte, en cache disque (%APPDATA%\FoxholeLogiHub\maptiles). Deux sources :
/// 1. Pack HD (2048×1776, JPEG) extrait du mod « Improved Map Mod » de RDZ, hébergé en
///    release d'assets GitHub — un seul téléchargement (~20 Mo) pour les 53 hexagones.
/// 2. Repli : tuiles TGA officielles du dépôt clapfoot/warapi (1024×888), converties en PNG
///    une par une (~190 Mo) — utilisé si le pack HD est indisponible.
/// </summary>
public sealed class MapTileService
{
    // Release dédiée aux assets, marquée « prerelease » : invisible des mises à jour Velopack
    // (GithubSource ignore les préversions) et du badge « latest » du README.
    private const string HdPackUrl =
        "https://github.com/Aegnis42/FoxholeLogiHub/releases/download/assets-maptiles-hd-v1/maptiles-hd.zip";

    private static readonly HttpClient Http = CreateHttp();
    private readonly string _dir = Path.Combine(AppPaths.DataDirectory, "maptiles");
    private readonly SemaphoreSlim _gate = new(3); // téléchargements simultanés
    private readonly object _hdLock = new();
    private Dictionary<string, string>? _urls;     // nom de carte normalisé → download_url
    private Task<bool>? _hdPack;                   // téléchargement du pack HD, partagé par toutes les tuiles

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxholeLogiHub");
        return http;
    }

    private string PngPath(string map) => Path.Combine(_dir, map + ".png");
    private string HdPath(string map) => Path.Combine(_dir, "hd", Norm(map) + ".jpg");

    /// <summary>Garantit la tuile en cache (télécharge si besoin). Renvoie le chemin image ou null.</summary>
    public async Task<string?> EnsureAsync(string map)
    {
        string hd = HdPath(map);
        if (File.Exists(hd))
            return hd;

        Task<bool> hdTask;
        lock (_hdLock)
            hdTask = _hdPack ??= DownloadHdPackAsync();
        if (await hdTask && File.Exists(hd))
            return hd;

        // Repli officiel warapi, tuile par tuile.
        string png = PngPath(map);
        if (File.Exists(png))
            return png;

        await _gate.WaitAsync();
        try
        {
            if (File.Exists(png))
                return png;
            Directory.CreateDirectory(_dir);

            var urls = await GetUrlsAsync();
            if (urls is null || !urls.TryGetValue(Norm(map), out var url) || url.Length == 0)
                return null;

            byte[] tga = await Http.GetByteArrayAsync(url);
            byte[]? pngBytes = await Task.Run(() => TgaToPng(tga));
            if (pngBytes is null)
                return null;
            await File.WriteAllBytesAsync(png, pngBytes);
            return png;
        }
        catch
        {
            return null; // pas de tuile = la carte reste vectorielle, jamais bloquant
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Télécharge et extrait le pack de fonds HD (~20 Mo) dans maptiles\hd. Extraction dans un
    /// dossier temporaire puis renommage : un pack à moitié extrait n'est jamais pris pour valide.
    /// </summary>
    private async Task<bool> DownloadHdPackAsync()
    {
        string hdDir = Path.Combine(_dir, "hd");
        try
        {
            if (Directory.Exists(hdDir) && Directory.EnumerateFiles(hdDir, "*.jpg").Any())
                return true;

            byte[] zip = await Http.GetByteArrayAsync(HdPackUrl);
            string tmp = hdDir + ".tmp";
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            using (var ms = new MemoryStream(zip))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                archive.ExtractToDirectory(tmp);
            Directory.Move(tmp, hdDir);
            return true;
        }
        catch
        {
            // Hors ligne ou GitHub indisponible : repli warapi, nouvelle tentative à la prochaine session.
            return Directory.Exists(hdDir) && Directory.EnumerateFiles(hdDir, "*.jpg").Any();
        }
    }

    /// <summary>Charge un PNG en ImageSource figée, à la résolution demandée (256 = vue monde, 1024 = zoom).</summary>
    public static ImageSource? LoadImage(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // Liste des fichiers du dépôt officiel (la casse des noms varie : « MapDeadlandsHex.TGA »
    // pour « DeadLandsHex ») → correspondance par nom normalisé, une seule requête par session.
    private async Task<Dictionary<string, string>?> GetUrlsAsync()
    {
        if (_urls is not null)
            return _urls;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                "https://api.github.com/repos/clapfoot/warapi/contents/Images/Maps"));
            var map = new Dictionary<string, string>();
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string name = f.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("Map", StringComparison.Ordinal)
                    || !name.EndsWith(".TGA", StringComparison.OrdinalIgnoreCase))
                    continue;
                map[Norm(name[3..^4])] = f.GetProperty("download_url").GetString() ?? "";
            }
            _urls = map;
            return map;
        }
        catch
        {
            return null;
        }
    }

    // Suffixe « Hex » ignoré : l'API War dit « MarbanHollow » mais la tuile officielle s'appelle
    // « MapMarbanHollowHex.TGA ».
    private static string Norm(string s)
    {
        string n = new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return n.EndsWith("hex", StringComparison.Ordinal) ? n[..^3] : n;
    }

    /// <summary>Décode un TGA truecolor non compressé (type 2, 24/32 bpp) et le réencode en PNG.</summary>
    private static byte[]? TgaToPng(byte[] tga)
    {
        if (tga.Length < 18 || tga[2] != 2)
            return null;
        int idLen = tga[0];
        int width = BitConverter.ToUInt16(tga, 12);
        int height = BitConverter.ToUInt16(tga, 14);
        int bpp = tga[16] / 8;
        bool topDown = (tga[17] & 0x20) != 0;
        int offset = 18 + idLen;
        // Plafond AVANT tout calcul de taille : les tuiles Foxhole font 1024×888 ; 4096 est large.
        // Sans ce garde, width*height*bpp déborde l'int (dim. max 65535) et l'allocation est aberrante.
        if (bpp is not (3 or 4) || width is <= 0 or > 4096 || height is <= 0 or > 4096
            || tga.Length < offset + (long)width * height * bpp)
            return null;

        // BGRA top-down attendu par WPF — on retourne l'image si l'origine TGA est en bas.
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int row = 0; row < height; row++)
        {
            int srcRow = topDown ? row : height - 1 - row;
            int src = offset + srcRow * width * bpp;
            int dst = row * stride;
            for (int x = 0; x < width; x++)
            {
                pixels[dst + 0] = tga[src + 0];
                pixels[dst + 1] = tga[src + 1];
                pixels[dst + 2] = tga[src + 2];
                pixels[dst + 3] = bpp == 4 ? tga[src + 3] : (byte)255;
                src += bpp;
                dst += 4;
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
