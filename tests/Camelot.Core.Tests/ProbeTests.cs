using System;
using System.Threading.Tasks;
using Camelot.Core;

/// <summary>
/// Проверка ProxyProbe против настоящих прокси (поднимаются sing-box'ом в CI/скрипте):
/// dotnet run -- probe socksPort httpPort httpsPort targetPort
/// </summary>
internal static class ProbeTests
{
    public static async Task<int> RunAsync(string[] args)
    {
        var socksPort = int.Parse(args[1]);
        var httpPort = int.Parse(args[2]);
        var httpsPort = int.Parse(args[3]);
        var targetPort = int.Parse(args[4]);
        var target = new ProbeTarget
        {
            Host = "127.0.0.1",
            Port = targetPort,
            Tls = false,
            Path = "/index.html",
            ValidateProxyCertificate = false,
        };

        var failures = 0;
        var checks = 0;

        async Task Expect(string title, ProxyProfile profile, Func<ProbeResult, bool> ok)
        {
            checks++;
            var started = DateTime.UtcNow;
            var result = await ProxyProbe.CheckAsync(profile, target);
            var seconds = (DateTime.UtcNow - started).TotalSeconds;
            var passed = ok(result);
            if (!passed)
            {
                failures++;
            }
            Console.WriteLine($"{(passed ? "OK  " : "FAIL")} {title} [{seconds:0.0} с]: {result.Failure} · {result.Protocol} tls={result.UseTls} · {result.Message}");
        }

        ProxyProfile P(ProxyProtocol protocol, int port, bool tls = false, string user = "user", string pass = "pass") => new()
        {
            Protocol = protocol,
            Server = "127.0.0.1",
            Port = port,
            UseTls = tls,
            Username = user,
            Password = pass,
        };

        await Expect("SOCKS5 с логином", P(ProxyProtocol.Socks, socksPort),
            r => r.Success && r.Protocol == ProxyProtocol.Socks);
        await Expect("HTTP с логином", P(ProxyProtocol.Http, httpPort),
            r => r.Success && r.Protocol == ProxyProtocol.Http && !r.UseTls);
        await Expect("HTTPS-прокси", P(ProxyProtocol.Http, httpsPort, tls: true),
            r => r.Success && r.UseTls);
        await Expect("галочка HTTPS у обычного HTTP-прокси → исправлено", P(ProxyProtocol.Http, httpPort, tls: true),
            r => r.Success && r.Protocol == ProxyProtocol.Http && !r.UseTls && r.Message.Contains("исправлен"));
        await Expect("SOCKS5 указан, а порт HTTP → исправлено", P(ProxyProtocol.Socks, httpPort),
            r => r.Success && r.Protocol == ProxyProtocol.Http);
        await Expect("HTTP указан, а порт SOCKS5 → исправлено", P(ProxyProtocol.Http, socksPort),
            r => r.Success && r.Protocol == ProxyProtocol.Socks);
        await Expect("HTTP без TLS, а порт HTTPS → исправлено", P(ProxyProtocol.Http, httpsPort),
            r => r.Success && r.UseTls);
        await Expect("SOCKS5 неверный пароль", P(ProxyProtocol.Socks, socksPort, pass: "WRONG"),
            r => r.Failure == ProbeFailure.Auth && r.IsBlocking);
        await Expect("HTTP неверный пароль (407)", P(ProxyProtocol.Http, httpPort, pass: "WRONG"),
            r => r.Failure == ProbeFailure.Auth);
        await Expect("SOCKS5 без логина, а прокси требует", P(ProxyProtocol.Socks, socksPort, user: "", pass: ""),
            r => r.Failure == ProbeFailure.Auth);
        await Expect("закрытый порт", P(ProxyProtocol.Socks, 1),
            r => r.Failure == ProbeFailure.Refused && r.IsBlocking);
        var unknown = P(ProxyProtocol.Socks, 1080);
        unknown.Server = "camelot-proxy-does-not-exist.invalid";
        await Expect("несуществующий адрес", unknown,
            r => r.Failure == ProbeFailure.DnsFailed);

        Console.WriteLine($"{checks - failures}/{checks} проверок прокси пройдено");
        return failures == 0 ? 0 : 1;
    }
}
