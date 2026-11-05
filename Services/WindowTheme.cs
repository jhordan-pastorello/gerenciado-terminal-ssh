using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StorageTerm.Services;

public static class WindowTheme
{
    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var dark = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));

            var caption = 0x000A0A0A; // #0A0A0A (COLORREF)
            _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));

            var text = 0x00EDEDED; // #EDEDED
            _ = DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));

            var border = 0x002A2A2A; // #2A2A2A
            _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));

            var corner = 2;
            _ = DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao aplicar o tema da janela: {ex.Message}");
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}


