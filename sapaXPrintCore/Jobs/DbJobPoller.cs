using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using sapaXPrintCore.Data;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Printing;

namespace sapaXPrintCore.Jobs
{
    /// <summary>
    /// Serverless agent loop:
    ///   anything (Manual / Power Automate / Power Apps / Web form ...) INSERTs a row into dbo.PrintJobs
    ///   -> this poller claims it every N seconds -> prints on the related printer -> writes the result back.
    /// Lives in Core, so both the WPF app and the Windows Service can use it.
    /// </summary>
    public static class DbJobPoller
    {
        public const int MinIntervalSeconds = 2;
        public const int MaxIntervalSeconds = 3600;

        // Printers + heartbeat are published this often (not on every poll, to keep DB traffic low)
        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);

        private static bool _started;
        private static PrintJobRepository? _repo;

        /// <summary>Last poll error (null = last poll was fine). Handy for a status label in the UI.</summary>
        public static string? LastError { get; private set; }

        /// <param name="connectionString">Returns the SQL connection string (null = DB not configured yet).</param>
        /// <param name="computerId">This computer's id (= agent id).</param>
        /// <param name="intervalSeconds">Read again on EVERY loop, so a change in the Administrator tab applies immediately.</param>
        /// <param name="printerProvider">Printers to publish. Master passes only the ones ticked Active. Null = all.</param>
        public static void Start(
            Func<string?> connectionString,
            Func<string> computerId,
            Func<int> intervalSeconds,
            Func<List<PrinterInfo>>? printerProvider = null)
        {
            if (_started) return;
            _started = true;

            var repo = new PrintJobRepository(connectionString);
            _repo = repo;

            // PDF results are reported by PrintJobWorker through this hook
            PrintJobWorker.ResultReporter = (job, ok, err) => repo.CompleteAsync(
                job.DbJobId, ok, err, job.PrinterName, job.PrinterStatus, job.SpoolerJobId, job.SpoolerStatus);

            printerProvider ??= () => PrinterDiscoveryService.GetPrinters(checkNetworkReachability: false);

            Task.Run(async () =>
            {
                DateTime lastSync = DateTime.MinValue;

                while (true)
                {
                    bool gotJob = false;

                    try
                    {
                        if (string.IsNullOrWhiteSpace(connectionString()))
                        {
                            LastError = "Database is not configured.";
                        }
                        else
                        {
                            string id = computerId();

                            if (DateTime.UtcNow - lastSync >= SyncInterval)
                            {
                                var printers = await Task.Run(printerProvider);
                                await repo.SyncPrintersAsync(id, printers);
                                lastSync = DateTime.UtcNow;
                            }

                            var job = await repo.ClaimNextAsync(id);
                            if (job != null)
                            {
                                gotJob = true;   // more jobs may be waiting: do not sleep before asking again
                                Debug.WriteLine($"[AGENT] JOB CLAIMED {job.JobId} ({job.JobType}) -> printer {job.PrinterId}");
                                await HandleJobAsync(repo, job);
                            }

                            LastError = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        Debug.WriteLine("[AGENT ERROR] " + ex.Message);
                        lastSync = DateTime.MinValue;   // re-sync printers after an error (DB may have been down)
                    }

                    if (!gotJob)
                    {
                        int s = Math.Clamp(intervalSeconds(), MinIntervalSeconds, MaxIntervalSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(s));
                    }
                }
            });
        }

        // ------------------------------------------------------------------
        private static async Task HandleJobAsync(PrintJobRepository repo, ClaimedJob job)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(job.Payload))
                    throw new Exception("Job has no Payload.");

                switch (job.JobType.Trim().ToUpperInvariant())
                {
                    case "ZPL":
                        HandleZpl(repo, job);
                        break;

                    case "PDF":
                        HandlePdf(job);
                        break;

                    default:
                        throw new Exception($"Unsupported JobType '{job.JobType}'. Use PDF or ZPL.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AGENT] JOB {job.JobId} FAILED: {ex.Message}");
                try { await repo.CompleteAsync(job.JobId, false, ex.Message); }
                catch (Exception ex2) { Debug.WriteLine("[AGENT] Could not report failure: " + ex2.Message); }
            }
        }

        // -------- PDF: queue for PrintJobWorker, which reports the result when the spooler confirms --------
        private static void HandlePdf(ClaimedJob job)
        {
            // Power Apps / browsers often send "data:application/pdf;base64,XXXX"
            string base64 = job.Payload!.Trim();
            if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = base64.IndexOf(',');
                if (comma >= 0) base64 = base64.Substring(comma + 1);
            }

            byte[] pdfBytes;
            try { pdfBytes = Convert.FromBase64String(base64); }
            catch { throw new Exception("Payload is not valid Base64 PDF data."); }

            PrintJobQueue.Enqueue(new PrintJob
            {
                DbJobId = job.JobId,
                JobType = "PDF",
                PrinterId = job.PrinterId,
                FileName = string.IsNullOrWhiteSpace(job.FileName) ? "document.pdf" : job.FileName!,
                PdfBytes = pdfBytes,
                Status = PrintJobStatus.Queued
            });
        }

        // -------- ZPL: send RAW, then CONFIRM with the spooler before reporting Completed --------
        private static void HandleZpl(PrintJobRepository repo, ClaimedJob job)
        {
            var local = new PrintJob
            {
                DbJobId = job.JobId,
                JobType = "ZPL",
                PrinterId = job.PrinterId,
                FileName = string.IsNullOrWhiteSpace(job.FileName) ? "label.zpl" : job.FileName!,
                Status = PrintJobStatus.Printing
            };
            PrintJobQueue.Track(local);   // shows in the Jobs tab

            ZplSendResult sent;
            try
            {
                sent = ZebraPrinterService.Send(job.PrinterId, job.Payload!, local.FileName, local.JobId);
            }
            catch (Exception ex)
            {
                local.Status = PrintJobStatus.Failed;
                local.LastError = ex.Message;
                throw;   // HandleJobAsync reports the failure to the DB
            }

            local.PrinterName = sent.PrinterName ?? "";
            local.PrinterStatus = sent.PrinterStatus;
            local.DocumentName = sent.DocumentName;
            local.SpoolerJobId = sent.SpoolerJobId;

            if (sent.PrinterName == null)   // DryRun: nothing was sent
            {
                local.Status = PrintJobStatus.Completed;
                _ = repo.CompleteAsync(job.JobId, true);
                return;
            }

            local.Status = PrintJobStatus.Spooled;   // accepted by the spooler, NOT printed yet
            _ = Task.Run(() => ConfirmZplAsync(repo, local));
        }

        private static async Task ConfirmZplAsync(PrintJobRepository repo, PrintJob local)
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
            local.Status = result.Success ? PrintJobStatus.Completed : PrintJobStatus.Failed;
            if (!result.Success) local.LastError = result.Message;

            try
            {
                await repo.CompleteAsync(
                    local.DbJobId, result.Success, result.Success ? null : result.Message,
                    local.PrinterName, local.PrinterStatus, local.SpoolerJobId, local.SpoolerStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AGENT] Could not report ZPL result: " + ex.Message);
            }
        }
    }
}