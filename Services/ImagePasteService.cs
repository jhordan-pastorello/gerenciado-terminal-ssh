using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace StorageTerm.Services;

/// <summary>
/// Salva imagens da área de transferência e envia para o servidor via scp,
/// devolvendo o caminho remoto para colar no terminal (agentes CLI incluídos).
/// </summary>
public static class ImagePasteService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff"
    };

    private static readonly Dictionary<string, string> HomeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool IsImageFile(string path)
    {
        try
        {
            return File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path));
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> FilterImages(IEnumerable<string> paths) =>
        paths.Where(IsImageFile).ToArray();

    public static string SaveClipboardImage(BitmapSource image)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StorageTerm", "pastes");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"paste-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        encoder.Save(stream);
        return file;
    }

    public static async Task<IReadOnlyList<string>> UploadAsync(
        string alias,
        IReadOnlyList<string> localFiles,
        CancellationToken cancellationToken = default)
    {
        if (localFiles.Count == 0) return Array.Empty<string>();
        if (!SshCommandBuilder.IsValidAlias(alias))
        {
            throw new InvalidOperationException($"Alias SSH inválido para envio de imagem: '{alias}'.");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            const string remoteDir = "storage-term-pastes";
            var home = await GetHomeAsync(alias, cancellationToken);
            await RunAsync(
                SshCommandBuilder.SshPath,
                new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=8", alias, $"mkdir -p {remoteDir}" },
                cancellationToken);

            var remotePaths = new List<string>(localFiles.Count);
            foreach (var localFile in localFiles)
            {
                var extension = Path.GetExtension(localFile).ToLowerInvariant();
                if (!ImageExtensions.Contains(extension)) extension = ".png";

                var name = $"paste-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}";
                await RunAsync(
                    SshCommandBuilder.ScpPath,
                    new[]
                    {
                        "-q", "-o", "BatchMode=yes", "-o", "ConnectTimeout=8",
                        localFile, $"{alias}:{remoteDir}/{name}"
                    },
                    cancellationToken);

                remotePaths.Add($"{home}/{remoteDir}/{name}");
            }

            return remotePaths;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> GetHomeAsync(string alias, CancellationToken cancellationToken)
    {
        if (HomeCache.TryGetValue(alias, out var cached)) return cached;

        var home = (await RunCaptureAsync(
            SshCommandBuilder.SshPath,
            new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=8", alias, "printf %s \"$HOME\"" },
            cancellationToken)).Trim();

        if (string.IsNullOrWhiteSpace(home) || !home.StartsWith('/'))
        {
            throw new InvalidOperationException("Não foi possível descobrir o diretório home no servidor.");
        }

        HomeCache[alias] = home;
        return home;
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (exitCode, _, error) = await RunProcessAsync(executable, arguments, captureOutput: false, cancellationToken);
        if (exitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? $"código {exitCode}" : error.Trim();
            throw new InvalidOperationException($"Falha ao enviar a imagem: {detail}");
        }
    }

    private static async Task<string> RunCaptureAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await RunProcessAsync(executable, arguments, captureOutput: true, cancellationToken);
        if (exitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? $"código {exitCode}" : error.Trim();
            throw new InvalidOperationException(detail);
        }

        return output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool captureOutput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = true
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível executar {Path.GetFileName(executable)}.");

        var outputTask = captureOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
