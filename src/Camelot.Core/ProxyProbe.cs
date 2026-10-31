using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Camelot.Core;

public enum ProbeFailure
{
    None,

    /// <summary>Не найден адрес прокси (DNS).</summary>
    DnsFailed,

    /// <summary>Порт закрыт: прокси отклонил TCP-подключение.</summary>
    Refused,

    /// <summary>Прокси не отвечает.</summary>
    Timeout,

    /// <summary>Прокси не принимает TLS (это не HTTPS-прокси).</summary>
    TlsToProxy,

    /// <summary>На порту работает прокси другого типа.</summary>
    WrongProtocol,

    /// <summary>Неверный логин или пароль.</summary>
    Auth,

    /// <summary>Прокси запретил подключение (например, IP не в белом списке).</summary>
    Forbidden,

    /// <summary>Прокси работает, но не смог подключиться к проверочному сайту.</summary>
    TargetUnreachable,

    /// <summary>Туннель открыт, но HTTPS через него не работает.</summary>
    TargetFailed,

    Other,
}

public sealed record ProbeResult(
    bool Success,
    ProbeFailure Failure,
    string Message,
    int? LatencyMs = null,
    ProxyProtocol? Protocol = null,
    bool UseTls = false)
{
    /// <summary>Ошибка самого прокси: подключаться бессмысленно, пока её не исправить.</summary>
    public bool IsBlocking => Failure is ProbeFailure.DnsFailed or ProbeFailure.Refused or ProbeFailure.Timeout
        or ProbeFailure.TlsToProxy or ProbeFailure.WrongProtocol or ProbeFailure.Auth or ProbeFailure.Forbidden;
}

/// <summary>Куда стучаться через прокси при проверке.</summary>
public sealed class ProbeTarget
{
    public static readonly ProbeTarget Default = new();

    public string Host { get; init; } = "www.gstatic.com";
    public int Port { get; init; } = 443;
    public bool Tls { get; init; } = true;
    public string Path { get; init; } = "/generate_204";

    /// <summary>Проверять сертификат HTTPS-прокси (sing-box тоже проверяет). Отключается только в тестах.</summary>
    public bool ValidateProxyCertificate { get; init; } = true;
}

/// <summary>
/// Проверка SOCKS5 / HTTP / HTTPS-прокси напрямую, без ядра: рукопожатие, авторизация,
/// CONNECT к проверочному сайту и запрос через туннель. Если на порту работает прокси
/// другого типа (например, включён HTTPS у обычного HTTP-прокси), подбирает правильный тип.
/// </summary>
public static class ProxyProbe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan GreetingTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TunnelTimeout = TimeSpan.FromSeconds(12);

    public static async Task<ProbeResult> CheckAsync(
        ProxyProfile profile, ProbeTarget? target = null, CancellationToken cancellationToken = default)
    {
        target ??= ProbeTarget.Default;
        if (!profile.Protocol.IsPlainProxy())
        {
            return new ProbeResult(false, ProbeFailure.Other, "Проверка доступна только для SOCKS5 и HTTP-прокси.");
        }

        var configured = (profile.Protocol, profile.UseTls);
        var first = await TryVariantAsync(profile, configured.Protocol, configured.UseTls, target, cancellationToken);
        if (first.Success || first.Failure is not (ProbeFailure.WrongProtocol or ProbeFailure.TlsToProxy))
        {
            return first;
        }

        // Похоже, тип прокси указан неверно: пробуем остальные.
        var alternatives = new List<(ProxyProtocol Protocol, bool Tls)>
        {
            (ProxyProtocol.Http, false),
            (ProxyProtocol.Socks, false),
            (ProxyProtocol.Http, true),
        }.Where(v => v != configured);

        foreach (var (protocol, tls) in alternatives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TryVariantAsync(profile, protocol, tls, target, cancellationToken);
            if (result.Success)
            {
                return result with
                {
                    Message = $"На этом порту работает {KindTitle(protocol, tls)}, а не {KindTitle(configured.Protocol, configured.UseTls)}. " +
                              $"Тип исправлен. {result.Message}",
                };
            }
            if (result.Failure is ProbeFailure.Auth or ProbeFailure.Forbidden or ProbeFailure.TargetUnreachable or ProbeFailure.TargetFailed)
            {
                // Тип угадан, но есть другая проблема — она важнее исходной ошибки.
                return result with { Message = $"Это {KindTitle(protocol, tls)}. {result.Message}" };
            }
        }
        return first;
    }

    public static string KindTitle(ProxyProtocol protocol, bool tls) => protocol switch
    {
        ProxyProtocol.Socks => "SOCKS5-прокси",
        ProxyProtocol.Http when tls => "HTTPS-прокси",
        ProxyProtocol.Http => "HTTP-прокси",
        _ => protocol.Title(),
    };

    private static async Task<ProbeResult> TryVariantAsync(
        ProxyProfile profile, ProxyProtocol protocol, bool useTls, ProbeTarget target, CancellationToken cancellationToken)
    {
        var host = profile.NormalizedServer;
        var endpoint = profile.DisplayAddress;
        var stopwatch = Stopwatch.StartNew();

        ProbeResult Fail(ProbeFailure failure, string message) => new(false, failure, message, null, protocol, useTls);

        // 1. TCP до прокси (сначала IPv4).
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : (await Dns.GetHostAddressesAsync(host, cancellationToken))
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        }
        catch (SocketException)
        {
            return Fail(ProbeFailure.DnsFailed, $"Не удалось найти адрес прокси «{host}». Проверьте, что адрес указан без ошибок.");
        }
        if (addresses.Length == 0)
        {
            return Fail(ProbeFailure.DnsFailed, $"Не удалось найти адрес прокси «{host}».");
        }

        using var tcp = new TcpClient(addresses[0].AddressFamily) { NoDelay = true };
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(addresses[0], profile.Port, connectTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(ProbeFailure.Timeout, $"Прокси {endpoint} не отвечает. Проверьте адрес и порт; возможно, провайдер прокси недоступен из вашей сети.");
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return Fail(ProbeFailure.Refused, $"Прокси {endpoint} отклонил подключение: на этом порту ничего не работает. Проверьте порт.");
        }
        catch (SocketException e)
        {
            return Fail(ProbeFailure.Timeout, $"Не удалось подключиться к прокси {endpoint}: {e.Message}");
        }

        Stream stream = tcp.GetStream();
        try
        {
            // 2. TLS до самого прокси (HTTPS-прокси).
            if (useTls)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                    target.ValidateProxyCertificate ? null : (_, _, _, _) => true);
                try
                {
                    using var tlsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    tlsTimeout.CancelAfter(GreetingTimeout);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, tlsTimeout.Token);
                }
                catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    var certificate = e is AuthenticationException && e.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase);
                    return Fail(ProbeFailure.TlsToProxy, certificate
                        ? "У HTTPS-прокси недействительный сертификат."
                        : "Прокси не работает по TLS: это не HTTPS-прокси. Снимите галочку «HTTPS-прокси».");
                }
                stream = ssl;
            }

            // 3. Рукопожатие и CONNECT к проверочному сайту.
            var tunnel = protocol == ProxyProtocol.Socks
                ? await Socks5ConnectAsync(stream, profile, target, cancellationToken)
                : await HttpConnectAsync(stream, profile, target, cancellationToken);
            if (tunnel is not null)
            {
                return tunnel with { Protocol = protocol, UseTls = useTls };
            }

            // 4. Запрос через туннель.
            var outcome = await RequestThroughTunnelAsync(stream, target, cancellationToken);
            if (outcome is not null)
            {
                return Fail(ProbeFailure.TargetFailed, outcome);
            }
            var ms = (int)stopwatch.ElapsedMilliseconds;
            return new ProbeResult(true, ProbeFailure.None,
                $"Прокси работает: {KindTitle(protocol, useTls)}, ответ за {ms} мс.", ms, protocol, useTls);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(ProbeFailure.Timeout, $"Прокси {endpoint} перестал отвечать во время проверки.");
        }
        catch (IOException)
        {
            return Fail(ProbeFailure.WrongProtocol, "Прокси оборвал соединение во время проверки.");
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------- HTTP CONNECT

    private static async Task<ProbeResult?> HttpConnectAsync(
        Stream stream, ProxyProfile profile, ProbeTarget target, CancellationToken cancellationToken)
    {
        var authority = $"{target.Host}:{target.Port}";
        var request = new StringBuilder()
            .Append($"CONNECT {authority} HTTP/1.1\r\n")
            .Append($"Host: {authority}\r\n")
            .Append("User-Agent: Camelot\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");
        if (profile.Username.Length > 0)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}"));
            request.Append($"Proxy-Authorization: Basic {token}\r\n");
        }
        request.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TunnelTimeout);
        string? head;
        try
        {
            head = await ReadHttpHeadAsync(stream, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(false, ProbeFailure.Timeout,
                "Прокси принял подключение, но не ответил на запрос CONNECT за 12 секунд.");
        }

        if (head is null || !head.StartsWith("HTTP/", StringComparison.Ordinal))
        {
            return new ProbeResult(false, ProbeFailure.WrongProtocol,
                "Прокси закрыл соединение без HTTP-ответа: похоже, это не HTTP-прокси.");
        }

        var statusLine = head.Split("\r\n")[0];
        var parts = statusLine.Split(' ', 3);
        var code = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 0;
        return code switch
        {
            >= 200 and < 300 => null,
            407 => new ProbeResult(false, ProbeFailure.Auth, profile.Username.Length == 0
                ? "Прокси требует логин и пароль (ответ 407)."
                : "Прокси не принял логин или пароль (ответ 407). Проверьте их у провайдера."),
            403 => new ProbeResult(false, ProbeFailure.Forbidden,
                "Прокси запретил подключение (ответ 403). Обычно это значит, что ваш IP не добавлен в белый список у провайдера или закончился тариф."),
            502 or 503 or 504 => new ProbeResult(false, ProbeFailure.TargetUnreachable,
                $"Прокси не смог подключиться к проверочному сайту ({statusLine})."),
            _ => new ProbeResult(false, ProbeFailure.Other, $"Прокси ответил: {statusLine}"),
        };
    }

    /// <summary>Читает заголовок HTTP-ответа до пустой строки. null — соединение закрыто без ответа.</summary>
    private static async Task<string?> ReadHttpHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(256);
        var one = new byte[1];
        while (buffer.Count < 16 * 1024)
        {
            var read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }
            buffer.Add(one[0]);
            if (buffer.Count == 1 && one[0] != (byte)'H')
            {
                // Не HTTP: дальше читать нечего ждать.
                return Encoding.ASCII.GetString(buffer.ToArray());
            }
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
            {
                return Encoding.ASCII.GetString(buffer.ToArray());
            }
        }
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    // ---------------------------------------------------------------- SOCKS5 (RFC 1928 / 1929)

    private static async Task<ProbeResult?> Socks5ConnectAsync(
        Stream stream, ProxyProfile profile, ProbeTarget target, CancellationToken cancellationToken)
    {
        var hasCredentials = profile.Username.Length > 0;
        byte[] greeting = hasCredentials ? [0x05, 0x02, 0x00, 0x02] : [0x05, 0x01, 0x00];
        await stream.WriteAsync(greeting, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var reply = new byte[2];
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(GreetingTimeout);
            try
            {
                if (!await ReadExactAsync(stream, reply, timeout.Token))
                {
                    return new ProbeResult(false, ProbeFailure.WrongProtocol,
                        "Прокси закрыл соединение на приветствии SOCKS5: похоже, это не SOCKS5-прокси.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProbeResult(false, ProbeFailure.WrongProtocol,
                    "Прокси не ответил на приветствие SOCKS5: похоже, это не SOCKS5-прокси.");
            }
        }
        if (reply[0] != 0x05)
        {
            return new ProbeResult(false, ProbeFailure.WrongProtocol, "Прокси ответил не по протоколу SOCKS5.");
        }

        switch (reply[1])
        {
            case 0x00:
                break;
            case 0x02 when hasCredentials:
                var user = Encoding.UTF8.GetBytes(profile.Username);
                var pass = Encoding.UTF8.GetBytes(profile.Password);
                if (user.Length > 255 || pass.Length > 255)
                {
                    return new ProbeResult(false, ProbeFailure.Auth, "Логин или пароль длиннее 255 байт — SOCKS5 такие не поддерживает.");
                }
                var auth = new byte[3 + user.Length + pass.Length];
                auth[0] = 0x01;
                auth[1] = (byte)user.Length;
                user.CopyTo(auth, 2);
                auth[2 + user.Length] = (byte)pass.Length;
                pass.CopyTo(auth, 3 + user.Length);
                await stream.WriteAsync(auth, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                var authReply = new byte[2];
                if (!await ReadExactAsync(stream, authReply, cancellationToken) || authReply[1] != 0x00)
                {
                    return new ProbeResult(false, ProbeFailure.Auth, "Прокси не принял логин или пароль SOCKS5. Проверьте их у провайдера.");
                }
                break;
            case 0x02:
                return new ProbeResult(false, ProbeFailure.Auth, "Прокси требует логин и пароль.");
            default:
                return new ProbeResult(false, ProbeFailure.Auth, hasCredentials
                    ? "Прокси не поддерживает вход по логину и паролю."
                    : "Прокси требует авторизацию, а логин и пароль не указаны.");
        }

        var hostBytes = Encoding.ASCII.GetBytes(target.Host);
        var connect = new byte[7 + hostBytes.Length];
        connect[0] = 0x05;
        connect[1] = 0x01; // CONNECT
        connect[2] = 0x00;
        connect[3] = 0x03; // доменное имя
        connect[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(connect, 5);
        connect[5 + hostBytes.Length] = (byte)(target.Port >> 8);
        connect[6 + hostBytes.Length] = (byte)(target.Port & 0xFF);
        await stream.WriteAsync(connect, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var head = new byte[4];
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TunnelTimeout);
            try
            {
                if (!await ReadExactAsync(stream, head, timeout.Token))
                {
                    return new ProbeResult(false, ProbeFailure.Other, "Прокси закрыл соединение после команды CONNECT.");
                }
                var addressLength = head[3] switch
                {
                    0x01 => 4,
                    0x04 => 16,
                    0x03 => await ReadByteAsync(stream, timeout.Token),
                    _ => 0,
                };
                if (addressLength > 0 && !await ReadExactAsync(stream, new byte[addressLength + 2], timeout.Token))
                {
                    return new ProbeResult(false, ProbeFailure.Other, "Прокси прислал неполный ответ SOCKS5.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProbeResult(false, ProbeFailure.Timeout, "Прокси не ответил на команду CONNECT за 12 секунд.");
            }
        }

        return head[1] switch
        {
            0x00 => null,
            0x02 => new ProbeResult(false, ProbeFailure.Forbidden,
                "Прокси запретил подключение. Обычно это значит, что ваш IP не добавлен в белый список у провайдера или закончился тариф."),
            0x03 or 0x04 or 0x05 or 0x06 => new ProbeResult(false, ProbeFailure.TargetUnreachable,
                $"Прокси не смог подключиться к проверочному сайту (код SOCKS5 {head[1]})."),
            _ => new ProbeResult(false, ProbeFailure.Other, $"Прокси вернул ошибку SOCKS5 с кодом {head[1]}."),
        };
    }

    // ---------------------------------------------------------------- запрос через туннель

    /// <summary>GET через открытый туннель. null — успех, иначе текст ошибки.</summary>
    private static async Task<string?> RequestThroughTunnelAsync(Stream stream, ProbeTarget target, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TunnelTimeout);
        var inner = stream;
        SslStream? ssl = null;
        try
        {
            if (target.Tls)
            {
                ssl = new SslStream(stream, leaveInnerStreamOpen: true);
                try
                {
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = target.Host }, timeout.Token);
                }
                catch (Exception e) when (e is AuthenticationException or IOException)
                {
                    return $"Туннель через прокси открылся, но HTTPS к {target.Host} не установился: {e.Message}";
                }
                inner = ssl;
            }
            var request = $"GET {target.Path} HTTP/1.1\r\nHost: {target.Host}\r\nUser-Agent: Camelot\r\nConnection: close\r\n\r\n";
            await inner.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
            await inner.FlushAsync(timeout.Token);
            var head = await ReadHttpHeadAsync(inner, timeout.Token);
            if (head is null || !head.StartsWith("HTTP/", StringComparison.Ordinal))
            {
                return $"Через прокси не пришёл ответ от {target.Host}.";
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"Через прокси {target.Host} не ответил за 12 секунд.";
        }
        catch (IOException e)
        {
            return $"Соединение через прокси оборвалось: {e.Message}";
        }
        finally
        {
            if (ssl is not null)
            {
                await ssl.DisposeAsync();
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        return await ReadExactAsync(stream, one, cancellationToken) ? one[0] : 0;
    }
}
