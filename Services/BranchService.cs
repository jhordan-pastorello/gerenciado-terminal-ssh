using System.Diagnostics;
using System.Text;

namespace StorageTerm.Services;

/// <summary>
/// Descobre a branch git de cada sessão tmux. Para agentes Kiro, usa o
/// session-lock do processo para achar a sessão e o último caminho citado
/// nas mensagens; para os demais, usa o path atual do pane.
/// Uma única conexão SSH por alias atende todos os terminais.
/// </summary>
public static class BranchService
{
    public sealed record BranchInfo(string Label, bool IsGit);

    public static async Task<Dictionary<string, BranchInfo>> FetchAsync(
        string alias,
        IReadOnlyList<string> sessions,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, BranchInfo>(StringComparer.OrdinalIgnoreCase);
        if (sessions.Count == 0 || !SshCommandBuilder.IsValidAlias(alias)) return result;

        var sessionList = string.Join(", ", sessions.Select(s => $"\"{s.Replace("\"", string.Empty)}\""));

        var python = """
import json, os, re, subprocess, glob
sessions = [__SESSIONS__]

def sh(cmd):
    try:
        return subprocess.check_output(cmd, shell=True, text=True, stderr=subprocess.DEVNULL).strip()
    except Exception:
        return ''

def ancestors(pid):
    out = set()
    for _ in range(12):
        out.add(pid)
        try:
            with open(f'/proc/{pid}/stat') as f:
                parts = f.read().split()
            pid = int(parts[3])
        except Exception:
            break
        if pid <= 1:
            break
    return out

locks = []
for lf in glob.glob('/home/storage/.kiro/sessions/**/.lock', recursive=True):
    try:
        data = json.load(open(lf))
        p = int(data.get('pid', 0))
        if p:
            locks.append((p, os.path.dirname(lf)))
    except Exception:
        pass

def branch_for_repo(path):
    cand = path if os.path.isdir(path) else os.path.dirname(path)
    root = sh(f'git -C "{cand}" rev-parse --show-toplevel 2>/dev/null')
    if root:
        b = sh(f'git -C "{root}" branch --show-current 2>/dev/null')
        if not b:
            b = sh(f'git -C "{root}" rev-parse --short HEAD 2>/dev/null')
        return b, root
    return '', ''

for s in sessions:
    shell = sh(f"tmux display-message -p -t {s} '#{{pane_pid}}'")
    kiro = ''
    if shell:
        kids = sh(f'pgrep -P {shell}').split()
        kiro = kids[0] if kids else ''

    sdir = ''
    if kiro:
        kp = int(kiro)
        for p, d in locks:
            if kp in ancestors(p):
                sdir = d
                break

    branch = ''
    repo = ''
    cwd = ''
    if sdir:
        mf = os.path.join(sdir, 'messages.jsonl')
        if os.path.exists(mf):
            try:
                lines = open(mf, errors='replace').read().splitlines()[-400:]
            except Exception:
                lines = []
            paths = []
            for line in lines:
                for m in re.findall(r'/home/storage/[^\s"\\,;:]+', line):
                    paths.append(m)
            for p in reversed(paths):
                branch, repo = branch_for_repo(p)
                if repo:
                    break

    if not repo:
        cwd = sh(f"tmux display-message -p -t {s} '#{{pane_current_path}}'")
        if cwd:
            branch, repo = branch_for_repo(cwd)

    print(f'{s}\t{branch}\t{repo}\t{cwd}')
"""
        .Replace("__SESSIONS__", sessionList);

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(python));
        var remoteCommand = $"echo {base64} | base64 -d | python3";

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
        startInfo.ArgumentList.Add(remoteCommand);

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
            if (parts.Length < 2) continue;

            var session = parts[0].Trim();
            var branch = parts[1].Trim();
            var repo = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            var cwd = parts.Length > 3 ? parts[3].Trim() : string.Empty;

            var isGit = !string.IsNullOrWhiteSpace(branch);
            if (!isGit)
            {
                var folder = !string.IsNullOrWhiteSpace(repo) ? repo : cwd;
                folder = folder.TrimEnd('/');
                var slash = folder.LastIndexOf('/');
                branch = slash >= 0 && slash + 1 < folder.Length
                    ? folder[(slash + 1)..]
                    : folder;

                if (string.IsNullOrWhiteSpace(branch)) branch = "—";
            }

            result[session] = new BranchInfo(branch, isGit);
        }

        return result;
    }
}
