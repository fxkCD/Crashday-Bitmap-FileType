using System.Text;

namespace CBMFileType;

internal readonly record struct CbmColor(byte R, byte G, byte B, byte A);
internal sealed record CbmFrame(int Width, int Height, CbmColor[] Pixels);
internal readonly record struct ColorFrequency(int Key, int Count);

internal static class CbmEncoder
{
    private static readonly HashSet<int> TextureDimensions = [4, 8, 16, 32, 64, 128, 256];

    public static bool IsTextureSize(int width, int height) => TextureDimensions.Contains(width) && TextureDimensions.Contains(height);

    public static void Write(Stream output, CbmKind kind, IReadOnlyList<CbmFrame> sourceFrames)
    {
        if (sourceFrames.Count == 0)
            throw new InvalidDataException("No images to save!");

        var width = sourceFrames[0].Width;
        var height = sourceFrames[0].Height;
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
            throw new InvalidDataException("Unsupported image size!");

        List<CbmFrame> frames;
        if (kind == CbmKind.Texture)
        {
            if (!IsTextureSize(width, height))
                throw new InvalidDataException("Unsupported texture size!");
            frames = CreateMipLevels(sourceFrames[0]);
        }
        else
        {
            if (sourceFrames.Count > byte.MaxValue)
                throw new InvalidDataException("Too many frames!");
            if (sourceFrames.Any(frame => frame.Width != width || frame.Height != height))
                throw new InvalidDataException("Frame sizes do not match!");
            frames = sourceFrames.ToList();
        }

        var hasAlpha = frames.SelectMany(frame => frame.Pixels).Any(color => color.A < 128);
        var quantized = Quantize(frames, hasAlpha);
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);

        writer.Write(Encoding.ASCII.GetBytes("CBM"));
        writer.Write((byte)1);
        writer.Write(Encoding.ASCII.GetBytes(kind == CbmKind.Texture ? "TEX" : "PIC"));
        writer.Write((byte)frames.Count);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0);
        writer.Write(hasAlpha ? (byte)1 : (byte)0);
        writer.Write(quantized.TransparentIndex);
        writer.Write(Encoding.ASCII.GetBytes("PAL"));
        writer.Write(quantized.Palette);

        for (var index = 0; index < frames.Count; index++)
        {
            writer.Write(Encoding.ASCII.GetBytes(kind == CbmKind.Texture ? "TEX" : "PIC"));
            writer.Write((byte)('0' + index));
            writer.Write(quantized.Indices[index]);
        }
    }

    private static List<CbmFrame> CreateMipLevels(CbmFrame source)
    {
        var result = new List<CbmFrame>(5) { source };
        for (var level = 1; level < 5; level++)
        {
            var width = source.Width >> level;
            var height = source.Height >> level;
            if (width == 0 || height == 0)
            {
                result.Add(new CbmFrame(width, height, []));
                continue;
            }

            var previous = result[level - 1];
            var pixels = new CbmColor[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = Average(previous, x * 2, y * 2);
            }
            result.Add(new CbmFrame(width, height, pixels));
        }
        return result;
    }

    private static CbmColor Average(CbmFrame frame, int x, int y)
    {
        var r = 0;
        var g = 0;
        var b = 0;
        var a = 0;
        var count = 0;
        for (var offsetY = 0; offsetY < 2; offsetY++)
        {
            var sourceY = Math.Min(y + offsetY, frame.Height - 1);
            for (var offsetX = 0; offsetX < 2; offsetX++)
            {
                var sourceX = Math.Min(x + offsetX, frame.Width - 1);
                var color = frame.Pixels[sourceY * frame.Width + sourceX];
                r += color.R;
                g += color.G;
                b += color.B;
                a += color.A;
                count++;
            }
        }
        return new CbmColor((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
    }

    private static QuantizedImages Quantize(IReadOnlyList<CbmFrame> frames, bool hasAlpha)
    {
        var histogram = new Dictionary<int, int>();
        foreach (var color in frames.SelectMany(frame => frame.Pixels))
        {
            if (hasAlpha && color.A < 128)
                continue;
            var key = Pack(color);
            histogram.TryGetValue(key, out var count);
            histogram[key] = count + 1;
        }

        var colorLimit = hasAlpha ? 255 : 256;
        var samples = histogram.OrderBy(pair => pair.Key).Select(pair => new ColorFrequency(pair.Key, pair.Value)).ToList();
        var paletteColors = CreatePalette(samples, colorLimit);
        var palette = new byte[256 * 3];
        var firstColor = hasAlpha ? 1 : 0;
        for (var index = 0; index < paletteColors.Count; index++)
        {
            var key = paletteColors[index];
            var offset = (firstColor + index) * 3;
            palette[offset] = (byte)((key >> 12) & 63);
            palette[offset + 1] = (byte)((key >> 6) & 63);
            palette[offset + 2] = (byte)(key & 63);
        }

        var cache = new Dictionary<int, byte>();
        var images = new List<byte[]>(frames.Count);
        foreach (var frame in frames)
        {
            var indices = new byte[frame.Pixels.Length];
            for (var pixelIndex = 0; pixelIndex < frame.Pixels.Length; pixelIndex++)
            {
                var color = frame.Pixels[pixelIndex];
                if (hasAlpha && color.A < 128)
                {
                    indices[pixelIndex] = 0;
                    continue;
                }

                var key = Pack(color);
                if (!cache.TryGetValue(key, out var paletteIndex))
                {
                    paletteIndex = (byte)(firstColor + FindNearest(key, paletteColors));
                    cache[key] = paletteIndex;
                }
                indices[pixelIndex] = paletteIndex;
            }
            images.Add(indices);
        }

        return new QuantizedImages(palette, images, 0);
    }

    private static List<int> CreatePalette(List<ColorFrequency> samples, int limit)
    {
        if (samples.Count <= limit)
            return samples.Select(sample => sample.Key).ToList();

        var boxes = new List<List<ColorFrequency>> { samples };
        while (boxes.Count < limit)
        {
            var boxIndex = -1;
            long bestScore = -1;
            for (var index = 0; index < boxes.Count; index++)
            {
                var box = boxes[index];
                if (box.Count < 2)
                    continue;
                var score = GetRange(box) * (long)box.Sum(sample => sample.Count);
                if (score > bestScore)
                {
                    bestScore = score;
                    boxIndex = index;
                }
            }
            if (boxIndex < 0)
                break;

            var selected = boxes[boxIndex];
            var channel = GetSplitChannel(selected);
            selected.Sort((left, right) => GetChannel(left.Key, channel).CompareTo(GetChannel(right.Key, channel)));
            var midpoint = selected.Sum(sample => sample.Count) / 2;
            var accumulated = 0;
            var split = 1;
            for (; split < selected.Count; split++)
            {
                accumulated += selected[split - 1].Count;
                if (accumulated >= midpoint)
                    break;
            }
            split = Math.Clamp(split, 1, selected.Count - 1);
            boxes[boxIndex] = selected.GetRange(0, split);
            boxes.Add(selected.GetRange(split, selected.Count - split));
        }

        return boxes.Select(AverageBox).ToList();
    }

    private static int GetRange(List<ColorFrequency> box)
    {
        var ranges = Enumerable.Range(0, 3).Select(channel => box.Max(sample => GetChannel(sample.Key, channel)) - box.Min(sample => GetChannel(sample.Key, channel)));
        return ranges.Max();
    }

    private static int GetSplitChannel(List<ColorFrequency> box)
    {
        var bestChannel = 0;
        var bestRange = -1;
        for (var channel = 0; channel < 3; channel++)
        {
            var range = box.Max(sample => GetChannel(sample.Key, channel)) - box.Min(sample => GetChannel(sample.Key, channel));
            if (range > bestRange)
            {
                bestRange = range;
                bestChannel = channel;
            }
        }
        return bestChannel;
    }

    private static int AverageBox(List<ColorFrequency> box)
    {
        long total = box.Sum(sample => (long)sample.Count);
        var r = box.Sum(sample => (long)GetChannel(sample.Key, 0) * sample.Count) / total;
        var g = box.Sum(sample => (long)GetChannel(sample.Key, 1) * sample.Count) / total;
        var b = box.Sum(sample => (long)GetChannel(sample.Key, 2) * sample.Count) / total;
        return ((int)r << 12) | ((int)g << 6) | (int)b;
    }

    private static int FindNearest(int key, List<int> palette)
    {
        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        for (var index = 0; index < palette.Count; index++)
        {
            var dr = GetChannel(key, 0) - GetChannel(palette[index], 0);
            var dg = GetChannel(key, 1) - GetChannel(palette[index], 1);
            var db = GetChannel(key, 2) - GetChannel(palette[index], 2);
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }
        return bestIndex;
    }

    private static int Pack(CbmColor color) => ((color.R >> 2) << 12) | ((color.G >> 2) << 6) | (color.B >> 2);
    private static int GetChannel(int key, int channel) => channel switch { 0 => (key >> 12) & 63, 1 => (key >> 6) & 63, _ => key & 63 };

    private sealed record QuantizedImages(byte[] Palette, IReadOnlyList<byte[]> Indices, byte TransparentIndex);
}
