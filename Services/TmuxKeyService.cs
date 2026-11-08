using System.Diagnostics;

namespace StorageTerm.Services;

/// <summary>
/// Envia teclas de navegação pelo próprio tmux. Alguns TUIs (Cursor/Kiro)
/// não recebem setas pelo caminho do ConPTY; o tmux entrega a tecla certa
/// para o painel, inclusive em menus.
/// </summary>
public static class TmuxKeyService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task SendAsync(string alias, int sessionSlot, string key, int repeat = 1)
    {
        if (!SshCommandBuilder.IsValidAlias(alias) || sessionSlot <= 0) return;
        if (repeat <= 0) repeat = 1;
        if (repeat > 50) repeat = 50;

        await Gate.WaitAsync();
        try
        {
            var startInfo = new ProcessStartInfo(SshCommandBuilder.SshPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("BatchMode=yes");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("ConnectTimeout=3");
            startInfo.ArgumentList.Add(alias);

            var keys = string.Join(' ', Enumerable.Repeat(key, repeat));
            startInfo.ArgumentList.Add($"tmux send-keys -t storage-{sessionSlot} {keys}");

            using var process = Process.Start(startInfo);
            if (process == null) return;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await errorTask;
            _ = await outputTask;

            if (process.ExitCode != 0)
            {
                Log.Warn($"tmux send-keys saiu com {process.ExitCode}: {error.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao enviar seta pelo tmux: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }
}
