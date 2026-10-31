using System.Text.Json;
using System.Text.Json.Nodes;

namespace Camelot.Core;

/// <summary>Параметры запуска ядра, которые не относятся к конкретному прокси.</summary>
public sealed class CoreOptions
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.Tun;

    /// <summary>Порт локального прокси для режима «Системный прокси».</summary>
    public int MixedPort { get; set; } = 2080;

    /// <summary>Порт Clash API на 127.0.0.1 — через него приложение читает статистику. 0 — отключить.</summary>
    public int ApiPort { get; set; }

    public string ApiSecret { get; set; } = "";

    public bool VerboseLog { get; set; }

    /// <summary>Имя виртуального адаптера в «Сетевых подключениях» Windows.</summary>
    public string InterfaceName { get; set; } = "Camelot";
}

/// <summary>
/// Собирает конфиг sing-box (формат 1.14) из профиля.
///
/// Режим TUN — «как VPN»: виртуальный адаптер wintun только с IPv4. strict_route правилами
/// брандмауэра Windows делает IPv6 недоступным и не выпускает DNS мимо туннеля, поэтому
/// программы идут через прокси по IPv4 и ничего не утекает в обход. Стек gVisor работает
/// внутри sing-box и не зависит от брандмауэра Windows и сторонних антивирусов.
/// Режим «Системный прокси»: локальный SOCKS/HTTP-порт на 127.0.0.1.
/// </summary>
public static class ConfigBuilder
{
    public const string TunInet4 = "172.19.0.1/30";
    public const int TunMtu = 1500;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Build(ProxyProfile profile, CoreOptions options)
    {
        profile.Validate();

        var proxy = new JsonObject
        {
            ["tag"] = "proxy",
            ["server"] = profile.NormalizedServer,
            ["server_port"] = profile.Port,
        };

        switch (profile.Protocol)
        {
            case ProxyProtocol.Socks:
                proxy["type"] = "socks";
                proxy["version"] = "5";
                AddCredentials(proxy, profile);
                break;

            case ProxyProtocol.Http:
                proxy["type"] = "http";
                AddCredentials(proxy, profile);
                if (profile.UseTls)
                {
                    proxy["tls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = profile.NormalizedServer,
                    };
                }
                break;

            case ProxyProtocol.Shadowsocks:
                proxy["type"] = "shadowsocks";
                proxy["method"] = profile.SsMethod;
                proxy["password"] = profile.SsPassword;
                break;

            case ProxyProtocol.Vless:
                proxy["type"] = "vless";
                proxy["uuid"] = profile.Uuid.Trim();
                if (profile.Flow.Length > 0)
                {
                    proxy["flow"] = profile.Flow;
                }
                var tls = new JsonObject { ["enabled"] = true };
                var sni = profile.Sni.Trim();
                if (sni.Length > 0)
                {
                    tls["server_name"] = sni;
                }
                tls["utls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["fingerprint"] = profile.Fingerprint.Length > 0 ? profile.Fingerprint : "chrome",
                };
                if (profile.Security == VlessSecurity.Reality)
                {
                    tls["reality"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["public_key"] = profile.RealityPublicKey.Trim(),
                        ["short_id"] = profile.RealityShortId.Trim(),
                    };
                }
                proxy["tls"] = tls;
                break;
        }

        // Домен прокси-сервера резолвит системный DNS, по IPv4.
        proxy["domain_resolver"] = new JsonObject
        {
            ["server"] = "dns-local",
            ["strategy"] = "prefer_ipv4",
        };

        var dns = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                // DNS сайтов — через прокси (DoH): провайдер его не видит и не подменяет.
                new JsonObject
                {
                    ["type"] = "https",
                    ["tag"] = "dns-remote",
                    ["server"] = "1.1.1.1",
                    ["detour"] = "proxy",
                },
                // Системный DNS — только чтобы найти сам прокси-сервер, если указан домен.
                new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "dns-local",
                },
            },
            ["final"] = "dns-remote",
            // Программам отдаём только IPv4-адреса сайтов: IPv6 в туннеле нет.
            ["strategy"] = "ipv4_only",
        };

        JsonObject inbound;
        if (options.Mode == ConnectionMode.Tun)
        {
            inbound = new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["interface_name"] = options.InterfaceName,
                ["address"] = new JsonArray(TunInet4),
                ["mtu"] = TunMtu,
                ["stack"] = "gvisor",
                ["auto_route"] = true,
                ["strict_route"] = true,
            };
        }
        else
        {
            inbound = new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = options.MixedPort,
            };
        }

        var rules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" },
        };
        if (profile.Protocol == ProxyProtocol.Http)
        {
            // HTTP-прокси передаёт только TCP. Остальной UDP отклоняем сразу,
            // чтобы программы не ждали таймаута и не уходили мимо прокси (DNS обработан выше).
            rules.Add(new JsonObject { ["network"] = "udp", ["action"] = "reject" });
        }
        else
        {
            // QUIC (UDP 443) через прокси работает плохо: браузеры сразу переходят на TCP.
            rules.Add(new JsonObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject" });
        }

        var config = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = options.VerboseLog ? "info" : "warn",
                ["timestamp"] = true,
            },
            ["dns"] = dns,
            ["inbounds"] = new JsonArray(inbound),
            ["outbounds"] = new JsonArray
            {
                proxy,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
            },
            ["route"] = new JsonObject
            {
                ["rules"] = rules,
                ["final"] = "proxy",
                // Обязательно на Windows: исходящие соединения ядра привязываются к реальному
                // адаптеру, иначе трафик к самому прокси зациклится в туннель.
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "dns-local",
            },
        };

        if (options.ApiPort > 0)
        {
            config["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"127.0.0.1:{options.ApiPort}",
                    ["secret"] = options.ApiSecret,
                },
            };
        }

        return config.ToJsonString(WriteOptions);
    }

    private static void AddCredentials(JsonObject proxy, ProxyProfile profile)
    {
        if (profile.Username.Length > 0)
        {
            proxy["username"] = profile.Username;
            proxy["password"] = profile.Password;
        }
    }
}
