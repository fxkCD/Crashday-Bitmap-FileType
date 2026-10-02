using System.Buffers.Binary;
using System.Text;

namespace CBMFileType;

internal enum CbmKind
{
    Picture,
    Texture
}

internal sealed record CbmImage(int Width, int Height, byte[] Indices);

internal sealed class CbmFile
{
    private const int PaletteSize = 256 * 3;
    private static readonly HashSet<int> TextureDimensions = [4, 8, 16, 32, 64, 128, 256];

    public CbmKind Kind { get; }
    public int Width { get; }
    public int Height { get; }
    public bool HasAlpha { get; }
    public byte TransparentIndex { get; }
    public byte[] Palette { get; }
    public IReadOnlyList<CbmImage> Images { get; }

    private CbmFile(CbmKind kind, int width, int height, bool hasAlpha, byte transparentIndex, byte[] palette, IReadOnlyList<CbmImage> images)
    {
        Kind = kind;
        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
        TransparentIndex = transparentIndex;
        Palette = palette;
        Images = images;
    }

    public static CbmFile Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var reader = new BinaryReader(input, Encoding.ASCII, true);

        RequireTag(reader, "CBM");
        if (reader.ReadByte() != 1)
            throw new InvalidDataException("Unsupported CBM version!");

        var kindTag = ReadTag(reader, 3);
        var kind = kindTag switch
        {
            "PIC" => CbmKind.Picture,
            "TEX" => CbmKind.Texture,
            _ => throw new InvalidDataException("Invalid CBM file!")
        };

        var count = reader.ReadByte();
        var width = ReadUInt16(reader);
        var height = ReadUInt16(reader);
        reader.ReadByte();
        var hasAlpha = reader.ReadByte() != 0;
        var transparentIndex = reader.ReadByte();

        if (count == 0 || width == 0 || height == 0)
            throw new InvalidDataException("Invalid CBM file!");
        if (kind == CbmKind.Texture && (count != 5 || !TextureDimensions.Contains(width) || !TextureDimensions.Contains(height)))
            throw new InvalidDataException("Invalid CBM texture!");

        RequireTag(reader, "PAL");
        var sourcePalette = ReadBytes(reader, PaletteSize);
        var palette = new byte[PaletteSize];
        for (var i = 0; i < PaletteSize; i++)
            palette[i] = unchecked((byte)(sourcePalette[i] << 2));

        var images = new List<CbmImage>(count);
        for (var level = 0; level < count; level++)
        {
            var marker = ReadTag(reader, 4);
            var expected = kind == CbmKind.Picture ? "PIC" : "TEX";
            if (!marker.StartsWith(expected, StringComparison.Ordinal))
                throw new InvalidDataException("Corrupted CBM file!");

            var imageWidth = kind == CbmKind.Picture ? width : width >> level;
            var imageHeight = kind == CbmKind.Picture ? height : height >> level;
            var pixelCount = checked(imageWidth * imageHeight);
            images.Add(new CbmImage(imageWidth, imageHeight, ReadBytes(reader, pixelCount)));
        }

        return new CbmFile(kind, width, height, hasAlpha, transparentIndex, palette, images);
    }

    private static ushort ReadUInt16(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        ReadExactly(reader, bytes);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    private static void RequireTag(BinaryReader reader, string expected)
    {
        if (ReadTag(reader, expected.Length) != expected)
            throw new InvalidDataException("Corrupted CBM file!");
    }

    private static string ReadTag(BinaryReader reader, int length)
    {
        var bytes = ReadBytes(reader, length);
        return Encoding.ASCII.GetString(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException("Corrupted CBM file!");
        return bytes;
    }

    private static void ReadExactly(BinaryReader reader, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = reader.Read(destination[offset..]);
            if (read == 0)
                throw new EndOfStreamException("Corrupted CBM file!");
            offset += read;
        }
    }
}
