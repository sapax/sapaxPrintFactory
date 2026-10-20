using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using sapaXPrintCore.Jobs;

namespace sapaXPrintService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("sapaXPrintService started");

            // Start CORE print worker (headless)
            PrintJobWorker.Start();

            // Cloud polling for service is NOT here
            // It must be implemented later as a CORE poller
            // (not the WPF CloudAgentPoller)

            return Task.CompletedTask;
        }
    }
}
