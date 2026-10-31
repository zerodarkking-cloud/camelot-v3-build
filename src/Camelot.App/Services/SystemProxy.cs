using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Camelot.App.Services;

/// <summary>
/// Системный прокси Windows (Параметры → Сеть → Прокси). Прежние значения сохраняются в файл
/// до включения и возвращаются при отключении, а если Camelot упал — при следующем запуске.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private const string Bypass =
        "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;" +
        "172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

    private sealed class Backup
    {
        public int ProxyEnable { get; set; }
        public string? ProxyServer { get; set; }
        public string? ProxyOverride { get; set; }
        public string? AutoConfigUrl { get; set; }
    }

    public static void Enable(int port)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (!File.Exists(AppPaths.ProxyBackupFile))
        {
            var backup = new Backup
            {
                ProxyEnable = key.GetValue("ProxyEnable") is int enabled ? enabled : 0,
                ProxyServer = key.GetValue("ProxyServer") as string,
                ProxyOverride = key.GetValue("ProxyOverride") as string,
                AutoConfigUrl = key.GetValue("AutoConfigURL") as string,
            };
            File.WriteAllText(AppPaths.ProxyBackupFile, JsonSerializer.Serialize(backup));
        }

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", Bypass, RegistryValueKind.String);
        // Скрипт автонастройки (PAC) важнее ручного прокси — на время подключения убираем его.
        if (key.GetValue("AutoConfigURL") is not null)
        {
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        }
        Notify();
    }

    /// <summary>Возвращает настройки, сохранённые перед включением. Ничего не делает, если копии нет.</summary>
    public static void Restore()
    {
        if (!File.Exists(AppPaths.ProxyBackupFile))
        {
            return;
        }
        Backup backup;
        try
        {
            backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(AppPaths.ProxyBackupFile)) ?? new Backup();
        }
        catch (Exception e)
        {
            CrashLog.Write("Резервная копия системного прокси повреждена", e);
            backup = new Backup();
        }

        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
        {
            key.SetValue("ProxyEnable", backup.ProxyEnable, RegistryValueKind.DWord);
            SetOrDelete(key, "ProxyServer", backup.ProxyServer);
            SetOrDelete(key, "ProxyOverride", backup.ProxyOverride);
            SetOrDelete(key, "AutoConfigURL", backup.AutoConfigUrl);
        }
        Notify();
        File.Delete(AppPaths.ProxyBackupFile);
    }

    private static void SetOrDelete(RegistryKey key, string name, string? value)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    /// <summary>Сообщает браузерам и WinINet, что настройки прокси изменились.</summary>
    private static void Notify()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
