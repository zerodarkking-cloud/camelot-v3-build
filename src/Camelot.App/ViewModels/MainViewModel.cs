using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Camelot.App.Services;
using Camelot.Core;

namespace Camelot.App.ViewModels;

public enum TunnelState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
}

/// <summary>
/// Состояние приложения и управление подключением. Не зависит от WPF: обновления из фоновых
/// потоков переносятся в поток интерфейса через SynchronizationContext.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int PreferredMixedPort = 10808;
    private const int MaxLogLines = 1500;

    private readonly SynchronizationContext _ui;
    private readonly AppSettings _settings;
    private readonly CoreProcess _core = new();
    private readonly Queue<string> _log = new();
    private readonly Timer _tick;

    private TunnelState _state = TunnelState.Disconnected;
    private ProfileItem? _activeProfile;
    private ConnectionMode _mode;
    private ConnectionMode _runningMode;
    private int _mixedPort;
    private DateTime? _connectedSince;
    private string? _errorMessage;
    private string? _notice;
    private CancellationTokenSource? _session;
    private ClashClient? _clash;
    private CoreSnapshot? _lastSnapshot;
    private DateTime _lastSnapshotAt;
    private int _polling;
    private volatile bool _logDirty;

    private string _elapsedText = "";
    private string _downloadSpeed = "—";
    private string _uploadSpeed = "—";
    private string _totals = "—";
    private string _pingText = "—";
    private string _externalIp = "—";
    private string _memoryText = "—";
    private string _logText = "";

    public MainViewModel()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _settings = ProfileStore.LoadSettings();
        _mode = _settings.Mode;

        foreach (var profile in ProfileStore.LoadProfiles())
        {
            Profiles.Add(new ProfileItem(profile));
        }
        _activeProfile = Profiles.FirstOrDefault(p => p.Profile.Id == _settings.SelectedProfileId) ?? Profiles.FirstOrDefault();
        if (_activeProfile is not null)
        {
            _activeProfile.IsActive = true;
        }

        _core.LogLine += line => AppendLog(line);
        _core.Crashed += reason => _ui.Post(_ => OnCoreCrashed(reason), null);

        // Во время подключения кнопка остаётся активной — повторное нажатие отменяет подключение.
        ToggleCommand = new RelayCommand(() => _ = ToggleAsync(), () => State != TunnelState.Disconnecting);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = null);
        DismissNoticeCommand = new RelayCommand(() => Notice = null);
        CheckCommand = new RelayCommand(() => _ = RefreshPingAndIpAsync(), () => State == TunnelState.Connected);
        ClearLogCommand = new RelayCommand(ClearLog);

        _tick = new Timer(_ => OnTick(), null, 1000, 1000);
        AppendLog($"[Camelot] Запуск. Ядро: {AppPaths.SingBoxPath}");
    }

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    public RelayCommand ToggleCommand { get; }

    public RelayCommand DismissErrorCommand { get; }

    public RelayCommand DismissNoticeCommand { get; }

    public RelayCommand CheckCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    /// <summary>Сообщение для иконки в трее: показать, что приложение продолжает работать.</summary>
    public event Action? StateChanged;

    // ---------------------------------------------------------------- состояние

    public TunnelState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value))
            {
                return;
            }
            Raise(nameof(StatusText));
            Raise(nameof(IsBusy));
            Raise(nameof(IsConnecting));
            Raise(nameof(IsConnected));
            Raise(nameof(IsDisconnected));
            Raise(nameof(CanChangeMode));
            Raise(nameof(PowerButtonHint));
            ToggleCommand.NotifyCanExecuteChanged();
            CheckCommand.NotifyCanExecuteChanged();
            StateChanged?.Invoke();
        }
    }

    public string StatusText => State switch
    {
        TunnelState.Connecting => "Подключение…",
        TunnelState.Connected => "Подключено",
        TunnelState.Disconnecting => "Отключение…",
        _ => "Отключено",
    };

    public string PowerButtonHint => State is TunnelState.Connected or TunnelState.Connecting ? "Отключить" : "Подключить";

    public bool IsBusy => State is TunnelState.Connecting or TunnelState.Disconnecting;

    public bool IsConnecting => State == TunnelState.Connecting;

    public bool IsConnected => State == TunnelState.Connected;

    public bool IsDisconnected => State == TunnelState.Disconnected;

    public bool CanChangeMode => State == TunnelState.Disconnected;

    public string ElapsedText
    {
        get => _elapsedText;
        private set => Set(ref _elapsedText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (Set(ref _errorMessage, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Информационное сообщение (например, тип прокси исправлен автоматически).</summary>
    public string? Notice
    {
        get => _notice;
        set
        {
            if (Set(ref _notice, value))
            {
                Raise(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    // ---------------------------------------------------------------- режим

    public ConnectionMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value))
            {
                return;
            }
            Raise(nameof(IsTunMode));
            Raise(nameof(IsSystemProxyMode));
            Raise(nameof(ModeHint));
            _settings.Mode = value;
            ProfileStore.SaveSettings(_settings);
        }
    }

    public bool IsTunMode
    {
        get => Mode == ConnectionMode.Tun;
        set
        {
            if (value)
            {
                Mode = ConnectionMode.Tun;
            }
        }
    }

    public bool IsSystemProxyMode
    {
        get => Mode == ConnectionMode.SystemProxy;
        set
        {
            if (value)
            {
                Mode = ConnectionMode.SystemProxy;
            }
        }
    }

    public string ModeHint => Mode == ConnectionMode.Tun
        ? "Как VPN: через прокси идёт весь трафик Windows — браузеры, мессенджеры, игры. IPv6 на время подключения отключается, DNS тоже идёт через прокси, ничего не уходит в обход."
        : "Через прокси идут только браузеры и программы, которые используют системный прокси Windows. Остальные работают напрямую.";

    // ---------------------------------------------------------------- прокси

    public ProfileItem? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            if (_activeProfile is not null)
            {
                _activeProfile.IsActive = false;
            }
            _activeProfile = value;
            if (_activeProfile is not null)
            {
                _activeProfile.IsActive = true;
            }
            Raise();
            Raise(nameof(SelectedForList));
            RaiseProfileDependent();
            _settings.SelectedProfileId = value?.Profile.Id;
            ProfileStore.SaveSettings(_settings);
        }
    }

    /// <summary>Выделение в списке прокси: выбор строки делает прокси активным.</summary>
    public ProfileItem? SelectedForList
    {
        get => _activeProfile;
        set
        {
            if (value is not null)
            {
                Activate(value);
            }
            else
            {
                Raise();
            }
        }
    }

    public bool HasProfiles => Profiles.Count > 0;

    public void Activate(ProfileItem item)
    {
        if (ReferenceEquals(item, ActiveProfile))
        {
            return;
        }
        ActiveProfile = item;
        if (State == TunnelState.Connected)
        {
            _ = ReconnectAsync();
        }
    }

    /// <summary>Добавляет новый или обновляет существующий прокси (по Id).</summary>
    public void Save(ProxyProfile profile)
    {
        var existing = Profiles.FirstOrDefault(p => p.Profile.Id == profile.Id);
        if (existing is null)
        {
            existing = new ProfileItem(profile);
            Profiles.Add(existing);
        }
        else
        {
            existing.Update(profile);
        }
        PersistProfiles();
        if (ActiveProfile is null)
        {
            ActiveProfile = existing;
        }
        else if (ReferenceEquals(existing, ActiveProfile))
        {
            RaiseProfileDependent();
            if (State == TunnelState.Connected)
            {
                _ = ReconnectAsync();
            }
        }
    }

    public void Delete(ProfileItem item)
    {
        var wasActive = ReferenceEquals(item, ActiveProfile);
        Profiles.Remove(item);
        PersistProfiles();
        if (wasActive)
        {
            if (State != TunnelState.Disconnected)
            {
                _ = DisconnectAsync();
            }
            ActiveProfile = Profiles.FirstOrDefault();
        }
    }

    /// <summary>Импорт одной или нескольких строк прокси. Возвращает добавленные прокси и ошибки.</summary>
    public (List<ProxyProfile> Added, List<string> Errors) Import(string text)
    {
        var (profiles, errors) = LinkParser.ParseMany(text);
        foreach (var profile in profiles)
        {
            Save(profile);
        }
        return (profiles, errors);
    }

    private void PersistProfiles()
    {
        try
        {
            ProfileStore.SaveProfiles(Profiles.Select(p => p.Profile));
        }
        catch (Exception e)
        {
            CrashLog.Write("Сохранение профилей", e);
            ErrorMessage = $"Не удалось сохранить список прокси: {e.Message}";
        }
        Raise(nameof(HasProfiles));
    }

    private void RaiseProfileDependent()
    {
        Raise(nameof(HasProfiles));
    }

    // ---------------------------------------------------------------- статистика

    public string DownloadSpeed
    {
        get => _downloadSpeed;
        private set => Set(ref _downloadSpeed, value);
    }

    public string UploadSpeed
    {
        get => _uploadSpeed;
        private set => Set(ref _uploadSpeed, value);
    }

    public string Totals
    {
        get => _totals;
        private set => Set(ref _totals, value);
    }

    public string PingText
    {
        get => _pingText;
        private set => Set(ref _pingText, value);
    }

    public string ExternalIp
    {
        get => _externalIp;
        private set => Set(ref _externalIp, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        private set => Set(ref _memoryText, value);
    }

    // ---------------------------------------------------------------- журнал

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    public bool VerboseLog
    {
        get => _settings.VerboseLog;
        set
        {
            if (_settings.VerboseLog == value)
            {
                return;
            }
            _settings.VerboseLog = value;
            ProfileStore.SaveSettings(_settings);
            Raise();
        }
    }

    public bool TrayHintShown
    {
        get => _settings.TrayHintShown;
        set
        {
            _settings.TrayHintShown = value;
            ProfileStore.SaveSettings(_settings);
        }
    }

    private void AppendLog(string line)
    {
        lock (_log)
        {
            _log.Enqueue(line);
            while (_log.Count > MaxLogLines)
            {
                _log.Dequeue();
            }
        }
        _logDirty = true;
    }

    private void ClearLog()
    {
        lock (_log)
        {
            _log.Clear();
        }
        LogText = "";
    }

    public string GetFullLog()
    {
        lock (_log)
        {
            return string.Join(Environment.NewLine, _log);
        }
    }

    // ---------------------------------------------------------------- подключение

    public async Task ToggleAsync()
    {
        switch (State)
        {
            case TunnelState.Disconnected:
                await ConnectAsync();
                break;
            case TunnelState.Connected:
            case TunnelState.Connecting:
                await DisconnectAsync();
                break;
        }
    }

    public async Task ConnectAsync()
    {
        if (State != TunnelState.Disconnected)
        {
            return;
        }
        var item = ActiveProfile;
        if (item is null)
        {
            ErrorMessage = "Сначала добавьте прокси на вкладке «Прокси».";
            return;
        }

        ErrorMessage = null;
        Notice = null;
        ResetStats();
        State = TunnelState.Connecting;
        var session = new CancellationTokenSource();
        _session = session;
        _runningMode = Mode;

        try
        {
            var profile = item.Profile.Clone();
            await CheckProxyAsync(profile, session.Token);

            var apiPort = NetUtil.FreeTcpPort();
            var secret = NetUtil.RandomSecret();
            _mixedPort = _runningMode == ConnectionMode.SystemProxy ? PickMixedPort() : 0;
            var options = new CoreOptions
            {
                Mode = _runningMode,
                MixedPort = _mixedPort,
                ApiPort = apiPort,
                ApiSecret = secret,
                VerboseLog = VerboseLog,
                InterfaceName = _settings.InterfaceName,
            };
            AppendLog($"[Camelot] Подключение: {profile.Name} ({profile.ProtocolTitle}, {profile.DisplayAddress}), режим: {(_runningMode == ConnectionMode.Tun ? $"VPN, адаптер {options.InterfaceName}" : $"системный прокси 127.0.0.1:{_mixedPort}")}");

            try
            {
                await _core.StartAsync(ConfigBuilder.Build(profile, options), apiPort, secret, session.Token);
            }
            catch (CoreException e) when (e.AdapterConflict && _runningMode == ConnectionMode.Tun)
            {
                // От прошлого запуска остался «зависший» адаптер с этим именем — создаём адаптер с новым.
                _settings.InterfaceName = $"Camelot-{RandomNumberGenerator.GetHexString(4, lowercase: true)}";
                ProfileStore.SaveSettings(_settings);
                options.InterfaceName = _settings.InterfaceName;
                AppendLog($"[Camelot] {e.Message} Создаю новый адаптер: {options.InterfaceName}");
                await _core.StartAsync(ConfigBuilder.Build(profile, options), apiPort, secret, session.Token);
            }
            session.Token.ThrowIfCancellationRequested();

            if (_runningMode == ConnectionMode.SystemProxy)
            {
                SystemProxy.Enable(_mixedPort);
                session.Token.ThrowIfCancellationRequested();
            }

            _clash = new ClashClient(apiPort, secret);
            _connectedSince = DateTime.Now;
            State = TunnelState.Connected;
            AppendLog("[Camelot] Подключено");
            _ = RefreshPingAndIpAsync();
        }
        catch (OperationCanceledException)
        {
            await StopCoreAsync();
            State = TunnelState.Disconnected;
        }
        catch (Exception e) when (e is ProfileException or CoreException)
        {
            AppendLog($"[Camelot] Ошибка: {e.Message}");
            await StopCoreAsync();
            ErrorMessage = e.Message;
            State = TunnelState.Disconnected;
        }
        catch (Exception e)
        {
            CrashLog.Write("Подключение", e);
            AppendLog($"[Camelot] Ошибка: {e}");
            await StopCoreAsync();
            ErrorMessage = $"Не удалось подключиться: {e.Message}";
            State = TunnelState.Disconnected;
        }
    }

    /// <summary>
    /// Проверка SOCKS5/HTTP-прокси до запуска ядра: вместо «подключено, но не работает» —
    /// понятная причина. Если тип прокси указан неверно (например, включён HTTPS у обычного
    /// HTTP-прокси), исправляет его в профиле.
    /// </summary>
    private async Task CheckProxyAsync(ProxyProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.Protocol.IsPlainProxy())
        {
            return;
        }
        AppendLog($"[Camelot] Проверка прокси {profile.DisplayAddress}…");
        var result = await ProxyProbe.CheckAsync(profile, cancellationToken: cancellationToken);
        AppendLog($"[Camelot] {result.Message}");

        if (result.Success && result.Protocol is ProxyProtocol detected
            && (detected != profile.Protocol || result.UseTls != profile.UseTls))
        {
            profile.Protocol = detected;
            profile.UseTls = result.UseTls;
            Save(profile.Clone());
            Notice = $"Тип прокси исправлен: это {ProxyProbe.KindTitle(detected, result.UseTls)}.";
        }

        if (!result.Success)
        {
            if (result.IsBlocking)
            {
                throw new CoreException(result.Message);
            }
            AppendLog("[Camelot] Прокси отвечает, но проверочный сайт недоступен — подключаюсь всё равно.");
        }
    }

    public async Task DisconnectAsync()
    {
        if (State is TunnelState.Disconnected or TunnelState.Disconnecting)
        {
            return;
        }
        var wasConnecting = State == TunnelState.Connecting;
        State = TunnelState.Disconnecting;
        _session?.Cancel();
        if (wasConnecting)
        {
            // ConnectAsync сам остановит ядро и переведёт состояние в «Отключено».
            await Task.Delay(50);
        }
        await StopCoreAsync();
        AppendLog("[Camelot] Отключено");
        State = TunnelState.Disconnected;
    }

    private async Task ReconnectAsync()
    {
        await DisconnectAsync();
        await ConnectAsync();
    }

    private async Task StopCoreAsync()
    {
        _clash?.Dispose();
        _clash = null;
        _connectedSince = null;
        RestoreSystemProxy();
        await Task.Run(_core.Stop);
        ResetStats();
    }

    private void RestoreSystemProxy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            SystemProxy.Restore();
        }
        catch (Exception e)
        {
            CrashLog.Write("Восстановление системного прокси", e);
            ErrorMessage = $"Не удалось вернуть настройки прокси Windows: {e.Message}. Проверьте «Параметры → Сеть и Интернет → Прокси».";
        }
    }

    private void OnCoreCrashed(string reason)
    {
        // Падение во время подключения сообщает сам ConnectAsync.
        if (State != TunnelState.Connected)
        {
            return;
        }
        AppendLog($"[Camelot] {reason}");
        _session?.Cancel();
        _clash?.Dispose();
        _clash = null;
        _connectedSince = null;
        RestoreSystemProxy();
        ResetStats();
        ErrorMessage = reason;
        State = TunnelState.Disconnected;
    }

    private static int PickMixedPort()
    {
        try
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, PreferredMixedPort);
            probe.Start();
            probe.Stop();
            return PreferredMixedPort;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return NetUtil.FreeTcpPort();
        }
    }

    // ---------------------------------------------------------------- таймер

    private void OnTick()
    {
        if (_logDirty)
        {
            _logDirty = false;
            string text;
            lock (_log)
            {
                var builder = new StringBuilder();
                foreach (var line in _log)
                {
                    builder.AppendLine(line);
                }
                text = builder.ToString();
            }
            _ui.Post(_ => LogText = text, null);
        }

        if (_connectedSince is DateTime since)
        {
            var elapsed = DateTime.Now - since;
            var text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            _ui.Post(_ =>
            {
                if (_connectedSince is not null)
                {
                    ElapsedText = text;
                }
            }, null);
        }

        var clash = _clash;
        if (clash is not null && Interlocked.Exchange(ref _polling, 1) == 0)
        {
            _ = PollAsync(clash);
        }
    }

    private async Task PollAsync(ClashClient clash)
    {
        try
        {
            var snapshot = await clash.GetSnapshotAsync(CancellationToken.None);
            if (snapshot is not CoreSnapshot current)
            {
                return;
            }
            var now = DateTime.UtcNow;
            var previous = _lastSnapshot;
            var seconds = (now - _lastSnapshotAt).TotalSeconds;
            _lastSnapshot = current;
            _lastSnapshotAt = now;

            var down = previous is CoreSnapshot p && seconds > 0.2 ? (long)Math.Max(0, (current.DownloadTotal - p.DownloadTotal) / seconds) : 0;
            var up = previous is CoreSnapshot q && seconds > 0.2 ? (long)Math.Max(0, (current.UploadTotal - q.UploadTotal) / seconds) : 0;
            _ui.Post(_ =>
            {
                if (!ReferenceEquals(clash, _clash))
                {
                    return;
                }
                DownloadSpeed = ByteFormat.Speed(down);
                UploadSpeed = ByteFormat.Speed(up);
                Totals = $"{ByteFormat.Bytes(current.DownloadTotal)} / {ByteFormat.Bytes(current.UploadTotal)}";
                MemoryText = $"{ByteFormat.Bytes(current.Memory)} · соединений: {current.Connections}";
            }, null);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    public async Task RefreshPingAndIpAsync()
    {
        var clash = _clash;
        var session = _session;
        if (clash is null || session is null)
        {
            return;
        }
        PingText = "проверка…";
        ExternalIp = "проверка…";
        var mode = _runningMode;
        var port = _mixedPort;
        var pingTask = clash.GetDelayAsync(session.Token);
        var ipTask = NetUtil.GetExternalIpAsync(mode, port, session.Token);
        int? delay = null;
        string? ip = null;
        try
        {
            delay = await pingTask;
            ip = await ipTask;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!ReferenceEquals(clash, _clash))
        {
            return;
        }
        PingText = delay is int ms ? $"{ms} мс" : "нет ответа";
        ExternalIp = ip ?? "не определён";
        if (delay is null)
        {
            AppendLog("[Camelot] Прокси не ответил на проверку (generate_204). Проверьте адрес, порт, логин и пароль.");
        }
    }

    private void ResetStats()
    {
        _lastSnapshot = null;
        ElapsedText = "";
        DownloadSpeed = "—";
        UploadSpeed = "—";
        Totals = "—";
        PingText = "—";
        ExternalIp = "—";
        MemoryText = "—";
    }

    /// <summary>Синхронное завершение при выходе из приложения или выключении Windows.</summary>
    public void Shutdown()
    {
        _session?.Cancel();
        _tick.Dispose();
        _clash?.Dispose();
        RestoreSystemProxy();
        _core.Dispose();
    }

    public void Dispose() => Shutdown();
}
