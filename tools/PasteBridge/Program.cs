using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StorageTerm.PasteBridge;

// Ponte temporária para a instância 1.0.4 que já está aberta:
// Ctrl+V cola texto e envia imagens da área de transferência para o servidor
// via scp, colando o caminho remoto. A partir da 1.0.5 o próprio app faz isso
// e esta ponte se desliga sozinha.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\StorageTermPasteBridge", out var createdNew);
        if (!createdNew) return;

        if (StorageTermVersion.IsUpdated)
        {
            LogMessage("App já está na 1.0.5+; ponte não é necessária.");
            RemoveAutostart();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BridgeContext());
    }

    internal static void LogMessage(string message)
    {
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StorageTerm", "paste-bridge.log");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    internal static void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("StorageTermPasteBridge", throwOnMissingValue: false);
        }
        catch
        {
            // ignore
        }
    }
}

internal static class StorageTermVersion
{
    private static readonly string ExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "StorageTerm", "StorageTerm.exe");

    public static bool IsUpdated
    {
        get
        {
            try
            {
                if (!File.Exists(ExePath)) return false;
                var text = FileVersionInfo.GetVersionInfo(ExePath).FileVersion;
                var raw = text?.Split('+').FirstOrDefault();
                if (!Version.TryParse(raw, out var version)) return false;
                return version >= new Version(1, 0, 6);
            }
            catch
            {
                return false;
            }
        }
    }
}

internal sealed class BridgeContext : ApplicationContext
{
    private static readonly bool DebugTarget =
        Environment.GetEnvironmentVariable("STORAGETERM_PASTE_DEBUG") == "1";

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkV = 0x56;
    private const int VkReturn = 0x0D;
    private const int VkPageUp = 0x21;
    private const int VkPageDown = 0x22;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;

    private static BridgeContext? _instance;
    private static LowLevelKeyboardProc? _hookProc;
    private static IntPtr _hook;
    private static LowLevelMouseProc? _mouseProc;
    private static IntPtr _mouseHook;

    private readonly Form _pump;
    private readonly System.Windows.Forms.Timer _pasteTimer;
    private readonly System.Windows.Forms.Timer _watchTimer;
    private readonly BridgeSettings _settings;
    private readonly string _logFile;
    private int _targetPid;
    private IntPtr _targetHwnd;
    private long _lastPidRefresh;
    private long _lastVersionCheck;
    private int _hwheelAccumulator;

    public BridgeContext()
    {
        _instance = this;
        _pump = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            WindowState = FormWindowState.Minimized,
            Opacity = 0
        };
        _ = _pump.Handle;
        _settings = BridgeSettings.Load();
        _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StorageTerm", "paste-bridge.log");

        _pasteTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _pasteTimer.Tick += (_, _) =>
        {
            _pasteTimer.Stop();
            HandlePaste();
        };

        _watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _watchTimer.Tick += (_, _) => Watch();
        _watchTimer.Start();

        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log($"Falha ao instalar hook: {Marshal.GetLastWin32Error()}");
            ExitThread();
            return;
        }

        _mouseProc = MouseHookCallback;
        _mouseHook = SetWindowsHookExMouse(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
        {
            Log($"Falha ao instalar hook de mouse: {Marshal.GetLastWin32Error()}");
        }

        RefreshTargetPid();
        Log($"Ponte de colagem iniciada. debug={DebugTarget} targetPid={_targetPid}");
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmMouseWheel || wParam == (IntPtr)WmMouseHWheel))
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            if (_instance != null)
            {
                if (wParam == (IntPtr)WmMouseWheel)
                {
                    var hwnd = _instance.FindTerminalForWheel(info.Point);
                    if (hwnd != IntPtr.Zero)
                    {
                        var delta = (short)(info.MouseData >> 16);
                        var wheelParam = (IntPtr)((delta & 0xFFFF) << 16);
                        var positionParam = (IntPtr)((info.Point.X & 0xFFFF) | ((info.Point.Y & 0xFFFF) << 16));
                        SendMessage(hwnd, WmMouseWheel, wheelParam, positionParam);
                        return (IntPtr)1;
                    }
                }
                else if (wParam == (IntPtr)WmMouseHWheel && _instance.IsTargetForeground())
                {
                    var delta = (short)(info.MouseData >> 16);
                    _instance.HandleHorizontalWheel(delta);
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (info.vkCode == VkV && IsCtrlDown() && !IsShiftDown() && !IsAltDown() && _instance?.IsTargetForeground() == true)
            {
                _instance?.Log("Ctrl+V interceptado.");
                _instance?._pasteTimer.Start();
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Watch()
    {
        RefreshTargetPid();

        var now = Environment.TickCount64;
        if (now - _lastVersionCheck < 5000) return;
        _lastVersionCheck = now;

        if (StorageTermVersion.IsUpdated)
        {
            Log("App atualizado para 1.0.5+; encerrando a ponte.");
            Program.RemoveAutostart();
            ExitThread();
        }
    }

    private void RefreshTargetPid()
    {
        var now = Environment.TickCount64;
        if (now - _lastPidRefresh < 1500 && _targetPid != 0) return;
        _lastPidRefresh = now;

        try
        {
            var app = Process.GetProcessesByName("StorageTerm")
                .FirstOrDefault(p => p.MainWindowHandle != 0);
            _targetPid = app?.Id ?? 0;
            _targetHwnd = app?.MainWindowHandle ?? IntPtr.Zero;
        }
        catch
        {
            _targetPid = 0;
            _targetHwnd = IntPtr.Zero;
        }
    }

    private IntPtr FindTerminalForWheel(NativePoint point)
    {
        if (_targetHwnd == IntPtr.Zero || !IsWindowVisible(_targetHwnd) || IsIconic(_targetHwnd))
        {
            return IntPtr.Zero;
        }

        // Mesmo sem a janela em foco: se o ponto está sobre um terminal do app
        // (e o app é a janela no topo naquele ponto), rola aquele painel.
        var hit = WindowFromPoint(point);
        if (hit != IntPtr.Zero &&
            GetClassNameText(hit).Equals("HwndTerminalClass", StringComparison.OrdinalIgnoreCase) &&
            GetAncestor(hit, 2 /* GA_ROOT */) == _targetHwnd)
        {
            return hit;
        }

        // Com o app em foco, usa o retângulo dos terminais (funciona mesmo se
        // houver um overlay de gesto do Windows em cima do ponto).
        return GetForegroundWindow() == _targetHwnd ? FindTerminalHwnd(point) : IntPtr.Zero;
    }

    private IntPtr FindTerminalHwnd(NativePoint point)
    {
        if (_targetHwnd == IntPtr.Zero) return IntPtr.Zero;

        var found = IntPtr.Zero;
        EnumChildWindows(_targetHwnd, (hwnd, _) =>
        {
            if (!GetClassNameText(hwnd).Equals("HwndTerminalClass", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect)) return true;
            if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private bool IsTargetForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        if (DebugTarget && GetWindowTitle(foreground).Contains("StorageTermPasteBridgeTest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == _targetPid) return true;

        RefreshTargetPid();
        return pid == _targetPid;
    }

    private void HandlePaste()
    {
        if (!IsTargetForeground()) return;

        try
        {
            var alias = GetAlias();
            if (alias == null)
            {
                Log("Título sem alias SSH; colando como texto.");
                PasteTextDirect();
                return;
            }

            var images = new List<string>();

            if (Clipboard.ContainsImage())
            {
                using var image = Clipboard.GetImage();
                if (image != null) images.Add(SaveImage(image));
            }

            if (images.Count == 0 && Clipboard.ContainsFileDropList())
            {
                foreach (var path in Clipboard.GetFileDropList().Cast<string>())
                {
                    if (IsImage(path)) images.Add(path);
                }
            }

            if (images.Count == 0)
            {
                PasteTextDirect();
                return;
            }

            var files = images.ToArray();
            Task.Run(() => UploadAndPasteAsync(alias, files));
        }
        catch (Exception ex)
        {
            Log($"Falha ao ler a área de transferência: {ex.Message}");
            SystemSounds.Hand.Play();
        }
    }

    private async Task UploadAndPasteAsync(string alias, IReadOnlyList<string> files)
    {
        try
        {
            var remoteDir = "storage-term-pastes";
            var home = await GetHomeAsync(alias);
            await RunAsync(SshPath, "-o", "BatchMode=yes", "-o", "ConnectTimeout=8",
                alias, $"mkdir -p {remoteDir}");

            var remotePaths = new List<string>();
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var name = $"paste-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}";
                await RunAsync(ScpPath, "-q", "-o", "BatchMode=yes", "-o", "ConnectTimeout=8",
                    file, $"{alias}:{remoteDir}/{name}");
                remotePaths.Add($"{home}/{remoteDir}/{name}");
            }

            var text = string.Join(" ", remotePaths);
            _pump.BeginInvoke(new Action(() =>
            {
                if (IsTargetForeground())
                {
                    SendUnicode(text);
                    Log($"Imagem colada: {text}");
                }
                else
                {
                    try { Clipboard.SetText(text); } catch { }
                    Log($"App perdeu o foco; caminho na área de transferência: {text}");
                }
            }));
        }
        catch (Exception ex)
        {
            Log($"Falha ao enviar imagem: {ex.Message}");
            SystemSounds.Hand.Play();
        }
    }

    private void HandleHorizontalWheel(int delta)
    {
        _hwheelAccumulator += delta;
        while (Math.Abs(_hwheelAccumulator) >= 120)
        {
            var direction = Math.Sign(_hwheelAccumulator);
            SendAltPage(direction > 0 ? VkPageDown : VkPageUp);
            _hwheelAccumulator -= direction * 120;
        }
    }

    private static void SendAltPage(int virtualKey)
    {
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event((byte)virtualKey, 0, 2, UIntPtr.Zero);
        keybd_event(VkMenu, 0, 2, UIntPtr.Zero);
    }

    private void PasteTextDirect()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch (Exception ex)
        {
            Log($"Falha ao ler texto da área de transferência: {ex.Message}");
            return;
        }

        if (string.IsNullOrEmpty(text)) return;

        Task.Run(async () =>
        {
            await Task.Delay(70);
            if (!IsTargetForeground()) return;
            SendText(text);
            Log($"Texto colado ({text.Length} caracteres).");
        });
    }

    private string? GetAlias()
    {
        var title = GetWindowTitle(GetForegroundWindow());
        if (title.Contains("shell local", StringComparison.OrdinalIgnoreCase)) return null;

        var match = Regex.Match(title, @"storage@([A-Za-z0-9._-]+)");
        if (match.Success) return match.Groups[1].Value;

        return ProbeLan() ? _settings.LanAlias : _settings.ExternalAlias;
    }

    private bool ProbeLan()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync(_settings.LanProbeHost, _settings.LanProbePort);
            return task.Wait(800) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string SaveImage(Image image)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StorageTerm", "pastes");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"paste-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        image.Save(file, ImageFormat.Png);
        return file;
    }

    private static bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetHomeAsync(string alias)
    {
        var home = (await RunCaptureAsync(SshPath, "-o", "BatchMode=yes", "-o", "ConnectTimeout=8",
            alias, "printf %s \"$HOME\"")).Trim();

        if (string.IsNullOrWhiteSpace(home) || !home.StartsWith('/'))
        {
            throw new InvalidOperationException("Não foi possível descobrir o home remoto.");
        }

        return home;
    }

    private static async Task RunAsync(string exe, params string[] args)
    {
        var (code, _, error) = await RunProcessAsync(exe, args, capture: false);
        if (code != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"código {code}"
                : error.Trim());
        }
    }

    private static async Task<string> RunCaptureAsync(string exe, params string[] args)
    {
        var (code, output, error) = await RunProcessAsync(exe, args, capture: true);
        if (code != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"código {code}"
                : error.Trim());
        }

        return output;
    }

    private static async Task<(int Code, string Output, string Error)> RunProcessAsync(
        string exe, string[] args, bool capture)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = true
        };

        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível executar {Path.GetFileName(exe)}.");

        var outputTask = capture ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static string SshPath
    {
        get
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            return File.Exists(candidate) ? candidate : "ssh.exe";
        }
    }

    private static string ScpPath
    {
        get
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "scp.exe");
            return File.Exists(candidate) ? candidate : "scp.exe";
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClassNameText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool IsCtrlDown() => (GetAsyncKeyState(VkControl) & 0x8000) != 0;
    private static bool IsShiftDown() => (GetAsyncKeyState(VkShift) & 0x8000) != 0;

    private static bool IsAltDown() =>
        (GetAsyncKeyState(VkMenu) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkLMenu) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkRMenu) & 0x8000) != 0;

    private static void SendText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var bracketed = normalized.Contains('\n');

        if (bracketed)
        {
            SendEscape();
            SendUnicode("[200~");
        }

        SendUnicode(normalized.Replace('\n', '\r'));

        if (bracketed)
        {
            SendEscape();
            SendUnicode("[201~");
        }
    }

    private static void SendEscape()
    {
        keybd_event(0x1B, 0, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0, 2, UIntPtr.Zero);
    }

    private static void SendUnicode(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                keybd_event(VkReturn, 0, 0, UIntPtr.Zero);
                keybd_event(VkReturn, 0, 2, UIntPtr.Zero);
                continue;
            }

            var inputs = new Input[2];
            inputs[0].Type = InputKeyboard;
            inputs[0].Union.Keyboard = new KeybdInput
            {
                Scan = ch,
                Flags = KeyeventfUnicode
            };
            inputs[1] = inputs[0];
            inputs[1].Union.Keyboard.Flags = KeyeventfUnicode | KeyeventfKeyup;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
            File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    protected override void ExitThreadCore()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _watchTimer.Stop();
        _pasteTimer.Stop();
        _watchTimer.Dispose();
        _pasteTimer.Dispose();
        try { _pump.Dispose(); } catch { }
        Log("Ponte encerrada.");
        base.ExitThreadCore();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeybdInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookExMouse(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

internal sealed record BridgeSettings(string LanAlias, string ExternalAlias, string LanProbeHost, int LanProbePort)
{
    public static BridgeSettings Load()
    {
        var defaults = new BridgeSettings("storage-lan", "storage", "192.168.1.30", 22);
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StorageTerm", "settings.json");

            if (!File.Exists(file)) return defaults;

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            return new BridgeSettings(
                GetString(root, "LanAlias") ?? defaults.LanAlias,
                GetString(root, "ExternalAlias") ?? defaults.ExternalAlias,
                GetString(root, "LanProbeHost") ?? defaults.LanProbeHost,
                GetInt(root, "LanProbePort") ?? defaults.LanProbePort);
        }
        catch
        {
            return defaults;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
}
