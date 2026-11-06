using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StorageTerm.Models;
using StorageTerm.Services;

namespace StorageTerm.Controls;

public partial class TerminalPane : UserControl, IDisposable
{
    private static readonly Brush TextPrimary = Frozen("#EDEDED");
    private static readonly Brush HostInactive = Frozen("#A3A3A3");
    private static readonly Brush TextDim = Frozen("#A3A3A3");
    private static readonly Brush TextMuted = Frozen("#6E6E6E");
    private static readonly Brush Green = Frozen("#C7C7C7");
    private static readonly Brush Amber = Frozen("#8F8F8F");
    private static readonly Brush Red = Frozen("#D18B8B");
    private static readonly Brush Purple = Frozen("#B0B0B0");
    private static readonly Brush HeaderIdle = Frozen("#111111");
    private static readonly Brush HeaderActive = Frozen("#1A1A1A");

    private Brush _accentBrush = Frozen("#9A9A9A");
    private Brush _idleBorderBrush = Frozen("#242424");
    private bool _isActive;

    private MenuItem? _menuAuto;
    private MenuItem? _menuLan;
    private MenuItem? _menuExternal;
    private SshProfile? _profile;
    private bool _disposed;

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;
    public event EventHandler? ReconnectRequested;
    public event EventHandler<ConnectionMode?>? ModeChangeRequested;
    public event EventHandler? Exited;
    public event EventHandler<string>? Notification;

    public Guid Id { get; } = Guid.NewGuid();
    public int Index { get; private set; }
    public int SessionSlot { get; set; }
    public string Alias { get; private set; } = "storage";
    public string ConnectionLabel { get; private set; } = "storage";
    public ConnectionMode ResolvedMode { get; private set; } = ConnectionMode.External;
    public ConnectionMode? ModeOverride { get; set; }
    public PaneStatus Status { get; private set; } = PaneStatus.Connecting;
    public string StatusLabel => StatusText.Text;
    public bool IsDemo { get; private set; }
    public bool IsExitHandled { get; private set; }
    public DateTime StartedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ConnectedAt { get; private set; }
    public int ReconnectAttempts { get; set; }
    public bool IsClosed => _disposed;
    public string BranchTitle { get; private set; } = "—";
    public Brush AccentBrush => _accentBrush;
    public Color AccentColor => ((SolidColorBrush)_accentBrush).Color;

    public TerminalPane()
    {
        InitializeComponent();
        Header.ContextMenu = BuildContextMenu();
        Term.Terminal.GotFocus += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        Term.Terminal.PreviewMouseDown += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        ApplyAppearance("Consolas", 13);
        SetStatus(PaneStatus.Connecting, "iniciando…");
    }

    public void Initialize(
        string command,
        string alias,
        ConnectionMode resolvedMode,
        ConnectionMode? modeOverride,
        bool demo,
        AppSettings settings,
        SshProfile? profile = null)
    {
        Term.StartupCommandLine = command;
        Term.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Term.Win32InputMode = false;
        Term.LogConPTYOutput = false;

        _profile = profile;
        ApplyAppearance(settings.FontFamily, settings.FontSize);
        ApplyIdentity(alias, resolvedMode, modeOverride, demo);

        StartedAt = DateTime.UtcNow;
        ConnectedAt = null;
        IsExitHandled = false;
        SetStatus(PaneStatus.Connecting, "iniciando…");
    }

    public void RestartWith(
        string command,
        string alias,
        ConnectionMode resolvedMode,
        ConnectionMode? modeOverride,
        bool demo,
        SshProfile? profile = null)
    {
        if (_disposed) return;

        IsExitHandled = false;
        ConnectedAt = null;
        StartedAt = DateTime.UtcNow;
        Term.StartupCommandLine = command;
        _profile = profile;
        ApplyIdentity(alias, resolvedMode, modeOverride, demo);
        SetStatus(PaneStatus.Connecting, "reconectando…");

        try
        {
            Term.ConPTYTerm?.ClearUITerminal(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao limpar o terminal: {ex.Message}");
        }

        _ = Term.RestartTerm(null!, true);
    }

    public void ApplyIdentity(string alias, ConnectionMode resolvedMode, ConnectionMode? modeOverride, bool demo)
    {
        Alias = alias;
        ResolvedMode = resolvedMode;
        ModeOverride = modeOverride;
        IsDemo = demo;

        HostText.Text = demo ? "shell local" : alias;
        HostText.ToolTip = demo ? "Modo demonstração (shell local)" : BuildIdentityHelp();
        ConnectionLabel = demo ? "demo" : _profile is { Found: true } p ? p.UserAtAlias : alias;

        if (demo)
        {
            ModeText.Text = "DEMO";
            ModeText.Foreground = Purple;
            ModeDot.Fill = Purple;
            ModeBadge.Background = Frozen("#181818");
            ModeBadge.BorderBrush = Frozen("#383838");
        }
        else if (resolvedMode == ConnectionMode.Lan)
        {
            ModeText.Text = "LAN";
            ModeText.Foreground = Green;
            ModeDot.Fill = Green;
            ModeBadge.Background = Frozen("#1A1A1A");
            ModeBadge.BorderBrush = Frozen("#3A3A3A");
        }
        else
        {
            ModeText.Text = "EXT";
            ModeText.Foreground = Amber;
            ModeDot.Fill = Amber;
            ModeBadge.Background = Frozen("#161616");
            ModeBadge.BorderBrush = Frozen("#333333");
        }

        ModeBadge.ToolTip = BuildIdentityHelp();
    }

    public void SetIndex(int index)
    {
        Index = index;
        IndexText.Text = $"#{index}";
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        PaneBorder.BorderBrush = active ? _accentBrush : _idleBorderBrush;
        Header.Background = active ? HeaderActive : HeaderIdle;
        ActiveBar.Background = active ? _accentBrush : Brushes.Transparent;
        HostText.Foreground = active ? TextPrimary : HostInactive;
        IndexText.Foreground = active ? _accentBrush : TextMuted;
    }

    public void SetAccentColor(Color color)
    {
        _accentBrush = Frozen(color);
        _idleBorderBrush = Frozen(Color.FromArgb(70, color.R, color.G, color.B));
        ColorBar.Background = _accentBrush;
        SetActive(_isActive);
    }

    public void SetBranch(string branch)
    {
        BranchTitle = string.IsNullOrWhiteSpace(branch) ? "—" : branch.Trim();
    }

    public void PollProcess()
    {
        if (_disposed || IsExitHandled) return;

        var term = Term.ConPTYTerm;
        if (term == null) return;

        bool started;
        try { started = term.TermProcIsStarted; }
        catch { return; }

        if (!started) return;

        if (HasExited(term))
        {
            IsExitHandled = true;
            ConnectedAt = null;
            SetStatus(PaneStatus.Disconnected, "desconectado");
            Exited?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Status == PaneStatus.Connecting &&
            DateTime.UtcNow - StartedAt > TimeSpan.FromMilliseconds(1200))
        {
            ConnectedAt = DateTime.UtcNow;
            SetStatus(PaneStatus.Connected, "conectado");
        }
    }

    public void SetStatus(PaneStatus status, string? detail = null)
    {
        Status = status;
        StatusText.Text = detail ?? status switch
        {
            PaneStatus.Connecting => "conectando…",
            PaneStatus.Connected => "conectado",
            PaneStatus.Reconnecting => "reconectando…",
            PaneStatus.Disconnected => "desconectado",
            _ => "encerrado"
        };

        (StatusDot.Fill, StatusText.Foreground) = status switch
        {
            PaneStatus.Connected => (Green, TextDim),
            PaneStatus.Connecting => (Amber, TextDim),
            PaneStatus.Reconnecting => (Amber, Amber),
            PaneStatus.Disconnected => (Red, Red),
            _ => (TextMuted, TextMuted)
        };

        var connected = status == PaneStatus.Connected;
        StatusText.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        StatusDot.ToolTip = StatusText.Text;

        StatusText.ToolTip = status switch
        {
            PaneStatus.Disconnected when IsDemo => "O shell local terminou. Use ↻ para reiniciar.",
            PaneStatus.Disconnected => "A conexão SSH terminou. Use ↻ para reconectar.",
            PaneStatus.Reconnecting => "Aguardando a próxima tentativa automática.",
            _ => null
        };
    }

    public void SetReconnectCountdown(int seconds)
    {
        SetStatus(PaneStatus.Reconnecting, $"em {Math.Max(0, seconds)}s…");
    }

    public void FocusTerminal()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            try
            {
                Term.Terminal.Focus();
                Keyboard.Focus(Term.Terminal);

                if (PresentationSource.FromVisual(Term.Terminal) == null) return;

                var topLeft = Term.Terminal.PointToScreen(new Point(0, 0));
                var bottomRight = Term.Terminal.PointToScreen(
                    new Point(Term.Terminal.ActualWidth, Term.Terminal.ActualHeight));

                var center = new NativePoint
                {
                    X = (int)((topLeft.X + bottomRight.X) / 2),
                    Y = (int)((topLeft.Y + bottomRight.Y) / 2)
                };

                // O terminal é um HWND nativo (HwndHost). O foco WPF sozinho não
                // entrega o teclado ao ConPTY; precisamos focar o filho nativo.
                var native = WindowFromPoint(center);
                if (native != IntPtr.Zero)
                {
                    SetFocus(native);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao focar terminal: {ex.Message}");
            }
        });
    }

    public void ApplyAppearance(string fontFamily, double fontSize)
    {
        var resolved = TerminalThemeFactory.ResolveFontFamily(fontFamily);
        var rounded = (short)Math.Clamp(Math.Round(fontSize), 9, 24);

        Term.FontFamilyWhenSettingTheme = new FontFamily(resolved);
        Term.FontSizeWhenSettingTheme = rounded;

        if (PresentationSource.FromVisual(Term.Terminal) != null)
        {
            Term.Terminal.SetTheme(TerminalThemeFactory.Create(), resolved, rounded);
        }
        else
        {
            Term.Theme = TerminalThemeFactory.Create();
        }
    }

    public string GetSelectedText()
    {
        try { return Term.Terminal.GetSelectedText() ?? string.Empty; }
        catch { return string.Empty; }
    }

    public bool SendText(string text)
    {
        var term = Term.ConPTYTerm;
        if (term == null) return false;

        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        try
        {
            term.WriteToTerm(normalized.AsSpan());
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao enviar texto para o terminal: {ex.Message}");
            return false;
        }
    }

    public void CopySelection()
    {
        var text = GetSelectedText();
        if (string.IsNullOrEmpty(text))
        {
            Notification?.Invoke(this, "Nenhum texto selecionado — arraste com o mouse para selecionar.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            Notification?.Invoke(this, "Seleção copiada.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao copiar: {ex.Message}");
            Notification?.Invoke(this, "Não foi possível copiar para a área de transferência.");
        }
    }

    public void PasteClipboard() => _ = PasteClipboardAsync();

    public async Task PasteClipboardAsync()
    {
        try
        {
            // Imagem copiada (print, navegador, etc.).
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image != null)
                {
                    var localFile = ImagePasteService.SaveClipboardImage(image);
                    await UploadImagesAndPasteAsync(new[] { localFile });
                    return;
                }
            }

            // Arquivos de imagem copiados no Explorer.
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = ImagePasteService.FilterImages(
                    System.Windows.Clipboard.GetFileDropList().Cast<string>());

                if (files.Count > 0)
                {
                    await UploadImagesAndPasteAsync(files);
                    return;
                }
            }

            if (!System.Windows.Clipboard.ContainsText())
            {
                Notification?.Invoke(this, "A área de transferência não contém texto ou imagem.");
                return;
            }

            if (SendText(System.Windows.Clipboard.GetText()))
            {
                Notification?.Invoke(this, "Texto colado.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao colar: {ex.Message}");
            Notification?.Invoke(this, "Não foi possível ler a área de transferência.");
        }
    }

    private async Task UploadImagesAndPasteAsync(IReadOnlyList<string> localFiles)
    {
        if (IsDemo)
        {
            Notification?.Invoke(this, "Modo demonstração: envio de imagem indisponível.");
            return;
        }

        try
        {
            Notification?.Invoke(this, "Enviando imagem para o servidor…");
            var remotePaths = await ImagePasteService.UploadAsync(Alias, localFiles);
            var text = string.Join(" ", remotePaths);

            if (SendText(text))
            {
                Notification?.Invoke(this, $"Imagem enviada: {text}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao enviar imagem: {ex.Message}");
            Notification?.Invoke(this, $"Não foi possível enviar a imagem: {ex.Message}");
        }
    }

    public void WriteSystemMessage(string message, bool error = false)
    {
        var term = Term.ConPTYTerm;
        if (term == null) return;

        var color = error ? "\u001b[38;2;248;113;113m" : "\u001b[38;2;126;143;163m";
        try
        {
            term.WriteToUITerminal($"\r\n{color}[StorageTerm] {message}\u001b[0m\r\n".AsSpan());
        }
        catch
        {
            // The process may already be gone.
        }
    }

    public void ClearScreen()
    {
        try { Term.ConPTYTerm?.ClearUITerminal(false); }
        catch { }
    }

    private string BuildIdentityHelp()
    {
        if (IsDemo)
        {
            return "Modo demonstração (shell local)";
        }

        if (_profile is { Found: true } profile)
        {
            return $"{profile.UserAtAlias} · {profile.Endpoint} · {profile.IdentityName}";
        }

        var mode = ModeOverride switch
        {
            ConnectionMode.Lan => "LAN forçada",
            ConnectionMode.External => "externo forçado",
            _ => "automático"
        };

        return $"{Alias} · perfil não encontrado em ~/.ssh/config · {mode}";
    }

    private static bool HasExited(Wpf.Ui.Terminal.TermPTY term)
    {
        try
        {
            var process = term.Process;
            return process != null && process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var reconnect = new MenuItem { Header = "Reconectar agora" };
        reconnect.Click += (_, _) => ReconnectRequested?.Invoke(this, EventArgs.Empty);

        var auto = new MenuItem { Header = "Conexão automática", IsCheckable = true };
        auto.Click += (_, _) => ModeChangeRequested?.Invoke(this, null);

        var lan = new MenuItem { Header = "Forçar LAN (storage-lan)", IsCheckable = true };
        lan.Click += (_, _) => ModeChangeRequested?.Invoke(this, ConnectionMode.Lan);

        var external = new MenuItem { Header = "Forçar externo (storage)", IsCheckable = true };
        external.Click += (_, _) => ModeChangeRequested?.Invoke(this, ConnectionMode.External);

        var copy = new MenuItem { Header = "Copiar seleção", InputGestureText = "Ctrl+Shift+C" };
        copy.Click += (_, _) => CopySelection();

        var paste = new MenuItem { Header = "Colar", InputGestureText = "Ctrl+V" };
        paste.Click += (_, _) => PasteClipboard();

        var clear = new MenuItem { Header = "Limpar tela (visual)" };
        clear.Click += (_, _) => ClearScreen();

        var close = new MenuItem { Header = "Fechar terminal", InputGestureText = "Ctrl+Q" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(reconnect);
        menu.Items.Add(new Separator());
        menu.Items.Add(auto);
        menu.Items.Add(lan);
        menu.Items.Add(external);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);

        menu.Opened += (_, _) =>
        {
            auto.IsChecked = ModeOverride is null;
            lan.IsChecked = ModeOverride == ConnectionMode.Lan;
            external.IsChecked = ModeOverride == ConnectionMode.External;
            auto.IsEnabled = lan.IsEnabled = external.IsEnabled = !IsDemo;
        };

        _menuAuto = auto;
        _menuLan = lan;
        _menuExternal = external;
        return menu;
    }

    private void PaneBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);

        if (!IsInsideButton(e.OriginalSource as DependencyObject))
        {
            FocusTerminal();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Header_MouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    private void ModeBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Activated?.Invoke(this, EventArgs.Empty);

        if (Header.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ModeBadge;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ReconnectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PaneMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Activated?.Invoke(this, EventArgs.Empty);
        OpenPaneMenu(PaneMenuButton);
    }

    public void OpenPaneMenu(FrameworkElement target)
    {
        if (Header.ContextMenu is { } menu)
        {
            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = -140;
            menu.IsOpen = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { Term.Dispose(); }
        catch (Exception ex) { Log.Warn($"Falha ao descartar terminal: {ex.Message}"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    private static Brush Frozen(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return Frozen(color);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}



