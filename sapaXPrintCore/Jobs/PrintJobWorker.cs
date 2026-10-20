using sapaXPrintCore.Cloud;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Printing;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace sapaXPrintCore.Jobs
{
    public static class PrintJobWorker
    {
        private static bool _started;

        /// <summary>
        /// Set by the Master's CloudAgentPoller. Used to report job results to the cloud
        /// (correct URL, agent id and API key).
        /// </summary>
        public static CloudPrintClient? CloudClient { get; set; }

        public static void Start()
        {
            if (_started) return;
            _started = true;

            new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "PrintJobWorker"
            }.Start();
        }

        private static void WorkerLoop()
        {
            Debug.WriteLine("PRINT JOB WORKER STARTED");

            while (true)
            {
                // Wait until at least ONE job exists
                PrintJobQueue.WaitForJob();

                // Drain queue (important)
                while (true)
                {
                    var job = PrintJobQueue.Dequeue();
                    if (job == null)
                        break;

                    try
                    {
                        job.Status = PrintJobStatus.Printing;
                        Debug.WriteLine($"PRINTING JOB: {job.JobId} -> {job.PrinterName}");

                        // 1. Is the printer able to print RIGHT NOW? (fresh status, not the one from when the job arrived)
                        var printer = PrinterDiscoveryService.GetPrinterById(job.PrinterId)
                                      ?? throw new Exception($"Printer not found for ID {job.PrinterId}");

                        job.PrinterName = printer.PrinterName;
                        job.PrinterStatus = printer.Status;

                        if (SpoolerJobMonitor.IsBlocking(printer.Status))
                            throw new Exception($"Printer '{printer.PrinterName}' is {printer.Status}. Job was not sent.");

                        // 2. Render + hand to the Windows spooler under a unique, searchable name
                        job.DocumentName = SpoolerJobMonitor.MakeDocumentName(job.JobId, job.FileName);

                        var pages = PdfBitmapRenderer.Render(job.PdfBytes, 300);
                        BitmapPrintService.PrintBitmaps(pages, job.PrinterName, job.DocumentName);

                        // 3. ACCEPTED BY THE SPOOLER is NOT "printed". Confirm in the background so this
                        //    worker can already start the next job.
                        job.Status = PrintJobStatus.Spooled;
                        Debug.WriteLine($"JOB SPOOLED (waiting for printer): {job.JobId}");

                        var spooledJob = job;
                        _ = Task.Run(() => ConfirmAndReport(spooledJob));
                    }
                    catch (Exception ex)
                    {
                        job.Status = PrintJobStatus.Failed;
                        job.LastError = ex.Message;

                        Debug.WriteLine($"JOB FAILED: {job.JobId}");
                        Debug.WriteLine(ex);

                        NotifyCloudResult(job, false, ex.Message);
                    }
                }
            }
        }

        private static void ConfirmAndReport(PrintJob job)
        {
            try
            {
                var result = SpoolerJobMonitor.Confirm(job.PrinterName, job.DocumentName);

                job.SpoolerJobId = result.SpoolerJobId;
                job.SpoolerStatus = result.SpoolerStatus;

                if (result.Success)
                {
                    job.Status = PrintJobStatus.Completed;
                    Debug.WriteLine($"JOB COMPLETED (confirmed): {job.JobId} - {result.Message}");
                    NotifyCloudResult(job, true, null);
                }
                else
                {
                    job.Status = PrintJobStatus.Failed;
                    job.LastError = result.Message;
                    Debug.WriteLine($"JOB FAILED (not delivered): {job.JobId} - {result.Message}");
                    NotifyCloudResult(job, false, result.Message);
                }
            }
            catch (Exception ex)
            {
                job.Status = PrintJobStatus.Failed;
                job.LastError = ex.Message;
                NotifyCloudResult(job, false, ex.Message);
            }
        }

        // Runs on a background thread (not the UI thread), so blocking here is safe.
        private static void NotifyCloudResult(PrintJob job, bool success, string? error)
        {
            try
            {
                CloudClient?.MarkCompleted(
                    job.CloudJobId,
                    success,
                    error,
                    job.PrinterName,
                    job.PrinterStatus,
                    job.SpoolerJobId,
                    job.SpoolerStatus).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Never crash the agent if the cloud is unavailable
                Debug.WriteLine("Could not notify cloud: " + ex.Message);
            }
        }
    }
}