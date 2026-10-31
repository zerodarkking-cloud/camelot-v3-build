using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Camelot.Core;

// Значения ниже — вымышленные примеры. Реальные логины и пароли в репозиторий не попадают.
var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "camelot-configs");
Directory.CreateDirectory(outDir);

var failures = 0;
var checks = 0;

void Check(bool condition, string what)
{
    checks++;
    if (!condition)
    {
        failures++;
        Console.WriteLine($"FAIL: {what}");
    }
}

void ExpectError(string link, string fragment)
{
    checks++;
    try
    {
        var p = LinkParser.Parse(link);
        p.Validate();
        failures++;
        Console.WriteLine($"FAIL: ожидалась ошибка для «{link}»");
    }
    catch (ProfileException e) when (e.Message.Contains(fragment, StringComparison.Ordinal))
    {
    }
    catch (ProfileException e)
    {
        failures++;
        Console.WriteLine($"FAIL: «{link}»: неожиданный текст ошибки «{e.Message}»");
    }
}

// ---------------------------------------------------------------- разбор строк прокси

var p1 = LinkParser.Parse("socks5://proxy-a.example.net:14660:lo0001:pAss123");
Check(p1.Protocol == ProxyProtocol.Socks && p1.Server == "proxy-a.example.net" && p1.Port == 14660
      && p1.Username == "lo0001" && p1.Password == "pAss123", "socks5://host:port:логин:пароль");

var p2 = LinkParser.Parse("socks5://user%40x:p%3Ass@proxy-b.example.net:1080#Мой");
Check(p2.Username == "user@x" && p2.Password == "p:ss" && p2.Name == "Мой", "socks5://логин:пароль@host:port#имя");

var p3 = LinkParser.Parse("http://user:pass@203.0.113.7:8080");
Check(p3.Protocol == ProxyProtocol.Http && !p3.UseTls && p3.Port == 8080, "http://");

var p4 = LinkParser.Parse("https://user:pass@proxy.example.org:443");
Check(p4.Protocol == ProxyProtocol.Http && p4.UseTls, "https://");

var p5 = LinkParser.Parse("proxy-c.example.net:3128:user:pa:ss");
Check(p5.Protocol == ProxyProtocol.Socks && p5.Password == "pa:ss", "без схемы, пароль с двоеточием");

var p6 = LinkParser.Parse("user:pass:198.51.100.2:9000");
Check(p6.Server == "198.51.100.2" && p6.Port == 9000 && p6.Username == "user", "логин:пароль:host:port");

var p7 = LinkParser.Parse("[2001:db8::1]:1080:user:pass");
Check(p7.Server == "2001:db8::1" && p7.IsIPv6Literal && p7.DisplayAddress == "[2001:db8::1]:1080", "[IPv6]:port:логин:пароль");

var p8 = LinkParser.Parse("203.0.113.9:1080");
Check(p8.Username.Length == 0 && p8.Port == 1080, "host:port без логина");

var ssBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:p@ss:w0rd")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
var p9 = LinkParser.Parse($"ss://{ssBase64}@[2001:db8::5]:443/#MTS");
Check(p9.Protocol == ProxyProtocol.Shadowsocks && p9.SsPassword == "p@ss:w0rd" && p9.Server == "2001:db8::5", "ss:// SIP002 base64url");

var legacy = Convert.ToBase64String(Encoding.UTF8.GetBytes("chacha20-ietf-poly1305:secret@vpn.example.com:8388"));
var p10 = LinkParser.Parse($"ss://{legacy}#legacy");
Check(p10.SsMethod == "chacha20-ietf-poly1305" && p10.Port == 8388, "ss:// старый формат");

var p11 = LinkParser.Parse("ss://2022-blake3-aes-128-gcm:AAECAwQFBgcICQoLDA0ODw%3D%3D@[2001:db8::10]:8443#SS22");
Check(p11.SsMethod == "2022-blake3-aes-128-gcm", "ss:// SS-2022");

var p12 = LinkParser.Parse("vless://bf000d23-0752-40b4-affe-68f7707a9661@[2a01:4f8::1]:443?encryption=none&flow=xtls-rprx-vision&security=reality&sni=www.microsoft.com&fp=chrome&pbk=UR59xITRJdqYMUQzeRW8fxJS3S54WziH4Sn6Ti7AIDI&sid=17ffddf51c9e3d4e&type=tcp#Reality");
Check(p12.Protocol == ProxyProtocol.Vless && p12.Security == VlessSecurity.Reality && p12.RealityShortId == "17ffddf51c9e3d4e", "vless:// Reality");

var p13 = LinkParser.Parse("vless://bf000d23-0752-40b4-affe-68f7707a9661@vpn.example.com:443?security=tls&sni=vpn.example.com&fp=firefox&type=tcp#tls");
Check(p13.Security == VlessSecurity.Tls && p13.Fingerprint == "firefox", "vless:// TLS");

var many = LinkParser.ParseMany("# список\nsocks5://a.example.net:1:u:p\n\nhttp://u:p@b.example.net:2\nмусор\n");
Check(many.Profiles.Count == 2 && many.Errors.Count == 1, "список из нескольких строк");

ExpectError("vless://bf000d23-0752-40b4-affe-68f7707a9661@h:443?security=reality&type=ws", "Транспорт");
ExpectError("ss://aes-128-gcm:x@host", "порт");
ExpectError("ftp://x:1", "Поддерживаются");
ExpectError("ss://2022-blake3-aes-128-gcm:short@1.2.3.4:443", "base64");
ExpectError("socks5://host", "порт");
ExpectError("socks5://host:99999", "Неверный порт");

// ---------------------------------------------------------------- генерация конфигов

var profiles = new List<(string Name, ProxyProfile Profile)>
{
    ("socks-provider", p1), ("socks-uri", p2), ("http", p3), ("https", p4), ("socks-ipv6", p7),
    ("ss", p9), ("ss-legacy", p10), ("ss2022", p11), ("vless-reality", p12), ("vless-tls", p13),
};

foreach (var (name, profile) in profiles)
{
    foreach (var mode in new[] { ConnectionMode.Tun, ConnectionMode.SystemProxy })
    {
        var options = new CoreOptions { Mode = mode, MixedPort = 2081, ApiPort = 19090, ApiSecret = "test-secret", VerboseLog = mode == ConnectionMode.SystemProxy };
        var json = ConfigBuilder.Build(profile, options);
        var node = JsonNode.Parse(json)!;
        var inbound = node["inbounds"]![0]!;
        if (mode == ConnectionMode.Tun)
        {
            Check((int)inbound["mtu"]! == 1280, $"{name}: MTU 1280");
            Check((bool)inbound["strict_route"]!, $"{name}: strict_route");
            Check(inbound["address"]!.AsArray().Count == 2, $"{name}: IPv4 + IPv6 в TUN");
        }
        else
        {
            Check((string)inbound["type"]! == "mixed" && (int)inbound["listen_port"]! == 2081, $"{name}: mixed 127.0.0.1:2081");
        }
        Check((bool)node["route"]!["auto_detect_interface"]!, $"{name}: auto_detect_interface");
        Check((string)node["outbounds"]![0]!["domain_resolver"]!["strategy"]! == "prefer_ipv6", $"{name}: prefer_ipv6");
        File.WriteAllText(Path.Combine(outDir, $"{name}-{mode.ToString().ToLowerInvariant()}.json"), json);
    }
}

// HTTP: весь UDP отклоняется, у остальных — только QUIC.
var httpRules = JsonNode.Parse(ConfigBuilder.Build(p3, new CoreOptions()))!["route"]!["rules"]!.AsArray();
Check(httpRules[^1]!["network"]!.ToString() == "udp" && httpRules[^1]!["port"] is null, "HTTP: весь UDP отклоняется");
var socksRules = JsonNode.Parse(ConfigBuilder.Build(p1, new CoreOptions()))!["route"]!["rules"]!.AsArray();
Check((int)socksRules[^1]!["port"]! == 443, "SOCKS5: отклоняется только QUIC");

// Вариант с изменёнными сетевыми настройками.
var tuned = p1.Clone();
tuned.Mtu = 1400;
tuned.TunnelIPv6 = true;
tuned.BlockQuic = false;
tuned.IpPreference = ServerIpPreference.IPv4Only;
var tunedJson = ConfigBuilder.Build(tuned, new CoreOptions());
var tunedNode = JsonNode.Parse(tunedJson)!;
Check(tunedNode["dns"]!["strategy"] is null, "IPv6 для сайтов: без ipv4_only");
Check(tunedNode["experimental"] is null, "без Clash API, если порт не задан");
File.WriteAllText(Path.Combine(outDir, "socks-tuned-tun.json"), tunedJson);

Console.WriteLine($"{checks - failures}/{checks} проверок пройдено, конфигов: {Directory.GetFiles(outDir, "*.json").Length} в {outDir}");
return failures == 0 ? 0 : 1;
