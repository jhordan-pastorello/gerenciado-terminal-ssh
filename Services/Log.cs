using System.IO;
using System.Text;

namespace StorageTerm.Services;

public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                var file = AppPaths.LogFile;
                RotateIfNeeded(file);

                var sb = new StringBuilder(256);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level).Append("] ")
                  .Append(message);

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex);
                }

                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void RotateIfNeeded(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length < MaxBytes) return;

            var old = Path.Combine(AppPaths.DataDirectory, "storage-term.old.log");
            File.Move(file, old, overwrite: true);
        }
        catch
        {
            // ignore
        }
    }
}

