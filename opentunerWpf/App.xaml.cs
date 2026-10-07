using System;
using System.IO;
using System.Net;
using System.Windows;
using Serilog;

namespace OpenTuner.Wpf
{
    public partial class App : Application
    {
        public App()
        {
            // .NET Framework defaults to SystemDefault TLS, which on older Windows
            // builds may not enable TLS 1.2 that eshail.batc.org.uk requires.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }

            // Configure Serilog so the WPF app logs to the same logs folder as the
            // rest of the app (the Debug tab tails the newest log file).
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(Path.Combine(logDir, "ot_wpf_log_" + DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + ".txt"))
                    .CreateLogger();

                Log.Information("Starting Open Tuner (WPF)");

                Exit += (s, e) =>
                {
                    Log.Information("Bye!");
                    Log.CloseAndFlush();
                };
            }
            catch { }

            DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception, "Unhandled UI exception");
                MessageBox.Show("Open Tuner encountered a problem:\n\n" + e.Exception.Message,
                                "Open Tuner", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
        }
    }
}
