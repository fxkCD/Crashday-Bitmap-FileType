using PaintDotNet;

namespace CBMFileType;

public sealed class CbmFileTypeFactory : IFileTypeFactory
{
    public FileType[] GetFileTypeInstances() => [new CbmFileTypePlugin()];
}
