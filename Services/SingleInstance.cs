using System.IO;
using System.IO.Pipes;
using System.Text;

namespace StorageTerm.Services;

/// <summary>
/// Garante uma única instância. A segunda instância pede para a primeira mostrar a janela.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\StorageTerm.SingleInstance";
    private const string PipeName = "StorageTerm.IPC.v1";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _serverCts;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _ownsMutex = createdNew;
    }

    public void StartServer(Action onShowRequested)
    {
        if (!IsFirstInstance) return;

        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                    var message = await reader.ReadLineAsync(token).ConfigureAwait(false);

                    if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                    {
                        onShowRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Falha no canal de instância única.", ex);
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    public static async Task<bool> SignalShowAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync("SHOW").ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _serverCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }
        }

        _mutex.Dispose();
    }
}

