using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Camelot.App.Services;
using Camelot.App.ViewModels;
using Camelot.Core;

namespace Camelot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Выход из приложения уже начат — окно можно закрыть по-настоящему.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Вызывается при попытке закрыть окно, пока туннель включён: приложение уходит в трей.</summary>
    public event Action? HiddenToTray;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (AllowClose)
        {
            return;
        }
        e.Cancel = true;
        if (_vm.State != TunnelState.Disconnected)
        {
            Hide();
            HiddenToTray?.Invoke();
        }
        else
        {
            // Выход запускаем после обработчика закрытия: во время Closing окно нельзя закрывать повторно.
            Dispatcher.InvokeAsync(() => ((App)Application.Current).ExitApplication());
        }
    }

    // ---------------------------------------------------------------- главная

    private void OpenProxies_Click(object sender, RoutedEventArgs e)
    {
        Tabs.SelectedIndex = 1;
        if (!_vm.HasProfiles)
        {
            Paste_Click(sender, e);
        }
    }

    // ---------------------------------------------------------------- список прокси

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (COMException)
        {
            text = "";
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this,
                "В буфере обмена нет текста. Скопируйте строку прокси, например socks5://host:port:логин:пароль, и нажмите кнопку снова.",
                "Camelot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (profiles, errors) = LinkParser.ParseMany(text);
        if (profiles.Count == 0)
        {
            MessageBox.Show(this,
                errors.FirstOrDefault() ?? "Не удалось распознать прокси в буфере обмена.",
                "Не удалось импортировать", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (profiles.Count == 1 && errors.Count == 0)
        {
            // Один прокси — открываем редактор, чтобы проверить поля перед сохранением.
            EditProfile(profiles[0], isNew: true);
            return;
        }

        foreach (var profile in profiles)
        {
            _vm.Save(profile);
        }
        var message = $"Добавлено прокси: {profiles.Count}.";
        if (errors.Count > 0)
        {
            message += $"\n\nНе распознано строк: {errors.Count}. Первая ошибка: {errors[0]}";
        }
        MessageBox.Show(this, message, "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => EditProfile(new ProxyProfile(), isNew: true);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileItem item })
        {
            EditProfile(item.Profile.Clone(), isNew: false);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProfileItem item })
        {
            return;
        }
        var answer = MessageBox.Show(this, $"Удалить прокси «{item.Name}»?", "Camelot",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _vm.Delete(item);
        }
    }

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ProfileList, source) is ListBoxItem { DataContext: ProfileItem item })
        {
            EditProfile(item.Profile.Clone(), isNew: false);
        }
    }

    private void EditProfile(ProxyProfile profile, bool isNew)
    {
        var editor = new ProfileEditorWindow(profile, isNew) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is ProxyProfile saved)
        {
            _vm.Save(saved);
        }
    }

    // ---------------------------------------------------------------- журнал

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.GetFullLog());
        }
        catch (COMException)
        {
            MessageBox.Show(this, "Буфер обмена занят другой программой. Попробуйте ещё раз.", "Camelot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LocalDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Открытие папки", ex);
        }
    }
}
