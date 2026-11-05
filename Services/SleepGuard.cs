using System.Runtime.InteropServices;

namespace StorageTerm.Services;

/// <summary>
/// Mantém o Windows acordado enquanto existir sessão ativa. Precisa de uma thread
/// dedicada porque SetThreadExecutionState é um estado por thread.
/// </summary>
public sealed class SleepGuard : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private readonly object _gate = new();
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private bool _enabled;

    public bool IsEnabled
    {
        get { lock (_gate) return _enabled; }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled == _enabled) return;
            _enabled = enabled;

            if (enabled)
            {
                var stop = new ManualResetEventSlim(false);
                _stop = stop;
                _thread = new Thread(() => Run(stop))
                {
                    IsBackground = true,
                    Name = "StorageTerm.SleepGuard"
                };
                _thread.Start();
                Log.Info("Proteção contra suspensão ativada.");
            }
            else
            {
                _stop?.Set();
                _stop = null;
                _thread = null;
                Log.Info("Proteção contra suspensão desativada.");
            }
        }
    }

    private static void Run(ManualResetEventSlim stop)
    {
        try
        {
            _ = SetThreadExecutionState(EsContinuous | EsSystemRequired);
            stop.Wait();
        }
        catch (Exception ex)
        {
            Log.Error("Falha na proteção contra suspensão.", ex);
        }
        finally
        {
            _ = SetThreadExecutionState(EsContinuous);
        }
    }

    public void Dispose()
    {
        SetEnabled(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
