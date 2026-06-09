namespace FoxholeLogiHub.Core.Gvas;

/// <summary>En-tête d'un fichier de sauvegarde GVAS (UE4).</summary>
public sealed class GvasHeader
{
    public int SaveGameVersion { get; init; }
    public int PackageVersion { get; init; }
    public ushort EngineMajor { get; init; }
    public ushort EngineMinor { get; init; }
    public ushort EnginePatch { get; init; }
    public uint EngineChangelist { get; init; }
    public string EngineBranch { get; init; } = "";
    public string SaveGameClassName { get; init; } = "";

    public string EngineVersion => $"{EngineMajor}.{EngineMinor}.{EnginePatch}";
}

/// <summary>Une propriété taguée UE4 : nom, type, et valeur décodée.</summary>
public sealed class GvasProperty
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public object? Value { get; set; }

    public override string ToString() => $"{Name} ({Type}) = {Value}";
}

/// <summary>
/// Un struct UE4 : soit un ensemble de propriétés taguées (struct « générique »),
/// soit des octets bruts (struct natif comme Guid/Vector).
/// </summary>
public sealed class GvasStruct
{
    public required string StructType { get; init; }
    public List<GvasProperty> Properties { get; } = new();
    public byte[]? RawBytes { get; set; }

    public GvasProperty? Find(string name) =>
        Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    public object? GetValue(string name) => Find(name)?.Value;
}

/// <summary>Un tableau UE4 (ArrayProperty) : type interne + éléments.</summary>
public sealed class GvasArray
{
    public required string InnerType { get; init; }
    public List<object?> Items { get; } = new();
}

/// <summary>Résultat complet du parsing d'un fichier .sav : en-tête + propriétés racine.</summary>
public sealed class GvasSaveGame
{
    public required GvasHeader Header { get; init; }
    public required GvasStruct Root { get; init; }
}
