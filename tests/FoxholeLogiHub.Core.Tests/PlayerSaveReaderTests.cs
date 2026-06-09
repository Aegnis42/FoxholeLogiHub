using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace FoxholeLogiHub.Core.Tests;

/// <summary>
/// Tests d'intégration contre le vrai fichier de sauvegarde installé sur la machine.
/// Ignorés (Skip) si Foxhole n'est pas installé / pas de .sav présent.
/// </summary>
public sealed class PlayerSaveReaderTests
{
    private readonly ITestOutputHelper _output;

    public PlayerSaveReaderTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void Reads_real_save_and_extracts_player_data()
    {
        string? path = SaveGameLocator.GetPlayerSavePath();
        Skip.If(path is null, "Aucun fichier .sav Foxhole trouvé sur cette machine.");

        var reader = new PlayerSaveReader();
        PlayerSave save = reader.Read(path!);

        _output.WriteLine($"SteamId   : {save.SteamId}");
        _output.WriteLine($"Faction   : {save.Faction}");
        _output.WriteLine($"Serveur   : {save.LastServer}");
        _output.WriteLine($"Langue    : {save.Language}");
        _output.WriteLine($"Wars      : {save.WarsJoined.Count}");
        _output.WriteLine($"Loadouts  : {save.Loadouts.Count}");
        foreach (Loadout lo in save.Loadouts)
        {
            _output.WriteLine($"  - {lo.Name}");
            foreach (LoadoutItem it in lo.Equipment)
                _output.WriteLine($"      [équip:{it.Slot}] {it.CodeName} x{it.Quantity}");
            foreach (LoadoutItem it in lo.Backpack)
                _output.WriteLine($"      [sac]        {it.CodeName} x{it.Quantity}");
        }

        // Le parsing doit aboutir à une faction connue.
        Assert.NotEqual(Faction.Unknown, save.Faction);
        Assert.False(string.IsNullOrEmpty(save.LastServer));
    }
}
