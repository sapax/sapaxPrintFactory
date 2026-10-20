using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using sapaXPrintCore.Models;
using System.Collections.Generic;
using System.Printing;
using System.Threading;

namespace sapaXPrintCore.Printing
{
    public sealed class SpoolResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int? SpoolerJobId { get; init; }
        public string? SpoolerStatus { get; init; }
    }

    /// <summary>
    /// Why this exists: PrintDocument.Print() / WritePrinter() return as soon as the job is
    /// ACCEPTED BY THE WINDOWS SPOOLER. If the printer is switched off the spooler happily
    /// accepts the job and keeps it in the queue ("The printer is offline"), so reporting
    /// "Completed" at that point is wrong. This class watches the real spooler job and only
    /// reports success when the job has actually been delivered to the printer.
    ///
    /// Used for BOTH PDF (bitmap) jobs and ZPL (RAW) jobs.
    /// </summary>
    public static class SpoolerJobMonitor
    {
        // Printer/job reports an error or offline state for this long -> give up, cancel the job, report Failed
        public static TimeSpan StuckTimeout { get; set; } = TimeSpan.FromSeconds(30);

        // Hard upper limit for a job that is still "Printing" without any error (very large PDFs)
        public static TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(10);

        // A job that never shows up in the queue within this time already left it (tiny ZPL labels)
        public static TimeSpan AppearGrace { get; set; } = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private const string Prefix = "sapaX-";
        private const int KeyLength = 6 + 36;   // "sapaX-" + Guid ("D" format)

        // Win32 JOB_STATUS_* bits (identical to the System.Printing.PrintJobStatus values)
        private const int JOB_PAUSED = 0x0001;
        private const int JOB_ERROR = 0x0002;
        private const int JOB_DELETING = 0x0004;
        private const int JOB_SPOOLING = 0x0008;
        private const int JOB_PRINTING = 0x0010;
        private const int JOB_OFFLINE = 0x0020;
        private const int JOB_PAPEROUT = 0x0040;
        private const int JOB_PRINTED = 0x0080;
        private const int JOB_DELETED = 0x0100;
        private const int JOB_BLOCKED = 0x0200;
        private const int JOB_USERINT = 0x0400;
        private const int JOB_RESTART = 0x0800;
        private const int JOB_COMPLETE = 0x1000;
        private const int JOB_RETAINED = 0x2000;

        private const int STUCK_MASK = JOB_PAUSED | JOB_ERROR | JOB_OFFLINE | JOB_PAPEROUT | JOB_BLOCKED | JOB_USERINT;

        // Printer states (from PrinterDiscoveryService) in which we must NOT send a job
        private static readonly HashSet<string> BlockingStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Offline", "Unavailable", "Paused", "Error", "Paper Jam", "Out Of Paper", "No Toner",
            "Door Open", "Out Of Memory", "User Action Required", "Stopped Printing",
            "Service Requested", "Output Bin Full", "Pending Deletion"
        };

        public static bool IsBlocking(string? printerStatus) =>
            !string.IsNullOrWhiteSpace(printerStatus) && BlockingStatuses.Contains(printerStatus);

        /// <summary>Unique, searchable spooler document name, e.g. "sapaX-&lt;guid&gt; label.zpl".</summary>
        public static string MakeDocumentName(Guid jobId, string? label)
        {
            string name = Prefix + jobId.ToString("D");
            if (!string.IsNullOrWhiteSpace(label))
                name += " " + label.Trim();
            return name.Length > 200 ? name.Substring(0, 200) : name;
        }

        private static string MatchKey(string documentName) =>
            documentName.Length >= KeyLength ? documentName.Substring(0, KeyLength) : documentName;

        public static Task<SpoolResult> ConfirmAsync(
            string printerName, string documentName, int? knownJobId = null, CancellationToken ct = default)
            => Task.Run(() => Confirm(printerName, documentName, knownJobId, ct), ct);

        /// <summary>
        /// Blocks (call from a worker thread) until the spooler job is delivered, failed or timed out.
        /// </summary>
        public static SpoolResult Confirm(
            string printerName, string documentName, int? knownJobId = null, CancellationToken ct = default)
        {
            string key = MatchKey(documentName);
            DateTime started = DateTime.UtcNow;
            DateTime? stuckSince = null;
            int? jobId = knownJobId;
            int lastBits = 0;
            bool seen = false;

            try
            {
                using var server = new PrintServer();
                using var queue = server.GetPrintQueue(printerName);

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var snap = FindJob(queue, key, knownJobId);
                    TimeSpan elapsed = DateTime.UtcNow - started;

                    if (snap != null)
                    {
                        seen = true;
                        jobId = snap.Value.Id;
                        lastBits = snap.Value.Bits;
                        string statusText = Describe(lastBits);

                        if ((lastBits & (JOB_DELETING | JOB_DELETED)) != 0)
                            return Fail($"Job was cancelled/deleted in the print queue ({statusText}).", jobId, statusText);

                        if ((lastBits & (JOB_PRINTED | JOB_COMPLETE)) != 0)
                            return Ok("Printed (spooler reports the job as printed).", jobId, statusText);

                        string? queueProblem = GetQueueProblem(queue);
                        bool stuck = (lastBits & STUCK_MASK) != 0 || queueProblem != null;

                        if (stuck)
                        {
                            stuckSince ??= DateTime.UtcNow;

                            if (DateTime.UtcNow - stuckSince.Value >= StuckTimeout)
                            {
                                string reason = queueProblem ?? statusText;
                                CancelJob(queue, jobId.Value);   // never let it print hours later by surprise
                                return Fail(
                                    $"Printer did not accept the job: {reason}. Check that the printer is connected and powered on. The job was removed from the print queue.",
                                    jobId, statusText);
                            }
                        }
                        else
                        {
                            stuckSince = null;
                        }

                        if (elapsed >= MaxWait)
                        {
                            CancelJob(queue, jobId.Value);
                            return Fail($"Job was not delivered within {MaxWait.TotalMinutes:0} minutes ({statusText}).", jobId, statusText);
                        }
                    }
                    else if (seen)
                    {
                        // The job left the queue.
                        string statusText = Describe(lastBits);

                        if ((lastBits & (STUCK_MASK | JOB_DELETING | JOB_DELETED)) != 0)
                            return Fail(
                                $"Job was removed from the print queue before it printed (last spooler status: {statusText}).",
                                jobId, statusText);

                        return Ok("Printed (job was delivered to the printer and left the queue).", jobId, statusText);
                    }
                    else if (elapsed >= AppearGrace)
                    {
                        // Never saw it: it was small and had already been delivered. A job for an OFFLINE
                        // printer can't do this, because it stays in the queue until the printer is back.
                        return Ok("Printed (job had already left the print queue).", jobId, null);
                    }

                    if (ct.WaitHandle.WaitOne(PollInterval))
                        ct.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                return Fail("Confirmation cancelled.", jobId, Describe(lastBits));
            }
            catch (Exception ex)
            {
                return Fail("Print status could not be verified: " + ex.Message, jobId, Describe(lastBits));
            }
        }

        // -------------------------------------------------------------------------------------

        private static (int Id, int Bits)? FindJob(PrintQueue queue, string key, int? knownId)
        {
            queue.Refresh();

            foreach (PrintSystemJobInfo j in queue.GetPrintJobInfoCollection())
            {
                try
                {
                    bool match =
                        (knownId.HasValue && j.JobIdentifier == knownId.Value) ||
                        (j.Name != null && j.Name.Contains(key, StringComparison.Ordinal));

                    if (match)
                        return (j.JobIdentifier, (int)j.JobStatus);
                }
                finally
                {
                    j.Dispose();
                }
            }

            return null;
        }

        private static string? GetQueueProblem(PrintQueue queue)
        {
            queue.Refresh();

            if (queue.IsOffline) return "Printer is offline";

            var s = queue.QueueStatus;
            if (s.HasFlag(PrintQueueStatus.Offline)) return "Printer is offline";
            if (s.HasFlag(PrintQueueStatus.PaperJam)) return "Paper jam";
            if (s.HasFlag(PrintQueueStatus.PaperOut)) return "Out of paper";
            if (s.HasFlag(PrintQueueStatus.DoorOpen)) return "Door open";
            if (s.HasFlag(PrintQueueStatus.NoToner)) return "No toner/ink";
            if (s.HasFlag(PrintQueueStatus.OutOfMemory)) return "Printer out of memory";
            if (s.HasFlag(PrintQueueStatus.UserIntervention)) return "Printer needs user action";
            if (s.HasFlag(PrintQueueStatus.Error)) return "Printer reports an error";
            if (s.HasFlag(PrintQueueStatus.Paused)) return "Print queue is paused";

            return null;
        }

        private static void CancelJob(PrintQueue queue, int jobId)
        {
            try
            {
                using var job = queue.GetJob(jobId);
                job.Cancel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not cancel spooler job {jobId}: {ex.Message}");
            }
        }

        private static string Describe(int bits)
        {
            if (bits == 0) return "Spooled";

            var parts = new List<string>();
            if ((bits & JOB_PAUSED) != 0) parts.Add("Paused");
            if ((bits & JOB_ERROR) != 0) parts.Add("Error");
            if ((bits & JOB_DELETING) != 0) parts.Add("Deleting");
            if ((bits & JOB_SPOOLING) != 0) parts.Add("Spooling");
            if ((bits & JOB_PRINTING) != 0) parts.Add("Printing");
            if ((bits & JOB_OFFLINE) != 0) parts.Add("Offline");
            if ((bits & JOB_PAPEROUT) != 0) parts.Add("Paper out");
            if ((bits & JOB_PRINTED) != 0) parts.Add("Printed");
            if ((bits & JOB_DELETED) != 0) parts.Add("Deleted");
            if ((bits & JOB_BLOCKED) != 0) parts.Add("Blocked");
            if ((bits & JOB_USERINT) != 0) parts.Add("User intervention");
            if ((bits & JOB_RESTART) != 0) parts.Add("Restarted");
            if ((bits & JOB_COMPLETE) != 0) parts.Add("Complete");
            if ((bits & JOB_RETAINED) != 0) parts.Add("Retained");

            return parts.Count == 0 ? "Spooled" : string.Join(", ", parts);
        }

        private static SpoolResult Ok(string message, int? id, string? status) =>
            new() { Success = true, Message = message, SpoolerJobId = id, SpoolerStatus = status };

        private static SpoolResult Fail(string message, int? id, string? status) =>
            new() { Success = false, Message = message, SpoolerJobId = id, SpoolerStatus = status };
    }
}