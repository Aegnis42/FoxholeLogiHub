namespace FoxholeLogiHub.Core.Gvas;

/// <summary>
/// Parseur du format de sauvegarde GVAS d'Unreal Engine 4 (testé sur UE 4.24,
/// le moteur de Foxhole). Produit un arbre <see cref="GvasSaveGame"/> de propriétés
/// taguées que l'on peut ensuite mapper vers le domaine métier.
/// </summary>
public sealed class GvasParser
{
    private const string Magic = "GVAS";

    /// <summary>Trace de diagnostic (nom@position de chaque propriété lue). Utile au débogage.</summary>
    public List<string> Trace { get; } = new();
    private int _depth;

    // Structs « natifs » sérialisés en octets bruts (taille fixe) plutôt qu'en
    // propriétés taguées. Foxhole n'en utilise pas dans PlayerSaveGame, mais on les
    // gère par robustesse pour ne pas casser le parsing si l'un d'eux apparaît.
    private static readonly Dictionary<string, int> NativeStructSizes = new(StringComparer.Ordinal)
    {
        ["Guid"] = 16,
        ["Vector"] = 12,
        ["Vector2D"] = 8,
        ["Rotator"] = 12,
        ["Quat"] = 16,
        ["LinearColor"] = 16,
        ["Color"] = 4,
        ["IntPoint"] = 8,
        ["DateTime"] = 8,
        ["Timespan"] = 8,
    };

    public GvasSaveGame Parse(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs);
    }

    public GvasSaveGame Parse(Stream stream)
    {
        using var r = new GvasReader(stream);
        var header = ReadHeader(r);
        var root = new GvasStruct { StructType = header.SaveGameClassName };
        root.Properties.AddRange(ReadProperties(r));
        return new GvasSaveGame { Header = header, Root = root };
    }

    private static GvasHeader ReadHeader(GvasReader r)
    {
        byte[] magic = r.ReadBytes(4);
        if (magic.Length < 4 || magic[0] != 'G' || magic[1] != 'V' || magic[2] != 'A' || magic[3] != 'S')
            throw new InvalidDataException($"Fichier non reconnu : magic attendu '{Magic}'.");

        int saveVersion = r.ReadInt32();
        int packageVersion = r.ReadInt32();
        ushort major = r.ReadUInt16();
        ushort minor = r.ReadUInt16();
        ushort patch = r.ReadUInt16();
        uint changelist = r.ReadUInt32();
        string branch = r.ReadFString();

        // Versions personnalisées (custom versions) : on les saute, non nécessaires au mapping.
        _ = r.ReadInt32(); // CustomVersionFormat
        int customCount = r.ReadInt32();
        for (int i = 0; i < customCount; i++)
        {
            r.ReadGuid();
            r.ReadInt32();
        }

        string className = r.ReadFString();

        return new GvasHeader
        {
            SaveGameVersion = saveVersion,
            PackageVersion = packageVersion,
            EngineMajor = major,
            EngineMinor = minor,
            EnginePatch = patch,
            EngineChangelist = changelist,
            EngineBranch = branch,
            SaveGameClassName = className,
        };
    }

    /// <summary>Lit des propriétés taguées jusqu'au marqueur de fin "None".</summary>
    private List<GvasProperty> ReadProperties(GvasReader r)
    {
        var list = new List<GvasProperty>();
        while (true)
        {
            GvasProperty? prop = ReadProperty(r);
            if (prop is null)
                break;
            list.Add(prop);
        }
        return list;
    }

    private GvasProperty? ReadProperty(GvasReader r)
    {
        long startPos = r.Position;
        string name = r.ReadFString();
        if (string.IsNullOrEmpty(name) || name == "None")
            return null;

        string type = r.ReadFString();
        int size = r.ReadInt32(); // FPropertyTag.Size est un int32
        _ = r.ReadInt32(); // ArrayIndex

        Trace.Add($"{new string(' ', _depth * 2)}@{startPos} {name} : {type} size={size}");

        var prop = new GvasProperty { Name = name, Type = type };

        switch (type)
        {
            case "StructProperty":
            {
                string structType = r.ReadFString();
                r.ReadGuid(); // GUID du struct
                ReadOptionalGuid(r);
                prop.Value = ReadStructBody(r, structType, size);
                break;
            }
            case "ArrayProperty":
            {
                string innerType = r.ReadFString();
                ReadOptionalGuid(r);
                prop.Value = ReadArray(r, innerType);
                break;
            }
            case "BoolProperty":
            {
                byte b = r.ReadByte(); // la valeur est dans le tag, corps de taille 0
                ReadOptionalGuid(r);
                prop.Value = b != 0;
                break;
            }
            case "ByteProperty":
            {
                string enumName = r.ReadFString();
                ReadOptionalGuid(r);
                prop.Value = enumName == "None" ? r.ReadByte() : r.ReadFString();
                break;
            }
            case "EnumProperty":
            {
                _ = r.ReadFString(); // nom de l'enum (type)
                ReadOptionalGuid(r);
                prop.Value = r.ReadFString(); // valeur, ex. "EFactionId::Wardens"
                break;
            }
            default:
            {
                ReadOptionalGuid(r);
                prop.Value = ReadScalar(r, type, size);
                break;
            }
        }

        return prop;
    }

    private object? ReadScalar(GvasReader r, string type, long size)
    {
        switch (type)
        {
            case "IntProperty": return r.ReadInt32();
            case "Int8Property": return (sbyte)r.ReadByte();
            case "Int16Property": return r.ReadInt16();
            case "Int64Property": return r.ReadInt64();
            case "UInt16Property": return r.ReadUInt16();
            case "UInt32Property": return r.ReadUInt32();
            case "UInt64Property": return r.ReadUInt64();
            case "FloatProperty": return r.ReadSingle();
            case "DoubleProperty": return r.ReadDouble();
            case "StrProperty": return r.ReadFString();
            case "NameProperty": return r.ReadFString();
            default:
                // Type inconnu : on saute le corps pour rester aligné.
                _ = r.ReadBytes((int)size);
                return null;
        }
    }

    private GvasArray ReadArray(GvasReader r, string innerType)
    {
        var arr = new GvasArray { InnerType = innerType };
        int count = r.ReadInt32();

        if (innerType == "StructProperty")
        {
            // Tableau de structs : un tag de propriété interne précède les éléments.
            _ = r.ReadFString();   // nom interne (répète le nom du tableau)
            _ = r.ReadFString();   // type interne ("StructProperty")
            _ = r.ReadInt32();     // taille (int32)
            _ = r.ReadInt32();     // ArrayIndex
            string structType = r.ReadFString();
            r.ReadGuid();          // GUID du struct
            ReadOptionalGuid(r);

            for (int i = 0; i < count; i++)
                arr.Items.Add(ReadStructBody(r, structType, size: null));
        }
        else
        {
            for (int i = 0; i < count; i++)
                arr.Items.Add(ReadArrayElement(r, innerType));
        }

        return arr;
    }

    private static object? ReadArrayElement(GvasReader r, string innerType) => innerType switch
    {
        "IntProperty" => r.ReadInt32(),
        "Int64Property" => r.ReadInt64(),
        "UInt16Property" => r.ReadUInt16(),
        "UInt32Property" => r.ReadUInt32(),
        "FloatProperty" => r.ReadSingle(),
        "DoubleProperty" => r.ReadDouble(),
        "ByteProperty" => r.ReadByte(),
        "BoolProperty" => r.ReadByte() != 0,
        "StrProperty" => r.ReadFString(),
        "NameProperty" => r.ReadFString(),
        "EnumProperty" => r.ReadFString(),
        _ => throw new NotSupportedException($"Type d'élément de tableau non géré : {innerType}"),
    };

    /// <summary>
    /// Lit le corps d'un struct. <paramref name="size"/> est connu pour les structs
    /// au niveau propriété (utilisé pour les structs natifs), null pour les éléments de tableau.
    /// </summary>
    private GvasStruct ReadStructBody(GvasReader r, string structType, long? size)
    {
        var s = new GvasStruct { StructType = structType };

        if (NativeStructSizes.TryGetValue(structType, out int nativeSize))
        {
            int toRead = size.HasValue ? (int)size.Value : nativeSize;
            s.RawBytes = r.ReadBytes(toRead);
            return s;
        }

        _depth++;
        s.Properties.AddRange(ReadProperties(r));
        _depth--;
        return s;
    }

    private static void ReadOptionalGuid(GvasReader r)
    {
        byte hasGuid = r.ReadByte();
        if (hasGuid != 0)
            r.ReadGuid();
    }
}
