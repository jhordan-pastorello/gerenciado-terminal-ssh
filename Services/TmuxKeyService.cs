using System.Diagnostics;
using System.IO;
using System.Text;

namespace StorageTerm.Services;

/// <summary>
/// Envia teclas de navegação pelo próprio tmux. Alguns TUIs (Cursor/Kiro)
/// não recebem setas pelo caminho do ConPTY; o tmux entrega a tecla certa
/// para o painel, inclusive em menus.
///
/// A conexão SSH fica aberta em segundo plano (uma por alias) lendo linhas
/// "painel tecla" e repassando ao tmux. Assim cada seta custa só um write
/// no pipe, sem abrir um ssh novo por tecla.
/// </summary>
public static class TmuxKeyService
{
    private const string RemoteLoop =
        "while read p k; do /usr/bin/tmux send-keys -t $p $k; done";

    private sealed class Pump
    {
        public Pump(Process process)
        {
            Process = process;
            Writer = process.StandardInput;
            Writer.AutoFlush = true;
            Writer.NewLine = "\n";
        }

        public Process Process { get; }
        public StreamWriter Writer { get; }
        public object Sync { get; } = new();
    }

    private static readonly object MapGate = new();
    private static readonly Dictionary<string, Pump> Pumps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Abre a conexão de teclas antes da primeira seta (aquece o caminho).</summary>
    public static void WarmUp(string alias)
    {
        if (!SshCommandBuilder.IsValidAlias(alias)) return;

        try
        {
            GetOrCreate(alias);
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao preparar o canal de teclas: {ex.Message}");
        }
    }

    public static Task SendAsync(string alias, int sessionSlot, string key, int repeat = 1)
    {
        Send(alias, sessionSlot, key, repeat);
        return Task.CompletedTask;
    }

    public static void Send(string alias, int sessionSlot, string key, int repeat = 1)
    {
        if (!SshCommandBuilder.IsValidAlias(alias) || sessionSlot <= 0) return;
        if (string.IsNullOrWhiteSpace(key)) return;

        if (repeat <= 0) repeat = 1;
        if (repeat > 50) repeat = 50;

        var keys = string.Join(' ', Enumerable.Repeat(key, repeat));
        var line = $"storage-{sessionSlot} {keys}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var pump = GetOrCreate(alias);
                lock (pump.Sync)
                {
                    pump.Writer.WriteLine(line);
                    pump.Writer.Flush();
                }

                return;
            }
            catch (Exception ex)
            {
                Drop(alias);
                if (attempt == 1)
                {
                    Log.Warn($"Falha ao enviar tecla pelo tmux: {ex.Message}");
                }
            }
        }
    }

    public static void Shutdown()
    {
        List<Pump> pumps;
        lock (MapGate)
        {
            pumps = Pumps.Values.ToList();
            Pumps.Clear();
        }

        foreach (var pump in pumps)
        {
            try
            {
                if (!pump.Process.HasExited)
                {
                    pump.Process.StandardInput.Close();
                    pump.Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }

            pump.Process.Dispose();
        }
    }

    private static Pump GetOrCreate(string alias)
    {
        lock (MapGate)
        {
            if (Pumps.TryGetValue(alias, out var existing))
            {
                if (!existing.Process.HasExited) return existing;
                Pumps.Remove(alias);
            }

            var pump = Start(alias);
            Pumps[alias] = pump;
            return pump;
        }
    }

    private static void Drop(string alias)
    {
        Pump? pump = null;
        lock (MapGate)
        {
            if (Pumps.TryGetValue(alias, out var existing))
            {
                pump = existing;
                Pumps.Remove(alias);
            }
        }

        if (pump == null) return;

        try
        {
            if (!pump.Process.HasExited)
            {
                pump.Process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        pump.Process.Dispose();
    }

    private static Pump Start(string alias)
    {
        var startInfo = new ProcessStartInfo(SshCommandBuilder.SshPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };

        startInfo.ArgumentList.Add("-T");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=5");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveInterval=15");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveCountMax=3");
        startInfo.ArgumentList.Add(alias);
        startInfo.ArgumentList.Add(RemoteLoop);

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("ssh não iniciou.");

        // Drena a saída para o pipe não encher e travar o ssh.
        _ = Task.Run(async () =>
        {
            try { await process.StandardOutput.ReadToEndAsync(); } catch { }
        });
        _ = Task.Run(async () =>
        {
            try { await process.StandardError.ReadToEndAsync(); } catch { }
        });

        Log.Info($"Canal de teclas pronto para {alias} (pid {process.Id}).");
        return new Pump(process);
    }
}
