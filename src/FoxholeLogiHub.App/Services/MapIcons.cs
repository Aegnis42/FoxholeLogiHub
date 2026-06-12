using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Icônes officielles de la carte (warapi, converties en PNG dans Data/mapicons) : blanches sur
/// fond transparent, conçues pour être TEINTÉES par faction (OpacityMask côté XAML).
/// </summary>
public static class MapIcons
{
    private static readonly string Dir =
        Path.Combine(AppContext.BaseDirectory, "Data", "mapicons");

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    /// <summary>Icône d'une structure/ressource par iconType War API (null si inconnue → repli emoji).</summary>
    public static ImageSource? ForStruct(int iconType) => Load(iconType.ToString());

    /// <summary>Icône de base de ville par tier (1-3) ; tier hors plage → tier 1.</summary>
    public static ImageSource? ForTown(int tier) => Load($"town{Math.Clamp(tier, 1, 3)}");

    private static ImageSource? Load(string name) => Cache.GetOrAdd(name, n =>
    {
        string path = Path.Combine(Dir, n + ".png");
        if (!File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 48; // affichées en 14-22 px écran
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    });
}
