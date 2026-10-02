using System.Reflection;
using PaintDotNet;

namespace CBMFileType;

public sealed class CbmPluginSupportInfo : IPluginSupportInfo
{
    public string DisplayName => "Crashday Bitmap FileType";
    public string Author => string.Empty;
    public string Copyright => string.Empty;
    public Version Version => typeof(CbmPluginSupportInfo).Assembly.GetName().Version ?? new Version(1, 0);
    public Uri WebsiteUri => null!;
}
