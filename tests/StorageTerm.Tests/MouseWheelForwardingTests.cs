using StorageTerm.Controls;

namespace StorageTerm.Tests;

public class MouseWheelForwardingTests
{
    private static short HighWord(IntPtr value) => (short)((value.ToInt64() >> 16) & 0xFFFF);

    private static short LowWord(IntPtr value) => (short)(value.ToInt64() & 0xFFFF);

    [Theory]
    [InlineData(120)]
    [InlineData(-120)]
    [InlineData(240)]
    [InlineData(-360)]
    public void BuildMouseWheelParams_PacksDeltaInHighWord(int delta)
    {
        var (wParam, _) = TerminalPane.BuildMouseWheelParams(delta, 0, 0, 0);

        Assert.Equal(delta, HighWord(wParam));
        Assert.Equal(0, LowWord(wParam));
    }

    [Fact]
    public void BuildMouseWheelParams_PacksKeyStateInLowWord()
    {
        const int MkControl = 0x0008;
        var (wParam, _) = TerminalPane.BuildMouseWheelParams(-120, MkControl, 0, 0);

        Assert.Equal(-120, HighWord(wParam));
        Assert.Equal(MkControl, LowWord(wParam));
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(-1920, 300)]
    [InlineData(100, -50)]
    public void BuildMouseWheelParams_PacksSignedCoordinates(int x, int y)
    {
        var (_, lParam) = TerminalPane.BuildMouseWheelParams(120, 0, x, y);

        Assert.Equal(x, LowWord(lParam));
        Assert.Equal(y, HighWord(lParam));
    }

    [Fact]
    public void BuildMouseWheelParams_TruncatesDeltaToSignedShort()
    {
        var (wParam, _) = TerminalPane.BuildMouseWheelParams(0x10000 - 120, 0, 0, 0);

        Assert.Equal(-120, HighWord(wParam));
    }
}
