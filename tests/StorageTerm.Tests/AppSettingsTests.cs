using System.Text.Json;
using StorageTerm.Services;

namespace StorageTerm.Tests;

public class AppSettingsTests
{
    [Fact]
    public void FromJson_WithoutTmuxArrowBridge_DefaultsToFalse()
    {
        var settings = AppSettings.FromJson("{ \"LanAlias\": \"x\", \"PersistentSession\": true }");

        Assert.False(settings.TmuxArrowBridge);
    }

    [Fact]
    public void ToJson_IncludesTmuxArrowBridgeAndRoundTrips()
    {
        var original = new AppSettings { TmuxArrowBridge = true };

        var json = original.ToJson();
        var loaded = AppSettings.FromJson(json);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("TmuxArrowBridge").GetBoolean());
        Assert.True(loaded.TmuxArrowBridge);
    }

    [Fact]
    public void FromJson_WithoutWin32InputMode_DefaultsToTrue()
    {
        var settings = AppSettings.FromJson("{}");

        Assert.True(settings.Win32InputMode);
    }

    [Fact]
    public void FromJson_WithWin32InputModeFalse_IsFalse()
    {
        var settings = AppSettings.FromJson("{ \"Win32InputMode\": false }");

        Assert.False(settings.Win32InputMode);
    }
}
