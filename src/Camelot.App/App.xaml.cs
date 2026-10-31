using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Camelot.App.Services;
using Camelot.App.ViewModels;

namespace Camelot.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Camelot.ProxyClient";
    private const string ShowEventName = @"Local\Camelot.ProxyClient.Show";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Второй запуск только показывает окно уже работающего Camelot.
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(WaitForShowRequests) { IsBackground = true, Name = "Camelot.Show" }.Start();

        // Тема Fluent как в Windows 11, светлая или тёмная — по настройке Windows.
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write("Необработанная ошибка", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("Необработанная ошибка задачи", args.Exception);
            args.SetObserved();
        };

        // Если в прошлый раз Camelot закрылся аварийно в режиме системного прокси — вернуть настройки Windows.
        try
        {
            SystemProxy.Restore();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Восстановление системного прокси при запуске", ex);
        }

        _vm = new MainViewModel();
        _window = new MainWindow(_vm);
        _window.HiddenToTray += OnHiddenToTray;
        _tray = new TrayIcon(_vm, ShowMainWindow, ExitApplication);
        MainWindow = _window;
        _window.Show();
    }

    private void WaitForShowRequests()
    {
        var showEvent = _showEvent;
        while (showEvent is not null && showEvent.WaitOne())
        {
            if (_exiting)
            {
                return;
            }
            Dispatcher.InvokeAsync(ShowMainWindow);
        }
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void OnHiddenToTray()
    {
        if (_vm is null || _tray is null || _vm.TrayHintShown)
        {
            return;
        }
        _vm.TrayHintShown = true;
        _tray.ShowHint("Camelot работает в фоне", "Прокси остаётся включённым. Значок Camelot — в области уведомлений, через него можно отключиться или выйти.");
    }

    /// <summary>Отключение, возврат настроек Windows и выход.</summary>
    public void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        if (_window is not null)
        {
            _window.AllowClose = true;
        }
        _tray?.Dispose();
        _tray = null;
        _vm?.Shutdown();
        _showEvent?.Set();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Выход из Windows или выключение: вернуть системный прокси и остановить ядро.
        _vm?.Shutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _vm?.Shutdown();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Ошибка интерфейса", e.Exception);
        MessageBox.Show(
            $"Произошла ошибка: {e.Exception.Message}\n\nПодробности записаны в {AppPaths.CrashLogFile}",
            "Camelot", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
