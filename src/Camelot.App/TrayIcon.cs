using System;
using Camelot.App.ViewModels;
using Forms = System.Windows.Forms;

namespace Camelot.App;

/// <summary>Значок в области уведомлений: подключить/отключить, открыть окно, выйти.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;

    public TrayIcon(MainViewModel vm, Action showWindow, Action exit)
    {
        _vm = vm;
        _toggleItem = new Forms.ToolStripMenuItem("Подключить", null, (_, _) => _ = _vm.ToggleAsync());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Открыть Camelot", null, (_, _) => showWindow()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Camelot",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                showWindow();
            }
        };

        _vm.StateChanged += Update;
        Update();
    }

    public void ShowHint(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    private void Update()
    {
        var active = _vm.State is TunnelState.Connected or TunnelState.Connecting;
        _toggleItem.Text = active ? "Отключить" : "Подключить";
        _toggleItem.Enabled = _vm.State != TunnelState.Disconnecting;
        var name = _vm.ActiveProfile?.Name ?? "";
        var text = $"Camelot — {_vm.StatusText.ToLowerInvariant()}" + (_vm.IsConnected && name.Length > 0 ? $"\n{name}" : "");
        _icon.Text = text.Length > 120 ? text[..120] : text;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/camelot.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _vm.StateChanged -= Update;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
