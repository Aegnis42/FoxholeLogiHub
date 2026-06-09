using System.Text;

namespace FoxholeLogiHub.Core.Gvas;

/// <summary>
/// Lecteur binaire bas niveau pour le format de sérialisation Unreal Engine (UE4).
/// Toutes les valeurs multi-octets sont little-endian (compatible x86/x64).
/// </summary>
internal sealed class GvasReader : IDisposable
{
    private readonly BinaryReader _reader;

    public GvasReader(Stream stream)
    {
        _reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
    }

    public long Position => _reader.BaseStream.Position;
    public long Length => _reader.BaseStream.Length;

    public byte ReadByte() => _reader.ReadByte();
    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);
    public short ReadInt16() => _reader.ReadInt16();
    public ushort ReadUInt16() => _reader.ReadUInt16();
    public int ReadInt32() => _reader.ReadInt32();
    public uint ReadUInt32() => _reader.ReadUInt32();
    public long ReadInt64() => _reader.ReadInt64();
    public ulong ReadUInt64() => _reader.ReadUInt64();
    public float ReadSingle() => _reader.ReadSingle();
    public double ReadDouble() => _reader.ReadDouble();

    /// <summary>Lit un FGuid (16 octets).</summary>
    public byte[] ReadGuid() => _reader.ReadBytes(16);

    /// <summary>
    /// Lit une FString UE4 : un int32 de longueur, puis les octets.
    /// Longueur positive = ASCII/UTF-8 (terminateur null inclus dans la longueur),
    /// longueur négative = UTF-16LE (nombre de char = -longueur, terminateur inclus).
    /// </summary>
    public string ReadFString()
    {
        int len = ReadInt32();
        if (len == 0)
            return string.Empty;

        if (len < 0)
        {
            int charCount = -len;
            int byteCount = checked(charCount * 2);
            byte[] bytes = ReadBytes(byteCount);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        byte[] ascii = ReadBytes(len);
        return Encoding.UTF8.GetString(ascii).TrimEnd('\0');
    }

    public void Dispose() => _reader.Dispose();
}
