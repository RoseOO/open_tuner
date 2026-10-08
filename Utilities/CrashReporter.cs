using System;
using System.IO;
using System.Text;
using Serilog;

namespace opentuner.Utilities
{
    /// <summary>
    /// Writes a plain-text crash report next to the logs so a user can send it in.
    /// Deliberately dependency-free (no dump generation) so it is safe to call
    /// from an unhandled exception handler.
    /// </summary>
    public static class CrashReporter
    {
        public static string CrashDirectory
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>Writes a crash report and returns the file path (or null on failure).</summary>
        public static string Report(Exception ex, string context = null)
        {
            try
            {
                string path = Path.Combine(CrashDirectory, "crash_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");

                var sb = new StringBuilder();
                sb.AppendLine("Open Tuner crash report");
                sb.AppendLine("=======================");
                sb.AppendLine("Time    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Version : " + (System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "?"));
                sb.AppendLine("OS      : " + Environment.OSVersion);
                sb.AppendLine(".NET    : " + Environment.Version);
                sb.AppendLine("Context : " + (context ?? "(none)"));
                sb.AppendLine();
                sb.AppendLine("Exception:");
                sb.AppendLine(ex?.ToString() ?? "(null)");

                if (ex?.InnerException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("Inner exception:");
                    sb.AppendLine(ex.InnerException.ToString());
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

                try { Log.Error(ex, "Crash report written to " + path + " (context: " + context + ")"); } catch { }

                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
