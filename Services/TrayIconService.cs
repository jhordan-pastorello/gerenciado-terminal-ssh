using System.IO;
using System.Drawing;
using StorageTerm.Models;
using WinForms = System.Windows.Forms;

namespace StorageTerm.Services;

/// <summary>Ícone na bandeja com menu de contexto. Permite manter o app vivo sem janela.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Stream _iconStream;
    private readonly WinForms.ToolStripMenuItem _newSessionItem;
    private readonly WinForms.ToolStripMenuItem _keepAwakeItem;
    private readonly WinForms.ToolStripMenuItem _startWithWindowsItem;
    private readonly WinForms.ToolStripMenuItem _openItem;

    public TrayIconService(
        Action showWindow,
        Action newSession,
        Action<ConnectionMode> newSessionWithMode,
        Action openSettings,
        Action exit,
        Action<bool> setKeepAwake,
        Action<bool> setStartWithWindows)
    {
        _iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/StorageTerm.ico"))?.Stream
            ?? throw new InvalidOperationException("Ícone do aplicativo não encontrado.");

        _icon = new Icon(_iconStream);

        _openItem = new WinForms.ToolStripMenuItem("Abrir StorageTerm") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _openItem.Click += (_, _) => showWindow();

        _newSessionItem = new WinForms.ToolStripMenuItem("Nova sessão");
        _newSessionItem.Click += (_, _) => newSession();

        var newLan = new WinForms.ToolStripMenuItem("Nova sessão · LAN");
        newLan.Click += (_, _) => newSessionWithMode(ConnectionMode.Lan);

        var newExternal = new WinForms.ToolStripMenuItem("Nova sessão · externo");
        newExternal.Click += (_, _) => newSessionWithMode(ConnectionMode.External);

        _keepAwakeItem = new WinForms.ToolStripMenuItem("Manter PC acordado")
        {
            CheckOnClick = false
        };
        _keepAwakeItem.Click += (_, _) =>
        {
            var next = !_keepAwakeItem.Checked;
            _keepAwakeItem.Checked = next;
            setKeepAwake(next);
        };

        _startWithWindowsItem = new WinForms.ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = false
        };
        _startWithWindowsItem.Click += (_, _) =>
        {
            var next = !_startWithWindowsItem.Checked;
            _startWithWindowsItem.Checked = next;
            setStartWithWindows(next);
        };

        var settings = new WinForms.ToolStripMenuItem("Configurações…");
        settings.Click += (_, _) => openSettings();

        var exitItem = new WinForms.ToolStripMenuItem("Sair");
        exitItem.Click += (_, _) => exit();

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(12, 20, 29),
            ForeColor = Color.FromArgb(220, 231, 242),
            ShowImageMargin = true,
            ShowCheckMargin = true,
            Padding = new WinForms.Padding(3),
            Font = new Font("Segoe UI", 9f),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColorTable())
        };

        menu.Items.Add(_openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_newSessionItem);
        menu.Items.Add(newLan);
        menu.Items.Add(newExternal);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_keepAwakeItem);
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = Color.FromArgb(220, 231, 242);
            if (item is WinForms.ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new WinForms.Padding(0, 3, 0, 3);
            }
        }


        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "StorageTerm",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => showWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => showWindow();
    }

    public void ShowBalloon(string text)
    {
        try
        {
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(3500, "StorageTerm", text, WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // A bandeja pode estar indisponível em sessões remotas.
        }
    }

    public void UpdateState(int panes, int connected, bool keepAwake, bool startWithWindows, string tooltip)
    {
        try
        {
            _newSessionItem.Text = panes >= AppSettings.MaxTerminals
                ? "Nova sessão (limite de 6)"
                : $"Nova sessão ({panes}/6)";
            _newSessionItem.Enabled = panes < AppSettings.MaxTerminals;

            _keepAwakeItem.Checked = keepAwake;
            _startWithWindowsItem.Checked = startWithWindows;

            var text = tooltip.Length > 62 ? tooltip[..62] : tooltip;
            _notifyIcon.Text = string.IsNullOrWhiteSpace(text) ? "StorageTerm" : text;
            _ = connected;
        }
        catch
        {
            // Tooltip tem limite de 63 caracteres em algumas versões do Windows.
        }
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch
        {
            // ignore
        }

        _icon.Dispose();
        _iconStream.Dispose();
    }

    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(12, 20, 29);
        private static readonly Color Hover = Color.FromArgb(24, 35, 47);
        private static readonly Color Border = Color.FromArgb(27, 38, 51);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Color.FromArgb(14, 55, 63);
        public override Color CheckSelectedBackground => Color.FromArgb(20, 70, 80);
        public override Color CheckPressedBackground => Color.FromArgb(20, 70, 80);
    }
}



