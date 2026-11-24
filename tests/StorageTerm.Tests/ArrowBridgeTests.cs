namespace StorageTerm.Tests;

public class ArrowBridgeTests
{
    [Theory]
    [InlineData(0x25u)]
    [InlineData(0x26u)]
    [InlineData(0x27u)]
    [InlineData(0x28u)]
    public void ArrowKeys_WithBridgeAndPersistent_Bridge(uint vk)
    {
        Assert.True(MainWindow.ShouldBridgeArrowKeys(vk, false, false, false, true, true));
    }

    [Theory]
    [InlineData(0x24u)]
    [InlineData(0x29u)]
    [InlineData(0x41u)]
    public void NonArrowKeys_DoNotBridge(uint vk)
    {
        Assert.False(MainWindow.ShouldBridgeArrowKeys(vk, false, false, false, true, true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Modifiers_DoNotBridge(bool ctrl, bool alt, bool shift)
    {
        Assert.False(MainWindow.ShouldBridgeArrowKeys(0x26, ctrl, alt, shift, true, true));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void BridgeOrPersistentOff_DoesNotBridge(bool bridge, bool persistent)
    {
        Assert.False(MainWindow.ShouldBridgeArrowKeys(0x26, false, false, false, bridge, persistent));
    }
}
