using System.IO;
using System.Text.RegularExpressions;

namespace StorageTerm.Services;

public static class SshCommandBuilder
{
    private static readonly Regex SafeAlias = new(@"^[A-Za-z0-9._@:\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeSession = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public static string SshPath
    {
        get
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system, "OpenSSH", "ssh.exe");
            return File.Exists(candidate) ? candidate : "ssh.exe";
        }
    }

    public static string ScpPath
    {
        get
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system, "OpenSSH", "scp.exe");
            return File.Exists(candidate) ? candidate : "scp.exe";
        }
    }

    public static bool IsValidAlias(string alias) =>
        !string.IsNullOrWhiteSpace(alias) && SafeAlias.IsMatch(alias.Trim());

    /// <summary>
    /// Monta a linha de comando do ssh usando o ~/.ssh/config. O alias é validado
    /// porque a linha é passada ao CreateProcess como texto. Se tmuxSession for
    /// informado, o ssh abre/reatar uma sessão tmux persistente no servidor.
    /// </summary>
    public static string Build(string alias, string? tmuxSession = null)
    {
        var clean = alias.Trim();
        if (!IsValidAlias(clean))
        {
            throw new ArgumentException($"Alias SSH inválido: '{alias}'. Use letras, números, ponto, hífen, dois-pontos ou @.", nameof(alias));
        }

        if (tmuxSession != null && !SafeSession.IsMatch(tmuxSession))
        {
            throw new ArgumentException($"Sessão tmux inválida: '{tmuxSession}'.", nameof(tmuxSession));
        }

        var tty = tmuxSession != null ? "-t " : string.Empty;
        var command = $"\"{SshPath}\" {tty}-o ConnectTimeout=8 -o ServerAliveInterval=30 " +
                      $"-o ServerAliveCountMax=3 -o TCPKeepAlive=yes {clean}";

        if (tmuxSession != null)
        {
            command += $" \"tmux start-server \\; set -s escape-time 500 \\; set -s extended-keys off \\; set -g focus-events off \\; set -g mouse on \\; set -g status off \\; set -g pane-border-style fg=#2A2A2A \\; set -g pane-active-border-style fg=#6E6E6E \\; new-session -A -s {tmuxSession} || exec bash -l\"";
        }

        return command;
    }

    public static string BuildDemo() => "cmd.exe /k";
}



