using System.IO;
using System.Text.RegularExpressions;

namespace StorageTerm.Services;

public static class SshCommandBuilder
{
    private static readonly Regex SafeAlias = new(@"^[A-Za-z0-9._@:\-]+$", RegexOptions.Compiled);

    public static string SshPath
    {
        get
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system, "OpenSSH", "ssh.exe");
            return File.Exists(candidate) ? candidate : "ssh.exe";
        }
    }

    public static bool IsValidAlias(string alias) =>
        !string.IsNullOrWhiteSpace(alias) && SafeAlias.IsMatch(alias.Trim());

    /// <summary>
    /// Monta a linha de comando do ssh usando o ~/.ssh/config. O alias é validado
    /// porque a linha é passada ao CreateProcess como texto.
    /// </summary>
    public static string Build(string alias)
    {
        var clean = alias.Trim();
        if (!IsValidAlias(clean))
        {
            throw new ArgumentException($"Alias SSH inválido: '{alias}'. Use letras, números, ponto, hífen, dois-pontos ou @.", nameof(alias));
        }

        return $"\"{SshPath}\" -o ConnectTimeout=8 -o ServerAliveInterval=30 " +
               $"-o ServerAliveCountMax=3 -o TCPKeepAlive=yes {clean}";
    }

    public static string BuildDemo() => "cmd.exe /k";
}

