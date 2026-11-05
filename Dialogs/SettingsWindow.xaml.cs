using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StorageTerm.Models;
using StorageTerm.Services;

namespace StorageTerm.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _source;
    private readonly Func<AppSettings, Task<ProbeResult>> _probe;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings settings, Func<AppSettings, Task<ProbeResult>> probe)
    {
        _source = settings;
        _probe = probe;

        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.Apply(this);

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 40));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 40));

        LoadValues();
        Loaded += (_, _) => LanAliasBox.Focus();
    }

    private void LoadValues()
    {
        ModeAuto.IsChecked = _source.Mode == ConnectionMode.Auto;
        ModeLan.IsChecked = _source.Mode == ConnectionMode.Lan;
        ModeExternal.IsChecked = _source.Mode == ConnectionMode.External;

        LanAliasBox.Text = _source.LanAlias;
        ExternalAliasBox.Text = _source.ExternalAlias;
        ProbeHostBox.Text = _source.LanProbeHost;
        ProbePortBox.Text = _source.LanProbePort.ToString(CultureInfo.InvariantCulture);

        AutoReconnectCheck.IsChecked = _source.AutoReconnect;
        PersistentSessionCheck.IsChecked = _source.PersistentSession;
        ConfirmCloseCheck.IsChecked = _source.ConfirmClose;
        KeepAwakeCheck.IsChecked = _source.KeepAwake;
        StartWithWindowsCheck.IsChecked = _source.StartWithWindows;
        StartMinimizedCheck.IsChecked = _source.StartMinimized;
        HideToTrayCheck.IsChecked = _source.HideToTrayOnClose;

        FontFamilyBox.Text = _source.FontFamily;

        var rounded = ((int)Math.Round(_source.FontSize)).ToString(CultureInfo.InvariantCulture);
        foreach (var radio in SizePanel.Children.OfType<RadioButton>())
        {
            radio.IsChecked = string.Equals(radio.Tag?.ToString(), rounded, StringComparison.Ordinal);
        }

        if (!SizePanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
        {
            SizePanel.Children.OfType<RadioButton>().First(r => r.Tag?.ToString() == "13").IsChecked = true;
        }

        ExecutablePathText.Text = "Executável atual:\n" + Environment.ProcessPath;
        RefreshDetectedProfiles(_source);
    }

    private void RefreshDetectedProfiles(AppSettings? settings = null)
    {
        var lanAlias = (settings?.LanAlias ?? LanAliasBox.Text).Trim();
        var externalAlias = (settings?.ExternalAlias ?? ExternalAliasBox.Text).Trim();

        var lan = SshConfigReader.Resolve(lanAlias);
        var external = SshConfigReader.Resolve(externalAlias);

        DetectedProfilesText.Text =
            "LAN: " + Describe(lan) + Environment.NewLine +
            "Externo: " + Describe(external);
    }

    private static string Describe(SshProfile profile) =>
        profile.Found
            ? $"{profile.UserAtAlias} · {profile.Endpoint} · {profile.IdentityName}"
            : "alias não encontrado no ~/.ssh/config";

    private AppSettings Collect()
    {
        var settings = _source.Clone();

        settings.Mode = ModeLan.IsChecked == true
            ? ConnectionMode.Lan
            : ModeExternal.IsChecked == true
                ? ConnectionMode.External
                : ConnectionMode.Auto;

        settings.LanAlias = LanAliasBox.Text.Trim();
        settings.ExternalAlias = ExternalAliasBox.Text.Trim();
        settings.LanProbeHost = ProbeHostBox.Text.Trim();

        settings.LanProbePort = int.TryParse(ProbePortBox.Text.Trim(), out var port)
            ? port
            : 0;

        settings.AutoReconnect = AutoReconnectCheck.IsChecked == true;
        settings.PersistentSession = PersistentSessionCheck.IsChecked == true;
        settings.ConfirmClose = ConfirmCloseCheck.IsChecked == true;
        settings.KeepAwake = KeepAwakeCheck.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
        settings.HideToTrayOnClose = HideToTrayCheck.IsChecked == true;
        settings.FontFamily = FontFamilyBox.Text.Trim();

        var sizeRadio = SizePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (sizeRadio?.Tag != null && double.TryParse(sizeRadio.Tag.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
        {
            settings.FontSize = size;
        }

        return settings;
    }

    private static bool ValidateAliases(AppSettings settings, out string error)
    {
        if (!SshCommandBuilder.IsValidAlias(settings.LanAlias))
        {
            error = "Alias da LAN inválido. Use letras, números, ponto, hífen, dois-pontos ou @.";
            return false;
        }

        if (!SshCommandBuilder.IsValidAlias(settings.ExternalAlias))
        {
            error = "Alias externo inválido. Use letras, números, ponto, hífen, dois-pontos ou @.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.LanProbeHost))
        {
            error = "Informe o host de teste da LAN (ex.: 192.168.1.30).";
            return false;
        }

        if (settings.LanProbePort is < 1 or > 65535)
        {
            error = "A porta de teste deve estar entre 1 e 65535.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var settings = Collect();
        if (!ValidateAliases(settings, out var error))
        {
            TestResult.Text = error;
            TestResult.Foreground = (Brush)FindResource("RedBrush");
            return;
        }

        RefreshDetectedProfiles(settings);

        var lanProfile = SshConfigReader.Resolve(settings.LanAlias);
        if (!lanProfile.Found)
        {
            TestResult.Text = $"Alias {settings.LanAlias} não encontrado no ~/.ssh/config.";
            TestResult.Foreground = (Brush)FindResource("RedBrush");
            return;
        }

        TestButton.IsEnabled = false;
        TestResult.Text = "Testando…";
        TestResult.Foreground = (Brush)FindResource("TextDimBrush");

        try
        {
            var probe = await _probe(settings);
            if (probe.LanReachable)
            {
                TestResult.Text = $"LAN respondeu em {probe.LatencyMs} ms — o modo automático usará {settings.LanAlias}.";
                TestResult.Foreground = (Brush)FindResource("GreenBrush");
            }
            else
            {
                TestResult.Text = $"LAN sem resposta — o modo automático usará {settings.ExternalAlias} (Tailscale).";
                TestResult.Foreground = (Brush)FindResource("AmberBrush");
            }
        }
        catch (Exception ex)
        {
            TestResult.Text = "Falha no teste: " + ex.Message;
            TestResult.Foreground = (Brush)FindResource("RedBrush");
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = Collect();
        if (!ValidateAliases(settings, out var error))
        {
            ErrorText.Text = error;
            return;
        }

        Result = settings;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
