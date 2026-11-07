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

    /// <summary>Set this environment variable to 1 to also log <see cref="Trace"/> messages (for testing only).</summary>
    public const string TraceVariable = "LOADOUT_TRACE";

    private static readonly Lock s_gate = new();
    private static readonly bool s_trace = Environment.GetEnvironmentVariable(TraceVariable) == "1";

    /// <summary>True when <see cref="Trace"/> writes anything. Check it before building expensive trace messages.</summary>
    public static bool IsTraceEnabled => s_trace;

    /// <summary>
    /// Diagnostic message, written only when LOADOUT_TRACE=1. Used by reviewers to see what the app did.
    /// Never logs typed keys, only Loadout's own events.
    /// </summary>
    public static void Trace(string message)
    {
        if (s_trace)
        {
            Error("TRACE " + message);
        }
    }

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
