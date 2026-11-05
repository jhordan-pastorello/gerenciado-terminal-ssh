using System.IO;
using Microsoft.Win32;

namespace StorageTerm.Services;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StorageTerm";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                var value = key?.GetValue(ValueName) as string;
                if (string.IsNullOrWhiteSpace(value)) return false;

                var exe = Environment.ProcessPath;
                return !string.IsNullOrWhiteSpace(exe) &&
                       value.Contains(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler a inicialização automática.", ex);
                return false;
            }
        }
    }

    public static bool Set(bool enabled, string? executablePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null)
            {
                Log.Warn("Não foi possível abrir a chave Run do usuário.");
                return false;
            }

            if (enabled)
            {
                var exe = executablePath ?? Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    Log.Warn($"Executável não encontrado para iniciar com o Windows: {exe}");
                    return false;
                }

                key.SetValue(ValueName, $"\"{exe}\" --tray", RegistryValueKind.String);
                Log.Info($"Inicialização automática ativada: {exe} --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Inicialização automática desativada.");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao alterar a inicialização automática.", ex);
            return false;
        }
    }
}

