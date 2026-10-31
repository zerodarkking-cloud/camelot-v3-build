using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Camelot.App.Services;

public readonly record struct CoreSnapshot(long UploadTotal, long DownloadTotal, long Memory, int Connections);

/// <summary>Статистика и проверка задержки через Clash API ядра (только 127.0.0.1, с токеном).</summary>
public sealed class ClashClient : IDisposable
{
    private readonly HttpClient _http;

    public ClashClient(int port, string secret)
    {
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
    }

    public async Task<CoreSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("connections", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            return new CoreSnapshot(
                ReadLong(root, "uploadTotal"),
                ReadLong(root, "downloadTotal"),
                ReadLong(root, "memory"),
                root.TryGetProperty("connections", out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Задержка через прокси до generate_204 в миллисекундах; null — прокси не ответил.</summary>
    public async Task<int?> GetDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = WebUtility.UrlEncode("https://www.gstatic.com/generate_204");
            using var response = await _http.GetAsync($"proxies/proxy/delay?timeout=6000&url={url}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var ms) ? ms : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static long ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    public void Dispose() => _http.Dispose();
}
