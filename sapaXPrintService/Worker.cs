using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Settings;

namespace sapaXPrintService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger) => _logger = logger;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("sapaXPrintService starting");

            LoadNativeLibraries();

            // Prints PDF jobs claimed from the database
            PrintJobWorker.Start();

            // Claims jobs from dbo.PrintJobs and writes the results back
            DbJobPoller.Start(
                connectionString: () => DbConfig.IsConfigured ? DbConfig.ConnectionString : null,
                computerId: () => AppSettings.AgentId,
                intervalSeconds: () => AppSettings.PollIntervalSeconds,
                printerProvider: () => PrinterDiscoveryService.GetPrinters(checkNetworkReachability: false));

            _logger.LogInformation("Polling the database every {Seconds} seconds", AppSettings.PollIntervalSeconds);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // service is stopping
            }

            _logger.LogInformation("sapaXPrintService stopping");
        }

        // Same native folder the desktop app used (PDFium), so PDF rendering works in the service
        private static void LoadNativeLibraries()
        {
            var nativeDir = Path.Combine(AppContext.BaseDirectory, "native", "x64");
            SetDllDirectory(nativeDir);

            Environment.SetEnvironmentVariable(
                "PATH",
                nativeDir + ";" + Environment.GetEnvironmentVariable("PATH"),
                EnvironmentVariableTarget.Process);
        }
    }
}