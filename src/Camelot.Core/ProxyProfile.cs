using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace Camelot.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ProxyProtocol>))]
public enum ProxyProtocol
{
    Socks,
    Http,
    Shadowsocks,
    Vless,
}

[JsonConverter(typeof(JsonStringEnumConverter<VlessSecurity>))]
public enum VlessSecurity
{
    Reality,
    Tls,
}

/// <summary>Каким IP-протоколом ядро подключается к серверу, если его адрес — домен.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerIpPreference>))]
public enum ServerIpPreference
{
    PreferIPv6,
    PreferIPv4,
    IPv6Only,
    IPv4Only,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionMode>))]
public enum ConnectionMode
{
    /// <summary>Виртуальный адаптер: через прокси идёт весь трафик Windows.</summary>
    Tun,

    /// <summary>Локальный прокси + системные настройки прокси Windows.</summary>
    SystemProxy,
}

public static class EnumText
{
    public static string Title(this ProxyProtocol value) => value switch
    {
        ProxyProtocol.Socks => "SOCKS5",
        ProxyProtocol.Http => "HTTP",
        ProxyProtocol.Shadowsocks => "Shadowsocks",
        ProxyProtocol.Vless => "VLESS",
        _ => value.ToString(),
    };

    public static string Title(this VlessSecurity value) => value switch
    {
        VlessSecurity.Reality => "Reality",
        VlessSecurity.Tls => "TLS",
        _ => value.ToString(),
    };

    public static string Title(this ServerIpPreference value) => value switch
    {
        ServerIpPreference.PreferIPv6 => "Сначала IPv6",
        ServerIpPreference.PreferIPv4 => "Сначала IPv4",
        ServerIpPreference.IPv6Only => "Только IPv6",
        ServerIpPreference.IPv4Only => "Только IPv4",
        _ => value.ToString(),
    };

    /// <summary>Значение стратегии DNS в sing-box.</summary>
    public static string SingBoxValue(this ServerIpPreference value) => value switch
    {
        ServerIpPreference.PreferIPv6 => "prefer_ipv6",
        ServerIpPreference.PreferIPv4 => "prefer_ipv4",
        ServerIpPreference.IPv6Only => "ipv6_only",
        ServerIpPreference.IPv4Only => "ipv4_only",
        _ => "prefer_ipv6",
    };

    public static bool IsPlainProxy(this ProxyProtocol value) =>
        value is ProxyProtocol.Socks or ProxyProtocol.Http;
}

/// <summary>Один прокси-сервер и его сетевые настройки.</summary>
public sealed class ProxyProfile
{
    public const int DefaultMtu = 1280;
    public const int MinMtu = 1280;
    public const int MaxMtu = 1500;

    public static readonly string[] SsMethods =
    [
        "2022-blake3-aes-128-gcm",
        "2022-blake3-aes-256-gcm",
        "2022-blake3-chacha20-poly1305",
        "aes-128-gcm",
        "aes-192-gcm",
        "aes-256-gcm",
        "chacha20-ietf-poly1305",
        "xchacha20-ietf-poly1305",
    ];

    public static readonly string[] Fingerprints = ["chrome", "firefox", "safari", "ios", "edge", "randomized"];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Мой прокси";
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Socks;
    public string Server { get; set; } = "";
    public int Port { get; set; } = 1080;

    // SOCKS5 / HTTP
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>HTTPS-прокси: TLS-соединение до самого прокси.</summary>
    public bool UseTls { get; set; }

    // Shadowsocks
    public string SsMethod { get; set; } = "aes-128-gcm";
    public string SsPassword { get; set; } = "";

    // VLESS
    public string Uuid { get; set; } = "";
    public string Flow { get; set; } = "xtls-rprx-vision";
    public VlessSecurity Security { get; set; } = VlessSecurity.Reality;
    public string Sni { get; set; } = "";
    public string Fingerprint { get; set; } = "chrome";
    public string RealityPublicKey { get; set; } = "";
    public string RealityShortId { get; set; } = "";

    // Сеть
    public int Mtu { get; set; } = DefaultMtu;
    public ServerIpPreference IpPreference { get; set; } = ServerIpPreference.PreferIPv6;

    /// <summary>Отдавать программам IPv6-адреса сайтов. Включать, только если прокси выходит в IPv6.</summary>
    public bool TunnelIPv6 { get; set; }

    /// <summary>Блокировать QUIC (UDP 443), чтобы программы переходили на TCP.</summary>
    public bool BlockQuic { get; set; } = true;

    /// <summary>Адрес без пробелов и квадратных скобок: «[2a01::1]» → «2a01::1».</summary>
    [JsonIgnore]
    public string NormalizedServer => Server.Trim().Trim('[', ']').Trim();

    [JsonIgnore]
    public bool IsIPv6Literal => NormalizedServer.Contains(':');

    [JsonIgnore]
    public string DisplayAddress
    {
        get
        {
            var host = NormalizedServer.Length == 0 ? "—" : NormalizedServer;
            return IsIPv6Literal ? $"[{host}]:{Port}" : $"{host}:{Port}";
        }
    }

    [JsonIgnore]
    public string ProtocolTitle => Protocol switch
    {
        ProxyProtocol.Socks => Username.Length == 0 ? "SOCKS5" : $"SOCKS5 · {Username}",
        ProxyProtocol.Http => (UseTls ? "HTTPS" : "HTTP") + (Username.Length == 0 ? "" : $" · {Username}"),
        ProxyProtocol.Shadowsocks => $"Shadowsocks · {SsMethod}",
        ProxyProtocol.Vless => $"VLESS · {Security.Title()}",
        _ => Protocol.ToString(),
    };

    public ProxyProfile Clone() => (ProxyProfile)MemberwiseClone();

    /// <summary>Проверяет поля. Бросает <see cref="ProfileException"/> с понятным текстом.</summary>
    public void Validate()
    {
        var host = NormalizedServer;
        if (host.Length == 0)
        {
            throw new ProfileException("Укажите адрес сервера");
        }
        if (host.Any(char.IsWhiteSpace) || host.Contains('/'))
        {
            throw new ProfileException($"Адрес сервера указан неверно: «{host}»");
        }
        if (Port is < 1 or > 65535)
        {
            throw new ProfileException("Порт должен быть от 1 до 65535");
        }
        if (Mtu is < MinMtu or > MaxMtu)
        {
            throw new ProfileException($"MTU должен быть от {MinMtu} до {MaxMtu}");
        }

        switch (Protocol)
        {
            case ProxyProtocol.Socks:
            case ProxyProtocol.Http:
                if (Username.Length == 0 && Password.Length > 0)
                {
                    throw new ProfileException("Указан пароль, но не указан логин прокси");
                }
                break;

            case ProxyProtocol.Shadowsocks:
                if (!SsMethods.Contains(SsMethod))
                {
                    throw new ProfileException($"Метод шифрования «{SsMethod}» не поддерживается");
                }
                if (SsPassword.Length == 0)
                {
                    throw new ProfileException("Укажите пароль Shadowsocks");
                }
                var keyLength = Ss2022KeyLength(SsMethod);
                if (keyLength is int need)
                {
                    foreach (var part in SsPassword.Split(':'))
                    {
                        if (Base64Util.DecodedLength(part) != need)
                        {
                            throw new ProfileException($"Для {SsMethod} пароль должен быть ключом base64 длиной {need} байт");
                        }
                    }
                }
                break;

            case ProxyProtocol.Vless:
                if (!Guid.TryParse(Uuid.Trim(), out _))
                {
                    throw new ProfileException("UUID пользователя VLESS указан неверно");
                }
                if (Security == VlessSecurity.Reality)
                {
                    if (Sni.Trim().Length == 0)
                    {
                        throw new ProfileException("Для Reality укажите SNI (server name)");
                    }
                    if (RealityPublicKey.Trim().Length == 0)
                    {
                        throw new ProfileException("Для Reality укажите публичный ключ (pbk)");
                    }
                    var sid = RealityShortId.Trim();
                    if (sid.Length > 16 || sid.Length % 2 != 0 || !sid.All(Uri.IsHexDigit))
                    {
                        throw new ProfileException("Short ID — до 16 шестнадцатеричных символов, чётное количество");
                    }
                }
                break;
        }
    }

    public static int? Ss2022KeyLength(string method) => method switch
    {
        "2022-blake3-aes-128-gcm" => 16,
        "2022-blake3-aes-256-gcm" or "2022-blake3-chacha20-poly1305" => 32,
        _ => null,
    };
}

public sealed class ProfileException(string message) : Exception(message);
