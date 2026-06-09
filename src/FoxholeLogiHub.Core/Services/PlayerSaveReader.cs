using FoxholeLogiHub.Core.Gvas;
using FoxholeLogiHub.Core.Models;

namespace FoxholeLogiHub.Core.Services;

/// <summary>
/// Lit le fichier de sauvegarde Foxhole et le mappe vers le domaine <see cref="PlayerSave"/>.
/// </summary>
public sealed class PlayerSaveReader
{
    private readonly GvasParser _parser = new();

    /// <summary>Lit et mappe le .sav situé à <paramref name="path"/>.</summary>
    public PlayerSave Read(string path)
    {
        GvasSaveGame save = _parser.Parse(path);
        string? steamId = Path.GetFileNameWithoutExtension(path);
        return Map(save, steamId);
    }

    /// <summary>
    /// Localise automatiquement le .sav du joueur et le lit.
    /// Retourne null si aucun fichier n'est trouvé.
    /// </summary>
    public PlayerSave? ReadCurrentPlayer()
    {
        string? path = SaveGameLocator.GetPlayerSavePath();
        return path is null ? null : Read(path);
    }

    private static PlayerSave Map(GvasSaveGame save, string? steamId)
    {
        GvasStruct root = save.Root;

        var loadouts = new List<Loadout>();
        if (root.Find("LoadoutSaveData")?.Value is GvasStruct lsd &&
            lsd.Find("LoadoutDataC")?.Value is GvasArray loadoutArray)
        {
            foreach (GvasStruct entry in loadoutArray.Items.OfType<GvasStruct>())
                loadouts.Add(MapLoadout(entry));
        }

        var wars = new List<string>();
        if (root.Find("WarIdsJoinedList")?.Value is GvasArray warArray)
            wars = warArray.Items.OfType<string>().Distinct().ToList();

        return new PlayerSave
        {
            SteamId = steamId,
            Faction = ParseFaction(root.GetValue("LastFactionId") as string),
            LastServer = root.GetValue("LastJoinedServerName") as string,
            LastShardId = root.GetValue("LastShardId") is int shard ? shard : 0,
            Language = StripEnumPrefix(root.GetValue("ClientLanguage") as string),
            WarsJoined = wars,
            Loadouts = loadouts,
        };
    }

    private static Loadout MapLoadout(GvasStruct s)
    {
        var loadout = new Loadout { Name = s.GetValue("LoadoutName") as string ?? "(sans nom)" };

        if (s.Find("EquipmentItems")?.Value is GvasArray eq)
            foreach (GvasStruct item in eq.Items.OfType<GvasStruct>())
                loadout.Equipment.Add(MapItem(item));

        if (s.Find("BackpackItems")?.Value is GvasArray bp)
            foreach (GvasStruct item in bp.Items.OfType<GvasStruct>())
                loadout.Backpack.Add(MapItem(item));

        return loadout;
    }

    private static LoadoutItem MapItem(GvasStruct s) => new()
    {
        CodeName = s.GetValue("CodeName") as string ?? "?",
        Quantity = ToInt(s.GetValue("Quantity")),
        Slot = StripEnumPrefix(s.GetValue("EquipmentSlot") as string),
    };

    private static Faction ParseFaction(string? raw)
    {
        string? value = StripEnumPrefix(raw);
        return value switch
        {
            "Wardens" => Faction.Wardens,
            "Colonials" => Faction.Colonials,
            _ => Faction.Unknown,
        };
    }

    /// <summary>Transforme "EFactionId::Wardens" en "Wardens".</summary>
    private static string? StripEnumPrefix(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;
        int idx = raw.LastIndexOf("::", StringComparison.Ordinal);
        return idx >= 0 ? raw[(idx + 2)..] : raw;
    }

    private static int ToInt(object? value) => value switch
    {
        null => 0,
        int i => i,
        byte b => b,
        sbyte sb => sb,
        short s => s,
        ushort us => us,
        long l => (int)l,
        _ => 0,
    };
}
