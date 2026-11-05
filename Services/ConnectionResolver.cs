using System.Diagnostics;
using System.Net.Sockets;
using StorageTerm.Models;

namespace StorageTerm.Services;

/// <summary>
/// Decide entre storage-lan e storage. O modo automático testa a porta 22 do host da LAN
/// e cai para o perfil externo (Tailscale) quando a LAN não responde.
/// </summary>
public sealed class ConnectionResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);

    private readonly object _gate = new();
    private ProbeResult? _lastProbe;
    private DateTime _lastProbeAt = DateTime.MinValue;

    public ProbeResult? LastProbe
    {
        get
        {
            lock (_gate) return _lastProbe;
        }
    }

    public bool IsCacheFresh
    {
        get
        {
            lock (_gate) return _lastProbe != null && DateTime.UtcNow - _lastProbeAt < CacheLifetime;
        }
    }

    public async Task<ProbeResult> ProbeAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.LanProbeHost))
        {
            var empty = new ProbeResult(false, 0, "host de teste vazio");
            Remember(empty);
            return empty;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(settings.LanProbeHost.Trim(), settings.LanProbePort, timeout.Token)
                        .ConfigureAwait(false);

            sw.Stop();
            var ok = new ProbeResult(true, (int)sw.ElapsedMilliseconds, null);
            Remember(ok);
            return ok;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var result = new ProbeResult(false, (int)sw.ElapsedMilliseconds, "tempo esgotado");
            Remember(result);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var result = new ProbeResult(false, (int)sw.ElapsedMilliseconds, ex.Message);
            Remember(result);
            return result;
        }
    }

    /// <summary>Resolução instantânea para abrir terminais sem atraso.</summary>
    public ConnectionTarget Resolve(AppSettings settings, ConnectionMode? perPaneOverride = null)
    {
        var mode = perPaneOverride ?? settings.Mode;

        if (mode == ConnectionMode.Auto)
        {
            bool lanOk;
            int latency;
            lock (_gate)
            {
                lanOk = _lastProbe?.LanReachable ?? false;
                latency = _lastProbe?.LatencyMs ?? 0;
            }

            mode = lanOk ? ConnectionMode.Lan : ConnectionMode.External;
            return new ConnectionTarget(
                mode == ConnectionMode.Lan ? settings.LanAlias : settings.ExternalAlias,
                mode,
                lanOk,
                latency);
        }

        return new ConnectionTarget(
            mode == ConnectionMode.Lan ? settings.LanAlias : settings.ExternalAlias,
            mode,
            mode == ConnectionMode.Lan,
            LastProbe?.LatencyMs ?? 0);
    }

    public ConnectionTarget Demo() => new("demo", ConnectionMode.External, false, 0);

    private void Remember(ProbeResult result)
    {
        lock (_gate)
        {
            _lastProbe = result;
            _lastProbeAt = DateTime.UtcNow;
        }
    }
}
