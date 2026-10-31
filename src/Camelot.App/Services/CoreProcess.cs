using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Camelot.App.Services;

public sealed class CoreException(string message) : Exception(message);

/// <summary>
/// Процесс sing-box.exe. Конфиг передаётся через stdin, поэтому пароли прокси не пишутся на диск.
/// Готовность определяется по Clash API: он начинает отвечать последним, когда TUN уже поднят.
/// </summary>
public sealed class CoreProcess : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<string> _tail = new();
    private JobObject? _job;
    private Process? _process;
    private volatile bool _stopping;

    /// <summary>Строка журнала ядра (вызывается из фонового потока).</summary>
    public event Action<string>? LogLine;

    /// <summary>Ядро завершилось само, не по команде «Отключить». Аргумент — понятная причина.</summary>
    public event Action<string>? Crashed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public async Task StartAsync(string configJson, int apiPort, string apiSecret, CancellationToken cancellationToken)
    {
        Stop();
        if (!File.Exists(AppPaths.SingBoxPath))
        {
            throw new CoreException("Не найден sing-box.exe рядом с Camelot.exe. Распакуйте архив целиком в одну папку.");
        }

        lock (_tail)
        {
            _tail.Clear();
        }
        _stopping = false;

        var startInfo = new ProcessStartInfo(AppPaths.SingBoxPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.CoreWorkDirectory,
        };
        foreach (var argument in new[] { "run", "-c", "stdin", "-D", AppPaths.CoreWorkDirectory, "--disable-color" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Exited += (_, _) => OnExited(process);

        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            throw new CoreException($"Не удалось запустить sing-box.exe: {e.Message}");
        }

        lock (_sync)
        {
            _process = process;
            try
            {
                _job ??= new JobObject();
                _job.Assign(process.Handle);
            }
            catch (Exception e)
            {
                // Без Job Object всё работает, просто ядро не закроется само при аварии Camelot.
                CrashLog.Write("Job Object", e);
            }
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.StandardInput.WriteAsync(configJson.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }
        catch (Exception) when (process.HasExited)
        {
            // причина будет в журнале — обработаем ниже
        }

        await WaitUntilReadyAsync(process, apiPort, apiSecret, cancellationToken);
    }

    private async Task WaitUntilReadyAsync(Process process, int apiPort, string apiSecret, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiSecret);
        var url = $"http://127.0.0.1:{apiPort}/version";
        var deadline = DateTime.UtcNow.AddSeconds(25);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                // Дочитываем вывод, чтобы в ошибке была настоящая причина.
                process.WaitForExit(1000);
                throw new CoreException(DescribeFailure(process.ExitCode));
            }
            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            if (DateTime.UtcNow > deadline)
            {
                Stop();
                throw new CoreException("Ядро sing-box не ответило за 25 секунд. Подробности — на вкладке «Журнал».");
            }
            await Task.Delay(200, cancellationToken);
        }
    }

    public void Stop()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
        }
        if (process is null)
        {
            return;
        }
        _stopping = true;
        try
        {
            if (!process.HasExited)
            {
                // Адаптер wintun и правила брандмауэра sing-box живут, пока жив процесс:
                // Windows убирает их сама при его завершении.
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception e)
        {
            CrashLog.Write("Остановка sing-box", e);
        }
        finally
        {
            process.Dispose();
        }
    }

    private void OnLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        lock (_tail)
        {
            _tail.Enqueue(line);
            while (_tail.Count > 40)
            {
                _tail.Dequeue();
            }
        }
        LogLine?.Invoke(line);
    }

    private void OnExited(Process process)
    {
        if (_stopping)
        {
            return;
        }
        int code;
        try
        {
            process.WaitForExit(1000);
            code = process.ExitCode;
        }
        catch
        {
            code = -1;
        }
        lock (_sync)
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }
        }
        Crashed?.Invoke(DescribeFailure(code));
    }

    /// <summary>Понятное объяснение по последним строкам журнала ядра.</summary>
    private string DescribeFailure(int exitCode)
    {
        string[] lines;
        lock (_tail)
        {
            lines = _tail.ToArray();
        }
        var all = string.Join("\n", lines);
        var lower = all.ToLowerInvariant();

        if (lower.Contains("access is denied") || lower.Contains("отказано в доступе") || lower.Contains("requires elevation"))
        {
            return "Нет прав администратора: Windows не дала создать сетевой адаптер. Запустите Camelot от имени администратора.";
        }
        if (lower.Contains("only one usage of each socket address") || lower.Contains("address already in use"))
        {
            return "Порт уже занят другой программой. Закройте другой VPN или прокси-клиент и попробуйте снова.";
        }
        if (lower.Contains("wintun") || lower.Contains("configure tun interface"))
        {
            return "Не удалось создать адаптер Camelot. Закройте другие VPN-программы или выберите режим «Системный прокси». Подробности — на вкладке «Журнал».";
        }

        var fatal = lines.LastOrDefault(l => l.Contains("FATAL", StringComparison.Ordinal))
                    ?? lines.LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal));
        if (fatal is not null)
        {
            var index = fatal.IndexOf(']');
            var text = index >= 0 && index + 1 < fatal.Length ? fatal[(index + 1)..].Trim() : fatal.Trim();
            return $"sing-box остановился: {text}";
        }
        return $"sing-box остановился (код {exitCode}). Подробности — на вкладке «Журнал».";
    }

    public void Dispose()
    {
        Stop();
        _job?.Dispose();
        _job = null;
    }
}
