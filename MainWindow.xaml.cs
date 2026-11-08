using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StorageTerm.Controls;
using StorageTerm.Dialogs;
using StorageTerm.Layout;
using StorageTerm.Models;
using StorageTerm.Services;

namespace StorageTerm;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int WmLeftButtonDown = 0x0201;

    private const int HotkeyNew = 1001;
    private const int HotkeyClose = 1002;
    private const int HotkeyNext = 1003;
    private const int HotkeyPrevious = 1004;
    private const int HotkeyCopy = 1005;
    private const int HotkeyPaste = 1006;
    private const int HotkeyMaximize = 1007;
    private const int HotkeyCloseQ = 1008;
    private const int HotkeyScrollPrev = 1009;
    private const int HotkeyScrollNext = 1010;
    private const int HotkeyPaneBase = 1100;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmResumeAutomatic = 0x0012;
    private const int PbtApmResumeSuspend = 0x0007;

    private AppSettings _settings;
    private readonly StartupOptions _startup;
    private readonly bool _demo;
    private readonly ConnectionResolver _resolver = new();
    private readonly SleepGuard _sleepGuard = new();
    private readonly List<TerminalPane> _panes = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _reconnectCts = new();

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _connectivityTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _branchTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _scrollAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly UpdateService _updateService = new();

    private static readonly Color[] PaneColors =
    {
        Color.FromRgb(0x6E, 0xA8, 0xFE), // azul
        Color.FromRgb(0xB6, 0x9C, 0xFF), // lilás
        Color.FromRgb(0xF2, 0xB8, 0x80), // âmbar
        Color.FromRgb(0xF2, 0x8B, 0xA8), // rosa
        Color.FromRgb(0x7A, 0xD7, 0xF0), // ciano
        Color.FromRgb(0xF4, 0x9E, 0x7A), // laranja
        Color.FromRgb(0xA0, 0xA8, 0xFF), // índigo
        Color.FromRgb(0xE0, 0xC2, 0x85), // areia
    };

    private UpdateInfo? _pendingUpdate;
    private string? _pendingUpdateZip;
    private bool _updateCheckInFlight;
    private bool _applyingUpdate;

    private static readonly Brush NeutralPillBg = Frozen("#111111");
    private static readonly Brush NeutralPillBorder = Frozen("#2A2A2A");
    private static readonly Brush LanPillBg = Frozen("#1A1A1A");
    private static readonly Brush LanPillBorder = Frozen("#3A3A3A");
    private static readonly Brush ExtPillBg = Frozen("#161616");
    private static readonly Brush ExtPillBorder = Frozen("#333333");
    private static readonly Brush DemoPillBg = Frozen("#181818");
    private static readonly Brush DemoPillBorder = Frozen("#383838");

    private TrayIconService? _tray;
    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _keyboardHook;
    private bool _resumeInProgress;
    private TerminalPane? _active;
    private bool _shuttingDown;
    private bool _hotkeysRegistered;
    private bool _probeInFlight;
    private bool _balloonShown;
    private bool _startupComplete;
    private bool _branchFetchInFlight;
    private bool _scrollAnimating;
    private double _scrollTarget;
    private double _scrollStartOffset;
    private DateTime _scrollStartTime;
    private double _carouselStep;
    private DateTime _hoverSuppressedUntil;
    private readonly DispatcherTimer _snapTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private const double ScrollDurationMs = 240;

    public MainWindow(AppSettings settings, StartupOptions startup)
    {
        _settings = settings;
        _startup = startup;
        _demo = startup.Demo;

        InitializeComponent();

        _pollTimer.Tick += (_, _) => PollPanes();
        _connectivityTimer.Tick += (_, _) => _ = RefreshConnectivityAsync(silent: true);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            FadeToast();
        };

        _branchTimer.Tick += (_, _) => _ = RefreshBranchesAsync();
        _hoverTimer.Tick += (_, _) => UpdateHoverFocus();
        _scrollAnimTimer.Tick += (_, _) => AnimateScroll();
        _snapTimer.Tick += (_, _) =>
        {
            _snapTimer.Stop();
            SnapToNearestPage();
        };
        PaneScroller.ScrollChanged += PaneScroller_ScrollChanged;
        PaneScroller.SizeChanged += (_, _) => LayoutPanes();
        SizeChanged += (_, _) => LayoutPanes();
        StateChanged += (_, _) => LayoutPanes();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Activated += (_, _) => RegisterHotkeys();
        Deactivated += (_, _) => UnregisterHotkeys();
        PreviewKeyDown += OnPreviewKeyDown;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;

        if (_startup.StartInTray)
        {
            Opacity = 0;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        WindowTheme.Apply(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        Log.Info($"DPI escala={dpi.DpiScaleX} workArea={SystemParameters.WorkArea} primary={SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}");
        RestoreWindowPlacement();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncAutoStartSetting();
            InitializeTray();

            await StartSessionsAsync();

            _pollTimer.Start();
            _connectivityTimer.Start();
            _branchTimer.Start();
            _hoverTimer.Start();
            _ = RefreshBranchesAsync();
            _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(manual: false);
            _updateTimer.Start();
            _ = InitialUpdateCheckAsync();
            _startupComplete = true;
            UpdateChrome();

            if (_startup.StartInTray && _settings.StartMinimized)
            {
                await Task.Delay(900);
                Hide();
                Opacity = 1;
                _tray?.ShowBalloon("StorageTerm está na bandeja. As sessões SSH continuam ativas.");
            }
            else
            {
                Opacity = 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha na inicialização da janela.", ex);
            Opacity = 1;
            System.Windows.MessageBox.Show(
                this,
                "Não foi possível iniciar o StorageTerm.\n\n" + ex.Message +
                "\n\nDetalhes em:\n" + AppPaths.LogFile,
                "StorageTerm",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task StartSessionsAsync()
    {
        var requested = Math.Clamp(_startup.RequestedPanes, 1, AppSettings.MaxSessionSlots);

        if (_demo)
        {
            EmptyTitle.Text = "Modo demonstração";
            EmptySubtitle.Text = "Abrindo shells locais para testar o layout.";

            for (var i = 0; i < requested; i++)
            {
                await AddPaneAsync(null, demo: true, focus: i == requested - 1);
            }

            return;
        }

        EmptyTitle.Text = "Detectando conexão…";
        EmptySubtitle.Text =
            $"Testando storage-lan em {_settings.LanProbeHost}:{_settings.LanProbePort} (LAN). " +
            "Se não responder, uso o perfil storage (Tailscale).";

        var probe = await _resolver.ProbeAsync(_settings);
        Log.Info($"Sonda inicial LAN {_settings.LanProbeHost}:{_settings.LanProbePort} => " +
                 $"{(probe.LanReachable ? "ok" : "sem resposta")} ({probe.LatencyMs} ms)");

        var alias = probe.LanReachable ? _settings.LanAlias : _settings.ExternalAlias;
        var profile = SshConfigReader.Resolve(alias);
        Log.Info($"Identificação SSH {alias}: {profile.Summary} · chave {profile.IdentityName} · alias encontrado={profile.Found}");

        EmptySubtitle.Text = !profile.Found
            ? $"Alias {alias} não encontrado em ~/.ssh/config. Verifique as configurações."
            : probe.LanReachable
                ? $"LAN disponível ({probe.LatencyMs} ms) — conectando como {profile.UserAtAlias} em {profile.Endpoint}…"
                : $"LAN indisponível — conectando como {profile.UserAtAlias} via Tailscale…";

        UpdateChrome();

        for (var i = 0; i < requested; i++)
        {
            await AddPaneAsync(null, demo: false, focus: i == requested - 1);
        }
    }

    private async Task<TerminalPane?> AddPaneAsync(ConnectionMode? overrideMode, bool demo = false, bool focus = true)
    {
        if (_shuttingDown) return null;

        demo = demo || _demo;

        ConnectionTarget target;
        string command;
        SshProfile? profile = null;
        var sessionSlot = 0;

        if (demo)
        {
            target = _resolver.Demo();
            command = SshCommandBuilder.BuildDemo();
        }
        else
        {
            target = _resolver.Resolve(_settings, overrideMode);
            if (!SshCommandBuilder.IsValidAlias(target.Alias))
            {
                ShowToast($"Alias SSH inválido: '{target.Alias}'. Abra as configurações.", error: true);
                return null;
            }

            sessionSlot = AllocateSessionSlot();
            var tmuxSession = _settings.PersistentSession ? $"storage-{sessionSlot}" : null;

            try
            {
                command = SshCommandBuilder.Build(target.Alias, tmuxSession);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao montar o comando SSH.", ex);
                ShowToast(ex.Message, error: true);
                return null;
            }

            profile = SshConfigReader.Resolve(target.Alias);
            Log.Info($"Perfil SSH {target.Alias}: {profile.Summary} · chave {profile.IdentityName}" +
                     (tmuxSession != null ? $" · tmux {tmuxSession}" : ""));
            if (!profile.Found)
            {
                ShowToast($"Alias '{target.Alias}' não encontrado em ~/.ssh/config.", error: true);
            }
        }

        var pane = new TerminalPane { SessionSlot = sessionSlot };
        try
        {
            pane.Initialize(command, target.Alias, target.Mode, overrideMode, demo, _settings, profile);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao inicializar o terminal.", ex);
            pane.Dispose();
            ShowToast("Falha ao iniciar o terminal. Veja o log.", error: true);
            return null;
        }

        WirePane(pane);
        _panes.Add(pane);
        PaneHost.Children.Add(pane);
        LayoutPanes();
        UpdateChrome();

        if (focus) SetActivePane(pane, focus: true);

        var label = demo ? "shell local" : target.Alias;
        ShowToast($"Terminal {pane.Index} · {label} · {ModeLabel(target)}");
        Log.Info($"Terminal {pane.Index} criado: {command}");

        _ = RefreshBranchesAfterDelayAsync();

        await Task.CompletedTask;
        return pane;
    }

    private void WirePane(TerminalPane pane)
    {
        pane.Activated += (_, _) => SetActivePane(pane, focus: false);
        pane.ReconnectRequested += (_, _) => _ = ReconnectPaneAsync(pane, manual: true);
        pane.CloseRequested += (_, _) => _ = ClosePaneAsync(pane);
        pane.Exited += (_, _) => _ = HandlePaneExitedAsync(pane);
        pane.Notification += (_, message) => ShowToast(message);
        pane.ModeChangeRequested += (_, mode) => _ = ChangePaneModeAsync(pane, mode);
    }

    private int AllocateSessionSlot()
    {
        var slot = 1;
        while (_panes.Any(p => p.SessionSlot == slot)) slot++;
        return slot;
    }

    private void LayoutPanes()
    {
        if (_shuttingDown) return;

        var viewportWidth = PaneScroller.ActualWidth;
        var viewportHeight = PaneScroller.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        const double gap = 6;
        const double scrollBarHeight = 5;
        var width = Math.Max(240, (viewportWidth - gap * 2) / 2);
        var reserved = _panes.Count > 2 ? scrollBarHeight : 0;
        var height = Math.Max(120, viewportHeight - gap - reserved);
        _carouselStep = width + gap;

        for (var i = 0; i < _panes.Count; i++)
        {
            var pane = _panes[i];
            pane.Width = width;
            pane.Height = height;
            pane.Margin = new Thickness(gap / 2);
            pane.HorizontalAlignment = HorizontalAlignment.Left;
            pane.VerticalAlignment = VerticalAlignment.Top;
            pane.SetIndex(pane.SessionSlot > 0 ? pane.SessionSlot : i + 1);
            pane.SetAccentColor(GetPaneColor(pane.SessionSlot > 0 ? pane.SessionSlot : i + 1));
            pane.SetActive(pane == _active);
        }

        EmptyState.Visibility = _panes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_panes.Count == 0) EmptyActionButton.IsEnabled = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollToActivePane();
            if (!_scrollAnimating) SnapToNearestPage();
        }), DispatcherPriority.ContextIdle);
    }

    private static Color GetPaneColor(int index)
    {
        if (index <= 0) index = 1;
        return PaneColors[(index - 1) % PaneColors.Length];
    }

    private async Task RefreshBranchesAfterDelayAsync()
    {
        try
        {
            await Task.Delay(2500);
        }
        catch
        {
            // ignore
        }

        await RefreshBranchesAsync();
    }

    private async Task RefreshBranchesAsync()
    {
        if (_branchFetchInFlight || _shuttingDown || !_settings.PersistentSession) return;

        var panes = _panes.Where(p => !p.IsDemo && p.SessionSlot > 0).ToArray();
        if (panes.Length == 0) return;

        _branchFetchInFlight = true;
        try
        {
            foreach (var group in panes.GroupBy(p => p.Alias))
            {
                var sessions = group
                    .Select(p => $"storage-{p.SessionSlot}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var branches = await BranchService.FetchAsync(group.Key, sessions);
                foreach (var pane in group)
                {
                    var key = $"storage-{pane.SessionSlot}";
                    if (branches.TryGetValue(key, out var info))
                    {
                        pane.SetBranch(info.Label, info.IsGit);
                    }
                    else
                    {
                        pane.SetBranch("—", isGit: false);
                    }
                }
            }

            UpdateActivePaneChrome();
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao atualizar branches: {ex.Message}");
        }
        finally
        {
            _branchFetchInFlight = false;
        }
    }

    private void ScrollToActivePane()
    {
        if (_active == null || _panes.Count == 0 || PaneScroller.ViewportWidth <= 0) return;

        var left = _active.TranslatePoint(new Point(0, 0), PaneHost).X;
        var right = left + _active.ActualWidth;
        var offset = PaneScroller.HorizontalOffset;
        var viewport = PaneScroller.ViewportWidth;

        if (left >= offset && right <= offset + viewport) return;

        var index = _panes.IndexOf(_active);
        var maxStart = Math.Max(0, _panes.Count - 2);
        ScrollToPage(Math.Clamp(index, 0, maxStart));
    }

    private void PaneScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        ScrollCarousel(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private TerminalPane? FindPaneAtCursor()
    {
        if (_panes.Count == 0 || !GetCursorPos(out var cursor)) return null;

        var local = PaneHost.PointFromScreen(new Point(cursor.X, cursor.Y));
        foreach (var pane in _panes)
        {
            if (pane.ActualWidth <= 0 || pane.ActualHeight <= 0) continue;

            var topLeft = pane.TranslatePoint(new Point(0, 0), PaneHost);
            var bounds = new Rect(topLeft, new Size(pane.ActualWidth, pane.ActualHeight));
            if (bounds.Contains(local)) return pane;
        }

        return null;
    }

    private void UpdateHoverFocus()
    {
        if (!IsActive || _shuttingDown || _panes.Count == 0 || WindowState == WindowState.Minimized)
        {
            return;
        }

        // Durante a rolagem, o painel sob o cursor muda a cada frame; não
        // deixe a cor/foco oscilar. Volta a seguir o mouse após a animação.
        if (_scrollAnimating || DateTime.UtcNow < _hoverSuppressedUntil) return;

        var hovered = FindPaneAtCursor();
        if (hovered != null)
        {
            if (_active != hovered)
            {
                SetActivePane(hovered, focus: true);
            }
            else if (!hovered.HasNativeFocus)
            {
                // Se o foco ficou na barra/ScrollViewer, devolve as setas ao
                // terminal nativo da sessão.
                hovered.FocusTerminal();
            }
        }
    }

    private void SetActivePane(TerminalPane? pane, bool focus)
    {
        if (pane != null && !_panes.Contains(pane)) pane = null;
        _active = pane ?? _panes.LastOrDefault();

        foreach (var item in _panes)
        {
            item.SetActive(item == _active);
        }

        UpdateTitle();
        UpdateActivePaneChrome();
        if (focus) _active?.FocusTerminal();
    }

    private void UpdateTitle()
    {
        Title = _active is { } pane ? $"StorageTerm — {pane.ConnectionLabel}" : "StorageTerm";
    }

    private void PollPanes()
    {
        if (_shuttingDown) return;

        foreach (var pane in _panes.ToArray())
        {
            pane.PollProcess();
        }

        var now = DateTime.UtcNow;
        foreach (var pane in _panes)
        {
            if (pane.Status == PaneStatus.Connected &&
                pane.ConnectedAt is { } connectedAt &&
                now - connectedAt > TimeSpan.FromSeconds(15))
            {
                pane.ReconnectAttempts = 0;
            }
        }

        UpdateChrome();
    }

    private async Task HandlePaneExitedAsync(TerminalPane pane)
    {
        if (_shuttingDown || pane.IsClosed) return;

        pane.WriteSystemMessage("conexão encerrada.", error: true);

        if (!_settings.AutoReconnect)
        {
            pane.SetStatus(PaneStatus.Disconnected, "desconectado");
            ShowToast($"{pane.Alias}: sessão encerrada. Use ↻ para reconectar.", error: true);
            UpdateChrome();
            return;
        }

        if (pane.ReconnectAttempts >= 5)
        {
            pane.SetStatus(PaneStatus.Disconnected, "reconexão pausada");
            ShowToast($"{pane.Alias}: não foi possível reconectar. Use ↻ para tentar novamente.", error: true);
            UpdateChrome();
            return;
        }

        pane.ReconnectAttempts++;
        var delay = Math.Min(30, 1 << Math.Min(5, pane.ReconnectAttempts - 1));

        CancelReconnect(pane);
        var cts = new CancellationTokenSource();
        _reconnectCts[pane.Id] = cts;

        ShowToast($"{pane.Alias} caiu — reconectando em {delay}s (tentativa {pane.ReconnectAttempts}/5).", error: true);
        Log.Warn($"{pane.Alias} caiu. Reconexão {pane.ReconnectAttempts}/5 em {delay}s.");

        await ScheduleReconnectAsync(pane, delay, cts.Token);
    }

    private async Task ScheduleReconnectAsync(TerminalPane pane, int delaySeconds, CancellationToken token)
    {
        try
        {
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                if (token.IsCancellationRequested || _shuttingDown || pane.IsClosed) return;
                pane.SetReconnectCountdown(remaining);
                await Task.Delay(1000, token);
            }

            if (token.IsCancellationRequested || _shuttingDown || pane.IsClosed) return;

            _reconnectCts.Remove(pane.Id);
            await ReconnectPaneAsync(pane, manual: false, token: token);
        }
        catch (OperationCanceledException)
        {
            // canceled by user action / close
        }
    }

    private async Task ReconnectPaneAsync(TerminalPane pane, bool manual, CancellationToken token = default)
    {
        if (_shuttingDown || pane.IsClosed) return;

        if (manual)
        {
            CancelReconnect(pane);
            pane.ReconnectAttempts = 0;
            pane.SetStatus(PaneStatus.Connecting, "reconectando…");
        }

        try
        {
            ConnectionTarget target;
            if (pane.IsDemo)
            {
                target = _resolver.Demo();
            }
            else
            {
                if (pane.ModeOverride is null && _settings.Mode == ConnectionMode.Auto)
                {
                    await _resolver.ProbeAsync(_settings, token);
                }

                target = _resolver.Resolve(_settings, pane.ModeOverride);
            }

            var tmuxSession = !pane.IsDemo && _settings.PersistentSession && pane.SessionSlot > 0
                ? $"storage-{pane.SessionSlot}"
                : null;

            var command = pane.IsDemo
                ? SshCommandBuilder.BuildDemo()
                : SshCommandBuilder.Build(target.Alias, tmuxSession);

            SshProfile? profile = null;
            if (!pane.IsDemo)
            {
                profile = SshConfigReader.Resolve(target.Alias);
                if (!profile.Found)
                {
                    ShowToast($"Alias '{target.Alias}' não encontrado em ~/.ssh/config.", error: true);
                }
            }

            Log.Info($"Terminal {pane.Index} reconectando via {target.Alias} ({ModeLabel(target)}).");
            pane.RestartWith(command, target.Alias, target.Mode, pane.ModeOverride, pane.IsDemo, profile);
            pane.WriteSystemMessage($"reconectando via {target.Alias}…");
            UpdateChrome();
        }
        catch (OperationCanceledException)
        {
            // canceled
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao reconectar o terminal.", ex);
            pane.SetStatus(PaneStatus.Disconnected, "falha ao reconectar");
            ShowToast($"Falha ao reconectar {pane.Alias}: {ex.Message}", error: true);
        }
    }

    private async Task ChangePaneModeAsync(TerminalPane pane, ConnectionMode? mode)
    {
        if (pane.IsClosed) return;
        if (pane.IsDemo)
        {
            ShowToast("Este terminal está em modo demonstração.");
            return;
        }

        pane.ModeOverride = mode;
        ShowToast($"{pane.Alias}: modo {ModeLabel(mode)} — reconectando…");
        await ReconnectPaneAsync(pane, manual: true);
    }

    private async Task ClosePaneAsync(TerminalPane pane)
    {
        if (pane.IsClosed) return;

        if (_settings.ConfirmClose && pane.Status is PaneStatus.Connected or PaneStatus.Connecting)
        {
            var dialog = new ConfirmDialog(
                "Fechar terminal",
                $"Encerrar a sessão {pane.ConnectionLabel}?",
                "Encerrar",
                "Não perguntar novamente")
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true) return;

            if (dialog.DoNotAskAgain)
            {
                _settings.ConfirmClose = false;
                _settings.Save();
                ShowToast("Confirmação de fechamento desativada nas configurações.");
            }
        }

        var index = pane.Index;
        CancelReconnect(pane);
        _panes.Remove(pane);
        PaneHost.Children.Remove(pane);
        pane.Dispose();

        if (_active == pane) _active = null;

        LayoutPanes();
        SetActivePane(_active ?? _panes.LastOrDefault(), focus: true);
        UpdateChrome();

        ShowToast($"Terminal {index} encerrado.");
        Log.Info($"Terminal {index} encerrado pelo usuário.");

        await Task.CompletedTask;
    }

    private void CancelReconnect(TerminalPane pane)
    {
        if (_reconnectCts.Remove(pane.Id, out var cts))
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
    }

    private async Task RefreshConnectivityAsync(bool silent)
    {
        if (_demo || _probeInFlight || _shuttingDown) return;

        _probeInFlight = true;
        try
        {
            var previous = _resolver.LastProbe?.LanReachable;
            var probe = await _resolver.ProbeAsync(_settings);

            if (previous != probe.LanReachable)
            {
                Log.Info($"LAN {(probe.LanReachable ? "disponível" : "indisponível")} " +
                         $"({_settings.LanProbeHost}:{_settings.LanProbePort}, {probe.LatencyMs} ms).");
            }

            UpdateChrome();
            UpdateFooters();
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha na sonda de conectividade: {ex.Message}");
            if (!silent) ShowToast("Não foi possível testar a conexão.", error: true);
        }
        finally
        {
            _probeInFlight = false;
        }
    }

    private void UpdateChrome()
    {
        var count = _panes.Count;
        var connected = _panes.Count(p => p.Status == PaneStatus.Connected);
        var reconnecting = _panes.Count(p => p.Status is PaneStatus.Reconnecting or PaneStatus.Connecting);
        var disconnected = _panes.Count(p => p.Status is PaneStatus.Disconnected or PaneStatus.Stopped);

        CountText.Text = count.ToString();
        NewButton.IsEnabled = true;

        if (count == 0 && _startupComplete)
        {
            EmptyTitle.Text = "Nenhum terminal aberto";
            EmptySubtitle.Text = "Use Ctrl+T, o botão + ou o menu da bandeja para abrir uma sessão.";
        }

        if (!_demo)
        {
            UpdateModeIndicator();
        }
        else
        {
            ModeButtonText.Text = "DEMO";
            ModeDot.Fill = (Brush)FindResource("PurpleBrush");
            ModeButton.Background = DemoPillBg;
            ModeButton.BorderBrush = DemoPillBorder;
        }

        FooterLeft.Text = BuildFooterLeft(count, connected, reconnecting, disconnected);
        FooterDot.Fill = count == 0
            ? (Brush)FindResource("TextMutedBrush")
            : reconnecting > 0
                ? (Brush)FindResource("AmberBrush")
                : connected > 0
                    ? (Brush)FindResource("GreenBrush")
                    : (Brush)FindResource("RedBrush");

        UpdateFooters();
        UpdateSleepGuard();
        UpdateTitle();
        UpdateActivePaneChrome();

        _tray?.UpdateState(
            count,
            connected,
            _settings.KeepAwake,
            _settings.StartWithWindows,
            count == 0
                ? "StorageTerm · nenhum terminal aberto"
                : $"StorageTerm · {count} terminal(is) · {connected} conectado(s)");
    }

    private void UpdateActivePaneChrome()
    {
        var pane = _active;
        var visible = pane != null ? Visibility.Visible : Visibility.Collapsed;

        ActivePaneGroup.Visibility = visible;
        ActivePaneSeparator.Visibility = visible;
        ActivePaneActions.Visibility = visible;
        ActivePaneActionsSeparator.Visibility = visible;

        if (pane == null) return;

        ActiveIndexText.Text = $"#{pane.Index}";
        ActiveIndexText.Foreground = pane.AccentBrush;
        ActiveHostText.Text = pane.IsDemo ? "shell local" : pane.Alias;
        ActiveHostText.ToolTip = pane.ConnectionLabel;
        ActiveBranchText.Text = $"{(pane.BranchIsGit ? "branch" : "pasta")}: {pane.BranchTitle}";
        ActiveBranchText.Foreground = pane.AccentBrush;
        ActiveStatusText.Text = pane.StatusLabel;

        if (pane.IsDemo)
        {
            ActiveModeText.Text = "DEMO";
            ActiveModeText.Foreground = (Brush)FindResource("PurpleBrush");
            ActiveModeDot.Fill = (Brush)FindResource("PurpleBrush");
            ActiveModeBadge.Background = DemoPillBg;
            ActiveModeBadge.BorderBrush = DemoPillBorder;
            ActiveModeBadge.ToolTip = "Modo demonstração (shell local)";
        }
        else if (pane.ResolvedMode == ConnectionMode.Lan)
        {
            ActiveModeText.Text = "LAN";
            ActiveModeText.Foreground = (Brush)FindResource("GreenBrush");
            ActiveModeDot.Fill = (Brush)FindResource("GreenBrush");
            ActiveModeBadge.Background = LanPillBg;
            ActiveModeBadge.BorderBrush = LanPillBorder;
            ActiveModeBadge.ToolTip = $"Conexão LAN — {pane.ConnectionLabel}";
        }
        else
        {
            ActiveModeText.Text = "EXT";
            ActiveModeText.Foreground = (Brush)FindResource("AmberBrush");
            ActiveModeDot.Fill = (Brush)FindResource("AmberBrush");
            ActiveModeBadge.Background = ExtPillBg;
            ActiveModeBadge.BorderBrush = ExtPillBorder;
            ActiveModeBadge.ToolTip = $"Conexão externa — {pane.ConnectionLabel}";
        }

        ActiveStatusDot.Fill = pane.Status switch
        {
            PaneStatus.Connected => (Brush)FindResource("GreenBrush"),
            PaneStatus.Reconnecting or PaneStatus.Connecting => (Brush)FindResource("AmberBrush"),
            PaneStatus.Disconnected => (Brush)FindResource("RedBrush"),
            _ => (Brush)FindResource("TextMutedBrush")
        };

        ActiveReconnectButton.IsEnabled = !pane.IsDemo;
        ActiveReconnectButton.ToolTip = pane.IsDemo
            ? "Modo demonstração (shell local)"
            : $"Reconectar {pane.Alias}";
        ActivePaneMenuButton.ToolTip = $"Opções de {pane.Alias}";
        ActiveCloseButton.ToolTip = $"Fechar {pane.Alias} (Ctrl+Q)";
    }

    private void ActiveReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { IsDemo: false } pane)
        {
            _ = ReconnectPaneAsync(pane, manual: true);
        }
    }

    private void ActivePaneMenu_Click(object sender, RoutedEventArgs e)
    {
        _active?.OpenPaneMenu(ActivePaneMenuButton);
    }

    private void ActiveModeBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _active?.OpenPaneMenu(ActiveModeBadge);
    }

    private void ActiveClose_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { } pane)
        {
            _ = ClosePaneAsync(pane);
        }
    }

    private void UpdateModeIndicator()
    {
        var probe = _resolver.LastProbe;

        if (_settings.Mode == ConnectionMode.Lan)
        {
            ModeButtonText.Text = "LAN forçada";
            ModeDot.Fill = (Brush)FindResource("GreenBrush");
            ModeButton.Background = LanPillBg;
            ModeButton.BorderBrush = LanPillBorder;
        }
        else if (_settings.Mode == ConnectionMode.External)
        {
            ModeButtonText.Text = "EXTERNO";
            ModeDot.Fill = (Brush)FindResource("AmberBrush");
            ModeButton.Background = ExtPillBg;
            ModeButton.BorderBrush = ExtPillBorder;
        }
        else if (probe == null)
        {
            ModeButtonText.Text = "AUTO · verificando";
            ModeDot.Fill = (Brush)FindResource("TextMutedBrush");
            ModeButton.Background = NeutralPillBg;
            ModeButton.BorderBrush = NeutralPillBorder;
        }
        else if (probe.LanReachable)
        {
            ModeButtonText.Text = "AUTO · LAN";
            ModeDot.Fill = (Brush)FindResource("GreenBrush");
            ModeButton.Background = LanPillBg;
            ModeButton.BorderBrush = LanPillBorder;
        }
        else
        {
            ModeButtonText.Text = "AUTO · EXTERNO";
            ModeDot.Fill = (Brush)FindResource("AmberBrush");
            ModeButton.Background = ExtPillBg;
            ModeButton.BorderBrush = ExtPillBorder;
        }
    }

    private void UpdateFooters()
    {
        var probe = _resolver.LastProbe;
        string full;
        string shortText;
        Brush dot;

        if (_demo)
        {
            full = "DEMO · shell local (sem SSH)";
            shortText = "DEMO";
            dot = (Brush)FindResource("PurpleBrush");
        }
        else if (_settings.Mode == ConnectionMode.Auto)
        {
            if (probe == null)
            {
                full = $"AUTO · testando {_settings.LanProbeHost}:{_settings.LanProbePort}…";
                shortText = "verificando…";
                dot = (Brush)FindResource("TextMutedBrush");
            }
            else if (probe.LanReachable)
            {
                full = $"AUTO · LAN {_settings.LanProbeHost}:{_settings.LanProbePort} · {probe.LatencyMs} ms";
                shortText = $"LAN · {probe.LatencyMs} ms";
                dot = (Brush)FindResource("GreenBrush");
            }
            else
            {
                full = $"AUTO · EXTERNO · {_settings.ExternalAlias} (Tailscale)";
                shortText = "EXTERNO · Tailscale";
                dot = (Brush)FindResource("AmberBrush");
            }
        }
        else if (_settings.Mode == ConnectionMode.Lan)
        {
            full = probe is { LanReachable: false }
                ? $"LAN forçada · {_settings.LanProbeHost}:{_settings.LanProbePort} · sem resposta"
                : $"LAN forçada · {_settings.LanProbeHost}:{_settings.LanProbePort}";
            shortText = "LAN forçada";
            dot = (Brush)FindResource("GreenBrush");
        }
        else
        {
            full = $"EXTERNO forçado · {_settings.ExternalAlias} (Tailscale)";
            shortText = "Externo forçado";
            dot = (Brush)FindResource("AmberBrush");
        }

        FooterConnection.Text = shortText;
        FooterConnection.ToolTip = full;
        FooterConnectionDot.Fill = dot;

        var awake = _settings.KeepAwake && _panes.Count > 0;
        FooterAwake.Text = awake ? "PC acordado" : "suspensão normal";
        FooterAwake.Foreground = awake
            ? (Brush)FindResource("GreenBrush")
            : (Brush)FindResource("TextMutedBrush");
        FooterAwakeIcon.Stroke = awake
            ? (Brush)FindResource("GreenBrush")
            : (Brush)FindResource("TextMutedBrush");
    }

    private void UpdateSleepGuard()
    {
        var shouldGuard = _settings.KeepAwake && _panes.Count > 0 && !_shuttingDown;
        if (_sleepGuard.IsEnabled != shouldGuard)
        {
            _sleepGuard.SetEnabled(shouldGuard);
        }
    }

    private static string BuildFooterLeft(int count, int connected, int reconnecting, int disconnected)
    {
        if (count == 0) return "0 terminais · máximo 6";

        var terminals = count == 1 ? "1 terminal" : $"{count} terminais";
        var parts = new List<string>(3) { terminals };

        parts.Add(connected == 1 ? "1 conectado" : $"{connected} conectados");
        if (reconnecting > 0) parts.Add(reconnecting == 1 ? "1 conectando" : $"{reconnecting} conectando");
        if (disconnected > 0) parts.Add(disconnected == 1 ? "1 desconectado" : $"{disconnected} desconectados");

        return string.Join(" · ", parts);
    }

    private void ShowToast(string message, bool error = false)
    {
        if (_shuttingDown) return;

        ToastText.Text = message;
        ToastText.Foreground = (Brush)FindResource(error ? "RedBrush" : "TextBrush");
        ToastDot.Fill = (Brush)FindResource(error ? "RedBrush" : "AccentBrush");
        ToastBadge.Background = error ? Frozen("#211616") : Frozen("#161616");
        ToastBadge.BorderBrush = error ? Frozen("#3E2A2A") : Frozen("#2A2A2A");

        ToastBadge.BeginAnimation(UIElement.OpacityProperty, null);
        ToastBadge.Opacity = 1;

        var transform = ToastBadge.RenderTransform as TranslateTransform ?? new TranslateTransform();
        ToastBadge.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-4, 0, TimeSpan.FromMilliseconds(160)));

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void FadeToast()
    {
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
        {
            FillBehavior = FillBehavior.Stop
        };

        ToastBadge.BeginAnimation(UIElement.OpacityProperty, animation);
        ToastBadge.Opacity = 0;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e) => _ = AddPaneAsync(null);

    private void EmptyAction_Click(object sender, RoutedEventArgs e) => _ = AddPaneAsync(null);

    private async Task ReconnectAllPanesAsync()
    {
        foreach (var pane in _panes.ToArray())
        {
            await ReconnectPaneAsync(pane, manual: true);
        }

        ShowToast("Sessões reconectadas.");
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = MoreButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        if (_pendingUpdate is { } pending)
        {
            var update = new MenuItem { Header = $"Atualizar para v{pending.Version}" };
            update.Click += (_, _) => ApplyUpdateNow();
            menu.Items.Add(update);
            menu.Items.Add(new Separator());
        }

        var reconnect = new MenuItem
        {
            Header = "Reconectar todos",
            IsEnabled = _panes.Count > 0
        };
        reconnect.Click += (_, _) => _ = ReconnectAllPanesAsync();

        var settings = new MenuItem { Header = "Configurações" };
        settings.Click += (_, _) => OpenSettings();

        var checkUpdates = new MenuItem
        {
            Header = "Verificar atualizações",
            IsEnabled = UpdateService.IsInstalledBuild
        };
        checkUpdates.Click += (_, _) => _ = CheckForUpdatesAsync(manual: true);

        var quit = new MenuItem { Header = "Sair" };
        quit.Click += (_, _) => ExitApp();

        menu.Items.Add(reconnect);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(checkUpdates);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = $"StorageTerm v{UpdateService.VersionLabel}",
            IsEnabled = false
        });
        menu.IsOpen = true;
    }

    private async Task InitialUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
        }
        catch
        {
            return;
        }

        await CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_shuttingDown) return;

        if (!UpdateService.IsInstalledBuild)
        {
            if (manual) ShowToast("Atualizações automáticas estão disponíveis apenas na versão instalada.", error: true);
            return;
        }

        if (_updateCheckInFlight)
        {
            if (manual) ShowToast("Já existe uma verificação de atualização em andamento.");
            return;
        }

        if (_pendingUpdate is { } ready)
        {
            if (manual) ShowToast($"A atualização v{ready.Version} já está pronta no menu ⋯.");
            return;
        }

        _updateCheckInFlight = true;
        try
        {
            if (manual) ShowToast("Verificando atualizações…");

            var info = await _updateService.CheckAsync();
            if (info == null)
            {
                if (manual) ShowToast($"Você já está na versão mais recente (v{UpdateService.VersionLabel}).");
                return;
            }

            ShowToast($"Nova versão v{info.Version} encontrada — baixando…");
            var zip = await _updateService.DownloadAsync(info);
            if (zip == null)
            {
                ShowToast("Não foi possível baixar a atualização. Veja o log.", error: true);
                return;
            }

            _pendingUpdate = info;
            _pendingUpdateZip = zip;
            MoreButton.Foreground = (Brush)FindResource("AccentBrush");
            MoreButton.ToolTip = $"Mais ações · atualização v{info.Version} pronta";
            ShowToast($"Atualização v{info.Version} pronta — ⋯ → Atualizar agora ou ao sair.");
            Log.Info($"Updater: atualização {info.Tag} pronta para aplicar.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Updater: falha ao verificar/baixar atualização: {ex.Message}");
            if (manual) ShowToast("Falha ao verificar atualizações. Veja o log.", error: true);
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }

    private void ApplyUpdateNow()
    {
        if (_pendingUpdate is not { } update || string.IsNullOrWhiteSpace(_pendingUpdateZip)) return;

        var active = _panes.Count;
        var message = active > 0
            ? $"Atualizar para v{update.Version} agora?\nAs {active} sessões abertas serão encerradas e o app reiniciará."
            : $"Atualizar para v{update.Version} agora?\nO app reiniciará.";

        var dialog = new ConfirmDialog("Atualizar StorageTerm", message, "Atualizar")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _applyingUpdate = true;
            _updateService.Apply(_pendingUpdateZip, restart: true);
            ExitApp();
        }
        catch (Exception ex)
        {
            _applyingUpdate = false;
            Log.Error("Updater: falha ao aplicar atualização.", ex);
            ShowToast("Não foi possível aplicar a atualização.", error: true);
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ModeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        AddModeItem(menu, "Automático (detectar LAN)", ConnectionMode.Auto);
        AddModeItem(menu, $"Sempre LAN ({_settings.LanAlias})", ConnectionMode.Lan);
        AddModeItem(menu, $"Sempre externo ({_settings.ExternalAlias})", ConnectionMode.External);
        menu.Items.Add(new Separator());

        var test = new MenuItem { Header = "Testar conexão agora" };
        test.Click += async (_, _) =>
        {
            await RefreshConnectivityAsync(silent: false);
            var probe = _resolver.LastProbe;
            ShowToast(probe is { LanReachable: true }
                ? $"LAN respondeu em {probe.LatencyMs} ms."
                : $"LAN sem resposta — usando {_settings.ExternalAlias} no modo automático.",
                error: probe is { LanReachable: false });
        };
        menu.Items.Add(test);

        menu.IsOpen = true;
    }

    private void AddModeItem(ContextMenu menu, string header, ConnectionMode mode)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = _settings.Mode == mode
        };

        item.Click += (_, _) =>
        {
            _settings.Mode = mode;
            _settings.Save();
            UpdateChrome();
            _ = RefreshConnectivityAsync(silent: true);
            ShowToast($"Modo de conexão: {ModeLabel(mode)}. Vale para as próximas sessões.");
        };

        menu.Items.Add(item);
    }

    private void OpenSettings()
    {
        SaveWindowPlacement();

        var dialog = new SettingsWindow(_settings.Clone(), async probeSettings =>
        {
            var temp = new ConnectionResolver();
            return await temp.ProbeAsync(probeSettings);
        })
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Result == null) return;

        var updated = dialog.Result;
        _settings = updated;
        _settings.Save();

        if (_settings.StartWithWindows && !AutoStart.Set(true))
        {
            _settings.StartWithWindows = false;
            _settings.Save();
            ShowToast("Não foi possível ativar a inicialização com o Windows.", error: true);
        }
        else if (!_settings.StartWithWindows)
        {
            AutoStart.Set(false);
        }

        foreach (var pane in _panes)
        {
            pane.ApplyAppearance(_settings.FontFamily, _settings.FontSize);
        }

        UpdateChrome();
        _ = RefreshConnectivityAsync(silent: true);
        ShowToast("Configurações salvas.");
        Log.Info($"Configurações atualizadas: {_settings}");
    }

    private void SyncAutoStartSetting()
    {
        var actual = AutoStart.IsEnabled;
        if (actual != _settings.StartWithWindows)
        {
            _settings.StartWithWindows = actual;
            _settings.Save();
        }
    }

    private void InitializeTray()
    {
        try
        {
            _tray = new TrayIconService(
                showWindow: () => Dispatcher.BeginInvoke(ShowFromExternalRequest),
                newSession: () => Dispatcher.BeginInvoke(() => _ = AddPaneAsync(null)),
                newSessionWithMode: mode => Dispatcher.BeginInvoke(() => _ = AddPaneAsync(mode)),
                openSettings: () => Dispatcher.BeginInvoke(OpenSettings),
                exit: () => Dispatcher.BeginInvoke(ExitApp),
                setKeepAwake: enabled =>
                {
                    _settings.KeepAwake = enabled;
                    _settings.Save();
                    UpdateChrome();
                    ShowToast(enabled ? "PC será mantido acordado durante as sessões." : "Suspensão normal restaurada.");
                },
                setStartWithWindows: enabled =>
                {
                    if (enabled && !AutoStart.Set(true))
                    {
                        ShowToast("Não foi possível ativar a inicialização com o Windows.", error: true);
                        return;
                    }

                    if (!enabled) AutoStart.Set(false);

                    _settings.StartWithWindows = enabled;
                    _settings.Save();
                    UpdateChrome();
                    ShowToast(enabled ? "StorageTerm iniciará com o Windows." : "Inicialização com o Windows desativada.");
                });
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao criar o ícone da bandeja.", ex);
            _tray = null;
        }
    }

    public void ShowFromExternalRequest()
    {
        if (_shuttingDown) return;

        if (!IsVisible) Show();
        Opacity = 1;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shuttingDown)
        {
            SaveWindowPlacement();
            return;
        }

        if (_settings.HideToTrayOnClose)
        {
            e.Cancel = true;
            SaveWindowPlacement();
            Hide();

            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray?.ShowBalloon(
                    "O StorageTerm continua na bandeja e mantém as sessões SSH ativas. " +
                    "Use o menu do ícone para sair de verdade.");
            }
        }
        else
        {
            e.Cancel = true;
            ExitApp();
        }
    }

    private void CloseActivePane()
    {
        if (_active != null)
        {
            _ = ClosePaneAsync(_active);
        }
    }

    private void ExitApp()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        Log.Info("Encerrando o StorageTerm.");
        SaveWindowPlacement();
        _settings.Save();

        _pollTimer.Stop();
        _connectivityTimer.Stop();
        _toastTimer.Stop();
        _updateTimer.Stop();
        _branchTimer.Stop();
        _hoverTimer.Stop();
        _scrollAnimTimer.Stop();
        _snapTimer.Stop();

        UnregisterHotkeys();

        try { _tray?.Dispose(); } catch { }
        _tray = null;

        _sleepGuard.Dispose();

        foreach (var cts in _reconnectCts.Values)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
        _reconnectCts.Clear();

        foreach (var pane in _panes.ToArray())
        {
            try { pane.Dispose(); } catch { }
        }
        _panes.Clear();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;

        if (!_applyingUpdate && _pendingUpdateZip != null && UpdateService.IsInstalledBuild)
        {
            try
            {
                _updateService.Apply(_pendingUpdateZip, restart: false);
            }
            catch (Exception ex)
            {
                Log.Warn($"Updater: falha ao agendar a atualização: {ex.Message}");
            }
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (ctrl && !shift && !alt && e.Key == Key.T)
        {
            e.Handled = true;
            _ = AddPaneAsync(null);
            return;
        }

        if (ctrl && shift && !alt && e.Key == Key.W)
        {
            e.Handled = true;
            if (_active != null) _ = ClosePaneAsync(_active);
            return;
        }

        if (ctrl && !alt && e.Key == Key.Tab)
        {
            e.Handled = true;
            CyclePane(shift ? -1 : 1);
            return;
        }

        if (ctrl && shift && !alt && e.Key == Key.C)
        {
            e.Handled = true;
            _active?.CopySelection();
            return;
        }

        if (ctrl && shift && !alt && e.Key == Key.V)
        {
            e.Handled = true;
            _active?.PasteClipboard();
            return;
        }

        if (ctrl && !shift && !alt && e.Key == Key.V)
        {
            e.Handled = true;
            _active?.PasteClipboard();
            return;
        }

        if (ctrl && shift && !alt && e.Key == Key.Q)
        {
            e.Handled = true;
            CloseActivePane();
            return;
        }

        if (!ctrl && !shift && !alt && e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleMaximize();
            return;
        }

        if (ctrl && shift && !alt && e.Key is >= Key.D1 and <= Key.D6)
        {
            e.Handled = true;
            FocusPane(e.Key - Key.D1);
        }
    }

    private void CyclePane(int delta)
    {
        if (_panes.Count == 0) return;

        var current = _active == null ? -1 : _panes.IndexOf(_active);
        var next = (current + delta + _panes.Count) % _panes.Count;
        SetActivePane(_panes[next], focus: true);
    }

    private void ScrollCarousel(int direction)
    {
        if (_panes.Count <= 2 || _scrollAnimating) return;

        var step = _carouselStep > 0
            ? _carouselStep
            : Math.Max(240, PaneScroller.ViewportWidth / 2);

        var index = (int)Math.Round(PaneScroller.HorizontalOffset / step);
        var maxStart = Math.Max(0, _panes.Count - 2);
        ScrollToPage(Math.Clamp(index + direction, 0, maxStart));
    }

    private void ScrollToPage(int index)
    {
        if (_panes.Count == 0) return;

        var maxStart = Math.Max(0, _panes.Count - 2);
        index = Math.Clamp(index, 0, maxStart);

        var left = _panes[index].TranslatePoint(new Point(0, 0), PaneHost).X - 3;
        _hoverSuppressedUntil = DateTime.UtcNow.AddMilliseconds(380);
        StartScroll(left);
    }

    private void StartScroll(double target)
    {
        target = Math.Clamp(target, 0, Math.Max(0, PaneScroller.ScrollableWidth));
        var current = PaneScroller.HorizontalOffset;

        if (Math.Abs(target - current) < 0.5)
        {
            PaneScroller.ScrollToHorizontalOffset(target);
            return;
        }

        _scrollStartOffset = current;
        _scrollTarget = target;
        _scrollStartTime = DateTime.UtcNow;
        _scrollAnimating = true;
        _scrollAnimTimer.Start();
    }

    private void AnimateScroll()
    {
        if (!_scrollAnimating) return;

        var elapsed = (DateTime.UtcNow - _scrollStartTime).TotalMilliseconds;
        var t = Math.Clamp(elapsed / ScrollDurationMs, 0, 1);
        var eased = t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        var value = _scrollStartOffset + (_scrollTarget - _scrollStartOffset) * eased;
        PaneScroller.ScrollToHorizontalOffset(value);

        if (t >= 1)
        {
            _scrollAnimating = false;
            _scrollAnimTimer.Stop();
        }
    }

    private void SnapToNearestPage()
    {
        if (_panes.Count <= 2)
        {
            if (PaneScroller.HorizontalOffset > 0.5) StartScroll(0);
            return;
        }

        var step = _carouselStep > 0
            ? _carouselStep
            : Math.Max(240, PaneScroller.ViewportWidth / 2);

        var index = (int)Math.Round(PaneScroller.HorizontalOffset / step);
        var maxStart = Math.Max(0, _panes.Count - 2);
        ScrollToPage(Math.Clamp(index, 0, maxStart));
    }

    private void PaneScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollAnimating || e.HorizontalChange == 0) return;

        _hoverSuppressedUntil = DateTime.UtcNow.AddMilliseconds(250);
        _snapTimer.Stop();
        _snapTimer.Start();
    }

    private void FocusPane(int index)
    {
        if (index >= 0 && index < _panes.Count)
        {
            SetActivePane(_panes[index], focus: true);
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WmLeftButtonDown || _panes.Count == 0 || !IsVisible) return;
        if (!GetCursorPos(out var point)) return;

        foreach (var pane in _panes)
        {
            try
            {
                var topLeft = pane.PointToScreen(new System.Windows.Point(0, 0));
                var bottomRight = pane.PointToScreen(new System.Windows.Point(pane.ActualWidth, pane.ActualHeight));

                if (point.X >= topLeft.X && point.X <= bottomRight.X &&
                    point.Y >= topLeft.Y && point.Y <= bottomRight.Y)
                {
                    SetActivePane(pane, focus: false);
                    break;
                }
            }
            catch
            {
                // The pane may be between layouts.
            }
        }
    }

    private void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero || _hotkeysRegistered || _shuttingDown) return;

        var failures = new List<string>();

        Register(HotkeyNew, ModControl, Key.T);
        Register(HotkeyClose, ModControl | ModShift, Key.W);
        Register(HotkeyNext, ModControl, Key.Tab);
        Register(HotkeyPrevious, ModControl | ModShift, Key.Tab);
        Register(HotkeyCopy, ModControl | ModShift, Key.C);
        Register(HotkeyPaste, ModControl | ModShift, Key.V);
        Register(HotkeyMaximize, 0, Key.F11);
        Register(HotkeyCloseQ, ModControl | ModShift, Key.Q);
        Register(HotkeyScrollPrev, ModAlt, Key.PageUp);
        Register(HotkeyScrollNext, ModAlt, Key.PageDown);

        for (var i = 0; i < AppSettings.MaxTerminals; i++)
        {
            Register(HotkeyPaneBase + i, ModControl | ModShift, (Key)((int)Key.D1 + i));
        }

        _hotkeysRegistered = true;

        // Hook só para AltGr: compõe o caractere e envia direto ao PTY.
        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardProc ??= KeyboardHookProc;
            _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                Log.Warn($"Falha ao instalar o hook de AltGr: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                Log.Info("Hook de AltGr instalado.");
            }
        }

        if (failures.Count > 0)
        {
            Log.Warn("Atalhos globais não registrados: " + string.Join(", ", failures));
        }

        void Register(int id, uint modifiers, Key key)
        {
            var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (!RegisterHotKey(_hwnd, id, modifiers | ModNoRepeat, vk))
            {
                failures.Add($"{modifiers}:{key}");
            }
        }
    }

    private void UnregisterHotkeys()
    {
        if (!_hotkeysRegistered || _hwnd == IntPtr.Zero) return;

        UnregisterHotKey(_hwnd, HotkeyNew);
        UnregisterHotKey(_hwnd, HotkeyClose);
        UnregisterHotKey(_hwnd, HotkeyNext);
        UnregisterHotKey(_hwnd, HotkeyPrevious);
        UnregisterHotKey(_hwnd, HotkeyCopy);
        UnregisterHotKey(_hwnd, HotkeyPaste);
        UnregisterHotKey(_hwnd, HotkeyMaximize);
        UnregisterHotKey(_hwnd, HotkeyCloseQ);
        UnregisterHotKey(_hwnd, HotkeyScrollPrev);
        UnregisterHotKey(_hwnd, HotkeyScrollNext);

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        for (var i = 0; i < AppSettings.MaxTerminals; i++)
        {
            UnregisterHotKey(_hwnd, HotkeyPaneBase + i);
        }

        _hotkeysRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            handled = true;
            HandleHotkey(wParam.ToInt32());
        }
        else if (msg == WmPowerBroadcast)
        {
            var powerEvent = wParam.ToInt32();
            if (powerEvent is PbtApmResumeAutomatic or PbtApmResumeSuspend)
            {
                handled = true;
                _ = OnSystemResumedAsync();
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown) &&
            _active != null &&
            GetForegroundWindow() == _hwnd)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
            var alt = (info.flags & 0x20) != 0 || // LLKHF_ALTDOWN
                      (GetAsyncKeyState(0xA5) & 0x8000) != 0 || // right Alt (AltGr)
                      (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
            var shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;

            // Ctrl+Q sem Alt fecha o terminal ativo. AltGr+Q (Ctrl+Alt+Q)
            // continua digitando "/" e nunca cai neste bloco.
            if (ctrl && !alt && !shift && info.vkCode == 0x51)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(CloseActivePane));
                return (IntPtr)1;
            }

            // Ctrl+V cola texto ou imagem (imagem sobe por scp e cola o caminho).
            // Ctrl+Shift+V continua no hotkey antigo.
            if (ctrl && !alt && !shift && info.vkCode == 0x56)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _active?.PasteClipboard()));
                return (IntPtr)1;
            }

            // AltGr = Ctrl+Alt. Compõe o caractere com o layout ABNT2 e envia
            // direto ao PTY, evitando sequências CSI do controle/tmux.
            if (ctrl && alt && !IsModifierKey(info.vkCode))
            {
                var layout = GetKeyboardLayout(0);
                var scan = info.scanCode != 0
                    ? info.scanCode
                    : MapVirtualKeyEx(info.vkCode, 0, layout);

                var state = new byte[256];
                state[VkControl] = 0x80;
                state[VkMenu] = 0x80;
                if (shift) state[VkShift] = 0x80;

                var buffer = new StringBuilder(8);
                var count = ToUnicodeEx(info.vkCode, scan, state, buffer, 8, 0, layout);
                if (count > 0)
                {
                    var text = buffer.ToString();
                    if (text.Length > 0 && text.Any(c => !char.IsControl(c)))
                    {
                        _ = Dispatcher.BeginInvoke(
                            DispatcherPriority.Input,
                            new Action(() => _active?.SendText(text)));
                        return (IntPtr)1;
                    }
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(uint vk) => vk is
        0x10 or 0x11 or 0x12 or // Shift, Ctrl, Alt
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or // left/right modifiers
        0x5B or 0x5C; // Win
    private async Task OnSystemResumedAsync()
    {
        if (_shuttingDown || _resumeInProgress) return;

        _resumeInProgress = true;
        try
        {
            Log.Info("Sistema retomado; aguardando a rede e reconectando as sessões.");
            await Task.Delay(TimeSpan.FromSeconds(3));

            if (_shuttingDown) return;
            ShowToast("Sistema retomado — revalidando sessões…");

            foreach (var pane in _panes.ToArray())
            {
                if (pane.IsClosed) continue;

                CancelReconnect(pane);
                await ReconnectPaneAsync(pane, manual: true);
            }
        }
        finally
        {
            _resumeInProgress = false;
        }
    }

    private void HandleHotkey(int id)
    {
        switch (id)
        {
            case HotkeyNew:
                _ = AddPaneAsync(null);
                break;
            case HotkeyClose:
                CloseActivePane();
                break;
            case HotkeyNext:
                CyclePane(1);
                break;
            case HotkeyPrevious:
                CyclePane(-1);
                break;
            case HotkeyCopy:
                _active?.CopySelection();
                break;
            case HotkeyPaste:
                _active?.PasteClipboard();
                break;
            case HotkeyMaximize:
                ToggleMaximize();
                break;
            case HotkeyCloseQ:
                CloseActivePane();
                break;
            case HotkeyScrollPrev:
                ScrollCarousel(-1);
                break;
            case HotkeyScrollNext:
                ScrollCarousel(1);
                break;
            default:
                if (id >= HotkeyPaneBase && id < HotkeyPaneBase + AppSettings.MaxTerminals)
                {
                    FocusPane(id - HotkeyPaneBase);
                }
                break;
        }
    }

    private void RestoreWindowPlacement()
    {
        try
        {
            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
            {
                var rect = new Rect(_settings.WindowLeft, _settings.WindowTop, _settings.WindowWidth, _settings.WindowHeight);
                var virtualDesktop = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);

                rect.Intersect(virtualDesktop);
                if (!rect.IsEmpty && rect.Width >= 400 && rect.Height >= 300)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = _settings.WindowLeft;
                    Top = _settings.WindowTop;
                    Width = _settings.WindowWidth;
                    Height = _settings.WindowHeight;
                }
            }

            var workArea = GetWorkAreaInDips();
            var maxWidth = Math.Max(MinWidth, workArea.Width - 24);
            var maxHeight = Math.Max(MinHeight, workArea.Height - 24);

            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);

            if (double.IsNaN(Left) || double.IsNaN(Top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
                Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
            }

            if (Left < workArea.Left) Left = workArea.Left;
            if (Top < workArea.Top) Top = workArea.Top;
            if (Left + Width > workArea.Right) Left = Math.Max(workArea.Left, workArea.Right - Width);
            if (Top + Height > workArea.Bottom) Top = Math.Max(workArea.Top, workArea.Bottom - Height);

            if (_settings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao restaurar a posição da janela: {ex.Message}");
        }
    }

    private Rect GetWorkAreaInDips() => SystemParameters.WorkArea;

    private void SaveWindowPlacement()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (bounds.Width >= 400 && bounds.Height >= 300)
            {
                _settings.WindowLeft = bounds.Left;
                _settings.WindowTop = bounds.Top;
                _settings.WindowWidth = bounds.Width;
                _settings.WindowHeight = bounds.Height;
            }

            _settings.WindowMaximized = WindowState == WindowState.Maximized;
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao salvar a posição da janela: {ex.Message}");
        }
    }

    private static string ModeLabel(ConnectionTarget? target) => target?.Kind switch
    {
        ConnectionKind.Lan => "LAN",
        ConnectionKind.External => "externo",
        ConnectionKind.Demo => "demo",
        _ => "automático"
    };

    private static string ModeLabel(ConnectionMode? mode) => mode switch
    {
        ConnectionMode.Lan => "LAN forçada",
        ConnectionMode.External => "externo forçado",
        _ => "automático"
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint vk, uint scan, byte[] state, StringBuilder buffer, int cch, uint flags, IntPtr layout);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point32 point);
}











