using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using sapaXPrintCore.Models;

namespace sapaXPrintCore.Jobs
{
    public static class ZplJobQueue
    {
        private static readonly ConcurrentQueue<ZplPrintJob> _queue = new();
        private static readonly AutoResetEvent _signal = new(false);

        public static void Enqueue(ZplPrintJob job)
        {
            _queue.Enqueue(job);
            _signal.Set();
        }

        public static ZplPrintJob? Dequeue()
        {
            _queue.TryDequeue(out var job);
            return job;
        }

        public static void Wait() => _signal.WaitOne();
    }
}
