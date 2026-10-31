using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Camelot.Core;

namespace Camelot.App.Services;

public static class NetUtil
{
    /// <summary>Свободный TCP-порт на 127.0.0.1.</summary>
    public static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// Внешний IP, как его видят сайты. В режиме TUN запрос сам уходит в туннель,
    /// в режиме системного прокси — явно через локальный порт.
    /// </summary>
    public static async Task<string?> GetExternalIpAsync(ConnectionMode mode, int mixedPort, CancellationToken cancellationToken)
    {
        var handler = new HttpClientHandler();
        if (mode == ConnectionMode.SystemProxy)
        {
            handler.Proxy = new WebProxy($"http://127.0.0.1:{mixedPort}");
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        foreach (var url in new[] { "https://api.ipify.org", "https://ifconfig.me/ip", "https://icanhazip.com" })
        {
            try
            {
                var text = (await http.GetStringAsync(url, cancellationToken)).Trim();
                if (IPAddress.TryParse(text, out _))
                {
                    return text;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
        }
        return null;
    }
}

/// <summary>Байты и скорость по-русски: «1,5 МБ», «320 КБ/с».</summary>
public static class ByteFormat
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Bytes(long value)
    {
        double amount = Math.Max(value, 0);
        var unit = 0;
        while (amount >= 1024 && unit < Units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }
        if (unit == 0)
        {
            return $"{(long)amount} {Units[0]}";
        }
        var format = amount >= 100 ? "0" : "0.0";
        return $"{amount.ToString(format, Russian)} {Units[unit]}";
    }

    public static string Speed(long bytesPerSecond) => $"{Bytes(bytesPerSecond)}/с";
}
