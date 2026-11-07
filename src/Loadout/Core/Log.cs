using System.Globalization;
using System.Text;

namespace Loadout.Core;

/// <summary>
/// Tiny local error log (SPEC section 10): errors only, never sent anywhere.
/// When the file passes 512 KB it is renamed to loadout.old.log (replacing any older one),
/// so the two files together stay under 1 MB. Never throws.
/// </summary>
public static class Log
{
    public const long MaxBytes = 512 * 1024;

    private static readonly Lock s_gate = new();

    public static void Error(string message, Exception? exception = null)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(message);
            if (exception is not null)
            {
                line.AppendLine().Append(exception);
            }

            line.AppendLine();

            lock (s_gate)
            {
                var path = AppPaths.LogFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = Path.Combine(info.DirectoryName!, "loadout.old.log");
                    File.Move(path, old, overwrite: true);
                }

                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
