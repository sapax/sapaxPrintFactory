using sapaXPrintCore.Cloud;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Printing;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
    public static class CloudAgentPoller
    {
        private static bool _started;

        // How often the agent re-announces its printers to the cloud.
        // The cloud keeps this in memory, so it is lost whenever the cloud restarts.
        private static readonly TimeSpan RegisterInterval = TimeSpan.FromSeconds(15);

        public static void Start()
        {
            if (_started) return;
            _started = true;

            Task.Run(async () =>
            {
                var client = new CloudPrintClient(
                    cloudUrl: AppSettings.CloudUrl,
                    agentId: AppSettings.AgentId,
                    apiKey: AppSettings.AgentApiKey
                );

                // Lets PrintJobWorker report PDF results back to the cloud
                PrintJobWorker.CloudClient = client;

                DateTime lastRegister = DateTime.MinValue;

                while (true)
                {
                    CloudPrintJobDto? job = null;

                    try
                    {
                        // 1. Tell the cloud which printers we have
                        if (DateTime.UtcNow - lastRegister > RegisterInterval)
                        {
                            var ids = PrinterDiscoveryService
                                .GetPrinters(checkNetworkReachability: false)
                                .Select(p => p.PrinterId)
                                .ToList();

                            await client.RegisterAsync(ids);
                            lastRegister = DateTime.UtcNow;

                            Debug.WriteLine("[AGENT] Registered printers: " + string.Join(", ", ids));
                        }

                        // 2. Ask for the next job
                        job = await client.FetchNextJobAsync();

                        if (job != null)
                        {
                            Debug.WriteLine($"[AGENT] JOB RECEIVED {job.Id} for printer {job.PrinterId}");
                            await HandleJobAsync(client, job);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[AGENT ERROR] " + ex);

                        if (job != null)
                        {
                            try { await client.MarkCompleted(job.Id, false, ex.Message); }
                            catch (Exception ex2) { Debug.WriteLine("[AGENT] Could not report failure: " + ex2.Message); }
                        }
                    }

                    // Always wait before polling again (only skip the wait right after a job)
                    if (job == null)
                        await Task.Delay(2000);
                }
            });
        }

        // -------- ZPL: send RAW, then CONFIRM with the spooler before reporting Completed --------
        private static async Task HandleZplAsync(CloudPrintClient client, CloudPrintJobDto job)
        {
            // Show ZPL jobs in the Jobs tab, same as PDF jobs
            var local = new PrintJob
            {
                CloudJobId = job.Id,
                JobType = "ZPL",
                PrinterId = job.PrinterId,
                FileName = string.IsNullOrWhiteSpace(job.FileName) ? "label.zpl" : job.FileName!,
                Status = PrintJobStatus.Printing
            };
            PrintJobQueue.Track(local);

            ZplSendResult sent;
            try
            {
                // Resolves the PrinterId, refuses to send when the printer is offline / in error,
                // and returns the Windows spooler job id.
                sent = ZebraPrinterService.Send(job.PrinterId, job.Zpl!, local.FileName, local.JobId);
            }
            catch (Exception ex)
            {
                local.Status = PrintJobStatus.Failed;
                local.LastError = ex.Message;
                throw;   // the poll loop reports the failure to the cloud
            }

            local.PrinterName = sent.PrinterName ?? "";
            local.PrinterStatus = sent.PrinterStatus;
            local.DocumentName = sent.DocumentName;
            local.SpoolerJobId = sent.SpoolerJobId;

            // DryRun: nothing was sent, nothing to confirm
            if (sent.PrinterName == null)
            {
                local.Status = PrintJobStatus.Completed;
                await client.MarkCompleted(job.Id, true, null, null);
                return;
            }

            // Spooler accepted it. That is NOT "printed" yet, so confirm in the background
            // (does not block polling for the next job).
            local.Status = PrintJobStatus.Spooled;
            Debug.WriteLine($"[AGENT] ZPL spooled (job {sent.SpoolerJobId}), waiting for printer...");

            _ = Task.Run(() => ConfirmZplAsync(client, job.Id, local));
        }

        private static async Task ConfirmZplAsync(CloudPrintClient client, Guid cloudJobId, PrintJob local)
        {
            SpoolResult result;
            try
            {
                result = await SpoolerJobMonitor.ConfirmAsync(local.PrinterName, local.DocumentName, local.SpoolerJobId);
            }
            catch (Exception ex)
            {
                result = new SpoolResult { Success = false, Message = "Print status could not be verified: " + ex.Message };
            }

            local.SpoolerJobId = result.SpoolerJobId ?? local.SpoolerJobId;
            local.SpoolerStatus = result.SpoolerStatus;

            if (result.Success)
            {
                local.Status = PrintJobStatus.Completed;
            }
            else
            {
                local.Status = PrintJobStatus.Failed;
                local.LastError = result.Message;
            }

            Debug.WriteLine($"[AGENT] ZPL {local.Status}: {result.Message}");

            try
            {
                await client.MarkCompleted(
                    cloudJobId,
                    result.Success,
                    result.Success ? null : result.Message,
                    local.PrinterName,
                    local.PrinterStatus,
                    local.SpoolerJobId,
                    local.SpoolerStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AGENT] Could not report ZPL result: " + ex.Message);
            }
        }

        private static async Task HandleJobAsync(CloudPrintClient client, CloudPrintJobDto job)
        {
            // -------- ZPL --------
            if (!string.IsNullOrWhiteSpace(job.Zpl))
            {
                await HandleZplAsync(client, job);
                return;
            }

            // -------- PDF --------
            if (!string.IsNullOrWhiteSpace(job.FileBase64))
            {
                // Swagger / browsers often send "data:application/pdf;base64,XXXX".
                // Convert.FromBase64String can't decode the prefix, so remove it.
                string base64 = job.FileBase64.Trim();
                if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = base64.IndexOf(',');
                    if (comma >= 0)
                        base64 = base64.Substring(comma + 1);
                }

                Debug.WriteLine($"[AGENT] PDF job {job.Id}: {base64.Length} base64 chars -> printer {job.PrinterId}");

                // Queued for PrintJobWorker, which reports completion/failure to the cloud
                PrintJobProcessor.Process(new PrintRequest
                {
                    JobId = job.Id,
                    PrinterId = job.PrinterId,
                    FileName = job.FileName,
                    FileBase64 = base64
                });
                return;
            }

            throw new Exception("Job has neither Zpl nor FileBase64");
        }
    }
}