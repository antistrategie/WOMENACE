namespace WOMENACE.Code;

internal static class SoundIds
{
    // Stem and KDL sound identifiers use FNV-1a over UTF-8 bytes.
    internal static int FromName(string name)
    {
        var hash = 2166136261u;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(name))
            hash = unchecked((hash ^ value) * 16777619u);
        return unchecked((int)hash);
    }
}
