using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using sapaXPrintCore.Models;

namespace sapaXPrintCore.Jobs
{
    public static class PrintJobQueue
    {
        private static readonly ConcurrentQueue<PrintJob> _queue = new();
        private static readonly ConcurrentDictionary<Guid, PrintJob> _allJobs = new();
        private static readonly AutoResetEvent _signal = new(false);

        public static void Enqueue(PrintJob job)
        {
            _queue.Enqueue(job);
            _allJobs[job.JobId] = job;
            _signal.Set();
        }

        /// <summary>
        /// Shows a job in the UI list WITHOUT queueing it for the PDF worker
        /// (used for ZPL jobs, which are sent straight to the printer).
        /// </summary>
        public static void Track(PrintJob job)
        {
            _allJobs[job.JobId] = job;
        }

        public static PrintJob? Dequeue()
        {
            _queue.TryDequeue(out var job);
            return job;
        }

        public static void WaitForJob()
        {
            while (_queue.IsEmpty)
            {
                _signal.WaitOne();
            }
        }


        // THIS is what UI will bind to
        public static IReadOnlyCollection<PrintJob> GetAllJobs()
        {
            return _allJobs.Values
                .OrderByDescending(j => j.CreatedAt)
                .ToList();
        }

    }
}