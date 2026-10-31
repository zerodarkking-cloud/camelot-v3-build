using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Camelot.Core;

namespace Camelot.App;

/// <summary>Редактор прокси. Работает с копией профиля и отдаёт результат только после проверки полей.</summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ProxyProfile _profile;
    private CancellationTokenSource? _check;

    public ProfileEditorWindow(ProxyProfile profile, bool isNew)
    {
        InitializeComponent();
        _profile = profile.Clone();
        Title = isNew ? "Новый прокси" : "Изменить прокси";

        Fill(ProtocolBox, Enum.GetValues<ProxyProtocol>().Select(p => (p.Title(), (object)p)));
        Fill(SsMethodBox, ProxyProfile.SsMethods.Select(m => (m, (object)m)));
        Fill(SecurityBox, Enum.GetValues<VlessSecurity>().Select(s => (s.Title(), (object)s)));
        Fill(FingerprintBox, ProxyProfile.Fingerprints.Select(f => (f, (object)f)));

        Load(_profile);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        Closed += (_, _) => _check?.Cancel();
    }

    public ProxyProfile? Result { get; private set; }

    private void Load(ProxyProfile p)
    {
        NameBox.Text = p.Name;
        Select(ProtocolBox, p.Protocol);
        ServerBox.Text = p.Server;
        PortBox.Text = p.Port.ToString(CultureInfo.InvariantCulture);

        UsernameBox.Text = p.Username;
        PasswordHidden.Password = p.Password;
        HttpsCheck.IsChecked = p.UseTls;

        Select(SsMethodBox, ProxyProfile.SsMethods.Contains(p.SsMethod) ? p.SsMethod : "aes-128-gcm");
        SsPasswordHidden.Password = p.SsPassword;

        UuidBox.Text = p.Uuid;
        FlowCheck.IsChecked = p.Flow == "xtls-rprx-vision";
        Select(SecurityBox, p.Security);
        SniBox.Text = p.Sni;
        Select(FingerprintBox, ProxyProfile.Fingerprints.Contains(p.Fingerprint) ? p.Fingerprint : "chrome");
        PublicKeyBox.Text = p.RealityPublicKey;
        ShortIdBox.Text = p.RealityShortId;

        UpdateVisibility();
    }

    /// <summary>Собирает профиль из полей. null — поле заполнено неверно (текст ошибки уже показан).</summary>
    private ProxyProfile? ReadForm()
    {
        var p = _profile.Clone();
        p.Name = NameBox.Text.Trim();
        p.Protocol = Selected<ProxyProtocol>(ProtocolBox);
        p.Server = ServerBox.Text.Trim().Trim('[', ']').Trim();

        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            ShowStatus("Порт должен быть числом от 1 до 65535", success: false);
            PortBox.Focus();
            return null;
        }
        p.Port = port;

        p.Username = UsernameBox.Text.Trim();
        p.Password = ReadSecret(PasswordHidden, PasswordVisible);
        p.UseTls = p.Protocol == ProxyProtocol.Http && HttpsCheck.IsChecked == true;

        p.SsMethod = Selected<string>(SsMethodBox);
        p.SsPassword = ReadSecret(SsPasswordHidden, SsPasswordVisible);

        p.Uuid = UuidBox.Text.Trim();
        p.Flow = FlowCheck.IsChecked == true ? "xtls-rprx-vision" : "";
        p.Security = Selected<VlessSecurity>(SecurityBox);
        p.Sni = SniBox.Text.Trim();
        p.Fingerprint = Selected<string>(FingerprintBox);
        p.RealityPublicKey = PublicKeyBox.Text.Trim();
        p.RealityShortId = ShortIdBox.Text.Trim();

        if (p.Name.Length == 0)
        {
            p.Name = p.NormalizedServer.Length > 0 ? $"{p.Protocol.Title()} · {p.NormalizedServer}" : "Мой прокси";
        }

        try
        {
            p.Validate();
        }
        catch (ProfileException ex)
        {
            ShowStatus(ex.Message, success: false);
            return null;
        }
        return p;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ReadForm() is not ProxyProfile p)
        {
            return;
        }
        Result = p;
        DialogResult = true;
    }

    /// <summary>Подключается к прокси напрямую и проверяет логин, пароль и тип прокси.</summary>
    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (ReadForm() is not ProxyProfile p)
        {
            return;
        }
        if (!p.Protocol.IsPlainProxy())
        {
            ShowStatus("Проверка перед сохранением доступна для SOCKS5 и HTTP. Shadowsocks и VLESS проверяются при подключении.", success: false);
            return;
        }

        _check?.Cancel();
        var check = new CancellationTokenSource();
        _check = check;
        CheckButton.IsEnabled = false;
        ShowStatus("Проверка прокси…", success: null);
        try
        {
            var result = await ProxyProbe.CheckAsync(p, cancellationToken: check.Token);
            if (check.IsCancellationRequested)
            {
                return;
            }
            if (result.Success && result.Protocol is ProxyProtocol detected)
            {
                // Тип подобран автоматически — переносим его в поля.
                Select(ProtocolBox, detected);
                HttpsCheck.IsChecked = result.UseTls;
                UpdateVisibility();
            }
            ShowStatus(result.Message, result.Success);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_check, check))
            {
                CheckButton.IsEnabled = true;
            }
        }
    }

    private void ShowStatus(string message, bool? success)
    {
        ErrorText.Text = message;
        var brush = success switch
        {
            true => "SystemFillColorSuccessBrush",
            false => "SystemFillColorCriticalBrush",
            null => "TextFillColorSecondaryBrush",
        };
        ErrorText.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    // ---------------------------------------------------------------- видимость секций

    private void ProtocolBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisibility();

    private void SecurityBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisibility();

    private void SsMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (!IsInitialized || ProtocolBox.SelectedItem is null)
        {
            return;
        }
        var protocol = Selected<ProxyProtocol>(ProtocolBox);
        PlainPanel.Visibility = protocol.IsPlainProxy() ? Visibility.Visible : Visibility.Collapsed;
        SsPanel.Visibility = protocol == ProxyProtocol.Shadowsocks ? Visibility.Visible : Visibility.Collapsed;
        VlessPanel.Visibility = protocol == ProxyProtocol.Vless ? Visibility.Visible : Visibility.Collapsed;
        HttpsCheck.Visibility = protocol == ProxyProtocol.Http ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Visibility = protocol.IsPlainProxy() ? Visibility.Visible : Visibility.Collapsed;

        if (SecurityBox.SelectedItem is not null)
        {
            RealityPanel.Visibility = Selected<VlessSecurity>(SecurityBox) == VlessSecurity.Reality
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        ServerHint.Text = protocol.IsPlainProxy()
            ? "Адрес и порт прокси, как их выдал провайдер."
            : "Адрес вашего сервера: домен или IPv4.";
        PlainHint.Text = protocol == ProxyProtocol.Http
            ? "Обычный HTTP-прокси провайдера работает и с HTTPS-сайтами — галочка «HTTPS-прокси» ему не нужна. " +
              "Включайте её, только если провайдер прямо назвал прокси HTTPS. Не уверены — нажмите «Проверить»: тип определится сам."
            : "Оставьте поля пустыми, если прокси без логина. Не уверены в типе прокси — нажмите «Проверить»: тип определится сам.";

        if (SsMethodBox.SelectedItem is not null)
        {
            var keyLength = ProxyProfile.Ss2022KeyLength(Selected<string>(SsMethodBox));
            SsHint.Text = keyLength is int length
                ? $"Для этого метода пароль — ключ base64 длиной {length} байт (sing-box generate rand --base64 {length})."
                : "";
            SsHint.Visibility = keyLength is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ---------------------------------------------------------------- пароли

    private void PasswordToggle_Click(object sender, RoutedEventArgs e) =>
        ToggleSecret(PasswordHidden, PasswordVisible, PasswordToggle);

    private void SsPasswordToggle_Click(object sender, RoutedEventArgs e) =>
        ToggleSecret(SsPasswordHidden, SsPasswordVisible, SsPasswordToggle);

    private static void ToggleSecret(PasswordBox hidden, TextBox visible, Button toggle)
    {
        if (visible.Visibility == Visibility.Visible)
        {
            hidden.Password = visible.Text;
            visible.Visibility = Visibility.Collapsed;
            hidden.Visibility = Visibility.Visible;
            toggle.Content = "";
            toggle.ToolTip = "Показать пароль";
            hidden.Focus();
        }
        else
        {
            visible.Text = hidden.Password;
            hidden.Visibility = Visibility.Collapsed;
            visible.Visibility = Visibility.Visible;
            toggle.Content = "";
            toggle.ToolTip = "Скрыть пароль";
            visible.Focus();
            visible.CaretIndex = visible.Text.Length;
        }
    }

    private static string ReadSecret(PasswordBox hidden, TextBox visible) =>
        visible.Visibility == Visibility.Visible ? visible.Text : hidden.Password;

    // ---------------------------------------------------------------- ComboBox

    private static void Fill(ComboBox box, System.Collections.Generic.IEnumerable<(string Title, object Value)> items)
    {
        foreach (var (title, value) in items)
        {
            box.Items.Add(new ComboBoxItem { Content = title, Tag = value });
        }
    }

    private static void Select(ComboBox box, object value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (Equals(item.Tag, value))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private static T Selected<T>(ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: T value } ? value : (T)((ComboBoxItem)box.Items[0]).Tag;
}
