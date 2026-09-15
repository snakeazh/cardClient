namespace CardShare.Domain;

public static class AvatarUrlPolicy
{
    private static readonly string[] HostSuffixes =
    {
        ".qlogo.cn",
        ".qpic.cn",
        ".byteimg.com",
        ".douyinpic.com",
        ".douyinstatic.com",
        ".tiktokcdn.com",
        ".pstatp.com",
        ".ibyteimg.com"
    };

    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        foreach (var suffix in HostSuffixes)
        {
            if (host == suffix.TrimStart('.') || host.EndsWith(suffix, StringComparison.Ordinal))
            {
                normalized = uri.GetLeftPart(UriPartial.Path);
                if (!string.IsNullOrEmpty(uri.Query))
                {
                    normalized += uri.Query;
                }

                return true;
            }
        }

        return false;
    }

    public static string NormalizeNickName(string? nickName)
    {
        if (string.IsNullOrWhiteSpace(nickName))
        {
            return string.Empty;
        }

        var trimmed = nickName.Trim();
        if (trimmed.Length > 32)
        {
            trimmed = trimmed.Substring(0, 32);
        }

        var chars = trimmed.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        return new string(chars).Trim();
    }
}
