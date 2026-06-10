namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Position axiale de chaque hexagone du monde Foxhole (53 régions, hexagones « flat-top »).
/// Convention : centre = (1.5·P, √3·(Q + P/2)) en taille unitaire — validée contre la géographie
/// du jeu (Basin Sionnach au nord, Kalokai au sud, Callahan's Passage plein nord de Deadlands).
/// Source des coordonnées : github.com/notbadjon/foxhole-hexes (hexes.json).
/// </summary>
public static class HexLayout
{
    public static readonly IReadOnlyDictionary<string, (int Q, int P)> ByMap = new Dictionary<string, (int Q, int P)>
    {
        ["AcrithiaHex"] = (2, 1),
        ["AllodsBightHex"] = (0, 2),
        ["AshFieldsHex"] = (3, -2),
        ["BasinSionnachHex"] = (-3, 0),
        ["CallahansPassageHex"] = (-1, 0),
        ["CallumsCapeHex"] = (-1, -2),
        ["ClahstraHex"] = (-1, 2),
        ["ClansheadValleyHex"] = (-3, 2),
        ["DeadLandsHex"] = (0, 0),
        ["DrownedValeHex"] = (0, 1),
        ["EndlessShoreHex"] = (-1, 3),
        ["FarranacCoastHex"] = (1, -3),
        ["FishermansRowHex"] = (2, -4),
        ["GodcroftsHex"] = (-3, 4),
        ["GreatMarchHex"] = (2, 0),
        ["GutterHex"] = (1, -4),
        ["HeartlandsHex"] = (2, -1),
        ["HowlCountyHex"] = (-3, 1),
        ["KalokaiHex"] = (3, 0),
        ["KingsCageHex"] = (1, -2),
        ["KuuraStrandHex"] = (0, -4),
        ["LinnMercyHex"] = (0, -1),
        ["LochMorHex"] = (1, -1),
        ["LykosIsleHex"] = (-3, 5),
        ["MarbanHollowHex"] = (-1, 1),
        ["MooringCountyHex"] = (-1, -1),
        ["MorgensCrossingHex"] = (-3, 3),
        ["NevishLineHex"] = (0, -3),
        ["OarbreakerHex"] = (3, -5),
        ["OlavisWakeHex"] = (2, -6),
        ["OnyxHex"] = (0, 4),
        ["OriginHex"] = (3, -3),
        ["PalantineBermHex"] = (2, -5),
        ["PariPeakHex"] = (1, -5),
        ["PipersEnclaveHex"] = (-2, 6),
        ["ReachingTrailHex"] = (-2, 0),
        ["ReaversPassHex"] = (0, 3),
        ["RedRiverHex"] = (3, -1),
        ["SableportHex"] = (2, -2),
        ["ShackledChasmHex"] = (1, 1),
        ["SpeakingWoodsHex"] = (-2, -1),
        ["StemaLandingHex"] = (3, -4),
        ["StlicanShelfHex"] = (-2, 3),
        ["StonecradleHex"] = (0, -2),
        ["TempestIslandHex"] = (-2, 4),
        ["TerminusHex"] = (1, 2),
        ["TheFingersHex"] = (-2, 5),
        ["TyrantFoothillsHex"] = (-1, 5),
        ["UmbralWildwoodHex"] = (1, 0),
        ["ViperPitHex"] = (-2, 1),
        ["WeatheredExpanseHex"] = (-2, 2),
        ["WestgateHex"] = (2, -3),
        ["WrestaHex"] = (-1, 4),
    };
}
