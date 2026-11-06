using System.Diagnostics;
using System.Text;

namespace StorageTerm.Services;

/// <summary>
/// Descobre a branch git de cada sessão tmux consultando o path atual do pane.
/// Uma única conexão SSH por alias atende todos os terminais.
/// </summary>
public static class BranchService
{
    public static async Task<Dictionary<string, string>> FetchAsync(
        string alias,
        IReadOnlyList<string> sessions,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sessions.Count == 0 || !SshCommandBuilder.IsValidAlias(alias)) return result;

        var list = string.Join(' ', sessions.Select(Quote));
        var script =
            $"for s in {list}; do " +
            "p=$(tmux display-message -p -t \"$s\" '#{pane_current_path}' 2>/dev/null); " +
            "root=$(git -C \"$p\" rev-parse --show-toplevel 2>/dev/null); " +
            "[ -z \"$root\" ] && [ -n \"$p\" ] && root=$(find \"$p\" -maxdepth 3 -name .git -printf '%T@ %h\\n' 2>/dev/null | sort -nr | head -1 | cut -d' ' -f2-); " +
            "b=$(git -C \"$root\" branch --show-current 2>/dev/null); " +
            "[ -z \"$b\" ] && b=$(git -C \"$root\" rev-parse --short HEAD 2>/dev/null); " +
            "printf '%s\\t%s\\t%s\\n' \"$s\" \"$b\" \"$p\"; " +
            "done";

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
        startInfo.ArgumentList.Add("ConnectTimeout=8");
        startInfo.ArgumentList.Add(alias);
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process == null) return result;

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        _ = await errorTask;

        if (process.ExitCode != 0) return result;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;

            var session = parts[0].Trim();
            var branch = parts[1].Trim();
            var path = parts[2].Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(branch))
            {
                var slash = path.LastIndexOf('/');
                branch = slash >= 0 && slash + 1 < path.Length
                    ? path[(slash + 1)..]
                    : path;
            }

            result[session] = string.IsNullOrWhiteSpace(branch) ? "—" : branch;
        }

        return result;
    }

    private static string Quote(string value) => $"'{value.Replace("'", "'\\''")}'";
}
