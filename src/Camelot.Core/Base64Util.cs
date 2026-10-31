using System;
using System.Text;

namespace Camelot.Core;

internal static class Base64Util
{
    /// <summary>Декодирует base64 и base64url, с паддингом и без. null — если это не base64 или не UTF-8.</summary>
    public static string? DecodeToString(string text)
    {
        var bytes = Decode(text);
        if (bytes is null)
        {
            return null;
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public static byte[]? Decode(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        var value = text.Replace('-', '+').Replace('_', '/');
        var trimmed = value.TrimEnd('=');
        foreach (var ch in trimmed)
        {
            var ok = ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/';
            if (!ok)
            {
                return null;
            }
        }
        var remainder = trimmed.Length % 4;
        if (remainder == 1)
        {
            return null;
        }
        var padded = remainder == 0 ? trimmed : trimmed + new string('=', 4 - remainder);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Длина ключа в байтах для строгого base64 (как требует Shadowsocks-2022). -1 — не base64.</summary>
    public static int DecodedLength(string text)
    {
        if (text.Length == 0 || text.Length % 4 != 0)
        {
            return -1;
        }
        try
        {
            return Convert.FromBase64String(text).Length;
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}
