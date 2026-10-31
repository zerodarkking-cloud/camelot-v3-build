using System;
using System.IO;

namespace Camelot.App.Services;

public static class AppPaths
{
    /// <summary>Папка с Camelot.exe (рядом лежит sing-box.exe).</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string SingBoxPath => Path.Combine(BaseDirectory, "sing-box.exe");

    /// <summary>%APPDATA%\Camelot — профили и настройки (переезжают вместе с профилем Windows).</summary>
    public static string DataDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Camelot"));

    /// <summary>%LOCALAPPDATA%\Camelot — рабочие файлы ядра, журнал сбоев, резервная копия прокси Windows.</summary>
    public static string LocalDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Camelot"));

    public static string CoreWorkDirectory => Ensure(Path.Combine(LocalDirectory, "core"));

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.dat");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ProxyBackupFile => Path.Combine(LocalDirectory, "windows-proxy-backup.json");

    public static string CrashLogFile => Path.Combine(LocalDirectory, "crash.log");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
