using PaintDotNet;

[assembly: PluginSupportInfo(typeof(CBMFileType.CbmPluginSupportInfo))]

namespace CBMFileType;

public sealed class CbmFileTypePlugin : FileType
{
    public CbmFileTypePlugin()
        : base(
            "Crashday Bitmap",
            new FileTypeOptions
            {
                LoadExtensions = [".cbm"],
                SaveExtensions = [".cbm"],
                SupportsLayers = true
            })
    {
    }

    protected override Document OnLoad(Stream input)
    {
        var file = CbmFile.Read(input);
        var document = new Document(file.Width, file.Height);
        document.CustomHeaders = CbmDocumentMetadata.Write(document.CustomHeaders, file.Kind);
        var imageCount = file.Kind == CbmKind.Picture ? file.Images.Count : 1;

        for (var index = 0; index < imageCount; index++)
        {
            var image = file.Images[index];
            var layer = new BitmapLayer(file.Width, file.Height)
            {
                Name = GetLayerName(file, index)
            };
            WriteImage(layer.Surface, file, image);
            document.Layers.Add(layer);
        }

        return document;
    }

    protected override void OnSave(
        Document input,
        Stream output,
        SaveConfigToken token,
        Surface scratchSurface,
        ProgressEventHandler progressCallback)
    {
        var storedKind = CbmDocumentMetadata.Read(input.CustomHeaders);
        var bitmapLayers = input.Layers.OfType<BitmapLayer>().ToList();
        if (bitmapLayers.Count == 0)
            throw new InvalidDataException("No bitmap layers!");

        var kind = storedKind ?? (bitmapLayers.Count == 1 && CbmEncoder.IsTextureSize(input.Width, input.Height) ? CbmKind.Texture : CbmKind.Picture);
        if (kind == CbmKind.Texture && !CbmEncoder.IsTextureSize(input.Width, input.Height))
            kind = CbmKind.Picture;

        List<CbmFrame> frames;
        if (kind == CbmKind.Texture)
        {
            input.Flatten(scratchSurface);
            frames = [ReadSurface(scratchSurface)];
        }
        else
        {
            frames = bitmapLayers.Select(layer => ReadSurface(layer.Surface)).ToList();
        }

        CbmEncoder.Write(output, kind, frames);
        progressCallback?.Invoke(this, new ProgressEventArgs(100));
    }

    private static unsafe CbmFrame ReadSurface(Surface surface)
    {
        var pixels = new CbmColor[surface.Width * surface.Height];
        for (var y = 0; y < surface.Height; y++)
        {
            var row = surface.GetRowPointerUnchecked(y);
            for (var x = 0; x < surface.Width; x++)
            {
                var color = row[x];
                pixels[y * surface.Width + x] = new CbmColor(color.R, color.G, color.B, color.A);
            }
        }
        return new CbmFrame(surface.Width, surface.Height, pixels);
    }

    private static string GetLayerName(CbmFile file, int index)
    {
        if (file.Kind == CbmKind.Texture)
            return "Texture";
        return file.Images.Count == 1 ? "Image" : $"Frame {index + 1}";
    }

    private static unsafe void WriteImage(Surface surface, CbmFile file, CbmImage image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            var row = surface.GetRowPointerUnchecked(y);
            var sourceOffset = y * image.Width;
            for (var x = 0; x < image.Width; x++)
            {
                var colorIndex = image.Indices[sourceOffset + x];
                var paletteOffset = colorIndex * 3;
                var alpha = file.HasAlpha && colorIndex == file.TransparentIndex ? (byte)0 : (byte)255;
                row[x] = ColorBgra.FromBgra(
                    file.Palette[paletteOffset + 2],
                    file.Palette[paletteOffset + 1],
                    file.Palette[paletteOffset],
                    alpha);
            }
        }
    }
}
