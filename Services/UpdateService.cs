using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StorageTerm.Services;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string ZipUrl,
    string ChecksumUrl,
    string ReleaseUrl);

/// <summary>
/// Auto-update via GitHub Releases. Só roda na versão instalada (marcador install.json),
/// baixa o zip self-contained, confere o SHA-256 e aplica ao sair ou sob demanda.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryOwner = "jhordan-pastorello";
    public const string RepositoryName = "gerenciado-terminal-ssh";
    public const string RepositoryUrl = "https://github.com/jhordan-pastorello/gerenciado-terminal-ssh";
    private const string AssetName = "StorageTerm-win-x64.zip";
    private const string ChecksumAssetName = AssetName + ".sha256";

    private static readonly Regex SafeTag = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    private static readonly HttpClient Client = CreateClient();

    public static bool IsInstalledBuild =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "install.json"));

    private readonly string? _updatesDirectoryOverride;

    public UpdateService(string? updatesDirectory = null)
    {
        _updatesDirectoryOverride = updatesDirectory;
    }

    public string UpdatesDirectory => _updatesDirectoryOverride ?? Path.Combine(AppPaths.DataDirectory, "updates");

    public static string VersionLabel => CurrentVersion.ToString(3);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
        using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warn($"Updater: GitHub respondeu {(int)response.StatusCode} ao procurar releases.");
            return null;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || !SafeTag.IsMatch(tag))
        {
            Log.Warn("Updater: release sem tag válida.");
            return null;
        }

        if (!IsNewer(tag, CurrentVersion))
        {
            Log.Info($"Updater: versão atual {VersionLabel}; última release {tag}.");
            return null;
        }

        var cleanVersion = tag.TrimStart('v', 'V');
        if (!Version.TryParse(cleanVersion, out var version))
        {
            Log.Warn($"Updater: tag inválida '{tag}'.");
            return null;
        }

        string? zipUrl = null;
        string? checksumUrl = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var download = asset.TryGetProperty("browser_download_url", out var downloadElement)
                    ? downloadElement.GetString()
                    : null;

                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) zipUrl = download;
                if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksumUrl = download;
            }
        }

        if (string.IsNullOrWhiteSpace(zipUrl) || string.IsNullOrWhiteSpace(checksumUrl))
        {
            Log.Warn($"Updater: release {tag} não contém {AssetName} e {ChecksumAssetName}.");
            return null;
        }

        var releaseUrl = root.TryGetProperty("html_url", out var htmlElement)
            ? htmlElement.GetString() ?? RepositoryUrl
            : RepositoryUrl;

        Log.Info($"Updater: nova versão {tag} disponível (atual {VersionLabel}).");
        return new UpdateInfo(version, tag, zipUrl, checksumUrl, releaseUrl);
    }

    /// <summary>
    /// Procura um zip já baixado e verificado (o arquivo só recebe o nome final após o SHA-256 conferir)
    /// em <see cref="UpdatesDirectory"/>. Retorna o mais novo cuja versão supera a atual.
    /// </summary>
    public (UpdateInfo Info, string ZipPath)? TryLoadPendingDownload()
    {
        try
        {
            var root = UpdatesDirectory;
            if (!Directory.Exists(root)) return null;

            (UpdateInfo Info, string ZipPath)? best = null;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var tag = Path.GetFileName(directory);
                if (!SafeTag.IsMatch(tag) || !IsNewer(tag, CurrentVersion)) continue;
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) continue;

                var zipPath = Path.Combine(directory, AssetName);
                if (!File.Exists(zipPath)) continue;

                if (best is { } current && Normalize(current.Info.Version) >= Normalize(version)) continue;

                var info = new UpdateInfo(version, tag, string.Empty, string.Empty,
                    $"{RepositoryUrl}/releases/tag/{tag}");
                best = (info, zipPath);
            }

            if (best is { } found)
            {
                Log.Info($"Updater: download pendente {found.Info.Tag} encontrado em disco.");
            }

            return best;
        }
        catch (Exception ex)
        {
            Log.Warn($"Updater: falha ao procurar download pendente: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> DownloadAsync(UpdateInfo info, CancellationToken cancellationToken = default)
    {
        if (!SafeTag.IsMatch(info.Tag))
        {
            Log.Warn($"Updater: tag insegura '{info.Tag}'.");
            return null;
        }

        var expected = (await Client.GetStringAsync(info.ChecksumUrl, cancellationToken).ConfigureAwait(false))
            .Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (expected is null || expected.Length != 64)
        {
            Log.Warn("Updater: checksum inválido na release.");
            return null;
        }

        var directory = Path.Combine(UpdatesDirectory, info.Tag);
        Directory.CreateDirectory(directory);

        var zipPath = Path.Combine(directory, AssetName);
        var partialPath = zipPath + ".part";

        try
        {
            await using (var source = await Client.GetStreamAsync(info.ZipUrl, cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(partialPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            await using (var stream = File.OpenRead(partialPath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(partialPath);
                    Log.Error("Updater: SHA-256 do download não confere.");
                    return null;
                }
            }

            File.Move(partialPath, zipPath, overwrite: true);
            Log.Info($"Updater: {info.Tag} baixado e verificado ({new FileInfo(zipPath).Length / 1024 / 1024} MB).");
            return zipPath;
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            throw;
        }
    }

    public void Apply(string zipPath, bool restart)
    {
        if (!IsInstalledBuild)
        {
            throw new InvalidOperationException("Auto-update só está disponível na versão instalada.");
        }

        Directory.CreateDirectory(UpdatesDirectory);

        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executable = Environment.ProcessPath ?? Path.Combine(installDirectory, "StorageTerm.exe");
        var scriptPath = Path.Combine(UpdatesDirectory, "apply-update.ps1");

        File.WriteAllText(scriptPath, UpdaterScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-Zip");
        startInfo.ArgumentList.Add(zipPath);
        startInfo.ArgumentList.Add("-InstallDir");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("-Exe");
        startInfo.ArgumentList.Add(executable);
        if (restart) startInfo.ArgumentList.Add("-Restart");

        Process.Start(startInfo);
        Log.Info($"Updater: aplicação de {Path.GetFileName(Path.GetDirectoryName(zipPath))} iniciada (restart={restart}).");
    }

    public static bool IsNewer(string? tag, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var clean = tag.Trim().TrimStart('v', 'V');
        if (clean.Contains('-')) return false;
        if (!Version.TryParse(clean, out var remote)) return false;

        return Normalize(remote) > Normalize(current);
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static Version ResolveCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"StorageTerm-Updater/{VersionLabel}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private const string UpdaterScript = """
        param(
            [int]$ProcessId,
            [string]$Zip,
            [string]$InstallDir,
            [string]$Exe,
            [switch]$Restart
        )

        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Milliseconds 900

        $applied = $false
        $staging = Join-Path $env:TEMP ("StorageTerm-update-" + [guid]::NewGuid().ToString("N"))
        $logDir = Join-Path $env:LOCALAPPDATA "StorageTerm"
        $log = Join-Path $logDir "updater-last.log"
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        function Write-Log([string]$m) { "$(Get-Date -Format s) $m" | Add-Content -LiteralPath $log -Encoding UTF8 }
        Set-Content -LiteralPath $log -Value "" -Encoding UTF8

        try {
            Write-Log "Extraindo $Zip para $staging"
            Expand-Archive -LiteralPath $Zip -DestinationPath $staging -Force -ErrorAction Stop

            $src = $staging
            if (-not (Test-Path -LiteralPath (Join-Path $src "StorageTerm.exe"))) {
                $inner = Get-ChildItem -LiteralPath $staging -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "StorageTerm.exe") } | Select-Object -First 1
                if ($inner) { $src = $inner.FullName } else { throw "StorageTerm.exe não encontrado no pacote." }
            }

            $installedExe = Join-Path $InstallDir "StorageTerm.exe"
            if (Test-Path -LiteralPath $installedExe) {
                $locked = $true
                for ($i = 0; $i -lt 10 -and $locked; $i++) {
                    try {
                        $fs = [System.IO.File]::Open($installedExe, 'Open', 'ReadWrite', 'None')
                        $fs.Close()
                        $locked = $false
                    } catch { Start-Sleep -Seconds 1 }
                }
                if ($locked) { throw "StorageTerm.exe continua em uso; atualização abortada sem alterar a instalação." }
            }

            Write-Log "Copiando $src para $InstallDir"
            & robocopy $src $InstallDir /E /R:5 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy falhou (código $LASTEXITCODE)." }

            Remove-Item -LiteralPath $Zip -Force -ErrorAction SilentlyContinue
            $applied = $true
            Write-Log "Atualização aplicada."
        } catch {
            # Mantém o zip para a próxima tentativa.
            Write-Log ("Falha: " + $_.Exception.Message)
        } finally {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }

        if ($Restart -and $applied) {
            Start-Process -FilePath $Exe -WorkingDirectory $InstallDir
        }
        """;
}
