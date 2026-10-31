using System;
using System.Collections.Generic;
using System.Linq;

namespace Camelot.Core;

/// <summary>
/// Разбор строк прокси:
/// <list type="bullet">
/// <item><c>socks5://host:port:логин:пароль</c> — формат провайдеров прокси;</item>
/// <item><c>socks5://логин:пароль@host:port</c>, <c>http://…</c>, <c>https://…</c> — стандартный URI;</item>
/// <item><c>host:port:логин:пароль</c>, <c>логин:пароль:host:port</c>, <c>host:port</c> без схемы — считаем SOCKS5;</item>
/// <item><c>ss://</c> (SIP002 и старый формат) и <c>vless://</c> (Reality/TLS поверх TCP).</item>
/// </list>
/// </summary>
public static class LinkParser
{
    private static readonly (string Scheme, ProxyProtocol Protocol, bool Tls)[] PlainSchemes =
    [
        ("socks5h://", ProxyProtocol.Socks, false),
        ("socks5://", ProxyProtocol.Socks, false),
        ("socks://", ProxyProtocol.Socks, false),
        ("https://", ProxyProtocol.Http, true),
        ("http://", ProxyProtocol.Http, false),
    ];

    public static ProxyProfile Parse(string raw)
    {
        var text = raw.Trim();
        var lower = text.ToLowerInvariant();
        if (lower.StartsWith("ss://", StringComparison.Ordinal))
        {
            return ParseShadowsocks(text[5..]);
        }
        if (lower.StartsWith("vless://", StringComparison.Ordinal))
        {
            return ParseVless(text[8..]);
        }
        foreach (var (scheme, protocol, tls) in PlainSchemes)
        {
            if (lower.StartsWith(scheme, StringComparison.Ordinal))
            {
                return ParsePlainProxy(text[scheme.Length..], protocol, tls);
            }
        }
        if (lower.Contains("://", StringComparison.Ordinal))
        {
            throw new ProfileException("Поддерживаются socks5://, http://, https://, ss:// и vless://");
        }
        return ParsePlainProxy(text, ProxyProtocol.Socks, false);
    }

    /// <summary>Несколько прокси, по одному в строке. Пустые строки и строки с # пропускаются.</summary>
    public static (List<ProxyProfile> Profiles, List<string> Errors) ParseMany(string raw)
    {
        var profiles = new List<ProxyProfile>();
        var errors = new List<string>();
        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var item = line.Trim();
            if (item.Length == 0 || item.StartsWith('#'))
            {
                continue;
            }
            try
            {
                profiles.Add(Parse(item));
            }
            catch (ProfileException e)
            {
                errors.Add(e.Message);
            }
        }
        return (profiles, errors);
    }

    // ---------------------------------------------------------------- SOCKS5 / HTTP

    private static ProxyProfile ParsePlainProxy(string body, ProxyProtocol protocol, bool tls)
    {
        var (withoutName, name) = SplitFragment(body);
        var (path, _) = SplitQuery(withoutName);
        var main = path.EndsWith('/') ? path[..^1] : path;
        if (main.Length == 0)
        {
            throw new ProfileException("Пустая строка прокси");
        }

        string host;
        int port;
        var username = "";
        var password = "";

        var at = main.LastIndexOf('@');
        if (at >= 0)
        {
            // логин:пароль@host:port
            var userInfo = main[..at];
            (host, port) = SplitHostPort(main[(at + 1)..]);
            var colon = userInfo.IndexOf(':');
            if (colon >= 0)
            {
                username = Unescape(userInfo[..colon]);
                password = Unescape(userInfo[(colon + 1)..]);
            }
            else
            {
                username = Unescape(userInfo);
            }
        }
        else if (main.StartsWith('['))
        {
            // [IPv6]:port[:логин:пароль]
            var close = main.IndexOf(']');
            if (close < 0)
            {
                throw new ProfileException("Неверный IPv6-адрес прокси");
            }
            var rest = main[(close + 1)..].Split(':', 3);
            if (rest.Length < 2 || rest[0].Length != 0)
            {
                throw new ProfileException("В строке прокси не указан порт");
            }
            host = main[1..close];
            port = ParsePort(rest[1]);
            if (rest.Length == 3)
            {
                var colon = rest[2].IndexOf(':');
                username = colon < 0 ? rest[2] : rest[2][..colon];
                password = colon < 0 ? "" : rest[2][(colon + 1)..];
            }
        }
        else
        {
            var parts = main.Split(':');
            switch (parts.Length)
            {
                case 1:
                    throw new ProfileException("В строке прокси не указан порт");
                case 2:
                    host = parts[0];
                    port = ParsePort(parts[1]);
                    break;
                case 3:
                    host = parts[0];
                    port = ParsePort(parts[1]);
                    username = parts[2];
                    break;
                default:
                    if (parts.Length == 4 && !IsNumber(parts[1]) && IsNumber(parts[3]))
                    {
                        // логин:пароль:host:port
                        username = parts[0];
                        password = parts[1];
                        host = parts[2];
                        port = ParsePort(parts[3]);
                    }
                    else
                    {
                        // host:port:логин:пароль (пароль может содержать «:»)
                        host = parts[0];
                        port = ParsePort(parts[1]);
                        username = parts[2];
                        password = string.Join(':', parts[3..]);
                    }
                    break;
            }
        }

        host = host.Trim();
        if (host.Length == 0)
        {
            throw new ProfileException("В строке прокси не указан адрес");
        }

        var kind = protocol == ProxyProtocol.Socks ? "SOCKS5" : tls ? "HTTPS" : "HTTP";
        return new ProxyProfile
        {
            Protocol = protocol,
            UseTls = tls,
            Server = host,
            Port = port,
            Username = username,
            Password = password,
            Name = name ?? $"{kind} · {host}",
        };
    }

    // ---------------------------------------------------------------- Shadowsocks

    private static ProxyProfile ParseShadowsocks(string body)
    {
        var (withoutName, name) = SplitFragment(body);
        var (path, query) = SplitQuery(withoutName);
        var main = path.EndsWith('/') ? path[..^1] : path;

        if (query.TryGetValue("plugin", out var plugin) && plugin.Length > 0)
        {
            throw new ProfileException($"Плагины Shadowsocks ({plugin}) не поддерживаются");
        }

        string userInfo;
        string hostPort;
        var at = main.LastIndexOf('@');
        if (at >= 0)
        {
            userInfo = main[..at];
            hostPort = main[(at + 1)..];
        }
        else
        {
            // Старый формат: ss://base64(method:password@host:port)
            var decoded = Base64Util.DecodeToString(main);
            var at2 = decoded?.LastIndexOf('@') ?? -1;
            if (decoded is null || at2 < 0)
            {
                throw new ProfileException("Не удалось разобрать ссылку ss://");
            }
            userInfo = decoded[..at2];
            hostPort = decoded[(at2 + 1)..];
        }

        // SIP002: userinfo = base64url(method:password); для SS-2022 — method:password в процентной кодировке.
        var fromBase64 = Base64Util.DecodeToString(userInfo);
        var credentials = fromBase64 is not null && fromBase64.Contains(':') ? fromBase64 : Unescape(userInfo);
        var colon = credentials.IndexOf(':');
        if (colon < 0)
        {
            throw new ProfileException("В ссылке ss:// нет метода шифрования или пароля");
        }

        var (host, port) = SplitHostPort(hostPort);
        return new ProxyProfile
        {
            Protocol = ProxyProtocol.Shadowsocks,
            Name = name ?? "Shadowsocks",
            Server = host,
            Port = port,
            SsMethod = credentials[..colon].ToLowerInvariant(),
            SsPassword = credentials[(colon + 1)..],
        };
    }

    // ---------------------------------------------------------------- VLESS

    private static ProxyProfile ParseVless(string body)
    {
        var (withoutName, name) = SplitFragment(body);
        var (path, query) = SplitQuery(withoutName);
        var main = path.EndsWith('/') ? path[..^1] : path;

        var at = main.LastIndexOf('@');
        if (at < 0)
        {
            throw new ProfileException("В ссылке vless:// нет UUID пользователя");
        }
        var uuid = Unescape(main[..at]);
        var (host, port) = SplitHostPort(main[(at + 1)..]);

        var transport = Get(query, "type", "tcp").ToLowerInvariant();
        if (transport is not ("tcp" or "raw"))
        {
            throw new ProfileException($"Транспорт «{transport}» не поддерживается, нужен TCP (Reality/TLS)");
        }

        var profile = new ProxyProfile
        {
            Protocol = ProxyProtocol.Vless,
            Name = name ?? "VLESS",
            Server = host,
            Port = port,
            Uuid = uuid,
            Flow = Get(query, "flow", ""),
        };

        switch (Get(query, "security", "").ToLowerInvariant())
        {
            case "reality":
                profile.Security = VlessSecurity.Reality;
                profile.RealityPublicKey = Get(query, "pbk", "");
                profile.RealityShortId = Get(query, "sid", "");
                break;
            case "tls":
                profile.Security = VlessSecurity.Tls;
                break;
            default:
                throw new ProfileException("Поддерживается только VLESS с security=reality или security=tls");
        }

        var sni = Get(query, "sni", "");
        profile.Sni = sni.Length > 0 ? sni : Get(query, "peer", "");
        var fp = Get(query, "fp", "").ToLowerInvariant();
        if (fp.Length > 0)
        {
            profile.Fingerprint = ProxyProfile.Fingerprints.Contains(fp) ? fp : "chrome";
        }
        return profile;
    }

    // ---------------------------------------------------------------- Помощники

    private static (string Body, string? Name) SplitFragment(string text)
    {
        var hash = text.IndexOf('#');
        if (hash < 0)
        {
            return (text, null);
        }
        var name = Unescape(text[(hash + 1)..]).Trim();
        return (text[..hash], name.Length == 0 ? null : name);
    }

    private static (string Path, Dictionary<string, string> Query) SplitQuery(string text)
    {
        var mark = text.IndexOf('?');
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mark < 0)
        {
            return (text, query);
        }
        foreach (var pair in text[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (key.Length == 0)
            {
                continue;
            }
            query[key.ToLowerInvariant()] = eq < 0 ? "" : Unescape(pair[(eq + 1)..]);
        }
        return (text[..mark], query);
    }

    /// <summary>«host:port», «[2a01::1]:443».</summary>
    private static (string Host, int Port) SplitHostPort(string text)
    {
        var value = text.Trim();
        string host;
        string portText;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0)
            {
                throw new ProfileException("Неверный IPv6-адрес в ссылке");
            }
            host = value[1..close];
            var rest = value[(close + 1)..];
            if (!rest.StartsWith(':'))
            {
                throw new ProfileException("В ссылке не указан порт");
            }
            portText = rest[1..];
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon < 0)
            {
                throw new ProfileException("В ссылке не указан порт");
            }
            host = value[..colon];
            portText = value[(colon + 1)..];
        }
        if (host.Length == 0)
        {
            throw new ProfileException("В ссылке не указан адрес сервера");
        }
        return (host, ParsePort(portText));
    }

    private static int ParsePort(string text)
    {
        var trimmed = text.Trim();
        if (!IsNumber(trimmed) || !int.TryParse(trimmed, out var port) || port is < 1 or > 65535)
        {
            throw new ProfileException($"Неверный порт: «{text}»");
        }
        return port;
    }

    private static bool IsNumber(string text) => text.Length is > 0 and <= 5 && text.All(char.IsAsciiDigit);

    private static string Get(Dictionary<string, string> query, string key, string fallback) =>
        query.TryGetValue(key, out var value) ? value : fallback;

    private static string Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
