using System.IO;
using System.Text.RegularExpressions;

namespace StorageTerm.Services;

public sealed record SshProfile(
    string Alias,
    string HostName,
    string User,
    int Port,
    string? IdentityFile,
    bool Found)
{
    public string UserAtAlias => $"{User}@{Alias}";
    public string Endpoint => $"{HostName}:{Port}";
    public string IdentityName => string.IsNullOrWhiteSpace(IdentityFile) ? "chave padrão" : Path.GetFileName(IdentityFile);
    public string Summary => $"{User}@{HostName}:{Port}";
}

/// <summary>
/// Lê o ~/.ssh/config apenas para exibir a identificação correta (usuário, host e chave).
/// A conexão continua sendo feita pelo ssh com o alias, preservando todo o comportamento do OpenSSH.
/// </summary>
public static class SshConfigReader
{
    public static SshProfile Resolve(string alias, string? configPath = null)
    {
        var path = configPath ?? AppPaths.SshConfigFile;
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var found = false;
        var inMatch = false;
        var inDefault = false;

        try
        {
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;

                    var separator = line.IndexOfAny(new[] { ' ', '\t', '=' });
                    string key;
                    string value;

                    if (separator < 0)
                    {
                        key = line;
                        value = string.Empty;
                    }
                    else
                    {
                        key = line[..separator];
                        value = line[(separator + 1)..].TrimStart(' ', '\t', '=').Trim().Trim('"');
                    }

                    if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        var patterns = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        inMatch = patterns.Any(p => Matches(p, alias));
                        inDefault = patterns.Any(p => p == "*");
                        if (inMatch) found = true;
                        continue;
                    }

                    if (inMatch)
                    {
                        matched[key] = value;
                    }
                    else if (inDefault)
                    {
                        defaults[key] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Falha ao ler {path}: {ex.Message}");
        }

        string Get(string key, string fallback)
        {
            if (matched.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
            if (defaults.TryGetValue(key, out var defaultValue) && !string.IsNullOrWhiteSpace(defaultValue)) return defaultValue;
            return fallback;
        }

        var hostName = Get("HostName", alias);
        var user = Get("User", Environment.UserName);
        var port = int.TryParse(Get("Port", "22"), out var parsedPort) ? parsedPort : 22;

        var identity = Get("IdentityFile", string.Empty);
        if (identity.StartsWith("~/", StringComparison.Ordinal) || identity.StartsWith("~\\", StringComparison.Ordinal))
        {
            identity = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), identity[2..]);
        }

        return new SshProfile(
            alias,
            hostName,
            user,
            port,
            string.IsNullOrWhiteSpace(identity) ? null : identity,
            found);
    }

    private static bool Matches(string pattern, string alias)
    {
        if (pattern == "*") return true;
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return pattern.Equals(alias, StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(alias, regex, RegexOptions.IgnoreCase);
    }
}
