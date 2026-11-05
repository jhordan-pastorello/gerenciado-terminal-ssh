using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StorageTerm.Models;

namespace StorageTerm.Services;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StorageTerm");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogFile => Path.Combine(DataDirectory, "storage-term.log");
    public static string SshConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
}

public sealed class AppSettings
{
    public const int MaxTerminals = 6;

    public ConnectionMode Mode { get; set; } = ConnectionMode.Auto;

    public string LanAlias { get; set; } = "storage-lan";
    public string ExternalAlias { get; set; } = "storage";
    public string LanProbeHost { get; set; } = "192.168.1.30";
    public int LanProbePort { get; set; } = 22;

    public bool AutoReconnect { get; set; } = true;
    public bool KeepAwake { get; set; } = true;
    public bool PersistentSession { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool ConfirmClose { get; set; } = true;
    public bool HideToTrayOnClose { get; set; } = true;

    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 13;

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }

    [JsonIgnore]
    public string PreferredLanLabel => Mode switch
    {
        ConnectionMode.Lan => "LAN forçada",
        ConnectionMode.External => "Externo forçado",
        _ => "Automático"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao carregar configurações; usando padrões.", ex);
            return new AppSettings();
        }
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        LanAlias = CleanAlias(LanAlias, "storage-lan");
        ExternalAlias = CleanAlias(ExternalAlias, "storage");
        LanProbeHost = string.IsNullOrWhiteSpace(LanProbeHost) ? "192.168.1.30" : LanProbeHost.Trim();
        LanProbePort = LanProbePort is < 1 or > 65535 ? 22 : LanProbePort;
        FontSize = Math.Clamp(FontSize, 9, 24);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Cascadia Mono" : FontFamily.Trim();
        WindowWidth = Math.Clamp(WindowWidth, 900, 8000);
        WindowHeight = Math.Clamp(WindowHeight, 560, 8000);
        if (!Enum.IsDefined(Mode)) Mode = ConnectionMode.Auto;
    }

    public void Save()
    {
        try
        {
            Normalize();
            var dir = AppPaths.DataDirectory;
            var temp = Path.Combine(dir, "settings.tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar configurações.", ex);
        }
    }

    private static string CleanAlias(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = value.Trim();
        return cleaned.Length > 128 ? cleaned[..128] : cleaned;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"mode={Mode}; lan={LanAlias}; ext={ExternalAlias}; probe={LanProbeHost}:{LanProbePort}; reconnect={AutoReconnect}; awake={KeepAwake}");
}


