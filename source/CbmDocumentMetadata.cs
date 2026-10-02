namespace CBMFileType;

internal static class CbmDocumentMetadata
{
    private const string Key = "crashday.cbm.kind";

    public static string Write(string? headers, CbmKind kind)
    {
        var lines = string.IsNullOrEmpty(headers) ? [] : headers.Split('\n').Where(line => !line.StartsWith(Key + "=", StringComparison.Ordinal)).ToArray();
        return string.Join('\n', lines.Append($"{Key}={kind}"));
    }

    public static CbmKind? Read(string? headers)
    {
        if (string.IsNullOrEmpty(headers))
            return null;
        foreach (var line in headers.Split('\n'))
        {
            if (!line.StartsWith(Key + "=", StringComparison.Ordinal))
                continue;
            return Enum.TryParse<CbmKind>(line[(Key.Length + 1)..], out var kind) ? kind : null;
        }
        return null;
    }
}
