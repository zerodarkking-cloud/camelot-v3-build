using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Camelot.Core;

namespace Camelot.App.Services;

public sealed class AppSettings
{
    public Guid? SelectedProfileId { get; set; }
    public ConnectionMode Mode { get; set; } = ConnectionMode.Tun;
    public bool VerboseLog { get; set; }
    public bool TrayHintShown { get; set; }
}

/// <summary>Профили (зашифрованы DPAPI) и настройки приложения.</summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<ProxyProfile> LoadProfiles()
    {
        try
        {
            if (!File.Exists(AppPaths.ProfilesFile))
            {
                return [];
            }
            var json = Dpapi.Unprotect(File.ReadAllBytes(AppPaths.ProfilesFile));
            return JsonSerializer.Deserialize<List<ProxyProfile>>(json, JsonOptions) ?? [];
        }
        catch (Exception e)
        {
            // Повреждённый или чужой файл не должен мешать запуску: откладываем его в сторону.
            TryBackup(AppPaths.ProfilesFile);
            CrashLog.Write("Не удалось прочитать профили", e);
            return [];
        }
    }

    public static void SaveProfiles(IEnumerable<ProxyProfile> profiles)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(profiles, JsonOptions);
        WriteAtomic(AppPaths.ProfilesFile, Dpapi.Protect(json));
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception e)
        {
            CrashLog.Write("Не удалось прочитать настройки", e);
        }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            WriteAtomic(AppPaths.SettingsFile, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
        }
        catch (Exception e)
        {
            CrashLog.Write("Не удалось сохранить настройки", e);
        }
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}

public static class CrashLog
{
    public static void Write(string context, Exception? error)
    {
        try
        {
            File.AppendAllText(AppPaths.CrashLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }
}
